namespace DamMaster.Api.Models;

// ---------- Reference catalogues ----------
public class RefDamType { public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string MaterialGroup { get; set; } = ""; public string? Description { get; set; } public string? Weaknesses { get; set; } public string? MonitoringPriority { get; set; } }
public class RefDamFunction { public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string? Description { get; set; } public string? MonitoringNote { get; set; } }
public class RefHazardClass { public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string? Description { get; set; } public decimal RiskFactor { get; set; } }
public class RefFailureMode { public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string? Description { get; set; } public string? AppliesTo { get; set; } public string? EarlySigns { get; set; } }
public class RefParameterGroup { public string Code { get; set; } = ""; public string Name { get; set; } = ""; }
public class RefParameter { public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string GroupCode { get; set; } = ""; public string Unit { get; set; } = ""; public bool IsLoad { get; set; } public bool IsEvent { get; set; } public string? Purpose { get; set; } }
public class RefInstrumentType { public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string? Measures { get; set; } public string? Principle { get; set; } public string? RawOutput { get; set; } public string? EngUnit { get; set; } public string? TypicalRange { get; set; } public string? UsedIn { get; set; } }
public class RefFailureModeParameter { public string FailureModeCode { get; set; } = ""; public string ParameterCode { get; set; } = ""; public string Role { get; set; } = ""; }
public class RefStandard { public string Code { get; set; } = ""; public string Title { get; set; } = ""; public string? Subject { get; set; } public string Category { get; set; } = ""; public string? Edition { get; set; } public string? Clauses { get; set; } public string? VerifiedBy { get; set; } public DateTime? VerifiedAt { get; set; } }
public class RefAlertLevel { public int Level { get; set; } public string Name { get; set; } = ""; public string Colour { get; set; } = ""; public string? Meaning { get; set; } public string? NotifyRoles { get; set; } public string? Channels { get; set; } public int AckMinutes { get; set; } }
public class RefInterpretation { public int Id { get; set; } public string GroupCode { get; set; } = ""; public string Observation { get; set; } = ""; public string? NormalPattern { get; set; } public string? WarningSign { get; set; } public string? CriticalSign { get; set; } public string? PossibleCause { get; set; } public string? ActionText { get; set; } }

// ---------- Users ----------
public class AppUser
{
    public string Id { get; set; } = ""; public string Username { get; set; } = ""; public string FullName { get; set; } = ""; public string Role { get; set; } = "";
    public string PasswordHash { get; set; } = ""; public bool IsActive { get; set; } public bool MustChangePassword { get; set; }
    public int FailedLogins { get; set; } public DateTime? LockedUntil { get; set; } public DateTime? LastLoginAt { get; set; } public DateTime CreatedAt { get; set; }
    public string? Email { get; set; } public string? Phone { get; set; }
}
public record UserView(string Id, string Username, string FullName, string Role, bool IsActive, bool MustChangePassword, DateTime? LastLoginAt, string? Email = null, string? Phone = null);
public record LoginRequest(string Username, string Password);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record CreateUserRequest(string Username, string FullName, string Role, string Password, string? Email = null, string? Phone = null);
public record UpdateUserRequest(string FullName, string Role, bool IsActive, string? Email = null, string? Phone = null);

// ---------- Dams ----------
public class Dam
{
    public string Id { get; set; } = ""; public string Code { get; set; } = ""; public string Name { get; set; } = "";
    public string? State { get; set; } public string? District { get; set; } public string? River { get; set; }
    public decimal? Latitude { get; set; } public decimal? Longitude { get; set; }
    public string DamTypeCode { get; set; } = ""; public string? FunctionCode { get; set; } public string HazardCode { get; set; } = "HIGH";
    public decimal? HeightM { get; set; } public decimal? CrestLengthM { get; set; } public decimal? FrlM { get; set; } public decimal? MwlM { get; set; } public decimal? MddlM { get; set; }
    public decimal? CrestLevelM { get; set; } public decimal? GrossStorageMcm { get; set; } public string? SeismicZone { get; set; } public bool IsSpecified { get; set; } = true;
    public string LifePhase { get; set; } = "Long-term operation"; public string? EapStatus { get; set; } public string? OwnerName { get; set; }
    public bool IsActive { get; set; } = true; public DateTime CreatedAt { get; set; } public DateTime? UpdatedAt { get; set; }
}

