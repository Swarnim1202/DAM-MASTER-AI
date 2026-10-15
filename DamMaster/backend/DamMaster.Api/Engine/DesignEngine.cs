using System.Globalization;
using DamMaster.Api.Common;

namespace DamMaster.Api.Engine;

// ===================== calculation record (12-step format, FRS-002) =====================
public class ResultItem { public string Label { get; set; } = ""; public string Value { get; set; } = ""; public string Unit { get; set; } = ""; }
public class CheckItem { public string Label { get; set; } = ""; public string Value { get; set; } = ""; public string Limit { get; set; } = ""; public bool Ok { get; set; } public string Unit { get; set; } = ""; public bool Max { get; set; } }
public class CalcRecord
{
    public string Code { get; set; } = ""; public string Title { get; set; } = "";
    public Dictionary<string, string> Inputs { get; set; } = new(); public List<string> Assumptions { get; set; } = new(); public List<string> Codes { get; set; } = new();
    public string Method { get; set; } = ""; public string Formula { get; set; } = ""; public List<string> Steps { get; set; } = new();
    public List<ResultItem> Results { get; set; } = new(); public List<CheckItem> Checks { get; set; } = new(); public string Interpretation { get; set; } = "";
    public object? Data { get; set; }
}

/// <summary>Design inputs: key → value (number or text) plus the reservoir elevation–area table.</summary>
public class DesignInputs
{
    public Dictionary<string, object?> V { get; set; } = new();
    public List<double[]> Ea { get; set; } = new();
    public double N(string k) => V.TryGetValue(k, out var v) && v != null ? Convert.ToDouble(v is System.Text.Json.JsonElement je ? je.GetDouble() : v, CultureInfo.InvariantCulture) : throw new BusinessException($"Input {k} is missing.");
    public string S(string k) => V.TryGetValue(k, out var v) && v != null ? (v is System.Text.Json.JsonElement je ? je.ToString() : v.ToString()!) : "";
    public DesignInputs With(string k, object v) { var c = new DesignInputs { V = new(V), Ea = Ea }; c.V[k] = v; return c; }
}

public class GravResult { public double Wt, U, P, Fh, Pe, V, Hs, FsSF, FsF, FsOT, E, SToe, SHeel; public bool Mid; }
public class DamOption
{
    public string Type = ""; public double Fl, K, Hwv, Crest, FB, H, T, B, Area, Vol, Slab, MainCost, CostCmp, Us, Ds; public string Kind = "", VolLbl = "", Slopes = "";
    public bool Feasible = true, Pass; public List<string> Notes = new(); public List<CheckItem> Checks = new(); public int Score; public List<string> Why = new();
    public GravResult? Normal, Seismic;
}
public record SpillOut(double Hs, double Ls, int Nb, double Gross, double GateH);

public static class DesignMath
{
    public const double G = 9.81, GW = 9.81;
    public static readonly string[] DamTypes = { "Concrete gravity", "Earthfill (zoned)", "Earthfill (homogeneous)", "Rockfill (CFRD)" };
    public static readonly Dictionary<string, double> Shape = new() { ["V-shaped (narrow)"] = .45, ["U-shaped"] = .62, ["Wide / open"] = .8 };
    public static string F(double v, int d = 2) => v.ToString("N" + d, CultureInfo.InvariantCulture);

    /// <summary>Placeholder seismic coefficient αh = (Z/2)·I, I = 1.5.</summary>
    public static double Ah(string zone) => (zone switch { "II" => .10, "III" => .16, "IV" => .24, "V" => .36, _ => .16 }) / 2 * 1.5;
    /// <summary>Molitor wave height (m), V km/h, F km.</summary>
    public static double Wave(double V, double F) => F < 32 ? 0.032 * Math.Sqrt(V * F) + 0.763 - 0.271 * Math.Pow(F, .25) : 0.032 * Math.Sqrt(V * F);
    public static double SlopeFS(double tanPhi, double k, double n) { var b = Math.Atan(1 / n); return tanPhi * (Math.Cos(b) - k * Math.Sin(b)) / (Math.Sin(b) + k * Math.Cos(b)); }

    /// <summary>Gravity section limit equilibrium per metre run (vertical u/s face).</summary>
    public static GravResult Grav(double Hd, double hw, double B, double T, double gc, double mu, double c, double up, double k)
    {
        double W1 = T * Hd * gc, W2 = .5 * (B - T) * Hd * gc, Wt = W1 + W2, U = .5 * GW * hw * up * B, P = .5 * GW * hw * hw, Fh = k * Wt, yc = (W1 * Hd / 2 + W2 * Hd / 3) / Wt, Pe = 7.0 / 12 * k * GW * hw * hw;
        double Mr = W1 * (B - T / 2) + W2 * (B - T) * 2 / 3, Mo = P * hw / 3 + U * 2 * B / 3 + Fh * yc + Pe * .4 * hw, V = Wt - U, Hs = P + Fh + Pe, e = B / 2 - (Mr - Mo) / V;
        return new GravResult { Wt = Wt, U = U, P = P, Fh = Fh, Pe = Pe, V = V, Hs = Hs, FsSF = (mu * V + c * B) / Hs, FsF = mu * V / Hs, FsOT = Mr / Mo, E = e, Mid = Math.Abs(e) <= B / 6, SToe = V / B * (1 + 6 * e / B), SHeel = V / B * (1 - 6 * e / B) };
    }

    public static SpillOut Spill(DesignInputs i)
    {
        var Hs = i.N("mwl") - i.N("spc"); var Ls = Hs > 0 ? i.N("Q") / (i.N("Cd") * Math.Pow(Hs, 1.5)) : double.NaN;
        var nb = Math.Max(1, (int)Math.Ceiling(Ls / i.N("bay"))); var gross = nb * i.N("bay") + (nb - 1) * i.N("pier");
        return new SpillOut(Hs, Ls, nb, gross, Math.Max(0, i.N("frl") - i.N("spc")) + 0.5);
    }

