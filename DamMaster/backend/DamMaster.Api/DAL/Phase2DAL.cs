using Dapper;
using DamMaster.Api.Common;

namespace DamMaster.Api.Models
{
    public class Inspection
    {
        public string Id { get; set; } = ""; public string DamId { get; set; } = ""; public string? DamName { get; set; } public string InspectionType { get; set; } = "Routine";
        public DateTime ScheduledDate { get; set; } public string Status { get; set; } = "Planned"; public string? Inspector { get; set; }
        public DateTime? StartedAt { get; set; } public DateTime? CompletedAt { get; set; } public string? OverallCondition { get; set; } public string? Summary { get; set; }
        public string? TriggerAlertId { get; set; } public string? CreatedBy { get; set; } public DateTime CreatedAt { get; set; }
        public int OpenFindings { get; set; } public int ChecksDone { get; set; }
    }
    public class InspectionItem { public string Code { get; set; } = ""; public string MaterialGroup { get; set; } = ""; public string Component { get; set; } = ""; public string Item { get; set; } = ""; public string? Result { get; set; } public string? Remark { get; set; } public string? CheckedBy { get; set; } }
    public class CheckInput { public string ItemCode { get; set; } = ""; public string Result { get; set; } = ""; public string? Remark { get; set; } }
    public class Finding
    {
        public string Id { get; set; } = ""; public string InspectionId { get; set; } = ""; public string DamId { get; set; } = ""; public string Component { get; set; } = "";
        public string? Location { get; set; } public string Description { get; set; } = ""; public string Severity { get; set; } = "Medium"; public string? SensorId { get; set; } public string? SensorCode { get; set; }
        public string? Recommendation { get; set; } public string Status { get; set; } = "Open"; public DateTime? DueDate { get; set; } public string? AlertId { get; set; }
        public string? CreatedBy { get; set; } public DateTime CreatedAt { get; set; } public string? ClosedBy { get; set; } public DateTime? ClosedAt { get; set; } public string? ClosureNote { get; set; }
        public string? InspectionType { get; set; } public string? DamName { get; set; }
    }
    public record CompleteInspectionRequest(string OverallCondition, string? Summary);
    public record FindingStatusRequest(string Status, string? Note);
    public class EapContact
    {
        public string Id { get; set; } = ""; public string DamId { get; set; } = ""; public string Name { get; set; } = ""; public string? Organisation { get; set; } public string? EapRole { get; set; }
        public string? Phone { get; set; } public string? Email { get; set; } public int MinLevel { get; set; } = 2; public int EscalationOrder { get; set; } = 1; public bool IsActive { get; set; } = true;
    }
    public class NotificationRow
    {
        public long Id { get; set; } public string? AlertId { get; set; } public string? DamId { get; set; } public int Level { get; set; } public string Channel { get; set; } = "";
        public string Recipient { get; set; } = ""; public string? RecipientName { get; set; } public string Subject { get; set; } = ""; public string Body { get; set; } = "";
        public int EscalationStep { get; set; } public string Status { get; set; } = ""; public int Attempts { get; set; } public string? Error { get; set; }
        public DateTime CreatedAt { get; set; } public DateTime? SentAt { get; set; } public DateTime? ReadAt { get; set; }
    }
    public class ScheduleRow
    {
        public string SensorId { get; set; } = ""; public string Code { get; set; } = ""; public string ParameterCode { get; set; } = ""; public string GroupCode { get; set; } = "";
        public double? RequiredHours { get; set; } public string? Note { get; set; } public int ConfiguredMinutes { get; set; } public DateTime? LastReading { get; set; } public DateTime? NextDue { get; set; }
        public string Status { get; set; } = ""; public int ReadingsLast30d { get; set; } public double? CompliancePct { get; set; } public bool LoggerSlowerThanRequired { get; set; }
    }
    public class MonsoonCompliance
    {
        public string DamId { get; set; } = ""; public string DamName { get; set; } = ""; public int Year { get; set; }
        public string PreStatus { get; set; } = ""; public DateTime PreDue { get; set; } public DateTime? PreDone { get; set; }
        public string PostStatus { get; set; } = ""; public DateTime PostDue { get; set; } public DateTime? PostDone { get; set; }
        public int OpenFindings { get; set; } public int OverdueFindings { get; set; }
    }
}

