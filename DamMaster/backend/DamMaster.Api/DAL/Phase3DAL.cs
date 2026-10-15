using Dapper;
using DamMaster.Api.Common;

namespace DamMaster.Api.Models
{
    public class HstModel
    {
        public string Id { get; set; } = ""; public string SensorId { get; set; } = ""; public string Variant { get; set; } = "";
        public DateTime DataFrom { get; set; } public DateTime DataTo { get; set; } public int NPoints { get; set; }
        public double HMin { get; set; } public double HMax { get; set; } public DateTime T0 { get; set; }
        public string Coefficients { get; set; } = "{}"; public double R2 { get; set; } public double Sigma { get; set; } public decimal KLimit { get; set; } = 2.5m; public double? DriftPerYear { get; set; }
        public string Status { get; set; } = "Draft"; public string? FittedBy { get; set; } public DateTime FittedAt { get; set; }
        public string? ApprovedBy { get; set; } public DateTime? ApprovedAt { get; set; } public string? Notes { get; set; }
    }
    public class DhiWeight { public string Code { get; set; } = ""; public string Name { get; set; } = ""; public decimal Weight { get; set; } public string? Description { get; set; } }
    public class DamDocument
    {
        public string Id { get; set; } = ""; public string DamId { get; set; } = ""; public string DocType { get; set; } = ""; public string Title { get; set; } = "";
        public string? ReferenceNo { get; set; } public DateTime? IssueDate { get; set; } public DateTime? NextReviewDate { get; set; } public string Status { get; set; } = "Draft";
        public string? CreatedBy { get; set; } public DateTime CreatedAt { get; set; }
    }
    public record FitRequest(double? KLimit);
    public record DhiSnapshotRow(long Id, DateTime ComputedAt, double Dhi, string Band, int Risk, string Rating);

    public class DhiInputs
    {
        public DateTime? DataAsOf { get; set; } public string? LastInspectionCondition { get; set; } public DateTime? LastInspectionDate { get; set; }
        public List<(string Severity, string Component, string? SensorGroup)> OpenFindings { get; set; } = new();
        public List<(string SensorId, int Level, string ParameterCode, string GroupCode)> OpenSensorAlerts { get; set; } = new();
        public int OpenCorroboration { get; set; }
        public List<(string Status, int IntervalMin, DateTime? CalibrationDue, DateTime? LastAt, string? LastQuality, int Valid30)> Sensors { get; set; } = new();
        public int DaysAboveFrl { get; set; } public bool MwlExceeded { get; set; } public bool FrlKnown { get; set; }
        public int MonsoonDue { get; set; } public int MonsoonOnTime { get; set; } public bool MonsoonOverdue { get; set; }
        public int FindingsClosedOnTime { get; set; } public int FindingsClosedLate { get; set; } public int FindingsOverdueOpen { get; set; }
        public HashSet<string> ApprovedDocs { get; set; } = new();
    }
}

namespace DamMaster.Api.DAL
{
    using DamMaster.Api.Models;

    public class Cls_HstDAL : Cls_DbBase
    {
        public async Task InsertAsync(HstModel m)
        {
            using var c = Conn();
            await c.ExecuteAsync(@"INSERT INTO hst_model (id, sensor_id, variant, data_from, data_to, n_points, h_min, h_max, t0, coefficients, r2, sigma, k_limit, drift_per_year, status, fitted_by, fitted_at, notes)
                VALUES (@Id, @SensorId, @Variant, @DataFrom, @DataTo, @NPoints, @HMin, @HMax, @T0, @Coefficients, @R2, @Sigma, @KLimit, @DriftPerYear, 'Draft', @FittedBy, UTC_TIMESTAMP(), @Notes)", m);
        }

        public async Task<List<HstModel>> ListAsync(string sensorId)
        {
            using var c = Conn();
            return (await c.QueryAsync<HstModel>("SELECT * FROM hst_model WHERE sensor_id=@sensorId ORDER BY fitted_at DESC", new { sensorId })).ToList();
        }

        public async Task<HstModel?> GetAsync(string id)
        {
            using var c = Conn();
            return await c.QueryFirstOrDefaultAsync<HstModel>("SELECT * FROM hst_model WHERE id=@id", new { id });
        }

        public async Task<HstModel?> ActiveAsync(string sensorId)
        {
            using var c = Conn();
            return await c.QueryFirstOrDefaultAsync<HstModel>("SELECT * FROM hst_model WHERE sensor_id=@sensorId AND status='Active' LIMIT 1", new { sensorId });
        }

        public async Task ApproveAsync(HstModel m, string by)
        {
            using var c = Conn();
            await c.OpenAsync();
            using var tx = await c.BeginTransactionAsync();
            await c.ExecuteAsync("UPDATE hst_model SET status='Superseded' WHERE sensor_id=@SensorId AND status='Active'", m, tx);
            await c.ExecuteAsync("UPDATE hst_model SET status='Active', approved_by=@by, approved_at=UTC_TIMESTAMP() WHERE id=@Id", new { m.Id, by }, tx);
            await tx.CommitAsync();
        }

        public async Task<int> RejectAsync(string id, string note)
        {
            using var c = Conn();
            return await c.ExecuteAsync("UPDATE hst_model SET status='Rejected', notes=CONCAT(COALESCE(notes,''), ' Rejected: ', @note) WHERE id=@id AND status='Draft'", new { id, note });
        }
    }