    public static DamOption Design(DesignInputs i, string type)
    {
        var fl = i.N("rbl") - i.N("fdn"); var k = Ah(i.S("zone")); var hwv = Wave(i.N("wind"), i.N("fetch")); var shape = Shape.GetValueOrDefault(i.S("valley"), .62);
        double slN = i.N("slN"), slS = i.N("slS"), ot = i.N("ot");
        var d = new DamOption { Type = type, Fl = fl, K = k, Hwv = hwv };
        if (type == "Concrete gravity")
        {
            var FB = Math.Max(1.5 * hwv, 1.5); var crest = i.N("mwl") + FB; var H = crest - fl; var T = Math.Max(6, Math.Ceiling(H * .14 * 2) / 2);
            double gc = i.N("gc"), mu = i.N("mu"), c = i.N("c"), up = i.N("up"), hN = i.N("frl") - fl, hF = i.N("mwl") - fl;
            List<(string c, string lc, double v, double r)> Cases(double B) => new()
            {
                ("Sliding, shear friction", "A · Normal (FRL)", Grav(H, hN, B, T, gc, mu, c, up, 0).FsSF, slN),
                ("Sliding, shear friction", "C · Flood (MWL)", Grav(H, hF, B, T, gc, mu, c, up, 0).FsSF, 1.3),
                ("Sliding, shear friction", "D · Normal + earthquake", Grav(H, hN, B, T, gc, mu, c, up, k).FsSF, slS),
                ("Overturning", "A · Normal (FRL)", Grav(H, hN, B, T, gc, mu, c, up, 0).FsOT, ot),
                ("Overturning", "D · Normal + earthquake", Grav(H, hN, B, T, gc, mu, c, up, k).FsOT, 1.2),
                ("Sliding, shear friction", "G · Drains inoperative", Grav(H, hN, B, T, gc, mu, c, 1, 0).FsSF, 1.2),
            };
            var Bw = Math.Ceiling(H * .65); var cs = Cases(Bw);
            for (; Bw <= H * 1.6; Bw += 0.5) { cs = Cases(Bw); if (cs.All(x => x.v >= x.r) && Grav(H, hN, Bw, T, gc, mu, c, up, 0).Mid) break; }
            var n = Grav(H, hN, Bw, T, gc, mu, c, up, 0); var e = Grav(H, hN, Bw, T, gc, mu, c, up, k);
            d.Crest = crest; d.FB = FB; d.H = H; d.T = T; d.B = Bw; d.Area = H * (Bw + T) / 2; d.Vol = d.Area * i.N("len") * shape; d.VolLbl = "Concrete"; d.Slopes = $"B/H {F(Bw / H)}";
            d.Normal = n; d.Seismic = e; d.MainCost = d.Vol * i.N("rConc");
            d.Checks = cs.Select(x => new CheckItem { Label = $"{x.c} · {x.lc}", Value = F(x.v), Limit = F(x.r, 2), Ok = x.v >= x.r }).ToList();
            d.Checks.Add(new CheckItem { Label = "Resultant in middle third · A · Normal (FRL)", Value = n.Mid ? "Yes" : "No", Limit = "Yes", Ok = n.Mid });
            d.Checks.Add(new CheckItem { Label = "Heel stress (no tension) · A · Normal (FRL)", Value = F(n.SHeel, 0), Limit = "0", Ok = n.SHeel >= 0, Unit = "kPa" });
            if (i.S("found") == "Soil / alluvium") { d.Feasible = false; d.Notes.Add("Concrete gravity needs a rock foundation."); }
        }
        else
        {
            var kind = type.Contains("zoned") ? "z" : type.Contains("homog") ? "h" : "r";
            var FB = Math.Max(1.5 * hwv, 2.0); var crest = i.N("mwl") + FB; var H = crest - fl; var T = Math.Min(15, Math.Max(6, Math.Ceiling((H / 5 + 3) * 2) / 2));
            double us = kind == "z" ? 2.75 : kind == "h" ? 3 : 1.4, ds = kind == "z" ? 2.25 : kind == "h" ? 2.5 : 1.4;
            var adj = (i.S("found") switch { "Soil / alluvium" => .5, "Fair rock" => .25, _ => 0 }) + (i.S("zone") switch { "IV" => .25, "V" => .5, _ => 0 }) + (H > 50 ? .25 : 0);
            if (kind != "r") { us += adj; ds += adj; } else if (i.S("zone") == "V") { us += .2; ds += .2; }
            var phi = (kind == "r" ? 48 : kind == "h" ? i.N("phi") - 2 : i.N("phi")) * Math.PI / 180; var tp = Math.Tan(phi); var rdd = kind == "z" ? .65 : .5;
            List<(string c, string lc, double v, double r, string side)> Ev() { var l = new List<(string, string, double, double, string)> { ("Downstream slope", "Steady seepage", SlopeFS(tp, 0, ds), slN, "ds") };
                if (kind != "r") l.Add(("Upstream slope", "Rapid drawdown", rdd * tp * us, 1.3, "us"));
                l.Add(("Downstream slope", "Seismic (pseudo-static)", SlopeFS(tp, k, ds), 1.0, "ds")); l.Add(("Upstream slope", "Seismic (pseudo-static)", SlopeFS(tp, kind == "r" ? k : 2 * k, us), 1.0, "us")); return l; }
            var cs = Ev(); var g = 0;
            while (cs.Any(x => x.v < x.r) && g++ < 40) { foreach (var x in cs.Where(x => x.v < x.r).ToList()) { if (x.side == "us") us += .25; else ds += .25; } cs = Ev(); }
            var B = T + (us + ds) * H; var area = H * (T + B) / 2; var vol = area * i.N("len") * shape;
            var mc = vol * (kind == "r" ? i.N("rRock") : i.N("rFill")); double slab = 0;
            if (kind == "r") { slab = i.N("len") * shape * H * Math.Sqrt(1 + us * us) * (0.3 + 0.002 * H); mc += slab * i.N("rConc"); }
            d.Crest = crest; d.FB = FB; d.H = H; d.T = T; d.B = B; d.Us = us; d.Ds = ds; d.Area = area; d.Vol = vol; d.Slab = slab; d.Kind = kind;
            d.VolLbl = kind == "r" ? "Rockfill" : "Earthfill"; d.Slopes = $"{F(us)}:1 u/s · {F(ds)}:1 d/s"; d.MainCost = mc;
            d.Checks = cs.Select(x => new CheckItem { Label = $"{x.c} · {x.lc}", Value = F(x.v), Limit = F(x.r, 2), Ok = x.v >= x.r }).ToList();
            d.Checks.Add(new CheckItem { Label = "Freeboard above MWL · Wave run-up", Value = F(FB), Limit = "2.00", Ok = FB >= 2.0, Unit = "m" });
            if (kind == "r" && i.S("found") == "Soil / alluvium") { d.Feasible = false; d.Notes.Add("CFRD plinth needs rock."); }
            if (kind == "h" && H > 30) d.Notes.Add("Homogeneous sections are normally limited to low dams (about 30 m).");
        }
        d.Pass = d.Checks.All(c => c.Ok);
        return d;
    }

