using System.Globalization;
using DamMaster.Api.Common;
using DamMaster.Api.DAL;
using DamMaster.Api.Models;

namespace DamMaster.Api.Engine;

/// <summary>Pure statistics helpers (unit-testable, no I/O).</summary>
public static class Cls_Stats
{
    public static double Mean(IReadOnlyList<double> x) => x.Average();

    public static double SampleSd(IReadOnlyList<double> x)
    {
        if (x.Count < 2) return 0;
        var m = Mean(x);
        return Math.Sqrt(x.Sum(v => (v - m) * (v - m)) / (x.Count - 1));
    }

    /// <summary>Least-squares line y = a + b·x. Returns slope b and intercept a.</summary>
    public static (double b, double a) Ols(IReadOnlyList<double> xs, IReadOnlyList<double> ys)
    {
        var mx = xs.Average(); var my = ys.Average(); double n = 0, d = 0;
        for (var i = 0; i < xs.Count; i++) { n += (xs[i] - mx) * (ys[i] - my); d += (xs[i] - mx) * (xs[i] - mx); }
        var b = d == 0 ? 0 : n / d;
        return (b, my - b * mx);
    }

    public static double Pearson(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        var ma = a.Average(); var mb = b.Average(); double s = 0, da = 0, db = 0;
        for (var i = 0; i < a.Count; i++) { s += (a[i] - ma) * (b[i] - mb); da += (a[i] - ma) * (a[i] - ma); db += (b[i] - mb) * (b[i] - mb); }
        return da == 0 || db == 0 ? 0 : s / Math.Sqrt(da * db);
    }

    /// <summary>Threshold level 0–3 of a value for a rising or falling sensor.</summary>
    public static int Level(double v, SensorThreshold? t, string direction)
    {
        if (t == null) return 0;
        var up = !string.Equals(direction, "Falling", StringComparison.OrdinalIgnoreCase);
        bool Hit(double? lim) => lim.HasValue && (up ? v >= lim.Value : v <= lim.Value);
        return Hit(t.EmergencyValue) ? 3 : Hit(t.AlarmValue) ? 2 : Hit(t.AlertValue) ? 1 : 0;
    }
}

// =====================================================================
/// <summary>
/// Validates imported or telemetry readings (FRS-001 §5–6), assigns quality flags G/S/B,
/// and on commit writes masters, readings, quarantine and the import record, then runs the alert engine.
/// </summary>
public class Cls_QualityEngine
{
    private static readonly TimeSpan Ist = TimeSpan.FromHours(5.5);

