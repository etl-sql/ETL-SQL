using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using ETL_SQL.Analysis.Services;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Engine.Services;
using ETL_SQL.Orchestrator;
using ETL_SQL.Orchestrator.Channels;
using ETL_SQL.Portal;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Middleware;
using ETL_SQL.Portal.Services;
using ETL_SQL.Portal.Services.HealthChecks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;

ETL_SQL.Core.Governance.SecurityEventRuntime.ConfigureLocalOutboxFactory(
    new ETL_SQL.Core.Governance.SqliteSecurityEventOutboxFactory());
await ETL_SQL.Core.Governance.EnterprisePolicyRuntime.InitializeFromMachineAsync();
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
builder.Host.UseWindowsService(o => o.ServiceName = "ETL-SQL Portal");
#endif
builder.Configuration.AddSecureConfiguration();
builder.Configuration.AddEnterprisePolicy();

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

// Shared connection-diagnostic core behind the admin "Test connection" button. Stateless; depends
// only on the IConnectorRegistry registered by AddEtlSqlEngine.
builder.Services.AddSingleton<ETL_SQL.Core.Diagnostics.ConnectionDiagnosticEngine>();

// The database-backed secret store only exists inside the Portal host, so this overrides the
// factory-based ISecretProvider from AddEtlSqlEngine (last registration wins). The provider kind
// is read at resolve time because test hosts layer configuration in after Program.cs runs.
builder.Services.AddSingleton<ETL_SQL.Core.Governance.ISecretProvider>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var options = ETL_SQL.Orchestrator.DependencyInjectionExtensions.BuildSecretProviderOptions(config);
    if (string.Equals(options.Provider, "PortalStore", StringComparison.OrdinalIgnoreCase))
        return new ETL_SQL.Portal.Services.PortalStoreSecretProvider(
            sp.GetRequiredService<IServiceScopeFactory>());

    return new ETL_SQL.Core.Governance.SecretProviderFactory(
        ETL_SQL.Core.Governance.PolicyBoundHttp.CreateClient()).Create(options);
});

// Same resolve-time dispatch for the connection catalog: the Portal-backed catalog only exists
// inside this host, and test hosts layer configuration in after Program.cs runs.
builder.Services.AddSingleton<ETL_SQL.Core.Governance.IConnectionCatalogProvider>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var options = ETL_SQL.Orchestrator.DependencyInjectionExtensions.BuildConnectionCatalogOptions(config);
    if (string.Equals(options.Provider, "Portal", StringComparison.OrdinalIgnoreCase))
        return new ETL_SQL.Portal.Services.PortalCatalogConnectionProvider(
            sp.GetRequiredService<IServiceScopeFactory>());

    return ETL_SQL.Core.Governance.ConnectionCatalogProviderFactory.Create(options)
        ?? (ETL_SQL.Core.Governance.IConnectionCatalogProvider)ETL_SQL.Portal.Services.UnconfiguredConnectionCatalogProvider.Instance;
});
builder.Services.AddSingleton<PortalModuleRegistry>();
builder.Services.AddSingleton<ETL_SQL.Core.IMetadataManager, ETL_SQL.Core.Services.MetadataManager>();
builder.Services.AddSingleton<ETL_SQL.Core.Services.ILanguageService, GrammarLanguageService>();

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

// Effective key-ring directory (Data Protection key ring + the Keys artifact area). Configurable so a
// multi-node HA deployment can point every node at the SAME shared location; defaults to the node-local
// .portal-keys beside the portal database. These two must agree so the key root is one place.
var keyRingPath = string.IsNullOrWhiteSpace(portalConfig.Storage.KeyRingPath)
    ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(portalConfig.DatabasePath))!, ".portal-keys")
    : Path.GetFullPath(portalConfig.Storage.KeyRingPath);

