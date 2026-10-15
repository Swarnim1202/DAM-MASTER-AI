using System.Net;
using System.Net.Mail;
using DamMaster.Api.Common;
using DamMaster.Api.DAL;
using DamMaster.Api.Models;

namespace DamMaster.Api.Engine;

/// <summary>
/// Builds notifications for alerts (REF-001 §10.2): who is told depends on the alert level
/// (ref_alert_level.notify_roles) and the dam's EAP contact list. Unacknowledged alerts escalate
/// step by step to the next EAP contacts and to management roles.
/// </summary>
public class Cls_NotificationEngine
{
    private static readonly string[] EscalationRoles = { Roles.Approver, Roles.Management, Roles.Admin };
    private readonly Cls_NotificationDAL _n = new();

    public async Task OnAlertAsync(string alertId, string reason)
    {
        var a = await new Cls_AlertDAL().GetAsync(alertId);
        if (a == null || a.Level < 1) return;
        var lv = await LevelAsync(a.Level);
        await _n.SetAckDueAsync(a.Id, DateTime.UtcNow.AddMinutes(Math.Max(1, lv.AckMinutes)), 0);
        var rows = new List<NotificationRow>();
        var users = await new Cls_UserDAL().ActiveByRolesAsync((lv.NotifyRoles ?? "").Split(','));
        AddUsers(rows, a, users, 0, reason);
        if (a.Level >= 2)
            foreach (var e in (await new Cls_EapDAL().ListAsync(a.DamId, true)).Where(e => e.MinLevel <= a.Level && e.EscalationOrder == 1))
                AddContact(rows, a, e, 0, reason);
        await _n.InsertManyAsync(rows);
    }

    /// <summary>Escalates every open alert whose acknowledgement time has passed. Returns the number escalated.</summary>
    public async Task<int> EscalateDueAsync()
    {
        var due = await _n.OverdueAlertsAsync();
        foreach (var a in due)
        {
            var step = a.EscalationStep + 1;
            var lv = await LevelAsync(a.Level);
            if (step > AppConfig.MaxEscalationSteps) { await _n.SetAckDueAsync(a.Id, null, a.EscalationStep); continue; }
            var rows = new List<NotificationRow>();
            var reason = $"Not acknowledged within {lv.AckMinutes} min — escalation step {step}";
            foreach (var e in (await new Cls_EapDAL().ListAsync(a.DamId, true)).Where(e => e.MinLevel <= a.Level && e.EscalationOrder == step + 1))
                AddContact(rows, a, e, step, reason);
            if (step == 1) AddUsers(rows, a, await new Cls_UserDAL().ActiveByRolesAsync(EscalationRoles), step, reason);
            await _n.InsertManyAsync(rows);
            await _n.SetAckDueAsync(a.Id, DateTime.UtcNow.AddMinutes(Math.Max(1, lv.AckMinutes)), step);
            await new Cls_AuditDAL().WriteAsync(null, "system", "Escalation", $"Alert escalated to step {step}: {a.Title} ({rows.Count} notification(s))", "alert", a.Id);
        }
        return due.Count;
    }

    private static async Task<RefAlertLevel> LevelAsync(int level)
    {
        var all = (IEnumerable<RefAlertLevel>)await new Cls_ReferenceDAL().GetCatalogAsync("alert-levels");
        return all.FirstOrDefault(l => l.Level == level) ?? new RefAlertLevel { Level = level, AckMinutes = 60, NotifyRoles = Roles.DamSafetyEngineer };
    }

    private static string Subject(AlertView a) => $"[DAM MASTER] Level {a.Level} {(a.Level == 3 ? "EMERGENCY" : a.Level == 2 ? "ALARM" : "ALERT")} — {a.DamName}: {a.Title}";
    private static string Body(AlertView a, string reason) =>
        $"{a.Title}\n{a.Detail}\nDam: {a.DamName}. Rule: {a.RuleType}. Observed: {a.LastAt:yyyy-MM-dd HH:mm} UTC.\n{reason}.\n" +
        (string.IsNullOrEmpty(AppConfig.AppBaseUrl) ? "" : $"Open: {AppConfig.AppBaseUrl}/alerts\n") + "Acknowledge in DAM MASTER to stop escalation.";
    private static string Sms(AlertView a) => $"DAM MASTER L{a.Level} {a.DamName}: {a.Title}".Length > 300 ? $"DAM MASTER L{a.Level} {a.DamName}: {a.Title}"[..300] : $"DAM MASTER L{a.Level} {a.DamName}: {a.Title}";

