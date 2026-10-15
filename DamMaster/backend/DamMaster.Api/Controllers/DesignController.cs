using System.Text.Json;
using DamMaster.Api.Common;
using DamMaster.Api.DAL;
using DamMaster.Api.Engine;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DamMaster.Api.Controllers;

/// <summary>Phase 5 — design workspace: projects, inputs, nine connected calculations, STALE tracking, check / approve.</summary>
[Authorize, Route("api/design")]
public class DesignController : AppController
{
    private static readonly JsonSerializerOptions Js = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly Cls_DesignDAL _dal = new();
    private readonly Cls_DesignService _svc = new();

    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog() => Ok(new
    {
        inputs = (await _dal.CatalogAsync()).Select(x => new { key = x.InputKey, x.Label, x.Unit, group = x.Grp, x.Kind, @default = JsonDocument.Parse(x.DefaultJson).RootElement, options = x.OptionsJson == null ? null : JsonSerializer.Deserialize<List<string>>(x.OptionsJson), x.Help }),
        calcs = DesignCalcs.Order.Select(c => new { code = c, title = DesignCalcs.Titles[c], dependsOn = DesignCalcs.Dep[c] }),
        defaultEa = await _dal.DefaultEaAsync()
    });

    [HttpGet("projects")] public async Task<IActionResult> Projects() => Ok(await _dal.ListAsync());

    [Authorize(Policy = Policies.EditDesign), HttpPost("projects")]
    public async Task<IActionResult> Create(CreateDesignProjectRequest req)
    {
        var p = await _svc.CreateAsync(req, User.FullName());
        await Audit("Design", $"Created design project {p.Code}: {p.Name}" + (req.CopyFromId != null ? " (copy)" : ""), "design_project", p.Id);
        return Ok(new { p.Id, p.Code });
    }

