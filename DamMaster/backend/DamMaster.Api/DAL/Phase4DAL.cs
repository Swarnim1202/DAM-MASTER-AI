using Dapper;
using DamMaster.Api.Common;

namespace DamMaster.Api.Models
{
    public class GatewayHealth
    {
        public string Id { get; set; } = ""; public string Code { get; set; } = ""; public string DamId { get; set; } = ""; public string? DamCode { get; set; } public string? DamName { get; set; }
        public bool IsActive { get; set; } public int ExpectedIntervalMin { get; set; } public string? Transport { get; set; }
        public DateTime? LastSeenAt { get; set; } public DateTime? LastMessageAt { get; set; } public DateTime? LastStatusAt { get; set; }
        public double? BatteryV { get; set; } public int? SignalDbm { get; set; } public int? BufferedCount { get; set; } public string? Firmware { get; set; }
        public int Messages24h { get; set; } public int Readings24h { get; set; } public int Rejected24h { get; set; } public int Late24h { get; set; } public double? AvgLatencyS { get; set; }
        public int OpenAlerts { get; set; } public string State { get; set; } = ""; public DateTime CreatedAt { get; set; }
    }
    public class GatewayMessageRow
    {
        public long Id { get; set; } public string MessageId { get; set; } = ""; public string Transport { get; set; } = ""; public DateTime ReceivedAt { get; set; }
        public int Readings { get; set; } public int Accepted { get; set; } public int Rejected { get; set; } public int Quarantined { get; set; } public int Duplicates { get; set; } public int Late { get; set; }
        public int? MaxLatencyS { get; set; } public string? Error { get; set; }
    }
    public class GatewayStatusRow { public DateTime At { get; set; } public double? BatteryV { get; set; } public int? SignalDbm { get; set; } public int? BufferedCount { get; set; } public long? UptimeS { get; set; } public string? Firmware { get; set; } }
    public class GatewayStatusMessage { public string? GatewayId { get; set; } public double? BatteryV { get; set; } public int? SignalDbm { get; set; } public int? BufferedCount { get; set; } public long? UptimeS { get; set; } public string? Firmware { get; set; } public string? At { get; set; } }
    public record UpdateGatewayRequest(int ExpectedIntervalMin, bool IsActive);
    public class JobRunRow { public string Job { get; set; } = ""; public DateTime LastRunAt { get; set; } public bool LastOk { get; set; } public string? LastResult { get; set; } }
    public class ReconciliationRow
    {
        public string SensorId { get; set; } = ""; public string Code { get; set; } = ""; public int IntervalMin { get; set; } public int Expected { get; set; } public int Received { get; set; }
        public int Backfilled { get; set; } public int Gaps { get; set; } public double LongestGapHours { get; set; } public List<object> TopGaps { get; set; } = new();
    }
}

namespace DamMaster.Api.DAL
{
    using DamMaster.Api.Models;

    public class Cls_Gateway4DAL : Cls_DbBase
    {
        private const string HealthSql = @"
SELECT g.id, g.code, g.dam_id AS DamId, d.code AS DamCode, d.name AS DamName, g.is_active AS IsActive, g.expected_interval_min AS ExpectedIntervalMin, g.transport,
  g.last_seen_at AS LastSeenAt, g.last_message_at AS LastMessageAt, g.last_status_at AS LastStatusAt, g.battery_v AS BatteryV, g.signal_dbm AS SignalDbm,
  g.buffered_count AS BufferedCount, g.firmware, g.created_at AS CreatedAt,
  (SELECT COUNT(*) FROM gateway_message m WHERE m.gateway_id=g.id AND m.received_at > DATE_SUB(UTC_TIMESTAMP(), INTERVAL 1 DAY)) AS Messages24h,
  (SELECT COALESCE(SUM(m.accepted),0) FROM gateway_message m WHERE m.gateway_id=g.id AND m.received_at > DATE_SUB(UTC_TIMESTAMP(), INTERVAL 1 DAY)) AS Readings24h,
  (SELECT COALESCE(SUM(m.rejected+m.quarantined),0) FROM gateway_message m WHERE m.gateway_id=g.id AND m.received_at > DATE_SUB(UTC_TIMESTAMP(), INTERVAL 1 DAY)) AS Rejected24h,
  (SELECT COALESCE(SUM(m.late),0) FROM gateway_message m WHERE m.gateway_id=g.id AND m.received_at > DATE_SUB(UTC_TIMESTAMP(), INTERVAL 1 DAY)) AS Late24h,
  (SELECT AVG(m.max_latency_s) FROM gateway_message m WHERE m.gateway_id=g.id AND m.received_at > DATE_SUB(UTC_TIMESTAMP(), INTERVAL 1 DAY)) AS AvgLatencyS,
  (SELECT COUNT(*) FROM alert a WHERE a.gateway_id=g.id AND a.status<>'Closed') AS OpenAlerts
FROM gateway g LEFT JOIN dam d ON d.id=g.dam_id ";

