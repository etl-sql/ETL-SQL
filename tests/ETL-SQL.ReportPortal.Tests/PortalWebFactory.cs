using System.IO;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using ETL_SQL.ReportPortal;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;

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

    public PortalWebFactory()
    {
        Directory.CreateDirectory(TempDir);
        Directory.CreateDirectory(Path.Combine(TempDir, "scripts"));
        Directory.CreateDirectory(Path.Combine(TempDir, "snapshots"));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var dbPath       = Path.Combine(TempDir, "portal.db");
        var scriptRoot   = Path.Combine(TempDir, "scripts");
        var snapshotDir  = Path.Combine(TempDir, "snapshots");
        const string jwtSecret = "integration-test-secret-key-1234567890";

        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Portal:DatabasePath"]           = dbPath,
                ["Portal:ScriptRootPath"]         = scriptRoot,
                ["Portal:SnapshotDirectory"]      = snapshotDir,
                ["Portal:Jwt:Secret"]             = jwtSecret,
                ["Portal:Jwt:ExpiryMinutes"]      = "60",
                ["Portal:Jwt:RefreshExpiryDays"]  = "7",
                ["Portal:FirstRun:AdminUsername"] = "admin",
                ["Portal:Resources:MaxConcurrentReportExecutions"] = "2",
                ["Portal:Resources:ExecutionTimeoutSeconds"]       = "30",
                ["Portal:Resources:SessionCacheMaxSize"]           = "10",
                ["Portal:Resources:SessionCacheTtlMinutes"]        = "5",
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
                DatabasePath      = dbPath,
                ScriptRootPath    = scriptRoot,
                SnapshotDirectory = snapshotDir,
                Jwt = new JwtConfig { Secret = jwtSecret, ExpiryMinutes = 60, RefreshExpiryDays = 7 },
                FirstRun          = new FirstRunConfig { AdminUsername = "admin" },
            };
            services.AddSingleton(cfg);

            // Override JWT signing key to match our test secret
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, opt =>
            {
                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
                opt.TokenValidationParameters.IssuerSigningKey = key;
            });

            // Disable the JWT validation hosted service (no longer needed — config is injected)
            services.RemoveAll<IHostedService>();
            // Re-add only the hosted services we want (exclude JwtSecretValidationService)
            // SessionCache and OrchestratorPollerService will be re-added below
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
