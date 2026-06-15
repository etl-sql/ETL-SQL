using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Engine.Services;
using ETL_SQL.Orchestrator;
using ETL_SQL.Orchestrator.Channels;
using ETL_SQL.ReportPortal;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Middleware;
using ETL_SQL.ReportPortal.Services;
using ETL_SQL.ReportPortal.Services.HealthChecks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
#if WINDOWS
// Running as a Windows Service, the working directory defaults to System32, which sends every
// relative path (file logs, portal.db, snapshots) there instead of the install folder. Anchor the
// working directory to the executable's folder before anything resolves a relative path.
if (Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService())
{
    System.IO.Directory.SetCurrentDirectory(System.AppContext.BaseDirectory);
}
// Integrate with the Windows SCM so the portal can run as a Windows Service (installed by the MSI);
// this also sets the content root to the executable's directory.
builder.Host.UseWindowsService(o => o.ServiceName = "ETL-SQL Report Portal");
#endif
builder.Configuration.AddSecureConfiguration();

// ── Configuration ─────────────────────────────────────────────────────────────
var portalConfig = builder.Configuration.GetSection("Portal").Get<PortalConfig>()
    ?? new PortalConfig();

// Expose the dataset at-rest key as a process secret so the engine can resolve ENCRYPT = PORTAL (used by
// scheduled refresh jobs, which must not embed the key in persisted SQL). A SEPARATE orchestrator service
// must set the same ETLSQL_DATASET_ATREST_KEY env var (or share this config) for scheduled refresh to run.
if (!string.IsNullOrWhiteSpace(portalConfig.Dataset.AtRestKey))
    Environment.SetEnvironmentVariable(
        ETL_SQL.Core.Common.EncryptionOptions.PortalAtRestKeyEnvVar, portalConfig.Dataset.AtRestKey);

// ── Engine services (centralized in Orchestrator extension) ─────────
var loggerService = new LoggerService();
loggerService.InitializeAppLogger(
    builder.Configuration["Logging:AppLog:Directory"] ?? "logs/portal",
    int.TryParse(builder.Configuration["Logging:AppLog:RetentionDays"], out var rd) ? rd : 30,
    int.TryParse(builder.Configuration["Logging:AppLog:FileSizeLimitMb"], out var sl) ? sl : 10);

builder.Services.AddSingleton<LoggerService>(loggerService);
builder.Services.AddSingleton<ETL_SQL.Common.ILogger>(loggerService);
builder.Services.AddSingleton<ETL_SQL.Common.ILoggerService>(loggerService);

builder.Services.AddEtlSqlEngine(builder.Configuration);

// Cluster node heartbeat (P1.7): register this Portal node in the shared node registry so the
// cluster has a live view of all Portal/Orchestrator nodes over shared state.
ETL_SQL.Orchestrator.Scheduling.NodeHeartbeatServiceCollectionExtensions.AddNodeHeartbeat(
    builder.Services, "Portal");

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

// ── Artifact storage (provider configurable: Local default, SMB/UNC for HA) ──────
// Maps each artifact area to its configured root; the Keys area is the Data Protection key ring
// directory beside the portal database. The provider is wrapped by GuardedArtifactStorage so the
// SecurityService path-traversal / executable / script-immutability guardrails are enforced at the
// single storage boundary (P1.6). Call sites migrate onto this seam incrementally.
builder.Services.AddSingleton<ETL_SQL.Core.Storage.IArtifactStorage>(sp =>
{
    var inner = ETL_SQL.Core.Storage.ArtifactStorageFactory.Create(
        portalConfig.Storage.Provider,
        new Dictionary<ETL_SQL.Core.Storage.ArtifactArea, string>
        {
            [ETL_SQL.Core.Storage.ArtifactArea.Scripts] = portalConfig.ScriptRootPath,
            [ETL_SQL.Core.Storage.ArtifactArea.Snapshots] = portalConfig.SnapshotDirectory,
            [ETL_SQL.Core.Storage.ArtifactArea.Maps] = portalConfig.MapRootPath,
            [ETL_SQL.Core.Storage.ArtifactArea.Datasets] = portalConfig.DatasetRootPath,
            [ETL_SQL.Core.Storage.ArtifactArea.Keys] =
                Path.Combine(Path.GetDirectoryName(Path.GetFullPath(portalConfig.DatabasePath))!, ".portal-keys"),
        });
    var security = sp.GetRequiredService<ETL_SQL.Services.SecurityService>();
    return new ETL_SQL.Core.Storage.GuardedArtifactStorage(inner, security);
});

