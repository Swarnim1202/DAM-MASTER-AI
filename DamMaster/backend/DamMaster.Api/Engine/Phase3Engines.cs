using System.Text.Json;
using DamMaster.Api.Common;
using DamMaster.Api.DAL;
using DamMaster.Api.Models;

namespace DamMaster.Api.Engine;

/// <summary>
/// HST (Hydrostatic–Season–Time) regression — pure maths, no I/O (REF-001 §8.1).
/// Reading = a0 + a1·z + a2·z² + a3·z³ + a4·z⁴ + b1·sin s + b2·cos s + b3·sin 2s + b4·cos 2s + c1·τ + c2·ln(1+τ)
/// z = (h − hmin)/(hmax − hmin) normalised reservoir level; s = 2π·day-of-year/365.25; τ = years since t0.
/// With less than ~11 months of data the seasonal terms cannot be separated from the trend, so a
/// provisional H+T model (a0, z, z², τ) is fitted instead.
/// </summary>
public static class Cls_Hst
{
    public static readonly string[] FullTerms = { "a0", "z", "z2", "z3", "z4", "sin1", "cos1", "sin2", "cos2", "t", "lnt" };
    public static readonly string[] ProvisionalTerms = { "a0", "z", "z2", "t" };
    public const double MinSpanDaysFull = 330;

    public static double[] Row(string[] terms, double z, DateTime t, DateTime t0)
    {
        var s = 2 * Math.PI * t.DayOfYear / 365.25; var tau = Math.Max(0, (t - t0).TotalDays / 365.25);
        return terms.Select(k => k switch
        {
            "a0" => 1.0, "z" => z, "z2" => z * z, "z3" => z * z * z, "z4" => z * z * z * z,
            "sin1" => Math.Sin(s), "cos1" => Math.Cos(s), "sin2" => Math.Sin(2 * s), "cos2" => Math.Cos(2 * s),
            "t" => tau, "lnt" => Math.Log(1 + tau), _ => throw new ArgumentException(k)
        }).ToArray();
    }

    /// <summary>Least squares via normal equations with a tiny ridge for numerical stability; Gaussian elimination with partial pivoting.</summary>
    public static (double[] beta, double r2, double sigma) Fit(IReadOnlyList<double[]> X, IReadOnlyList<double> y)
    {
        int n = X.Count, p = X[0].Length;
        if (n <= p + 5) throw new BusinessException($"Not enough data to fit {p} coefficients (need more than {p + 5} paired readings).");
        var A = new double[p, p]; var b = new double[p];
        for (var i = 0; i < n; i++) for (var j = 0; j < p; j++) { b[j] += X[i][j] * y[i]; for (var k = 0; k < p; k++) A[j, k] += X[i][j] * X[i][k]; }
        double trace = 0; for (var j = 0; j < p; j++) trace += A[j, j];
        for (var j = 1; j < p; j++) A[j, j] += 1e-9 * trace / p;   // do not penalise the intercept
        var beta = Solve(A, b);
        double mean = y.Average(), sse = 0, sst = 0;
        for (var i = 0; i < n; i++) { var e = y[i] - Dot(X[i], beta); sse += e * e; sst += (y[i] - mean) * (y[i] - mean); }
        return (beta, sst == 0 ? 0 : 1 - sse / sst, Math.Sqrt(sse / Math.Max(1, n - p)));
    }

    public static double Dot(double[] a, double[] b) { double s = 0; for (var i = 0; i < a.Length; i++) s += a[i] * b[i]; return s; }

    public static double[] Solve(double[,] A, double[] b)
    {
        int n = b.Length; var M = (double[,])A.Clone(); var v = (double[])b.Clone();
        for (var c = 0; c < n; c++)
        {
            var piv = c; for (var r = c + 1; r < n; r++) if (Math.Abs(M[r, c]) > Math.Abs(M[piv, c])) piv = r;
            if (Math.Abs(M[piv, c]) < 1e-14) throw new BusinessException("Data do not allow a unique model (the reservoir level may not vary enough).");
            if (piv != c) { for (var k = 0; k < n; k++) (M[c, k], M[piv, k]) = (M[piv, k], M[c, k]); (v[c], v[piv]) = (v[piv], v[c]); }
            for (var r = c + 1; r < n; r++) { var f = M[r, c] / M[c, c]; for (var k = c; k < n; k++) M[r, k] -= f * M[c, k]; v[r] -= f * v[c]; }
        }
        var x = new double[n];
        for (var r = n - 1; r >= 0; r--) { var s = v[r]; for (var k = r + 1; k < n; k++) s -= M[r, k] * x[k]; x[r] = s / M[r, r]; }
        return x;
    }

