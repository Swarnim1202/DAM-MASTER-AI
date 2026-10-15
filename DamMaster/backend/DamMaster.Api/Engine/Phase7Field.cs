using System.Text.Json;
using Dapper;
using DamMaster.Api.Common;
using DamMaster.Api.Controllers;
using DamMaster.Api.DAL;
using DamMaster.Api.Models;

namespace DamMaster.Api.Engine;

/// <summary>Inspection rules in one place — used by the web pages and by offline field sync (identical validation).</summary>
public class Cls_InspectionService
{
    private readonly Cls_InspectionDAL _dal = new();
    private static Task Audit(string actor, string action, string id) => new Cls_AuditDAL().WriteAsync(null, actor, "Inspections", action, "inspection", id);

    public async Task StartAsync(string id, string actor)
    {
        if (await _dal.StartAsync(id, actor) == 0) throw new BusinessException("Only a planned inspection can be started.");
        await Audit(actor, "Inspection started", id);
    }

    public async Task SaveChecksAsync(string id, List<CheckInput> checks, string actor)
    {
        var i = await _dal.GetAsync(id) ?? throw new BusinessException("Inspection not found.");
        if (i.Status != "In progress") throw new BusinessException("Start the inspection before recording checks.");
        if (checks.Any(k => !Cls_InspectionEngine.CheckResults.Contains(k.Result))) throw new BusinessException("Each result must be OK, Attention or Not applicable.");
        if (checks.Any(k => k.Result == "Attention" && string.IsNullOrWhiteSpace(k.Remark))) throw new BusinessException("Add a remark for every item marked Attention.");
        await _dal.SaveChecksAsync(id, checks, actor);
        await Audit(actor, $"Recorded {checks.Count} checklist item(s)", id);
    }

    public async Task<Finding> AddFindingAsync(string id, CreateFindingRequest req, string actor)
    {
        var i = await _dal.GetAsync(id) ?? throw new BusinessException("Inspection not found.");
        if (i.Status != "In progress") throw new BusinessException("Findings are recorded while the inspection is in progress.");
        if (!Cls_InspectionEngine.Severities.Contains(req.Severity)) throw new BusinessException("Severity must be Low, Medium, High or Critical.");
        if (string.IsNullOrWhiteSpace(req.Component) || string.IsNullOrWhiteSpace(req.Description)) throw new BusinessException("Component and description are required.");
        if (!string.IsNullOrWhiteSpace(req.SensorId) && (await new Cls_SensorDAL().GetAsync(req.SensorId))?.DamId != i.DamId) throw new BusinessException("Linked sensor must belong to the same dam.");
        var f = new Finding
        {
            Id = Ids.New(), InspectionId = id, DamId = i.DamId, Component = req.Component.Trim(), Location = req.Location?.Trim(), Description = req.Description.Trim(), Severity = req.Severity,
            SensorId = string.IsNullOrWhiteSpace(req.SensorId) ? null : req.SensorId, Recommendation = req.Recommendation?.Trim(),
            DueDate = (req.DueDate ?? DateTime.UtcNow.AddDays(Cls_InspectionEngine.DueDays(req.Severity))).Date, CreatedBy = actor
        };
        f.AlertId = await new Cls_InspectionEngine().AlertForFindingAsync(f, i.DamName ?? "");
        await _dal.InsertFindingAsync(f);
        await Audit(actor, $"{f.Severity} finding: {f.Component} — {f.Description}" + (f.AlertId != null ? " (alert raised)" : ""), id);
        return f;
    }

    public async Task CompleteAsync(string id, CompleteInspectionRequest req, string actor)
    {
        var i = await _dal.GetAsync(id) ?? throw new BusinessException("Inspection not found.");
        if (!Cls_InspectionEngine.Conditions.Contains(req.OverallCondition)) throw new BusinessException("Overall condition must be Good, Fair, Poor or Unsatisfactory.");
        var dam = await new Cls_DamDAL().GetAsync(i.DamId);
        var group = (await new Cls_ReferenceDAL().DamTypesAsync()).FirstOrDefault(t => t.Code == dam?.DamTypeCode)?.MaterialGroup ?? "Concrete";
        var missing = (await _dal.ChecklistAsync(id, group)).Count(c => c.Result == null);
        if (missing > 0) throw new BusinessException($"{missing} checklist item(s) not yet recorded.");
        if (await _dal.CompleteAsync(id, req.OverallCondition, req.Summary?.Trim()) == 0) throw new BusinessException("Only an inspection in progress can be completed.");
        await Audit(actor, $"Inspection completed — overall condition {req.OverallCondition}", id);
    }
}

