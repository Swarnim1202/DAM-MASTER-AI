using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using DamMaster.Api.Common;
using DamMaster.Api.DAL;

namespace DamMaster.Api.Engine;

public class RefDesignInput { public string InputKey { get; set; } = ""; public string Label { get; set; } = ""; public string? Unit { get; set; } public string Grp { get; set; } = ""; public string Kind { get; set; } = ""; public string DefaultJson { get; set; } = ""; public string? OptionsJson { get; set; } public string? Help { get; set; } public int SortOrder { get; set; } }
public class DesignProject { public string Id { get; set; } = ""; public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string? DamId { get; set; } public string? DamName { get; set; } public string Status { get; set; } = "Active"; public string InputsJson { get; set; } = "{}"; public string EaJson { get; set; } = "[]"; public string? CreatedBy { get; set; } public DateTime CreatedAt { get; set; } public DateTime? UpdatedAt { get; set; } public int StaleCount { get; set; } public int ApprovedCount { get; set; } public int CalcCount { get; set; } }
public class DesignCalcRow { public string ProjectId { get; set; } = ""; public string CalcCode { get; set; } = ""; public int Version { get; set; } public string Status { get; set; } = ""; public bool Stale { get; set; } public string? StaleKey { get; set; } public string? StaleReason { get; set; } public string RecordJson { get; set; } = ""; public string InputsHash { get; set; } = ""; public string RunBy { get; set; } = ""; public DateTime RunAt { get; set; } public string? CheckedBy { get; set; } public DateTime? CheckedAt { get; set; } public string? ApprovedBy { get; set; } public DateTime? ApprovedAt { get; set; } public string Maturity { get; set; } = "Preliminary"; }
public record CreateDesignProjectRequest(string Name, string? DamId, string? CopyFromId);
public record UpdateDesignInputsRequest(Dictionary<string, JsonElement> Changes);
public record UpdateEaRequest(List<double[]> Rows);
public record RunRequest(bool OnlyStale = true);
public record PullDamRequest(string DamId);

public class Cls_DesignDAL : Cls_DbBase
{
    public async Task<List<RefDesignInput>> CatalogAsync() { using var c = Conn(); return (await c.QueryAsync<RefDesignInput>("SELECT * FROM ref_design_input ORDER BY sort_order")).ToList(); }
    public async Task<List<double[]>> DefaultEaAsync() { using var c = Conn(); return JsonSerializer.Deserialize<List<double[]>>(await c.ExecuteScalarAsync<string>("SELECT value_json FROM ref_design_setting WHERE setting_key='default_ea'") ?? "[]")!; }

