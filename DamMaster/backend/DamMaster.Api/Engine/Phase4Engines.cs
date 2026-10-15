using System.Text;
using System.Text.Json;
using DamMaster.Api.Common;
using DamMaster.Api.DAL;
using DamMaster.Api.Models;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace DamMaster.Api.Engine;

public record TelemetryOutcome(string MessageId, bool DuplicateMessage, int Accepted, int Rejected, int Quarantined, int Duplicates, int Late, int AlertsRaised, string? Error);

/// <summary>Single path for every telemetry message (MQTT or HTTP): idempotency, dam check, validation, late-data accounting.</summary>
public class Cls_TelemetryProcessor
{
    public async Task<TelemetryOutcome> ProcessAsync(TelemetryMessage msg, GatewayView gw, int expectedIntervalMin, string transport)
    {
        if (string.IsNullOrWhiteSpace(msg.MessageId)) return new(msg.MessageId ?? "", false, 0, 0, 0, 0, 0, 0, "messageId is required");
        var dam = await new Cls_DamDAL().GetAsync(gw.DamId);
        if (dam == null || !string.Equals(dam.Code, msg.DamId, StringComparison.OrdinalIgnoreCase))
            return new(msg.MessageId, false, 0, 0, 0, 0, 0, 0, "Gateway is not registered for this dam");
        var dal = new Cls_Gateway4DAL();
        if (!await dal.TryRegisterMessageAsync(gw.Id, msg.MessageId, transport))
        {
            await dal.SetTransportAsync(gw.Id, transport);
            return new(msg.MessageId, true, 0, 0, 0, 0, 0, 0, null);
        }
        var req = new ImportRequest
        {
            FileName = $"{transport} {gw.Code} message {msg.MessageId}", Commit = true,
            Readings = msg.Readings.Select(r => new ImportReadingRow { DamId = msg.DamId, SensorId = r.SensorId, ParameterCode = r.ParameterCode, ObservedAt = r.ObservedAt, Value = r.Value, Unit = r.Unit, RawValue = r.RawValue, LoggerStatus = r.LoggerStatus }).ToList()
        };
        var now = DateTime.UtcNow;
        var times = msg.Readings.Select(r => Cls_QualityEngine.ParseObserved(r.ObservedAt)).Where(t => t.HasValue && t <= now.AddMinutes(5)).Select(t => t!.Value).ToList();
        var late = times.Count(t => (now - t).TotalMinutes > 2.0 * expectedIntervalMin);
        int? maxLatency = times.Count > 0 ? (int)Math.Max(0, times.Max(t => (now - t).TotalSeconds)) : null;
        try
        {
            var res = await new Cls_QualityEngine().ProcessAsync(req, transport, "gateway:" + gw.Code, null);
            await dal.CompleteMessageAsync(gw.Id, msg.MessageId, msg.Readings.Count, res.Good + res.Suspect + res.Bad, res.Rejected, res.Quarantined, res.Duplicates, late, maxLatency, null);
            await dal.SetTransportAsync(gw.Id, transport);
            await new Cls_GatewayHealthJob().ResolveOfflineAsync(gw.Id);
            return new(msg.MessageId, false, res.Good + res.Suspect + res.Bad, res.Rejected, res.Quarantined, res.Duplicates, late, res.AlertsRaised.Count, null);
        }
        catch (Exception ex)
        {
            await dal.CompleteMessageAsync(gw.Id, msg.MessageId, msg.Readings.Count, 0, 0, 0, 0, late, maxLatency, ex.Message);
            throw;
        }
    }
}

/// <summary>Gateway online / late / offline and battery checks; raises Level 1 data-health alerts and resolves them on recovery.</summary>
public class Cls_GatewayHealthJob
{
    private readonly Cls_AlertDAL _alerts = new();

