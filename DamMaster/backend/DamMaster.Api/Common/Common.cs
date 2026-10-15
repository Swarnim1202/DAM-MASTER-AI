using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DamMaster.Api.Common;

/// <summary>Static application configuration (team convention: DAL classes are instantiated directly, no DI).</summary>
public static class AppConfig
{
    public static string ConnectionString { get; private set; } = "";
    public static double DefaultZLimit { get; private set; } = 2.5;
    public static int WindowDays { get; private set; } = 14;
    public static int TrendDays { get; private set; } = 21;
    // Phase 2 — notifications and inspections
    public static string AppBaseUrl { get; private set; } = "";
    public static string? SmtpHost { get; private set; }
    public static int SmtpPort { get; private set; } = 587;
    public static string? SmtpUser { get; private set; }
    public static string? SmtpPassword { get; private set; }
    public static string SmtpFrom { get; private set; } = "dammaster@localhost";
    public static bool SmtpSsl { get; private set; } = true;
    public static string? SmsUrlTemplate { get; private set; }
    public static int EscalationIntervalSeconds { get; private set; } = 60;
    public static int MaxEscalationSteps { get; private set; } = 4;
    public static string PreMonsoonDue { get; private set; } = "05-31";
    public static string PostMonsoonDue { get; private set; } = "11-30";
    // Phase 4 — live IoT
    public static bool MqttEnabled { get; private set; }
    public static string MqttHost { get; private set; } = "localhost";
    public static int MqttPort { get; private set; } = 1883;
    public static bool MqttTls { get; private set; }
    public static bool MqttAllowUntrustedCert { get; private set; }
    public static string? MqttUser { get; private set; }
    public static string? MqttPassword { get; private set; }
    public static string MqttClientId { get; private set; } = "dammaster-api";
    public static string MqttTopicRoot { get; private set; } = "dammaster";
    public static int HstRefitDays { get; private set; } = 30;
    public static double GatewayLowBatteryV { get; private set; } = 11.5;
    // Phase 6 — files, AI, map
    public static string StorageRoot { get; private set; } = "App_Data/files";
    public static long MaxUploadBytes { get; private set; } = 50_000_000;
    public static string? AiEndpoint { get; private set; }
    public static string AiModel { get; private set; } = "local-model";
    public static string? AiApiKey { get; private set; }
    public static string MapTileUrl { get; private set; } = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
    public static string MapAttribution { get; private set; } = "© OpenStreetMap contributors";
    public static double StructureRadiusM { get; private set; } = 1000;
    // Phase 7 — OCR, SSO, federation
    public static bool OcrEnabled { get; private set; }
    public static string OcrTesseract { get; private set; } = "tesseract";
    public static string OcrPdftoppm { get; private set; } = "pdftoppm";
    public static string OcrLanguage { get; private set; } = "eng";
    public static int OcrDpi { get; private set; } = 200;
    public static bool SsoEnabled { get; private set; }
    public static string SsoAuthority { get; private set; } = "";
    public static string SsoClientId { get; private set; } = "";
    public static string? SsoClientSecret { get; private set; }
    public static string SsoScopes { get; private set; } = "openid profile email";
    public static string SsoUserClaim { get; private set; } = "email";
    public static string SsoButtonLabel { get; private set; } = "Sign in with organisation account";
    public static bool AllowLocalLogin { get; private set; } = true;
    public static string FederationInstanceName { get; private set; } = "DAM MASTER";
    public static string? FederationInstanceKey { get; private set; }
    public static bool FederationIncludeSelf { get; private set; } = true;
    public static List<(string Name, string Url, string Key)> FederationInstances { get; private set; } = new();