// ── Artifact storage (provider configurable: Local default, SMB/UNC for HA) ──────
// Maps each artifact area to its configured root; the Keys area is the Data Protection key ring
// directory beside the portal database. Writes pass through FencedArtifactStorage (P1.8) and then the
// outer GuardedArtifactStorage enforces SecurityService path-traversal / executable /
// script-immutability guardrails before any epoch is stamped (P1.6).
builder.Services.AddSingleton<ETL_SQL.Core.Storage.IArtifactStorage>(sp =>
{
    // Resolve PortalConfig from DI (not the startup-bound local) so any later override — e.g. a test
    // host that replaces the PortalConfig singleton — drives the storage roots too.
    var cfg = sp.GetRequiredService<PortalConfig>();
    var keysRoot = string.IsNullOrWhiteSpace(cfg.Storage.KeyRingPath)
        ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(cfg.DatabasePath))!, ".portal-keys")
        : Path.GetFullPath(cfg.Storage.KeyRingPath);
    var inner = ETL_SQL.Core.Storage.ArtifactStorageFactory.Create(
        cfg.Storage.Provider,
        new Dictionary<ETL_SQL.Core.Storage.ArtifactArea, string>
        {
            [ETL_SQL.Core.Storage.ArtifactArea.Scripts] = cfg.ScriptRootPath,
            [ETL_SQL.Core.Storage.ArtifactArea.Snapshots] = cfg.SnapshotDirectory,
            [ETL_SQL.Core.Storage.ArtifactArea.Maps] = cfg.MapRootPath,
            [ETL_SQL.Core.Storage.ArtifactArea.Datasets] = cfg.DatasetRootPath,
            [ETL_SQL.Core.Storage.ArtifactArea.Keys] = keysRoot,
        });
    var epochs = sp.GetRequiredService<ETL_SQL.Core.Data.IWriteEpochStore>();
    var locks = sp.GetRequiredService<ETL_SQL.Core.Data.IClusterLockStore>();
    var fenced = new ETL_SQL.Core.Storage.FencedArtifactStorage(
        inner, epochs, locks);
    var security = sp.GetRequiredService<ETL_SQL.Services.SecurityService>();
    return new ETL_SQL.Core.Storage.GuardedArtifactStorage(fenced, security);
});

// ── EF Core (provider configurable: SQLite default, Postgres for HA) ────────────
var dbPath = Path.GetFullPath(portalConfig.DatabasePath);
// The fail-closed audit interceptor (P1.12) blocks audited mutations when required remote audit
// delivery is unavailable; it is a no-op unless Portal:Audit:RequireRemoteDelivery is set.
builder.Services.AddSingleton<ETL_SQL.Portal.Services.AuditFailClosedInterceptor>();
builder.Services.AddDbContext<PortalDbContext>((sp, opt) =>
{
    PortalDatabase.Configure(opt, portalConfig);
    opt.AddInterceptors(sp.GetRequiredService<ETL_SQL.Portal.Services.AuditFailClosedInterceptor>());
    // PII column encryption is owned by the context, not a process-global static, so multiple
    // hosts in one process each use their own Data Protection key ring.
    opt.UsePortalEncryption(sp.GetRequiredService<PortalPiiProtector>());
});

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
builder.Services.AddScoped<IPasswordHasher<ServiceAccount>, PasswordHasher<ServiceAccount>>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.ServiceAccountSecurityStateCache>();

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
        // COMPAT_BREAK: 0.14 — tokens minted before iss/aud were stamped are rejected,
        // forcing one re-login (user tokens) or re-mint (short-lived service tokens).
        ValidateIssuer = true,
        ValidIssuer = TokenService.TokenIssuer,
        ValidateAudience = true,
        ValidAudience = TokenService.TokenAudience,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKeys = validationKeys,
        ClockSkew = TimeSpan.FromSeconds(30)
    };
    opt.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var services = context.HttpContext.RequestServices;
            if (context.Principal?.FindFirstValue(TokenService.IdentityTypeClaim) == TokenService.ServiceIdentityType)
            {
                var accountId = context.Principal.FindFirstValue(TokenService.ServiceAccountIdClaim);
                if (string.IsNullOrWhiteSpace(accountId))
                {
                    context.Fail("Invalid service identity.");
                    return;
                }
                var db = services.GetRequiredService<PortalDbContext>();
                var state = await services.GetRequiredService<ETL_SQL.Portal.Services.ServiceAccountSecurityStateCache>()
                    .GetAsync(accountId, db);
                var serviceIssuedStamp = context.Principal.FindFirstValue(TokenService.SecurityStampClaim);
                if (state is null || !state.IsEnabled || state.RevokedAt is not null
                    || (state.ExpiresAt is { } expiresAt && expiresAt <= DateTime.UtcNow)
                    || !string.Equals(serviceIssuedStamp, state.SecurityStamp, StringComparison.Ordinal))
                {
                    context.Fail("Service account is disabled, expired, or revoked.");
                    return;
                }
                var owner = await services.GetRequiredService<ETL_SQL.Portal.Services.UserSecurityStateCache>()
                    .GetAsync(state.OwnerUserId, db);
                if (owner is null || !owner.IsActive)
                    context.Fail("Service account owner is disabled.");
                return;
            }

            var userIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdValue, out var userId))
            {
                context.Fail("Invalid user identity.");
                return;
            }

            var user = await services
                .GetRequiredService<ETL_SQL.Portal.Services.UserSecurityStateCache>()
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
    // Quarantine remediation reads raw failing source rows (which carry whatever PII the source
    // carried), edits them, and enqueues jobs that re-run production loads. That is steward work,
    // not something every authenticated report reader may do.
    opt.AddPolicy("DataQualityStewardAccess", p => p.RequireRole("Admin", "DataSteward"));
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
    options.AddPolicy("designer", context =>
    {
        var limits = context.RequestServices.GetRequiredService<PortalConfig>().RateLimit;
        return CreateFixedWindowPartition(
            context,
            "designer",
            limits.DesignerPermitLimit,
            limits.DesignerWindowSeconds);
    });
    options.AddPolicy("metrics", context =>
    {
        var limits = context.RequestServices.GetRequiredService<PortalConfig>().RateLimit;
        return CreateFixedWindowPartition(
            context, "metrics", limits.MetricsPermitLimit, limits.MetricsWindowSeconds);
    });
});

