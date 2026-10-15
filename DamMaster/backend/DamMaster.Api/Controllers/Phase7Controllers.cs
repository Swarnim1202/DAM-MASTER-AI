using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DamMaster.Api.Common;
using DamMaster.Api.DAL;
using DamMaster.Api.Engine;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

namespace DamMaster.Api.Engine
{
    /// <summary>OCR through Tesseract (and Poppler's pdftoppm for PDF pages) — external tools, configured paths, time-limited.</summary>
    public static class Cls_Ocr
    {
        private static async Task<string> Run(string exe, IEnumerable<string> args, int timeoutSec)
        {
            var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {exe}.");
            var outTask = p.StandardOutput.ReadToEndAsync(); var errTask = p.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
            try { await p.WaitForExitAsync(cts.Token); } catch (OperationCanceledException) { try { p.Kill(true); } catch { } throw new TimeoutException($"{Path.GetFileName(exe)} timed out."); }
            if (p.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(exe)} failed: {(await errTask).Trim()}");
            return await outTask;
        }

        public static async Task<List<(int? page, string text)>> RunAsync(string path, string ext)
        {
            var tmp = Path.Combine(Path.GetTempPath(), "dm-ocr-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(tmp);
            try
            {
                var images = new List<(int? page, string file)>();
                if (ext == ".pdf")
                {
                    await Run(AppConfig.OcrPdftoppm, new[] { "-r", AppConfig.OcrDpi.ToString(), "-png", path, Path.Combine(tmp, "p") }, 600);
                    images = Directory.GetFiles(tmp, "p-*.png").OrderBy(f => f).Select((f, k) => ((int?)(k + 1), f)).ToList();
                }
                else images.Add((null, path));
                var pages = new List<(int? page, string text)>();
                foreach (var (page, file) in images)
                {
                    var text = await Run(AppConfig.OcrTesseract, new[] { file, "stdout", "-l", AppConfig.OcrLanguage }, 180);
                    pages.Add((page, System.Text.RegularExpressions.Regex.Replace(text, @"[ \t\r\f\v]+", " ").Trim()));
                }
                return pages;
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }
    }

    /// <summary>
    /// OpenID Connect authorization-code flow with PKCE, implemented with in-box cryptography: discovery, state / nonce in a
    /// data-protected cookie, token exchange, RS256 id_token signature check against the provider's JWKS, issuer / audience /
    /// expiry / nonce validation. Users are matched to existing accounts; roles stay in DAM MASTER.
    /// </summary>
    public static class Cls_Oidc
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
        private static JsonElement? _disco; private static DateTime _discoAt; private static JsonElement? _jwks; private static DateTime _jwksAt;

        public static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        public static byte[] FromB64Url(string s) { s = s.Replace('-', '+').Replace('_', '/'); return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=')); }

        public static async Task<JsonElement> DiscoveryAsync()
        {
            if (_disco.HasValue && DateTime.UtcNow - _discoAt < TimeSpan.FromHours(1)) return _disco.Value;
            var json = await Http.GetStringAsync(AppConfig.SsoAuthority + "/.well-known/openid-configuration");
            _disco = JsonDocument.Parse(json).RootElement.Clone(); _discoAt = DateTime.UtcNow; return _disco.Value;
        }

        private static async Task<JsonElement> JwksAsync(bool refresh)
        {
            if (!refresh && _jwks.HasValue && DateTime.UtcNow - _jwksAt < TimeSpan.FromHours(1)) return _jwks.Value;
            var d = await DiscoveryAsync();
            _jwks = JsonDocument.Parse(await Http.GetStringAsync(d.GetProperty("jwks_uri").GetString())).RootElement.Clone(); _jwksAt = DateTime.UtcNow; return _jwks.Value;
        }

        /// <summary>Validates an id_token and returns its claims; throws with a plain reason on any failure.</summary>
        public static async Task<JsonElement> ValidateIdTokenAsync(string token, string expectedNonce)
        {
            var parts = token.Split('.');
            if (parts.Length != 3) throw new SecurityException("Malformed id_token.");
            var header = JsonDocument.Parse(FromB64Url(parts[0])).RootElement;
            if (header.GetProperty("alg").GetString() != "RS256") throw new SecurityException("Unsupported token algorithm (RS256 required).");
            var kid = header.TryGetProperty("kid", out var k) ? k.GetString() : null;
            var verified = false; var keyFound = false;
            for (var attempt = 0; attempt < 2 && !verified; attempt++)          // second attempt re-reads the JWKS (key rotation)
            {
                var key = (await JwksAsync(attempt == 1)).GetProperty("keys").EnumerateArray().Where(x => x.GetProperty("kty").GetString() == "RSA").Cast<JsonElement?>()
                    .FirstOrDefault(x => kid == null || (x!.Value.TryGetProperty("kid", out var xk) && xk.GetString() == kid));
                if (key == null) continue;
                keyFound = true;
                using var rsa = RSA.Create();
                rsa.ImportParameters(new RSAParameters { Modulus = FromB64Url(key.Value.GetProperty("n").GetString()!), Exponent = FromB64Url(key.Value.GetProperty("e").GetString()!) });
                verified = rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), FromB64Url(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            if (!keyFound) throw new SecurityException("Signing key not found at the identity provider.");
            if (!verified) throw new SecurityException("id_token signature is invalid.");
            var c = JsonDocument.Parse(FromB64Url(parts[1])).RootElement.Clone();
            var disco = await DiscoveryAsync(); var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (c.GetProperty("iss").GetString() != disco.GetProperty("issuer").GetString()) throw new SecurityException("Token issuer does not match.");
            var aud = c.GetProperty("aud"); var audOk = aud.ValueKind == JsonValueKind.Array ? aud.EnumerateArray().Any(a => a.GetString() == AppConfig.SsoClientId) : aud.GetString() == AppConfig.SsoClientId;
            if (!audOk) throw new SecurityException("Token audience does not match this application.");
            if (c.GetProperty("exp").GetInt64() < now - 60) throw new SecurityException("Token has expired.");
            if (c.TryGetProperty("nbf", out var nbf) && nbf.GetInt64() > now + 60) throw new SecurityException("Token not yet valid.");
            if (!c.TryGetProperty("nonce", out var nn) || nn.GetString() != expectedNonce) throw new SecurityException("Token nonce does not match the sign-in request.");
            return c;
        }

        public static async Task<string> ExchangeCodeAsync(string code, string redirectUri, string verifier)
        {
            var d = await DiscoveryAsync();
            var form = new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = redirectUri, ["client_id"] = AppConfig.SsoClientId, ["code_verifier"] = verifier };
            if (!string.IsNullOrEmpty(AppConfig.SsoClientSecret)) form["client_secret"] = AppConfig.SsoClientSecret!;
            var res = await Http.PostAsync(d.GetProperty("token_endpoint").GetString(), new FormUrlEncodedContent(form)); var txt = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) throw new SecurityException($"Token exchange failed (HTTP {(int)res.StatusCode}).");
            return JsonDocument.Parse(txt).RootElement.GetProperty("id_token").GetString() ?? throw new SecurityException("No id_token returned.");
        }
    }

