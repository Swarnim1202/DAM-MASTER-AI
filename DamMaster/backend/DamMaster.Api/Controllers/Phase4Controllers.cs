using DamMaster.Api.Common;
using DamMaster.Api.DAL;
using DamMaster.Api.Engine;
using DamMaster.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DamMaster.Api.Controllers;

// =====================================================================
/// <summary>Gateway health dashboard, settings, key rotation, message log and reconciliation of buffered data.</summary>
[Authorize, Route("api/gateways")]
public class GatewayHealthController : AppController
{
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await new Cls_Gateway4DAL().HealthAsync());

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, int statusDays = 7)
    {
        var g = (await new Cls_Gateway4DAL().HealthAsync(id)).FirstOrDefault();
        if (g == null) return NotFound();
        var root = AppConfig.MqttTopicRoot;
        return Ok(new
        {
            gateway = g, messages = await new Cls_Gateway4DAL().MessagesAsync(id, 100), status = await new Cls_Gateway4DAL().StatusLogAsync(id, Math.Clamp(statusDays, 1, 90)),
            alerts = (await new Cls_AlertDAL().ListAsync(g.DamId, null, null, 500)).Where(a => a.GatewayId == id).Take(20),
            mqtt = new { telemetryTopic = $"{root}/{g.DamCode}/{g.Code}/telemetry", statusTopic = $"{root}/{g.DamCode}/{g.Code}/status", ackTopic = $"{root}/{g.DamCode}/{g.Code}/ack" }
        });
    }

    [Authorize(Policy = Policies.ManageGateways), HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, UpdateGatewayRequest req)
    {
        if (req.ExpectedIntervalMin is < 1 or > 1440) throw new BusinessException("Expected interval must be 1–1440 minutes.");
        if ((await new Cls_Gateway4DAL().HealthAsync(id)).Count == 0) return NotFound();
        await new Cls_Gateway4DAL().UpdateSettingsAsync(id, req.ExpectedIntervalMin, req.IsActive);
        await Audit("Gateways", $"Settings: every {req.ExpectedIntervalMin} min, {(req.IsActive ? "active" : "disabled")}", "gateway", id);
        return NoContent();
    }

    [Authorize(Policy = Policies.ManageGateways), HttpPost("{id}/rotate-key")]
    public async Task<IActionResult> RotateKey(string id)
    {
        if ((await new Cls_Gateway4DAL().HealthAsync(id)).Count == 0) return NotFound();
        var key = PasswordHasher.NewApiKey();
        await new Cls_Gateway4DAL().SetKeyAsync(id, PasswordHasher.Hash(key));
        await Audit("Gateways", "HTTP API key rotated", "gateway", id);
        return Ok(new { apiKey = key, note = "The old key stops working now. Store this key — it is shown only once." });
    }

    [HttpGet("{id}/reconciliation")]
    public async Task<IActionResult> Reconciliation(string id, int days = 7)
    {
        var g = (await new Cls_Gateway4DAL().HealthAsync(id)).FirstOrDefault();
        if (g == null) return NotFound();
        return Ok(new { gateway = g.Code, days, rows = await new Cls_AggregateDAL().ReconciliationAsync(g.DamId, Math.Clamp(days, 1, 90)) });
    }
}

// =====================================================================
[Authorize, Route("api/system")]
public class SystemController : AppController
{
    [HttpGet("status")]
    public async Task<IActionResult> Status() => Ok(new
    {
        version = "4.0",
        mqtt = new { MqttStatus.Enabled, MqttStatus.Connected, MqttStatus.Broker, MqttStatus.Topics, MqttStatus.ConnectedSince, MqttStatus.LastMessageAt, MqttStatus.Received, MqttStatus.Failed, MqttStatus.DuplicateMessages, MqttStatus.LastError },
        jobs = await new Cls_JobDAL().ListAsync(),
        storage = await new Cls_JobDAL().StorageAsync(),
        serverTimeUtc = DateTime.UtcNow
    });

    /// <summary>Runs every background job once, including the daily ones (DHI snapshot, HST refit check).</summary>
    [Authorize(Roles = Roles.Admin), HttpPost("run-jobs")]
    public async Task<IActionResult> RunJobs()
    {
        var r = await Cls_JobRunner.RunAsync(true);
        await Audit("System", "Ran all background jobs: " + string.Join("; ", r.Select(kv => $"{kv.Key} {kv.Value}")));
        return Ok(r);
    }
}