    /// <summary>Split a prediction into hydrostatic, seasonal and time parts (plus constant).</summary>
    public static (double constant, double hydro, double season, double time) Parts(string[] terms, double[] row, IReadOnlyDictionary<string, double> c)
    {
        double h = 0, s = 0, t = 0, k0 = 0;
        for (var i = 0; i < terms.Length; i++)
        {
            var v = row[i] * c[terms[i]];
            if (terms[i] == "a0") k0 += v; else if (terms[i].StartsWith("z")) h += v; else if (terms[i] is "t" or "lnt") t += v; else s += v;
        }
        return (k0, h, s, t);
    }
}

/// <summary>Reservoir level lookup (nearest valid RWL reading within ±12 h).</summary>
public class RwlLookup
{
    private readonly List<ReadingPoint> _p;
    public RwlLookup(List<ReadingPoint> points) => _p = points;
    public bool Any => _p.Count > 0;
    public double? At(DateTime t, double maxHours = 12)
    {
        if (_p.Count == 0) return null;
        int lo = 0, hi = _p.Count - 1;
        while (lo < hi) { var m = (lo + hi) / 2; if (_p[m].T < t) lo = m + 1; else hi = m; }
        var best = lo; if (lo > 0 && Math.Abs((_p[lo - 1].T - t).TotalHours) < Math.Abs((_p[lo].T - t).TotalHours)) best = lo - 1;
        return Math.Abs((_p[best].T - t).TotalHours) <= maxHours ? _p[best].V : null;
    }
    public static async Task<RwlLookup> ForDamAsync(string damId)
    {
        var rwl = (await new Cls_SensorDAL().ListByDamAsync(damId)).FirstOrDefault(s => s.ParameterCode == "RWL" && s.Status != "Retired");
        return new RwlLookup(rwl == null ? new() : await new Cls_ReadingDAL().SeriesAsync(rwl.Id, null, null, validOnly: true));
    }
}

public record HstPointDto(DateTime T, double Measured, double Predicted, double Hydro, double Season, double Time, double Residual, double Rwl);

public class Cls_HstService
{
    private static readonly JsonSerializerOptions Js = new();

    public static Dictionary<string, double> Coef(HstModel m) => JsonSerializer.Deserialize<Dictionary<string, double>>(m.Coefficients, Js) ?? new();
    public static string[] Terms(HstModel m) => m.Variant == "Full HST" ? Cls_Hst.FullTerms : Cls_Hst.ProvisionalTerms;
    public static double Z(HstModel m, double h) => m.HMax - m.HMin < 1e-9 ? 0 : (h - m.HMin) / (m.HMax - m.HMin);

    public static double Predict(HstModel m, Dictionary<string, double> c, DateTime t, double h)
    {
        var terms = Terms(m); var row = Cls_Hst.Row(terms, Z(m, h), t, m.T0);
        return terms.Select((k, i) => row[i] * c[k]).Sum();
    }