// ---------- PFMA ----------
public class Pfma
{
    public string Id { get; set; } = ""; public string DamId { get; set; } = ""; public string FailureModeCode { get; set; } = "";
    public string? Location { get; set; } public string? Description { get; set; } public string? Likelihood { get; set; } public string? Consequence { get; set; }
    public string Status { get; set; } = "Active"; public string? CreatedBy { get; set; } public DateTime CreatedAt { get; set; }
    public List<string> SensorIds { get; set; } = new();
}

// ---------- Sensors & thresholds ----------
public class Sensor
{
    public string Id { get; set; } = ""; public string DamId { get; set; } = ""; public string Code { get; set; } = ""; public string ParameterCode { get; set; } = "";
    public string? InstrumentTypeCode { get; set; } public string? Location { get; set; } public decimal? ElevationM { get; set; } public string EngUnit { get; set; } = "";
    public double? ExpectedMin { get; set; } public double? ExpectedMax { get; set; } public string Direction { get; set; } = "Rising";
    public int ReadingIntervalMin { get; set; } = 360; public DateTime? CalibrationDue { get; set; } public string Status { get; set; } = "Active";
    public DateTime CreatedAt { get; set; }
    public decimal? Latitude { get; set; } public decimal? Longitude { get; set; } public double? OffsetM { get; set; } public double? ChainageM { get; set; }
}
public class SensorThreshold
{
    public string Id { get; set; } = ""; public string SensorId { get; set; } = "";
    public double? AlertValue { get; set; } public double? AlarmValue { get; set; } public double? EmergencyValue { get; set; }
    public double? RateLimitPerDay { get; set; } public decimal ZLimit { get; set; } = 2.5m; public int Persistence { get; set; } = 2;
    public string Method { get; set; } = "Design"; public string? Basis { get; set; } public string? CreatedBy { get; set; } public DateTime CreatedAt { get; set; }
    public string? ApprovedBy { get; set; } public DateTime? ApprovedAt { get; set; } public bool IsCurrent { get; set; }
}
public class SensorStatusView
{
    public string Id { get; set; } = ""; public string Code { get; set; } = ""; public string ParameterCode { get; set; } = ""; public string ParameterName { get; set; } = "";
    public string? Location { get; set; } public string EngUnit { get; set; } = ""; public string Status { get; set; } = "";
    public double? LatestValue { get; set; } public DateTime? LatestAt { get; set; } public string? LatestQuality { get; set; }
    public int ReadingCount { get; set; } public int OpenAlerts { get; set; } public int MaxOpenLevel { get; set; }
    public double? AlertValue { get; set; } public double? AlarmValue { get; set; } public double? EmergencyValue { get; set; } public bool ThresholdApproved { get; set; }
}

// ---------- Readings ----------
public class Reading { public long Id { get; set; } public string SensorId { get; set; } = ""; public DateTime ObservedAt { get; set; } public double Value { get; set; } public double? RawValue { get; set; } public string Quality { get; set; } = "G"; public string? QcRule { get; set; } }
public record ReadingPoint(DateTime T, double V, string Q, string? Rule);

// ---------- Import ----------
public class ImportDamRow { public string? DamId { get; set; } public string? DamName { get; set; } public string? State { get; set; } public string? DamType { get; set; } public string? HazardClass { get; set; } public string? SeismicZone { get; set; } public double? HeightM { get; set; } public double? FrlM { get; set; } public double? MwlM { get; set; } public double? MddlM { get; set; } public double? CrestLevelM { get; set; } }
public class ImportSensorRow { public string? SensorId { get; set; } public string? DamId { get; set; } public string? ParameterCode { get; set; } public string? InstrumentType { get; set; } public string? Location { get; set; } public double? ElevationM { get; set; } public string? EngUnit { get; set; } public double? ExpectedMin { get; set; } public double? ExpectedMax { get; set; } public double? AlertLevel { get; set; } public double? AlarmLevel { get; set; } public double? EmergencyLevel { get; set; } public string? Direction { get; set; } public int? ReadingIntervalMin { get; set; } public string? CalibrationDue { get; set; } public string? Status { get; set; } public double? Latitude { get; set; } public double? Longitude { get; set; } public double? OffsetM { get; set; } public double? ChainageM { get; set; } }
public class ImportReadingRow { public string? DamId { get; set; } public string? SensorId { get; set; } public string? ParameterCode { get; set; } public string? ObservedAt { get; set; } public double? Value { get; set; } public string? Unit { get; set; } public double? RawValue { get; set; } public string? LoggerStatus { get; set; } }
public class ImportRequest
{
    public string? FileName { get; set; } public bool Commit { get; set; }
    public List<ImportDamRow> Dams { get; set; } = new(); public List<ImportSensorRow> Sensors { get; set; } = new(); public List<ImportReadingRow> Readings { get; set; } = new();
}
public class ImportIssue { public string Sheet { get; set; } = ""; public int RowNo { get; set; } public string? ItemCode { get; set; } public string Outcome { get; set; } = ""; public string RuleCode { get; set; } = ""; public string Message { get; set; } = ""; }
public class ImportResult
{
    public string? ImportId { get; set; } public bool Committed { get; set; }
    public int RowsRead { get; set; } public int Good { get; set; } public int Suspect { get; set; } public int Bad { get; set; } public int Rejected { get; set; } public int Quarantined { get; set; } public int Duplicates { get; set; }
    public int DamsUpserted { get; set; } public int SensorsUpserted { get; set; } public List<ImportIssue> Issues { get; set; } = new(); public List<AlertView> AlertsRaised { get; set; } = new();
}

