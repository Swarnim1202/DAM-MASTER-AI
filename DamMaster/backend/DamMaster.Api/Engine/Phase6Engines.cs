using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Dapper;
using DamMaster.Api.Common;
using DamMaster.Api.DAL;
using DamMaster.Api.Models;

namespace DamMaster.Api.Engine;

// ===================== models =====================
public class SurveyDataset { public string Id { get; set; } = ""; public string DamId { get; set; } = ""; public string Kind { get; set; } = ""; public string Name { get; set; } = ""; public string? Provider { get; set; } public DateTime? EpochDate { get; set; } public DateTime? PeriodFrom { get; set; } public DateTime? PeriodTo { get; set; } public string Unit { get; set; } = ""; public double? Threshold { get; set; } public int PointCount { get; set; } public string? Notes { get; set; } public string? CreatedBy { get; set; } public DateTime CreatedAt { get; set; } public string? DamName { get; set; } }
public class SurveyPointRow { public string PointId { get; set; } = ""; public decimal Latitude { get; set; } public decimal Longitude { get; set; } public double Value { get; set; } public double? Coherence { get; set; } public string? Label { get; set; } }
public class SurveyImportRequest { public string DamId { get; set; } = ""; public string Kind { get; set; } = "InSAR"; public string Name { get; set; } = ""; public string? Provider { get; set; } public DateTime? EpochDate { get; set; } public DateTime? PeriodFrom { get; set; } public DateTime? PeriodTo { get; set; } public double? Threshold { get; set; } public string? Notes { get; set; } public List<SurveyPointRow> Points { get; set; } = new(); }
public class DocFile { public string Id { get; set; } = ""; public string DocumentId { get; set; } = ""; public string DamId { get; set; } = ""; public string FileName { get; set; } = ""; public string? ContentType { get; set; } public long SizeBytes { get; set; } public string Sha256 { get; set; } = ""; public string StoragePath { get; set; } = ""; public int? Pages { get; set; } public int TextChars { get; set; } public bool ExtractedOk { get; set; } public string? ExtractNote { get; set; } public bool OcrUsed { get; set; } public string? CreatedBy { get; set; } public DateTime CreatedAt { get; set; } }
public class SearchHit { public long Id { get; set; } public string FileId { get; set; } = ""; public string DocumentId { get; set; } = ""; public string DamId { get; set; } = ""; public int Seq { get; set; } public int? Page { get; set; } public string Text { get; set; } = ""; public double Score { get; set; } public string? Title { get; set; } public string? DocType { get; set; } public string? DamName { get; set; } public string? FileName { get; set; } }
public record AskRequest(string Question, string? DamId);
public record ApplySuggestionsRequest(string ProjectId, List<string> SensorIds);

// ===================== surveys (InSAR / campaigns) =====================
public class Cls_SurveyService : Cls_DbBase
{
    private static double Dist(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000; double r(double d) => d * Math.PI / 180;
        var a = Math.Pow(Math.Sin(r(lat2 - lat1) / 2), 2) + Math.Cos(r(lat1)) * Math.Cos(r(lat2)) * Math.Pow(Math.Sin(r(lon2 - lon1) / 2), 2);
        return 2 * R * Math.Asin(Math.Sqrt(a));
    }