    private static void AddUsers(List<NotificationRow> rows, AlertView a, IEnumerable<AppUser> users, int step, string reason)
    {
        foreach (var u in users)
        {
            rows.Add(new NotificationRow { AlertId = a.Id, DamId = a.DamId, Level = a.Level, Channel = "InApp", Recipient = u.Id, RecipientName = u.FullName, Subject = Subject(a), Body = Body(a, reason), EscalationStep = step, Status = "Delivered" });
            if (!string.IsNullOrWhiteSpace(u.Email)) rows.Add(new NotificationRow { AlertId = a.Id, DamId = a.DamId, Level = a.Level, Channel = "Email", Recipient = u.Email!, RecipientName = u.FullName, Subject = Subject(a), Body = Body(a, reason), EscalationStep = step, Status = "Queued" });
            if (a.Level >= 2 && !string.IsNullOrWhiteSpace(u.Phone)) rows.Add(new NotificationRow { AlertId = a.Id, DamId = a.DamId, Level = a.Level, Channel = "SMS", Recipient = u.Phone!, RecipientName = u.FullName, Subject = Subject(a), Body = Sms(a), EscalationStep = step, Status = "Queued" });
        }
    }

    private static void AddContact(List<NotificationRow> rows, AlertView a, EapContact e, int step, string reason)
    {
        var who = $"{e.Name}{(string.IsNullOrWhiteSpace(e.EapRole) ? "" : " (" + e.EapRole + ")")}";
        if (!string.IsNullOrWhiteSpace(e.Email)) rows.Add(new NotificationRow { AlertId = a.Id, DamId = a.DamId, Level = a.Level, Channel = "Email", Recipient = e.Email!, RecipientName = who, Subject = Subject(a), Body = Body(a, reason), EscalationStep = step, Status = "Queued" });
        if (!string.IsNullOrWhiteSpace(e.Phone)) rows.Add(new NotificationRow { AlertId = a.Id, DamId = a.DamId, Level = a.Level, Channel = "SMS", Recipient = e.Phone!, RecipientName = who, Subject = Subject(a), Body = Sms(a), EscalationStep = step, Status = "Queued" });
    }
}

/// <summary>Sends queued e-mail / SMS. Without SMTP or SMS gateway configuration, messages are recorded as Simulated.</summary>
public class Cls_NotificationDispatcher
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<int> DispatchAsync()
    {
        var dal = new Cls_NotificationDAL();
        var queued = await dal.QueuedAsync(200);
        foreach (var n in queued)
        {
            try
            {
                if (n.Channel == "Email")
                {
                    if (string.IsNullOrWhiteSpace(AppConfig.SmtpHost)) { await dal.MarkAsync(n.Id, "Simulated", "SMTP not configured"); continue; }
                    using var smtp = new SmtpClient(AppConfig.SmtpHost, AppConfig.SmtpPort) { EnableSsl = AppConfig.SmtpSsl };
                    if (!string.IsNullOrWhiteSpace(AppConfig.SmtpUser)) smtp.Credentials = new NetworkCredential(AppConfig.SmtpUser, AppConfig.SmtpPassword);
                    using var msg = new MailMessage(AppConfig.SmtpFrom, n.Recipient, n.Subject, n.Body);
                    await smtp.SendMailAsync(msg);
                    await dal.MarkAsync(n.Id, "Sent", null);
                }
                else if (n.Channel == "SMS")
                {
                    if (string.IsNullOrWhiteSpace(AppConfig.SmsUrlTemplate)) { await dal.MarkAsync(n.Id, "Simulated", "SMS gateway not configured"); continue; }
                    var url = AppConfig.SmsUrlTemplate.Replace("{to}", Uri.EscapeDataString(n.Recipient)).Replace("{message}", Uri.EscapeDataString(n.Body));
                    var res = await Http.GetAsync(url);
                    await dal.MarkAsync(n.Id, res.IsSuccessStatusCode ? "Sent" : "Queued", res.IsSuccessStatusCode ? null : $"HTTP {(int)res.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                await dal.MarkAsync(n.Id, n.Attempts >= 2 ? "Failed" : "Queued", ex.Message.Length > 450 ? ex.Message[..450] : ex.Message);
            }
        }
        return queued.Count;
    }
}

/// <summary>Background job: escalates unacknowledged alerts and dispatches queued messages every N seconds.</summary>
public class AlertEscalationService : BackgroundService
{
    private readonly ILogger<AlertEscalationService> _log;
    public AlertEscalationService(ILogger<AlertEscalationService> log) => _log = log;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(10, AppConfig.EscalationIntervalSeconds)));
        while (await timer.WaitForNextTickAsync(stop))
        {
            try { await Cls_JobRunner.RunAsync(false); }
            catch (Exception ex) { _log.LogError(ex, "Escalation / dispatch cycle failed"); }
        }
    }
}

