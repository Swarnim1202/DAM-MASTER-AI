using DamMaster.Api.Common;
using DamMaster.Api.DAL;
using DamMaster.Api.Engine;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DamMaster.Api.Controllers;

// =====================================================================
[Authorize, Route("api/map")]
public class MapController : AppController
{
    /// <summary>Dams, sensors with coordinates and the latest InSAR dataset per dam, for the GIS view.</summary>
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var dams = await new Cls_DamDAL().SummariesAsync(); var damRows = (await new Cls_DamDAL().ListAsync()).ToDictionary(d => d.Id);
        var sensors = new List<object>(); var insar = new List<object>();
        foreach (var d in dams)
        {
            foreach (var s in (await new Cls_SensorDAL().StatusListAsync(d.Id))) { var full = await new Cls_SensorDAL().GetAsync(s.Id); if (full?.Latitude != null && full.Longitude != null) sensors.Add(new { s.Id, s.Code, s.ParameterName, damId = d.Id, lat = full.Latitude, lon = full.Longitude, s.LatestValue, s.EngUnit, level = s.OpenAlerts > 0 ? s.MaxOpenLevel : 0 }); }
            var ds = (await new Cls_SurveyService().ListAsync(d.Id)).FirstOrDefault(x => x.Kind == "InSAR");
            if (ds != null) { var (_, pts) = await new Cls_SurveyService().GetAsync(ds.Id); insar.Add(new { damId = d.Id, dataset = ds.Name, ds.Threshold, points = pts.Take(20000).Select(p => new { p.PointId, lat = p.Latitude, lon = p.Longitude, v = p.Value }) }); }
        }
        return Ok(new { tileUrl = AppConfig.MapTileUrl, attribution = AppConfig.MapAttribution,
            dams = dams.Select(d => new { d.Id, d.Code, d.Name, lat = damRows[d.Id].Latitude, lon = damRows[d.Id].Longitude, d.MaxOpenLevel, d.OpenAlerts, d.Dhi, d.DhiBand, d.HazardCode }), sensors, insar });
    }
}

// =====================================================================
[Authorize, Route("api/surveys")]
public class SurveysController : AppController
{
    [HttpGet] public async Task<IActionResult> List(string? damId) => Ok(await new Cls_SurveyService().ListAsync(damId));

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id) { var (ds, pts) = await new Cls_SurveyService().GetAsync(id); return ds == null ? NotFound() : Ok(new { dataset = ds, points = pts }); }

    [Authorize(Policy = Policies.ManageSurveys), HttpPost]
    [RequestSizeLimit(60_000_000)]
    public async Task<IActionResult> Import(SurveyImportRequest req) => Ok(await new Cls_SurveyService().ImportAsync(req, User.FullName()));

    [HttpGet("compare")]
    public async Task<IActionResult> Compare(string a, string b) => Ok(await new Cls_SurveyService().CompareAsync(a, b));
}

// =====================================================================
[Authorize, Route("api/dams/{damId}/documents/{documentId}/files")]
public class DocumentFilesController : AppController
{
    [HttpGet] public async Task<IActionResult> List(string documentId) => Ok(await new Cls_DocIndexer().FilesAsync(documentId));

    [Authorize(Policy = Policies.ManageDocuments), HttpPost]
    [RequestSizeLimit(60_000_000)]
    public async Task<IActionResult> Upload(string damId, string documentId, IFormFile file)
    {
        var doc = await new Cls_DocumentDAL().GetAsync(documentId);
        if (doc == null || doc.DamId != damId) return NotFound();
        var f = await new Cls_DocIndexer().StoreAsync(damId, documentId, file, User.FullName());
        await Audit("Documents", $"Uploaded {f.FileName} to {doc.Title} ({f.SizeBytes / 1024} KB, {(f.ExtractedOk ? $"{f.TextChars} characters indexed" : f.ExtractNote)})", "dam_document", documentId);
        return Ok(f);
    }
}

[Authorize, Route("api/files")]
public class FilesController : AppController
{
    [HttpGet("{id}")]
    public async Task<IActionResult> Download(string id)
    {
        var f = await new Cls_DocIndexer().FileAsync(id);
        if (f == null || !System.IO.File.Exists(f.StoragePath)) return NotFound();
        return PhysicalFile(Path.GetFullPath(f.StoragePath), f.ContentType ?? "application/octet-stream", f.FileName);
    }
}

// =====================================================================
[Authorize, Route("api/library")]
public class LibraryController : AppController
{
    [HttpGet("search")]
    public async Task<IActionResult> Search(string q, string? damId, int take = 20) => Ok(await new Cls_DocIndexer().SearchAsync(q, damId, Math.Clamp(take, 1, 50)));

    [HttpPost("ask")]
    public async Task<IActionResult> Ask(AskRequest req)
    {
        var r = await new Cls_LibraryAi().AskAsync(req);
        await Audit("Library", "Asked: " + (req.Question.Length > 200 ? req.Question[..200] : req.Question));
        return Ok(r);
    }
}

// =====================================================================
[Authorize, Route("api/dams/{damId}")]
public class TwinController : AppController
{
    [HttpGet("twin")] public async Task<IActionResult> Twin(string damId) => Ok(await new Cls_TwinService().BuildAsync(damId));

    [HttpGet("design-thresholds")]
    public async Task<IActionResult> Suggest(string damId, string projectId) => Ok(await new Cls_DesignLink().SuggestAsync(damId, projectId));

    [Authorize(Policy = Policies.ApplyDesignThresholds), HttpPost("design-thresholds")]
    public async Task<IActionResult> Apply(string damId, ApplySuggestionsRequest req)
    {
        var n = await new Cls_DesignLink().ApplyAsync(damId, req, User.FullName());
        return Ok(new { applied = n, note = "New threshold versions created — another Dam Safety Engineer or an Approver must approve them." });
    }
}
