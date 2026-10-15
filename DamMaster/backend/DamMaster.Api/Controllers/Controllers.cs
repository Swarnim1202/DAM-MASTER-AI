using System.Security.Claims;
using DamMaster.Api.Common;
using DamMaster.Api.DAL;
using DamMaster.Api.Engine;
using DamMaster.Api.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DamMaster.Api.Controllers;

/// <summary>Base controller: audit helper bound to the signed-in user.</summary>
[ApiController]
public abstract class AppController : ControllerBase
{
    protected Task Audit(string module, string action, string? entity = null, string? entityId = null, string? detail = null) =>
        new Cls_AuditDAL().WriteAsync(User.UserId(), User.UserName(), module, action, entity, entityId, detail);
}

// =====================================================================
[Route("api/auth")]
public class AuthController : AppController
{
    [AllowAnonymous, HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var dal = new Cls_UserDAL();
        var u = await dal.GetByUsernameAsync((req.Username ?? "").Trim().ToLowerInvariant());
        if (u == null) return Unauthorized(new { error = "Incorrect username or password." });
        if (u.LockedUntil.HasValue && u.LockedUntil > DateTime.UtcNow) return Unauthorized(new { error = $"Account locked after repeated failures. Try again after {u.LockedUntil:HH:mm} UTC." });
        if (!PasswordHasher.Verify(req.Password ?? "", u.PasswordHash))
        {
            await dal.RecordLoginAsync(u.Id, false);
            await new Cls_AuditDAL().WriteAsync(u.Id, u.Username, "Session", "Failed sign-in");
            return Unauthorized(new { error = "Incorrect username or password." });
        }
        if (!u.IsActive) return Unauthorized(new { error = "This account is disabled." });
        if (!AppConfig.AllowLocalLogin && u.Role != Roles.Admin) return Unauthorized(new { error = "Password sign-in is disabled — use the organisation account (SSO)." });
        await dal.RecordLoginAsync(u.Id, true);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, u.Id), new(ClaimTypes.Name, u.Username), new(ClaimTypes.GivenName, u.FullName), new(ClaimTypes.Role, u.Role) };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
        await new Cls_AuditDAL().WriteAsync(u.Id, u.Username, "Session", "Signed in");
        return Ok(new UserView(u.Id, u.Username, u.FullName, u.Role, u.IsActive, u.MustChangePassword, DateTime.UtcNow));
    }

    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await Audit("Session", "Signed out");
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    [Authorize, HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var u = await new Cls_UserDAL().GetAsync(User.UserId());
        if (u == null || !u.IsActive) { await HttpContext.SignOutAsync(); return Unauthorized(); }
        return Ok(new UserView(u.Id, u.Username, u.FullName, u.Role, u.IsActive, u.MustChangePassword, u.LastLoginAt));
    }

    [Authorize, HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req)
    {
        var dal = new Cls_UserDAL(); var u = await dal.GetAsync(User.UserId());
        if (u == null || !PasswordHasher.Verify(req.CurrentPassword, u.PasswordHash)) throw new BusinessException("Current password is incorrect.");
        if (!PasswordHasher.IsStrong(req.NewPassword)) throw new BusinessException("New password must be at least 8 characters with letters and digits.");
        await dal.SetPasswordAsync(u.Id, PasswordHasher.Hash(req.NewPassword), false);
        await Audit("Users", "Changed own password", "app_user", u.Id);
        return NoContent();
    }
}

// =====================================================================
[Authorize, Route("api/reference")]
public class ReferenceController : AppController
{
    [HttpGet("{name}")]
    public async Task<IActionResult> Get(string name) => Ok(await new Cls_ReferenceDAL().GetCatalogAsync(name));

    [Authorize(Policy = Policies.VerifyStandards), HttpPost("standards/{code}/verify")]
    public async Task<IActionResult> Verify(string code, VerifyStandardRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Edition)) throw new BusinessException("Enter the edition / year in force before verifying.");
        if (await new Cls_ReferenceDAL().VerifyStandardAsync(code, req.Edition.Trim(), req.Clauses?.Trim(), User.FullName()) == 0) return NotFound();
        await Audit("Code validation", $"{code} verified — edition {req.Edition}{(string.IsNullOrWhiteSpace(req.Clauses) ? "" : ", clauses " + req.Clauses)}", "ref_standard", code);
        return NoContent();
    }

    [Authorize(Policy = Policies.VerifyStandards), HttpDelete("standards/{code}/verify")]
    public async Task<IActionResult> Revoke(string code)
    {
        await new Cls_ReferenceDAL().RevokeStandardAsync(code);
        await Audit("Code validation", $"{code} verification revoked", "ref_standard", code);
        return NoContent();
    }
}