    public async Task<object> ImportAsync(SurveyImportRequest req, string actor)
    {
        if (req.Kind is not ("InSAR" or "Survey")) throw new BusinessException("Kind must be InSAR or Survey.");
        if (string.IsNullOrWhiteSpace(req.Name)) throw new BusinessException("Dataset name is required.");
        var dam = await new Cls_DamDAL().GetAsync(req.DamId) ?? throw new BusinessException("Dam not found.");
        if (req.Points.Count == 0) throw new BusinessException("No points in the file.");
        if (req.Points.Count > 200_000) throw new BusinessException("Maximum 200 000 points per dataset.");
        var bad = req.Points.Where(p => string.IsNullOrWhiteSpace(p.PointId) || Math.Abs(p.Latitude) > 90 || Math.Abs(p.Longitude) > 180 || double.IsNaN(p.Value)).Take(5).ToList();
        if (bad.Count > 0) throw new BusinessException($"Invalid points (missing id or coordinates out of range), e.g. {string.Join(", ", bad.Select(b => b.PointId))}.");
        if (req.Points.Select(p => p.PointId).Distinct().Count() != req.Points.Count) throw new BusinessException("Point ids must be unique within a dataset.");
        if (req.Kind == "Survey" && req.EpochDate == null) throw new BusinessException("Survey campaigns need an epoch (survey) date.");
        var d = new SurveyDataset { Id = Ids.New(), DamId = dam.Id, Kind = req.Kind, Name = req.Name.Trim(), Provider = req.Provider, EpochDate = req.EpochDate, PeriodFrom = req.PeriodFrom, PeriodTo = req.PeriodTo,
            Unit = req.Kind == "InSAR" ? "mm/year" : "m", Threshold = req.Kind == "InSAR" ? req.Threshold ?? 10 : null, PointCount = req.Points.Count, Notes = req.Notes, CreatedBy = actor };
        using var c = Conn(); await c.OpenAsync(); using var tx = await c.BeginTransactionAsync();
        await c.ExecuteAsync(@"INSERT INTO survey_dataset (id, dam_id, kind, name, provider, epoch_date, period_from, period_to, unit, threshold, point_count, notes, created_by, created_at)
            VALUES (@Id, @DamId, @Kind, @Name, @Provider, @EpochDate, @PeriodFrom, @PeriodTo, @Unit, @Threshold, @PointCount, @Notes, @CreatedBy, UTC_TIMESTAMP())", d, tx);
        foreach (var chunk in req.Points.Chunk(1000))
            await c.ExecuteAsync("INSERT INTO survey_point (dataset_id, point_id, latitude, longitude, value, coherence, label) VALUES (@ds, @PointId, @Latitude, @Longitude, @Value, @Coherence, @Label)",
                chunk.Select(p => new { ds = d.Id, p.PointId, p.Latitude, p.Longitude, p.Value, p.Coherence, p.Label }), tx);
        await tx.CommitAsync();
        await new Cls_AuditDAL().WriteAsync(null, actor, "Remote sensing", $"Imported {d.Kind} dataset '{d.Name}' ({d.PointCount} points)", "survey_dataset", d.Id);

        object? alert = null;
        if (d.Kind == "InSAR")
        {
            var lim = d.Threshold!.Value;
            var fast = req.Points.Where(p => Math.Abs(p.Value) > lim).ToList();
            if (fast.Count > 0)
            {
                var near = dam.Latitude.HasValue && dam.Longitude.HasValue
                    ? fast.Count(p => Dist((double)dam.Latitude, (double)dam.Longitude, (double)p.Latitude, (double)p.Longitude) <= AppConfig.StructureRadiusM) : (int?)null;
                var worst = fast.OrderByDescending(p => Math.Abs(p.Value)).First();
                var a = new AlertView { Id = Ids.New(), DamId = dam.Id, SurveyId = d.Id, Level = 1, RuleType = "InSAR",
                    Title = $"InSAR: {fast.Count} point(s) moving faster than {lim} mm/year ({d.Name})",
                    Detail = $"Fastest: {worst.PointId} {worst.Value:0.0} mm/year." + (near.HasValue ? $" {near} of them within {AppConfig.StructureRadiusM:0} m of the dam; {fast.Count - near} on the rim / surroundings." : " Dam coordinates not set — distance to the structure unknown.") + " Compare with GNSS / levelling before acting.",
                    Value = worst.Value, LimitValue = lim, FirstAt = DateTime.UtcNow, LastAt = DateTime.UtcNow, Status = "Open" };
                await new Cls_AlertDAL().InsertAsync(a);
                await new Cls_NotificationEngine().OnAlertAsync(a.Id, "InSAR dataset imported");
                alert = new { a.Id, a.Title };
            }
        }
        return new { d.Id, d.Kind, d.PointCount, alert };
    }

    public async Task<List<SurveyDataset>> ListAsync(string? damId)
    { using var c = Conn(); return (await c.QueryAsync<SurveyDataset>("SELECT s.*, d.name AS DamName FROM survey_dataset s LEFT JOIN dam d ON d.id=s.dam_id" + (damId != null ? " WHERE s.dam_id=@damId" : "") + " ORDER BY s.created_at DESC", new { damId })).ToList(); }

    public async Task<(SurveyDataset? ds, List<SurveyPointRow> pts)> GetAsync(string id)
    {
        using var c = Conn();
        var ds = await c.QueryFirstOrDefaultAsync<SurveyDataset>("SELECT s.*, d.name AS DamName FROM survey_dataset s LEFT JOIN dam d ON d.id=s.dam_id WHERE s.id=@id", new { id });
        var pts = (await c.QueryAsync<SurveyPointRow>("SELECT point_id, latitude, longitude, value, coherence, label FROM survey_point WHERE dataset_id=@id ORDER BY point_id LIMIT 50000", new { id })).ToList();
        return (ds, pts);
    }

    /// <summary>Displacement between two survey campaigns of the same dam, matched by point id (later − earlier).</summary>
    public async Task<object> CompareAsync(string a, string b)
    {
        var (da, pa) = await GetAsync(a); var (db, pb) = await GetAsync(b);
        if (da == null || db == null) throw new BusinessException("Dataset not found.");
        if (da.Kind != "Survey" || db.Kind != "Survey" || da.DamId != db.DamId) throw new BusinessException("Choose two survey campaigns of the same dam.");
        if (da.EpochDate > db.EpochDate) { (da, db) = (db, da); (pa, pb) = (pb, pa); }
        var years = Math.Max(1e-6, (db.EpochDate!.Value - da.EpochDate!.Value).TotalDays / 365.25);
        var mb = pb.ToDictionary(p => p.PointId);
        var rows = pa.Where(p => mb.ContainsKey(p.PointId)).Select(p => { var q = mb[p.PointId]; var dmm = (q.Value - p.Value) * 1000; return new { p.PointId, p.Label, p.Latitude, p.Longitude, from = p.Value, to = q.Value, changeMm = Math.Round(dmm, 1), ratePerYear = Math.Round(dmm / years, 1) }; })
            .OrderBy(r => r.changeMm).ToList();
        return new { from = new { da.Id, da.Name, da.EpochDate }, to = new { db.Id, db.Name, db.EpochDate }, years = Math.Round(years, 2), matched = rows.Count, onlyInFirst = pa.Count - rows.Count, onlyInSecond = pb.Count - rows.Count,
            maxSettlementMm = rows.Count > 0 ? rows.Min(r => r.changeMm) : 0, maxHeaveMm = rows.Count > 0 ? rows.Max(r => r.changeMm) : 0, rows };
    }
}

// ===================== documents: storage, text extraction, chunking =====================
public class Cls_DocIndexer : Cls_DbBase
{
    public static readonly string[] Allowed = { ".pdf", ".docx", ".txt", ".md", ".csv", ".html", ".htm", ".png", ".jpg", ".jpeg" };
    private const int ChunkChars = 900;