    public static (int score, List<string> why) Score(DesignInputs i, DamOption d, double minC)
    {
        string f = i.S("found"), z = i.S("zone"), v = i.S("valley"), t = d.Type; double s = 0; var why = new List<string>();
        void Add(double p, string w = "") { s += p; if (w != "") why.Add(w); }
        int Pick(string key, params (string k, int v)[] m) => m.FirstOrDefault(x => x.k == key).v;
        var bigFlood = i.N("Q") > 8000;
        if (t == "Concrete gravity")
        {
            Add(Pick(f, ("Excellent rock", 30), ("Good rock", 25), ("Fair rock", 12), ("Soil / alluvium", 0)), f.Contains("rock") ? "Rock foundation carries concentrated base pressure" : "");
            Add(Pick(v, ("V-shaped (narrow)", 18), ("U-shaped", 20), ("Wide / open", 8)), v != "Wide / open" ? "Valley suits a compact concrete section" : "");
            Add(Pick(z, ("II", 20), ("III", 18), ("IV", 12), ("V", 6))); Add(bigFlood ? 18 : 10, bigFlood ? "Large flood passes over an overflow spillway on the dam" : "");
        }
        else if (t == "Rockfill (CFRD)")
        {
            Add(Pick(f, ("Excellent rock", 26), ("Good rock", 26), ("Fair rock", 16), ("Soil / alluvium", 3)));
            Add(Pick(v, ("V-shaped (narrow)", 20), ("U-shaped", 18), ("Wide / open", 10)), v == "V-shaped (narrow)" ? "Steep rockfill slopes fit a narrow gorge" : "");
            Add(18, "Rockfill performs well in earthquakes"); Add(bigFlood ? 6 : 12);
        }
        else
        {
            Add(Pick(f, ("Excellent rock", 18), ("Good rock", 20), ("Fair rock", 25), ("Soil / alluvium", 30)), !f.Contains("Excellent") ? "Embankment tolerates weaker foundations" : "");
            Add(Pick(v, ("V-shaped (narrow)", 6), ("U-shaped", 16), ("Wide / open", 26)), v == "Wide / open" ? "Long crest in a wide valley favours earthfill" : "");
            Add(Pick(z, ("II", 18), ("III", 17), ("IV", 14), ("V", 10))); Add(bigFlood ? 4 : 12);
            if (t.Contains("homog")) Add(d.H <= 30 ? 10 : -20);
        }
        var cs = Math.Round(20 * minC / d.CostCmp, MidpointRounding.AwayFromZero); Add(cs, cs >= 19 ? "Lowest estimated cost" : "");
        if (!d.Feasible) s = Math.Min(s, 8); if (!d.Pass) s -= 5;
        return ((int)Math.Max(0, Math.Min(100, Math.Round(s, MidpointRounding.AwayFromZero))), why);
    }

    /// <summary>Storage-indication (modified Puls) routing; gates hold FRL while free outflow would draw the level below it.</summary>
    public static (List<double[]> ser, double pkO, double tO, double maxH, double vin, double vout, double send, double err, double L, bool over, double capFrl) Route(DesignInputs i, int nb)
    {
        var ea = i.Ea.OrderBy(r => r[0]).ToList(); double L = nb * i.N("bay"), dh = 0.01, frl = i.N("frl"), spc = i.N("spc"), Cd = i.N("Cd"), Qp = i.N("Qp"), tp = i.N("tp"), tb = i.N("tb");
        double Area(double h)
        {
            if (h <= ea[0][0]) return ea[0][1] * 1e6;
            for (var k = 1; k < ea.Count; k++) if (h <= ea[k][0]) { double h1 = ea[k - 1][0], a1 = ea[k - 1][1], h2 = ea[k][0], a2 = ea[k][1]; return (a1 + (a2 - a1) * (h - h1) / (h2 - h1)) * 1e6; }
            var n = ea.Count; return Math.Max(0, ea[n - 1][1] + (ea[n - 1][1] - ea[n - 2][1]) / (ea[n - 1][0] - ea[n - 2][0]) * (h - ea[n - 1][0])) * 1e6;
        }
        var hs = new List<double>(); var Sv = new List<double>();
        for (var k = 0; frl + k * dh <= i.N("mwl") + 15; k++) { var h = frl + k * dh; hs.Add(h); Sv.Add(k > 0 ? Sv[k - 1] + 0.5 * (Area(h - dh) + Area(h)) * dh : 0); }
        double O(double h) => Cd * L * Math.Pow(Math.Max(h - spc, 0), 1.5);
        const double dt = 1800;
        double Inflow(double t) { var th = t / 3600; return th <= tp ? Qp * th / tp : th <= tb ? Qp * (tb - th) / (tb - tp) : 0; }
        double Fj(int j) => 2 * Sv[j] / dt + O(hs[j]);
        double hh = frl, s = 0, o = Math.Min(Inflow(0), O(frl)), vin = 0, vout = 0, pkO = o, tO = 0, maxH = hh; var over = false;
        var ser = new List<double[]> { new[] { 0, Inflow(0), o, hh } }; var nSteps = (int)Math.Ceiling((tb + 24) * 3600 / dt);
        for (var k = 0; k < nSteps; k++)
        {
            double I1 = Inflow(k * dt), I2 = Inflow((k + 1) * dt), o1 = o, rhs = I1 + I2 + 2 * s / dt - o;
            if (rhs <= Fj(0)) { hh = frl; s = 0; o = Math.Min(I2, O(frl)); }
            else
            {
                int lo = 0, hi = hs.Count - 1;
                if (rhs >= Fj(hi)) { over = true; hh = hs[hi]; s = Sv[hi]; o = O(hh); }
                else { while (hi - lo > 1) { var m = (lo + hi) >> 1; if (Fj(m) < rhs) lo = m; else hi = m; } var fr = (rhs - Fj(lo)) / (Fj(lo + 1) - Fj(lo)); hh = hs[lo] + dh * fr; s = Sv[lo] + (Sv[lo + 1] - Sv[lo]) * fr; o = O(hh); }
            }
            vin += (I1 + I2) / 2 * dt; vout += (o1 + o) / 2 * dt; ser.Add(new[] { (k + 1) * dt / 3600, I2, o, hh });
            if (o > pkO) { pkO = o; tO = (k + 1) * dt / 3600; }
            if (hh > maxH) maxH = hh;
        }
        return (ser, pkO, tO, maxH, vin, vout, s, vin > 0 ? Math.Abs(vin - vout - s) / vin * 100 : 0, L, over, O(frl));
    }
}