        public static string StateOf(GatewayHealth g)
        {
            if (!g.IsActive) return "Disabled";
            var last = g.LastMessageAt ?? g.LastSeenAt;
            if (!last.HasValue) return "Never connected";
            var age = (DateTime.UtcNow - last.Value).TotalMinutes;
            return age <= 1.5 * g.ExpectedIntervalMin ? "Online" : age <= 3 * g.ExpectedIntervalMin ? "Late" : "Offline";
        }

        public async Task<List<GatewayHealth>> HealthAsync(string? id = null)
        {
            using var c = Conn();
            var list = (await c.QueryAsync<GatewayHealth>(HealthSql + (id != null ? "WHERE g.id=@id " : "") + "ORDER BY d.name, g.code", new { id })).ToList();
            foreach (var g in list) g.State = StateOf(g);
            return list;
        }

        public async Task UpdateSettingsAsync(string id, int interval, bool active)
        {
            using var c = Conn();
            await c.ExecuteAsync("UPDATE gateway SET expected_interval_min=@interval, is_active=@active WHERE id=@id", new { id, interval, active });
        }

        public async Task SetKeyAsync(string id, string hash)
        {
            using var c = Conn();
            await c.ExecuteAsync("UPDATE gateway SET key_hash=@hash WHERE id=@id", new { id, hash });
        }

        public async Task SetIntervalAsync(string id, int interval)
        {
            using var c = Conn();
            await c.ExecuteAsync("UPDATE gateway SET expected_interval_min=@interval WHERE id=@id", new { id, interval });
        }

        /// <summary>Registers a message id; returns false if this gateway already sent it (redelivery / retry).</summary>
        public async Task<bool> TryRegisterMessageAsync(string gatewayId, string messageId, string transport)
        {
            using var c = Conn();
            return await c.ExecuteAsync("INSERT IGNORE INTO gateway_message (gateway_id, message_id, transport, received_at) VALUES (@gatewayId, @messageId, @transport, UTC_TIMESTAMP())",
                new { gatewayId, messageId, transport }) == 1;
        }

        public async Task CompleteMessageAsync(string gatewayId, string messageId, int readings, int accepted, int rejected, int quarantined, int duplicates, int late, int? maxLatency, string? error)
        {
            using var c = Conn();
            await c.ExecuteAsync(@"UPDATE gateway_message SET readings=@readings, accepted=@accepted, rejected=@rejected, quarantined=@quarantined, duplicates=@duplicates,
                late=@late, max_latency_s=@maxLatency, error=@error WHERE gateway_id=@gatewayId AND message_id=@messageId",
                new { gatewayId, messageId, readings, accepted, rejected, quarantined, duplicates, late, maxLatency, error });
            await c.ExecuteAsync("UPDATE gateway SET last_message_at=UTC_TIMESTAMP(), last_seen_at=UTC_TIMESTAMP() WHERE id=@gatewayId", new { gatewayId });
        }