    public class Cls_DocumentDAL : Cls_DbBase
    {
        public async Task<List<DamDocument>> ListAsync(string damId)
        {
            using var c = Conn();
            return (await c.QueryAsync<DamDocument>("SELECT * FROM dam_document WHERE dam_id=@damId ORDER BY doc_type, issue_date DESC", new { damId })).ToList();
        }

        public async Task<DamDocument?> GetAsync(string id)
        {
            using var c = Conn();
            return await c.QueryFirstOrDefaultAsync<DamDocument>("SELECT * FROM dam_document WHERE id=@id", new { id });
        }

        public async Task UpsertAsync(DamDocument d, bool isNew)
        {
            using var c = Conn();
            await c.ExecuteAsync(isNew
                ? @"INSERT INTO dam_document (id, dam_id, doc_type, title, reference_no, issue_date, next_review_date, status, created_by, created_at)
                    VALUES (@Id, @DamId, @DocType, @Title, @ReferenceNo, @IssueDate, @NextReviewDate, @Status, @CreatedBy, UTC_TIMESTAMP())"
                : @"UPDATE dam_document SET doc_type=@DocType, title=@Title, reference_no=@ReferenceNo, issue_date=@IssueDate, next_review_date=@NextReviewDate,
                    status=@Status, updated_at=UTC_TIMESTAMP() WHERE id=@Id", d);
        }
    }

    public class Cls_DhiDAL : Cls_DbBase
    {
        public async Task<List<DhiWeight>> WeightsAsync()
        {
            using var c = Conn();
            return (await c.QueryAsync<DhiWeight>("SELECT code, name, weight, description FROM ref_dhi_component ORDER BY sort_order")).ToList();
        }

        public async Task InsertSnapshotAsync(string damId, double dhi, string band, int risk, string rating, string componentsJson, string by)
        {
            using var c = Conn();
            await c.ExecuteAsync("INSERT INTO dhi_snapshot (dam_id, computed_at, dhi, band, risk, rating, components, computed_by) VALUES (@damId, UTC_TIMESTAMP(), @dhi, @band, @risk, @rating, @componentsJson, @by)",
                new { damId, dhi, band, risk, rating, componentsJson, by });
        }

        public async Task<List<DhiSnapshotRow>> HistoryAsync(string damId, int take)
        {
            using var c = Conn();
            return (await c.QueryAsync<DhiSnapshotRow>("SELECT id, computed_at AS ComputedAt, dhi, band, risk, rating FROM dhi_snapshot WHERE dam_id=@damId ORDER BY computed_at DESC LIMIT @take", new { damId, take })).Reverse().ToList();
        }