    private const string SelP = @"SELECT p.*, d.name AS DamName,
        (SELECT COUNT(*) FROM design_calc c WHERE c.project_id=p.id AND c.stale=1) AS StaleCount,
        (SELECT COUNT(*) FROM design_calc c WHERE c.project_id=p.id AND c.status='Approved' AND c.stale=0) AS ApprovedCount,
        (SELECT COUNT(*) FROM design_calc c WHERE c.project_id=p.id) AS CalcCount FROM design_project p LEFT JOIN dam d ON d.id=p.dam_id ";
    public async Task<List<DesignProject>> ListAsync() { using var c = Conn(); return (await c.QueryAsync<DesignProject>(SelP + "ORDER BY p.status, p.created_at DESC")).ToList(); }
    public async Task<DesignProject?> GetAsync(string id) { using var c = Conn(); return await c.QueryFirstOrDefaultAsync<DesignProject>(SelP + "WHERE p.id=@id", new { id }); }
    public async Task<int> CountAsync() { using var c = Conn(); return await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM design_project"); }
    public async Task InsertAsync(DesignProject p) { using var c = Conn(); await c.ExecuteAsync("INSERT INTO design_project (id, code, name, dam_id, status, inputs_json, ea_json, created_by, created_at) VALUES (@Id, @Code, @Name, @DamId, 'Active', @InputsJson, @EaJson, @CreatedBy, UTC_TIMESTAMP())", p); }
    public async Task SaveInputsAsync(string id, string inputs, string ea) { using var c = Conn(); await c.ExecuteAsync("UPDATE design_project SET inputs_json=@inputs, ea_json=@ea, updated_at=UTC_TIMESTAMP() WHERE id=@id", new { id, inputs, ea }); }
    public async Task UpdateMetaAsync(string id, string name, string? damId, string status) { using var c = Conn(); await c.ExecuteAsync("UPDATE design_project SET name=@name, dam_id=@damId, status=@status, updated_at=UTC_TIMESTAMP() WHERE id=@id", new { id, name, damId, status }); }

    public async Task<List<DesignCalcRow>> CalcsAsync(string projectId) { using var c = Conn(); return (await c.QueryAsync<DesignCalcRow>("SELECT * FROM design_calc WHERE project_id=@projectId", new { projectId })).ToList(); }
    public async Task SaveCalcAsync(string projectId, string code, int version, string record, string hash, string inputsJson, string by)
    {
        using var c = Conn(); await c.OpenAsync(); using var tx = await c.BeginTransactionAsync();
        await c.ExecuteAsync(@"INSERT INTO design_calc (project_id, calc_code, version, status, stale, stale_key, stale_reason, record_json, inputs_hash, run_by, run_at, maturity)
            VALUES (@projectId, @code, @version, 'Draft', 0, NULL, NULL, @record, @hash, @by, UTC_TIMESTAMP(), 'Preliminary')
            ON DUPLICATE KEY UPDATE version=@version, status='Draft', stale=0, stale_key=NULL, stale_reason=NULL, record_json=@record, inputs_hash=@hash, run_by=@by, run_at=UTC_TIMESTAMP(),
            checked_by=NULL, checked_at=NULL, approved_by=NULL, approved_at=NULL", new { projectId, code, version, record, hash, by }, tx);
        await c.ExecuteAsync("INSERT INTO design_calc_history (project_id, calc_code, version, record_json, inputs_json, run_by, run_at) VALUES (@projectId, @code, @version, @record, @inputsJson, @by, UTC_TIMESTAMP())", new { projectId, code, version, record, inputsJson, by }, tx);
        await tx.CommitAsync();
    }
    public async Task ClearStaleAsync(string projectId, string code) { using var c = Conn(); await c.ExecuteAsync("UPDATE design_calc SET stale=0, stale_key=NULL, stale_reason=NULL WHERE project_id=@projectId AND calc_code=@code", new { projectId, code }); }
    public async Task MarkStaleAsync(string projectId, IEnumerable<string> codes, string key, string reason)
    { using var c = Conn(); await c.ExecuteAsync("UPDATE design_calc SET stale=1, stale_key=@key, stale_reason=@reason WHERE project_id=@projectId AND calc_code IN @codes", new { projectId, codes = codes.ToList(), key, reason = reason.Length > 290 ? reason[..290] : reason }); }
    public async Task<int> SetStatusAsync(string projectId, string code, string status, string by)
    {
        using var c = Conn();
        return status == "Checked"
            ? await c.ExecuteAsync("UPDATE design_calc SET status='Checked', checked_by=@by, checked_at=UTC_TIMESTAMP() WHERE project_id=@projectId AND calc_code=@code AND status='Draft' AND stale=0", new { projectId, code, by })
            : await c.ExecuteAsync("UPDATE design_calc SET status='Approved', approved_by=@by, approved_at=UTC_TIMESTAMP() WHERE project_id=@projectId AND calc_code=@code AND status='Checked' AND stale=0", new { projectId, code, by });
    }
    public async Task<IEnumerable<dynamic>> HistoryAsync(string projectId, string code) { using var c = Conn(); return await c.QueryAsync("SELECT version, run_by AS runBy, run_at AS runAt FROM design_calc_history WHERE project_id=@projectId AND calc_code=@code ORDER BY version DESC", new { projectId, code }); }
    public async Task LogChangeAsync(string projectId, string key, string? oldV, string? newV, string affected, string by)
    { using var c = Conn(); await c.ExecuteAsync("INSERT INTO design_input_change (project_id, input_key, old_value, new_value, affected, changed_by, changed_at) VALUES (@projectId, @key, @oldV, @newV, @affected, @by, UTC_TIMESTAMP())", new { projectId, key, oldV, newV, affected, by }); }
    public async Task<IEnumerable<dynamic>> ChangesAsync(string projectId) { using var c = Conn(); return await c.QueryAsync("SELECT input_key AS inputKey, old_value AS oldValue, new_value AS newValue, affected, changed_by AS changedBy, changed_at AS changedAt FROM design_input_change WHERE project_id=@projectId ORDER BY id DESC LIMIT 300", new { projectId }); }
    public async Task<HashSet<string>> VerifiedStandardsAsync() { using var c = Conn(); return (await c.QueryAsync<string>("SELECT code FROM ref_standard WHERE verified_by IS NOT NULL AND edition IS NOT NULL AND edition<>''")).ToHashSet(); }
    public async Task<Dictionary<string, string>> StandardTitlesAsync() { using var c = Conn(); return (await c.QueryAsync<(string code, string title)>("SELECT code, title FROM ref_standard")).ToDictionary(x => x.code, x => x.title); }
}

/// <summary>Design workspace business logic: inputs with consistency rules, STALE propagation, versioned runs, check / approve with segregation of duties and code verification.</summary>
public class Cls_DesignService
{
    private static readonly JsonSerializerOptions Js = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly Cls_DesignDAL _dal = new();

    public static object? FromJson(JsonElement e) => e.ValueKind switch { JsonValueKind.Number => e.GetDouble(), JsonValueKind.String => e.GetString(), JsonValueKind.Null => null, _ => e.ToString() };

    public async Task<DesignInputs> DefaultsAsync()
    {
        var i = new DesignInputs { Ea = await _dal.DefaultEaAsync() };
        foreach (var x in await _dal.CatalogAsync()) i.V[x.InputKey] = FromJson(JsonDocument.Parse(x.DefaultJson).RootElement);
        return i;
    }

    public static DesignInputs Load(DesignProject p) => new()
    {
        V = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(p.InputsJson)!.ToDictionary(kv => kv.Key, kv => FromJson(kv.Value)),
        Ea = JsonSerializer.Deserialize<List<double[]>>(p.EaJson)!
    };

    public async Task<DesignProject> CreateAsync(CreateDesignProjectRequest req, string actor)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) throw new BusinessException("Project name is required.");
        DesignInputs i;
        if (req.CopyFromId != null) i = Load(await _dal.GetAsync(req.CopyFromId) ?? throw new BusinessException("Project to copy not found."));
        else i = await DefaultsAsync();
        var p = new DesignProject { Id = Ids.New(), Code = $"DP-{await _dal.CountAsync() + 1:000}", Name = req.Name.Trim(), DamId = string.IsNullOrWhiteSpace(req.DamId) ? null : req.DamId, InputsJson = JsonSerializer.Serialize(i.V), EaJson = JsonSerializer.Serialize(i.Ea), CreatedBy = actor };
        await _dal.InsertAsync(p);
        await RunAsync(p.Id, actor, false);
        return p;
    }