public class FieldOp { public string ClientOpId { get; set; } = ""; public string Kind { get; set; } = ""; public string InspectionId { get; set; } = ""; public JsonElement Payload { get; set; } }
public class FieldSyncRequest { public List<FieldOp> Ops { get; set; } = new(); }
public record FieldOpResult(string ClientOpId, bool Ok, string? Error, string? EntityId, bool Replayed);
public class FindingPhoto { public string Id { get; set; } = ""; public string FindingId { get; set; } = ""; public string InspectionId { get; set; } = ""; public string? ClientRef { get; set; } public string FileName { get; set; } = ""; public string ContentType { get; set; } = ""; public long SizeBytes { get; set; } public string StoragePath { get; set; } = ""; public int? Width { get; set; } public int? Height { get; set; } public decimal? Latitude { get; set; } public decimal? Longitude { get; set; } public DateTime? TakenAt { get; set; } public string? Caption { get; set; } public string? CreatedBy { get; set; } public DateTime CreatedAt { get; set; } }

/// <summary>
/// Offline field sync: the device queues operations (start, checks, finding, complete) with its own ids and replays them
/// in order when online. Each client id is applied once — replays return the stored result — so a lost response never
/// creates duplicates. Validation is exactly the same as on the web pages.
/// </summary>
public class Cls_FieldService : Cls_DbBase
{
    private static readonly JsonSerializerOptions Js = new() { PropertyNameCaseInsensitive = true };

    public async Task<object> PackageAsync(string inspectionId)
    {
        var dal = new Cls_InspectionDAL();
        var i = await dal.GetAsync(inspectionId) ?? throw new BusinessException("Inspection not found.");
        var dam = await new Cls_DamDAL().GetAsync(i.DamId);
        var group = (await new Cls_ReferenceDAL().DamTypesAsync()).FirstOrDefault(t => t.Code == dam?.DamTypeCode)?.MaterialGroup ?? "Concrete";
        return new { inspection = i, dam = new { dam!.Id, dam.Code, dam.Name }, checklist = await dal.ChecklistAsync(inspectionId, group), findings = await dal.FindingsAsync(inspectionId, null, false),
            sensors = (await new Cls_SensorDAL().ListByDamAsync(i.DamId)).Where(s => s.Status != "Retired").Select(s => new { s.Id, s.Code, s.ParameterCode, s.Location }), packagedAt = DateTime.UtcNow };
    }

    public async Task<List<FieldOpResult>> SyncAsync(FieldSyncRequest req, string actor)
    {
        if (req.Ops.Count > 500) throw new BusinessException("At most 500 operations per sync.");
        var svc = new Cls_InspectionService(); var results = new List<FieldOpResult>();
        foreach (var op in req.Ops)
        {
            if (string.IsNullOrWhiteSpace(op.ClientOpId) || op.ClientOpId.Length > 64) { results.Add(new(op.ClientOpId ?? "", false, "clientOpId is required (max 64 characters).", null, false)); continue; }
            // claim the id first (atomic): a concurrent or repeated request for the same id waits for, and returns, the first result
            using (var c = Conn())
            {
                var claimed = await c.ExecuteAsync("INSERT IGNORE INTO field_op (client_op_id, user_name, kind, inspection_id, ok, error, received_at) VALUES (@id, @actor, @kind, @insp, 0, '(processing)', UTC_TIMESTAMP())",
                    new { id = op.ClientOpId, actor, kind = op.Kind, insp = op.InspectionId }) == 1;
                if (!claimed)
                {
                    (bool ok, string? error, string? entity) prev = default;
                    for (var k = 0; k < 50; k++)
                    {
                        prev = await c.QueryFirstAsync<(bool, string?, string?)>("SELECT ok, error, entity_id FROM field_op WHERE client_op_id=@id", new { id = op.ClientOpId });
                        if (prev.error != "(processing)") break;
                        await Task.Delay(200);
                    }
                    results.Add(new(op.ClientOpId, prev.ok, prev.error == "(processing)" ? "Still being processed — sync again shortly." : prev.error, prev.entity, true)); continue;
                }
            }
            string? entity = null, error = null;
            try
            {
                switch (op.Kind)
                {
                    case "start": await svc.StartAsync(op.InspectionId, actor); break;
                    case "checks": await svc.SaveChecksAsync(op.InspectionId, op.Payload.Deserialize<List<CheckInput>>(Js) ?? new(), actor); break;
                    case "finding": entity = (await svc.AddFindingAsync(op.InspectionId, op.Payload.Deserialize<CreateFindingRequest>(Js) ?? throw new BusinessException("Finding data missing."), actor)).Id; break;
                    case "complete": await svc.CompleteAsync(op.InspectionId, op.Payload.Deserialize<CompleteInspectionRequest>(Js) ?? throw new BusinessException("Completion data missing."), actor); break;
                    default: throw new BusinessException($"Unknown operation '{op.Kind}'.");
                }
            }
            catch (BusinessException ex) { error = ex.Message; }
            catch (Exception ex) { error = "Server error: " + ex.Message; }
            using (var c = Conn())
                await c.ExecuteAsync("UPDATE field_op SET entity_id=@entity, ok=@ok, error=@error WHERE client_op_id=@id", new { id = op.ClientOpId, entity, ok = error == null, error });
            results.Add(new(op.ClientOpId, error == null, error, entity, false));
        }
        if (results.Count > 0) await new Cls_AuditDAL().WriteAsync(null, actor, "Field", $"Field sync: {results.Count(r => r.Ok && !r.Replayed)} applied, {results.Count(r => r.Replayed)} already applied, {results.Count(r => !r.Ok)} rejected");
        return results;
    }