    public async Task<string> RunAsync()
    {
        int raised = 0, resolved = 0;
        foreach (var g in await new Cls_Gateway4DAL().HealthAsync())
        {
            if (!g.IsActive || g.State == "Never connected") continue;
            if (g.State == "Offline")
            {
                var last = g.LastMessageAt ?? g.LastSeenAt;
                raised += await RaiseAsync(g, "Gateway", $"Gateway {g.Code} offline — no data for {(DateTime.UtcNow - last!.Value).TotalHours:0.0} h",
                    $"Expected a message every {g.ExpectedIntervalMin} min. Readings buffered on the logger will be accepted when the link returns. Check power, modem and broker connection.");
            }
            else resolved += await ResolveAsync(g.Id, "Gateway", "Gateway back online");
            if (g.BatteryV.HasValue && g.BatteryV < AppConfig.GatewayLowBatteryV)
                raised += await RaiseAsync(g, "GatewayBattery", $"Gateway {g.Code} battery low ({g.BatteryV:0.0} V)", $"Below {AppConfig.GatewayLowBatteryV} V. Check the solar panel, charge controller and battery before the logger shuts down.");
            else if (g.BatteryV.HasValue) resolved += await ResolveAsync(g.Id, "GatewayBattery", "Battery voltage back to normal");
        }
        return $"{raised} raised, {resolved} resolved";
    }

    public Task<int> ResolveOfflineAsync(string gatewayId) => ResolveAsync(gatewayId, "Gateway", "Gateway back online");

    private async Task<int> ResolveAsync(string gatewayId, string rule, string reason)
    {
        var open = await _alerts.FindOpenGatewayAsync(gatewayId, rule);
        if (open == null) return 0;
        await _alerts.AutoResolveAsync(open.Id, reason);
        await new Cls_AuditDAL().WriteAsync(null, "system", "Gateways", $"Auto-resolved: {open.Title} — {reason}", "alert", open.Id);
        return 1;
    }

    private async Task<int> RaiseAsync(GatewayHealth g, string rule, string title, string detail)
    {
        if (await _alerts.FindOpenGatewayAsync(g.Id, rule) != null) return 0;
        var a = new AlertView { Id = Ids.New(), DamId = g.DamId, GatewayId = g.Id, Level = 1, RuleType = rule, Title = title, Detail = detail, FirstAt = DateTime.UtcNow, LastAt = DateTime.UtcNow, Status = "Open" };
        await _alerts.InsertAsync(a);
        await new Cls_NotificationEngine().OnAlertAsync(a.Id, "Gateway health check");
        return 1;
    }
}