/// <summary>The nine connected calculations, their dependencies and code references.</summary>
public static class DesignCalcs
{
    public static readonly string[] Order = { "top", "type", "dam", "spill", "route", "basin", "gate", "hp", "cost" };
    public static readonly Dictionary<string, string> Titles = new()
    {
        ["top"] = "Top of dam & freeboard", ["type"] = "Dam type selection", ["dam"] = "Main dam stability", ["spill"] = "Spillway capacity & gates", ["route"] = "Reservoir flood routing",
        ["basin"] = "Energy dissipation (stilling basin)", ["gate"] = "Radial gate & hoist", ["hp"] = "Hydropower chain & penstock", ["cost"] = "Quantities, BOQ & cost"
    };
    /// <summary>Input keys and upstream calculations (@code) each calculation depends on.</summary>
    public static readonly Dictionary<string, string[]> Dep = new()
    {
        ["top"] = new[] { "mwl", "wind", "fetch" },
        ["type"] = new[] { "rbl", "fdn", "len", "valley", "found", "zone", "frl", "mwl", "spc", "Q", "wind", "fetch", "type", "gc", "mu", "c", "up", "phi", "rConc", "rFill", "rRock", "bay", "pier", "Cd", "slN", "slS", "ot", "@top" },
        ["dam"] = new[] { "@type" },
        ["spill"] = new[] { "Q", "mwl", "spc", "bay", "pier", "Cd", "len", "@type" },
        ["route"] = new[] { "@spill", "Qp", "tp", "tb", "ea", "frl", "mwl", "spc", "Cd", "bay" },
        ["basin"] = new[] { "@spill", "rbl", "mwl", "twlf" },
        ["gate"] = new[] { "@spill", "frl", "spc", "gateW", "hoistV", "hoistEta" },
        ["hp"] = new[] { "hp", "Qd", "D", "Lp", "f", "eta", "aw", "Tc", "sig", "frl", "mddl", "twln" },
        ["cost"] = new[] { "@dam", "@spill", "@basin", "@gate", "@hp", "rConc", "rFill", "rRock", "rSteel", "rGate", "rEM", "ind", "cont", "esc", "tax" },
    };
    public static readonly Dictionary<string, string[]> Codes = new()
    {
        ["top"] = new[] { "IS6512", "IS10635" }, ["type"] = new[] { "USBR_DSD" }, ["spill"] = new[] { "IS11223", "IS6934" }, ["route"] = new[] { "IS11223", "CWC_DF" },
        ["basin"] = new[] { "IS4997", "USBR_EM25" }, ["gate"] = new[] { "IS4623", "IS6938" }, ["hp"] = new[] { "IS11639", "IS7396" }, ["cost"] = new[] { "SOR" },
    };

    /// <summary>Calculations affected by a change to <paramref name="key"/> (directly or through upstream calculations).</summary>
    public static List<string> Dependents(string key)
    {
        var outSet = new HashSet<string>(); var q = new Queue<string>(); q.Enqueue(key);
        while (q.Count > 0) { var k = q.Dequeue(); foreach (var c in Order) if (Dep[c].Contains(k) && outSet.Add(c)) q.Enqueue("@" + c); }
        return Order.Where(outSet.Contains).ToList();
    }

    public static string Path(string key, string target)
    {
        var seen = new HashSet<string>(); var q = new Queue<(string k, List<string> p)>(); q.Enqueue((key, new List<string> { key }));
        while (q.Count > 0) { var (k, p) = q.Dequeue(); foreach (var c in Order) if (Dep[c].Contains(k) && seen.Add(c)) { var np = new List<string>(p) { Titles[c] }; if (c == target) return string.Join(" → ", np); q.Enqueue(("@" + c, np)); } }
        return "";
    }

    private static string F(double v, int d = 2) => DesignMath.F(v, d);