    /// <summary>Resolves a finding referenced by its offline client op id (photos taken before the finding reached the server).</summary>
    public async Task<string?> FindingIdForClientOpAsync(string clientOpId)
    { using var c = Conn(); return await c.ExecuteScalarAsync<string?>("SELECT entity_id FROM field_op WHERE client_op_id=@clientOpId AND kind='finding' AND ok=1", new { clientOpId }); }

    public static (string type, int w, int h)? ImageInfo(byte[] b)
    {
        if (b.Length > 24 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
            return ("image/png", (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19], (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23]);
        if (b.Length > 4 && b[0] == 0xFF && b[1] == 0xD8)
        {
            var i = 2;
            while (i + 9 < b.Length)
            {
                if (b[i] != 0xFF) { i++; continue; }
                var m = b[i + 1]; var len = (b[i + 2] << 8) | b[i + 3];
                if (m is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC) return ("image/jpeg", (b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]);
                i += 2 + len;
            }
            return ("image/jpeg", 0, 0);
        }
        return null;
    }

    public async Task<FindingPhoto> AddPhotoAsync(string findingId, IFormFile file, double? lat, double? lon, DateTime? takenAt, string? caption, string? clientRef, string actor)
    {
        if (!string.IsNullOrWhiteSpace(clientRef))
            using (var c0 = Conn()) { var ex = await c0.QueryFirstOrDefaultAsync<FindingPhoto>("SELECT * FROM finding_photo WHERE client_ref=@clientRef", new { clientRef }); if (ex != null) return ex; }
        var f = await new Cls_InspectionDAL().FindingAsync(findingId) ?? throw new BusinessException("Finding not found.");
        if (file.Length == 0 || file.Length > 10_000_000) throw new BusinessException("Photo must be between 1 byte and 10 MB.");
        using var ms = new MemoryStream(); await file.CopyToAsync(ms); var bytes = ms.ToArray();
        var info = ImageInfo(bytes) ?? throw new BusinessException("Only JPEG or PNG photos are accepted.");
        if (lat is < -90 or > 90 || lon is < -180 or > 180) throw new BusinessException("Photo location out of range.");
        var p = new FindingPhoto { Id = Ids.New(), FindingId = findingId, InspectionId = f.InspectionId, ClientRef = string.IsNullOrWhiteSpace(clientRef) ? null : clientRef, FileName = Path.GetFileName(file.FileName), ContentType = info.type,
            SizeBytes = bytes.Length, Width = info.w > 0 ? info.w : null, Height = info.h > 0 ? info.h : null, Latitude = (decimal?)lat, Longitude = (decimal?)lon, TakenAt = takenAt?.ToUniversalTime(), Caption = caption?.Trim(), CreatedBy = actor };
        var dir = Path.Combine(AppConfig.StorageRoot, "photos"); Directory.CreateDirectory(dir);
        p.StoragePath = Path.Combine(dir, p.Id + (info.type == "image/png" ? ".png" : ".jpg")); await File.WriteAllBytesAsync(p.StoragePath, bytes);
        using var c = Conn();
        await c.ExecuteAsync(@"INSERT INTO finding_photo (id, finding_id, inspection_id, client_ref, file_name, content_type, size_bytes, storage_path, width, height, latitude, longitude, taken_at, caption, created_by, created_at)
            VALUES (@Id, @FindingId, @InspectionId, @ClientRef, @FileName, @ContentType, @SizeBytes, @StoragePath, @Width, @Height, @Latitude, @Longitude, @TakenAt, @Caption, @CreatedBy, UTC_TIMESTAMP())", p);
        await new Cls_AuditDAL().WriteAsync(null, actor, "Inspections", $"Photo added to finding ({p.Width}×{p.Height}, {p.SizeBytes / 1024} KB)", "finding", findingId);
        return p;
    }

    public async Task<List<FindingPhoto>> PhotosAsync(string findingId) { using var c = Conn(); return (await c.QueryAsync<FindingPhoto>("SELECT * FROM finding_photo WHERE finding_id=@findingId ORDER BY created_at", new { findingId })).ToList(); }
    public async Task<FindingPhoto?> PhotoAsync(string id) { using var c = Conn(); return await c.QueryFirstOrDefaultAsync<FindingPhoto>("SELECT * FROM finding_photo WHERE id=@id", new { id }); }
}