// ── Swagger ───────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "ETL-SQL Portal", Version = "v1" });
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
// Persist the Data Protection key ring to the shared key-ring directory (see keyRingPath above). In a
// multi-node HA deployment this must be a shared location so every node decrypts the same protected
// secrets; SetApplicationName keeps the ring isolated to this app on a shared volume.
Directory.CreateDirectory(keyRingPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
    .SetApplicationName("ETL-SQL.Portal");
// Context-owned PII protector built from this host's Data Protection key ring. Consumed by the
// PortalDbContext options (UsePortalEncryption) and by startup PII maintenance.
builder.Services.AddSingleton(sp =>
    PortalPiiProtector.Create(sp.GetRequiredService<IDataProtectionProvider>()));
builder.Services.AddScoped<ETL_SQL.Portal.Services.TokenService>();
builder.Services.AddSingleton<ETL_SQL.Portal.Services.UserSecurityStateCache>();
builder.Services.AddSingleton<ETL_SQL.Portal.Services.PortalNodeIdentity>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.SecuritySessionService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.AuditService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.ScriptDagProjectionService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.DesignerSnapshotService>();
builder.Services.AddHttpClient<ETL_SQL.Portal.Services.AuditOutboxTransportService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(Math.Max(1, portalConfig.Audit.TransportTimeoutSeconds));
})
    .ConfigurePrimaryHttpMessageHandler(_ => ETL_SQL.Core.Governance.PolicyBoundHttp.CreateHandler());