    public class SecurityException : Exception { public SecurityException(string m) : base(m) { } }

    /// <summary>Portfolio summary of this instance (for a national / central hub) and aggregation of several state instances.</summary>
    public static class Cls_Federation
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

        public static async Task<object> LocalSummaryAsync()
        {
            var dams = await new Cls_DamDAL().SummariesAsync(); var rows = new List<object>();
            foreach (var d in dams) { var r = await new Cls_DhiEngine().ComputeAsync(d.Id); rows.Add(new { d.Code, d.Name, d.State, d.HazardCode, d.DamTypeCode, dhi = r.Dhi, band = r.Band, risk = r.Risk, rating = r.Rating, d.MaxOpenLevel, d.OpenAlerts, d.DataAsOf }); }
            return new { instance = AppConfig.FederationInstanceName, generatedAt = DateTime.UtcNow, dams = rows, totals = new { dams = dams.Count, openAlerts = dams.Sum(x => x.OpenAlerts), level3 = dams.Sum(x => x.L3), damsInAlarm = dams.Count(x => x.MaxOpenLevel >= 2) } };
        }

        public static async Task<object> PortfolioAsync()
        {
            var tasks = AppConfig.FederationInstances.Select(async inst =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, inst.Url.TrimEnd('/') + "/api/federation/summary"); req.Headers.Add("X-Federation-Key", inst.Key);
                    var res = await Http.SendAsync(req);
                    if (!res.IsSuccessStatusCode) return (object)new { name = inst.Name, url = inst.Url, ok = false, error = $"HTTP {(int)res.StatusCode}", latencyMs = sw.ElapsedMilliseconds, summary = (JsonElement?)null };
                    return new { name = inst.Name, url = inst.Url, ok = true, error = (string?)null, latencyMs = sw.ElapsedMilliseconds, summary = (JsonElement?)JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.Clone() };
                }
                catch (Exception ex) { return new { name = inst.Name, url = inst.Url, ok = false, error = ex.Message, latencyMs = sw.ElapsedMilliseconds, summary = (JsonElement?)null }; }
            }).ToList();
            var remote = await Task.WhenAll(tasks);
            object? self = AppConfig.FederationIncludeSelf ? await LocalSummaryAsync() : null;
            return new { self, instances = remote, generatedAt = DateTime.UtcNow };
        }
    }
}

