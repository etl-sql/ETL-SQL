using System.IO;
using System.Text;
using ETL_SQL.ReportPortal;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
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

namespace ETL_SQL.ReportPortal.Tests;

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
        File.WriteAllBytes(Path.Combine(TempDir, "etlsql.db"), []);
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
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
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
                ["Portal:FirstRun:AdminUsername"] = "admin",
                ["Portal:FirstRun:AdminPassword"] = "Admin@12345!",
                ["Portal:Resources:MaxConcurrentReportExecutions"] = "2",
                ["Portal:Resources:ExecutionTimeoutSeconds"] = "30",
                ["Portal:Resources:SessionCacheMaxSize"] = "10",
                ["Portal:Resources:SessionCacheTtlMinutes"] = "5",
                ["Portal:Orchestrator:DatabasePath"] = orchDbPath,
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace DbContext with test-specific SQLite
            services.RemoveAll<DbContextOptions<PortalDbContext>>();
            services.RemoveAll<PortalDbContext>();
            services.AddDbContext<PortalDbContext>(opt =>
                opt.UseSqlite($"Data Source={dbPath}"));

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
                FirstRun = new FirstRunConfig { AdminUsername = "admin", AdminPassword = "Admin@12345!" },
                Orchestrator = new OrchestratorConfig { DatabasePath = orchDbPath },
            };
            services.AddSingleton(cfg);
            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(TempDir, "keys")))
                .SetApplicationName("ETL-SQL.ReportPortal.Tests");

            // Override JWT signing key to match our test secret
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, opt =>
            {
                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
                opt.TokenValidationParameters.IssuerSigningKey = key;
            });

            // Replace the global SQLiteJobHistoryStore (DefaultDbPath) with an isolated test db
            // so concurrent test factories don't race on the shared LocalApplicationData file.
            services.RemoveAll<ETL_SQL.Orchestrator.Storage.SQLiteJobHistoryStore>();
            services.RemoveAll<ETL_SQL.Core.Data.IJobHistoryStore>();
            services.RemoveAll<ETL_SQL.Core.Data.IBundleStore>();
            services.RemoveAll<ETL_SQL.Core.Data.ILineageCatalogStore>();
            var testStore = new ETL_SQL.Orchestrator.Storage.SQLiteJobHistoryStore(orchDbPath);
            services.AddSingleton(testStore);
            services.AddSingleton<ETL_SQL.Core.Data.IJobHistoryStore>(testStore);
            services.AddSingleton<ETL_SQL.Core.Data.IBundleStore>(testStore);
            services.AddSingleton<ETL_SQL.Core.Data.ILineageCatalogStore>(testStore);

            // Disable the JWT validation hosted service (no longer needed — config is injected)
            services.RemoveAll<IHostedService>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(TempDir))
        {
            try { Directory.Delete(TempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