    public async Task<HstModel> FitAsync(string sensorId, double kLimit, string actor)
    {
        var s = await new Cls_SensorDAL().GetAsync(sensorId) ?? throw new BusinessException("Sensor not found.");
        var prm = (await new Cls_ReferenceDAL().ParametersAsync()).FirstOrDefault(p => p.Code == s.ParameterCode);
        if (prm == null || prm.IsLoad || prm.IsEvent) throw new BusinessException("HST models are fitted to response instruments only (not reservoir level, inflow, rainfall or event data).");
        var rwl = await RwlLookup.ForDamAsync(s.DamId);
        if (!rwl.Any) throw new BusinessException("The dam has no reservoir-level sensor with data; the hydrostatic term cannot be computed.");
        var last = await new Cls_ReadingDAL().LatestTimeAsync(sensorId) ?? throw new BusinessException("Sensor has no readings.");
        var pts = (await new Cls_ReadingDAL().SeriesAsync(sensorId, last.AddYears(-5), null, validOnly: true))
            .Select(p => (p.T, p.V, h: rwl.At(p.T))).Where(p => p.h.HasValue).Select(p => (p.T, p.V, h: p.h!.Value)).ToList();
        if (pts.Count < 60) throw new BusinessException($"Only {pts.Count} readings could be paired with reservoir level; at least 60 are needed.");
        var span = (pts[^1].T - pts[0].T).TotalDays;
        if (span < 30) throw new BusinessException("At least 30 days of data are needed.");
        var full = span >= Cls_Hst.MinSpanDaysFull;
        var terms = full ? Cls_Hst.FullTerms : Cls_Hst.ProvisionalTerms;
        double hmin = pts.Min(p => p.h), hmax = pts.Max(p => p.h);
        if (hmax - hmin < 0.5) throw new BusinessException($"Reservoir level varied only {hmax - hmin:0.00} m in the data — not enough to separate the water-load effect.");
        var t0 = pts[0].T;
        var X = pts.Select(p => Cls_Hst.Row(terms, (p.h - hmin) / (hmax - hmin), p.T, t0)).ToList();
        var (beta, r2, sigma) = Cls_Hst.Fit(X, pts.Select(p => p.V).ToList());
        var coef = terms.Select((k, i) => (k, beta[i])).ToDictionary(x => x.k, x => x.Item2);
        var tauEnd = (pts[^1].T - t0).TotalDays / 365.25;
        var drift = coef["t"] + (coef.TryGetValue("lnt", out var c2) ? c2 / (1 + tauEnd) : 0);
        var m = new HstModel
        {
            Id = Ids.New(), SensorId = sensorId, Variant = full ? "Full HST" : "Provisional H+T", DataFrom = pts[0].T, DataTo = pts[^1].T, NPoints = pts.Count,
            HMin = hmin, HMax = hmax, T0 = t0, Coefficients = JsonSerializer.Serialize(coef), R2 = r2, Sigma = Math.Max(sigma, 1e-9), KLimit = (decimal)kLimit,
            DriftPerYear = drift, Status = "Draft", FittedBy = actor,
            Notes = full ? $"{span:0} days of data; seasonal terms included." : $"Only {span:0} days of data (< {Cls_Hst.MinSpanDaysFull:0}): seasonal effect cannot be separated from the trend. Refit after one full year."
        };
        await new Cls_HstDAL().InsertAsync(m);
        return m;
    }

    /// <summary>Series for charts: measured, predicted, parts and residual over the last <paramref name="days"/>.</summary>
    public async Task<List<HstPointDto>> SeriesAsync(HstModel m, int days)
    {
        var s = await new Cls_SensorDAL().GetAsync(m.SensorId) ?? throw new BusinessException("Sensor not found.");
        var rwl = await RwlLookup.ForDamAsync(s.DamId); var c = Coef(m); var terms = Terms(m);
        var last = await new Cls_ReadingDAL().LatestTimeAsync(m.SensorId) ?? DateTime.UtcNow;
        var list = new List<HstPointDto>();
        foreach (var p in await new Cls_ReadingDAL().SeriesAsync(m.SensorId, days > 0 ? last.AddDays(-days) : null, null, validOnly: true))
        {
            var h = rwl.At(p.T); if (!h.HasValue) continue;
            var row = Cls_Hst.Row(terms, Z(m, h.Value), p.T, m.T0); var parts = Cls_Hst.Parts(terms, row, c);
            var pred = parts.constant + parts.hydro + parts.season + parts.time;
            list.Add(new HstPointDto(p.T, p.V, pred, parts.hydro, parts.season, parts.time, p.V - pred, h.Value));
        }
        return list;
    }
}

// =====================================================================
/// <summary>How a sensor responds to reservoir level and rainfall (lagged correlation, events).</summary>
public class Cls_ResponseAnalysis
{
    private static readonly int[] ReservoirLags = { 0, 6, 12, 24, 48, 72, 120, 168 };
    private static readonly int[] RainLags = { 0, 6, 12, 24, 48, 72 };