    public static void Init(IConfiguration cfg)
    {
        ConnectionString = cfg.GetConnectionString("DamMaster") ?? throw new InvalidOperationException("ConnectionStrings:DamMaster missing");
        DefaultZLimit = cfg.GetValue("Engine:DefaultZLimit", 2.5);
        WindowDays = cfg.GetValue("Engine:WindowDays", 14);
        TrendDays = cfg.GetValue("Engine:TrendDays", 21);
        AppBaseUrl = cfg["Notifications:AppBaseUrl"] ?? "";
        SmtpHost = cfg["Notifications:Smtp:Host"]; SmtpPort = cfg.GetValue("Notifications:Smtp:Port", 587);
        SmtpUser = cfg["Notifications:Smtp:User"]; SmtpPassword = cfg["Notifications:Smtp:Password"];
        SmtpFrom = cfg["Notifications:Smtp:From"] ?? "dammaster@localhost"; SmtpSsl = cfg.GetValue("Notifications:Smtp:EnableSsl", true);
        SmsUrlTemplate = cfg["Notifications:Sms:UrlTemplate"];
        EscalationIntervalSeconds = cfg.GetValue("Notifications:EscalationIntervalSeconds", 60);
        MaxEscalationSteps = cfg.GetValue("Notifications:MaxEscalationSteps", 4);
        PreMonsoonDue = cfg["Inspection:PreMonsoonDue"] ?? "05-31"; PostMonsoonDue = cfg["Inspection:PostMonsoonDue"] ?? "11-30";
        MqttEnabled = cfg.GetValue("Mqtt:Enabled", false); MqttHost = cfg["Mqtt:Host"] ?? "localhost"; MqttPort = cfg.GetValue("Mqtt:Port", 1883);
        MqttTls = cfg.GetValue("Mqtt:UseTls", false); MqttAllowUntrustedCert = cfg.GetValue("Mqtt:AllowUntrustedCertificate", false);
        MqttUser = cfg["Mqtt:Username"]; MqttPassword = cfg["Mqtt:Password"]; MqttClientId = cfg["Mqtt:ClientId"] ?? "dammaster-api"; MqttTopicRoot = cfg["Mqtt:TopicRoot"] ?? "dammaster";
        HstRefitDays = cfg.GetValue("Hst:RefitDays", 30); GatewayLowBatteryV = cfg.GetValue("Gateway:LowBatteryVolts", 11.5);
        StorageRoot = cfg["Storage:Root"] ?? "App_Data/files"; MaxUploadBytes = cfg.GetValue("Storage:MaxUploadMb", 50L) * 1_000_000;
        AiEndpoint = cfg["Ai:Endpoint"]; AiModel = cfg["Ai:Model"] ?? "local-model"; AiApiKey = cfg["Ai:ApiKey"];
        OcrEnabled = cfg.GetValue("Ocr:Enabled", false); OcrTesseract = cfg["Ocr:TesseractPath"] ?? "tesseract"; OcrPdftoppm = cfg["Ocr:PdftoppmPath"] ?? "pdftoppm"; OcrLanguage = cfg["Ocr:Language"] ?? "eng"; OcrDpi = cfg.GetValue("Ocr:Dpi", 200);
        SsoEnabled = cfg.GetValue("Sso:Enabled", false); SsoAuthority = (cfg["Sso:Authority"] ?? "").TrimEnd('/'); SsoClientId = cfg["Sso:ClientId"] ?? ""; SsoClientSecret = cfg["Sso:ClientSecret"];
        SsoScopes = cfg["Sso:Scopes"] ?? "openid profile email"; SsoUserClaim = cfg["Sso:UserClaim"] ?? "email"; SsoButtonLabel = cfg["Sso:ButtonLabel"] ?? SsoButtonLabel; AllowLocalLogin = cfg.GetValue("Sso:AllowLocalLogin", true);
        FederationInstanceName = cfg["Federation:InstanceName"] ?? FederationInstanceName; FederationInstanceKey = cfg["Federation:InstanceKey"]; FederationIncludeSelf = cfg.GetValue("Federation:IncludeSelf", true);
        FederationInstances = cfg.GetSection("Federation:Instances").GetChildren().Select(x => (x["Name"] ?? "", x["Url"] ?? "", x["Key"] ?? "")).Where(x => x.Item2 != "").ToList();
        MapTileUrl = cfg["Map:TileUrl"] ?? MapTileUrl; MapAttribution = cfg["Map:Attribution"] ?? MapAttribution; StructureRadiusM = cfg.GetValue("Map:StructureRadiusM", 1000.0);
    }
}

/// <summary>Role names. Keep in sync with frontend src/auth/roles.ts.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string DamSafetyEngineer = "DamSafetyEngineer";
    public const string InstrumentationEngineer = "InstrumentationEngineer";
    public const string Inspector = "Inspector";
    public const string Approver = "Approver";
    public const string Management = "Management";
    public const string DesignEngineer = "DesignEngineer";
    public const string Checker = "Checker";
    public static readonly string[] All = { Admin, DamSafetyEngineer, InstrumentationEngineer, Inspector, Approver, Management, DesignEngineer, Checker };
}

/// <summary>Authorization policies mapped to roles — the single place that decides who may do what.</summary>
public static class Policies
{
    public const string ManageDams = "ManageDams";
    public const string ManageSensors = "ManageSensors";
    public const string ImportData = "ImportData";
    public const string ApproveThresholds = "ApproveThresholds";
    public const string AcknowledgeAlerts = "AcknowledgeAlerts";
    public const string CloseAlerts = "CloseAlerts";
    public const string ManagePfma = "ManagePfma";
    public const string VerifyStandards = "VerifyStandards";
    public const string ManageUsers = "ManageUsers";
    public const string ManageGateways = "ManageGateways";
    public const string ManageInspections = "ManageInspections";
    public const string CloseFindings = "CloseFindings";
    public const string ManageEap = "ManageEap";
    public const string ViewNotificationLog = "ViewNotificationLog";
    public const string FitModels = "FitModels";
    public const string ManageDocuments = "ManageDocuments";
    public const string EditDesign = "EditDesign";
    public const string CheckDesign = "CheckDesign";
    public const string ApproveDesign = "ApproveDesign";
    public const string ManageSurveys = "ManageSurveys";
    public const string ApplyDesignThresholds = "ApplyDesignThresholds";