namespace DamMaster.Api.DAL
{
    using DamMaster.Api.Models;

    public class Cls_InspectionDAL : Cls_DbBase
    {
        private const string Sel = @"SELECT i.*, d.name AS DamName,
            (SELECT COUNT(*) FROM inspection_finding f WHERE f.inspection_id=i.id AND f.status<>'Closed') AS OpenFindings,
            (SELECT COUNT(*) FROM inspection_check k WHERE k.inspection_id=i.id) AS ChecksDone
            FROM inspection i LEFT JOIN dam d ON d.id=i.dam_id ";

        public async Task<List<Inspection>> ListAsync(string? damId, string? status, int take)
        {
            using var c = Conn();
            var w = new List<string>();
            if (damId != null) w.Add("i.dam_id=@damId");
            if (status == "Active") w.Add("i.status IN ('Planned','In progress')"); else if (status != null) w.Add("i.status=@status");
            return (await c.QueryAsync<Inspection>(Sel + (w.Count > 0 ? "WHERE " + string.Join(" AND ", w) : "") + " ORDER BY i.scheduled_date DESC, i.created_at DESC LIMIT @take", new { damId, status, take })).ToList();
        }

        public async Task<Inspection?> GetAsync(string id)
        {
            using var c = Conn();
            return await c.QueryFirstOrDefaultAsync<Inspection>(Sel + "WHERE i.id=@id", new { id });
        }

        public async Task<Inspection?> OpenSpecialAsync(string damId)
        {
            using var c = Conn();
            return await c.QueryFirstOrDefaultAsync<Inspection>(Sel + "WHERE i.dam_id=@damId AND i.inspection_type='Special' AND i.status IN ('Planned','In progress') LIMIT 1", new { damId });
        }