    public async Task<object> AnalyseAsync(string sensorId, double rainEventMm)
    {
        var s = await new Cls_SensorDAL().GetAsync(sensorId) ?? throw new BusinessException("Sensor not found.");
        var rd = new Cls_ReadingDAL(); var last = await rd.LatestTimeAsync(sensorId);
        if (!last.HasValue) throw new BusinessException("Sensor has no readings.");
        var series = await rd.SeriesAsync(sensorId, last.Value.AddDays(-365), null, validOnly: true);
        var sensors = await new Cls_SensorDAL().ListByDamAsync(s.DamId);
        var rwl = await RwlLookup.ForDamAsync(s.DamId);

        // ---- reservoir: correlation of the reading with the level `lag` hours earlier
        object? reservoir = null;
        if (rwl.Any && s.ParameterCode != "RWL")
        {
            var lags = ReservoirLags.Select(lag =>
            {
                var pairs = series.Select(p => (v: p.V, h: rwl.At(p.T.AddHours(-lag), 6))).Where(x => x.h.HasValue).ToList();
                return new { lagHours = lag, n = pairs.Count, r = pairs.Count >= 10 ? Math.Round(Cls_Stats.Pearson(pairs.Select(x => x.h!.Value).ToList(), pairs.Select(x => x.v).ToList()), 3) : (double?)null };
            }).ToList();
            var best = lags.Where(l => l.r.HasValue).OrderByDescending(l => Math.Abs(l.r!.Value)).FirstOrDefault();
            var recent = series.Where(p => p.T >= last.Value.AddDays(-90)).Select(p => (p.T, p.V, h: rwl.At(p.T))).Where(x => x.h.HasValue).ToList();
            (double b, double a) fit = recent.Count >= 10 ? Cls_Stats.Ols(recent.Select(x => x.h!.Value).ToList(), recent.Select(x => x.V).ToList()) : (0, 0);
            var r = best?.r ?? 0;
            var text = Math.Abs(r) < 0.3 ? "No meaningful relation with reservoir level — the reading is driven by something else (temperature, rainfall, time, or a local condition). Check whether this is expected for this instrument."
                : Math.Abs(r) < 0.6 ? $"Moderate relation with reservoir level (r = {r:0.00}, best lag {best!.lagHours} h). Part of the behaviour is explained by water load."
                : $"Strong relation with reservoir level (r = {r:0.00}, best lag {best!.lagHours} h): about {fit.b:0.###} {s.EngUnit} per metre of reservoir. "
                  + (best.lagHours == 0 ? "Immediate response suggests a direct hydraulic connection — normal for uplift near the heel or seepage at the drains; unusual deep in an embankment." : "A delayed response is typical of pressure diffusing through the foundation or fill.");
            reservoir = new { lags, bestLagHours = best?.lagHours, bestR = best?.r, slopePerMetre = fit.b, scatter = recent.Select(x => new { h = x.h, v = x.V }).ToList(), interpretation = text };
        }

        // ---- rainfall: correlation of reading changes with rain in the preceding 24 h, and rain events
        object? rain = null;
        var gauge = sensors.FirstOrDefault(x => x.ParameterCode == "RNF" && x.Status != "Retired");
        if (gauge != null && s.ParameterCode != "RNF")
        {
            var rainPts = await rd.SeriesAsync(gauge.Id, last.Value.AddDays(-366), null, validOnly: true);
            double RainIn(DateTime from, DateTime to) => rainPts.Where(p => p.T > from && p.T <= to).Sum(p => p.V);
            var deltas = series.Zip(series.Skip(1), (a, b) => (t: b.T, d: b.V - a.V)).ToList();
            var lags = RainLags.Select(lag =>
            {
                var pairs = deltas.Select(x => (x.d, rain: RainIn(x.t.AddHours(-lag - 24), x.t.AddHours(-lag)))).ToList();
                return new { lagHours = lag, n = pairs.Count, r = pairs.Count >= 10 && pairs.Any(p => p.rain > 0) ? Math.Round(Cls_Stats.Pearson(pairs.Select(p => p.rain).ToList(), pairs.Select(p => p.d).ToList()), 3) : (double?)null };
            }).ToList();
            var ist = TimeSpan.FromHours(5.5);
            var events = rainPts.GroupBy(p => (p.T + ist).Date).Select(g => new { day = g.Key, mm = g.Sum(p => p.V) }).Where(e => e.mm >= rainEventMm).OrderByDescending(e => e.mm).Take(10)
                .Select(e =>
                {
                    var start = e.day - ist; var before = series.LastOrDefault(p => p.T <= start); var after = series.Where(p => p.T > start && p.T <= start.AddHours(72)).ToList();
                    return new { e.day, e.mm, rise = before != null && after.Count > 0 ? after.Max(p => p.V) - before.V : (double?)null };
                }).ToList();
            var best = lags.Where(l => l.r.HasValue).OrderByDescending(l => Math.Abs(l.r!.Value)).FirstOrDefault();
            var r = best?.r ?? 0;
            var text = best == null ? "Not enough rainfall in the period to judge." : Math.Abs(r) < 0.3 ? "Little response to rainfall — the reading is not driven by surface infiltration."
                : $"The reading changes with rainfall (r = {r:0.00}, about {best.lagHours} h after rain). A fast, strong rain response in a seepage or pore-pressure instrument suggests surface water entering the measurement (weir catchment, open joints) or shallow infiltration — check drainage and whether the weir collects surface run-off.";
            rain = new { gauge = gauge.Code, eventThresholdMm = rainEventMm, lags, bestLagHours = best?.lagHours, bestR = best?.r, events, interpretation = text };
        }
        return new { sensor = s.Code, unit = s.EngUnit, reservoir, rain };
    }
}