    public static DateTime? ParseObserved(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        var hasOffset = s.EndsWith("Z", StringComparison.OrdinalIgnoreCase) || System.Text.RegularExpressions.Regex.IsMatch(s, @"[+-]\d{2}:?\d{2}$");
        if (hasOffset && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto)) return dto.UtcDateTime;
        string[] fmts = { "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "dd-MM-yyyy HH:mm", "dd/MM/yyyy HH:mm", "yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy" };
        if (DateTime.TryParseExact(s, fmts, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return new DateTimeOffset(local, Ist).UtcDateTime;   // text without offset is IST wall-clock time
        return null;
    }

    private static string Norm(string? s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    public async Task<ImportResult> ProcessAsync(ImportRequest req, string source, string actor, string? actorId)
    {
        var res = new ImportResult { RowsRead = req.Readings.Count };
        var refDal = new Cls_ReferenceDAL(); var damDal = new Cls_DamDAL(); var senDal = new Cls_SensorDAL(); var rdDal = new Cls_ReadingDAL();
        var parameters = (await refDal.ParametersAsync()).ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);
        var damTypes = await refDal.DamTypesAsync(); var hazards = await refDal.HazardClassesAsync();
        void Issue(string sheet, int row, string? code, string outcome, string rule, string msg) =>
            res.Issues.Add(new ImportIssue { Sheet = sheet, RowNo = row, ItemCode = code, Outcome = outcome, RuleCode = rule, Message = msg });

        // ---------------- Dam_Master ----------------
        var damsByCode = (await damDal.ListAsync()).ToDictionary(d => d.Code, StringComparer.OrdinalIgnoreCase);
        var damUpserts = new List<(Dam dam, bool isNew)>();
        for (var i = 0; i < req.Dams.Count; i++)
        {
            var r = req.Dams[i]; var row = i + 2;
            if (string.IsNullOrWhiteSpace(r.DamId) || string.IsNullOrWhiteSpace(r.DamName)) { Issue("Dam_Master", row, r.DamId, "Rejected", "Mandatory", "dam_id and dam_name are required"); continue; }
            var t = damTypes.FirstOrDefault(x => Norm(x.Code) == Norm(r.DamType)) ?? damTypes.FirstOrDefault(x => Norm(r.DamType).Length > 3 && Norm(x.Name).StartsWith(Norm(r.DamType)))
                    ?? damTypes.FirstOrDefault(x => Norm(r.DamType).Length > 3 && Norm(x.Name).Contains(Norm(r.DamType)));
            if (t == null) { Issue("Dam_Master", row, r.DamId, "Rejected", "Reference", $"Unknown dam_type '{r.DamType}'. Use a code or name from the Dam types catalogue."); continue; }
            var h = hazards.FirstOrDefault(x => Norm(x.Code) == Norm(r.HazardClass) || Norm(x.Name) == Norm(r.HazardClass));
            if (h == null) { Issue("Dam_Master", row, r.DamId, "Warning", "Reference", $"hazard_class '{r.HazardClass}' not recognised — set to High"); h = hazards.First(x => x.Code == "HIGH"); }
            if (r.MwlM.HasValue && r.FrlM.HasValue && r.MwlM < r.FrlM) Issue("Dam_Master", row, r.DamId, "Warning", "Levels", "MWL is below FRL");
            var isNew = !damsByCode.TryGetValue(r.DamId.Trim(), out var d);
            d ??= new Dam { Id = Ids.New(), Code = r.DamId.Trim() };
            d.Name = r.DamName!.Trim(); d.State = r.State ?? d.State; d.DamTypeCode = t.Code; d.HazardCode = h.Code; d.SeismicZone = r.SeismicZone ?? d.SeismicZone;
            d.HeightM = (decimal?)r.HeightM ?? d.HeightM; d.FrlM = (decimal?)r.FrlM ?? d.FrlM; d.MwlM = (decimal?)r.MwlM ?? d.MwlM; d.MddlM = (decimal?)r.MddlM ?? d.MddlM; d.CrestLevelM = (decimal?)r.CrestLevelM ?? d.CrestLevelM;
            d.IsSpecified = (r.HeightM ?? (double?)d.HeightM ?? 0) > 15 || d.IsSpecified;
            damsByCode[d.Code] = d; damUpserts.Add((d, isNew));
        }
        res.DamsUpserted = damUpserts.Count;

        // ---------------- Sensor_Master ----------------
        var sensorCodes = req.Sensors.Select(s => s.SensorId ?? "").Concat(req.Readings.Select(r => r.SensorId ?? "")).Where(s => s != "");
        var sensorsByCode = await senDal.ByCodesAsync(sensorCodes);
        var sensorUpserts = new List<(Sensor s, bool isNew, SensorThreshold? th)>();
        for (var i = 0; i < req.Sensors.Count; i++)
        {
            var r = req.Sensors[i]; var row = i + 2;
            if (string.IsNullOrWhiteSpace(r.SensorId) || string.IsNullOrWhiteSpace(r.DamId) || string.IsNullOrWhiteSpace(r.ParameterCode) || string.IsNullOrWhiteSpace(r.EngUnit))
            { Issue("Sensor_Master", row, r.SensorId, "Rejected", "Mandatory", "sensor_id, dam_id, parameter_code and eng_unit are required"); continue; }
            if (!parameters.TryGetValue(r.ParameterCode.Trim(), out var prm)) { Issue("Sensor_Master", row, r.SensorId, "Rejected", "Reference", $"Unknown parameter_code {r.ParameterCode}"); continue; }
            if (!damsByCode.TryGetValue(r.DamId.Trim(), out var dam)) { Issue("Sensor_Master", row, r.SensorId, "Rejected", "Master", $"dam_id {r.DamId} not found"); continue; }
            var dir = string.Equals(r.Direction, "Falling", StringComparison.OrdinalIgnoreCase) ? "Falling" : "Rising";
            var lv = new[] { r.AlertLevel, r.AlarmLevel, r.EmergencyLevel }.Where(x => x.HasValue).Select(x => x!.Value).ToList();
            var ordered = lv.Zip(lv.Skip(1), (a, b) => dir == "Rising" ? b > a : b < a).All(x => x);
            if (!ordered) Issue("Sensor_Master", row, r.SensorId, "Warning", "FR-MD-04", $"Threshold levels not in order for direction {dir}");
            var isNew = !sensorsByCode.TryGetValue(r.SensorId.Trim(), out var s);
            if (!isNew && s!.DamId != dam.Id) { Issue("Sensor_Master", row, r.SensorId, "Rejected", "Master", "Sensor already registered to another dam"); continue; }
            s ??= new Sensor { Id = Ids.New(), Code = r.SensorId.Trim(), DamId = dam.Id };
            s.ParameterCode = prm.Code; s.InstrumentTypeCode = r.InstrumentType ?? s.InstrumentTypeCode; s.Location = r.Location ?? s.Location; s.ElevationM = (decimal?)r.ElevationM ?? s.ElevationM;
            s.EngUnit = r.EngUnit.Trim(); s.ExpectedMin = r.ExpectedMin; s.ExpectedMax = r.ExpectedMax; s.Direction = dir; s.ReadingIntervalMin = r.ReadingIntervalMin ?? 360;
            s.CalibrationDue = ParseObserved(r.CalibrationDue)?.Date ?? s.CalibrationDue; s.Status = string.IsNullOrWhiteSpace(r.Status) ? "Active" : r.Status!.Trim();
            s.Latitude = (decimal?)r.Latitude ?? s.Latitude; s.Longitude = (decimal?)r.Longitude ?? s.Longitude; s.OffsetM = r.OffsetM ?? s.OffsetM; s.ChainageM = r.ChainageM ?? s.ChainageM;
            SensorThreshold? th = null;
            if (lv.Count > 0)
            {
                var cur = isNew ? null : await senDal.CurrentThresholdAsync(s.Id);
                if (cur == null || cur.AlertValue != r.AlertLevel || cur.AlarmValue != r.AlarmLevel || cur.EmergencyValue != r.EmergencyLevel)
                    th = new SensorThreshold { Id = Ids.New(), SensorId = s.Id, AlertValue = r.AlertLevel, AlarmValue = r.AlarmLevel, EmergencyValue = r.EmergencyLevel,
                        ZLimit = (decimal)AppConfig.DefaultZLimit, Persistence = 2, Method = "Design", Basis = $"Imported from {req.FileName ?? source}", CreatedBy = actor };
            }
            sensorsByCode[s.Code] = s; sensorUpserts.Add((s, isNew, th));
        }
        res.SensorsUpserted = sensorUpserts.Count;

        // ---------------- Readings ----------------
        var now = DateTime.UtcNow; var seen = new HashSet<string>(); var accepted = new List<(Reading r, int row)>();
        var quarantine = new List<(string?, string?, string?, DateTime?, double?, string?)>();
        var existingCache = new Dictionary<string, HashSet<DateTime>>();
        var parsed = req.Readings.Select((r, i) => (r, row: i + 2, t: ParseObserved(r.ObservedAt))).ToList();
        foreach (var grp in parsed.Where(p => p.t.HasValue && p.r.SensorId != null && sensorsByCode.ContainsKey(p.r.SensorId)).GroupBy(p => p.r.SensorId!, StringComparer.OrdinalIgnoreCase))
        {
            var s = sensorsByCode[grp.Key];
            existingCache[s.Id] = await rdDal.ExistingTimesAsync(s.Id, grp.Min(p => p.t!.Value), grp.Max(p => p.t!.Value));
        }
        foreach (var (r, row, t) in parsed)
        {
            void Rej(string rule, string msg) { res.Rejected++; Issue("Readings", row, r.SensorId, "Rejected", rule, msg); }
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(r.DamId)) missing.Add("dam_id"); if (string.IsNullOrWhiteSpace(r.SensorId)) missing.Add("sensor_id");
            if (string.IsNullOrWhiteSpace(r.ParameterCode)) missing.Add("parameter_code"); if (string.IsNullOrWhiteSpace(r.ObservedAt)) missing.Add("observed_at");
            if (!r.Value.HasValue) missing.Add("value"); if (string.IsNullOrWhiteSpace(r.Unit)) missing.Add("unit");
            if (missing.Count > 0) { Rej("Mandatory", "Missing " + string.Join(", ", missing)); continue; }
            if (!sensorsByCode.TryGetValue(r.SensorId!.Trim(), out var s))
            { res.Quarantined++; Issue("Readings", row, r.SensorId, "Quarantined", "FR-ING-05", "Sensor not registered"); quarantine.Add((r.DamId, r.SensorId, r.ParameterCode, t, r.Value, r.Unit)); continue; }
            var dam = damsByCode.Values.FirstOrDefault(d => d.Id == s.DamId);
            if (dam != null && !string.Equals(dam.Code, r.DamId!.Trim(), StringComparison.OrdinalIgnoreCase)) { Rej("Master", $"dam_id {r.DamId} does not match the sensor's dam {dam.Code}"); continue; }
            if (!string.Equals(s.ParameterCode, r.ParameterCode!.Trim(), StringComparison.OrdinalIgnoreCase)) { Rej("Master", $"parameter_code {r.ParameterCode} does not match the sensor ({s.ParameterCode})"); continue; }
            if (!string.Equals(s.EngUnit, r.Unit!.Trim(), StringComparison.OrdinalIgnoreCase)) { Rej("QC-07 unit", $"unit {r.Unit} ≠ sensor unit {s.EngUnit}"); continue; }
            if (!t.HasValue) { Rej("QC-04 time", "observed_at not readable"); continue; }
            if (t.Value > now.AddMinutes(5)) { Rej("QC-04 time", $"observed_at is in the future ({t:yyyy-MM-dd HH:mm} UTC)"); continue; }
            var key = s.Id + "|" + t.Value.Ticks;
            if ((existingCache.TryGetValue(s.Id, out var ex) && ex.Contains(t.Value)) || !seen.Add(key))
            { res.Duplicates++; Issue("Readings", row, r.SensorId, "Skipped", "FR-ING-03", $"Duplicate reading at {t:yyyy-MM-dd HH:mm} UTC"); continue; }
            var q = "G"; string? rule = null; var ls = (r.LoggerStatus ?? "OK").Trim().ToUpperInvariant();
            if (ls != "OK" && ls != "MANUAL" && ls != "") { q = "B"; rule = "QC-05 logger " + ls; }
            else if ((s.ExpectedMin.HasValue && r.Value < s.ExpectedMin) || (s.ExpectedMax.HasValue && r.Value > s.ExpectedMax)) { q = "B"; rule = "QC-01 range"; }
            accepted.Add((new Reading { SensorId = s.Id, ObservedAt = t.Value, Value = r.Value!.Value, RawValue = r.RawValue, Quality = q, QcRule = rule }, row));
        }

        // ---------------- QC-02 spike / QC-03 flatline on the merged series ----------------
        foreach (var grp in accepted.GroupBy(a => a.r.SensorId))
        {
            var s = sensorsByCode.Values.First(x => x.Id == grp.Key);
            if (parameters.TryGetValue(s.ParameterCode, out var prm) && prm.IsEvent) continue;
            var from = grp.Min(a => a.r.ObservedAt).AddDays(-10);
            var stored = await rdDal.SeriesAsync(s.Id, from, grp.Max(a => a.r.ObservedAt));
            var series = stored.Select(p => (t: p.T, v: p.V, bad: p.Q == "B", item: (Reading?)null))
                .Concat(grp.Select(a => (t: a.r.ObservedAt, v: a.r.Value, bad: a.r.Quality == "B", item: (Reading?)a.r)))
                .OrderBy(x => x.t).Where(x => !x.bad).ToList();
            // flatline: ≥ 12 identical consecutive values
            for (int i = 0; i < series.Count;)
            {
                var j = i; while (j + 1 < series.Count && series[j + 1].v == series[i].v) j++;
                if (j - i + 1 >= 12) for (var k = i; k <= j; k++) if (series[k].item != null) { series[k].item!.Quality = "S"; series[k].item!.QcRule = "QC-03 flatline"; }
                i = j + 1;
            }
            // isolated spike: jump > 5 × median step and immediate return
            var steps = new List<double>(); for (var k = 1; k < series.Count; k++) steps.Add(Math.Abs(series[k].v - series[k - 1].v));
            for (var k = 1; k < series.Count; k++)
            {
                var win = steps.Skip(Math.Max(0, k - 31)).Take(Math.Min(30, k - 1)).OrderBy(x => x).ToList();
                if (win.Count < 10) continue;
                var med = Math.Max(win[win.Count / 2], 1e-9); var up = series[k].v - series[k - 1].v;
                if (Math.Abs(up) <= 5 * med) continue;
                var back = k + 1 < series.Count ? series[k + 1].v - series[k].v : (double?)null;
                if (back == null || (Math.Sign(back.Value) != Math.Sign(up) && Math.Abs(back.Value) > 2.5 * med))
                    if (series[k].item is { Quality: "G" } it) { it.Quality = "S"; it.QcRule = "QC-02 spike"; }
            }
        }
        foreach (var (rd, row) in accepted)
        {
            if (rd.Quality == "G") res.Good++; else if (rd.Quality == "S") res.Suspect++; else res.Bad++;
            if (rd.Quality != "G") Issue("Readings", row, sensorsByCode.Values.First(x => x.Id == rd.SensorId).Code, "Flag " + rd.Quality, rd.QcRule ?? "", rd.Quality == "B" ? "Saved, excluded from analysis" : "Saved, used with lower confidence");
        }
        if (!req.Commit) return res;

        // ---------------- Commit ----------------
        var importId = Ids.New(); res.ImportId = importId; res.Committed = true;
        foreach (var (d, isNew) in damUpserts) { if (isNew) await damDal.InsertAsync(d); else await damDal.UpdateAsync(d); }
        foreach (var (s, isNew, th) in sensorUpserts) { if (isNew) await senDal.InsertAsync(s); else await senDal.UpdateAsync(s); if (th != null) await senDal.InsertThresholdAsync(th); }
        await rdDal.BulkInsertAsync(accepted.Select(a => a.r).ToList(), importId);
        foreach (var g in accepted.GroupBy(a => a.r.SensorId))                                          // Phase 4: daily aggregates
            await new Cls_AggregateDAL().RefreshAsync(g.Key, g.Min(a => a.r.ObservedAt), g.Max(a => a.r.ObservedAt));
        if (quarantine.Count > 0) await new Cls_ImportDAL().InsertQuarantineAsync(importId, quarantine);
        await new Cls_ImportDAL().InsertBatchAsync(importId, source, req.FileName, actor, res);
        await new Cls_AuditDAL().WriteAsync(actorId, actor, "Data import", $"{source} import {req.FileName}: {res.Good} G, {res.Suspect} S, {res.Bad} B, {res.Rejected} rejected, {res.Quarantined} quarantined, {res.Duplicates} duplicates", "import_batch", importId);

        var damIds = accepted.Select(a => sensorsByCode.Values.First(x => x.Id == a.r.SensorId).DamId).Concat(damUpserts.Select(d => d.dam.Id)).Distinct();
        var engine = new Cls_AlertEngine();
        foreach (var damId in damIds) res.AlertsRaised.AddRange(await engine.EvaluateDamAsync(damId, actor, actorId));
        return res;
    }
}

// =====================================================================
/// <summary>
/// Four-level alert engine (REF-001 §10): absolute thresholds with persistence and Level-3 confirmation,
/// statistical band (z-score), rate of change with acceleration, data/system health, and PFMA corroboration.
/// Alerts are never closed automatically — an engineer closes them with a disposition.
/// </summary>
public class Cls_AlertEngine
{
    private readonly Cls_AlertDAL _alerts = new();

