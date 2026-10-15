using Dapper;
using DamMaster.Api.Common;
using DamMaster.Api.Models;
using MySqlConnector;

namespace DamMaster.Api.DAL;

/// <summary>Base for all DAL classes. Each call opens a pooled connection (MySqlConnector pools by default).</summary>
public abstract class Cls_DbBase
{
    protected static MySqlConnection Conn() => new(AppConfig.ConnectionString);
}

// =====================================================================
public class Cls_ReferenceDAL : Cls_DbBase
{
    public async Task<object> GetCatalogAsync(string name)
    {
        using var c = Conn();
        return name switch
        {
            "dam-types" => await c.QueryAsync<RefDamType>("SELECT * FROM ref_dam_type ORDER BY sort_order"),
            "dam-functions" => await c.QueryAsync<RefDamFunction>("SELECT * FROM ref_dam_function ORDER BY sort_order"),
            "hazard-classes" => await c.QueryAsync<RefHazardClass>("SELECT * FROM ref_hazard_class ORDER BY risk_factor DESC"),
            "failure-modes" => await c.QueryAsync<RefFailureMode>("SELECT * FROM ref_failure_mode ORDER BY sort_order"),
            "parameter-groups" => await c.QueryAsync<RefParameterGroup>("SELECT * FROM ref_parameter_group ORDER BY sort_order"),
            "parameters" => await c.QueryAsync<RefParameter>("SELECT * FROM ref_parameter ORDER BY group_code, code"),
            "instrument-types" => await c.QueryAsync<RefInstrumentType>("SELECT * FROM ref_instrument_type ORDER BY sort_order"),
            "failure-mode-parameters" => await c.QueryAsync<RefFailureModeParameter>("SELECT * FROM ref_failure_mode_parameter"),
            "standards" => await c.QueryAsync<RefStandard>("SELECT * FROM ref_standard ORDER BY sort_order"),
            "alert-levels" => await c.QueryAsync<RefAlertLevel>("SELECT * FROM ref_alert_level ORDER BY level"),
            "interpretations" => await c.QueryAsync<RefInterpretation>("SELECT * FROM ref_interpretation ORDER BY group_code, id"),
            "dhi-components" => await c.QueryAsync<DhiWeight>("SELECT code, name, weight, description FROM ref_dhi_component ORDER BY sort_order"),
            _ => throw new BusinessException($"Unknown catalogue '{name}'.")
        };
    }

    public async Task<List<RefParameter>> ParametersAsync()
    {
        using var c = Conn();
        return (await c.QueryAsync<RefParameter>("SELECT * FROM ref_parameter")).ToList();
    }

    public async Task<List<RefDamType>> DamTypesAsync()
    {
        using var c = Conn();
        return (await c.QueryAsync<RefDamType>("SELECT * FROM ref_dam_type")).ToList();
    }

    public async Task<List<RefHazardClass>> HazardClassesAsync()
    {
        using var c = Conn();
        return (await c.QueryAsync<RefHazardClass>("SELECT * FROM ref_hazard_class")).ToList();
    }

    public async Task<List<RefInterpretation>> InterpretationsForGroupAsync(string group)
    {
        using var c = Conn();
        return (await c.QueryAsync<RefInterpretation>("SELECT * FROM ref_interpretation WHERE group_code=@group ORDER BY id", new { group })).ToList();
    }

    public async Task<int> VerifyStandardAsync(string code, string edition, string? clauses, string by)
    {
        using var c = Conn();
        return await c.ExecuteAsync("UPDATE ref_standard SET edition=@edition, clauses=@clauses, verified_by=@by, verified_at=UTC_TIMESTAMP() WHERE code=@code",
            new { code, edition, clauses, by });
    }

    public async Task<int> RevokeStandardAsync(string code)
    {
        using var c = Conn();
        return await c.ExecuteAsync("UPDATE ref_standard SET verified_by=NULL, verified_at=NULL WHERE code=@code", new { code });
    }
}

// =====================================================================
public class Cls_UserDAL : Cls_DbBase
{
    public async Task<AppUser?> GetByUsernameAsync(string username)
    {
        using var c = Conn();
        return await c.QueryFirstOrDefaultAsync<AppUser>("SELECT * FROM app_user WHERE username=@username", new { username });
    }