    /// <summary>Runs all nine calculations in order (deterministic; a calculation whose inputs did not change gives the same record).</summary>
    public static Dictionary<string, CalcRecord> RunAll(DesignInputs i)
    {
        var R = new Dictionary<string, CalcRecord>();
        void Add(string code, CalcRecord r) { r.Code = code; r.Title = Titles[code]; if (Codes.TryGetValue(code, out var c) && r.Codes.Count == 0) r.Codes = c.ToList(); R[code] = r; }

        // 1. top of dam
        var hw = DesignMath.Wave(i.N("wind"), i.N("fetch")); double fbC = Math.Max(1.5 * hw, 1.5), fbE = Math.Max(1.5 * hw, 2.0);
        Add("top", new CalcRecord
        {
            Inputs = new() { ["MWL"] = $"{i.N("mwl")} m", ["Wind V"] = $"{i.N("wind")} km/h", ["Fetch F"] = $"{i.N("fetch")} km" },
            Assumptions = { "Wave height by Molitor relation, fetch < 32 km", "Freeboard = max(1.5 × wave height, minimum) — minimum 1.5 m concrete, 2.0 m embankment (screening values)", "Settlement camber for embankments added separately" },
            Method = "Empirical wave height and freeboard above MWL", Formula = "hw = 0.032√(V·F) + 0.763 − 0.271·F^0.25;  FB = max(1.5·hw, FBmin);  Top = MWL + FB",
            Steps = { $"hw = 0.032·√({i.N("wind")}×{i.N("fetch")}) + 0.763 − 0.271·{i.N("fetch")}^0.25 = {F(hw)} m", $"FB concrete = max(1.5×{F(hw)}, 1.5) = {F(fbC)} m → top {F(i.N("mwl") + fbC)} m", $"FB embankment = max(1.5×{F(hw)}, 2.0) = {F(fbE)} m → top {F(i.N("mwl") + fbE)} m" },
            Results = { new() { Label = "Wave height", Value = F(hw), Unit = "m" }, new() { Label = "Top of dam (concrete)", Value = F(i.N("mwl") + fbC), Unit = "m" }, new() { Label = "Top of dam (embankment)", Value = F(i.N("mwl") + fbE), Unit = "m" } },
            Interpretation = $"Top of dam is set {F(fbC)}–{F(fbE)} m above MWL depending on dam type.", Data = new { hw, fbC, fbE }
        });

        // 2. dam type selection
        var sp = DesignMath.Spill(i);
        var all = DesignMath.DamTypes.Select(t => DesignMath.Design(i, t)).ToList();
        foreach (var d in all) d.CostCmp = d.MainCost + (d.Type == "Concrete gravity" ? 0 : sp.Gross * 900 * i.N("rConc"));
        var minC = all.Where(d => d.Feasible).Select(d => d.CostCmp).DefaultIfEmpty(all.Min(d => d.CostCmp)).Min();
        foreach (var d in all) (d.Score, d.Why) = DesignMath.Score(i, d, minC);
        var rec = all.OrderByDescending(d => d.Score).First(); var sel = i.S("type") == "Auto" ? rec : all.First(d => d.Type == i.S("type"));
        Add("type", new CalcRecord
        {
            Inputs = new() { ["Foundation"] = i.S("found"), ["Valley"] = i.S("valley"), ["Seismic zone"] = i.S("zone"), ["Design flood"] = $"{i.N("Q")} m³/s", ["Selection mode"] = i.S("type") },
            Assumptions = { "Each type sized with rule-of-thumb sections and simplified stability (see Main dam stability)", "Suitability weights: foundation, valley shape, seismicity, flood handling, cost", "Material availability and environmental impact not yet scored" },
            Method = "Multi-criteria scoring of four sized alternatives", Formula = "Score = Σ criterion points + 20 × (lowest cost ÷ option cost); infeasible ≤ 8; failing checks −5",
            Steps = all.Select(d => $"{d.Type}: H {F(d.H, 1)} m, {d.Slopes}, cost ₹{F(d.CostCmp / 1e7, 0)} cr, score {d.Score}{(d.Feasible ? "" : " (not feasible)")}").ToList(),
            Results = { new() { Label = "Recommended", Value = rec.Type }, new() { Label = "Selected for design", Value = sel.Type }, new() { Label = "Suitability of selected", Value = sel.Score.ToString(), Unit = "/100" } },
            Interpretation = $"{rec.Type} scores highest ({rec.Score}/100): {string.Join("; ", rec.Why.Take(3))}.{(sel != rec ? " The user has selected " + sel.Type + " instead." : "")}",
            Data = new { alternatives = all.Select(d => new { d.Type, d.H, d.B, d.T, d.Slopes, cost = d.CostCmp, d.Feasible, d.Pass, d.Score, d.Notes }), recommended = rec.Type, selected = sel.Type }
        });

        // 3. main dam stability
        var g = sel.Type == "Concrete gravity";
        var damRec = new CalcRecord
        {
            Inputs = new() { ["Type"] = sel.Type, ["Top of dam"] = $"{F(sel.Crest)} m", ["Foundation level"] = $"{F(sel.Fl, 1)} m", ["αh"] = F(sel.K, 3) },
            Assumptions = g ? new() { "2-D rigid-body analysis per metre run of the maximum section", "Vertical upstream face; triangular uplift reduced by drains; drains-inoperative case G checked", "Seismic coefficient αh = (Z/2)·I with I = 1.5 — placeholder until site-specific study", "Westergaard hydrodynamic force (7/12)·αh·γw·h² at 0.4h", "Silt, tailwater and temperature loads not yet included" }
                : new() { "Infinite-slope pseudo-static analysis (screening only)", "Rapid drawdown: drained-shell factor 0.65 (zoned) / 0.5 (homogeneous)", "Rockfill φ = 48° assumed for CFRD", "Limit-equilibrium circular / wedge analysis required at Feasibility" },
            Codes = g ? new() { "IS6512", "IS1893_1" } : new() { "IS7894", "IS8826", "IS1893_1" },
            Method = g ? "Limit equilibrium — sliding (shear friction), overturning, middle third; base width iterated to satisfy all load cases" : "Infinite-slope stability; slopes flattened until all cases pass",
            Formula = g ? "FS_sliding = (μ·ΣV + c·B) / ΣH;  FS_overturning = ΣMr / ΣMo;  σ = (ΣV/B)(1 ± 6e/B)" : "FS = tanφ (cosβ − k sinβ) / (sinβ + k cosβ);  drawdown FS = r·tanφ / tanβ",
            Steps = new List<string> { $"Height H = {F(sel.H, 1)} m, top width {F(sel.T, 1)} m, base {F(sel.B, 1)} m ({sel.Slopes})" }.Concat(sel.Checks.Select(c => $"{c.Label}: {c.Value} (need ≥ {c.Limit})")).ToList(),
            Results = { new() { Label = "Maximum height", Value = F(sel.H, 1), Unit = "m" }, new() { Label = "Base width", Value = F(sel.B, 1), Unit = "m" }, new() { Label = "Section area", Value = F(sel.Area, 0), Unit = "m²" }, new() { Label = sel.VolLbl + " volume", Value = F(sel.Vol / 1e6, 2), Unit = "M m³" } },
            Checks = sel.Checks,
            Interpretation = g ? $"Base width {F(sel.B / sel.H)}H satisfies all {sel.Checks.Count} checks. Cohesion supplies {F(i.N("c") * sel.B / (i.N("mu") * sel.Normal!.V + i.N("c") * sel.B) * 100, 0)}% of normal-case sliding resistance — confirm by in-situ shear tests.{(sel.Seismic!.SHeel < 0 ? $" Seismic case shows heel tension of {F(-sel.Seismic.SHeel, 0)} kPa — check permissible tension for the extreme case." : "")}"
                : $"Slopes {sel.Slopes} satisfy the screening checks; detailed slope stability, seepage and filter design follow.",
            Data = new { sel.Type, sel.H, sel.B, sel.T, sel.Crest, sel.Fl, sel.Area, sel.Vol, sel.Slab, sel.Kind, sel.Us, sel.Ds, sel.Slopes, sel.VolLbl }
        };
        if (!sel.Feasible) damRec.Interpretation = string.Join(" ", sel.Notes) + " " + damRec.Interpretation;
        Add("dam", damRec);

        // 4. spillway
        var okW = g ? sp.Gross <= 0.7 * i.N("len") : sp.Gross <= 0.35 * i.N("len");
        Add("spill", new CalcRecord
        {
            Inputs = new() { ["Design outflow Q"] = $"{i.N("Q")} m³/s", ["MWL"] = $"{i.N("mwl")} m", ["Crest level"] = $"{i.N("spc")} m", ["Bay / pier"] = $"{i.N("bay")} m / {i.N("pier")} m", ["C"] = i.N("Cd").ToString(CultureInfo.InvariantCulture) },
            Assumptions = { "Design outflow from routing study (checked in Reservoir flood routing)", "Effective length losses at piers / abutments not yet deducted", "Gate height = (FRL − crest) + 0.5 m" },
            Method = "Ogee weir equation, gated bays", Formula = "Q = C · L · He^1.5  →  L = Q / (C · He^1.5);  He = MWL − crest",
            Steps = { $"He = {i.N("mwl")} − {i.N("spc")} = {F(sp.Hs)} m", $"L = {i.N("Q")} / ({i.N("Cd")} × {F(sp.Hs)}^1.5) = {F(sp.Ls, 1)} m", $"Bays = ceil({F(sp.Ls, 1)} / {i.N("bay")}) = {sp.Nb}; gross width = {F(sp.Gross, 0)} m" },
            Results = { new() { Label = "Head on crest", Value = F(sp.Hs), Unit = "m" }, new() { Label = "Effective length", Value = F(sp.Ls, 1), Unit = "m" }, new() { Label = "Gated bays", Value = sp.Nb.ToString() }, new() { Label = "Gross width", Value = F(sp.Gross, 0), Unit = "m" }, new() { Label = "Gate height", Value = F(sp.GateH, 1), Unit = "m" } },
            Checks = { new() { Label = "Head on crest within 3–15 m", Value = F(sp.Hs), Limit = "3–15", Ok = sp.Hs >= 3 && sp.Hs <= 15, Unit = "m" },
                       new() { Label = g ? "Overflow width ≤ 70% of crest length" : "Chute spillway width ≤ 35% of valley", Value = F(sp.Gross, 0), Limit = F((g ? .7 : .35) * i.N("len"), 0), Ok = okW, Unit = "m", Max = true } },
            Interpretation = $"{sp.Nb} radial gates of {i.N("bay")} m × {F(sp.GateH, 1)} m pass {F(i.N("Q"), 0)} m³/s at MWL.{(okW ? "" : " Width is large for the valley — raise the head or relocate the spillway.")} A largest-gate-inoperative case must be routed at Feasibility.",
            Data = sp
        });

        // 5. routing
        var r = DesignMath.Route(i, sp.Nb); var att = (1 - r.pkO / i.N("Qp")) * 100;
        Add("route", new CalcRecord
        {
            Inputs = new() { ["Inflow peak Qp"] = $"{i.N("Qp")} m³/s", ["Time to peak / base"] = $"{i.N("tp")} h / {i.N("tb")} h", ["Spillway"] = $"{sp.Nb} × {i.N("bay")} m, C {i.N("Cd")}, crest {i.N("spc")} m", ["Initial level"] = $"FRL {i.N("frl")} m", ["Elevation–area points"] = i.Ea.Count.ToString() },
            Assumptions = { "Triangular inflow hydrograph", "Gates hold FRL while free outflow would draw the level below FRL; above FRL all gates fully open", "Time step 0.5 h; storage from the elevation–area table (0.01 m integration)", "Pier contraction not deducted" },
            Method = "Storage-indication (modified Puls) reservoir routing", Formula = "(2S/Δt + O)ₙ₊₁ = Iₙ + Iₙ₊₁ + (2S/Δt − O)ₙ ;  O = C·(n·b)·(h − crest)^1.5",
            Steps = { $"Spillway capacity at FRL = {F(r.capFrl, 0)} m³/s", $"Inflow volume {F(r.vin / 1e6, 1)} Mm³; outflow {F(r.vout / 1e6, 1)} Mm³; storage change {F(r.send / 1e6, 2)} Mm³", $"Mass-balance error = {F(r.err, 3)} %", $"Peak outflow {F(r.pkO, 0)} m³/s at {F(r.tO, 1)} h; maximum level {F(r.maxH)} m" },
            Results = { new() { Label = "Peak inflow", Value = F(i.N("Qp"), 0), Unit = "m³/s" }, new() { Label = "Peak outflow", Value = F(r.pkO, 0), Unit = "m³/s" }, new() { Label = "Flood attenuation", Value = F(att, 1), Unit = "%" }, new() { Label = "Maximum reservoir level", Value = F(r.maxH), Unit = "m" }, new() { Label = "Mass-balance error", Value = F(r.err, 3), Unit = "%" } },
            Checks = { new() { Label = "Maximum level ≤ MWL", Value = F(r.maxH), Limit = F(i.N("mwl")), Ok = r.maxH <= i.N("mwl") + 1e-6, Unit = "m", Max = true },
                       new() { Label = "Peak outflow ≤ spillway design outflow", Value = F(r.pkO, 0), Limit = F(i.N("Q"), 0), Ok = r.pkO <= i.N("Q") * 1.0001, Unit = "m³/s", Max = true },
                       new() { Label = "Mass balance error ≤ 1 %", Value = F(r.err, 3), Limit = "1", Ok = r.err <= 1, Unit = "%", Max = true } },
            Interpretation = $"The {F(i.N("Qp"), 0)} m³/s flood is attenuated {F(att, 0)}% to {F(r.pkO, 0)} m³/s; the reservoir peaks at {F(r.maxH)} m, {F(i.N("mwl") - r.maxH)} m {(r.maxH <= i.N("mwl") ? "below" : "ABOVE")} MWL.{(r.pkO > i.N("Q") ? " Routed outflow exceeds the spillway design outflow — increase Q or spillway length." : "")}{(r.over ? " Level exceeded the routing table range — check inputs." : "")}",
            Data = new { series = r.ser.Where((_, k) => k % 2 == 0).ToList(), r.pkO, r.maxH, r.err }
        });

        // 6. stilling basin
        double q = i.N("Q") / (sp.Nb * i.N("bay")), E = i.N("mwl") - i.N("rbl"), y1 = 0.5;
        for (var k = 0; k < 60; k++) { var V = 0.9 * Math.Sqrt(2 * DesignMath.G * Math.Max(E - y1, 0.1)); y1 = q / V; }
        double V1 = q / y1, F1 = V1 / Math.Sqrt(DesignMath.G * y1), y2 = y1 / 2 * (Math.Sqrt(1 + 8 * F1 * F1) - 1), Lj = 6 * (y2 - y1), dE = Math.Pow(y2 - y1, 3) / (4 * y1 * y2), tw = i.N("twlf") - i.N("rbl"), lower = Math.Max(0, y2 - tw), floor = i.N("rbl") - lower, bvol = Lj * sp.Gross * 2 + 2 * Lj * (y2 + 1.5) * 1.5;
        var btype = F1 < 4.5 ? "Transition range (F1 < 4.5) — consider bucket or alternative" : F1 <= 9 ? "Hydraulic-jump stilling basin" : "High Froude — consider ski-jump / roller bucket (rock dependent)";
        Add("basin", new CalcRecord
        {
            Inputs = new() { ["Unit discharge q"] = $"{F(q, 1)} m²/s", ["Upstream energy above floor"] = $"{F(E, 1)} m", ["Tailwater (flood)"] = $"{i.N("twlf")} m", ["Floor (initial)"] = $"{i.N("rbl")} m" },
            Assumptions = { "Basin floor initially at river bed level", "10% energy loss on the spillway face", "Jump length ≈ 6(y2 − y1) (indicative); slab 2 m and walls 1.5 m thick for quantity" },
            Method = "Energy equation + Bélanger sequent depth", Formula = "V1 = 0.9√(2g(E − y1)), y1 = q/V1;  F1 = V1/√(g·y1);  y2 = ½y1(√(1+8F1²) − 1);  ΔE = (y2−y1)³/(4y1y2)",
            Steps = { $"q = {i.N("Q")} / ({sp.Nb}×{i.N("bay")}) = {F(q)} m²/s", $"Iterating: y1 = {F(y1, 3)} m, V1 = {F(V1)} m/s, F1 = {F(F1)}", $"y2 = {F(y2)} m; jump length ≈ {F(Lj, 1)} m; energy loss {F(dE, 1)} m", $"Available tailwater depth = {F(tw)} m {(tw >= y2 ? "≥" : "<")} y2" },
            Results = { new() { Label = "Froude number F1", Value = F(F1) }, new() { Label = "Sequent depth y2", Value = F(y2), Unit = "m" }, new() { Label = "Basin length", Value = F(Lj, 1), Unit = "m" }, new() { Label = "Basin floor level", Value = F(floor), Unit = "m" }, new() { Label = "Concrete (indicative)", Value = F(bvol, 0), Unit = "m³" } },
            Checks = { new() { Label = "Tailwater depth ≥ sequent depth", Value = F(tw), Limit = F(y2), Ok = tw >= y2, Unit = "m" }, new() { Label = "Froude number in jump-basin range 4.5–9", Value = F(F1), Limit = "4.5–9", Ok = F1 >= 4.5 && F1 <= 9 } },
            Interpretation = $"{btype}. {(tw < y2 ? $"Tailwater is {F(y2 - tw)} m short — lower the floor to {F(floor)} m or add end sill / baffles." : "Tailwater is sufficient at the design flood; check the full discharge range at Feasibility.")}",
            Data = new { q, y1, V1, F1, y2, Lj, floor, vol = bvol }
        });

        // 7. gate & hoist
        double hwg = i.N("frl") - i.N("spc"), P = .5 * DesignMath.GW * hwg * hwg * i.N("bay"), Rr = 1.25 * sp.GateH, fr = 0.3 * P * 0.25 / Rr, Fh = 1.2 * (i.N("gateW") + fr), kW = Fh * (i.N("hoistV") / 60) / i.N("hoistEta"), gsteel = i.N("gateW") / DesignMath.G * sp.Nb;
        Add("gate", new CalcRecord
        {
            Inputs = new() { ["Water depth on gate"] = $"{F(hwg)} m", ["Gate (b × h)"] = $"{i.N("bay")} × {F(sp.GateH, 1)} m", ["Gate weight (assumed)"] = $"{i.N("gateW")} kN", ["Hoist speed"] = $"{i.N("hoistV")} m/min" },
            Assumptions = { "Radius R = 1.25 × gate height; trunnion pin radius 0.25 m; trunnion friction μ 0.3; seal friction and downpull included in 20% margin — all preliminary", "Gate weight is an input until the structural gate design is done", "Two-point hoist; force shown is total" },
            Method = "Hydrostatic thrust and hoisting force balance", Formula = "P = ½·γw·h²·b;  F = 1.2·(W + μ·P·r/R);  Power = F·v/η",
            Steps = { $"P = 0.5 × 9.81 × {F(hwg)}² × {i.N("bay")} = {F(P, 0)} kN", $"Trunnion friction = 0.3 × {F(P, 0)} × 0.25 / {F(Rr)} = {F(fr, 0)} kN", $"F = 1.2 × ({i.N("gateW")} + {F(fr, 0)}) = {F(Fh, 0)} kN", $"Power = {F(Fh, 0)} × {F(i.N("hoistV") / 60, 4)} / {i.N("hoistEta")} = {F(kW, 1)} kW" },
            Results = { new() { Label = "Hydrostatic thrust per gate", Value = F(P, 0), Unit = "kN" }, new() { Label = "Trunnion reaction (each side)", Value = F(P / 2, 0), Unit = "kN" }, new() { Label = "Hoisting force", Value = F(Fh, 0), Unit = "kN" }, new() { Label = "Motor power per gate", Value = F(kW, 1), Unit = "kW" }, new() { Label = "Gate steel (all gates)", Value = F(gsteel, 0), Unit = "t" } },
            Interpretation = $"{sp.Nb} gates need about {F(kW, 1)} kW each ({F(kW * sp.Nb, 0)} kW total) — standby DG must cover simultaneous operation per the gate operation schedule. Trunnion reactions pass to the pier design.",
            Data = new { P, F = Fh, kW, steel = gsteel }
        });

        // 8. hydropower
        double MW = 0, psteel = 0;
        if (i.S("hp") != "Yes") Add("hp", new CalcRecord { Inputs = new() { ["Hydropower"] = "Not included" }, Method = "—", Formula = "—", Steps = { "Hydropower switched off for this project" }, Interpretation = "Not applicable.", Data = new { off = true, MW = 0.0, steel = 0.0 } });
        else
        {
            double A = Math.PI * i.N("D") * i.N("D") / 4, V = i.N("Qd") / A, Hgx = i.N("frl") - i.N("twln"), Hgn = i.N("mddl") - i.N("twln"), Hr = 0.67 * i.N("frl") + 0.33 * i.N("mddl") - i.N("twln"),
                hf = (i.N("f") * i.N("Lp") / i.N("D") + 1.5) * V * V / (2 * DesignMath.G), Hn = Hr - hf, tcr = 2 * i.N("Lp") / i.N("aw"),
                dh = i.N("Tc") <= tcr ? i.N("aw") * V / DesignMath.G : 2 * i.N("Lp") * V / (DesignMath.G * i.N("Tc")), p = DesignMath.GW * (Hgx + dh), t = p / 1000 * i.N("D") * 1000 / (2 * i.N("sig") * 0.9) + 2, Tw = i.N("Lp") * V / (DesignMath.G * Hn);
            MW = DesignMath.G * i.N("Qd") * Hn * i.N("eta") / 1000; psteel = Math.PI * i.N("D") * (t / 1000) * i.N("Lp") * 7.85;
            var tur = Hn > 300 ? "Pelton" : Hn >= 40 ? "Francis" : Hn >= 30 ? "Francis or Kaplan (overlap zone)" : "Kaplan / bulb"; var nU = Math.Max(2, (int)Math.Ceiling(MW / 60));
            Add("hp", new CalcRecord
            {
                Inputs = new() { ["Design discharge"] = $"{i.N("Qd")} m³/s", ["Penstock D × L"] = $"{i.N("D")} m × {i.N("Lp")} m", ["f"] = i.N("f").ToString(CultureInfo.InvariantCulture), ["Closure time"] = $"{i.N("Tc")} s", ["Wave speed a"] = $"{i.N("aw")} m/s" },
                Assumptions = { "Rated head at 2/3 FRL + 1/3 MDDL weighting", "Local losses = 1.5 velocity heads (entrance, bends, valve)", "Water hammer: Joukowsky for rapid closure, Michaud otherwise; surge tank effect not included", "Joint efficiency 0.9; corrosion allowance 2 mm; allowable stress is an input", "Turbine type from indicative head ranges — OEM to confirm" },
                Method = "Energy equation, Darcy–Weisbach losses, P = ρgQHη, water hammer, hoop stress", Formula = "hf = (f·L/D + Σk)·V²/2g;  P = ρ·g·Q·Hn·η;  Δh = a·V/g (T ≤ 2L/a) or 2LV/(gT);  t = p·D/(2σ·ηj) + c",
                Steps = { $"V = {i.N("Qd")} / {F(A)} = {F(V)} m/s", $"Head loss hf = {F(hf)} m", $"Rated gross head {F(Hr, 1)} m → net head {F(Hn, 1)} m (range {F(Hgn, 1)}–{F(Hgx, 1)} m gross)", $"P = 9.81 × {i.N("Qd")} × {F(Hn, 1)} × {i.N("eta")} = {F(MW, 1)} MW", $"2L/a = {F(tcr)} s; closure {i.N("Tc")} s → Δh = {F(dh, 1)} m", $"Design pressure = {F(p, 0)} kPa → t = {F(t, 1)} mm", $"Water starting time Tw = {F(Tw)} s" },
                Results = { new() { Label = "Net head (rated)", Value = F(Hn, 1), Unit = "m" }, new() { Label = "Installed capacity", Value = F(MW, 1), Unit = "MW" }, new() { Label = "Units (indicative)", Value = nU.ToString() }, new() { Label = "Turbine type", Value = tur }, new() { Label = "Water-hammer rise", Value = F(dh, 1), Unit = "m" }, new() { Label = "Penstock shell thickness", Value = F(t, 1), Unit = "mm" }, new() { Label = "Penstock steel", Value = F(psteel, 0), Unit = "t" } },
                Checks = { new() { Label = "Penstock velocity ≤ 7 m/s (screening)", Value = F(V), Limit = "7", Ok = V <= 7, Unit = "m/s", Max = true }, new() { Label = "Water starting time Tw ≤ 3 s (else surge study)", Value = F(Tw), Limit = "3", Ok = Tw <= 3, Unit = "s", Max = true } },
                Interpretation = $"{F(MW, 1)} MW from {nU} {tur} units at {F(Hn, 1)} m net head. {(Tw > 3 ? "Tw above 3 s — a surge tank / transient study is needed." : "Surge relief likely not required; confirm with transient analysis.")}",
                Data = new { MW, steel = psteel, Hn, dh, t, V, Tw }
            });
        }

        // 9. cost
        var lines = new List<(string sys, string str, string item, double qty, string unit, double rate)>
        {
            ("Main dam", sel.Type, sel.VolLbl + " body", sel.Vol, "m³", g ? i.N("rConc") : sel.Kind == "r" ? i.N("rRock") : i.N("rFill"))
        };
        if (sel.Slab > 0) lines.Add(("Main dam", sel.Type, "Concrete face slab", sel.Slab, "m³", i.N("rConc")));
        if (!g) lines.Add(("Spillway & gates", "Chute spillway", "Spillway concrete (parametric 900 m³/m)", sp.Gross * 900, "m³", i.N("rConc")));
        lines.Add(("Spillway & gates", "Radial gates", "Gate & hoist steel", gsteel, "t", i.N("rGate")));
        lines.Add(("Energy dissipation", "Stilling basin", "Basin concrete", bvol, "m³", i.N("rConc")));
        if (i.S("hp") == "Yes") { lines.Add(("Hydropower", "Penstock", "Penstock steel", psteel, "t", i.N("rSteel"))); lines.Add(("Hydropower", "Powerhouse", "E&M equipment (parametric)", MW, "MW", i.N("rEM") * 1e7)); }
        double direct = lines.Sum(l => l.qty * l.rate), ind = direct * i.N("ind") / 100, cont = (direct + ind) * i.N("cont") / 100, esc = (direct + ind + cont) * i.N("esc") / 100, tax = (direct + ind + cont + esc) * i.N("tax") / 100, total = direct + ind + cont + esc + tax;
        string Cr(double x) => F(x / 1e7, 1);
        var costRec = new CalcRecord
        {
            Inputs = new() { ["Rates"] = "see Rates & cost group", ["Indirect / contingency / escalation / taxes"] = $"{i.N("ind")}% / {i.N("cont")}% / {i.N("esc")}% / {i.N("tax")}%" },
            Assumptions = { "Quantities generated from design geometry (AUTO); rates are user inputs", "Excavation, foundation treatment, powerhouse civil, roads and land not yet included", "E&M cost is parametric per MW until OEM quotes" },
            Method = "Quantity × rate, then cost build-up", Formula = "Total = Direct + Indirect + Contingency + Escalation + Taxes",
            Steps = lines.Select(l => $"{l.item}: {F(l.qty, 0)} {l.unit} × ₹{F(l.rate, 0)} = ₹{Cr(l.qty * l.rate)} cr [AUTO]").Append($"Direct ₹{Cr(direct)} cr + indirect ₹{Cr(ind)} + contingency ₹{Cr(cont)} + escalation ₹{Cr(esc)} + taxes ₹{Cr(tax)} = ₹{Cr(total)} cr").ToList(),
            Results = { new() { Label = "Direct cost", Value = Cr(direct), Unit = "₹ cr" }, new() { Label = "Total project cost (modelled scope)", Value = Cr(total), Unit = "₹ cr" } },
            Interpretation = $"Modelled scope totals ₹{F(total / 1e7, 0)} crore at preliminary level (±30%). Largest item: {lines.OrderByDescending(l => l.qty * l.rate).First().item}.",
            Data = new { lines = lines.Select(l => new { l.sys, l.str, l.item, l.qty, l.unit, l.rate, amount = l.qty * l.rate }), direct, ind, cont, esc, tax, total }
        };
        if (MW > 0) costRec.Results.Add(new() { Label = "Cost per MW (whole modelled scope)", Value = F(total / 1e7 / MW), Unit = "₹ cr/MW" });
        Add("cost", costRec);
        return R;
    }