/// <summary>Monthly HST refit: a new Draft model for review when the active one is old and enough new data exists; reports model bias.</summary>
public class Cls_HstRefitJob
{
    public async Task<string> RunAsync()
    {
        int fitted = 0, skipped = 0;
        using var c = new MySqlConnector.MySqlConnection(AppConfig.ConnectionString);
        var due = (await Dapper.SqlMapper.QueryAsync<HstModel>(c, @"SELECT m.* FROM hst_model m WHERE m.status='Active' AND m.fitted_at < DATE_SUB(UTC_TIMESTAMP(), INTERVAL @d DAY)
            AND NOT EXISTS (SELECT 1 FROM hst_model x WHERE x.sensor_id=m.sensor_id AND x.status='Draft' AND x.fitted_at > m.fitted_at)", new { d = AppConfig.HstRefitDays })).ToList();
        foreach (var m in due)
        {
            var last = await new Cls_ReadingDAL().LatestTimeAsync(m.SensorId);
            if (!last.HasValue || (last.Value - m.DataTo).TotalDays < 30) { skipped++; continue; }
            try
            {
                var recent = (await new Cls_HstService().SeriesAsync(m, 30)).Select(p => p.Residual).ToList();
                var bias = recent.Count > 0 ? recent.Average() / m.Sigma : 0;
                var nm = await new Cls_HstService().FitAsync(m.SensorId, (double)m.KLimit, "system (scheduled refit)");
                var sensor = await new Cls_SensorDAL().GetAsync(m.SensorId);
                var text = $"Scheduled refit for {sensor?.Code}: R² {m.R2:0.000} → {nm.R2:0.000}, σ {m.Sigma:0.###} → {nm.Sigma:0.###}. " +
                           $"Active model bias over the last 30 days: {bias:+0.00;-0.00}σ{(Math.Abs(bias) > 1 ? " — the current model no longer fits well; review the new draft" : "")}.";
                var users = await new Cls_UserDAL().ActiveByRolesAsync(new[] { Roles.DamSafetyEngineer });
                await new Cls_NotificationDAL().InsertManyAsync(users.Select(u => new NotificationRow { DamId = sensor?.DamId, Level = 0, Channel = "InApp", Recipient = u.Id, RecipientName = u.FullName, Subject = $"[DAM MASTER] HST model refit ready — {sensor?.Code}", Body = text, Status = "Delivered" }));
                await new Cls_AuditDAL().WriteAsync(null, "system", "HST", text, "sensor", m.SensorId);
                fitted++;
            }
            catch (Exception ex) { skipped++; await new Cls_AuditDAL().WriteAsync(null, "system", "HST", $"Scheduled refit skipped: {ex.Message}", "sensor", m.SensorId); }
        }
        return $"{fitted} refitted, {skipped} skipped, {due.Count} due";
    }
}

/// <summary>All background jobs in one place: used by the hosted service and by the admin "run now" endpoint.</summary>
public static class Cls_JobRunner
{
    private static DateTime _lastDaily = DateTime.UtcNow.Date;

    public static async Task<Dictionary<string, string>> RunAsync(bool forceDaily)
    {
        var results = new Dictionary<string, string>();
        async Task Run(string job, Func<Task<string>> f)
        {
            try { var r = await f(); results[job] = r; await new Cls_JobDAL().RecordAsync(job, true, r); }
            catch (Exception ex) { results[job] = "ERROR: " + ex.Message; await new Cls_JobDAL().RecordAsync(job, false, ex.Message); }
        }
        await Run("Escalation", async () => $"{await new Cls_NotificationEngine().EscalateDueAsync()} alert(s) escalated");
        await Run("Dispatch", async () => $"{await new Cls_NotificationDispatcher().DispatchAsync()} message(s) processed");
        await Run("GatewayHealth", () => new Cls_GatewayHealthJob().RunAsync());
        if (forceDaily || DateTime.UtcNow.Date > _lastDaily)
        {
            await Run("DhiSnapshot", async () =>
            {
                var dams = await new Cls_DamDAL().ListAsync();
                foreach (var d in dams)
                {
                    var r = await new Cls_DhiEngine().ComputeAsync(d.Id);
                    await new Cls_DhiDAL().InsertSnapshotAsync(d.Id, r.Dhi, r.Band, r.Risk, r.Rating, JsonSerializer.Serialize(r.Components), "system (daily)");
                }
                return $"{dams.Count} dam(s)";
            });
            await Run("HstRefit", () => new Cls_HstRefitJob().RunAsync());
            _lastDaily = DateTime.UtcNow.Date;
        }
        return results;
    }
}

// =====================================================================
public static class MqttStatus
{
    public static bool Enabled; public static bool Connected; public static string? Broker; public static string[] Topics = Array.Empty<string>();
    public static DateTime? ConnectedSince; public static DateTime? LastMessageAt; public static long Received; public static long Failed; public static long DuplicateMessages; public static string? LastError;
}

/// <summary>
/// Subscribes to {root}/{damCode}/{gatewayCode}/telemetry and /status (QoS 1, persistent session so the broker keeps
/// messages while the API restarts). Acknowledgements are published to {root}/{damCode}/{gatewayCode}/ack.
/// Broker authentication + topic ACLs ensure a gateway can publish only under its own topic.
/// </summary>
public class MqttIngestionService : BackgroundService
{
    private static readonly JsonSerializerOptions Js = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly ILogger<MqttIngestionService> _log;
    public MqttIngestionService(ILogger<MqttIngestionService> log) => _log = log;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        MqttStatus.Enabled = AppConfig.MqttEnabled;
        if (!AppConfig.MqttEnabled) return;
        var root = AppConfig.MqttTopicRoot; MqttStatus.Broker = $"{AppConfig.MqttHost}:{AppConfig.MqttPort}{(AppConfig.MqttTls ? " (TLS)" : "")}";
        MqttStatus.Topics = new[] { $"{root}/+/+/telemetry", $"{root}/+/+/status" };
        var factory = new MqttFactory();
        using var client = factory.CreateMqttClient();
        client.ApplicationMessageReceivedAsync += e => HandleAsync(client, e);
        client.DisconnectedAsync += e => { if (MqttStatus.Connected) _log.LogWarning("MQTT disconnected: {Reason}", e.Reason); MqttStatus.Connected = false; MqttStatus.LastError = e.Reason.ToString(); return Task.CompletedTask; };
        var delay = 5;
        while (!stop.IsCancellationRequested)
        {
            if (!client.IsConnected)
            {
                try
                {
                    var b = new MqttClientOptionsBuilder().WithTcpServer(AppConfig.MqttHost, AppConfig.MqttPort).WithClientId(AppConfig.MqttClientId)
                        .WithCleanSession(false).WithKeepAlivePeriod(TimeSpan.FromSeconds(30));
                    if (!string.IsNullOrEmpty(AppConfig.MqttUser)) b = b.WithCredentials(AppConfig.MqttUser, AppConfig.MqttPassword);
                    if (AppConfig.MqttTls) b = b.WithTlsOptions(o => o.UseTls().WithAllowUntrustedCertificates(AppConfig.MqttAllowUntrustedCert));
                    await client.ConnectAsync(b.Build(), stop);
                    var sub = factory.CreateSubscribeOptionsBuilder();
                    foreach (var t in MqttStatus.Topics) sub = sub.WithTopicFilter(f => f.WithTopic(t).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce));
                    await client.SubscribeAsync(sub.Build(), stop);
                    MqttStatus.Connected = true; MqttStatus.ConnectedSince = DateTime.UtcNow; MqttStatus.LastError = null; delay = 5;
                    _log.LogInformation("MQTT connected to {Broker}, subscribed to {Topics}", MqttStatus.Broker, string.Join(", ", MqttStatus.Topics));
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    MqttStatus.LastError = ex.Message; _log.LogWarning("MQTT connect failed ({Error}); retry in {Delay} s", ex.Message, delay);
                    try { await Task.Delay(TimeSpan.FromSeconds(delay), stop); } catch (OperationCanceledException) { break; }
                    delay = Math.Min(60, delay * 2); continue;
                }
            }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stop); } catch (OperationCanceledException) { break; }
        }
        if (client.IsConnected) await client.DisconnectAsync();
    }

    private async Task HandleAsync(IMqttClient client, MqttApplicationMessageReceivedEventArgs e)
    {
        var parts = e.ApplicationMessage.Topic.Split('/');
        if (parts.Length != 4 || parts[0] != AppConfig.MqttTopicRoot) return;
        var (damCode, gwCode, kind) = (parts[1], parts[2], parts[3]);
        var ackTopic = $"{AppConfig.MqttTopicRoot}/{damCode}/{gwCode}/ack";
        MqttStatus.Received++; MqttStatus.LastMessageAt = DateTime.UtcNow;
        try
        {
            var (gw, _) = await new Cls_GatewayDAL().GetByCodeAsync(gwCode);
            if (gw == null || !gw.IsActive) { MqttStatus.Failed++; await Ack(client, ackTopic, new { error = $"Unknown or disabled gateway {gwCode}" }); return; }
            var json = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
            if (kind == "status")
            {
                var s = JsonSerializer.Deserialize<GatewayStatusMessage>(json, Js) ?? new();
                await new Cls_Gateway4DAL().UpdateStatusAsync(gw.Id, s);
                await new Cls_Gateway4DAL().SetTransportAsync(gw.Id, "MQTT");
                return;
            }
            var msg = JsonSerializer.Deserialize<TelemetryMessage>(json, Js) ?? throw new InvalidOperationException("Empty message");
            if (!string.Equals(msg.DamId, damCode, StringComparison.OrdinalIgnoreCase) || !string.Equals(msg.GatewayId, gwCode, StringComparison.OrdinalIgnoreCase))
            { MqttStatus.Failed++; await Ack(client, ackTopic, new { msg.MessageId, error = "Topic does not match damId / gatewayId in the payload" }); return; }
            var interval = (await new Cls_Gateway4DAL().HealthAsync(gw.Id)).FirstOrDefault()?.ExpectedIntervalMin ?? 60;
            var outcome = await new Cls_TelemetryProcessor().ProcessAsync(msg, gw, interval, "MQTT");
            if (outcome.DuplicateMessage) MqttStatus.DuplicateMessages++;
            if (outcome.Error != null) MqttStatus.Failed++;
            await Ack(client, ackTopic, outcome);
        }
        catch (Exception ex)
        {
            MqttStatus.Failed++; MqttStatus.LastError = ex.Message;
            _log.LogError(ex, "MQTT message on {Topic} failed", e.ApplicationMessage.Topic);
            try { await Ack(client, ackTopic, new { error = "Processing failed: " + ex.Message }); } catch { /* ack is best effort */ }
        }
    }

    private static Task Ack(IMqttClient client, string topic, object body) =>
        client.PublishAsync(new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(JsonSerializer.Serialize(body, Js))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build());
}