    public async Task<AppUser?> GetAsync(string id)
    {
        using var c = Conn();
        return await c.QueryFirstOrDefaultAsync<AppUser>("SELECT * FROM app_user WHERE id=@id", new { id });
    }

    public async Task<List<UserView>> ListAsync()
    {
        using var c = Conn();
        return (await c.QueryAsync<UserView>("SELECT id, username, full_name AS FullName, role, is_active AS IsActive, must_change_password AS MustChangePassword, last_login_at AS LastLoginAt, email, phone FROM app_user ORDER BY username")).ToList();
    }

    public async Task<int> CountAsync()
    {
        using var c = Conn();
        return await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM app_user");
    }

    public async Task<int> CountActiveAdminsAsync()
    {
        using var c = Conn();
        return await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM app_user WHERE role='Admin' AND is_active=1");
    }

    public async Task InsertAsync(AppUser u)
    {
        using var c = Conn();
        await c.ExecuteAsync(@"INSERT INTO app_user (id, username, full_name, role, password_hash, is_active, must_change_password, email, phone, created_at)
                              VALUES (@Id, @Username, @FullName, @Role, @PasswordHash, @IsActive, @MustChangePassword, @Email, @Phone, UTC_TIMESTAMP())", u);
    }

    public async Task UpdateAsync(string id, string fullName, string role, bool isActive, string? email, string? phone)
    {
        using var c = Conn();
        await c.ExecuteAsync("UPDATE app_user SET full_name=@fullName, role=@role, is_active=@isActive, email=@email, phone=@phone WHERE id=@id", new { id, fullName, role, isActive, email, phone });
    }

    public async Task<List<AppUser>> ActiveByRolesAsync(IEnumerable<string> roles)
    {
        var list = roles.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).ToList();
        if (list.Count == 0) return new();
        using var c = Conn();
        return (await c.QueryAsync<AppUser>("SELECT * FROM app_user WHERE is_active=1 AND role IN @list", new { list })).ToList();
    }

    public async Task SetPasswordAsync(string id, string hash, bool mustChange)
    {
        using var c = Conn();
        await c.ExecuteAsync("UPDATE app_user SET password_hash=@hash, must_change_password=@mustChange, failed_logins=0, locked_until=NULL WHERE id=@id", new { id, hash, mustChange });
    }

    public async Task RecordLoginAsync(string id, bool success)
    {
        using var c = Conn();
        if (success)
            await c.ExecuteAsync("UPDATE app_user SET failed_logins=0, locked_until=NULL, last_login_at=UTC_TIMESTAMP() WHERE id=@id", new { id });
        else
            await c.ExecuteAsync(@"UPDATE app_user SET failed_logins=failed_logins+1,
                                   locked_until = CASE WHEN failed_logins+1 >= 5 THEN DATE_ADD(UTC_TIMESTAMP(), INTERVAL 15 MINUTE) ELSE locked_until END,
                                   failed_logins = CASE WHEN failed_logins >= 5 THEN 0 ELSE failed_logins END
                                   WHERE id=@id", new { id });
    }
}

// =====================================================================
public class Cls_AuditDAL : Cls_DbBase
{
    public async Task WriteAsync(string? userId, string? username, string module, string action, string? entity = null, string? entityId = null, string? detail = null)
    {
        using var c = Conn();
        await c.ExecuteAsync(@"INSERT INTO audit_log (at, user_id, username, module, action, entity, entity_id, detail)
                              VALUES (UTC_TIMESTAMP(), @userId, @username, @module, @action, @entity, @entityId, @detail)",
            new { userId, username, module, action = Trunc(action, 300), entity, entityId, detail });
    }

    public async Task<List<AuditEntry>> ListAsync(int take, string? entityId)
    {
        using var c = Conn();
        var sql = "SELECT id, at, username, module, action, entity, entity_id AS EntityId FROM audit_log "
                + (entityId != null ? "WHERE entity_id=@entityId " : "") + "ORDER BY id DESC LIMIT @take";
        return (await c.QueryAsync<AuditEntry>(sql, new { take, entityId })).ToList();
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n];
}

// =====================================================================
public class Cls_DamDAL : Cls_DbBase
{
    public async Task<List<Dam>> ListAsync()
    {
        using var c = Conn();
        return (await c.QueryAsync<Dam>("SELECT * FROM dam WHERE is_active=1 ORDER BY name")).ToList();
    }