builder.Services.AddScoped<ETL_SQL.Portal.Services.ConfigurationExportService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.OperationalMetricsService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.PortalPrometheusMetricsExporter>();
builder.Services.AddSingleton<ETL_SQL.Portal.Services.PortalPrometheusMetricsCache>();
builder.Services.AddSingleton<ETL_SQL.Portal.Services.PortalStorageUsageSampler>();
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<ETL_SQL.Portal.Services.PortalStorageUsageSampler>());
builder.Services.AddScoped<ETL_SQL.Portal.Services.PortalTopologyReadinessService>();
builder.Services.AddSingleton<ETL_SQL.Portal.Services.DocumentationLibraryService>();
// Enterprise policy authority: the signer references a certificate by thumbprint in the OS store
// (no exportable private key persisted); when unset the authority reports "not configured".
builder.Services.AddSingleton<ETL_SQL.Core.Governance.IPolicyEnvelopeSigner>(_ =>
{
    var thumbprint = builder.Configuration["Portal:PolicyAuthority:SigningCertThumbprint"];
    return string.IsNullOrWhiteSpace(thumbprint)
        ? new ETL_SQL.Core.Governance.DisabledPolicyEnvelopeSigner()
        : new ETL_SQL.Core.Governance.CertificatePolicyEnvelopeSigner(thumbprint);
});
builder.Services.AddScoped<ETL_SQL.Core.Governance.IPolicyAuthorityStore, ETL_SQL.Portal.Data.DbPolicyAuthorityStore>();
builder.Services.AddScoped<ETL_SQL.Core.Governance.PolicyAuthorityService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.SubscriptionDeliveryStatusService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.SubscriptionScriptService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.SubscriptionQueryService>();
// Trusted subscription executor (P0.1/P0.2): delivery runs in-process with delivery-time
// reauthorization; persisted job scripts are credential-free triggers.
builder.Services.AddScoped<ETL_SQL.Portal.Services.ISubscriptionScriptRunner,
    ETL_SQL.Portal.Services.EngineSubscriptionScriptRunner>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.SubscriptionDeliveryService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.FolderPermissionService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.DatasetPermissionService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.DatasetAtRestKeyRotationService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.PortalSecretStoreService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.PortalConnectionCatalogService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.DesignerAnalysisService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.DesignerScriptGenerationService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.PortalDesignerSchemaService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.PortalDesignerRunService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.PortalDesignerPreviewService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.PortalScriptSourceControlService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.ReportScriptSaveService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.ReferenceImpactService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.ReportScriptInspectionService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.ReportStructureService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.ReportDependencyService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.LineageImpactService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.LineageStewardNotificationService>();
builder.Services.AddSingleton<ETL_SQL.Portal.Services.SnapshotPackageService>();
builder.Services.AddSingleton<ETL_SQL.Portal.Services.PortalAlertEvaluationService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.DatasetRegistryService>();
builder.Services.AddScoped<IDatasetRegistry>(sp =>
    ETL_SQL.Core.Observability.DatasetObservability.Instrument(
        sp.GetRequiredService<ETL_SQL.Portal.Services.DatasetRegistryService>()));
builder.Services.AddScoped<ETL_SQL.Portal.Services.DatasetQueryService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.DatasetExportService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.DatasetRefreshService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.DatasetMoveService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.DatasetAclService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.DatasetUpdateService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.DatasetDeleteService>();
builder.Services.AddSingleton<ETL_SQL.Portal.Services.DatasetPreviewCache>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.DatasetViewerService>();
builder.Services.AddScoped<ETL_SQL.Portal.Services.ILdapService, ETL_SQL.Portal.Services.LdapService>();

// ── Federated OIDC login (Portal:Identity:Oidc) ───────────────────────────────
// Discovery is a singleton so its ConfigurationManager caches the well-known doc/JWKS across
// requests; the auth service and provisioning bridge mint the portal's own JWT/refresh session.
builder.Services.AddHttpClient("oidc-discovery", c => c.Timeout = TimeSpan.FromSeconds(10))
    .ConfigurePrimaryHttpMessageHandler(_ => ETL_SQL.Core.Governance.PolicyBoundHttp.CreateHandler());
builder.Services.AddSingleton<ETL_SQL.Portal.Services.IOidcDiscoveryProvider>(sp =>
    new ETL_SQL.Portal.Services.OidcDiscoveryProvider(
        portalConfig, sp.GetRequiredService<IHttpClientFactory>().CreateClient("oidc-discovery")));
builder.Services.AddHttpClient<ETL_SQL.Portal.Services.IOidcAuthenticationService,
    ETL_SQL.Portal.Services.OidcAuthenticationService>()
    .ConfigurePrimaryHttpMessageHandler(_ => ETL_SQL.Core.Governance.PolicyBoundHttp.CreateHandler());
builder.Services.AddScoped<ETL_SQL.Portal.Services.OidcUserProvisioningService>();

// Read-only fleet health aggregation (P2.2): fans out to each environment's GET /api/fleet/status
// with a scoped FleetReader token. Registered so an aggregator host can resolve it.
builder.Services.AddHttpClient<ETL_SQL.Portal.Services.FleetHealthAggregator>(client =>
    client.Timeout = TimeSpan.FromSeconds(10))
    .ConfigurePrimaryHttpMessageHandler(_ => ETL_SQL.Core.Governance.PolicyBoundHttp.CreateHandler());