        public async Task SetTransportAsync(string id, string transport)
        {
            using var c = Conn();
            await c.ExecuteAsync("UPDATE gateway SET transport=@transport, last_seen_at=UTC_TIMESTAMP() WHERE id=@id", new { id, transport });
        }

        public async Task UpdateStatusAsync(string id, GatewayStatusMessage s)
        {
            using var c = Conn();
            await c.ExecuteAsync(@"UPDATE gateway SET battery_v=COALESCE(@BatteryV, battery_v), signal_dbm=COALESCE(@SignalDbm, signal_dbm), buffered_count=COALESCE(@BufferedCount, buffered_count),
                firmware=COALESCE(@Firmware, firmware), last_status_at=UTC_TIMESTAMP(), last_seen_at=UTC_TIMESTAMP() WHERE id=@id",
                new { id, s.BatteryV, s.SignalDbm, s.BufferedCount, s.Firmware });
            await c.ExecuteAsync("INSERT INTO gateway_status_log (gateway_id, at, battery_v, signal_dbm, buffered_count, uptime_s, firmware) VALUES (@id, UTC_TIMESTAMP(), @BatteryV, @SignalDbm, @BufferedCount, @UptimeS, @Firmware)",
                new { id, s.BatteryV, s.SignalDbm, s.BufferedCount, s.UptimeS, s.Firmware });
        }

        public async Task<List<GatewayMessageRow>> MessagesAsync(string id, int take)
        {
            using var c = Conn();
            return (await c.QueryAsync<GatewayMessageRow>("SELECT * FROM gateway_message WHERE gateway_id=@id ORDER BY id DESC LIMIT @take", new { id, take })).ToList();
        }

        public async Task<List<GatewayStatusRow>> StatusLogAsync(string id, int days)
        {
            using var c = Conn();
            return (await c.QueryAsync<GatewayStatusRow>("SELECT at, battery_v, signal_dbm, buffered_count, uptime_s, firmware FROM gateway_status_log WHERE gateway_id=@id AND at > DATE_SUB(UTC_TIMESTAMP(), INTERVAL @days DAY) ORDER BY at", new { id, days })).ToList();
        }
    }

    public class Cls_AggregateDAL : Cls_DbBase
    {
        public async Task RefreshAsync(string sensorId, DateTime from, DateTime to)
        {
            using var c = Conn();
            await c.ExecuteAsync(@"INSERT INTO reading_daily (sensor_id, day, n, valid_n, min_v, max_v, avg_v)
                SELECT sensor_id, DATE(observed_at), COUNT(*), SUM(quality<>'B'), MIN(CASE WHEN quality<>'B' THEN value END), MAX(CASE WHEN quality<>'B' THEN value END), AVG(CASE WHEN quality<>'B' THEN value END)
                FROM reading WHERE sensor_id=@sensorId AND observed_at >= @d1 AND observed_at < @d2 GROUP BY sensor_id, DATE(observed_at)
                ON DUPLICATE KEY UPDATE n=VALUES(n), valid_n=VALUES(valid_n), min_v=VALUES(min_v), max_v=VALUES(max_v), avg_v=VALUES(avg_v)",
                new { sensorId, d1 = from.Date, d2 = to.Date.AddDays(1) }, commandTimeout: 300);
        }