// =====================================================================
[Authorize, Route("api/dams")]
public class DamsController : AppController
{
    [HttpGet] public async Task<IActionResult> List() => Ok(await new Cls_DamDAL().ListAsync());

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id) => (await new Cls_DamDAL().GetAsync(id)) is { } d ? Ok(d) : NotFound();

    [Authorize(Policy = Policies.ManageDams), HttpPost]
    public async Task<IActionResult> Create(Dam d)
    {
        Validate(d);
        var dal = new Cls_DamDAL();
        if (await dal.GetByCodeAsync(d.Code.Trim()) != null) throw new BusinessException($"Dam code {d.Code} already exists.");
        d.Id = Ids.New(); d.Code = d.Code.Trim();
        await dal.InsertAsync(d);
        await Audit("Dams", $"Registered dam {d.Code} {d.Name}", "dam", d.Id);
        return Ok(d);
    }

    [Authorize(Policy = Policies.ManageDams), HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, Dam d)
    {
        Validate(d);
        var dal = new Cls_DamDAL();
        if (await dal.GetAsync(id) == null) return NotFound();
        d.Id = id;
        await dal.UpdateAsync(d);
        await Audit("Dams", $"Updated dam {d.Code}", "dam", id);
        return Ok(d);
    }

    [HttpGet("{id}/sensors")]
    public async Task<IActionResult> Sensors(string id) => Ok(await new Cls_SensorDAL().StatusListAsync(id));

    [HttpGet("{id}/pfma")]
    public async Task<IActionResult> Pfma(string id) => Ok(await new Cls_PfmaDAL().ListByDamAsync(id));

    [Authorize(Roles = Roles.Admin + "," + Roles.DamSafetyEngineer + "," + Roles.InstrumentationEngineer), HttpPost("{id}/evaluate")]
    public async Task<IActionResult> Evaluate(string id) => Ok(await new Cls_AlertEngine().EvaluateDamAsync(id, User.UserName(), User.UserId()));

    private static void Validate(Dam d)
    {
        if (string.IsNullOrWhiteSpace(d.Code) || string.IsNullOrWhiteSpace(d.Name)) throw new BusinessException("Dam code and name are required.");
        if (string.IsNullOrWhiteSpace(d.DamTypeCode)) throw new BusinessException("Dam type is required.");
        if (d.MwlM.HasValue && d.FrlM.HasValue && d.MwlM < d.FrlM) throw new BusinessException("MWL must be at or above FRL.");
        if (d.FrlM.HasValue && d.MddlM.HasValue && d.MddlM >= d.FrlM) throw new BusinessException("MDDL must be below FRL.");
    }
}