builder.Services.AddSingleton<ETL_SQL.Portal.Services.OrchestratorApiKeyProtector>();
builder.Services.AddSingleton<ETL_SQL.Portal.Services.OrchestratorDbLocator>();
builder.Services.AddSingleton<ETL_SQL.Portal.Services.PortalBrandingSettingsService>();

// ── Orchestrator Management Proxy ─────────────────────────────────────────────
// OrchestratorSettingsService holds the active URL/key and persists UI-configured
// overrides so the admin can point the portal at a different host without a restart.
builder.Services.AddSingleton<ETL_SQL.Portal.Services.OrchestratorSettingsService>();
builder.Services.AddHttpClient<ETL_SQL.Portal.Services.OrchestratorProxyService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    // No base address — OrchestratorProxyService builds absolute URIs from OrchestratorSettingsService.
})
    .ConfigurePrimaryHttpMessageHandler(_ => ETL_SQL.Core.Governance.PolicyBoundHttp.CreateHandler());

// ── Orchestrator Channel ──────────────────────────────────────────────────────
if (!string.IsNullOrEmpty(portalConfig.Orchestrator.ApiUrl))
{
    builder.Services.AddHttpClient<IJobChannel, HttpJobChannelClient>(client =>
    {
        client.BaseAddress = new Uri(portalConfig.Orchestrator.ApiUrl);
        if (!string.IsNullOrWhiteSpace(portalConfig.Orchestrator.ApiKey))
            client.DefaultRequestHeaders.Add("X-Orchestrator-Key", portalConfig.Orchestrator.ApiKey);
    })
        .ConfigurePrimaryHttpMessageHandler(_ => ETL_SQL.Core.Governance.PolicyBoundHttp.CreateHandler());
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
builder.Services.AddHostedService<ETL_SQL.Portal.Services.AuditRetentionService>();
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<ETL_SQL.Portal.Services.AuditOutboxTransportService>());

// Optional operational workers. Portal:Modules gates the whole operations surface; each worker's
// own Enabled flag still controls whether it performs work after registration.
// Optional scheduled operational-metrics digest email (Portal:OperationalDigest; disabled by default).
// HA-safe: a cluster lock ensures exactly one node sends per interval.
AddPortalModuleHostedService<ETL_SQL.Portal.Services.OperationalMetricsDigestService>(
    builder.Services,
    "Operations",
    modules => modules.IsEnabled("Operations"));

// Native admin services (Portal:AdminServices; all disabled by default) — managed replacements for
// the samples/admin_operations scheduler scripts. Same HA cluster-lock cadence as the digest above.
builder.Services.AddScoped<ETL_SQL.Portal.Services.IAdminNotificationSender,
    ETL_SQL.Portal.Services.SmtpAdminNotificationSender>();
AddPortalModuleHostedService<ETL_SQL.Portal.Services.FailureDigestAdminService>(
    builder.Services,
    "Operations",
    modules => modules.IsEnabled("Operations"));
AddPortalModuleHostedService<ETL_SQL.Portal.Services.BackupReportAdminService>(
    builder.Services,
    "Operations",
    modules => modules.IsEnabled("Operations"));
AddPortalModuleHostedService<ETL_SQL.Portal.Services.CapacityReportAdminService>(
    builder.Services,
    "Operations",
    modules => modules.IsEnabled("Operations"));

// Phase 2 — execution, session cache, Orchestrator poller
builder.Services.AddSingleton<ETL_SQL.Portal.Services.SessionCache>();
builder.Services.AddSingleton<ETL_SQL.Portal.Services.ExecutionJobService>();
builder.Services.AddSingleton<ETL_SQL.Orchestrator.Scheduling.INodeLeaseLossHandler>(
    sp => sp.GetRequiredService<ETL_SQL.Portal.Services.ExecutionJobService>());
AddPortalModuleHostedService<ETL_SQL.Portal.Services.SessionCache>(
    builder.Services,
    "Reporting",
    modules => modules.IsEnabled("Reporting"),
    sp => sp.GetRequiredService<ETL_SQL.Portal.Services.SessionCache>());
AddPortalModuleHostedService<ETL_SQL.Portal.Services.ExecutionJobService>(
    builder.Services,
    "Reporting",
    modules => modules.IsEnabled("Reporting"),
    sp => sp.GetRequiredService<ETL_SQL.Portal.Services.ExecutionJobService>());