    public async Task<Dam?> GetAsync(string id)
    {
        using var c = Conn();
        return await c.QueryFirstOrDefaultAsync<Dam>("SELECT * FROM dam WHERE id=@id", new { id });
    }

    public async Task<Dam?> GetByCodeAsync(string code)
    {
        using var c = Conn();
        return await c.QueryFirstOrDefaultAsync<Dam>("SELECT * FROM dam WHERE code=@code", new { code });
    }

    public async Task InsertAsync(Dam d)
    {
        using var c = Conn();
        await c.ExecuteAsync(@"INSERT INTO dam (id, code, name, state, district, river, latitude, longitude, dam_type_code, function_code, hazard_code, height_m, crest_length_m,
            frl_m, mwl_m, mddl_m, crest_level_m, gross_storage_mcm, seismic_zone, is_specified, life_phase, eap_status, owner_name, is_active, created_at)
            VALUES (@Id, @Code, @Name, @State, @District, @River, @Latitude, @Longitude, @DamTypeCode, @FunctionCode, @HazardCode, @HeightM, @CrestLengthM,
            @FrlM, @MwlM, @MddlM, @CrestLevelM, @GrossStorageMcm, @SeismicZone, @IsSpecified, @LifePhase, @EapStatus, @OwnerName, 1, UTC_TIMESTAMP())", d);
    }

    public async Task UpdateAsync(Dam d)
    {
        using var c = Conn();
        await c.ExecuteAsync(@"UPDATE dam SET name=@Name, state=@State, district=@District, river=@River, latitude=@Latitude, longitude=@Longitude,
            dam_type_code=@DamTypeCode, function_code=@FunctionCode, hazard_code=@HazardCode, height_m=@HeightM, crest_length_m=@CrestLengthM,
            frl_m=@FrlM, mwl_m=@MwlM, mddl_m=@MddlM, crest_level_m=@CrestLevelM, gross_storage_mcm=@GrossStorageMcm, seismic_zone=@SeismicZone,
            is_specified=@IsSpecified, life_phase=@LifePhase, eap_status=@EapStatus, owner_name=@OwnerName, updated_at=UTC_TIMESTAMP() WHERE id=@Id", d);
    }

    public async Task<List<DamSummary>> SummariesAsync()
    {
        using var c = Conn();
        const string sql = @"
SELECT d.id, d.code, d.name, d.state, d.dam_type_code AS DamTypeCode, d.hazard_code AS HazardCode, d.life_phase AS LifePhase, d.frl_m AS FrlM,
  (SELECT COUNT(*) FROM sensor s WHERE s.dam_id=d.id AND s.status<>'Retired') AS Sensors,
  (SELECT COUNT(*) FROM reading r JOIN sensor s ON s.id=r.sensor_id WHERE s.dam_id=d.id) AS Readings,
  (SELECT MAX(r.observed_at) FROM reading r JOIN sensor s ON s.id=r.sensor_id WHERE s.dam_id=d.id) AS DataAsOf,
  (SELECT COUNT(*) FROM alert a WHERE a.dam_id=d.id AND a.status<>'Closed') AS OpenAlerts,
  (SELECT COALESCE(MAX(a.level),0) FROM alert a WHERE a.dam_id=d.id AND a.status<>'Closed') AS MaxOpenLevel,
  (SELECT COUNT(*) FROM alert a WHERE a.dam_id=d.id AND a.status<>'Closed' AND a.level=1) AS L1,
  (SELECT COUNT(*) FROM alert a WHERE a.dam_id=d.id AND a.status<>'Closed' AND a.level=2) AS L2,
  (SELECT COUNT(*) FROM alert a WHERE a.dam_id=d.id AND a.status<>'Closed' AND a.level=3) AS L3,
  (SELECT COUNT(*) FROM pfma p WHERE p.dam_id=d.id AND p.status='Active') AS ActivePfma,
  (SELECT r.value FROM reading r JOIN sensor s ON s.id=r.sensor_id WHERE s.dam_id=d.id AND s.parameter_code='RWL' AND r.quality<>'B' ORDER BY r.observed_at DESC LIMIT 1) AS ReservoirLevel
FROM dam d WHERE d.is_active=1 ORDER BY MaxOpenLevel DESC, OpenAlerts DESC, d.name";
        return (await c.QueryAsync<DamSummary>(sql)).ToList();
    }
}

// =====================================================================
public class Cls_SensorDAL : Cls_DbBase
{
    public async Task<List<Sensor>> ListByDamAsync(string damId)
    {
        using var c = Conn();
        return (await c.QueryAsync<Sensor>("SELECT * FROM sensor WHERE dam_id=@damId ORDER BY code", new { damId })).ToList();
    }