// =====================================================================
[Authorize, Route("api/sensors")]
public class SensorsController : AppController
{
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id) => (await new Cls_SensorDAL().GetAsync(id)) is { } s ? Ok(s) : NotFound();

    [Authorize(Policy = Policies.ManageSensors), HttpPost]
    public async Task<IActionResult> Create(Sensor s)
    {
        if (string.IsNullOrWhiteSpace(s.Code) || string.IsNullOrWhiteSpace(s.DamId) || string.IsNullOrWhiteSpace(s.ParameterCode) || string.IsNullOrWhiteSpace(s.EngUnit))
            throw new BusinessException("Code, dam, parameter and unit are required.");
        var dal = new Cls_SensorDAL();
        if ((await dal.ByCodesAsync(new[] { s.Code.Trim() })).Count > 0) throw new BusinessException($"Sensor code {s.Code} already exists.");
        s.Id = Ids.New(); s.Code = s.Code.Trim();
        await dal.InsertAsync(s);
        await Audit("Sensors", $"Registered sensor {s.Code}", "sensor", s.Id);
        return Ok(s);
    }

    [Authorize(Policy = Policies.ManageSensors), HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, Sensor s)
    {
        var dal = new Cls_SensorDAL(); var cur = await dal.GetAsync(id);
        if (cur == null) return NotFound();
        s.Id = id; s.DamId = cur.DamId; s.Code = cur.Code;
        await dal.UpdateAsync(s);
        await Audit("Sensors", $"Updated sensor {cur.Code}", "sensor", id);
        return Ok(s);
    }

    [HttpGet("{id}/readings")]
    public async Task<IActionResult> Readings(string id, int days = 90, string agg = "auto")
    {
        var last = await new Cls_ReadingDAL().LatestTimeAsync(id);
        if (!last.HasValue) return Ok(Array.Empty<ReadingPoint>());
        DateTime? from = days > 0 ? last.Value.AddDays(-days) : null;
        // Phase 4: long ranges are served from daily aggregates (agg=auto switches above 5 000 readings)
        if (agg == "daily" || (agg == "auto" && await new Cls_AggregateDAL().CountAsync(id, from) > 5000))
            return Ok(await new Cls_AggregateDAL().DailyAsync(id, from));
        return Ok(await new Cls_ReadingDAL().SeriesAsync(id, from, null));
    }

    [HttpGet("{id}/thresholds")]
    public async Task<IActionResult> Thresholds(string id) => Ok(await new Cls_SensorDAL().ThresholdHistoryAsync(id));

    [Authorize(Policy = Policies.ManageSensors), HttpPost("{id}/thresholds")]
    public async Task<IActionResult> NewThreshold(string id, SensorThreshold t)
    {
        var s = await new Cls_SensorDAL().GetAsync(id) ?? throw new BusinessException("Sensor not found.");
        var lv = new[] { t.AlertValue, t.AlarmValue, t.EmergencyValue }.Where(x => x.HasValue).Select(x => x!.Value).ToList();
        if (lv.Count == 0) throw new BusinessException("Enter at least one threshold level.");
        if (!lv.Zip(lv.Skip(1), (a, b) => s.Direction == "Falling" ? b < a : b > a).All(x => x)) throw new BusinessException($"Levels must be in order for a {s.Direction.ToLower()} sensor.");
        if (string.IsNullOrWhiteSpace(t.Basis)) throw new BusinessException("Record the basis of the thresholds (design value, statistics, history or PFMA).");
        t.Id = Ids.New(); t.SensorId = id; t.CreatedBy = User.FullName(); t.Persistence = Math.Clamp(t.Persistence, 1, 10);
        await new Cls_SensorDAL().InsertThresholdAsync(t);
        await Audit("Thresholds", $"New threshold version for {s.Code}: {t.AlertValue}/{t.AlarmValue}/{t.EmergencyValue} ({t.Method})", "sensor", id);
        return Ok(t);
    }

    [Authorize(Policy = Policies.ApproveThresholds), HttpPost("thresholds/{thresholdId}/approve")]
    public async Task<IActionResult> Approve(string thresholdId)
    {
        var dal = new Cls_SensorDAL();
        var history = await dal.ThresholdHistoryAsync((await FindSensorOfThreshold(thresholdId)) ?? throw new BusinessException("Threshold not found."));
        var t = history.First(x => x.Id == thresholdId);
        if (t.CreatedBy == User.FullName()) throw new BusinessException("Segregation of duties: thresholds must be approved by someone other than their author.");
        if (await dal.ApproveThresholdAsync(thresholdId, User.FullName()) == 0) throw new BusinessException("Already approved.");
        await Audit("Thresholds", $"Approved threshold version {thresholdId}", "sensor", t.SensorId);
        return NoContent();
    }

    private static async Task<string?> FindSensorOfThreshold(string thresholdId)
    {
        using var c = new MySqlConnector.MySqlConnection(AppConfig.ConnectionString);
        return await Dapper.SqlMapper.ExecuteScalarAsync<string?>(c, "SELECT sensor_id FROM sensor_threshold WHERE id=@thresholdId", new { thresholdId });
    }
}

// =====================================================================
[Authorize, Route("api/pfma")]
public class PfmaController : AppController
{
    [Authorize(Policy = Policies.ManagePfma), HttpPost]
    public async Task<IActionResult> Create(Pfma p)
    {
        if (string.IsNullOrWhiteSpace(p.DamId) || string.IsNullOrWhiteSpace(p.FailureModeCode)) throw new BusinessException("Dam and failure mode are required.");
        p.Id = Ids.New(); p.CreatedBy = User.FullName(); p.Status = string.IsNullOrWhiteSpace(p.Status) ? "Active" : p.Status;
        var dal = new Cls_PfmaDAL();
        await dal.InsertAsync(p);
        if (p.SensorIds.Count > 0) await dal.SetSensorsAsync(p.Id, p.SensorIds);
        await Audit("PFMA", $"Added failure mode {p.FailureModeCode} at {p.Location}", "pfma", p.Id);
        return Ok(p);
    }