    /// <summary>Applies input changes after type, option and consistency checks; flags every dependent calculation STALE.</summary>
    public async Task<object> UpdateInputsAsync(string projectId, Dictionary<string, object?> changes, List<double[]>? ea, string actor)
    {
        var p = await _dal.GetAsync(projectId) ?? throw new BusinessException("Project not found.");
        var cat = (await _dal.CatalogAsync()).ToDictionary(x => x.InputKey);
        var i = Load(p); var applied = new List<(string key, string? oldV, string? newV)>();
        foreach (var (k, raw) in changes)
        {
            if (!cat.TryGetValue(k, out var def)) throw new BusinessException($"Unknown input '{k}'.");
            object? v = raw is JsonElement je ? FromJson(je) : raw;
            if (def.Kind == "number")
            {
                double d;
                if (v is double dd) d = dd; else if (!double.TryParse(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) throw new BusinessException($"{def.Label}: enter a number.");
                if (double.IsNaN(d) || double.IsInfinity(d)) throw new BusinessException($"{def.Label}: enter a number.");
                v = d;
            }
            else if (def.Kind == "select")
            {
                var opts = JsonSerializer.Deserialize<List<string>>(def.OptionsJson!)!;
                if (!opts.Contains(Convert.ToString(v) ?? "")) throw new BusinessException($"{def.Label} must be one of: {string.Join(", ", opts)}.");
            }
            var old = i.V.GetValueOrDefault(k);
            if (Equals(Convert.ToString(old, System.Globalization.CultureInfo.InvariantCulture), Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture))) continue;
            i.V[k] = v; applied.Add((k, Convert.ToString(old, System.Globalization.CultureInfo.InvariantCulture), Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)));
        }
        if (ea != null && JsonSerializer.Serialize(ea.OrderBy(r => r[0])) != JsonSerializer.Serialize(i.Ea.OrderBy(r => r[0])))
        { i.Ea = ea.OrderBy(r => r[0]).ToList(); applied.Add(("ea", $"{p.EaJson.Length} chars", $"{ea.Count} rows")); }
        if (applied.Count == 0) return new { changed = Array.Empty<string>(), stale = Array.Empty<string>() };
        var bad = DesignCalcs.Inconsistency(i);
        if (bad != null) throw new BusinessException("Not applied: " + bad + " (FR-DB-02).");
        await _dal.SaveInputsAsync(projectId, JsonSerializer.Serialize(i.V), JsonSerializer.Serialize(i.Ea));
        var allStale = new HashSet<string>();
        foreach (var (k, o, n) in applied)
        {
            var dep = DesignCalcs.Dependents(k); var label = k == "ea" ? "Elevation–area table" : cat[k].Label;
            var reason = k == "ea" ? "Elevation–area table changed" : $"{label} changed {o} → {n}";
            if (dep.Count > 0) await _dal.MarkStaleAsync(projectId, dep, k, reason);
            await _dal.LogChangeAsync(projectId, k, o, n, string.Join(", ", dep), actor);
            allStale.UnionWith(dep);
        }
        await new Cls_AuditDAL().WriteAsync(null, actor, "Design", $"{p.Code}: {applied.Count} input(s) changed ({string.Join(", ", applied.Select(a => a.key))}); {allStale.Count} calculation(s) STALE", "design_project", projectId);
        return new { changed = applied.Select(a => a.key), stale = DesignCalcs.Order.Where(allStale.Contains) };
    }

    /// <summary>Runs the calculations; a new version is stored when the calculation is new, STALE, or (full run) its result changed.</summary>
    public async Task<List<string>> RunAsync(string projectId, string actor, bool onlyStale)
    {
        var p = await _dal.GetAsync(projectId) ?? throw new BusinessException("Project not found.");
        var i = Load(p); var records = DesignCalcs.RunAll(i);
        var existing = (await _dal.CalcsAsync(projectId)).ToDictionary(c => c.CalcCode);
        var written = new List<string>();
        foreach (var code in DesignCalcs.Order)
        {
            var json = JsonSerializer.Serialize(records[code], Js); var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
            existing.TryGetValue(code, out var cur);
            var write = cur == null || cur.Stale || (!onlyStale && cur.InputsHash != hash);
            if (!write) continue;
            if (cur != null && cur.Stale && cur.InputsHash == hash && onlyStale) { await _dal.ClearStaleAsync(projectId, code); written.Add(code + " (unchanged)"); continue; }
            await _dal.SaveCalcAsync(projectId, code, (cur?.Version ?? 0) + 1, json, hash, p.InputsJson, actor);
            written.Add(code);
        }
        if (written.Count > 0) await new Cls_AuditDAL().WriteAsync(null, actor, "Design", $"{p.Code}: ran {string.Join(", ", written)}", "design_project", projectId);
        return written;
    }

    public async Task SetStatusAsync(string projectId, string code, string target, string actor)
    {
        var c = (await _dal.CalcsAsync(projectId)).FirstOrDefault(x => x.CalcCode == code) ?? throw new BusinessException("Calculation not found.");
        if (c.Stale) throw new BusinessException("Blocked: the calculation is STALE — run it again first (FR-VC-02).");
        if (target == "Checked")
        {
            if (c.Status != "Draft") throw new BusinessException("Only a Draft calculation can be checked.");
            if (c.RunBy == actor) throw new BusinessException("Segregation of duties: you ran this calculation, so another person must check it.");
        }
        else
        {
            if (c.Status != "Checked") throw new BusinessException("The calculation must be checked before approval.");
            if (c.RunBy == actor || c.CheckedBy == actor) throw new BusinessException("Segregation of duties: the approver must differ from the person who ran and the person who checked the calculation.");
            var rec = JsonSerializer.Deserialize<CalcRecord>(c.RecordJson, Js)!; var ok = await _dal.VerifiedStandardsAsync(); var titles = await _dal.StandardTitlesAsync();
            var missing = rec.Codes.Where(x => !ok.Contains(x)).ToList();
            if (missing.Count > 0) throw new BusinessException("CODE VERIFICATION REQUIRED before approval: " + string.Join("; ", missing.Select(m => titles.GetValueOrDefault(m, m))) + ". Verify edition and clauses in Reference library → Codes & standards (FR-CODE-02).");
        }
        if (await _dal.SetStatusAsync(projectId, code, target, actor) == 0) throw new BusinessException("Status could not be changed (it may have just changed).");
        await new Cls_AuditDAL().WriteAsync(null, actor, "Design", $"{DesignCalcs.Titles[code]} v{c.Version} → {target}", "design_project", projectId);
    }
}