AddPortalModuleHostedService<ETL_SQL.Portal.Services.OrchestratorPollerService>(
    builder.Services,
    "Scheduling",
    modules => modules.IsEnabled("Reporting") && modules.IsEnabled("Scheduling"));

// JWT secret validation (runs after WebApplicationFactory can inject test configuration)
builder.Services.AddHostedService<ETL_SQL.Portal.Services.JwtSecretValidationService>();

// OIDC configuration validation: fail closed if Portal:Identity:Oidc is enabled but misconfigured.
builder.Services.AddHostedService<ETL_SQL.Portal.Services.OidcConfigValidationService>();

// Hourly purge of expired refresh tokens (revoked-but-live rows are kept for reuse detection)
builder.Services.AddHostedService<ETL_SQL.Portal.Services.RefreshTokenMaintenanceService>();

// Dataset at-rest key validation: fail closed if Portal:Dataset:AtRestKey is missing/weak in production
// (unless Portal:Dataset:AllowMachineFallback is deliberately set for dev/standalone).
AddPortalModuleHostedService<ETL_SQL.Portal.Services.DatasetAtRestKeyValidationService>(
    builder.Services,
    "Reporting",
    modules => modules.IsEnabled("Reporting"));
AddPortalModuleHostedService<ETL_SQL.Portal.Services.SnapshotMigrationService>(
    builder.Services,
    "Reporting",
    modules => modules.IsEnabled("Reporting"));

// Phase 5 — subscriptions (backed by Orchestrator jobs)

// Phase 6 — health checks
builder.Services.AddHealthChecks()
    .AddCheck<PortalDbHealthCheck>("db", HealthStatus.Unhealthy, ["ready"])
    .AddCheck<OrchestratorHealthCheck>("orchestrator", HealthStatus.Degraded, ["live"])
    .AddCheck<ExecutionCapacityHealthCheck>("execution", HealthStatus.Degraded, ["live"])
    .AddCheck<ETL_SQL.Portal.Services.HealthChecks.PolicyAuthorityHealthCheck>(
        "policy-authority", HealthStatus.Degraded, ["live"])
    .AddCheck<ETL_SQL.Portal.Services.HealthChecks.SecretStoreKeyRingHealthCheck>(
        "secret-store-keyring", HealthStatus.Unhealthy, ["ready"]);

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

if (Directory.Exists(app.Environment.WebRootPath))
{
    try
    {
        AssetFingerprinter.Apply(app.Environment.WebRootPath, ETL_SQL.Common.LanguageMetadata.EngineVersion);
    }
    catch (Exception ex)
    {
        app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("AssetFingerprinter")
            .LogWarning(ex, "Asset fingerprinter failed: {Message}", ex.Message);
    }
}

// Surface server-side chart SSR failures (engine init / per-chart render) to the logger so a
// missing V8 runtime or a bad chart option is diagnosable instead of silently degrading exports.
ETL_SQL.Reporting.EChartsSsrRenderer.OnError = (message, ex) =>
    app.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("ETL_SQL.Reporting.EChartsSsrRenderer")
        .LogWarning(ex, "{Message}", message);