    [Authorize(Policy = Policies.ManagePfma), HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, Pfma p)
    {
        var dal = new Cls_PfmaDAL(); var cur = await dal.GetAsync(id);
        if (cur == null) return NotFound();
        p.Id = id; p.DamId = cur.DamId;
        await dal.UpdateAsync(p);
        await dal.SetSensorsAsync(id, p.SensorIds);
        await Audit("PFMA", $"Updated failure mode {p.FailureModeCode} ({p.SensorIds.Count} linked sensors)", "pfma", id);
        return Ok(p);
    }
}

// =====================================================================
[Authorize, Route("api/import")]
public class ImportController : AppController
{
    [Authorize(Policy = Policies.ImportData), HttpPost]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> Import(ImportRequest req)
    {
        if (req.Readings.Count > 200_000) throw new BusinessException("Maximum 200 000 readings per import — split the file.");
        return Ok(await new Cls_QualityEngine().ProcessAsync(req, "Excel", User.FullName(), User.UserId()));
    }

    [HttpGet("batches")] public async Task<IActionResult> Batches(int take = 50) => Ok(await new Cls_ImportDAL().BatchesAsync(take));
    [HttpGet("{id}/issues")] public async Task<IActionResult> Issues(string id) => Ok(await new Cls_ImportDAL().IssuesAsync(id));
    [HttpGet("quarantine")] public async Task<IActionResult> Quarantine() => Ok(await new Cls_ImportDAL().QuarantineAsync());

    [Authorize(Policy = Policies.ImportData), HttpDelete("quarantine")]
    public async Task<IActionResult> DiscardQuarantine()
    {
        var n = await new Cls_ImportDAL().DiscardQuarantineAsync();
        await Audit("Data import", $"Discarded {n} quarantined reading(s)");
        return Ok(new { discarded = n });
    }
}

// =====================================================================
/// <summary>IoT gateway endpoint (FRS-001 §5.2). Authenticated by gateway code + API key, not by user session.</summary>
[Route("api/telemetry")]
public class TelemetryController : AppController
{
    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> Post(TelemetryMessage msg, [FromHeader(Name = "X-Gateway-Code")] string? code, [FromHeader(Name = "X-Api-Key")] string? key)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(key)) return Unauthorized(new { error = "Gateway credentials missing." });
        var (gw, hash) = await new Cls_GatewayDAL().GetByCodeAsync(code);
        if (gw == null || hash == null || !gw.IsActive || !PasswordHasher.Verify(key, hash)) return Unauthorized(new { error = "Invalid gateway credentials." });
        var dam = await new Cls_DamDAL().GetAsync(gw.DamId);
        if (dam == null || !string.Equals(dam.Code, msg.DamId, StringComparison.OrdinalIgnoreCase)) return StatusCode(403, new { error = "Gateway is not registered for this dam." });
        var interval = (await new Cls_Gateway4DAL().HealthAsync(gw.Id)).FirstOrDefault()?.ExpectedIntervalMin ?? 60;
        var o = await new Cls_TelemetryProcessor().ProcessAsync(msg, gw, interval, "HTTP");
        if (o.Error != null) return BadRequest(new { messageId = o.MessageId, error = o.Error });
        return o.DuplicateMessage ? Ok(new { messageId = o.MessageId, duplicateMessage = true, accepted = 0 })
            : Accepted(new { messageId = o.MessageId, duplicateMessage = false, accepted = o.Accepted, rejected = o.Rejected, quarantined = o.Quarantined, duplicates = o.Duplicates, late = o.Late, alerts = o.AlertsRaised });
    }
}

