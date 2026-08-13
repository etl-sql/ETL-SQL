using System;
using System.Collections.Generic;
using System.IO;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Orchestrator.Service;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.Portal.Tests;

public class OrchestratorWebFactory : WebApplicationFactory<OrchestratorMarker>
{
    public const string IdentitySecret = "test-only-federated-orchestrator-identity-secret";
    public string TempDir { get; }
    private readonly bool requireFederatedIdentity;

    public OrchestratorWebFactory(string? tempDir = null, bool requireFederatedIdentity = false)
    {
        this.requireFederatedIdentity = requireFederatedIdentity;
        TempDir = tempDir ?? Path.Combine(Path.GetTempPath(), $"orch_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(TempDir);
        Directory.CreateDirectory(Path.Combine(TempDir, "logs"));
        Directory.CreateDirectory(Path.Combine(TempDir, "scripts"));
        if (!File.Exists(Path.Combine(TempDir, "etlsql.db")))
        {
            File.WriteAllBytes(Path.Combine(TempDir, "etlsql.db"), []);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var orchDbPath = Path.Combine(TempDir, "etlsql.db");
        var scriptRoot = Path.Combine(TempDir, "scripts");

        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orchestrator:DatabasePath"] = orchDbPath,
                ["Orchestrator:ScriptRoot"] = scriptRoot,
                ["Scheduler:MetricsIntervalSeconds"] = "5",
                ["Scheduler:SleepIntervalSeconds"] = "1",
                ["Orchestrator:ApiKey"] = "test-orch-key-12345",
                ["Orchestrator:RequireFederatedIdentity"] = requireFederatedIdentity.ToString(),
                ["Orchestrator:IdentitySigningSecret"] = requireFederatedIdentity ? IdentitySecret : null,
                ["Logging:AppLog:Directory"] = Path.Combine(TempDir, "logs"),
                ["Jobs:UseProcessSpawning"] = "false", // Run jobs in-process during tests
                ["Governance:ConnectionCatalog:Provider"] = "Local",
                ["Governance:ConnectionCatalog:LocalRoot"] = Path.Combine(TempDir, "connections"),
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<SQLiteJobHistoryStore>();
            services.RemoveAll<IJobHistoryStore>();
            services.RemoveAll<IJobCatalogStore>();
            services.RemoveAll<IOrchestratorAuthorizationStore>();
            services.RemoveAll<IHostMetricsStore>();
            services.RemoveAll<ISharedTenantLifecycleStore>();
            services.RemoveAll<IBundleStore>();
            services.RemoveAll<ILineageCatalogStore>();
            services.RemoveAll<ITenantLineageCatalogStore>();
            services.RemoveAll<IConnectionCatalogProvider>();

            var testStore = new SQLiteJobHistoryStore(orchDbPath);
            services.AddSingleton(testStore);
            services.AddSingleton<IJobHistoryStore>(testStore);
            services.AddSingleton<IJobCatalogStore>(testStore);
            services.AddSingleton<IOrchestratorAuthorizationStore>(testStore);
            services.AddSingleton<IHostMetricsStore>(testStore);
            services.AddSingleton<ISharedTenantLifecycleStore>(testStore);
            services.AddSingleton<IBundleStore>(testStore);
            services.AddSingleton<ILineageCatalogStore>(testStore);
            services.AddSingleton<ITenantLineageCatalogStore>(testStore);
            services.AddSingleton<IConnectionCatalogProvider>(
                new LocalConnectionCatalogProvider(Path.Combine(TempDir, "connections")));
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
