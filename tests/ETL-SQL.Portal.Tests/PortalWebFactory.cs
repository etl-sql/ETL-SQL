using System.IO;
using System.Text;
using ETL_SQL.Portal;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Creates an in-process portal host backed by a temp-directory SQLite database.
/// Uses WebApplicationFactory with ConfigureWebHost to inject test configuration
/// rather than relying on entry-point discovery (which would pick up ETL-SQL.App via
/// the ReportPlayer → App transitive reference).
/// </summary>
public class PortalWebFactory : WebApplicationFactory<PortalMarker>
{
    public string TempDir { get; } = Path.Combine(Path.GetTempPath(), $"portal_test_{Guid.NewGuid():N}");
    private readonly int authPermitLimit;
    private readonly int anonymousTokenPermitLimit;

    public PortalWebFactory() : this(500, 500)
    {
    }

    internal PortalWebFactory(int authPermitLimit, int anonymousTokenPermitLimit)
    {
        this.authPermitLimit = authPermitLimit;
        this.anonymousTokenPermitLimit = anonymousTokenPermitLimit;
        Directory.CreateDirectory(TempDir);
        Directory.CreateDirectory(Path.Combine(TempDir, "scripts"));
        Directory.CreateDirectory(Path.Combine(TempDir, "snapshots"));
        Directory.CreateDirectory(Path.Combine(TempDir, "maps"));
        Directory.CreateDirectory(Path.Combine(TempDir, "datasets"));
        Directory.CreateDirectory(Path.Combine(TempDir, "keys"));
        Directory.CreateDirectory(Path.Combine(TempDir, "security"));
        File.WriteAllBytes(Path.Combine(TempDir, "etlsql.db"), []);

        // The security-event outbox otherwise defaults to a MACHINE-WIDE SQLite file under
        // LocalApplicationData, shared by every ETL-SQL process on the host. Program.cs opens it
        // before the host is built, so a previous test process still shutting down could hold it and
        // the next host would fail to start at all — every test in the assembly reporting
        // "The server has not been started" in about a millisecond. Pointing each factory at its own
        // file removes the contention rather than timing around it.
        //
        // Worth noting the same sharing exists in production: two Portal or Orchestrator processes on
        // one host contend for this file too.
        Environment.SetEnvironmentVariable(
            ETL_SQL.Core.Governance.SecurityEventOutboxPaths.StandaloneOverrideEnvironmentVariable,
            Path.Combine(TempDir, "security", "security-events.db"));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var dbPath = Path.Combine(TempDir, "portal.db");
        var scriptRoot = Path.Combine(TempDir, "scripts");
        var snapshotDir = Path.Combine(TempDir, "snapshots");
        var mapRoot = Path.Combine(TempDir, "maps");
        var datasetRoot = Path.Combine(TempDir, "datasets");
        var orchDbPath = Path.Combine(TempDir, "etlsql.db");
        const string jwtSecret = "integration-test-secret-key-1234567890";

        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Portal:DatabasePath"] = dbPath,
                ["Portal:ScriptRootPath"] = scriptRoot,
                ["Portal:SnapshotDirectory"] = snapshotDir,
                ["Portal:MapRootPath"] = mapRoot,
                ["Portal:DatasetRootPath"] = datasetRoot,
                ["Portal:Jwt:Secret"] = jwtSecret,
                ["Portal:Jwt:ExpiryMinutes"] = "60",
                ["Portal:Jwt:RefreshExpiryDays"] = "7",
                ["Portal:RateLimit:AuthPermitLimit"] = authPermitLimit.ToString(),
                ["Portal:RateLimit:AnonymousTokenPermitLimit"] = anonymousTokenPermitLimit.ToString(),
                ["Portal:Dataset:AtRestKey"] = HostedPortalFactory.DefaultAtRestKey,
                ["Portal:Dataset:AtRestKeyVersion"] = "v1",
                ["Portal:FirstRun:AdminUsername"] = "admin",
                ["Portal:FirstRun:AdminPassword"] = "Admin@12345!",
                ["Portal:Resources:MaxConcurrentReportExecutions"] = "2",
                ["Portal:Resources:ExecutionTimeoutSeconds"] = "30",
                ["Portal:Resources:SessionCacheMaxSize"] = "10",
                ["Portal:Resources:SessionCacheTtlMinutes"] = "5",
                ["Portal:Orchestrator:DatabasePath"] = orchDbPath,
            };
            CustomizeConfiguration(settings);
            cfg.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            // Replace DbContext with test-specific SQLite. RemoveAll of the options/context services
            // alone leaves the production IDbContextOptionsConfiguration in place, so both it and this
            // registration would apply — an implicit double-configuration. Strip it and re-add the
            // configuration explicitly: test SQLite, the audit interceptor, and context-owned PII
            // encryption (matching production, so the DI context decrypts what startup PII maintenance
            // encrypts). Tests that hand-build a PortalDbContext over the same database must likewise
            // opt into UsePortalEncryption with the host's PortalPiiProtector.
            services.RemoveAll<DbContextOptions<PortalDbContext>>();
            services.RemoveAll<PortalDbContext>();
            services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<PortalDbContext>>();
            services.AddDbContext<PortalDbContext>((sp, opt) =>
            {
                opt.UseSqlite($"Data Source={dbPath}");
                opt.AddInterceptors(sp.GetRequiredService<AuditFailClosedInterceptor>());
                opt.UsePortalEncryption(sp.GetRequiredService<PortalPiiProtector>());
            });