[Authorize(Policy = Policies.ManageGateways), Route("api/gateways")]
public class GatewaysController : AppController
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateGatewayRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Code) || await new Cls_DamDAL().GetAsync(req.DamId) == null) throw new BusinessException("Gateway code and a valid dam are required.");
        if ((await new Cls_GatewayDAL().GetByCodeAsync(req.Code.Trim())).gw != null) throw new BusinessException("Gateway code already exists.");
        var key = PasswordHasher.NewApiKey(); var id = Ids.New();
        await new Cls_GatewayDAL().InsertAsync(id, req.Code.Trim(), req.DamId, PasswordHasher.Hash(key));
        if (req.ExpectedIntervalMin is int iv) await new Cls_Gateway4DAL().SetIntervalAsync(id, Math.Clamp(iv, 1, 1440));
        await Audit("Gateways", $"Registered gateway {req.Code}", "gateway", id);
        var damCode = (await new Cls_DamDAL().GetAsync(req.DamId))!.Code; var root = AppConfig.MqttTopicRoot;
        return Ok(new { id, code = req.Code.Trim(), apiKey = key, note = "Store this key now — it is shown only once.",
            mqtt = new { telemetryTopic = $"{root}/{damCode}/{req.Code.Trim()}/telemetry", statusTopic = $"{root}/{damCode}/{req.Code.Trim()}/status", ackTopic = $"{root}/{damCode}/{req.Code.Trim()}/ack" } });
    }
}

// =====================================================================
[Authorize, Route("api/alerts")]
public class AlertsController : AppController
{
    private static readonly string[] Dispositions = { "Confirmed", "False alarm", "Instrument fault", "Resolved" };

    [HttpGet]
    public async Task<IActionResult> List(string? damId, string? status = "Active", int? minLevel = null, int take = 500) =>
        Ok(await new Cls_AlertDAL().ListAsync(damId, status == "All" ? null : status, minLevel, Math.Clamp(take, 1, 2000)));

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var a = await new Cls_AlertDAL().GetAsync(id);
        if (a == null) return NotFound();
        object? series = null; object interp = Array.Empty<object>();
        if (a.SensorId != null)
        {
            var last = await new Cls_ReadingDAL().LatestTimeAsync(a.SensorId);
            series = last.HasValue ? await new Cls_ReadingDAL().SeriesAsync(a.SensorId, last.Value.AddDays(-30), null) : null;
            var prm = (await new Cls_ReferenceDAL().ParametersAsync()).FirstOrDefault(p => p.Code == a.ParameterCode);
            if (prm != null) interp = await new Cls_ReferenceDAL().InterpretationsForGroupAsync(prm.GroupCode);
        }
        var history = await new Cls_AuditDAL().ListAsync(50, id);
        return Ok(new { alert = a, series, interpretations = interp, history });
    }

    [Authorize(Policy = Policies.AcknowledgeAlerts), HttpPost("{id}/acknowledge")]
    public async Task<IActionResult> Acknowledge(string id, AckRequest req)
    {
        if (await new Cls_AlertDAL().AcknowledgeAsync(id, User.FullName(), req.Note) == 0) throw new BusinessException("Only open alerts can be acknowledged.");
        await Audit("Alerts", "Acknowledged" + (string.IsNullOrWhiteSpace(req.Note) ? "" : ": " + req.Note), "alert", id);
        return NoContent();
    }

    [Authorize(Policy = Policies.CloseAlerts), HttpPost("{id}/close")]
    public async Task<IActionResult> Close(string id, CloseRequest req)
    {
        if (!Dispositions.Contains(req.Disposition)) throw new BusinessException("Disposition must be one of: " + string.Join(", ", Dispositions) + ".");
        if (string.IsNullOrWhiteSpace(req.Note)) throw new BusinessException("Record the reason for closing.");
        if (await new Cls_AlertDAL().CloseAsync(id, User.FullName(), req.Disposition, req.Note) == 0) throw new BusinessException("Alert is already closed.");
        await Audit("Alerts", $"Closed as {req.Disposition}: {req.Note}", "alert", id);
        return NoContent();
    }
}

