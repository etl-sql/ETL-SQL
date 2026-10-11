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
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.Portal.Tests;

public class OrchestratorWebFactory : WebApplicationFactory<OrchestratorMarker>
{
    protected virtual bool EnableApplicationFileLogging => false;
    public const string IdentitySecret = "test-only-federated-orchestrator-identity-secret";
    public string TempDir { get; }
    private readonly bool requireFederatedIdentity;
    private readonly bool ownsTempDir;
    private SchedulerService? scheduler;

    public OrchestratorWebFactory(string? tempDir = null, bool requireFederatedIdentity = false)
    {
        this.requireFederatedIdentity = requireFederatedIdentity;
        ownsTempDir = tempDir is null;
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
        if (!EnableApplicationFileLogging)
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
            // The real monitor reads this machine's memory and this process's CPU. On a busy CI
            // runner it judged the node overloaded and the service, correctly, refused a manual
            // trigger with 503; a journey then reported "0 of 2 jobs triggered". A test host's
            // capacity must not depend on what else the machine is doing. Tests about overload
            // replace this registration with their own monitor.
            services.RemoveAll<INodeCapacityMonitor>();
            services.AddSingleton<INodeCapacityMonitor>(new PortalTestCapacityMonitor());
            services.AddSingleton<IConnectionCatalogProvider>(
                new LocalConnectionCatalogProvider(Path.Combine(TempDir, "connections")));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        scheduler = host.Services.GetService<SchedulerService>();
        return host;
    }

    public override async ValueTask DisposeAsync()
    {
        // A triggered run is a detached task. The hosted service waits for it at shutdown, but a
        // fixture that removes hosted services removes that wait, and the run would still be
        // writing to etlsql.db while the directory below is deleted.
        if (scheduler is not null) await scheduler.StopAsync();
        await base.DisposeAsync();
        using var connection = new SqliteConnection($"Data Source={Path.Combine(TempDir, "etlsql.db")}");
        SqliteConnection.ClearPool(connection);
        // A supplied directory belongs to the paired Portal factory, which is still running.
        if (!ownsTempDir || !Directory.Exists(TempDir)) return;
        try
        {
            Directory.Delete(TempDir, recursive: true);
        }
        catch (IOException ex)
        {
            // A remaining handle is a fixture failure; say which run or file kept it.
            var active = scheduler?.GetMetrics().ActiveJobs ?? 0;
            var remaining = Directory.Exists(TempDir)
                ? string.Join(", ", Directory.EnumerateFileSystemEntries(TempDir, "*", SearchOption.AllDirectories))
                : string.Empty;
            throw new IOException(
                $"Failed to clean up test directory '{TempDir}'. Runs still active: {active}. Remaining entries: [{remaining}].", ex);
        }
    }
}