// Apply EF migrations and startup catalog maintenance. In HA, the full database-mutating startup
// block must be serialized, not only EF migrations: first-run seed and reconciliation are also
// shared-catalog writes.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
    var migrationLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("PortalDatabaseMigration");
    try
    {
        // HA: serialize startup database writes at the Portal database session boundary. Several
        // Portal nodes can boot together against one PostgreSQL catalog; a provider-native advisory
        // lock ensures only one applies DDL, reconciliation, and first-run seed at a time.
        await PortalDatabaseMigrationLock.RunExclusiveAsync(
            db,
            migrationLogger,
            criticalSection: async () =>
            {
                // Forward-only, automatic on startup/upgrade. Log the applied set so an operator can
                // confirm exactly which schema migrations ran during an upgrade.
                var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
                PortalDatabaseMigrationLock.ReportProgress("MigrationCheck", pending.Count);
                if (pending.Count == 0)
                {
                    migrationLogger.LogInformation("Portal database schema is up to date; no migrations to apply.");
                }
                else
                {
                    PortalDatabaseMigrationLock.ReportProgress("ApplyingMigrations", pending.Count);
                    migrationLogger.LogInformation(
                        "Applying {Count} pending portal database migration(s): {Migrations}",
                        pending.Count, string.Join(", ", pending));
                    await db.Database.MigrateAsync();
                    PortalDatabaseMigrationLock.ReportProgress("MigrationsApplied", 0);
                    migrationLogger.LogInformation("Portal database migrations applied successfully.");
                }

                if (db.Database.IsSqlite())
                    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
                await PiiColumnEncryptionMaintenance.EncryptExistingPlaintextAsync(
                    db,
                    scope.ServiceProvider.GetRequiredService<PortalPiiProtector>(),
                    scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("PiiColumnEncryptionMaintenance"));
                if (scope.ServiceProvider.GetRequiredService<PortalModuleRegistry>().IsEnabled("Reporting"))
                {
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
                }

                // Resolve PortalConfig from DI (not the locally parsed copy) so test hosts that override the
                // singleton — e.g. to pin FirstRun.AdminPassword — seed with the effective configuration.
                await SeedFirstRunAsync(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<PortalConfig>());
            },
            ownerNodeId: scope.ServiceProvider.GetRequiredService<PortalNodeIdentity>().NodeId);
    }
    catch (Exception migrationEx)
    {
        // Fail fast: never serve requests against a half-migrated catalog.
        migrationLogger.LogCritical(migrationEx,
            "Portal database migration failed. The portal will not start. Restore from your pre-upgrade " +
            "backup (rollback is restore-from-backup, not a down-migration) and retry.");
        throw;
    }
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
    var correlationId = context.TraceIdentifier;
    var traceId = Activity.Current?.TraceId.ToString();
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.TryAdd("X-Correlation-ID", correlationId);
        return Task.CompletedTask;
    });

    using (app.Logger.BeginScope(new Dictionary<string, object?>
    {
        [ETL_SQL.Core.Observability.ObservabilityConventions.Tags.CorrelationId] = correlationId,
        ["trace_id"] = traceId
    }))
    {
        await next();
    }
});

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0");
    context.Response.Headers.Append("Pragma", "no-cache");
    await next();
});
// Fail-closed audit policy (P1.12): a blocked mutation surfaces as 503 Service Unavailable so the
// client retries once the audit collector is reachable, rather than as an opaque 500.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (ETL_SQL.Portal.Services.AuditDeliveryUnavailableException ex)
    {
        app.Logger.LogError(ex, "Mutation blocked by fail-closed audit delivery policy");
        if (!context.Response.HasStarted)
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = "30";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "audit_delivery_unavailable",
                message = ex.Message
            });
        }
    }
});
app.Use(async (context, next) =>
{
    var cfg = context.RequestServices.GetRequiredService<PortalConfig>();
    if (cfg.LoadBalancer.SessionAffinityEnabled)
    {
        var node = context.RequestServices.GetRequiredService<ETL_SQL.Portal.Services.PortalNodeIdentity>();
        var name = string.IsNullOrWhiteSpace(cfg.LoadBalancer.SessionAffinityCookieName)
            ? "ETLSQL_PORTAL_AFFINITY"
            : cfg.LoadBalancer.SessionAffinityCookieName.Trim();
        var maxAge = TimeSpan.FromMinutes(Math.Max(1, cfg.LoadBalancer.SessionAffinityCookieMinutes));
        context.Response.OnStarting(() =>
        {
            context.Response.Cookies.Append(name, node.NodeId, new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                MaxAge = maxAge,
                Path = "/",
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps
            });
            return Task.CompletedTask;
        });
    }

    await next();
});
app.UseMiddleware<SecurityHeadersMiddleware>();