// ── EF Core (provider configurable: SQLite default, Postgres for HA) ────────────
var dbPath = Path.GetFullPath(portalConfig.DatabasePath);
builder.Services.AddDbContext<PortalDbContext>(opt =>
    PortalDatabase.Configure(opt, portalConfig));

// ── Identity ──────────────────────────────────────────────────────────────────
builder.Services.AddIdentity<PortalUser, PortalRole>(opt =>
{
    opt.Password.RequireDigit = true;
    opt.Password.RequiredLength = 8;
    opt.Password.RequireNonAlphanumeric = false;
    opt.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    opt.Lockout.MaxFailedAccessAttempts = 5;
    opt.Lockout.AllowedForNewUsers = true;
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
var validationKeys = string.IsNullOrEmpty(portalConfig.Jwt.Secret)
    ? [signingKey]
    : JwtSigningKeyRing.ValidationKeys(portalConfig.Jwt);

builder.Services.AddAuthentication(opt =>
{
    opt.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    opt.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(opt =>
{
    opt.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKeys = validationKeys,
        ClockSkew = TimeSpan.FromSeconds(30)
    };
    opt.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var userIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdValue, out var userId))
            {
                context.Fail("Invalid user identity.");
                return;
            }

            var services = context.HttpContext.RequestServices;
            var user = await services
                .GetRequiredService<ETL_SQL.ReportPortal.Services.UserSecurityStateCache>()
                .GetAsync(userId, services.GetRequiredService<PortalDbContext>());
            if (user is null || !user.IsActive)
            {
                context.Fail("User account is disabled.");
                return;
            }

            var issuedStamp = context.Principal?.FindFirstValue(TokenService.SecurityStampClaim);
            if (string.IsNullOrEmpty(issuedStamp)
                || !string.Equals(issuedStamp, user.SecurityStamp, StringComparison.Ordinal))
                context.Fail("Security context has changed.");
        }
    };
});

builder.Services.AddAuthorization(opt =>
{
    opt.AddPolicy("OrchestratorAccess", p => p.RequireRole("Admin", "OrchestratorManager"));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter =
                Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Too many requests. Try again later." },
            cancellationToken);
    };
    options.AddPolicy("auth", context =>
    {
        var limits = context.RequestServices.GetRequiredService<PortalConfig>().RateLimit;
        return CreateFixedWindowPartition(
            context, "auth", limits.AuthPermitLimit, limits.AuthWindowSeconds);
    });
    options.AddPolicy("anonymous-token", context =>
    {
        var limits = context.RequestServices.GetRequiredService<PortalConfig>().RateLimit;
        return CreateFixedWindowPartition(
            context,
            "anonymous-token",
            limits.AnonymousTokenPermitLimit,
            limits.AnonymousTokenWindowSeconds);
    });
});

// ── Swagger ───────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "ETL-SQL Report Portal", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.ParameterLocation.Header
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
var dataProtectionPath = Path.Combine(
    Path.GetDirectoryName(dbPath) ?? AppContext.BaseDirectory,
    ".portal-keys");
Directory.CreateDirectory(dataProtectionPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath))
    .SetApplicationName("ETL-SQL.ReportPortal");
builder.Services.AddScoped<ETL_SQL.ReportPortal.Services.TokenService>();
builder.Services.AddSingleton<ETL_SQL.ReportPortal.Services.UserSecurityStateCache>();
builder.Services.AddScoped<ETL_SQL.ReportPortal.Services.SecuritySessionService>();
builder.Services.AddScoped<ETL_SQL.ReportPortal.Services.AuditService>();
builder.Services.AddScoped<ETL_SQL.ReportPortal.Services.ConfigurationExportService>();
builder.Services.AddScoped<ETL_SQL.ReportPortal.Services.OperationalMetricsService>();
builder.Services.AddScoped<ETL_SQL.ReportPortal.Services.SubscriptionDeliveryStatusService>();
// Trusted subscription executor (P0.1/P0.2): delivery runs in-process with delivery-time
// reauthorization; persisted job scripts are credential-free triggers.
builder.Services.AddScoped<ETL_SQL.ReportPortal.Services.ISubscriptionScriptRunner,
    ETL_SQL.ReportPortal.Services.EngineSubscriptionScriptRunner>();