    public async Task<Sensor?> GetAsync(string id)
    {
        using var c = Conn();
        return await c.QueryFirstOrDefaultAsync<Sensor>("SELECT * FROM sensor WHERE id=@id", new { id });
    }

    public async Task<Dictionary<string, Sensor>> ByCodesAsync(IEnumerable<string> codes)
    {
        var list = codes.Distinct().ToList();
        if (list.Count == 0) return new();
        using var c = Conn();
        return (await c.QueryAsync<Sensor>("SELECT * FROM sensor WHERE code IN @list", new { list })).ToDictionary(s => s.Code, StringComparer.OrdinalIgnoreCase);
    }

    public async Task InsertAsync(Sensor s)
    {
        using var c = Conn();
        await c.ExecuteAsync(@"INSERT INTO sensor (id, dam_id, code, parameter_code, instrument_type_code, location, elevation_m, eng_unit, expected_min, expected_max,
            direction, reading_interval_min, calibration_due, status, latitude, longitude, offset_m, chainage_m, created_at)
            VALUES (@Id, @DamId, @Code, @ParameterCode, @InstrumentTypeCode, @Location, @ElevationM, @EngUnit, @ExpectedMin, @ExpectedMax,
            @Direction, @ReadingIntervalMin, @CalibrationDue, @Status, @Latitude, @Longitude, @OffsetM, @ChainageM, UTC_TIMESTAMP())", s);
    }