    /// <summary>Consistency rules applied before any input change is accepted (FR-DB-02).</summary>
    public static string? Inconsistency(DesignInputs i)
    {
        if (!(i.N("mwl") >= i.N("frl"))) return "MWL must be at or above FRL";
        if (!(i.N("frl") > i.N("mddl"))) return "FRL must be above MDDL";
        if (!(i.N("frl") > i.N("spc"))) return "Spillway crest must be below FRL";
        if (!(i.N("spc") > i.N("rbl"))) return "Spillway crest must be above river bed";
        if (!(i.N("twlf") >= i.N("twln"))) return "Flood tailwater must be ≥ normal tailwater";
        if (!(i.N("mddl") > i.N("twln"))) return "MDDL must be above tailwater for hydropower";
        if (!(i.N("tb") > i.N("tp") && i.N("tp") > 0)) return "Flood base time must exceed time to peak (> 0)";
        if (!(i.N("Qp") > 0 && i.N("Q") > 0)) return "Flood peak and design outflow must be positive";
        if (i.Ea.Count < 2) return "Elevation–area table needs at least two rows";
        var ea = i.Ea.OrderBy(r => r[0]).ToList();
        if (ea.Select(r => r[0]).Distinct().Count() != ea.Count) return "Elevation–area table: elevations must be unique";
        if (ea.Where((r, k) => k > 0 && r[1] < ea[k - 1][1]).Any()) return "Elevation–area table: area must not decrease with elevation";
        return null;
    }
}