/// <summary>Inspection rules: every Level 2+ instrument alert gets a special inspection task; serious findings raise alerts.</summary>
public class Cls_InspectionEngine
{
    public static readonly string[] Types = { "Routine", "Pre-monsoon", "Post-monsoon", "Special", "Post-earthquake", "Post-flood" };
    public static readonly string[] Severities = { "Low", "Medium", "High", "Critical" };
    public static readonly string[] Conditions = { "Good", "Fair", "Poor", "Unsatisfactory" };
    public static readonly string[] CheckResults = { "OK", "Attention", "Not applicable" };

    public static int DueDays(string severity) => severity switch { "Critical" => 1, "High" => 7, "Medium" => 30, _ => 90 };

    public async Task EnsureSpecialInspectionAsync(AlertView a)
    {
        if (a.Level < 2 || a.RuleType is "System" or "Calibration" or "Inspection") return;
        var dal = new Cls_InspectionDAL();
        var open = await dal.OpenSpecialAsync(a.DamId);
        if (open == null)
        {
            open = new Inspection { Id = Ids.New(), DamId = a.DamId, InspectionType = "Special", ScheduledDate = DateTime.UtcNow.Date, TriggerAlertId = a.Id, CreatedBy = "System (alert engine)" };
            await dal.InsertAsync(open);
            await new Cls_AuditDAL().WriteAsync(null, "system", "Inspections", $"Special inspection created for Level {a.Level} alert: {a.Title}", "inspection", open.Id);
        }
        await new Cls_NotificationDAL().LinkInspectionAsync(a.Id, open.Id);
    }

    /// <summary>High finding → Level 2 alert; Critical finding → Level 3 alert (with notifications).</summary>
    public async Task<string?> AlertForFindingAsync(Finding f, string damName)
    {
        if (f.Severity is not ("High" or "Critical")) return null;
        var level = f.Severity == "Critical" ? 3 : 2;
        var a = new AlertView
        {
            Id = Ids.New(), DamId = f.DamId, SensorId = null, PfmaId = null, Level = level, RuleType = "Inspection",
            Title = $"{f.Severity} inspection finding — {f.Component}: {Trim(f.Description, 120)}",
            Detail = $"Location: {f.Location}. Recommendation: {f.Recommendation}. Reported by {f.CreatedBy}.", FirstAt = DateTime.UtcNow, LastAt = DateTime.UtcNow, Status = "Open"
        };
        await new Cls_AlertDAL().InsertAsync(a);
        await new Cls_NotificationDAL().LinkInspectionAsync(a.Id, f.InspectionId);
        await new Cls_NotificationEngine().OnAlertAsync(a.Id, "Raised from an inspection finding");
        return a.Id;
    }

    private static string Trim(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