    public async Task<List<AlertView>> EvaluateDamAsync(string damId, string actor, string? actorId)
    {
        var raised = new List<AlertView>();
        var sensors = (await new Cls_SensorDAL().ListByDamAsync(damId)).Where(s => s.Status != "Retired").ToList();
        var parameters = (await new Cls_ReferenceDAL().ParametersAsync()).ToDictionary(p => p.Code);
        var rd = new Cls_ReadingDAL(); var senDal = new Cls_SensorDAL();
        var latestBySensor = new Dictionary<string, DateTime?>();
        foreach (var s in sensors) latestBySensor[s.Id] = await rd.LatestTimeAsync(s.Id);
        var asOf = latestBySensor.Values.Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty(DateTime.MinValue).Max();
        if (asOf == DateTime.MinValue) return raised;
        var abnormal = new Dictionary<string, int>();   // sensorId → highest level raised this run (non-system)
        RwlLookup? rwl = null;

        foreach (var s in sensors)
        {
            var last = latestBySensor[s.Id];
            if (!last.HasValue) continue;
            parameters.TryGetValue(s.ParameterCode, out var prm);
            var th = await senDal.CurrentThresholdAsync(s.Id);
            var series = await rd.SeriesAsync(s.Id, last.Value.AddDays(-30), null, validOnly: true);

            // -- system health
            if (asOf - last.Value > TimeSpan.FromMinutes(3.0 * s.ReadingIntervalMin))
                await Raise(raised, damId, s, null, 1, "System", $"{s.Code}: no data since {last:yyyy-MM-dd HH:mm} UTC", $"More than 3 expected intervals ({s.ReadingIntervalMin} min) without a reading.", null, null, asOf);
            else if (await _alerts.FindOpenAsync(s.Id, null, "System") is { } stale)
                await _alerts.AutoResolveAsync(stale.Id, $"Data resumed ({last:yyyy-MM-dd HH:mm} UTC)");      // Phase 4: buffered / restored data closes the data-health alert
            if (s.CalibrationDue.HasValue && s.CalibrationDue.Value.Date < asOf.Date)
                await Raise(raised, damId, s, null, 1, "Calibration", $"{s.Code}: calibration overdue", $"Calibration was due on {s.CalibrationDue:yyyy-MM-dd}.", null, null, asOf);
            if (series.Count == 0) continue;
            var latest = series[^1];
            var persistence = Math.Max(1, th?.Persistence ?? 2);

            // -- 1. absolute thresholds with persistence; Level 3 needs two consecutive readings (confirmation)
            if (th != null && (th.AlertValue.HasValue || th.AlarmValue.HasValue || th.EmergencyValue.HasValue) && series.Count >= persistence)
            {
                var tail = series.TakeLast(Math.Max(persistence, 2)).Select(p => Cls_Stats.Level(p.V, th, s.Direction)).ToList();
                var lvl = tail.TakeLast(persistence).Min();
                if (lvl == 3 && tail.TakeLast(2).Min() < 3) lvl = 2;
                if (lvl > 0)
                {
                    var lim = lvl == 3 ? th.EmergencyValue : lvl == 2 ? th.AlarmValue : th.AlertValue;
                    var name = lvl == 3 ? "EMERGENCY" : lvl == 2 ? "ALARM" : "ALERT";
                    await Raise(raised, damId, s, null, lvl, "Threshold", $"{s.Code} {name}: {latest.V:0.###} {s.EngUnit} beyond {lim:0.###}",
                        $"{prm?.Name ?? s.ParameterCode} beyond the Level {lvl} value for {persistence} consecutive reading(s). Threshold method: {th.Method}{(th.ApprovedBy == null ? " (not yet approved)" : "")}.", latest.V, lim, latest.T);
                    abnormal[s.Id] = Math.Max(abnormal.GetValueOrDefault(s.Id), lvl);
                }
            }

            var isResponse = prm == null || (!prm.IsLoad && !prm.IsEvent);
            // -- 2a. HST residual (Phase 3): an approved behaviour model replaces the simple statistical band
            var hst = isResponse ? await new Cls_HstDAL().ActiveAsync(s.Id) : null;
            if (hst != null)
            {
                rwl ??= await RwlLookup.ForDamAsync(damId);
                var c = Cls_HstService.Coef(hst); var k = (double)hst.KLimit; var tail = series.TakeLast(persistence).ToList(); var allOut = tail.Count == persistence; double lastRes = 0, lastPred = 0;
                foreach (var p in tail)
                {
                    var h = rwl.At(p.T); if (!h.HasValue) { allOut = false; break; }
                    var pred = Cls_HstService.Predict(hst, c, p.T, h.Value); var res = p.V - pred;
                    if (Math.Abs(res) <= k * hst.Sigma) { allOut = false; break; }
                    lastRes = res; lastPred = pred;
                }
                if (allOut)
                {
                    await Raise(raised, damId, s, null, 1, "HST", $"{s.Code}: differs from its behaviour model by {lastRes:+0.###;-0.###} {s.EngUnit}",
                        $"Last {persistence} reading(s) outside the HST prediction ± {k}·σ (σ = {hst.Sigma:0.###}). Latest {latest.V:0.###} vs predicted {lastPred:0.###} for the current reservoir level and season ({hst.Variant}, R² {hst.R2:0.00}).", latest.V, lastPred + Math.Sign(lastRes) * k * hst.Sigma, latest.T);
                    abnormal[s.Id] = Math.Max(abnormal.GetValueOrDefault(s.Id), 1);
                }
            }
            // -- 2. statistical band: last P readings all outside mean ± z·σ of the previous WindowDays
            if (isResponse && hst == null)
            {
                var zLim = (double)(th?.ZLimit ?? (decimal)AppConfig.DefaultZLimit); var allOut = true; double zLast = 0, mLast = 0, sdLast = 0;
                for (var k = series.Count - persistence; k < series.Count; k++)
                {
                    if (k < 0) { allOut = false; break; }
                    var t = series[k].T; var win = series.Where(p => p.T < t && p.T >= t.AddDays(-AppConfig.WindowDays)).Select(p => p.V).ToList();
                    if (win.Count < 10) { allOut = false; break; }
                    var m = Cls_Stats.Mean(win); var sd = Math.Max(Cls_Stats.SampleSd(win), 1e-9); var z = (series[k].V - m) / sd;
                    if (Math.Abs(z) <= zLim) { allOut = false; break; }
                    zLast = z; mLast = m; sdLast = sd;
                }
                if (allOut)
                {
                    await Raise(raised, damId, s, null, 1, "Statistical", $"{s.Code}: unusual reading (z = {zLast:0.0})",
                        $"Last {persistence} reading(s) outside the expected band. Latest {latest.V:0.###} vs {AppConfig.WindowDays}-day mean {mLast:0.###} ± {zLim}·σ (σ = {sdLast:0.###}).", latest.V, mLast + Math.Sign(zLast) * zLim * sdLast, latest.T);
                    abnormal[s.Id] = Math.Max(abnormal.GetValueOrDefault(s.Id), 1);
                }
            }

            // -- 3. rate of change (toward danger), with acceleration → Level 2
            if (th?.RateLimitPerDay is double rateLim && rateLim > 0)
            {
                var w7 = series.Where(p => p.T >= latest.T.AddDays(-7)).ToList(); var w3 = series.Where(p => p.T >= latest.T.AddDays(-3)).ToList();
                if (w7.Count >= 4)
                {
                    var b7 = Cls_Stats.Ols(w7.Select(p => (p.T - latest.T).TotalDays).ToList(), w7.Select(p => p.V).ToList()).b;
                    var b3 = w3.Count >= 3 ? Cls_Stats.Ols(w3.Select(p => (p.T - latest.T).TotalDays).ToList(), w3.Select(p => p.V).ToList()).b : b7;
                    var sign = s.Direction == "Falling" ? -1 : 1;
                    if (sign * b7 > rateLim || sign * b3 > rateLim)
                    {
                        var accel = sign * b3 > 2 * sign * b7 && sign * b3 > rateLim;
                        await Raise(raised, damId, s, null, accel ? 2 : 1, "Rate", $"{s.Code}: {(accel ? "accelerating" : "fast")} change {b3:0.###} {s.EngUnit}/day",
                            $"7-day trend {b7:0.###}/day, 3-day trend {b3:0.###}/day; limit {rateLim:0.###}/day.", b3, rateLim, latest.T);
                        abnormal[s.Id] = Math.Max(abnormal.GetValueOrDefault(s.Id), accel ? 2 : 1);
                    }
                }
            }
        }

        // -- 4. corroboration: ≥ 2 instruments linked to the same failure mode abnormal → at least Level 2
        var open = await _alerts.ListAsync(damId, "Active", 1, 2000);
        foreach (var a in open.Where(a => a.SensorId != null && a.RuleType is "Threshold" or "Statistical" or "Rate" or "HST"))
            abnormal[a.SensorId!] = Math.Max(abnormal.GetValueOrDefault(a.SensorId!), a.Level);
        foreach (var p in (await new Cls_PfmaDAL().ListByDamAsync(damId)).Where(p => p.Status == "Active"))
        {
            var hits = p.SensorIds.Where(abnormal.ContainsKey).ToList();
            if (hits.Count < 2) continue;
            var lvl = Math.Max(2, hits.Max(h => abnormal[h]));
            var codes = sensors.Where(s => hits.Contains(s.Id)).Select(s => s.Code);
            await Raise(raised, damId, null, p.Id, lvl, "Corroboration", $"Failure mode {p.FailureModeCode}: {hits.Count} related instruments abnormal",
                $"PFMA at {p.Location}: {string.Join(", ", codes)} are abnormal together. Corroborated signals escalate to at least Level 2.", hits.Count, 2, asOf);
        }
        if (raised.Count > 0) await new Cls_AuditDAL().WriteAsync(actorId, actor, "Alert engine", $"Evaluated dam: {raised.Count} alert(s) raised or updated", "dam", damId);
        return raised;
    }