namespace DamMaster.Api.Controllers
{
    // =====================================================================
    [Authorize, Route("api/field")]
    public class FieldController : AppController
    {
        [HttpGet("inspections")] public async Task<IActionResult> Active() => Ok(await new Cls_InspectionDAL().ListAsync(null, "Active", 200));
        [HttpGet("package/{inspectionId}")] public async Task<IActionResult> Package(string inspectionId) => Ok(await new Cls_FieldService().PackageAsync(inspectionId));

        [Authorize(Policy = Policies.ManageInspections), HttpPost("sync")]
        public async Task<IActionResult> Sync(FieldSyncRequest req) => Ok(await new Cls_FieldService().SyncAsync(req, User.FullName()));
    }

    [Authorize, Route("api")]
    public class PhotosController : AppController
    {
        [HttpGet("findings/{findingId}/photos")] public async Task<IActionResult> List(string findingId) => Ok(await new Cls_FieldService().PhotosAsync(findingId));

        /// <summary>Photo for a finding — by finding id, or by the offline client op id that created the finding.</summary>
        [Authorize(Policy = Policies.ManageInspections), HttpPost("findings/{findingRef}/photos")]
        [RequestSizeLimit(12_000_000)]
        public async Task<IActionResult> Add(string findingRef, IFormFile file, [FromForm] double? latitude, [FromForm] double? longitude, [FromForm] DateTime? takenAt, [FromForm] string? caption, [FromForm] string? clientRef)
        {
            var svc = new Cls_FieldService();
            var findingId = await new Cls_InspectionDAL().FindingAsync(findingRef) != null ? findingRef : await svc.FindingIdForClientOpAsync(findingRef) ?? throw new BusinessException("Finding not found — sync the finding first.");
            return Ok(await svc.AddPhotoAsync(findingId, file, latitude, longitude, takenAt, caption, clientRef, User.FullName()));
        }

        [HttpGet("photos/{id}")]
        public async Task<IActionResult> Get(string id)
        {
            var p = await new Cls_FieldService().PhotoAsync(id);
            if (p == null || !System.IO.File.Exists(p.StoragePath)) return NotFound();
            return PhysicalFile(Path.GetFullPath(p.StoragePath), p.ContentType);
        }
    }

    // =====================================================================
    [Route("api/auth/sso")]
    public class SsoController : AppController
    {
        private const string CookieName = "dm.sso";
        private IDataProtector Protector => HttpContext.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector("dm.sso.v1");
        private string RedirectUri => $"{Request.Scheme}://{Request.Host}{Request.PathBase}/api/auth/sso/callback";

        [AllowAnonymous, HttpGet("config")]
        public IActionResult Config() => Ok(new { enabled = AppConfig.SsoEnabled, label = AppConfig.SsoButtonLabel, localLogin = AppConfig.AllowLocalLogin });

        [AllowAnonymous, HttpGet("login")]
        public async Task<IActionResult> Login(string? returnUrl)
        {
            if (!AppConfig.SsoEnabled) return NotFound();
            var d = await Cls_Oidc.DiscoveryAsync();
            string state = Cls_Oidc.B64Url(RandomNumberGenerator.GetBytes(24)), nonce = Cls_Oidc.B64Url(RandomNumberGenerator.GetBytes(24)), verifier = Cls_Oidc.B64Url(RandomNumberGenerator.GetBytes(48));
            var challenge = Cls_Oidc.B64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var safeReturn = returnUrl != null && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") ? returnUrl : "/";
            Response.Cookies.Append(CookieName, Protector.Protect(JsonSerializer.Serialize(new { state, nonce, verifier, ret = safeReturn, at = DateTime.UtcNow })),
                new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = Request.IsHttps, MaxAge = TimeSpan.FromMinutes(10), Path = "/api/auth/sso" });
            var url = d.GetProperty("authorization_endpoint").GetString() + "?" + string.Join("&", new Dictionary<string, string>
            {
                ["response_type"] = "code", ["client_id"] = AppConfig.SsoClientId, ["redirect_uri"] = RedirectUri, ["scope"] = AppConfig.SsoScopes,
                ["state"] = state, ["nonce"] = nonce, ["code_challenge"] = challenge, ["code_challenge_method"] = "S256"
            }.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
            return Redirect(url);
        }