        public async Task<List<ReadingPoint>> DailyAsync(string sensorId, DateTime? from)
        {
            using var c = Conn();
            return (await c.QueryAsync<ReadingPoint>(@"SELECT CAST(day AS DATETIME) AS T, avg_v AS V, 'D' AS Q, CONCAT('daily mean of ', valid_n, ' (', ROUND(min_v,3), ' – ', ROUND(max_v,3), ')') AS Rule
                FROM reading_daily WHERE sensor_id=@sensorId AND avg_v IS NOT NULL" + (from.HasValue ? " AND day >= @from" : "") + " ORDER BY day", new { sensorId, from })).ToList();
        }

        public async Task<int> CountAsync(string sensorId, DateTime? from)
        {
            using var c = Conn();
            return await c.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(n),0) FROM reading_daily WHERE sensor_id=@sensorId" + (from.HasValue ? " AND day >= @from" : ""), new { sensorId, from });
        }

        /// <summary>Per sensor: gateway readings that arrived late (created_at − observed_at &gt; 2 intervals; Excel back-loads excluded) and gaps still open.</summary>
        public async Task<List<ReconciliationRow>> ReconciliationAsync(string damId, int days)
        {
            using var c = Conn();
            var sensors = await c.QueryAsync<(string id, string code, int interval)>("SELECT id, code, reading_interval_min FROM sensor WHERE dam_id=@damId AND status<>'Retired' ORDER BY code", new { damId });
            var rows = new List<ReconciliationRow>();
            foreach (var (id, code, interval) in sensors)
            {
                var end = await c.ExecuteScalarAsync<DateTime?>("SELECT MAX(observed_at) FROM reading WHERE sensor_id=@id", new { id }) ?? DateTime.UtcNow;
                var start = end.AddDays(-days);
                var pts = (await c.QueryAsync<(DateTime t, DateTime created, string? source)>(@"SELECT r.observed_at, r.created_at, b.source FROM reading r LEFT JOIN import_batch b ON b.id=r.import_id
                    WHERE r.sensor_id=@id AND r.observed_at > @start AND r.observed_at <= @end ORDER BY r.observed_at", new { id, start, end })).ToList();
                var times = pts.Select(p => p.t).Prepend(start).ToList();
                var gaps = times.Zip(times.Skip(1), (a, b) => (from: a, to: b, h: (b - a).TotalHours)).Where(g => g.h > 2.0 * interval / 60).OrderByDescending(g => g.h).ToList();
                rows.Add(new ReconciliationRow
                {
                    SensorId = id, Code = code, IntervalMin = interval, Expected = (int)Math.Round(days * 1440.0 / interval), Received = pts.Count,
                    Backfilled = pts.Count(p => p.source is "MQTT" or "HTTP" or "API" && (p.created - p.t).TotalMinutes > 2.0 * interval), Gaps = gaps.Count, LongestGapHours = Math.Round(gaps.Select(g => g.h).DefaultIfEmpty(0).Max(), 1),
                    TopGaps = gaps.Take(5).Select(g => (object)new { g.from, g.to, hours = Math.Round(g.h, 1) }).ToList()
                });
            }
            return rows;
        }
    }

    public class Cls_JobDAL : Cls_DbBase
    {
        public async Task RecordAsync(string job, bool ok, string result)
        {
            using var c = Conn();
            await c.ExecuteAsync("INSERT INTO job_run (job, last_run_at, last_ok, last_result) VALUES (@job, UTC_TIMESTAMP(), @ok, @result) ON DUPLICATE KEY UPDATE last_run_at=VALUES(last_run_at), last_ok=VALUES(last_ok), last_result=VALUES(last_result)",
                new { job, ok, result = result.Length > 490 ? result[..490] : result });
        }

        public async Task<List<JobRunRow>> ListAsync()
        {
            using var c = Conn();
            return (await c.QueryAsync<JobRunRow>("SELECT job, last_run_at AS LastRunAt, last_ok AS LastOk, last_result AS LastResult FROM job_run ORDER BY job")).ToList();
        }

        public async Task<object> StorageAsync()
        {
            using var c = Conn();
            var readings = await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM reading");
            var daily = await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM reading_daily");
            var parts = (await c.QueryAsync<(string name, long rows)>("SELECT PARTITION_NAME, TABLE_ROWS FROM information_schema.PARTITIONS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='reading' AND PARTITION_NAME IS NOT NULL ORDER BY PARTITION_ORDINAL_POSITION"))
                .Select(p => new { p.name, p.rows }).ToList();
            var sizeMb = await c.ExecuteScalarAsync<double?>("SELECT ROUND(SUM(DATA_LENGTH+INDEX_LENGTH)/1048576,1) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()");
            return new { readings, dailyAggregates = daily, partitioned = parts.Count > 0, partitions = parts, databaseSizeMb = sizeMb };
        }
    }
}