        public async Task InsertAsync(Inspection i)
        {
            using var c = Conn();
            await c.ExecuteAsync(@"INSERT INTO inspection (id, dam_id, inspection_type, scheduled_date, status, inspector, trigger_alert_id, created_by, created_at)
                VALUES (@Id, @DamId, @InspectionType, @ScheduledDate, 'Planned', @Inspector, @TriggerAlertId, @CreatedBy, UTC_TIMESTAMP())", i);
        }

        public async Task<int> StartAsync(string id, string inspector)
        {
            using var c = Conn();
            return await c.ExecuteAsync("UPDATE inspection SET status='In progress', started_at=UTC_TIMESTAMP(), inspector=COALESCE(inspector,@inspector) WHERE id=@id AND status='Planned'", new { id, inspector });
        }

        public async Task<int> CompleteAsync(string id, string condition, string? summary)
        {
            using var c = Conn();
            return await c.ExecuteAsync("UPDATE inspection SET status='Completed', completed_at=UTC_TIMESTAMP(), overall_condition=@condition, summary=@summary WHERE id=@id AND status='In progress'", new { id, condition, summary });
        }

        public async Task<int> CancelAsync(string id)
        {
            using var c = Conn();
            return await c.ExecuteAsync("UPDATE inspection SET status='Cancelled' WHERE id=@id AND status IN ('Planned','In progress')", new { id });
        }

        public async Task<List<InspectionItem>> ChecklistAsync(string inspectionId, string materialGroup)
        {
            using var c = Conn();
            var groups = materialGroup == "Masonry" ? new[] { "All", "Masonry", "Concrete" } : new[] { "All", materialGroup };
            return (await c.QueryAsync<InspectionItem>(@"SELECT r.code, r.material_group, r.component, r.item, k.result, k.remark, k.checked_by
                FROM ref_inspection_item r LEFT JOIN inspection_check k ON k.item_code=r.code AND k.inspection_id=@inspectionId
                WHERE r.material_group IN @groups ORDER BY r.sort_order", new { inspectionId, groups })).ToList();
        }

        public async Task SaveChecksAsync(string inspectionId, IEnumerable<CheckInput> checks, string by)
        {
            using var c = Conn();
            await c.ExecuteAsync(@"INSERT INTO inspection_check (inspection_id, item_code, result, remark, checked_by, checked_at) VALUES (@inspectionId, @ItemCode, @Result, @Remark, @by, UTC_TIMESTAMP())
                ON DUPLICATE KEY UPDATE result=VALUES(result), remark=VALUES(remark), checked_by=VALUES(checked_by), checked_at=VALUES(checked_at)",
                checks.Select(k => new { inspectionId, k.ItemCode, k.Result, k.Remark, by }));
        }

        // ---- findings ----
        private const string SelF = @"SELECT f.*, s.code AS SensorCode, i.inspection_type AS InspectionType, d.name AS DamName FROM inspection_finding f
            LEFT JOIN sensor s ON s.id=f.sensor_id LEFT JOIN inspection i ON i.id=f.inspection_id LEFT JOIN dam d ON d.id=f.dam_id ";

        public async Task<List<Finding>> FindingsAsync(string? inspectionId, string? damId, bool openOnly)
        {
            using var c = Conn();
            var w = new List<string>();
            if (inspectionId != null) w.Add("f.inspection_id=@inspectionId");
            if (damId != null) w.Add("f.dam_id=@damId");
            if (openOnly) w.Add("f.status<>'Closed'");
            return (await c.QueryAsync<Finding>(SelF + (w.Count > 0 ? "WHERE " + string.Join(" AND ", w) : "") +
                " ORDER BY (f.status='Closed'), FIELD(f.severity,'Critical','High','Medium','Low'), f.due_date LIMIT 1000", new { inspectionId, damId })).ToList();
        }

        public async Task<Finding?> FindingAsync(string id)
        {
            using var c = Conn();
            return await c.QueryFirstOrDefaultAsync<Finding>(SelF + "WHERE f.id=@id", new { id });
        }

        public async Task InsertFindingAsync(Finding f)
        {
            using var c = Conn();
            await c.ExecuteAsync(@"INSERT INTO inspection_finding (id, inspection_id, dam_id, component, location, description, severity, sensor_id, recommendation, status, due_date, alert_id, created_by, created_at)
                VALUES (@Id, @InspectionId, @DamId, @Component, @Location, @Description, @Severity, @SensorId, @Recommendation, 'Open', @DueDate, @AlertId, @CreatedBy, UTC_TIMESTAMP())", f);
        }

        public async Task<int> SetFindingStatusAsync(string id, string status, string by, string? note)
        {
            using var c = Conn();
            return status == "Closed"
                ? await c.ExecuteAsync("UPDATE inspection_finding SET status='Closed', closed_by=@by, closed_at=UTC_TIMESTAMP(), closure_note=@note WHERE id=@id AND status<>'Closed'", new { id, by, note })
                : await c.ExecuteAsync("UPDATE inspection_finding SET status=@status WHERE id=@id AND status<>'Closed'", new { id, status });
        }

        public async Task<(DateTime? pre, DateTime? post, int open, int overdue)> MonsoonAsync(string damId, int year)
        {
            using var c = Conn();
            var pre = await c.ExecuteScalarAsync<DateTime?>("SELECT MAX(completed_at) FROM inspection WHERE dam_id=@damId AND inspection_type='Pre-monsoon' AND status='Completed' AND YEAR(completed_at)=@year", new { damId, year });
            var post = await c.ExecuteScalarAsync<DateTime?>("SELECT MAX(completed_at) FROM inspection WHERE dam_id=@damId AND inspection_type='Post-monsoon' AND status='Completed' AND YEAR(completed_at)=@year", new { damId, year });
            var open = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM inspection_finding WHERE dam_id=@damId AND status<>'Closed'", new { damId });
            var overdue = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM inspection_finding WHERE dam_id=@damId AND status<>'Closed' AND due_date < UTC_DATE()", new { damId });
            return (pre, post, open, overdue);
        }

        public async Task<(int open, int high, int overdueInspections)> TotalsAsync()
        {
            using var c = Conn();
            var open = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM inspection_finding WHERE status<>'Closed'");
            var high = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM inspection_finding WHERE status<>'Closed' AND severity IN ('High','Critical')");
            var od = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM inspection WHERE status IN ('Planned','In progress') AND scheduled_date < UTC_DATE()");
            return (open, high, od);
        }
    }

    public class Cls_EapDAL : Cls_DbBase
    {
        public async Task<List<EapContact>> ListAsync(string damId, bool activeOnly = false)
        {
            using var c = Conn();
            return (await c.QueryAsync<EapContact>("SELECT * FROM eap_contact WHERE dam_id=@damId" + (activeOnly ? " AND is_active=1" : "") + " ORDER BY escalation_order, name", new { damId })).ToList();
        }

        public async Task<EapContact?> GetAsync(string id)
        {
            using var c = Conn();
            return await c.QueryFirstOrDefaultAsync<EapContact>("SELECT * FROM eap_contact WHERE id=@id", new { id });
        }

        public async Task UpsertAsync(EapContact e, bool isNew)
        {
            using var c = Conn();
            await c.ExecuteAsync(isNew
                ? @"INSERT INTO eap_contact (id, dam_id, name, organisation, eap_role, phone, email, min_level, escalation_order, is_active, created_at)
                    VALUES (@Id, @DamId, @Name, @Organisation, @EapRole, @Phone, @Email, @MinLevel, @EscalationOrder, @IsActive, UTC_TIMESTAMP())"
                : @"UPDATE eap_contact SET name=@Name, organisation=@Organisation, eap_role=@EapRole, phone=@Phone, email=@Email, min_level=@MinLevel,
                    escalation_order=@EscalationOrder, is_active=@IsActive WHERE id=@Id", e);
        }
    }

    public class Cls_NotificationDAL : Cls_DbBase
    {
        public async Task InsertManyAsync(IEnumerable<NotificationRow> rows)
        {
            var list = rows.ToList();
            if (list.Count == 0) return;
            using var c = Conn();
            await c.ExecuteAsync(@"INSERT INTO notification (alert_id, dam_id, level, channel, recipient, recipient_name, subject, body, escalation_step, status, created_at, sent_at)
                VALUES (@AlertId, @DamId, @Level, @Channel, @Recipient, @RecipientName, @Subject, @Body, @EscalationStep, @Status, UTC_TIMESTAMP(), CASE WHEN @Status='Delivered' THEN UTC_TIMESTAMP() END)", list);
        }

        public async Task<List<NotificationRow>> QueuedAsync(int take)
        {
            using var c = Conn();
            return (await c.QueryAsync<NotificationRow>("SELECT * FROM notification WHERE status='Queued' AND attempts < 3 ORDER BY id LIMIT @take", new { take })).ToList();
        }

        public async Task MarkAsync(long id, string status, string? error)
        {
            using var c = Conn();
            await c.ExecuteAsync("UPDATE notification SET status=@status, error=@error, attempts=attempts+1, sent_at=CASE WHEN @status IN ('Sent','Simulated') THEN UTC_TIMESTAMP() ELSE sent_at END WHERE id=@id", new { id, status, error });
        }

        public async Task<List<NotificationRow>> ForAlertAsync(string alertId)
        {
            using var c = Conn();
            return (await c.QueryAsync<NotificationRow>("SELECT * FROM notification WHERE alert_id=@alertId ORDER BY id", new { alertId })).ToList();
        }

        public async Task<List<NotificationRow>> InboxAsync(string userId, bool unreadOnly)
        {
            using var c = Conn();
            return (await c.QueryAsync<NotificationRow>("SELECT * FROM notification WHERE channel='InApp' AND recipient=@userId" + (unreadOnly ? " AND read_at IS NULL" : "") + " ORDER BY id DESC LIMIT 50", new { userId })).ToList();
        }

        public async Task<int> UnreadCountAsync(string userId)
        {
            using var c = Conn();
            return await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM notification WHERE channel='InApp' AND recipient=@userId AND read_at IS NULL", new { userId });
        }

        public async Task MarkReadAsync(string userId, long? id)
        {
            using var c = Conn();
            await c.ExecuteAsync("UPDATE notification SET read_at=UTC_TIMESTAMP() WHERE channel='InApp' AND recipient=@userId AND read_at IS NULL" + (id.HasValue ? " AND id=@id" : ""), new { userId, id });
        }

        public async Task<List<NotificationRow>> LogAsync(string? channel, string? status, int take)
        {
            using var c = Conn();
            var w = new List<string>();
            if (channel != null) w.Add("channel=@channel");
            if (status != null) w.Add("status=@status");
            return (await c.QueryAsync<NotificationRow>("SELECT * FROM notification " + (w.Count > 0 ? "WHERE " + string.Join(" AND ", w) : "") + " ORDER BY id DESC LIMIT @take", new { channel, status, take })).ToList();
        }

        // ---- alert escalation fields ----
        public async Task SetAckDueAsync(string alertId, DateTime? due, int? step)
        {
            using var c = Conn();
            await c.ExecuteAsync("UPDATE alert SET ack_due_at=@due" + (step.HasValue ? ", escalation_step=@step" : "") + " WHERE id=@alertId", new { alertId, due, step });
        }

        public async Task<List<AlertView>> OverdueAlertsAsync()
        {
            using var c = Conn();
            return (await c.QueryAsync<AlertView>(@"SELECT a.id, a.dam_id AS DamId, d.name AS DamName, a.level, a.title, a.detail, a.rule_type AS RuleType, a.status,
                a.ack_due_at AS AckDueAt, a.escalation_step AS EscalationStep, a.first_at AS FirstAt, a.last_at AS LastAt
                FROM alert a LEFT JOIN dam d ON d.id=a.dam_id WHERE a.status='Open' AND a.level>=1 AND a.ack_due_at IS NOT NULL AND a.ack_due_at < UTC_TIMESTAMP()")).ToList();
        }

        public async Task LinkInspectionAsync(string alertId, string inspectionId)
        {
            using var c = Conn();
            await c.ExecuteAsync("UPDATE alert SET inspection_id=@inspectionId WHERE id=@alertId", new { alertId, inspectionId });
        }
    }

    public class Cls_ScheduleDAL : Cls_DbBase
    {
        public async Task<List<ScheduleRow>> ForDamAsync(string damId, string lifePhase)
        {
            using var c = Conn();
            return (await c.QueryAsync<ScheduleRow>(@"
SELECT s.id AS SensorId, s.code, s.parameter_code AS ParameterCode, p.group_code AS GroupCode, CAST(f.interval_hours AS DOUBLE) AS RequiredHours, f.note,
  s.reading_interval_min AS ConfiguredMinutes,
  (SELECT MAX(r.observed_at) FROM reading r WHERE r.sensor_id=s.id) AS LastReading,
  (SELECT COUNT(*) FROM reading r WHERE r.sensor_id=s.id AND r.quality<>'B' AND r.observed_at >= DATE_SUB((SELECT MAX(r2.observed_at) FROM reading r2 WHERE r2.sensor_id=s.id), INTERVAL 30 DAY)) AS ReadingsLast30d
FROM sensor s LEFT JOIN ref_parameter p ON p.code=s.parameter_code
LEFT JOIN ref_reading_frequency f ON f.group_code=p.group_code AND f.life_phase=@lifePhase
WHERE s.dam_id=@damId AND s.status<>'Retired' ORDER BY s.code", new { damId, lifePhase })).ToList();
        }
    }
}