builder.Services.AddScoped<ETL_SQL.ReportPortal.Services.SubscriptionDeliveryService>();
builder.Services.AddScoped<ETL_SQL.ReportPortal.Services.FolderPermissionService>();
builder.Services.AddScoped<ETL_SQL.ReportPortal.Services.DatasetPermissionService>();
builder.Services.AddScoped<ETL_SQL.ReportPortal.Services.DatasetAtRestKeyRotationService>();
builder.Services.AddScoped<ETL_SQL.ReportPortal.Services.ReportScriptInspectionService>();
builder.Services.AddScoped<IDatasetRegistry, ETL_SQL.ReportPortal.Services.DatasetRegistryService>();
builder.Services.AddScoped<ETL_SQL.ReportPortal.Services.DatasetViewerService>();
builder.Services.AddScoped<ETL_SQL.ReportPortal.Services.ILdapService, ETL_SQL.ReportPortal.Services.LdapService>();
builder.Services.AddSingleton<ETL_SQL.ReportPortal.Services.SmtpPasswordProtector>();
builder.Services.AddSingleton<ETL_SQL.ReportPortal.Services.OrchestratorApiKeyProtector>();
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
        if (!string.IsNullOrWhiteSpace(portalConfig.Orchestrator.ApiKey))
            client.DefaultRequestHeaders.Add("X-Orchestrator-Key", portalConfig.Orchestrator.ApiKey);
    });
}
else
{
    // Fallback to in-process execution (requires IScriptExecutor)
    builder.Services.AddTransient<IScriptExecutor, ETL_SQL.Orchestrator.Execution.ScriptExecutorAdapter>();
    builder.Services.AddSingleton<IJobChannel, InProcessJobChannel>();
}

// Clock abstraction for background maintenance loops; the hosted-service test lane overrides it.
builder.Services.AddSingleton(TimeProvider.System);

// AuditService stamps each row with the request trace identifier as its correlation id.
builder.Services.AddHttpContextAccessor();

// Optional audit retention (Portal:Audit:RetentionDays; disabled by default).
builder.Services.AddHostedService<ETL_SQL.ReportPortal.Services.AuditRetentionService>();

// Phase 2 — execution, session cache, Orchestrator poller
builder.Services.AddSingleton<ETL_SQL.ReportPortal.Services.SessionCache>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ETL_SQL.ReportPortal.Services.SessionCache>());
builder.Services.AddSingleton<ETL_SQL.ReportPortal.Services.ExecutionJobService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ETL_SQL.ReportPortal.Services.ExecutionJobService>());
builder.Services.AddHostedService<ETL_SQL.ReportPortal.Services.OrchestratorPollerService>();

// JWT secret validation (runs after WebApplicationFactory can inject test configuration)
builder.Services.AddHostedService<ETL_SQL.ReportPortal.Services.JwtSecretValidationService>();

// Hourly purge of expired refresh tokens (revoked-but-live rows are kept for reuse detection)
builder.Services.AddHostedService<ETL_SQL.ReportPortal.Services.RefreshTokenMaintenanceService>();

// Dataset at-rest key validation: fail closed if Portal:Dataset:AtRestKey is missing/weak in production
// (unless Portal:Dataset:AllowMachineFallback is deliberately set for dev/standalone).
builder.Services.AddHostedService<ETL_SQL.ReportPortal.Services.DatasetAtRestKeyValidationService>();

// Phase 5 — subscriptions (backed by Orchestrator jobs)

// Phase 6 — health checks
builder.Services.AddHealthChecks()
    .AddCheck<PortalDbHealthCheck>("db", HealthStatus.Unhealthy, ["ready"])
    .AddCheck<OrchestratorHealthCheck>("orchestrator", HealthStatus.Degraded, ["live"])
    .AddCheck<ExecutionCapacityHealthCheck>("execution", HealthStatus.Degraded, ["live"]);

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

// Surface server-side chart SSR failures (engine init / per-chart render) to the logger so a
// missing V8 runtime or a bad chart option is diagnosable instead of silently degrading exports.
ETL_SQL.Reporting.EChartsSsrRenderer.OnError = (message, ex) =>
    app.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("ETL_SQL.Reporting.EChartsSsrRenderer")
        .LogWarning(ex, "{Message}", message);

