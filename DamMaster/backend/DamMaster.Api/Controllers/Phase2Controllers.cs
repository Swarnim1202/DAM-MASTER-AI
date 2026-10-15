using DamMaster.Api.Common;
using DamMaster.Api.DAL;
using DamMaster.Api.Engine;
using DamMaster.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DamMaster.Api.Controllers;

public record CreateInspectionRequest(string DamId, string InspectionType, DateTime ScheduledDate, string? Inspector);
public record CreateFindingRequest(string Component, string? Location, string Description, string Severity, string? SensorId, string? Recommendation, DateTime? DueDate);
public record MarkReadRequest(long? Id);

// =====================================================================
[Authorize, Route("api/inspections")]
public class InspectionsController : AppController
{
    [HttpGet]
    public async Task<IActionResult> List(string? damId, string? status, int take = 200) =>
        Ok(await new Cls_InspectionDAL().ListAsync(damId, status == "All" ? null : status, Math.Clamp(take, 1, 1000)));

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var dal = new Cls_InspectionDAL();
        var i = await dal.GetAsync(id);
        if (i == null) return NotFound();
        var dam = await new Cls_DamDAL().GetAsync(i.DamId);
        var type = (await new Cls_ReferenceDAL().DamTypesAsync()).FirstOrDefault(t => t.Code == dam?.DamTypeCode);
        var checklist = await dal.ChecklistAsync(id, type?.MaterialGroup ?? "Concrete");
        var findings = await dal.FindingsAsync(id, null, false);
        return Ok(new { inspection = i, dam, checklist, findings });
    }

    [Authorize(Policy = Policies.ManageInspections), HttpPost]
    public async Task<IActionResult> Create(CreateInspectionRequest req)
    {
        if (!Cls_InspectionEngine.Types.Contains(req.InspectionType)) throw new BusinessException("Inspection type must be one of: " + string.Join(", ", Cls_InspectionEngine.Types) + ".");
        if (await new Cls_DamDAL().GetAsync(req.DamId) == null) throw new BusinessException("Dam not found.");
        var i = new Inspection { Id = Ids.New(), DamId = req.DamId, InspectionType = req.InspectionType, ScheduledDate = req.ScheduledDate.Date, Inspector = string.IsNullOrWhiteSpace(req.Inspector) ? null : req.Inspector.Trim(), CreatedBy = User.FullName() };
        await new Cls_InspectionDAL().InsertAsync(i);
        await Audit("Inspections", $"Planned {i.InspectionType} inspection for {i.ScheduledDate:yyyy-MM-dd}", "inspection", i.Id);
        return Ok(i);
    }

    [Authorize(Policy = Policies.ManageInspections), HttpPost("{id}/start")]
    public async Task<IActionResult> Start(string id)
    {
        await new Cls_InspectionService().StartAsync(id, User.FullName());
        return NoContent();
    }


    [Authorize(Policy = Policies.ManageInspections), HttpPut("{id}/checks")]
    public async Task<IActionResult> Checks(string id, List<CheckInput> checks)
    {
        await new Cls_InspectionService().SaveChecksAsync(id, checks, User.FullName());
        return NoContent();
    }


    [Authorize(Policy = Policies.ManageInspections), HttpPost("{id}/findings")]
    public async Task<IActionResult> AddFinding(string id, CreateFindingRequest req) => Ok(await new Cls_InspectionService().AddFindingAsync(id, req, User.FullName()));


    [Authorize(Policy = Policies.ManageInspections), HttpPost("{id}/complete")]
    public async Task<IActionResult> Complete(string id, CompleteInspectionRequest req)
    {
        await new Cls_InspectionService().CompleteAsync(id, req, User.FullName());
        return NoContent();
    }


    [Authorize(Policy = Policies.ManageInspections), HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(string id)
    {
        if (await new Cls_InspectionDAL().CancelAsync(id) == 0) throw new BusinessException("Only planned or in-progress inspections can be cancelled.");
        await Audit("Inspections", "Inspection cancelled", "inspection", id);
        return NoContent();
    }

    [HttpGet("findings")]
    public async Task<IActionResult> Findings(string? damId, bool open = true) => Ok(await new Cls_InspectionDAL().FindingsAsync(null, damId, open));

    [Authorize(Policy = Policies.ManageInspections), HttpPost("findings/{id}/status")]
    public async Task<IActionResult> FindingStatus(string id, FindingStatusRequest req)
    {
        var allowed = new[] { "Assigned", "In progress", "Closed" };
        if (!allowed.Contains(req.Status)) throw new BusinessException("Status must be Assigned, In progress or Closed.");
        if (req.Status == "Closed")
        {
            if (!Policies.Map[Policies.CloseFindings].Contains(User.Role())) return StatusCode(403, new { error = "Only a Dam Safety Engineer can close findings." });
            if (string.IsNullOrWhiteSpace(req.Note)) throw new BusinessException("Record what was done to close the finding.");
        }
        if (await new Cls_InspectionDAL().SetFindingStatusAsync(id, req.Status, User.FullName(), req.Note?.Trim()) == 0) throw new BusinessException("Finding not found or already closed.");
        await Audit("Inspections", $"Finding {req.Status}" + (string.IsNullOrWhiteSpace(req.Note) ? "" : ": " + req.Note), "finding", id);
        return NoContent();
    }

    /// <summary>Dam Safety Act annual inspections: pre- and post-monsoon status per dam for a year.</summary>
    [HttpGet("compliance")]
    public async Task<IActionResult> Compliance(int? year)
    {
        var y = year ?? DateTime.UtcNow.Year; var today = DateTime.UtcNow.Date; var dal = new Cls_InspectionDAL();
        DateTime Due(string mmdd) => DateTime.ParseExact($"{y}-{mmdd}", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        string Status(DateTime? done, DateTime due) => done.HasValue ? (done.Value.Date <= due ? "Done on time" : "Done late") : today > due ? "Overdue" : "Due";
        var list = new List<MonsoonCompliance>();
        foreach (var d in await new Cls_DamDAL().ListAsync())
        {
            var (pre, post, open, overdue) = await dal.MonsoonAsync(d.Id, y);
            var pd = Due(AppConfig.PreMonsoonDue); var qd = Due(AppConfig.PostMonsoonDue);
            list.Add(new MonsoonCompliance { DamId = d.Id, DamName = d.Name, Year = y, PreDue = pd, PreDone = pre, PreStatus = Status(pre, pd), PostDue = qd, PostDone = post, PostStatus = Status(post, qd), OpenFindings = open, OverdueFindings = overdue });
        }
        return Ok(list);
    }
}

// =====================================================================
[Authorize, Route("api/dams/{damId}/eap-contacts")]
public class EapContactsController : AppController
{
    [HttpGet] public async Task<IActionResult> List(string damId) => Ok(await new Cls_EapDAL().ListAsync(damId));

    [Authorize(Policy = Policies.ManageEap), HttpPost]
    public async Task<IActionResult> Create(string damId, EapContact e)
    {
        Validate(e);
        if (await new Cls_DamDAL().GetAsync(damId) == null) return NotFound();
        e.Id = Ids.New(); e.DamId = damId;
        await new Cls_EapDAL().UpsertAsync(e, true);
        await Audit("EAP", $"Added EAP contact {e.Name} ({e.EapRole}), order {e.EscalationOrder}, from Level {e.MinLevel}", "dam", damId);
        return Ok(e);
    }

    [Authorize(Policy = Policies.ManageEap), HttpPut("{id}")]
    public async Task<IActionResult> Update(string damId, string id, EapContact e)
    {
        Validate(e);
        var cur = await new Cls_EapDAL().GetAsync(id);
        if (cur == null || cur.DamId != damId) return NotFound();
        e.Id = id; e.DamId = damId;
        await new Cls_EapDAL().UpsertAsync(e, false);
        await Audit("EAP", $"Updated EAP contact {e.Name}{(e.IsActive ? "" : " (inactive)")}", "dam", damId);
        return Ok(e);
    }

    private static void Validate(EapContact e)
    {
        if (string.IsNullOrWhiteSpace(e.Name)) throw new BusinessException("Name is required.");
        if (string.IsNullOrWhiteSpace(e.Phone) && string.IsNullOrWhiteSpace(e.Email)) throw new BusinessException("Give a phone number or an e-mail address.");
        if (!string.IsNullOrWhiteSpace(e.Email) && !System.Text.RegularExpressions.Regex.IsMatch(e.Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$")) throw new BusinessException("E-mail address is not valid.");
        if (!string.IsNullOrWhiteSpace(e.Phone) && !System.Text.RegularExpressions.Regex.IsMatch(e.Phone, @"^\+?[0-9 \-]{8,16}$")) throw new BusinessException("Phone number is not valid.");
        if (e.MinLevel is < 1 or > 3) throw new BusinessException("Notify-from level must be 1, 2 or 3.");
        if (e.EscalationOrder is < 1 or > 9) throw new BusinessException("Escalation order must be 1–9.");
    }
}

// =====================================================================
[Authorize, Route("api/notifications")]
public class NotificationsController : AppController
{
    [HttpGet("mine")]
    public async Task<IActionResult> Mine(bool unread = false) => Ok(await new Cls_NotificationDAL().InboxAsync(User.UserId(), unread));

    [HttpGet("mine/count")]
    public async Task<IActionResult> Count() => Ok(new { unread = await new Cls_NotificationDAL().UnreadCountAsync(User.UserId()) });

    [HttpPost("mine/read")]
    public async Task<IActionResult> Read(MarkReadRequest req) { await new Cls_NotificationDAL().MarkReadAsync(User.UserId(), req.Id); return NoContent(); }

    [Authorize(Policy = Policies.ViewNotificationLog), HttpGet("log")]
    public async Task<IActionResult> Log(string? channel, string? status, int take = 300) =>
        Ok(await new Cls_NotificationDAL().LogAsync(string.IsNullOrEmpty(channel) ? null : channel, string.IsNullOrEmpty(status) ? null : status, Math.Clamp(take, 1, 2000)));

    /// <summary>Runs one escalation + dispatch cycle immediately (normally every minute in the background).</summary>
    [Authorize(Roles = Roles.Admin), HttpPost("run")]
    public async Task<IActionResult> Run()
    {
        var escalated = await new Cls_NotificationEngine().EscalateDueAsync();
        var sent = await new Cls_NotificationDispatcher().DispatchAsync();
        await Audit("Notifications", $"Manual cycle: {escalated} escalated, {sent} dispatched");
        return Ok(new { escalated, dispatched = sent });
    }
}

// =====================================================================
[Authorize, Route("api/dams/{damId}/schedule")]
public class ScheduleController : AppController
{
    /// <summary>Required reading frequency (by dam life phase) against actual readings for every sensor.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(string damId)
    {
        var dam = await new Cls_DamDAL().GetAsync(damId);
        if (dam == null) return NotFound();
        var rows = await new Cls_ScheduleDAL().ForDamAsync(damId, dam.LifePhase);
        var now = DateTime.UtcNow;
        foreach (var r in rows)
        {
            if (r.RequiredHours is not double h || h <= 0) { r.Status = "Event-based"; continue; }
            r.LoggerSlowerThanRequired = r.ConfiguredMinutes > h * 60;
            if (!r.LastReading.HasValue) { r.Status = "No data"; continue; }
            r.NextDue = r.LastReading.Value.AddHours(h);
            r.Status = now > r.NextDue.Value.AddHours(h) ? "Overdue" : now > r.NextDue ? "Due" : "On time";
            r.CompliancePct = Math.Min(100, Math.Round(100.0 * r.ReadingsLast30d / (30 * 24 / h), 1));
        }
        return Ok(new { lifePhase = dam.LifePhase, rows });
    }
}

// =====================================================================
[Authorize, Route("api/alerts")]
public class AlertExtrasController : AppController
{
    [HttpGet("{id}/notifications")]
    public async Task<IActionResult> Notifications(string id) => Ok(await new Cls_NotificationDAL().ForAlertAsync(id));

    [Authorize(Policy = Policies.ManageInspections), HttpPost("{id}/inspection")]
    public async Task<IActionResult> CreateInspection(string id)
    {
        var a = await new Cls_AlertDAL().GetAsync(id) ?? throw new BusinessException("Alert not found.");
        if (a.InspectionId != null) return Ok(new { inspectionId = a.InspectionId });
        var i = new Inspection { Id = Ids.New(), DamId = a.DamId, InspectionType = "Special", ScheduledDate = DateTime.UtcNow.Date, TriggerAlertId = a.Id, CreatedBy = User.FullName() };
        await new Cls_InspectionDAL().InsertAsync(i);
        await new Cls_NotificationDAL().LinkInspectionAsync(a.Id, i.Id);
        await Audit("Inspections", $"Special inspection created from alert: {a.Title}", "inspection", i.Id);
        return Ok(new { inspectionId = i.Id });
    }
}
