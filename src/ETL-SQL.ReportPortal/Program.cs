using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using ETL_SQL.ReportPortal;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Middleware;
using ETL_SQL.ReportPortal.Services.HealthChecks;
using ETL_SQL.Orchestrator.Channels;
using ETL_SQL.Orchestrator;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Engine.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddSecureConfiguration();

// ── Configuration ─────────────────────────────────────────────────────────────
var portalConfig = builder.Configuration.GetSection("Portal").Get<PortalConfig>()
    ?? new PortalConfig();

// ── Engine services (centralized in Orchestrator extension) ─────────
var loggerService = new LoggerService();
loggerService.InitializeAppLogger(
    builder.Configuration["Logging:AppLog:Directory"] ?? "logs/portal",
    int.TryParse(builder.Configuration["Logging:AppLog:RetentionDays"],   out var rd) ? rd : 30,
    int.TryParse(builder.Configuration["Logging:AppLog:FileSizeLimitMb"], out var sl) ? sl : 10);

builder.Services.AddSingleton<LoggerService>(loggerService);
builder.Services.AddSingleton<ETL_SQL.Common.ILogger>(loggerService);
builder.Services.AddSingleton<ETL_SQL.Common.ILoggerService>(loggerService);

builder.Services.AddEtlSqlEngine(builder.Configuration);

// JWT secret validation: registered as a hosted-service check so it fires AFTER
// WebApplicationFactory has had a chance to inject test configuration.
// A fatal startup error is raised via IHostApplicationLifetime if the secret is missing/short.

builder.Services.AddSingleton(portalConfig);

// Ensure required directories exist
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(portalConfig.DatabasePath))!);
Directory.CreateDirectory(Path.GetFullPath(portalConfig.ScriptRootPath));
Directory.CreateDirectory(Path.GetFullPath(portalConfig.SnapshotDirectory));
Directory.CreateDirectory(Path.GetFullPath(portalConfig.MapRootPath));
Directory.CreateDirectory(Path.GetFullPath(portalConfig.DatasetRootPath));

// ── EF Core / SQLite ──────────────────────────────────────────────────────────
var dbPath = Path.GetFullPath(portalConfig.DatabasePath);
builder.Services.AddDbContext<PortalDbContext>(opt =>
    opt.UseSqlite($"Data Source={dbPath}"));

// ── Identity ──────────────────────────────────────────────────────────────────
builder.Services.AddIdentity<PortalUser, PortalRole>(opt =>
{
    opt.Password.RequireDigit           = true;
    opt.Password.RequiredLength         = 8;
    opt.Password.RequireNonAlphanumeric = false;
    opt.Lockout.DefaultLockoutTimeSpan  = TimeSpan.FromMinutes(15);
    opt.Lockout.MaxFailedAccessAttempts = 5;
    opt.Lockout.AllowedForNewUsers      = true;
})
.AddEntityFrameworkStores<PortalDbContext>()
.AddDefaultTokenProviders();

// ── JWT Authentication ────────────────────────────────────────────────────────
// Use a zero-filled placeholder when no secret is configured so the service can start.
// JwtSecretValidationService shuts down the app if the secret is missing/short in production.
// In tests, ConfigureWebHost's PostConfigure<JwtBearerOptions> replaces the key.
// If no secret is configured, generate a random ephemeral key so the app can start
// (JwtSecretValidationService will shut it down before serving requests in production).
// A random key — rather than all-zero — means no attacker-crafted token signed with a
// known placeholder will ever validate during the brief startup window.
var rawSecret = string.IsNullOrEmpty(portalConfig.Jwt.Secret)
    ? System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)
    : Encoding.UTF8.GetBytes(portalConfig.Jwt.Secret);
var signingKey = new SymmetricSecurityKey(rawSecret);

builder.Services.AddAuthentication(opt =>
{
    opt.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    opt.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(opt =>
{
    opt.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer           = false,
        ValidateAudience         = false,
        ValidateLifetime         = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey         = signingKey,
        ClockSkew                = TimeSpan.FromSeconds(30)
    };
});

builder.Services.AddAuthorization(opt =>
{
    opt.AddPolicy("OrchestratorAccess", p => p.RequireRole("Admin", "OrchestratorManager"));
});

// ── Swagger ───────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "ETL-SQL Report Portal", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.OpenApiSecurityScheme
    {
        Name   = "Authorization",
        Type   = Microsoft.OpenApi.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In     = Microsoft.OpenApi.ParameterLocation.Header
    });
    c.AddSecurityRequirement(_ => new Microsoft.OpenApi.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.OpenApiSecuritySchemeReference("Bearer"),
            []
        }
    });
});

builder.Services.AddMemoryCache();
builder.Services.AddScoped<ETL_SQL.ReportPortal.Services.TokenService>();
builder.Services.AddScoped<ETL_SQL.ReportPortal.Services.AuditService>();
builder.Services.AddScoped<IDatasetRegistry, ETL_SQL.ReportPortal.Services.DatasetRegistryService>();
builder.Services.AddScoped<ETL_SQL.ReportPortal.Services.DatasetViewerService>();
builder.Services.AddSingleton<ETL_SQL.ReportPortal.Services.SmtpPasswordProtector>();
builder.Services.AddSingleton<ETL_SQL.ReportPortal.Services.OrchestratorDbLocator>();
builder.Services.AddSingleton<ETL_SQL.ReportPortal.Services.PortalBrandingSettingsService>();