    public async Task<DocFile> StoreAsync(string damId, string documentId, IFormFile file, string actor)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!Allowed.Contains(ext)) throw new BusinessException("Allowed file types: " + string.Join(", ", Allowed) + ".");
        if (file.Length == 0 || file.Length > AppConfig.MaxUploadBytes) throw new BusinessException($"File must be between 1 byte and {AppConfig.MaxUploadBytes / 1_000_000} MB.");
        using var ms = new MemoryStream(); await file.CopyToAsync(ms); var bytes = ms.ToArray();
        var f = new DocFile { Id = Ids.New(), DocumentId = documentId, DamId = damId, FileName = Path.GetFileName(file.FileName), ContentType = file.ContentType, SizeBytes = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), CreatedBy = actor };
        var dir = Path.Combine(AppConfig.StorageRoot, "docs"); Directory.CreateDirectory(dir);
        f.StoragePath = Path.Combine(dir, f.Id + ext); await File.WriteAllBytesAsync(f.StoragePath, bytes);
        List<(int? page, string text)> pages;
        try { pages = Extract(bytes, ext); f.ExtractedOk = pages.Sum(p => p.text.Length) > 0; f.ExtractNote = f.ExtractedOk ? null : "No text found — scanned document? OCR is needed to make it searchable."; }
        catch (Exception ex) { pages = new(); f.ExtractNote = "Text extraction failed: " + (ex.Message.Length > 200 ? ex.Message[..200] : ex.Message); }
        if (!f.ExtractedOk && AppConfig.OcrEnabled && ext is ".pdf" or ".png" or ".jpg" or ".jpeg")     // Phase 7: OCR for scanned documents
        {
            try { pages = await Cls_Ocr.RunAsync(f.StoragePath, ext); f.OcrUsed = true; f.ExtractedOk = pages.Sum(p => p.text.Length) > 0; f.ExtractNote = f.ExtractedOk ? "Text recognised by OCR — check accuracy before relying on it." : "OCR found no text."; }
            catch (Exception ex) { f.ExtractNote = "OCR failed: " + (ex.Message.Length > 200 ? ex.Message[..200] : ex.Message); }
        }
        f.Pages = ext == ".pdf" ? pages.Count : null; f.TextChars = pages.Sum(p => p.text.Length);
        using var c = Conn(); await c.OpenAsync(); using var tx = await c.BeginTransactionAsync();
        await c.ExecuteAsync(@"INSERT INTO doc_file (id, document_id, dam_id, file_name, content_type, size_bytes, sha256, storage_path, pages, text_chars, extracted_ok, extract_note, ocr_used, created_by, created_at)
            VALUES (@Id, @DocumentId, @DamId, @FileName, @ContentType, @SizeBytes, @Sha256, @StoragePath, @Pages, @TextChars, @ExtractedOk, @ExtractNote, @OcrUsed, @CreatedBy, UTC_TIMESTAMP())", f, tx);
        var seq = 0;
        foreach (var (page, text) in pages)
            foreach (var chunk in Chunk(text))
                await c.ExecuteAsync("INSERT INTO doc_chunk (file_id, document_id, dam_id, seq, page, text) VALUES (@fid, @did, @dam, @seq, @page, @chunk)", new { fid = f.Id, did = documentId, dam = damId, seq = seq++, page, chunk }, tx);
        await tx.CommitAsync();
        return f;
    }

    public static List<(int? page, string text)> Extract(byte[] bytes, string ext)
    {
        switch (ext)
        {
            case ".pdf":
                using (var pdf = UglyToad.PdfPig.PdfDocument.Open(bytes))
                    return pdf.GetPages().Select(p => ((int?)p.Number, Clean(string.Join(" ", p.GetWords().Select(w => w.Text))))).ToList();
            case ".docx":
                using (var zip = new ZipArchive(new MemoryStream(bytes)))
                {
                    var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("Not a Word document.");
                    using var s = entry.Open(); var xml = XDocument.Load(s); XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                    var paras = xml.Descendants(w + "p").Select(p => string.Concat(p.Descendants(w + "t").Select(t => t.Value)));
                    return new() { (null, Clean(string.Join("\n", paras))) };
                }
            case ".png": case ".jpg": case ".jpeg":
                return new();                                                   // images: text only through OCR
            case ".html": case ".htm":
                var html = Encoding.UTF8.GetString(bytes);
                html = Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                return new() { (null, Clean(System.Net.WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")))) };
            default:
                return new() { (null, Clean(Encoding.UTF8.GetString(bytes))) };
        }
    }

    private static string Clean(string s) => Regex.Replace(s.Replace('\u00A0', ' '), @"[ \t\r\f\v]+", " ").Trim();

    /// <summary>Passages of about 900 characters, split at sentence or word boundaries.</summary>
    public static IEnumerable<string> Chunk(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var len = Math.Min(ChunkChars, text.Length - i);
            if (i + len < text.Length)
            {
                var cut = text.LastIndexOfAny(new[] { '.', '\n' }, i + len - 1, len); if (cut > i + ChunkChars / 2) len = cut - i + 1;
                else { var sp = text.LastIndexOf(' ', i + len - 1, len); if (sp > i + ChunkChars / 2) len = sp - i; }
            }
            var piece = text.Substring(i, len).Trim(); if (piece.Length > 20) yield return piece; i += len;
        }
    }

    public async Task<List<DocFile>> FilesAsync(string documentId) { using var c = Conn(); return (await c.QueryAsync<DocFile>("SELECT * FROM doc_file WHERE document_id=@documentId ORDER BY created_at DESC", new { documentId })).ToList(); }
    public async Task<List<DocFile>> FilesForDamAsync(string damId) { using var c = Conn(); return (await c.QueryAsync<DocFile>("SELECT * FROM doc_file WHERE dam_id=@damId ORDER BY created_at DESC", new { damId })).ToList(); }
    public async Task<DocFile?> FileAsync(string id) { using var c = Conn(); return await c.QueryFirstOrDefaultAsync<DocFile>("SELECT * FROM doc_file WHERE id=@id", new { id }); }

    /// <summary>MySQL full-text (natural language) search over passages; every hit carries its document, dam and page.</summary>
    public async Task<List<SearchHit>> SearchAsync(string q, string? damId, int take)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 3) throw new BusinessException("Type at least 3 characters.");
        using var c = Conn();
        return (await c.QueryAsync<SearchHit>(@"SELECT k.id, k.file_id AS FileId, k.document_id AS DocumentId, k.dam_id AS DamId, k.seq, k.page, k.text, MATCH(k.text) AGAINST (@q IN NATURAL LANGUAGE MODE) AS Score,
                d.title, d.doc_type AS DocType, m.name AS DamName, f.file_name AS FileName
            FROM doc_chunk k JOIN dam_document d ON d.id=k.document_id LEFT JOIN dam m ON m.id=k.dam_id LEFT JOIN doc_file f ON f.id=k.file_id
            WHERE MATCH(k.text) AGAINST (@q IN NATURAL LANGUAGE MODE)" + (damId != null ? " AND k.dam_id=@damId" : "") + " ORDER BY Score DESC LIMIT @take", new { q = q.Trim(), damId, take })).ToList();
    }
}

/// <summary>"Ask the library": retrieval of passages + optional answer from an OpenAI-compatible endpoint (e.g. a self-hosted model).</summary>
public class Cls_LibraryAi
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    public async Task<object> AskAsync(AskRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Question)) throw new BusinessException("Type a question.");
        var hits = await new Cls_DocIndexer().SearchAsync(req.Question, req.DamId, 6);
        var sources = hits.Select((h, k) => new { n = k + 1, h.Title, h.DocType, h.DamName, h.Page, h.FileId, h.FileName, text = h.Text }).ToList();
        if (hits.Count == 0) return new { answer = (string?)null, configured = !string.IsNullOrWhiteSpace(AppConfig.AiEndpoint), note = "No passage in the registered documents matches the question.", sources };
        if (string.IsNullOrWhiteSpace(AppConfig.AiEndpoint))
            return new { answer = (string?)null, configured = false, note = "AI answering is not configured (Ai:Endpoint). The most relevant passages are listed below.", sources };
        var context = string.Join("\n\n", sources.Select(s => $"[{s.n}] {s.Title}{(s.Page.HasValue ? ", page " + s.Page : "")}: {s.text}"));
        var body = new
        {
            model = AppConfig.AiModel, temperature = 0.1,
            messages = new object[]
            {
                new { role = "system", content = "You answer questions for dam safety engineers using ONLY the numbered passages provided. Cite passages like [1]. If the passages do not contain the answer, say so plainly. Never invent clause numbers, values or standards." },
                new { role = "user", content = $"Passages:\n{context}\n\nQuestion: {req.Question}" }
            }
        };
        using var msg = new HttpRequestMessage(HttpMethod.Post, AppConfig.AiEndpoint) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        if (!string.IsNullOrWhiteSpace(AppConfig.AiApiKey)) msg.Headers.Authorization = new("Bearer", AppConfig.AiApiKey);
        try
        {
            var res = await Http.SendAsync(msg); var txt = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) return new { answer = (string?)null, configured = true, note = $"AI service returned HTTP {(int)res.StatusCode}; passages listed below.", sources };
            var answer = JsonDocument.Parse(txt).RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            return new { answer, configured = true, note = "Answer generated from the passages below — verify against the documents before use.", sources };
        }
        catch (Exception ex) { return new { answer = (string?)null, configured = true, note = "AI service unreachable (" + ex.Message + "); passages listed below.", sources }; }
    }
}