var staticFileOptions = new StaticFileOptions();
var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
provider.Mappings[".geojson"] = "application/geo+json";
staticFileOptions.ContentTypeProvider = provider;
app.Use(async (context, next) =>
{
    if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
    {
        var modules = context.RequestServices.GetRequiredService<PortalModuleRegistry>();
        var path = context.Request.Path.Value ?? string.Empty;
        var isReportEntry = string.Equals(path, "/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "/index.html", StringComparison.OrdinalIgnoreCase);
        var isDesignerEntry = string.Equals(path, "/designer.html", StringComparison.OrdinalIgnoreCase);
        var isDocsEntry = string.Equals(path, "/docs.html", StringComparison.OrdinalIgnoreCase);

        if ((isReportEntry && !modules.IsEnabled("Reporting"))
            || (isDesignerEntry && !modules.IsEnabled("Designer")))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (isDocsEntry && !modules.IsEnabled("Documentation"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
    }

    await next();
});
app.UseStaticFiles(staticFileOptions);
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<ETL_SQL.Portal.Middleware.ServiceAccountScopeMiddleware>();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true
        && context.User.IsInRole("FleetReader")
        && !context.User.IsInRole("Admin"))
    {
        var isFleetStatusRequest =
            HttpMethods.IsGet(context.Request.Method)
            && string.Equals(
                context.Request.Path.Value,
                "/api/fleet/status",
                StringComparison.OrdinalIgnoreCase);

        if (!isFleetStatusRequest)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "fleet_reader_scope_violation" });
            return;
        }
    }

    await next();
});
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

// Lightweight load-balancer probe — intentionally narrower than /health. It checks only the
// dependencies required for this node to safely accept traffic: portal DB, shared artifact storage,
// and the node-registry/lease store.
app.MapGet("/healthz", async (
    ETL_SQL.Portal.Services.PortalTopologyReadinessService readiness,
    CancellationToken ct) =>
{
    var result = await readiness.CheckAsync(ct);
    var healthy = string.Equals(result.Status, "Healthy", StringComparison.OrdinalIgnoreCase);
    return Results.Json(
        new
        {
            status = result.Status,
            mode = result.Mode,
            checks = result.Checks,
            findings = result.Findings
        },
        statusCode: healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
}).AllowAnonymous();

app.MapGet("/metrics", async (
    ETL_SQL.Portal.Services.PortalPrometheusMetricsCache cache,
    CancellationToken ct) =>
{
    var text = await cache.GetAsync(ct);
    return Results.Text(text, "text/plain; version=0.0.4; charset=utf-8");
}).AllowAnonymous().RequireRateLimiting("metrics");

app.MapGet("/third-party-notices", () =>
{
    var noticesPath = FindRepoFile("THIRD-PARTY-NOTICES.md");
    return noticesPath is null
        ? Results.NotFound("THIRD-PARTY-NOTICES.md was not found.")
        : Results.Text(File.ReadAllText(noticesPath), "text/markdown; charset=utf-8");
}).AllowAnonymous();

app.MapGet("/docs", (ETL_SQL.Portal.Services.PortalModuleRegistry modules) =>
    modules.IsEnabled("Documentation")
        ? Results.Redirect("/docs.html")
        : Results.NotFound(new { error = "documentation_disabled" }))
   .AllowAnonymous();

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

    // FleetReader: a scoped, read-only role for the fleet aggregator — authorizes only
    // GET /api/fleet/status and nothing else (see Departmental_Isolation.md fleet trust boundary).
    foreach (var role in new[] { "Admin", "Publisher", "Viewer", "OrchestratorManager", "FleetReader", "GovernanceViewer", "DataSteward", "GovernanceManager" })
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
            MustChangePassword = config.FirstRun.MustChangePassword
        };
        var password = config.FirstRun.AdminPassword;
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Portal first-run admin password is not configured. Set Portal:FirstRun:AdminPassword " +
                "or Portal__FirstRun__AdminPassword before starting an empty Portal database.");
        }

        var result = await userMgr.CreateAsync(admin, password);
        if (result.Succeeded)
        {
            await userMgr.AddToRoleAsync(admin, "Admin");
            services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Portal.FirstRun")
                .LogWarning(
                    "First-run admin account '{User}' created. Log in with the configured bootstrap password and change it now.",
                    adminUsername);
        }
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

static void AddPortalModuleHostedService<TService>(
    IServiceCollection services,
    string moduleName,
    Func<PortalModuleRegistry, bool> isEnabled,
    Func<IServiceProvider, TService>? factory = null)
    where TService : class, IHostedService
{
    services.AddSingleton<IHostedService>(sp =>
    {
        var modules = sp.GetRequiredService<PortalModuleRegistry>();
        if (!isEnabled(modules))
            return new DisabledPortalModuleHostedService(moduleName, typeof(TService).Name);

        return factory is null
            ? ActivatorUtilities.CreateInstance<TService>(sp)
            : factory(sp);
    });
}