        [AllowAnonymous, HttpGet("callback")]
        public async Task<IActionResult> Callback(string? code, string? state, string? error)
        {
            IActionResult Fail(string why) { Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/api/auth/sso" }); return Redirect("/login?sso_error=" + Uri.EscapeDataString(why)); }
            if (!AppConfig.SsoEnabled) return NotFound();
            if (error != null) return Fail("The identity provider refused the sign-in: " + error);
            if (!Request.Cookies.TryGetValue(CookieName, out var raw)) return Fail("Sign-in session expired — please try again.");
            JsonElement ctx;
            try { ctx = JsonDocument.Parse(Protector.Unprotect(raw)).RootElement; } catch { return Fail("Sign-in session is invalid."); }
            if (state == null || code == null || state != ctx.GetProperty("state").GetString()) return Fail("Sign-in state does not match (possible forged request).");
            if (DateTime.UtcNow - ctx.GetProperty("at").GetDateTime() > TimeSpan.FromMinutes(10)) return Fail("Sign-in took too long — please try again.");
            JsonElement claims;
            try { claims = await Cls_Oidc.ValidateIdTokenAsync(await Cls_Oidc.ExchangeCodeAsync(code, RedirectUri, ctx.GetProperty("verifier").GetString()!), ctx.GetProperty("nonce").GetString()!); }
            catch (Engine.SecurityException ex) { await new Cls_AuditDAL().WriteAsync(null, null, "Session", "SSO rejected: " + ex.Message); return Fail(ex.Message); }
            var sub = claims.GetProperty("sub").GetString() ?? "";
            var who = claims.TryGetProperty(AppConfig.SsoUserClaim, out var cv) ? cv.GetString() : null;
            if (string.IsNullOrWhiteSpace(who)) return Fail($"The identity provider did not send the '{AppConfig.SsoUserClaim}' claim.");
            using var c = new MySqlConnector.MySqlConnection(AppConfig.ConnectionString);
            var u = await Dapper.SqlMapper.QueryFirstOrDefaultAsync<Models.AppUser>(c, "SELECT * FROM app_user WHERE LOWER(email)=LOWER(@who) OR username=LOWER(@who) LIMIT 1", new { who });
            if (u == null) return Fail($"No DAM MASTER account for {who}. Ask the administrator to create it.");
            if (!u.IsActive) return Fail("This account is disabled.");
            var bound = await Dapper.SqlMapper.ExecuteScalarAsync<string?>(c, "SELECT sso_subject FROM app_user WHERE id=@id", new { id = u.Id });
            if (bound != null && bound != sub) return Fail("This account is linked to a different organisation identity.");
            if (bound == null) await Dapper.SqlMapper.ExecuteAsync(c, "UPDATE app_user SET sso_subject=@sub WHERE id=@id", new { sub, id = u.Id });
            await new Cls_UserDAL().RecordLoginAsync(u.Id, true);
            var identity = new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, u.Id), new(ClaimTypes.Name, u.Username), new(ClaimTypes.GivenName, u.FullName), new(ClaimTypes.Role, u.Role), new("auth", "sso") }, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/api/auth/sso" });
            await new Cls_AuditDAL().WriteAsync(u.Id, u.Username, "Session", "Signed in (SSO)");
            return Redirect(ctx.GetProperty("ret").GetString() ?? "/");
        }
    }

    // =====================================================================
    [Route("api/federation")]
    public class FederationController : AppController
    {
        /// <summary>Read by a central hub with the instance key — portfolio summary only, no raw data.</summary>
        [AllowAnonymous, HttpGet("summary")]
        public async Task<IActionResult> Summary([FromHeader(Name = "X-Federation-Key")] string? key)
        {
            var expected = AppConfig.FederationInstanceKey;
            if (string.IsNullOrEmpty(expected) || key == null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(expected))) return Unauthorized(new { error = "Invalid federation key." });
            return Ok(await Cls_Federation.LocalSummaryAsync());
        }

        [Authorize(Roles = Roles.Admin + "," + Roles.Management + "," + Roles.Approver + "," + Roles.DamSafetyEngineer), HttpGet("portfolio")]
        public async Task<IActionResult> Portfolio() => Ok(await Cls_Federation.PortfolioAsync());
    }
}