            // Override PortalConfig singleton with test values
            services.RemoveAll<PortalConfig>();
            var cfg = new PortalConfig
            {
                DatabasePath = dbPath,
                ScriptRootPath = scriptRoot,
                SnapshotDirectory = snapshotDir,
                MapRootPath = mapRoot,
                DatasetRootPath = datasetRoot,
                Jwt = new JwtConfig { Secret = jwtSecret, ExpiryMinutes = 60, RefreshExpiryDays = 7 },
                RateLimit = new PortalRateLimitConfig
                {
                    AuthPermitLimit = authPermitLimit,
                    AnonymousTokenPermitLimit = anonymousTokenPermitLimit
                },
                Dataset = new DatasetConfig
                {
                    AtRestKey = HostedPortalFactory.DefaultAtRestKey,
                    AtRestKeyVersion = "v1"
                },
                FirstRun = new FirstRunConfig { AdminUsername = "admin", AdminPassword = "Admin@12345!" },
                Orchestrator = new OrchestratorConfig { DatabasePath = orchDbPath },
                Studio = new PortalStudioConfig
                {
                    Mode = StudioDeploymentMode.SourceControlled,
                    RoleCapabilities = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Admin"] = StudioCapabilities.All.ToList(),
                        ["Publisher"] = StudioCapabilities.All.Where(capability => capability != StudioCapabilities.SourcePush).ToList()
                    }
                },
            };
            CustomizePortalConfig(cfg);
            services.AddSingleton(cfg);
            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(TempDir, "keys")))
                .SetApplicationName("ETL-SQL.Portal.Tests");

            // Override JWT signing key to match our test secret
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, opt =>
            {
                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
                opt.TokenValidationParameters.IssuerSigningKey = key;
            });

            // Replace the global SQLiteJobHistoryStore (DefaultDbPath) with an isolated test db
            // so concurrent test factories don't race on the shared LocalApplicationData file.
            services.RemoveAll<ETL_SQL.Orchestrator.Storage.RelationalJobHistoryStore>();
            services.RemoveAll<ETL_SQL.Orchestrator.Storage.SQLiteJobHistoryStore>();
            services.RemoveAll<ETL_SQL.Core.Data.IJobHistoryStore>();
            services.RemoveAll<ETL_SQL.Core.Data.IBundleStore>();
            services.RemoveAll<ETL_SQL.Core.Data.ILineageCatalogStore>();
            services.RemoveAll<ETL_SQL.Core.Data.INodeRegistryStore>();
            services.RemoveAll<ETL_SQL.Core.Data.IWriteEpochStore>();
            services.RemoveAll<ETL_SQL.Core.Data.IClusterLockStore>();
            var testStore = new ETL_SQL.Orchestrator.Storage.SQLiteJobHistoryStore(orchDbPath);
            services.AddSingleton(testStore);
            services.AddSingleton<ETL_SQL.Orchestrator.Storage.RelationalJobHistoryStore>(testStore);
            services.AddSingleton<ETL_SQL.Core.Data.IJobHistoryStore>(testStore);
            services.AddSingleton<ETL_SQL.Core.Data.IBundleStore>(testStore);
            services.AddSingleton<ETL_SQL.Core.Data.ILineageCatalogStore>(testStore);
            services.AddSingleton<ETL_SQL.Core.Data.INodeRegistryStore>(testStore);
            services.AddSingleton<ETL_SQL.Core.Data.IWriteEpochStore>(testStore);
            services.AddSingleton<ETL_SQL.Core.Data.IClusterLockStore>(testStore);
            services.RemoveAll<ETL_SQL.Orchestrator.Scheduling.INodeCapacityMonitor>();
            services.AddSingleton<ETL_SQL.Orchestrator.Scheduling.INodeCapacityMonitor>(new PortalTestCapacityMonitor());

            ConfigureHostedServices(services);
            CustomizeServices(services);
        });
    }

    /// <summary>Last-chance hook over the service collection (e.g. swapping the policy signer).</summary>
    protected virtual void CustomizeServices(IServiceCollection services)
    {
    }

    /// <summary>Last-chance hook over the in-memory configuration before it is added.</summary>
    protected virtual void CustomizeConfiguration(Dictionary<string, string?> settings)
    {
    }

    /// <summary>Last-chance hook over the <see cref="PortalConfig"/> singleton before registration.</summary>
    protected virtual void CustomizePortalConfig(PortalConfig config)
    {
    }

    /// <summary>
    /// Ordinary API tests strip every hosted service so requests run without background loops or
    /// startup validators. The hosted-service lane (<see cref="HostedPortalFactory"/>) overrides
    /// this to keep the full pipeline running against the same isolated databases.
    /// </summary>
    protected virtual void ConfigureHostedServices(IServiceCollection services)
        => services.RemoveAll<IHostedService>();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(TempDir))
        {
            try { Directory.Delete(TempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}

internal sealed class PortalTestCapacityMonitor : ETL_SQL.Orchestrator.Scheduling.INodeCapacityMonitor
{
    public ETL_SQL.Orchestrator.Scheduling.NodeCapacitySnapshot Capture() => new(
        WorkingSetBytes: 50 * 1024 * 1024,
        GcHeapBytes: 20 * 1024 * 1024,
        TotalAvailableMemoryBytes: 1024L * 1024 * 1024 * 8,
        MemoryLoadPercent: 10.0,
        ProcessCpuPercent: 5.0,
        ProcessorCount: Environment.ProcessorCount,
        IsOverloaded: false,
        CapturedAtUtc: DateTime.UtcNow);
}