// ── Orchestrator Management Proxy ─────────────────────────────────────────────
// OrchestratorSettingsService holds the active URL/key and persists UI-configured
// overrides so the admin can point the portal at a different host without a restart.
builder.Services.AddSingleton<ETL_SQL.ReportPortal.Services.OrchestratorSettingsService>();
builder.Services.AddHttpClient<ETL_SQL.ReportPortal.Services.OrchestratorProxyService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    // No base address — OrchestratorProxyService builds absolute URIs from OrchestratorSettingsService.
});

// ── Orchestrator Channel ──────────────────────────────────────────────────────
if (!string.IsNullOrEmpty(portalConfig.Orchestrator.ApiUrl))
{
    builder.Services.AddHttpClient<IJobChannel, HttpJobChannelClient>(client =>
    {
        client.BaseAddress = new Uri(portalConfig.Orchestrator.ApiUrl);
    });
}
else
{
    // Fallback to in-process execution (requires IScriptExecutor)
    builder.Services.AddTransient<IScriptExecutor, ETL_SQL.Orchestrator.Execution.ScriptExecutorAdapter>();
    builder.Services.AddSingleton<IJobChannel, InProcessJobChannel>();
}

// Phase 2 — execution, session cache, Orchestrator poller
builder.Services.AddSingleton<ETL_SQL.ReportPortal.Services.SessionCache>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ETL_SQL.ReportPortal.Services.SessionCache>());
builder.Services.AddSingleton<ETL_SQL.ReportPortal.Services.ExecutionJobService>();
builder.Services.AddHostedService<ETL_SQL.ReportPortal.Services.OrchestratorPollerService>();

// JWT secret validation (runs after WebApplicationFactory can inject test configuration)
builder.Services.AddHostedService<ETL_SQL.ReportPortal.Services.JwtSecretValidationService>();

// Phase 5 — subscriptions (backed by Orchestrator jobs)

// Phase 6 — health checks
builder.Services.AddHealthChecks()
    .AddCheck<PortalDbHealthCheck>     ("db",          HealthStatus.Unhealthy, ["ready"])
    .AddCheck<OrchestratorHealthCheck> ("orchestrator", HealthStatus.Degraded,  ["live"])
    .AddCheck<ExecutionCapacityHealthCheck>("execution", HealthStatus.Degraded,  ["live"]);

builder.Services.AddControllers();

// ── Kestrel Config (HTTPS) ────────────────────────────────────────────────────
builder.WebHost.ConfigureKestrel(options =>
{
    var kestrelSection = builder.Configuration.GetSection("Kestrel");
    if (kestrelSection.Exists())
    {
        options.Configure(kestrelSection);
    }
});

// ── App pipeline ──────────────────────────────────────────────────────────────
var app = builder.Build();

// Apply EF migrations and enable WAL mode on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
    db.Database.Migrate();
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

    await SeedFirstRunAsync(scope.ServiceProvider, portalConfig);
}

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0");
    context.Response.Headers.Append("Pragma", "no-cache");
    await next();
});

var staticFileOptions = new StaticFileOptions();
var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
provider.Mappings[".geojson"] = "application/geo+json";
staticFileOptions.ContentTypeProvider = provider;
app.UseStaticFiles(staticFileOptions);
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<MustChangePasswordMiddleware>();
app.MapControllers();

// Health endpoint — detailed checks (db, orchestrator, execution capacity)
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        var overall = report.Status switch
        {
            HealthStatus.Healthy  => "Healthy",
            HealthStatus.Degraded => "Degraded",
            _                     => "Unhealthy"
        };
        var result = new
        {
            status = overall,
            checks = report.Entries.ToDictionary(
                kv => kv.Key,
                kv => new
                {
                    status      = kv.Value.Status.ToString(),
                    description = kv.Value.Description,
                    data        = kv.Value.Data.Count > 0 ? kv.Value.Data : null,
                    error       = kv.Value.Exception?.Message
                })
        };
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(result,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}).AllowAnonymous();

app.MapGet("/third-party-notices", () =>
{
    var noticesPath = FindRepoFile("THIRD-PARTY-NOTICES.md");
    return noticesPath is null
        ? Results.NotFound("THIRD-PARTY-NOTICES.md was not found.")
        : Results.Text(File.ReadAllText(noticesPath), "text/markdown; charset=utf-8");
}).AllowAnonymous();

// Root → login
app.MapGet("/", () => Results.Redirect("/login.html"))
   .AllowAnonymous();

app.Run();
return 0;

// ── First-run seed ────────────────────────────────────────────────────────────
static async Task SeedFirstRunAsync(IServiceProvider services, PortalConfig config)
{
    var userMgr = services.GetRequiredService<UserManager<PortalUser>>();
    var roleMgr = services.GetRequiredService<RoleManager<PortalRole>>();

    foreach (var role in new[] { "Admin", "Publisher", "Viewer", "OrchestratorManager" })
    {
        if (!await roleMgr.RoleExistsAsync(role))
            await roleMgr.CreateAsync(new PortalRole(role));
    }

    var adminUsername = config.FirstRun.AdminUsername;
    if (await userMgr.FindByNameAsync(adminUsername) is null)
    {
        var admin = new PortalUser
        {
            UserName            = adminUsername,
            Email               = $"{adminUsername}@localhost",
            IsActive            = true,
            MustChangePassword  = true
        };
        // Temporary password — must be changed on first login
        var result = await userMgr.CreateAsync(admin, "Admin@12345!");
        if (result.Succeeded)
            await userMgr.AddToRoleAsync(admin, "Admin");
    }
}

static string? FindRepoFile(string fileName)
{
    foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
    {
        var dir = new DirectoryInfo(start);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
    }

    return null;
}