// =====================================================================
/// <summary>Dam Health Index (REF-001 / FRS-001 §9): six weighted components, each with its reasons.</summary>
public class Cls_DhiEngine
{
    public record Component(string Code, string Name, double Weight, double Score, List<string> Reasons);
    public record Result(string DamId, string DamName, DateTime ComputedAt, DateTime? DataAsOf, double Dhi, string Band, int Risk, string Rating, List<Component> Components, List<string> RiskReasons);

    public static string Band(double d) => d >= 85 ? "Good" : d >= 75 ? "Fair" : d >= 65 ? "Poor" : "Critical";
    public static string Rating(int r) => r >= 60 ? "Action" : r >= 40 ? "Watch" : r >= 25 ? "Monitor" : "Stable";
    private static double Clip(double v) => Math.Max(0, Math.Min(100, v));

    public async Task<Result> ComputeAsync(string damId)
    {
        var dam = await new Cls_DamDAL().GetAsync(damId) ?? throw new BusinessException("Dam not found.");
        var x = await new Cls_DhiDAL().InputsAsync(damId);
        var weights = (await new Cls_DhiDAL().WeightsAsync()).ToDictionary(w => w.Code);
        var asOf = x.DataAsOf ?? DateTime.UtcNow; var today = DateTime.UtcNow.Date;
        var comps = new List<Component>();
        Component C(string code, double score, List<string> why) => new(code, weights[code].Name, (double)weights[code].Weight, Math.Round(Clip(score), 1), why);

        // ---- structural
        {
            var why = new List<string>(); double sc;
            var cond = x.LastInspectionCondition;
            if (cond == null) { sc = 70; why.Add("No completed inspection in the last 12 months → 70"); }
            else { sc = cond switch { "Good" => 100, "Fair" => 80, "Poor" => 55, _ => 30 }; why.Add($"Latest inspection ({x.LastInspectionDate:dd MMM yyyy}): {cond} → {sc}"); }
            var high = x.OpenFindings.Count(f => f.Severity == "High"); var crit = x.OpenFindings.Count(f => f.Severity == "Critical");
            var pf = Math.Min(40, 10 * high + 20 * crit); if (pf > 0) { sc -= pf; why.Add($"{high} High + {crit} Critical open finding(s) → −{pf}"); }
            var def = x.OpenSensorAlerts.Where(a => a.GroupCode is "D" or "E" or "F").GroupBy(a => a.SensorId).Select(g => g.Max(a => a.Level)).ToList();
            var pd = Math.Min(30, def.Sum(l => l >= 2 ? 15 : 5)); if (pd > 0) { sc -= pd; why.Add($"{def.Count} deformation / stress sensor(s) in alert → −{pd}"); }
            comps.Add(C("STRUCTURAL", sc, why));
        }
        // ---- seepage
        {
            var why = new List<string>(); double sc = 100;
            var see = x.OpenSensorAlerts.Where(a => a.GroupCode is "B" or "C").GroupBy(a => a.SensorId).Select(g => g.Max(a => a.Level)).ToList();
            var ps = Math.Min(60, see.Sum(l => l >= 3 ? 40 : l == 2 ? 25 : 10)); if (ps > 0) { sc -= ps; why.Add($"{see.Count} seepage / pressure sensor(s) in alert (levels {string.Join(", ", see.OrderByDescending(l => l))}) → −{ps}"); }
            if (x.OpenCorroboration > 0) { sc -= 15; why.Add("Failure-mode corroboration alert open → −15"); }
            var lf = x.OpenFindings.Count(f => f.Severity is "High" or "Critical" && f.SensorGroup is "B" or "C");
            if (lf > 0) { sc -= 10 * lf; why.Add($"{lf} serious finding(s) linked to seepage / pressure sensors → −{10 * lf}"); }
            if (why.Count == 0) why.Add("No seepage or pressure sensor in alert");
            comps.Add(C("SEEPAGE", sc, why));
        }
        // ---- instrumentation
        {
            var act = x.Sensors.Where(s => s.Status != "Retired").ToList(); var why = new List<string>();
            double func = 0, avail = 0, cal = 0;
            if (act.Count > 0)
            {
                func = 100.0 * act.Count(s => s.Status == "Active" && s.LastAt.HasValue && asOf - s.LastAt.Value <= TimeSpan.FromMinutes(3.0 * s.IntervalMin) && s.LastQuality != "B") / act.Count;
                avail = Math.Min(100, 100.0 * act.Sum(s => Math.Min(s.Valid30, 30 * 1440.0 / s.IntervalMin)) / act.Sum(s => 30 * 1440.0 / s.IntervalMin));
                cal = 100.0 * act.Count(s => !s.CalibrationDue.HasValue || s.CalibrationDue.Value.Date >= asOf.Date) / act.Count;
            }
            why.Add($"Functional {func:0}% · availability {avail:0.0}% (30 days) · calibration in date {cal:0}% ({act.Count} sensors)");
            comps.Add(C("INSTRUMENTATION", act.Count == 0 ? 0 : 0.5 * func + 0.3 * avail + 0.2 * cal, why));
        }
        // ---- hydrological
        {
            var why = new List<string>(); double sc = 100;
            if (x.DaysAboveFrl > 0) { var p = Math.Min(20, x.DaysAboveFrl); sc -= p; why.Add($"{x.DaysAboveFrl} day(s) above FRL in last 30 days → −{p}"); }
            if (x.MwlExceeded) { sc -= 20; why.Add("MWL exceeded in last 30 days → −20"); }
            if (x.OpenSensorAlerts.Any(a => a.Level >= 2 && a.ParameterCode is "RWL" or "INF")) { sc -= 20; why.Add("Level 2+ alert open on reservoir level / inflow → −20"); }
            var sg = x.OpenFindings.Count(f => f.Severity is "High" or "Critical" && f.Component == "Spillway & gates");
            if (sg > 0) { sc -= 15 * sg; why.Add($"{sg} serious spillway / gate finding(s) → −{15 * sg}"); }
            if (why.Count == 0) why.Add(x.FrlKnown ? "Reservoir within FRL; no flood-handling issues" : "FRL not recorded for the dam — level checks skipped");
            comps.Add(C("HYDROLOGICAL", sc, why));
        }
        // ---- O&M
        {
            var why = new List<string>();
            var insp = x.MonsoonDue == 0 ? 100 : 100.0 * x.MonsoonOnTime / x.MonsoonDue;
            why.Add(x.MonsoonDue == 0 ? "No monsoon inspection due yet this year → 100" : $"{x.MonsoonOnTime} of {x.MonsoonDue} due monsoon inspection(s) done on time → {insp:0}");
            var den = x.FindingsClosedOnTime + x.FindingsClosedLate + x.FindingsOverdueOpen;
            var fnd = den == 0 ? 100 : 100.0 * x.FindingsClosedOnTime / den;
            why.Add(den == 0 ? "No findings due in the last 365 days → 100" : $"Findings: {x.FindingsClosedOnTime} closed on time, {x.FindingsClosedLate} late, {x.FindingsOverdueOpen} overdue open → {fnd:0}");
            comps.Add(C("OM", 0.5 * insp + 0.5 * fnd, why));
        }
        // ---- documentation
        {
            var why = new List<string>(); double sc = 0;
            foreach (var (type, pts) in new[] { ("EAP", 30), ("O&M manual", 20), ("Instrument register", 20), ("As-built drawings", 15), ("Safety review", 15) })
            {
                var okDoc = x.ApprovedDocs.Contains(type);
                if (okDoc) sc += pts;
                why.Add($"{type}: {(okDoc ? "+" + pts : "missing / not approved / review overdue (0 of " + pts + ")")}");
            }
            comps.Add(C("DOCUMENTATION", sc, why));
        }

        var dhi = Math.Round(comps.Sum(c => c.Weight * c.Score), 1);
        var hazard = (await new Cls_ReferenceDAL().HazardClassesAsync()).FirstOrDefault(h => h.Code == dam.HazardCode);
        var H = (double)(hazard?.RiskFactor ?? 1m);
        var A = x.OpenSensorAlerts.Count(a => a.Level >= 2) + x.OpenCorroboration;
        var O = x.MonsoonOverdue ? 1 : 0;
        var risk = (int)Math.Min(100, Math.Round((100 - dhi) * 1.6 * H + 4 * A + 6 * O));
        var rr = new List<string> { $"(100 − DHI {dhi}) × 1.6 × hazard factor {H}", $"+ 4 × {A} open Level 2+ instrument / corroboration alert(s)", $"+ 6 × {O} (monsoon inspection overdue)", $"= {risk} → {Rating(risk)}" };
        return new Result(damId, dam.Name, DateTime.UtcNow, x.DataAsOf, dhi, Band(dhi), risk, Rating(risk), comps, rr);
    }
}

