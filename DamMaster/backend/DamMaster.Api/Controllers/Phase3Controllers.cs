using System.Text.Json;
using DamMaster.Api.Common;
using DamMaster.Api.DAL;
using DamMaster.Api.Engine;
using DamMaster.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DamMaster.Api.Controllers;

public record RejectRequest(string? Note);

// =====================================================================
/// <summary>HST behaviour models: fit (Draft) → approve (Active, used by the alert engine) or reject.</summary>
[Authorize, Route("api/sensors/{sensorId}/hst")]
public class HstController : AppController
{
    [HttpGet]
    public async Task<IActionResult> Get(string sensorId, string? modelId = null, int days = 365)
    {
        var models = await new Cls_HstDAL().ListAsync(sensorId);
        var shown = modelId != null ? models.FirstOrDefault(m => m.Id == modelId) : models.FirstOrDefault(m => m.Status == "Active") ?? models.FirstOrDefault(m => m.Status == "Draft");
        var series = shown == null ? null : await new Cls_HstService().SeriesAsync(shown, days);
        var coef = shown == null ? null : Cls_HstService.Coef(shown);
        return Ok(new { models, shown, coefficients = coef, series });
    }

    [Authorize(Policy = Policies.FitModels), HttpPost("fit")]
    public async Task<IActionResult> Fit(string sensorId, FitRequest req)
    {
        var k = req.KLimit ?? 2.5;
        if (k < 1.5 || k > 5) throw new BusinessException("Residual limit k must be between 1.5 and 5.");
        var m = await new Cls_HstService().FitAsync(sensorId, k, User.FullName());
        await Audit("HST", $"Fitted {m.Variant} model: R² {m.R2:0.000}, σ {m.Sigma:0.###}, {m.NPoints} points", "sensor", sensorId);
        return Ok(m);
    }

    [Authorize(Policy = Policies.ApproveThresholds), HttpPost("{modelId}/approve")]
    public async Task<IActionResult> Approve(string sensorId, string modelId)
    {
        var dal = new Cls_HstDAL(); var m = await dal.GetAsync(modelId);
        if (m == null || m.SensorId != sensorId) return NotFound();
        if (m.Status != "Draft") throw new BusinessException("Only a draft model can be approved.");
        if (m.FittedBy == User.FullName()) throw new BusinessException("Segregation of duties: the model must be approved by someone other than the person who fitted it.");
        if (m.R2 < 0.5) throw new BusinessException($"R² is only {m.R2:0.00} — the model explains too little of the behaviour to be used for alerts. Reject it or collect more data.");
        await dal.ApproveAsync(m, User.FullName());
        await Audit("HST", $"Approved {m.Variant} model (R² {m.R2:0.000}); residual alerts now use it", "sensor", sensorId);
        return NoContent();
    }

    [Authorize(Policy = Policies.ApproveThresholds), HttpPost("{modelId}/reject")]
    public async Task<IActionResult> Reject(string sensorId, string modelId, RejectRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Note)) throw new BusinessException("Give the reason for rejecting.");
        if (await new Cls_HstDAL().RejectAsync(modelId, req.Note.Trim()) == 0) throw new BusinessException("Only a draft model can be rejected.");
        await Audit("HST", "Rejected model: " + req.Note, "sensor", sensorId);
        return NoContent();
    }
}

// =====================================================================
[Authorize, Route("api/sensors/{sensorId}/response")]
public class ResponseController : AppController
{
    [HttpGet]
    public async Task<IActionResult> Get(string sensorId, double rainEventMm = 20) => Ok(await new Cls_ResponseAnalysis().AnalyseAsync(sensorId, Math.Clamp(rainEventMm, 1, 500)));
}

// =====================================================================
[Authorize, Route("api/dams/{damId}/dhi")]
public class DhiController : AppController
{
    [HttpGet]
    public async Task<IActionResult> Get(string damId) =>
        Ok(new { current = await new Cls_DhiEngine().ComputeAsync(damId), history = await new Cls_DhiDAL().HistoryAsync(damId, 120), weights = await new Cls_DhiDAL().WeightsAsync() });

    [Authorize(Roles = Roles.Admin + "," + Roles.DamSafetyEngineer), HttpPost("snapshot")]
    public async Task<IActionResult> Snapshot(string damId)
    {
        var r = await new Cls_DhiEngine().ComputeAsync(damId);
        await new Cls_DhiDAL().InsertSnapshotAsync(damId, r.Dhi, r.Band, r.Risk, r.Rating, JsonSerializer.Serialize(r.Components), User.FullName());
        await Audit("DHI", $"Snapshot recorded: DHI {r.Dhi} ({r.Band}), risk {r.Risk} ({r.Rating})", "dam", damId);
        return Ok(r);
    }
}

// =====================================================================
[Authorize, Route("api/dams/{damId}/documents")]
public class DocumentsController : AppController
{
    public static readonly string[] Types = { "EAP", "O&M manual", "Instrument register", "As-built drawings", "Safety review", "Inspection report", "Design report", "Other" };
    private static readonly string[] Statuses = { "Approved", "Under review", "Revision due", "Draft" };

    [HttpGet] public async Task<IActionResult> List(string damId) => Ok(await new Cls_DocumentDAL().ListAsync(damId));

    [Authorize(Policy = Policies.ManageDocuments), HttpPost]
    public async Task<IActionResult> Create(string damId, DamDocument d)
    {
        Validate(d);
        if (await new Cls_DamDAL().GetAsync(damId) == null) return NotFound();
        d.Id = Ids.New(); d.DamId = damId; d.CreatedBy = User.FullName();
        await new Cls_DocumentDAL().UpsertAsync(d, true);
        await Audit("Documents", $"Registered {d.DocType}: {d.Title} ({d.Status})", "dam", damId);
        return Ok(d);
    }

    [Authorize(Policy = Policies.ManageDocuments), HttpPut("{id}")]
    public async Task<IActionResult> Update(string damId, string id, DamDocument d)
    {
        Validate(d);
        var cur = await new Cls_DocumentDAL().GetAsync(id);
        if (cur == null || cur.DamId != damId) return NotFound();
        d.Id = id; d.DamId = damId;
        await new Cls_DocumentDAL().UpsertAsync(d, false);
        await Audit("Documents", $"Updated {d.DocType}: {d.Title} → {d.Status}", "dam", damId);
        return Ok(d);
    }

    private static void Validate(DamDocument d)
    {
        if (!Types.Contains(d.DocType)) throw new BusinessException("Document type must be one of: " + string.Join(", ", Types) + ".");
        if (!Statuses.Contains(d.Status)) throw new BusinessException("Status must be Approved, Under review, Revision due or Draft.");
        if (string.IsNullOrWhiteSpace(d.Title)) throw new BusinessException("Title is required.");
        if (d.IssueDate.HasValue && d.NextReviewDate.HasValue && d.NextReviewDate < d.IssueDate) throw new BusinessException("Next review date cannot be before the issue date.");
    }
}

// =====================================================================
[Authorize, Route("api/reports")]
public class ReportsController : AppController
{
    [HttpGet("availability")]
    public async Task<IActionResult> Availability(string damId, int days = 30, bool relativeToNow = false) =>
        Ok(await new Cls_AvailabilityReport().BuildAsync(damId, Math.Clamp(days, 1, 366), relativeToNow));
}