/// <summary>Validation suite: the C# engine against independent hand / Python calculations (same cases as the prototype's 37 tests).</summary>
public static class Cls_DesignValidation
{
    public record Test(string Name, string Formula, string Inputs, double Expected, double Got, double Tol) { public bool Pass => Math.Abs(Got - Expected) <= Tol; }

    public static List<Test> Run(DesignInputs D)
    {
        var t = new List<Test>();
        void T(string n, string f, string inp, double exp, double got, double tol) => t.Add(new Test(n, f, inp, exp, got, tol));
        var g = DesignMath.Grav(74, 74, 58, 8, 24, .75, 400, .67, 0);
        T("Molitor wave height", "Wave height and freeboard", "V 120 km/h, F 8 km", 1.2987178795615857, DesignMath.Wave(120, 8), 1e-6);
        T("Seismic coefficient zone III", "Seismic coefficient", "Z 0.16, I 1.5", 0.12, DesignMath.Ah("III"), 1e-12);
        T("Sliding FS (shear friction)", "Gravity sliding", "H=h 74, B 58, T 8, γc 24, μ .75, c 400, u .67", 2.10639250768249, g.FsSF, 1e-6);
        T("Overturning FS", "Gravity overturning", "same section", 1.8603913060620842, g.FsOT, 1e-6);
        T("Toe pressure", "Base pressure", "same section", 1215.4909155766943, g.SToe, 1e-4);
        T("Heel pressure", "Base pressure", "same section", 319.0948016646847, g.SHeel, 1e-4);
        T("Infinite-slope FS (k = 0)", "Embankment slope", "φ 34°, n 2.5", 2.5 * Math.Tan(34 * Math.PI / 180), DesignMath.SlopeFS(Math.Tan(34 * Math.PI / 180), 0, 2.5), 1e-9);
        var sp = DesignMath.Spill(D.With("Q", 12000.0).With("mwl", 673.0).With("spc", 658.0).With("Cd", 2.2).With("bay", 15.0).With("pier", 3.0));
        T("Ogee crest length", "Spillway", "Q 12 000, He 15, C 2.2", 93.89050536260405, sp.Ls, 1e-6);
        T("Spillway gross width", "Spillway", "7 × 15 + 6 × 3", 123, sp.Gross, 1e-9);
        var R = DesignCalcs.RunAll(D); var basin = JsonDocument.Parse(JsonSerializer.Serialize(R["basin"].Data)).RootElement;
        T("Froude number F1", "Stilling basin", "Q 12 000, 7 × 15 m, MWL 673, floor 600", 5.725851484807773, basin.GetProperty("F1").GetDouble(), 1e-6);
        T("Sequent depth y2", "Stilling basin", "same", 26.16780775973968, basin.GetProperty("y2").GetDouble(), 1e-6);
        var gate = JsonDocument.Parse(JsonSerializer.Serialize(R["gate"].Data)).RootElement;
        T("Gate hydrostatic thrust", "Radial gate", "h 12, b 15", 10594.8, gate.GetProperty("P").GetDouble(), 1e-6);
        T("Hoisting force", "Hoist", "W 1100 kN, gate height 12.5", 1381.026048, gate.GetProperty("F").GetDouble(), 1e-6);
        T("Motor power per gate", "Hoist", "v 0.3 m/min, η 0.85", 8.123682635294118, gate.GetProperty("kW").GetDouble(), 1e-9);
        var hp = JsonDocument.Parse(JsonSerializer.Serialize(R["hp"].Data)).RootElement;
        T("Net head", "Hydropower", "Q 120, D 5.5, L 250, f .012", 55.440360334749, hp.GetProperty("Hn").GetDouble(), 1e-6);
        T("Installed capacity", "Hydropower", "η 0.9", 58.737952967459876, hp.GetProperty("MW").GetDouble(), 1e-6);
        T("Water hammer (Michaud)", "Water hammer", "Tc 10 s", 25.743463891326865, hp.GetProperty("dh").GetDouble(), 1e-6);
        T("Penstock thickness", "Penstock", "σa 150 MPa, ηj 0.9, c 2 mm", 20.733068867616822, hp.GetProperty("t").GetDouble(), 1e-6);
        T("Water starting time", "Penstock", "L 250, Hn 55.4", 2.3217258812792507, hp.GetProperty("Tw").GetDouble(), 1e-6);
        var jou = DesignCalcs.RunAll(D.With("Tc", 0.1).With("Qd", 4 * Math.PI * 5.5 * 5.5 / 4))["hp"].Data; var jd = JsonDocument.Parse(JsonSerializer.Serialize(jou)).RootElement;
        T("Joukowsky rise", "Water hammer", "a 1000, ΔV 4 (instant closure)", 407.747, jd.GetProperty("dh").GetDouble(), 1e-3);
        var rt = DesignMath.Route(D, 7);
        T("Routed peak outflow", "Flood routing", "Qp 15 000, tp 12 h, tb 36 h, 7 × 15 m", 11836.6334147975, rt.pkO, 11836.6334147975 * 0.01);
        T("Routed maximum level", "Flood routing", "same", 671.7957128153877, rt.maxH, 0.05);
        T("Mass balance error (%)", "Flood routing", "same — must be < 1", 0, rt.err, 1);
        T("Small flood held at FRL", "Flood routing", "Qp 5 000", 670, DesignMath.Route(D.With("Qp", 5000.0), 7).maxH, 1e-6);
        var cost = JsonDocument.Parse(JsonSerializer.Serialize(R["cost"].Data)).RootElement;
        var direct = cost.GetProperty("direct").GetDouble(); double ind = direct * .12, cont = (direct + ind) * .10, esc = (direct + ind + cont) * .05, tax = (direct + ind + cont + esc) * .12;
        T("Cost build-up (12/10/5/12 %)", "Cost", "direct from design", direct + ind + cont + esc + tax, cost.GetProperty("total").GetDouble(), 1e-3);
        T("Cost build-up on ₹100 cr", "Cost", "hand calculation", 144.8832, 100 * 1.12 * 1.10 * 1.05 * 1.12, 1e-4);
        return t;
    }
}
