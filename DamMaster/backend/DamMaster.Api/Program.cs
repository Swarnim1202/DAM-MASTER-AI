using DamMaster.Api.Common;
using DamMaster.Api.DAL;
using DamMaster.Api.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.DataProtection;

System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;   // engineering numbers: "." decimal everywhere
var builder = WebApplication.CreateBuilder(args);
AppConfig.Init(builder.Configuration);
Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

// Phase 7: persist data-protection keys so sign-in cookies (and SSO state) survive restarts and work across instances
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(AppConfig.StorageRoot, "keys"))).SetApplicationName("DamMaster");
builder.Services.AddControllers(o => o.Filters.Add<BusinessExceptionFilter>()).AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter());
    o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

// Session-cookie authentication (HttpOnly, SameSite=Lax). APIs return 401/403 instead of redirecting.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "dm.session";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization(o =>
{
    foreach (var (name, roles) in Policies.Map) o.AddPolicy(name, p => p.RequireRole(roles));
});
builder.Services.AddHostedService<DamMaster.Api.Engine.AlertEscalationService>();   // background jobs (escalation, dispatch, gateway health, daily DHI / HST refit)
builder.Services.AddHostedService<DamMaster.Api.Engine.MqttIngestionService>();    // Phase 4: MQTT ingestion (Mqtt:Enabled)
var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(o => o.AddPolicy("spa", p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

// Errors: business rules → 400 with message; anything else → 500 with a trace id (details only in the server log).
app.UseExceptionHandler(e => e.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    ctx.Response.ContentType = "application/json";
    if (ex is BusinessException)
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    else
    {
        app.Logger.LogError(ex, "Unhandled error {TraceId}", ctx.TraceIdentifier);
        ctx.Response.StatusCode = 500;
        await ctx.Response.WriteAsJsonAsync(new { error = "Unexpected server error.", traceId = ctx.TraceIdentifier });
    }
}));

app.UseDefaultFiles();
app.UseStaticFiles();          // production: React build is copied to wwwroot and served from the same origin
app.UseCors("spa");

// CSRF guard: state-changing API calls from the browser must carry X-Requested-With (forces a CORS pre-flight).
app.Use(async (ctx, next) =>
{
    var p = ctx.Request.Path;
    var unsafeMethod = !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method) && !HttpMethods.IsOptions(ctx.Request.Method);
    if (unsafeMethod && p.StartsWithSegments("/api") && !p.StartsWithSegments("/api/telemetry")
        && ctx.Request.Headers["X-Requested-With"] != "DamMaster")
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsJsonAsync(new { error = "Missing X-Requested-With header." });
        return;
    }
    await next();
});

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapFallbackToFile("index.html");

await EnsureAdminAsync(app.Configuration, app.Logger);
app.Run();

// Creates the first administrator when the user table is empty (password must be changed at first sign-in).
static async Task EnsureAdminAsync(IConfiguration cfg, ILogger log)
{
    var dal = new Cls_UserDAL();
    if (await dal.CountAsync() > 0) return;
    var pw = cfg["Bootstrap:AdminPassword"] ?? "ChangeMe@2026";
    await dal.InsertAsync(new AppUser
    {
        Id = Ids.New(), Username = (cfg["Bootstrap:AdminUsername"] ?? "admin").ToLowerInvariant(), FullName = cfg["Bootstrap:AdminFullName"] ?? "System Administrator",
        Role = Roles.Admin, PasswordHash = PasswordHasher.Hash(pw), IsActive = true, MustChangePassword = true
    });
    log.LogWarning("Bootstrap administrator created. Sign in and change the password immediately.");
}