// ===================== live section (digital twin) =====================
public class Cls_TwinService : Cls_DbBase
{
    public async Task<object> BuildAsync(string damId)
    {
        var dam = await new Cls_DamDAL().GetAsync(damId) ?? throw new BusinessException("Dam not found.");
        using var c = Conn();
        // geometry: latest design project linked to the dam (Main dam stability), else an outline from the dam register
        var rec = await c.QueryFirstOrDefaultAsync<(string? json, string? code, int? version, string? status)>(@"SELECT dc.record_json, p.code, dc.version, dc.status FROM design_calc dc JOIN design_project p ON p.id=dc.project_id
            WHERE p.dam_id=@damId AND p.status='Active' AND dc.calc_code='dam' ORDER BY (dc.status='Approved') DESC, p.updated_at DESC LIMIT 1", new { damId });
        object geometry; string source;
        if (rec.json != null)
        {
            var d = JsonDocument.Parse(rec.json).RootElement.GetProperty("data");
            geometry = new { type = d.GetProperty("type").GetString(), h = d.GetProperty("h").GetDouble(), b = d.GetProperty("b").GetDouble(), t = d.GetProperty("t").GetDouble(), crest = d.GetProperty("crest").GetDouble(), fl = d.GetProperty("fl").GetDouble(), us = d.GetProperty("us").GetDouble(), ds = d.GetProperty("ds").GetDouble(), kind = d.GetProperty("kind").GetString() };
            source = $"Design project {rec.code}, Main dam stability v{rec.version} ({rec.status})";
        }
        else
        {
            double H = (double)(dam.HeightM ?? 50), crest = (double)(dam.CrestLevelM ?? (dam.MwlM ?? 100) + 2), fl = crest - H; var types = await new Cls_ReferenceDAL().DamTypesAsync();
            var emb = types.FirstOrDefault(t => t.Code == dam.DamTypeCode)?.MaterialGroup == "Embankment";
            geometry = emb ? new { type = "Embankment (outline)", h = H, b = 8 + 4.75 * H, t = 8.0, crest, fl, us = 2.5, ds = 2.25, kind = "z" } : new { type = "Concrete (outline)", h = H, b = 0.8 * H, t = Math.Max(6, 0.12 * H), crest, fl, us = 0.0, ds = 0.0, kind = "" };
            source = "Outline from the dam register (height, top of dam) — link a design project for the real section";
        }
        var sensors = (await c.QueryAsync(@"SELECT s.id, s.code, s.parameter_code AS parameterCode, s.eng_unit AS unit, s.location, s.elevation_m AS elevationM, s.offset_m AS offsetM, s.chainage_m AS chainageM,
              (SELECT r.value FROM reading r WHERE r.sensor_id=s.id AND r.quality<>'B' ORDER BY r.observed_at DESC LIMIT 1) AS latest,
              (SELECT MAX(r.observed_at) FROM reading r WHERE r.sensor_id=s.id) AS latestAt,
              (SELECT COALESCE(MAX(a.level),0) FROM alert a WHERE a.sensor_id=s.id AND a.status<>'Closed' AND a.rule_type NOT IN ('System','Calibration')) AS level
            FROM sensor s WHERE s.dam_id=@damId AND s.status<>'Retired' ORDER BY s.code", new { damId })).ToList();
        double? lvl(string code) => sensors.Where(s => (string)s.parameterCode == code).Select(s => (double?)s.latest).FirstOrDefault(v => v.HasValue);
        return new { dam = new { dam.Id, dam.Name, dam.FrlM, dam.MwlM, dam.MddlM, dam.CrestLengthM }, geometry, source, reservoirLevel = lvl("RWL"), tailwaterLevel = lvl("TWL"), sensors,
            unplaced = sensors.Count(s => s.elevationM == null || s.offsetM == null) };
    }
}

// ===================== design → monitoring thresholds =====================
/// <summary>
/// Suggests thresholds from an approved design: uplift piezometers from the design uplift diagram at the sensor's position
/// (alert 80 % of normal-case design uplift, alarm = design uplift, emergency = drains-inoperative uplift), reservoir level from FRL / MWL.
/// </summary>
public class Cls_DesignLink : Cls_DbBase
{
    public async Task<object> SuggestAsync(string damId, string projectId)
    {
        var p = await new Cls_DesignDAL().GetAsync(projectId) ?? throw new BusinessException("Design project not found.");
        var calcs = (await new Cls_DesignDAL().CalcsAsync(projectId)).ToDictionary(x => x.CalcCode);
        if (!calcs.TryGetValue("dam", out var damCalc)) throw new BusinessException("The project has no Main dam stability calculation.");
        var i = Cls_DesignService.Load(p); var d = JsonDocument.Parse(damCalc.RecordJson).RootElement.GetProperty("data");
        double fl = d.GetProperty("fl").GetDouble(), B = d.GetProperty("b").GetDouble(), u = i.N("up"), frl = i.N("frl"), mwl = i.N("mwl"); var gravity = d.GetProperty("type").GetString() == "Concrete gravity";
        var canApply = damCalc.Status == "Approved" && !damCalc.Stale;
        var rows = new List<object>();
        foreach (var s in (await new Cls_SensorDAL().ListByDamAsync(damId)).Where(s => s.Status != "Retired"))
        {
            if (s.ParameterCode == "RWL")
                rows.Add(new { sensorId = s.Id, s.Code, s.ParameterCode, unit = s.EngUnit, alert = frl, alarm = Math.Round(mwl - 0.5, 2), emergency = mwl, ok = true,
                    basis = $"Design levels of {p.Code}: alert at FRL {frl} m, alarm 0.5 m below MWL, emergency at MWL {mwl} m" });
            else if (s.ParameterCode == "UPL")
            {
                if (!gravity) { rows.Add(new { sensorId = s.Id, s.Code, s.ParameterCode, unit = s.EngUnit, alert = (double?)null, alarm = (double?)null, emergency = (double?)null, ok = false, basis = "Uplift diagram applies to concrete / masonry gravity sections only" }); continue; }
                if (s.OffsetM is not double x) { rows.Add(new { sensorId = s.Id, s.Code, s.ParameterCode, unit = s.EngUnit, alert = (double?)null, alarm = (double?)null, emergency = (double?)null, ok = false, basis = "Set the sensor's offset from the heel (m) to compute the design uplift at its position" }); continue; }
                var frac = Math.Max(0, 1 - x / B);
                double Kpa(double head, double factor) => DesignMath.GW * factor * head * frac;
                var alarm = Kpa(frl - fl, u); var emerg = Kpa(frl - fl, 1.0);
                if (!string.Equals(s.EngUnit, "kPa", StringComparison.OrdinalIgnoreCase)) { rows.Add(new { sensorId = s.Id, s.Code, s.ParameterCode, unit = s.EngUnit, alert = (double?)null, alarm = (double?)null, emergency = (double?)null, ok = false, basis = "Suggestions are computed in kPa; the sensor uses " + s.EngUnit }); continue; }
                rows.Add(new { sensorId = s.Id, s.Code, s.ParameterCode, unit = s.EngUnit, alert = Math.Round(0.8 * alarm, 1), alarm = Math.Round(alarm, 1), emergency = Math.Round(emerg, 1), ok = true,
                    basis = $"Design uplift at {x:0.#} m from the heel (B = {B:0.#} m, foundation {fl:0.##} m): γw·u·(FRL − foundation)·(1 − x/B) = 9.81 × {u} × {frl - fl:0.##} × {frac:0.###} = {alarm:0.#} kPa (alarm); alert 80 %; emergency with drains inoperative (u = 1) {emerg:0.#} kPa. {p.Code} Main dam stability v{damCalc.Version} ({damCalc.Status})." });
            }
        }
        return new { project = new { p.Id, p.Code, p.Name }, damCalc = new { damCalc.Version, damCalc.Status, damCalc.Stale }, canApply,
            note = canApply ? "Suggestions come from an approved design. Applied thresholds still need approval by a Dam Safety Engineer or Approver other than you." : "The Main dam stability calculation must be Approved (and not STALE) before suggestions can be applied.", rows };
    }

    public async Task<int> ApplyAsync(string damId, ApplySuggestionsRequest req, string actor)
    {
        var s = await SuggestAsync(damId, req.ProjectId);
        var json = JsonSerializer.SerializeToElement(s, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        if (!json.GetProperty("canApply").GetBoolean()) throw new BusinessException(json.GetProperty("note").GetString()!);
        var n = 0;
        foreach (var r in json.GetProperty("rows").EnumerateArray().Where(r => r.GetProperty("ok").GetBoolean() && req.SensorIds.Contains(r.GetProperty("sensorId").GetString()!)))
        {
            var cur = await new Cls_SensorDAL().CurrentThresholdAsync(r.GetProperty("sensorId").GetString()!);
            await new Cls_SensorDAL().InsertThresholdAsync(new SensorThreshold
            {
                Id = Ids.New(), SensorId = r.GetProperty("sensorId").GetString()!, AlertValue = r.GetProperty("alert").GetDouble(), AlarmValue = r.GetProperty("alarm").GetDouble(), EmergencyValue = r.GetProperty("emergency").GetDouble(),
                RateLimitPerDay = cur?.RateLimitPerDay, ZLimit = cur?.ZLimit ?? 2.5m, Persistence = cur?.Persistence ?? 2, Method = "Design", Basis = r.GetProperty("basis").GetString(), CreatedBy = actor
            });
            await new Cls_AuditDAL().WriteAsync(null, actor, "Thresholds", $"Design-based thresholds applied to {r.GetProperty("code").GetString()}: {r.GetProperty("alert")}/{r.GetProperty("alarm")}/{r.GetProperty("emergency")}", "sensor", r.GetProperty("sensorId").GetString());
            n++;
        }
        return n;
    }
}