    private async Task Raise(List<AlertView> raised, string damId, Sensor? s, string? pfmaId, int level, string rule, string title, string detail, double? value, double? limit, DateTime at)
    {
        var existing = await _alerts.FindOpenAsync(s?.Id, pfmaId, rule);
        if (existing != null)
        {
            if (at <= existing.LastAt && level <= existing.Level) return;     // nothing new
            var escalated = level > existing.Level;
            await _alerts.RaiseAgainAsync(existing.Id, level, title, detail, value, limit, at);
            existing.Level = Math.Max(existing.Level, level); existing.Title = title; raised.Add(existing);
            if (escalated)
            {
                await new Cls_NotificationEngine().OnAlertAsync(existing.Id, $"Escalated to Level {level}");
                await new Cls_InspectionEngine().EnsureSpecialInspectionAsync(existing);
            }
            return;
        }
        var a = new AlertView { Id = Ids.New(), DamId = damId, SensorId = s?.Id, SensorCode = s?.Code, PfmaId = pfmaId, Level = level, RuleType = rule, Title = title, Detail = detail, Value = value, LimitValue = limit, FirstAt = at, LastAt = at, Occurrences = 1, Status = "Open" };
        await _alerts.InsertAsync(a);
        raised.Add(a);
        await new Cls_NotificationEngine().OnAlertAsync(a.Id, "New alert");
        await new Cls_InspectionEngine().EnsureSpecialInspectionAsync(a);
    }
}