// =====================================================================
[Authorize, Route("api/dashboard")]
public class DashboardController : AppController
{
    [HttpGet("portfolio")]
    public async Task<IActionResult> Portfolio()
    {
        var dams = await new Cls_DamDAL().SummariesAsync();
        var (openFindings, highFindings, overdueInspections) = await new Cls_InspectionDAL().TotalsAsync();
        foreach (var d in dams) { var r = await new Cls_DhiEngine().ComputeAsync(d.Id); d.Dhi = r.Dhi; d.DhiBand = r.Band; d.Risk = r.Risk; d.Rating = r.Rating; }
        dams = dams.OrderByDescending(d => d.MaxOpenLevel).ThenByDescending(d => d.Risk).ToList();
        return Ok(new
        {
            dams,
            totals = new
            {
                dams = dams.Count, sensors = dams.Sum(d => d.Sensors), readings = dams.Sum(d => d.Readings), openAlerts = dams.Sum(d => d.OpenAlerts),
                level1 = dams.Sum(d => d.L1), level2 = dams.Sum(d => d.L2), level3 = dams.Sum(d => d.L3),
                damsInAlarm = dams.Count(d => d.MaxOpenLevel >= 2), highHazard = dams.Count(d => d.HazardCode == "HIGH"),
                openFindings, highFindings, overdueInspections, averageDhi = dams.Count == 0 ? (double?)null : Math.Round(dams.Average(d => d.Dhi ?? 0), 1)
            }
        });
    }
}

// =====================================================================
[Authorize(Policy = Policies.ManageUsers), Route("api/users")]
public class UsersController : AppController
{
    [HttpGet] public async Task<IActionResult> List() => Ok(await new Cls_UserDAL().ListAsync());

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest req)
    {
        var username = (req.Username ?? "").Trim().ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(username, "^[a-z0-9._-]{3,30}$")) throw new BusinessException("Username: 3–30 letters, digits, dot, dash or underscore.");
        if (!Roles.All.Contains(req.Role)) throw new BusinessException("Unknown role.");
        if (!PasswordHasher.IsStrong(req.Password)) throw new BusinessException("Password must be at least 8 characters with letters and digits.");
        var dal = new Cls_UserDAL();
        if (await dal.GetByUsernameAsync(username) != null) throw new BusinessException("Username already exists.");
        var u = new AppUser { Id = Ids.New(), Username = username, FullName = req.FullName.Trim(), Role = req.Role, PasswordHash = PasswordHasher.Hash(req.Password), IsActive = true, MustChangePassword = true, Email = req.Email?.Trim(), Phone = req.Phone?.Trim() };
        await dal.InsertAsync(u);
        await Audit("Users", $"Created user {username} as {req.Role}", "app_user", u.Id);
        return Ok(new UserView(u.Id, u.Username, u.FullName, u.Role, true, true, null, u.Email, u.Phone));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, UpdateUserRequest req)
    {
        var dal = new Cls_UserDAL(); var u = await dal.GetAsync(id) ?? throw new BusinessException("User not found.");
        if (!Roles.All.Contains(req.Role)) throw new BusinessException("Unknown role.");
        if (id == User.UserId() && (req.Role != u.Role || !req.IsActive)) throw new BusinessException("You cannot change your own role or disable yourself.");
        if (u.Role == Roles.Admin && u.IsActive && (req.Role != Roles.Admin || !req.IsActive) && await dal.CountActiveAdminsAsync() < 2) throw new BusinessException("At least one active administrator is required.");
        await dal.UpdateAsync(id, req.FullName.Trim(), req.Role, req.IsActive, req.Email?.Trim(), req.Phone?.Trim());
        await Audit("Users", $"Updated {u.Username}: role {req.Role}, {(req.IsActive ? "active" : "disabled")}", "app_user", id);
        return NoContent();
    }

    [HttpPost("{id}/reset-password")]
    public async Task<IActionResult> Reset(string id)
    {
        var dal = new Cls_UserDAL(); var u = await dal.GetAsync(id) ?? throw new BusinessException("User not found.");
        var temp = "Dm" + PasswordHasher.NewApiKey()[..8] + "9";
        await dal.SetPasswordAsync(id, PasswordHasher.Hash(temp), true);
        await Audit("Users", $"Reset password for {u.Username}", "app_user", id);
        return Ok(new { temporaryPassword = temp, note = "Give this to the user securely; they must change it at next sign-in." });
    }
}

// =====================================================================
[Authorize(Roles = Roles.Admin + "," + Roles.Approver + "," + Roles.DamSafetyEngineer + "," + Roles.Management), Route("api/audit")]
public class AuditController : AppController
{
    [HttpGet] public async Task<IActionResult> List(int take = 300, string? entityId = null) => Ok(await new Cls_AuditDAL().ListAsync(Math.Clamp(take, 1, 2000), entityId));
}