    public async Task UpdateAsync(Sensor s)
    {
        using var c = Conn();
        await c.ExecuteAsync(@"UPDATE sensor SET parameter_code=@ParameterCode, instrument_type_code=@InstrumentTypeCode, location=@Location, elevation_m=@ElevationM,
            eng_unit=@EngUnit, expected_min=@ExpectedMin, expected_max=@ExpectedMax, direction=@Direction, reading_interval_min=@ReadingIntervalMin,
            calibration_due=@CalibrationDue, status=@Status, latitude=@Latitude, longitude=@Longitude, offset_m=@OffsetM, chainage_m=@ChainageM, updated_at=UTC_TIMESTAMP() WHERE id=@Id", s);
    }

    public async Task<List<SensorStatusView>> StatusListAsync(string damId)
    {
        using var c = Conn();
        const string sql = @"
SELECT s.id, s.code, s.parameter_code AS ParameterCode, p.name AS ParameterName, s.location, s.eng_unit AS EngUnit, s.status,
  lr.value AS LatestValue, lr.observed_at AS LatestAt, lr.quality AS LatestQuality,
  (SELECT COUNT(*) FROM reading r WHERE r.sensor_id=s.id) AS ReadingCount,
  (SELECT COUNT(*) FROM alert a WHERE a.sensor_id=s.id AND a.status<>'Closed') AS OpenAlerts,
  (SELECT COALESCE(MAX(a.level),0) FROM alert a WHERE a.sensor_id=s.id AND a.status<>'Closed') AS MaxOpenLevel,
  t.alert_value AS AlertValue, t.alarm_value AS AlarmValue, t.emergency_value AS EmergencyValue, (t.approved_by IS NOT NULL) AS ThresholdApproved
FROM sensor s
LEFT JOIN ref_parameter p ON p.code=s.parameter_code
LEFT JOIN sensor_threshold t ON t.sensor_id=s.id AND t.is_current=1
LEFT JOIN reading lr ON lr.id=(SELECT r2.id FROM reading r2 WHERE r2.sensor_id=s.id ORDER BY r2.observed_at DESC LIMIT 1)
WHERE s.dam_id=@damId ORDER BY s.code";
        return (await c.QueryAsync<SensorStatusView>(sql, new { damId })).ToList();
    }

    // ---- thresholds (versioned) ----
    public async Task<SensorThreshold?> CurrentThresholdAsync(string sensorId)
    {
        using var c = Conn();
        return await c.QueryFirstOrDefaultAsync<SensorThreshold>("SELECT * FROM sensor_threshold WHERE sensor_id=@sensorId AND is_current=1 LIMIT 1", new { sensorId });
    }

    public async Task<List<SensorThreshold>> ThresholdHistoryAsync(string sensorId)
    {
        using var c = Conn();
        return (await c.QueryAsync<SensorThreshold>("SELECT * FROM sensor_threshold WHERE sensor_id=@sensorId ORDER BY created_at DESC", new { sensorId })).ToList();
    }

    public async Task InsertThresholdAsync(SensorThreshold t)
    {
        using var c = Conn();
        await c.OpenAsync();
        using var tx = await c.BeginTransactionAsync();
        await c.ExecuteAsync("UPDATE sensor_threshold SET is_current=0 WHERE sensor_id=@SensorId", t, tx);
        await c.ExecuteAsync(@"INSERT INTO sensor_threshold (id, sensor_id, alert_value, alarm_value, emergency_value, rate_limit_per_day, z_limit, persistence, method, basis, created_by, created_at, is_current)
            VALUES (@Id, @SensorId, @AlertValue, @AlarmValue, @EmergencyValue, @RateLimitPerDay, @ZLimit, @Persistence, @Method, @Basis, @CreatedBy, UTC_TIMESTAMP(), 1)", t, tx);
        await tx.CommitAsync();
    }

    public async Task<int> ApproveThresholdAsync(string thresholdId, string by)
    {
        using var c = Conn();
        return await c.ExecuteAsync("UPDATE sensor_threshold SET approved_by=@by, approved_at=UTC_TIMESTAMP() WHERE id=@thresholdId AND approved_by IS NULL", new { thresholdId, by });
    }
}

// =====================================================================
public class Cls_ReadingDAL : Cls_DbBase
{
    public async Task<List<ReadingPoint>> SeriesAsync(string sensorId, DateTime? from, DateTime? to, bool validOnly = false)
    {
        using var c = Conn();
        var sql = "SELECT observed_at AS T, value AS V, quality AS Q, qc_rule AS Rule FROM reading WHERE sensor_id=@sensorId"
                + (from.HasValue ? " AND observed_at>=@from" : "") + (to.HasValue ? " AND observed_at<=@to" : "")
                + (validOnly ? " AND quality<>'B'" : "") + " ORDER BY observed_at";
        return (await c.QueryAsync<ReadingPoint>(sql, new { sensorId, from, to })).ToList();
    }

    public async Task<DateTime?> LatestTimeAsync(string sensorId)
    {
        using var c = Conn();
        return await c.ExecuteScalarAsync<DateTime?>("SELECT MAX(observed_at) FROM reading WHERE sensor_id=@sensorId", new { sensorId });
    }

    public async Task<HashSet<DateTime>> ExistingTimesAsync(string sensorId, DateTime from, DateTime to)
    {
        using var c = Conn();
        return (await c.QueryAsync<DateTime>("SELECT observed_at FROM reading WHERE sensor_id=@sensorId AND observed_at BETWEEN @from AND @to", new { sensorId, from, to })).ToHashSet();
    }

    /// <summary>Multi-row insert in chunks of 500 inside one transaction.</summary>
    public async Task<int> BulkInsertAsync(List<Reading> rows, string importId)
    {
        if (rows.Count == 0) return 0;
        using var c = Conn();
        await c.OpenAsync();
        using var tx = await c.BeginTransactionAsync();
        var n = 0;
        foreach (var chunk in rows.Chunk(500))
        {
            var sb = new System.Text.StringBuilder("INSERT IGNORE INTO reading (sensor_id, observed_at, value, raw_value, quality, qc_rule, import_id, created_at) VALUES ");
            var p = new DynamicParameters();
            for (var i = 0; i < chunk.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append($"(@s{i},@t{i},@v{i},@r{i},@q{i},@c{i},@imp,UTC_TIMESTAMP())");
                p.Add($"s{i}", chunk[i].SensorId); p.Add($"t{i}", chunk[i].ObservedAt); p.Add($"v{i}", chunk[i].Value);
                p.Add($"r{i}", chunk[i].RawValue); p.Add($"q{i}", chunk[i].Quality); p.Add($"c{i}", chunk[i].QcRule);
            }
            p.Add("imp", importId);
            n += await c.ExecuteAsync(sb.ToString(), p, tx);
        }
        await tx.CommitAsync();
        return n;
    }
}

// =====================================================================
public class Cls_ImportDAL : Cls_DbBase
{
    public async Task InsertBatchAsync(string id, string source, string? fileName, string by, ImportResult r)
    {
        using var c = Conn();
        await c.ExecuteAsync(@"INSERT INTO import_batch (id, source, file_name, created_by, created_at, rows_read, good, suspect, bad, rejected, quarantined, duplicates, dams_upserted, sensors_upserted)
            VALUES (@id, @source, @fileName, @by, UTC_TIMESTAMP(), @RowsRead, @Good, @Suspect, @Bad, @Rejected, @Quarantined, @Duplicates, @DamsUpserted, @SensorsUpserted)",
            new { id, source, fileName, by, r.RowsRead, r.Good, r.Suspect, r.Bad, r.Rejected, r.Quarantined, r.Duplicates, r.DamsUpserted, r.SensorsUpserted });
        if (r.Issues.Count > 0)
            await c.ExecuteAsync("INSERT INTO import_issue (import_id, sheet, row_no, item_code, outcome, rule_code, message) VALUES (@importId, @Sheet, @RowNo, @ItemCode, @Outcome, @RuleCode, @Message)",
                r.Issues.Take(5000).Select(i => new { importId = id, i.Sheet, i.RowNo, i.ItemCode, i.Outcome, i.RuleCode, i.Message }));
    }

    public async Task InsertQuarantineAsync(string importId, IEnumerable<(string? dam, string? sensor, string? param, DateTime? at, double? value, string? unit)> rows)
    {
        using var c = Conn();
        await c.ExecuteAsync(@"INSERT INTO quarantine (id, import_id, dam_code, sensor_code, parameter_code, observed_at, value, unit, status, created_at)
            VALUES (@id, @importId, @dam, @sensor, @param, @at, @value, @unit, 'Held', UTC_TIMESTAMP())",
            rows.Select(r => new { id = Ids.New(), importId, r.dam, r.sensor, r.param, r.at, r.value, r.unit }));
    }

    public async Task<IEnumerable<dynamic>> BatchesAsync(int take)
    {
        using var c = Conn();
        return await c.QueryAsync("SELECT * FROM import_batch ORDER BY created_at DESC LIMIT @take", new { take });
    }

    public async Task<IEnumerable<dynamic>> IssuesAsync(string importId)
    {
        using var c = Conn();
        return await c.QueryAsync("SELECT sheet, row_no AS rowNo, item_code AS itemCode, outcome, rule_code AS ruleCode, message FROM import_issue WHERE import_id=@importId ORDER BY id LIMIT 5000", new { importId });
    }

    public async Task<IEnumerable<dynamic>> QuarantineAsync()
    {
        using var c = Conn();
        return await c.QueryAsync("SELECT id, dam_code AS damCode, sensor_code AS sensorCode, parameter_code AS parameterCode, observed_at AS observedAt, value, unit, created_at AS createdAt FROM quarantine WHERE status='Held' ORDER BY created_at DESC LIMIT 2000");
    }

    public async Task<int> DiscardQuarantineAsync()
    {
        using var c = Conn();
        return await c.ExecuteAsync("UPDATE quarantine SET status='Discarded' WHERE status='Held'");
    }
}

// =====================================================================
public class Cls_AlertDAL : Cls_DbBase
{
    private const string SelectView = @"SELECT a.id, a.dam_id AS DamId, d.name AS DamName, a.sensor_id AS SensorId, s.code AS SensorCode, s.parameter_code AS ParameterCode, a.pfma_id AS PfmaId,
        a.level, a.rule_type AS RuleType, a.title, a.detail, a.value, a.limit_value AS LimitValue, a.first_at AS FirstAt, a.last_at AS LastAt, a.occurrences,
        a.status, a.ack_by AS AckBy, a.ack_at AS AckAt, a.ack_note AS AckNote, a.closed_by AS ClosedBy, a.closed_at AS ClosedAt, a.disposition, a.close_note AS CloseNote,
        a.ack_due_at AS AckDueAt, a.escalation_step AS EscalationStep, a.inspection_id AS InspectionId, a.gateway_id AS GatewayId, a.survey_id AS SurveyId
        FROM alert a LEFT JOIN dam d ON d.id=a.dam_id LEFT JOIN sensor s ON s.id=a.sensor_id ";

    public async Task<AlertView?> FindOpenAsync(string? sensorId, string? pfmaId, string ruleType)
    {
        using var c = Conn();
        return await c.QueryFirstOrDefaultAsync<AlertView>(SelectView +
            "WHERE a.status<>'Closed' AND a.rule_type=@ruleType AND " + (sensorId != null ? "a.sensor_id=@sensorId" : "a.pfma_id=@pfmaId") + " LIMIT 1",
            new { sensorId, pfmaId, ruleType });
    }

    public async Task InsertAsync(AlertView a)
    {
        using var c = Conn();
        await c.ExecuteAsync(@"INSERT INTO alert (id, dam_id, sensor_id, pfma_id, gateway_id, survey_id, level, rule_type, title, detail, value, limit_value, first_at, last_at, occurrences, status, created_at)
            VALUES (@Id, @DamId, @SensorId, @PfmaId, @GatewayId, @SurveyId, @Level, @RuleType, @Title, @Detail, @Value, @LimitValue, @FirstAt, @LastAt, 1, 'Open', UTC_TIMESTAMP())", a);
    }

    /// <summary>Repeat of an open alert: keep the highest level; an escalation re-opens an acknowledged alert.</summary>
    public async Task RaiseAgainAsync(string id, int level, string title, string? detail, double? value, double? limit, DateTime lastAt)
    {
        using var c = Conn();
        await c.ExecuteAsync(@"UPDATE alert SET occurrences=occurrences+1, last_at=GREATEST(last_at, @lastAt), title=@title, detail=@detail, value=@value, limit_value=@limit,
            status = CASE WHEN @level > level THEN 'Open' ELSE status END, level=GREATEST(level, @level) WHERE id=@id",
            new { id, level, title, detail, value, limit, lastAt });
    }

    public async Task<List<AlertView>> ListAsync(string? damId, string? status, int? minLevel, int take)
    {
        using var c = Conn();
        var where = new List<string>();
        if (damId != null) where.Add("a.dam_id=@damId");
        if (status == "Active") where.Add("a.status<>'Closed'"); else if (status != null) where.Add("a.status=@status");
        if (minLevel.HasValue) where.Add("a.level>=@minLevel");
        var sql = SelectView + (where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "") + " ORDER BY (a.status='Closed'), a.level DESC, a.last_at DESC LIMIT @take";
        return (await c.QueryAsync<AlertView>(sql, new { damId, status, minLevel, take })).ToList();
    }

    public async Task<AlertView?> GetAsync(string id)
    {
        using var c = Conn();
        return await c.QueryFirstOrDefaultAsync<AlertView>(SelectView + "WHERE a.id=@id", new { id });
    }

    public async Task<AlertView?> FindOpenGatewayAsync(string gatewayId, string ruleType)
    {
        using var c = Conn();
        return await c.QueryFirstOrDefaultAsync<AlertView>(SelectView + "WHERE a.status<>'Closed' AND a.rule_type=@ruleType AND a.gateway_id=@gatewayId LIMIT 1", new { gatewayId, ruleType });
    }

    /// <summary>Data-health alerts (System, Gateway) close themselves when the cause disappears; safety alerts never do.</summary>
    public async Task<int> AutoResolveAsync(string id, string reason)
    {
        using var c = Conn();
        return await c.ExecuteAsync("UPDATE alert SET status='Closed', closed_by='system', closed_at=UTC_TIMESTAMP(), disposition='Resolved', close_note=@reason WHERE id=@id AND status<>'Closed' AND rule_type IN ('System','Gateway','GatewayBattery')", new { id, reason });
    }

    public async Task<int> AcknowledgeAsync(string id, string by, string? note)
    {
        using var c = Conn();
        return await c.ExecuteAsync("UPDATE alert SET status='Acknowledged', ack_by=@by, ack_at=UTC_TIMESTAMP(), ack_note=@note WHERE id=@id AND status='Open'", new { id, by, note });
    }

    public async Task<int> CloseAsync(string id, string by, string disposition, string? note)
    {
        using var c = Conn();
        return await c.ExecuteAsync("UPDATE alert SET status='Closed', closed_by=@by, closed_at=UTC_TIMESTAMP(), disposition=@disposition, close_note=@note WHERE id=@id AND status<>'Closed'",
            new { id, by, disposition, note });
    }
}

// =====================================================================
public class Cls_PfmaDAL : Cls_DbBase
{
    public async Task<List<Pfma>> ListByDamAsync(string damId)
    {
        using var c = Conn();
        var list = (await c.QueryAsync<Pfma>("SELECT * FROM pfma WHERE dam_id=@damId ORDER BY created_at", new { damId })).ToList();
        var links = await c.QueryAsync<(string PfmaId, string SensorId)>("SELECT ps.pfma_id, ps.sensor_id FROM pfma_sensor ps JOIN pfma p ON p.id=ps.pfma_id WHERE p.dam_id=@damId", new { damId });
        foreach (var p in list) p.SensorIds = links.Where(l => l.PfmaId == p.Id).Select(l => l.SensorId).ToList();
        return list;
    }

    public async Task<Pfma?> GetAsync(string id)
    {
        using var c = Conn();
        var p = await c.QueryFirstOrDefaultAsync<Pfma>("SELECT * FROM pfma WHERE id=@id", new { id });
        if (p != null) p.SensorIds = (await c.QueryAsync<string>("SELECT sensor_id FROM pfma_sensor WHERE pfma_id=@id", new { id })).ToList();
        return p;
    }

    public async Task InsertAsync(Pfma p)
    {
        using var c = Conn();
        await c.ExecuteAsync(@"INSERT INTO pfma (id, dam_id, failure_mode_code, location, description, likelihood, consequence, status, created_by, created_at)
            VALUES (@Id, @DamId, @FailureModeCode, @Location, @Description, @Likelihood, @Consequence, @Status, @CreatedBy, UTC_TIMESTAMP())", p);
    }

    public async Task UpdateAsync(Pfma p)
    {
        using var c = Conn();
        await c.ExecuteAsync(@"UPDATE pfma SET failure_mode_code=@FailureModeCode, location=@Location, description=@Description, likelihood=@Likelihood,
            consequence=@Consequence, status=@Status, updated_at=UTC_TIMESTAMP() WHERE id=@Id", p);
    }

    public async Task SetSensorsAsync(string pfmaId, IEnumerable<string> sensorIds)
    {
        using var c = Conn();
        await c.OpenAsync();
        using var tx = await c.BeginTransactionAsync();
        await c.ExecuteAsync("DELETE FROM pfma_sensor WHERE pfma_id=@pfmaId", new { pfmaId }, tx);
        await c.ExecuteAsync("INSERT INTO pfma_sensor (pfma_id, sensor_id) VALUES (@pfmaId, @sensorId)", sensorIds.Distinct().Select(s => new { pfmaId, sensorId = s }), tx);
        await tx.CommitAsync();
    }
}

// =====================================================================
public class Cls_GatewayDAL : Cls_DbBase
{
    public async Task<(GatewayView? gw, string? keyHash)> GetByCodeAsync(string code)
    {
        using var c = Conn();
        var row = await c.QueryFirstOrDefaultAsync("SELECT id, code, dam_id, key_hash, is_active, last_seen_at, created_at FROM gateway WHERE code=@code", new { code });
        if (row == null) return (null, null);
        var gw = new GatewayView { Id = row.id, Code = row.code, DamId = row.dam_id, IsActive = Convert.ToBoolean(row.is_active), LastSeenAt = row.last_seen_at, CreatedAt = row.created_at };
        return (gw, (string)row.key_hash);
    }

    public async Task<List<GatewayView>> ListAsync()
    {
        using var c = Conn();
        return (await c.QueryAsync<GatewayView>("SELECT id, code, dam_id AS DamId, is_active AS IsActive, last_seen_at AS LastSeenAt, created_at AS CreatedAt FROM gateway ORDER BY code")).ToList();
    }

    public async Task InsertAsync(string id, string code, string damId, string keyHash)
    {
        using var c = Conn();
        await c.ExecuteAsync("INSERT INTO gateway (id, code, dam_id, key_hash, is_active, created_at) VALUES (@id, @code, @damId, @keyHash, 1, UTC_TIMESTAMP())", new { id, code, damId, keyHash });
    }

    public async Task TouchAsync(string id)
    {
        using var c = Conn();
        await c.ExecuteAsync("UPDATE gateway SET last_seen_at=UTC_TIMESTAMP() WHERE id=@id", new { id });
    }
}