// Apply EF migrations and enable WAL mode for SQLite on startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
    var migrationLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("PortalDatabaseMigration");
    try
    {
        // Forward-only, automatic on startup/upgrade. Log the applied set so an operator can confirm
        // exactly which schema migrations ran during an upgrade.
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count == 0)
        {
            migrationLogger.LogInformation("Portal database schema is up to date; no migrations to apply.");
        }
        else
        {
            migrationLogger.LogInformation(
                "Applying {Count} pending portal database migration(s): {Migrations}",
                pending.Count, string.Join(", ", pending));
            await db.Database.MigrateAsync();
            migrationLogger.LogInformation("Portal database migrations applied successfully.");
        }
    }
    catch (Exception migrationEx)
    {
        // Fail fast: never serve requests against a half-migrated catalog.
        migrationLogger.LogCritical(migrationEx,
            "Portal database migration failed. The portal will not start. Restore from your pre-upgrade " +
            "backup (rollback is restore-from-backup, not a down-migration) and retry.");
        throw;
    }
    if (db.Database.IsSqlite())
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    await DatasetStorageMaintenance.ReconcileAsync(
        db,
        portalConfig,
        scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DatasetStorageMaintenance"));
    // P0.1/P1.2: rewrite any pre-upgrade subscription script (which embedded decrypted SMTP
    // credentials) to the credential-free trigger form, drop orphaned scripts and temp files,
    // and converge Orchestrator jobs to subscription row state (the source of truth).
    await SubscriptionScriptMaintenance.ReconcileAsync(
        db,
        scope.ServiceProvider.GetRequiredService<PortalConfig>(),
        scope.ServiceProvider.GetRequiredService<OrchestratorDbLocator>().Resolve(),
        scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("SubscriptionScriptMaintenance"),
        scope.ServiceProvider.GetRequiredService<ETL_SQL.Orchestrator.Storage.IOrchestratorStoreFactory>());

    // Resolve PortalConfig from DI (not the locally parsed copy) so test hosts that override the
    // singleton — e.g. to pin FirstRun.AdminPassword — seed with the effective configuration.
    await SeedFirstRunAsync(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<PortalConfig>());
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
app.UseMiddleware<SecurityHeadersMiddleware>();

var staticFileOptions = new StaticFileOptions();
var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
provider.Mappings[".geojson"] = "application/geo+json";
staticFileOptions.ContentTypeProvider = provider;
app.UseStaticFiles(staticFileOptions);
app.UseRouting();
app.UseRateLimiter();
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
            HealthStatus.Healthy => "Healthy",
            HealthStatus.Degraded => "Degraded",
            _ => "Unhealthy"
        };
        var result = new
        {
            status = overall,
            checks = report.Entries.ToDictionary(
                kv => kv.Key,
                kv => new
                {
                    status = kv.Value.Status.ToString(),
                    description = kv.Value.Description,
                    data = kv.Value.Data.Count > 0 ? kv.Value.Data : null,
                    error = kv.Value.Exception?.Message
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
            UserName = adminUsername,
            Email = $"{adminUsername}@localhost",
            IsActive = true,
            MustChangePassword = true
        };
        // Temporary password — must be changed on first login. No hardcoded default: either the
        // operator supplies Portal:FirstRun:AdminPassword or a random one is generated and logged once.
        var password = config.FirstRun.AdminPassword;
        var generated = string.IsNullOrWhiteSpace(password);
        if (generated)
            password = GenerateInitialAdminPassword();

        var result = await userMgr.CreateAsync(admin, password!);
        if (result.Succeeded)
        {
            await userMgr.AddToRoleAsync(admin, "Admin");
            if (generated)
            {
                services.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Portal.FirstRun")
                    .LogWarning(
                        "First-run admin account '{User}' created with generated password: {Password} — " +
                        "log in and change it now, or set Portal:FirstRun:AdminPassword before first start.",
                        adminUsername, password);
            }
        }
    }
}

static string GenerateInitialAdminPassword()
{
    // Fixed prefix deterministically satisfies the Identity policy (upper/lower/digit/length);
    // all entropy lives in the random suffix.
    return "Aa1!" + Convert.ToBase64String(
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(18));
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

static RateLimitPartition<string> CreateFixedWindowPartition(
    HttpContext context,
    string policyName,
    int permitLimit,
    int windowSeconds)
{
    var remoteAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var routePattern = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
        ?? context.Request.Path.Value
        ?? "unknown";
    var partitionKey = $"{policyName}:{remoteAddress}:{routePattern.ToLowerInvariant()}";
    return RateLimitPartition.GetFixedWindowLimiter(
        partitionKey,
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, permitLimit),
            Window = TimeSpan.FromSeconds(Math.Max(1, windowSeconds)),
            QueueLimit = 0,
            AutoReplenishment = true
        });
}