        public async Task<DhiInputs> InputsAsync(string damId)
        {
            using var c = Conn();
            var x = new DhiInputs();
            var dam = await c.QueryFirstAsync<(decimal? frl, decimal? mwl)>("SELECT frl_m, mwl_m FROM dam WHERE id=@damId", new { damId });
            x.DataAsOf = await c.ExecuteScalarAsync<DateTime?>("SELECT MAX(r.observed_at) FROM reading r JOIN sensor s ON s.id=r.sensor_id WHERE s.dam_id=@damId", new { damId });
            var asOf = x.DataAsOf ?? DateTime.UtcNow;
            var li = await c.QueryFirstOrDefaultAsync<(string? cond, DateTime? at)>("SELECT overall_condition, completed_at FROM inspection WHERE dam_id=@damId AND status='Completed' AND completed_at >= DATE_SUB(UTC_TIMESTAMP(), INTERVAL 365 DAY) ORDER BY completed_at DESC LIMIT 1", new { damId });
            x.LastInspectionCondition = li.cond; x.LastInspectionDate = li.at;
            x.OpenFindings = (await c.QueryAsync<(string, string, string?)>(@"SELECT f.severity, f.component, p.group_code FROM inspection_finding f LEFT JOIN sensor s ON s.id=f.sensor_id
                LEFT JOIN ref_parameter p ON p.code=s.parameter_code WHERE f.dam_id=@damId AND f.status<>'Closed'", new { damId })).ToList();
            x.OpenSensorAlerts = (await c.QueryAsync<(string, int, string, string)>(@"SELECT a.sensor_id, a.level, s.parameter_code, p.group_code FROM alert a JOIN sensor s ON s.id=a.sensor_id
                JOIN ref_parameter p ON p.code=s.parameter_code WHERE a.dam_id=@damId AND a.status<>'Closed' AND a.rule_type IN ('Threshold','Statistical','Rate','HST')", new { damId })).ToList();
            x.OpenCorroboration = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM alert WHERE dam_id=@damId AND status<>'Closed' AND rule_type='Corroboration'", new { damId });
            x.Sensors = (await c.QueryAsync<(string, int, DateTime?, DateTime?, string?, int)>(@"
                SELECT s.status, s.reading_interval_min, s.calibration_due,
                  (SELECT MAX(r.observed_at) FROM reading r WHERE r.sensor_id=s.id),
                  (SELECT r.quality FROM reading r WHERE r.sensor_id=s.id ORDER BY r.observed_at DESC LIMIT 1),
                  (SELECT COUNT(*) FROM reading r WHERE r.sensor_id=s.id AND r.quality<>'B' AND r.observed_at > DATE_SUB(@asOf, INTERVAL 30 DAY) AND r.observed_at <= @asOf)
                FROM sensor s WHERE s.dam_id=@damId", new { damId, asOf })).ToList();
            x.FrlKnown = dam.frl.HasValue;
            if (dam.frl.HasValue)
            {
                var daily = (await c.QueryAsync<double>(@"SELECT MAX(r.value) FROM reading r JOIN sensor s ON s.id=r.sensor_id WHERE s.dam_id=@damId AND s.parameter_code='RWL'
                    AND r.quality<>'B' AND r.observed_at > DATE_SUB(@asOf, INTERVAL 30 DAY) AND r.observed_at <= @asOf GROUP BY DATE(r.observed_at)", new { damId, asOf })).ToList();
                x.DaysAboveFrl = daily.Count(v => v > (double)dam.frl.Value);
                x.MwlExceeded = dam.mwl.HasValue && daily.Any(v => v > (double)dam.mwl!.Value);
            }
            var today = DateTime.UtcNow.Date; var year = today.Year;
            foreach (var (type, mmdd) in new[] { ("Pre-monsoon", AppConfig.PreMonsoonDue), ("Post-monsoon", AppConfig.PostMonsoonDue) })
            {
                var due = DateTime.ParseExact($"{year}-{mmdd}", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                var done = await c.ExecuteScalarAsync<DateTime?>("SELECT MIN(completed_at) FROM inspection WHERE dam_id=@damId AND inspection_type=@type AND status='Completed' AND YEAR(completed_at)=@year", new { damId, type, year });
                if (today > due || done.HasValue)
                {
                    x.MonsoonDue++;
                    if (done.HasValue && done.Value.Date <= due) x.MonsoonOnTime++;
                    if (!done.HasValue) x.MonsoonOverdue = true;
                }
            }
            x.FindingsClosedOnTime = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM inspection_finding WHERE dam_id=@damId AND status='Closed' AND closed_at >= DATE_SUB(UTC_TIMESTAMP(), INTERVAL 365 DAY) AND (due_date IS NULL OR DATE(closed_at) <= due_date)", new { damId });
            x.FindingsClosedLate = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM inspection_finding WHERE dam_id=@damId AND status='Closed' AND closed_at >= DATE_SUB(UTC_TIMESTAMP(), INTERVAL 365 DAY) AND due_date IS NOT NULL AND DATE(closed_at) > due_date", new { damId });
            x.FindingsOverdueOpen = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM inspection_finding WHERE dam_id=@damId AND status<>'Closed' AND due_date < UTC_DATE()", new { damId });
            x.ApprovedDocs = (await c.QueryAsync<string>(@"SELECT DISTINCT doc_type FROM dam_document WHERE dam_id=@damId AND status='Approved'
                AND (next_review_date IS NULL OR next_review_date >= UTC_DATE()) AND (doc_type<>'Safety review' OR issue_date >= DATE_SUB(UTC_DATE(), INTERVAL 10 YEAR))", new { damId })).ToHashSet();
            return x;
        }
    }
}