    public static readonly Dictionary<string, string[]> Map = new()
    {
        [ManageDams] = new[] { Roles.Admin, Roles.DamSafetyEngineer },
        [ManageSensors] = new[] { Roles.Admin, Roles.InstrumentationEngineer, Roles.DamSafetyEngineer },
        [ImportData] = new[] { Roles.Admin, Roles.InstrumentationEngineer },
        [ApproveThresholds] = new[] { Roles.DamSafetyEngineer, Roles.Approver },
        [AcknowledgeAlerts] = new[] { Roles.DamSafetyEngineer, Roles.InstrumentationEngineer },
        [CloseAlerts] = new[] { Roles.DamSafetyEngineer },
        [ManagePfma] = new[] { Roles.DamSafetyEngineer },
        [VerifyStandards] = new[] { Roles.DamSafetyEngineer, Roles.Approver },
        [ManageUsers] = new[] { Roles.Admin },
        [ManageGateways] = new[] { Roles.Admin, Roles.InstrumentationEngineer },
        [ManageInspections] = new[] { Roles.Inspector, Roles.DamSafetyEngineer },
        [CloseFindings] = new[] { Roles.DamSafetyEngineer },
        [ManageEap] = new[] { Roles.DamSafetyEngineer, Roles.Admin },
        [ViewNotificationLog] = new[] { Roles.Admin, Roles.DamSafetyEngineer },
        [FitModels] = new[] { Roles.DamSafetyEngineer, Roles.InstrumentationEngineer },
        [ManageDocuments] = new[] { Roles.DamSafetyEngineer, Roles.Admin },
        [EditDesign] = new[] { Roles.DesignEngineer, Roles.Admin },
        [CheckDesign] = new[] { Roles.Checker, Roles.DamSafetyEngineer },
        [ApproveDesign] = new[] { Roles.Approver },
        [ManageSurveys] = new[] { Roles.DamSafetyEngineer, Roles.InstrumentationEngineer, Roles.Admin },
        [ApplyDesignThresholds] = new[] { Roles.DamSafetyEngineer },
    };
}

/// <summary>PBKDF2-SHA256 password hashing (in-box cryptography, no external package).</summary>
public static class PasswordHasher
{
    private const int Iterations = 100_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var p = stored.Split('$');
        if (p.Length != 4 || p[0] != "pbkdf2") return false;
        var salt = Convert.FromBase64String(p[2]);
        var expected = Convert.FromBase64String(p[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, int.Parse(p[1]), HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static string NewApiKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(30)).Replace('+', 'x').Replace('/', 'y').TrimEnd('=');

    public static bool IsStrong(string pw) => pw.Length >= 8 && pw.Any(char.IsDigit) && pw.Any(char.IsLetter);
}

public static class Ids
{
    public static string New() => Guid.NewGuid().ToString();
}

/// <summary>Current-user helpers on ClaimsPrincipal.</summary>
public static class UserExt
{
    public static string UserId(this ClaimsPrincipal u) => u.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    public static string UserName(this ClaimsPrincipal u) => u.FindFirstValue(ClaimTypes.Name) ?? "";
    public static string FullName(this ClaimsPrincipal u) => u.FindFirstValue(ClaimTypes.GivenName) ?? u.UserName();
    public static string Role(this ClaimsPrincipal u) => u.FindFirstValue(ClaimTypes.Role) ?? "";
}

/// <summary>All DateTimes are stored as UTC; they are serialised with a trailing Z and parsed from any ISO offset.</summary>
public class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => r.GetDateTimeOffset().UtcDateTime;

    public override void Write(Utf8JsonWriter w, DateTime v, JsonSerializerOptions o)
    {
        var utc = v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc);
        w.WriteStringValue(utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));
    }
}

/// <summary>Expected business-rule failure; returned to the client as HTTP 400 with its message.</summary>
public class BusinessException : Exception
{
    public BusinessException(string message) : base(message) { }
}

/// <summary>Turns BusinessException into HTTP 400 inside MVC, so expected rule violations are not logged as server errors.</summary>
public class BusinessExceptionFilter : Microsoft.AspNetCore.Mvc.Filters.IExceptionFilter
{
    public void OnException(Microsoft.AspNetCore.Mvc.Filters.ExceptionContext ctx)
    {
        if (ctx.Exception is not BusinessException ex) return;
        ctx.Result = new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new { error = ex.Message });
        ctx.ExceptionHandled = true;
    }
}