    [HttpGet("projects/{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var p = await _dal.GetAsync(id);
        if (p == null) return NotFound();
        var ok = await _dal.VerifiedStandardsAsync(); var titles = await _dal.StandardTitlesAsync();
        var calcs = (await _dal.CalcsAsync(id)).OrderBy(c => Array.IndexOf(DesignCalcs.Order, c.CalcCode)).Select(c =>
        {
            var rec = JsonSerializer.Deserialize<JsonElement>(c.RecordJson);
            var codes = rec.GetProperty("codes").EnumerateArray().Select(x => x.GetString()!).ToList();
            return new
            {
                code = c.CalcCode, title = DesignCalcs.Titles[c.CalcCode], c.Version, c.Status, c.Stale, c.StaleReason, c.RunBy, c.RunAt, c.CheckedBy, c.CheckedAt, c.ApprovedBy, c.ApprovedAt, c.Maturity,
                results = rec.GetProperty("results"), checks = rec.GetProperty("checks"), interpretation = rec.GetProperty("interpretation").GetString(), data = rec.GetProperty("data"),
                codes = codes.Select(x => new { code = x, title = titles.GetValueOrDefault(x, x), verified = ok.Contains(x) })
            };
        });
        return Ok(new { project = p, inputs = JsonSerializer.Deserialize<JsonElement>(p.InputsJson), ea = JsonSerializer.Deserialize<JsonElement>(p.EaJson), calcs });
    }

    [Authorize(Policy = Policies.EditDesign), HttpPut("projects/{id}")]
    public async Task<IActionResult> UpdateMeta(string id, [FromBody] Dictionary<string, string?> body)
    {
        var p = await _dal.GetAsync(id) ?? throw new BusinessException("Project not found.");
        var name = body.GetValueOrDefault("name") ?? p.Name; var status = body.GetValueOrDefault("status") ?? p.Status;
        if (status is not ("Active" or "Archived")) throw new BusinessException("Status must be Active or Archived.");
        await _dal.UpdateMetaAsync(id, name.Trim(), body.ContainsKey("damId") ? body["damId"] : p.DamId, status);
        await Audit("Design", $"{p.Code}: details updated", "design_project", id);
        return NoContent();
    }

    [Authorize(Policy = Policies.EditDesign), HttpPut("projects/{id}/inputs")]
    public async Task<IActionResult> Inputs(string id, UpdateDesignInputsRequest req) =>
        Ok(await _svc.UpdateInputsAsync(id, req.Changes.ToDictionary(kv => kv.Key, kv => (object?)kv.Value), null, User.FullName()));

    [Authorize(Policy = Policies.EditDesign), HttpPut("projects/{id}/ea")]
    public async Task<IActionResult> Ea(string id, UpdateEaRequest req) => Ok(await _svc.UpdateInputsAsync(id, new(), req.Rows, User.FullName()));

    /// <summary>Takes FRL / MWL / MDDL, river bed and dam type from a registered dam (through the normal input rules).</summary>
    [Authorize(Policy = Policies.EditDesign), HttpPost("projects/{id}/pull-dam")]
    public async Task<IActionResult> PullDam(string id, PullDamRequest req)
    {
        var d = await new Cls_DamDAL().GetAsync(req.DamId) ?? throw new BusinessException("Dam not found.");
        var p = await _dal.GetAsync(id) ?? throw new BusinessException("Project not found.");
        var cur = Cls_DesignService.Load(p); var ch = new Dictionary<string, object?>();
        if (d.FrlM.HasValue) ch["frl"] = (double)d.FrlM; if (d.MwlM.HasValue) ch["mwl"] = (double)d.MwlM; if (d.MddlM.HasValue) ch["mddl"] = (double)d.MddlM;
        if (d.CrestLevelM.HasValue && d.HeightM.HasValue)
        {
            var rbl = Math.Round((double)(d.CrestLevelM - d.HeightM) + cur.N("fdn"), 2); ch["rbl"] = rbl; ch["twln"] = rbl + 2; ch["twlf"] = rbl + 12;
            var frl = d.FrlM.HasValue ? (double)d.FrlM : cur.N("frl");
            if (cur.N("spc") >= frl || cur.N("spc") <= rbl) ch["spc"] = Math.Max(rbl + 1, frl - 12);
        }
        ch["type"] = d.DamTypeCode switch { "CONC_GRAVITY" or "RCC_GRAVITY" or "MASONRY_GRAVITY" => "Concrete gravity", "EARTH_ZONED" or "ECRD" => "Earthfill (zoned)", "EARTH_HOMOG" => "Earthfill (homogeneous)", "CFRD" or "ACRD" => "Rockfill (CFRD)", _ => "Auto" };
        if (d.SeismicZone != null) ch["zone"] = d.SeismicZone;
        // the elevation–area table is shifted with FRL so it stays in range — replace it with the reservoir survey
        List<double[]>? ea = null; string? note = null;
        if (d.FrlM.HasValue && Math.Abs((double)d.FrlM - cur.N("frl")) > 0.001)
        {
            var delta = (double)d.FrlM - cur.N("frl"); ea = cur.Ea.Select(r => new[] { Math.Round(r[0] + delta, 3), r[1] }).ToList();
            note = $"Elevation–area table shifted by {delta:+0.##;-0.##} m to follow FRL — replace it with the reservoir survey for this dam.";
        }
        var r = await _svc.UpdateInputsAsync(id, ch, ea, User.FullName());
        await _dal.UpdateMetaAsync(id, p.Name, d.Id, p.Status);
        return Ok(new { result = r, note });
    }

    [Authorize(Policy = Policies.EditDesign), HttpPost("projects/{id}/run")]
    public async Task<IActionResult> Run(string id, RunRequest req) => Ok(new { written = await _svc.RunAsync(id, User.FullName(), req.OnlyStale) });

    [HttpGet("projects/{id}/calcs/{code}")]
    public async Task<IActionResult> Calc(string id, string code)
    {
        var c = (await _dal.CalcsAsync(id)).FirstOrDefault(x => x.CalcCode == code);
        if (c == null) return NotFound();
        var ok = await _dal.VerifiedStandardsAsync(); var titles = await _dal.StandardTitlesAsync();
        var rec = JsonSerializer.Deserialize<CalcRecord>(c.RecordJson, Js)!;
        return Ok(new { calc = new { c.CalcCode, c.Version, c.Status, c.Stale, c.StaleReason, c.RunBy, c.RunAt, c.CheckedBy, c.CheckedAt, c.ApprovedBy, c.ApprovedAt, c.Maturity }, record = JsonSerializer.Deserialize<JsonElement>(c.RecordJson),
            codes = rec.Codes.Select(x => new { code = x, title = titles.GetValueOrDefault(x, x), verified = ok.Contains(x) }), history = await _dal.HistoryAsync(id, code) });
    }

    [Authorize(Policy = Policies.CheckDesign), HttpPost("projects/{id}/calcs/{code}/check")]
    public async Task<IActionResult> Check(string id, string code) { await _svc.SetStatusAsync(id, code, "Checked", User.FullName()); return NoContent(); }

    [Authorize(Policy = Policies.ApproveDesign), HttpPost("projects/{id}/calcs/{code}/approve")]
    public async Task<IActionResult> Approve(string id, string code) { await _svc.SetStatusAsync(id, code, "Approved", User.FullName()); return NoContent(); }

    [HttpGet("projects/{id}/impact")]
    public async Task<IActionResult> Impact(string id) => Ok((await _dal.CalcsAsync(id)).Where(c => c.Stale).OrderBy(c => Array.IndexOf(DesignCalcs.Order, c.CalcCode))
        .Select(c => new { code = c.CalcCode, title = DesignCalcs.Titles[c.CalcCode], reason = c.StaleReason, path = c.StaleKey == null ? "" : DesignCalcs.Path(c.StaleKey, c.CalcCode) }));

    [HttpGet("projects/{id}/changes")] public async Task<IActionResult> Changes(string id) => Ok(await _dal.ChangesAsync(id));

    /// <summary>Runs the engine against independent reference values (same cases as the prototype validation suite).</summary>
    [HttpGet("validation")]
    public async Task<IActionResult> Validation()
    {
        var tests = Cls_DesignValidation.Run(await _svc.DefaultsAsync());
        return Ok(new { passed = tests.Count(t => t.Pass), total = tests.Count, tests = tests.Select(t => new { t.Name, t.Formula, t.Inputs, t.Expected, t.Got, t.Tol, t.Pass }) });
    }
}