// ---------- Telemetry (IoT gateway, FRS-001 §5.2) ----------
public class TelemetryReading { public string SensorId { get; set; } = ""; public string ParameterCode { get; set; } = ""; public string ObservedAt { get; set; } = ""; public double? Value { get; set; } public string? Unit { get; set; } public double? RawValue { get; set; } public string? LoggerStatus { get; set; } }
public class TelemetryMessage { public string SchemaVersion { get; set; } = "1.0"; public string MessageId { get; set; } = ""; public string GatewayId { get; set; } = ""; public string DamId { get; set; } = ""; public string? SentAt { get; set; } public List<TelemetryReading> Readings { get; set; } = new(); }

// ---------- Alerts ----------
public class AlertView
{
    public string? SurveyId { get; set; }
    public string Id { get; set; } = ""; public string DamId { get; set; } = ""; public string? DamName { get; set; } public string? SensorId { get; set; } public string? SensorCode { get; set; } public string? PfmaId { get; set; }
    public int Level { get; set; } public string RuleType { get; set; } = ""; public string Title { get; set; } = ""; public string? Detail { get; set; }
    public double? Value { get; set; } public double? LimitValue { get; set; } public DateTime FirstAt { get; set; } public DateTime LastAt { get; set; } public int Occurrences { get; set; }
    public string Status { get; set; } = ""; public string? AckBy { get; set; } public DateTime? AckAt { get; set; } public string? AckNote { get; set; }
    public string? ClosedBy { get; set; } public DateTime? ClosedAt { get; set; } public string? Disposition { get; set; } public string? CloseNote { get; set; }
    public string? ParameterCode { get; set; }
    public DateTime? AckDueAt { get; set; } public int EscalationStep { get; set; } public string? InspectionId { get; set; }
    public string? GatewayId { get; set; }
}
public record AckRequest(string? Note);
public record CloseRequest(string Disposition, string? Note);

// ---------- Misc ----------
public record VerifyStandardRequest(string Edition, string? Clauses);
public record AuditEntry(long Id, DateTime At, string? Username, string Module, string Action, string? Entity, string? EntityId);
public class DamSummary
{
    public string Id { get; set; } = ""; public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string? State { get; set; }
    public string DamTypeCode { get; set; } = ""; public string HazardCode { get; set; } = ""; public string LifePhase { get; set; } = "";
    public int Sensors { get; set; } public long Readings { get; set; } public DateTime? DataAsOf { get; set; }
    public int OpenAlerts { get; set; } public int MaxOpenLevel { get; set; } public int L1 { get; set; } public int L2 { get; set; } public int L3 { get; set; }
    public double? ReservoirLevel { get; set; } public decimal? FrlM { get; set; } public int ActivePfma { get; set; }
    public double? Dhi { get; set; } public string? DhiBand { get; set; } public int? Risk { get; set; } public string? Rating { get; set; }
}
public class GatewayView { public string Id { get; set; } = ""; public string Code { get; set; } = ""; public string DamId { get; set; } = ""; public bool IsActive { get; set; } public DateTime? LastSeenAt { get; set; } public DateTime CreatedAt { get; set; } }
public record CreateGatewayRequest(string Code, string DamId, int? ExpectedIntervalMin = null);