/// <summary>Data availability per sensor over a window ending at the dam's latest reading (or now).</summary>
public class Cls_AvailabilityReport
{
    public async Task<object> BuildAsync(string damId, int days, bool relativeToNow)
    {
        var dam = await new Cls_DamDAL().GetAsync(damId) ?? throw new BusinessException("Dam not found.");
        var sensors = (await new Cls_SensorDAL().ListByDamAsync(damId)).Where(s => s.Status != "Retired").ToList();
        var rd = new Cls_ReadingDAL();
        var latest = new List<DateTime>();
        foreach (var s in sensors) if (await rd.LatestTimeAsync(s.Id) is DateTime t) latest.Add(t);
        var end = relativeToNow || latest.Count == 0 ? DateTime.UtcNow : latest.Max(); var start = end.AddDays(-days);
        var rows = new List<object>(); double totExp = 0, totRecv = 0, totValid = 0;
        foreach (var s in sensors)
        {
            var pts = await rd.SeriesAsync(s.Id, start, end);
            var expected = days * 1440.0 / Math.Max(1, s.ReadingIntervalMin);
            var valid = pts.Count(p => p.Q != "B");
            var times = pts.Select(p => p.T).Prepend(start).Append(end).ToList();
            var gap = times.Zip(times.Skip(1), (a, b) => (b - a).TotalHours).DefaultIfEmpty(0).Max();
            totExp += expected; totRecv += Math.Min(pts.Count, expected); totValid += Math.Min(valid, expected);
            rows.Add(new
            {
                sensorId = s.Id, code = s.Code, parameterCode = s.ParameterCode, intervalMin = s.ReadingIntervalMin, expected = Math.Round(expected),
                received = pts.Count, valid, suspect = pts.Count(p => p.Q == "S"), bad = pts.Count(p => p.Q == "B"),
                availabilityPct = Math.Round(Math.Min(100, 100.0 * pts.Count / expected), 1), validPct = Math.Round(Math.Min(100, 100.0 * valid / expected), 1),
                longestGapHours = Math.Round(gap, 1), lastReading = pts.Count > 0 ? pts[^1].T : (DateTime?)null
            });
        }
        return new
        {
            damId, damName = dam.Name, windowStart = start, windowEnd = end, days, relativeTo = relativeToNow ? "now" : "latest reading",
            availabilityPct = totExp == 0 ? 0 : Math.Round(100 * totRecv / totExp, 1), validPct = totExp == 0 ? 0 : Math.Round(100 * totValid / totExp, 1), rows
        };
    }
}
