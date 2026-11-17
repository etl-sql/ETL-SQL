using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Functions;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Engine.Services;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public sealed class TenantBundleStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly SQLiteJobHistoryStore _store;

    public TenantBundleStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"etlsql_tenant_bundles_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "orch.db");
        _store = new SQLiteJobHistoryStore(_dbPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Ignore transient SQLite handle release delays on Windows.
        }
    }

    [Fact]
    public async Task SQLiteStore_TwoTenantsCannotReadOrOverwriteEachOther_EvenWithSameBundleName()
    {
        var tenantA = TenantContext.FromVerifiedCredential("tenant-a");
        var tenantB = TenantContext.FromVerifiedCredential("tenant-b");
        ITenantBundleStore tenantStore = _store;

        var aV1 = await tenantStore.PublishBundleAsync(tenantA, MakeBundle(
            "nightly", "sha256:a-v1", "SELECT 'tenant-a-v1' AS owner;", "alice"));
        var bV1 = await tenantStore.PublishBundleAsync(tenantB, MakeBundle(
            "nightly", "sha256:b-v1", "SELECT 'tenant-b-v1' AS owner;", "bob"));
        var aOnly = await tenantStore.PublishBundleAsync(tenantA, MakeBundle(
            "alpha-only", "sha256:alpha-1", "SELECT 'alpha' AS owner;", "alice"));

        Assert.Equal(1, aV1.Version);
        Assert.Equal(1, bV1.Version);
        Assert.Equal(1, aOnly.Version);

        // Tenant A retention GC across 6 publishes must not touch Tenant B's version 1.
        for (var i = 2; i <= 6; i++)
        {
            await tenantStore.PublishBundleAsync(tenantA, MakeBundle(
                "nightly", $"sha256:a-v{i}", $"SELECT 'tenant-a-v{i}' AS owner;", "alice"));
        }

        var aLatest = await tenantStore.GetLatestVersionAsync(tenantA, "nightly");
        var bLatest = await tenantStore.GetLatestVersionAsync(tenantB, "nightly");
        Assert.Equal(6, aLatest!.Version);
        Assert.Equal(1, bLatest!.Version);

        var bFile = await tenantStore.GetFileAsync(tenantB, "nightly", 1, "main.etlsql");
        Assert.NotNull(bFile);
        Assert.Contains("tenant-b-v1", bFile!.Content, StringComparison.Ordinal);

        Assert.Null(await tenantStore.GetLatestVersionAsync(tenantB, "alpha-only"));
        Assert.Null(await tenantStore.GetVersionAsync(tenantB, "alpha-only", 1));
        Assert.Null(await tenantStore.GetFileAsync(tenantB, "alpha-only", 1, "main.etlsql"));
        Assert.Empty(await tenantStore.GetVersionsAsync(tenantB, "alpha-only"));
        Assert.Empty(await tenantStore.GetFilesAsync(tenantB, "alpha-only", 1));
        Assert.Empty(await tenantStore.GetDependenciesAsync(tenantB, "alpha-only", 1));
        Assert.Null(await tenantStore.GetVersionAsync(tenantB, "nightly", 6));

        var listB = (await tenantStore.GetBundlesAsync(tenantB)).ToList();
        Assert.Single(listB);
        Assert.Equal("nightly", listB[0].BundleName);
        Assert.Equal("bob", listB[0].Publisher);

        // Untenanted reads must not expose tenant A/B bundles.
        Assert.Empty(await _store.GetBundlesAsync());
    }

    [Fact]
    public async Task EnginePublishRunScriptAndCatalogViews_ResolveThroughCallerTenant_AndFailClosedWithoutTenantStore()
    {
        var bundleDirA = Path.Combine(_tempDir, "src-a");
        var bundleDirB = Path.Combine(_tempDir, "src-b");
        Directory.CreateDirectory(bundleDirA);
        Directory.CreateDirectory(bundleDirB);

        await File.WriteAllTextAsync(Path.Combine(bundleDirA, "main.etlsql"), """
            SELECT 'tenant-a-secret' AS who;
            """);
        await File.WriteAllTextAsync(Path.Combine(bundleDirB, "main.etlsql"), """
            SELECT 'tenant-b-secret' AS who;
            """);

        await using var sp = BuildEngineServiceProvider(_store, _store);
        var identityA = new ExecutionIdentity { EffectiveUser = "user:a", RealUser = "user:a", IsAdmin = false, TenantId = "tenant-a" };
        var identityB = new ExecutionIdentity { EffectiveUser = "user:b", RealUser = "user:b", IsAdmin = false, TenantId = "tenant-b" };

        await ExecuteScriptAsync(sp, identityA, $"PUBLISH BUNDLE 'shared_pkg' FROM '{bundleDirA.Replace("'", "''")}' ENTRY 'main.etlsql';");
        await ExecuteScriptAsync(sp, identityB, $"PUBLISH BUNDLE 'shared_pkg' FROM '{bundleDirB.Replace("'", "''")}' ENTRY 'main.etlsql';");

        var evalA = await ExecuteScriptAsync(sp, identityA, "RUN SCRIPT 'orch://shared_pkg/main.etlsql';");
        Assert.Equal("tenant-a-secret", evalA.LastResult!.Rows[0]["who"]);

        var evalB = await ExecuteScriptAsync(sp, identityB, "RUN SCRIPT 'orch://shared_pkg/main.etlsql';");
        Assert.Equal("tenant-b-secret", evalB.LastResult!.Rows[0]["who"]);

        var catalogA = await ExecuteScriptAsync(sp, identityA, "SELECT bundle_name, version FROM eng.bundles;");
        Assert.Single(catalogA.LastResult!.Rows);
        Assert.Equal("shared_pkg", catalogA.LastResult.Rows[0]["bundle_name"]);
        Assert.Equal(1m, Convert.ToDecimal(catalogA.LastResult.Rows[0]["version"]));

        // Tenant A publishes an isolated bundle; Tenant B cannot RUN SCRIPT or query its files.
        await ExecuteScriptAsync(sp, identityA, $"PUBLISH BUNDLE 'only_a' FROM '{bundleDirA.Replace("'", "''")}' ENTRY 'main.etlsql';");
        await Assert.ThrowsAsync<ExecutionException>(() => ExecuteScriptAsync(
            sp, identityB, "RUN SCRIPT 'orch://only_a/main.etlsql';"));

        var filesBForOnlyA = await ExecuteScriptAsync(sp, identityB, "SELECT * FROM eng.bundle_files('only_a');");
        Assert.Empty(filesBForOnlyA.LastResult!.Rows);

        // Fail closed when a tenant-bound execution has only an untenanted IBundleStore.
        var mockUntenanted = new Mock<IBundleStore>(MockBehavior.Strict);
        await using var spNoTenantStore = BuildEngineServiceProvider(mockUntenanted.Object, tenantStore: null);
        var ex = await Assert.ThrowsAsync<ExecutionException>(() => ExecuteScriptAsync(
            spNoTenantStore, identityB, "RUN SCRIPT 'orch://shared_pkg/main.etlsql';"));
        Assert.Contains("ITenantBundleStore", ex.Message, StringComparison.Ordinal);
        mockUntenanted.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LegacyDefaultBundleRows_AutoMigrateOnUniqueJobOwnership_LeaveAmbiguousForOperatorReassign()
    {
        await _store.InitializeAsync();

        // Seed three legacy bundles under 'default' plus tenant jobs referencing them.
        await using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync();
            await SeedDefaultBundleAsync(conn, "owned-by-a", "SELECT 'a' AS v;");
            await SeedDefaultBundleAsync(conn, "shared-ambiguous", "SELECT 'ambiguous' AS v;");
            await SeedDefaultBundleAsync(conn, "unreferenced-orphan", "SELECT 'orphan' AS v;");

            await SeedJobAsync(conn, "job-a1", "tenant-a", "orch://owned-by-a/main.etlsql");
            await SeedJobAsync(conn, "job-a2", "tenant-a", "orch://shared-ambiguous@1/main.etlsql");
            await SeedJobAsync(conn, "job-b1", "tenant-b", "orch://shared-ambiguous@1/main.etlsql");
        }

        // Open a fresh store instance over the upgraded DB and run migration (as startup InitializeAsync does).
        var migratedStore = new SQLiteJobHistoryStore(_dbPath);
        await migratedStore.InitializeAsync();
        ITenantBundleStore tenantStore = migratedStore;

        var tenantA = TenantContext.FromVerifiedCredential("tenant-a");
        var tenantB = TenantContext.FromVerifiedCredential("tenant-b");

        // owned-by-a had a single owning tenant in jobs -> moved automatically to tenant-a.
        var aOwned = await tenantStore.GetLatestVersionAsync(tenantA, "owned-by-a");
        Assert.NotNull(aOwned);
        Assert.NotNull(await tenantStore.GetFileAsync(tenantA, "owned-by-a", 1, "main.etlsql"));
        Assert.Null(await tenantStore.GetLatestVersionAsync(tenantB, "owned-by-a"));

        // Ambiguous and unreferenced bundles stay untouched in 'default' and invisible to tenants.
        Assert.Null(await tenantStore.GetLatestVersionAsync(tenantA, "shared-ambiguous"));
        Assert.Null(await tenantStore.GetLatestVersionAsync(tenantB, "shared-ambiguous"));
        Assert.Null(await tenantStore.GetLatestVersionAsync(tenantA, "unreferenced-orphan"));
        Assert.Null(await tenantStore.GetLatestVersionAsync(tenantB, "unreferenced-orphan"));

        var unassigned = await tenantStore.GetUnassignedDefaultBundlesAsync();
        Assert.Equal(2, unassigned.Count);
        Assert.Contains(unassigned, b => b.BundleName == "shared-ambiguous");
        Assert.Contains(unassigned, b => b.BundleName == "unreferenced-orphan");

        // Operator explicitly reassigns unreferenced-orphan to tenant-b.
        var movedCount = await tenantStore.ReassignDefaultBundlesAsync(tenantB, "unreferenced-orphan");
        Assert.Equal(1, movedCount);
        Assert.NotNull(await tenantStore.GetLatestVersionAsync(tenantB, "unreferenced-orphan"));
        Assert.Null(await tenantStore.GetLatestVersionAsync(tenantA, "unreferenced-orphan"));

        // Reassigning a bundle whose version already exists in target refuses safely.
        await tenantStore.PublishBundleAsync(tenantB, MakeBundle(
            "shared-ambiguous", "sha256:existing-b", "SELECT 'b-owned';", "bob"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tenantStore.ReassignDefaultBundlesAsync(tenantB, "shared-ambiguous"));
    }

    private static BundlePublishRequest MakeBundle(
        string name, string hash, string mainContent, string publisher) => new(
        name,
        "main.etlsql",
        [
            new BundlePublishFile("main.etlsql", mainContent, hash, mainContent.Length, "application/etlsql"),
            new BundlePublishFile("lib/helper.etlsql", "PRINT 'helper';", $"{hash}-dep", 15, "application/etlsql")
        ],
        [new BundleDependencyInfo(name, 0, "main.etlsql", "lib/helper.etlsql")],
        hash,
        "MACHINE",
        null,
        publisher,
        null);

    private static ServiceProvider BuildEngineServiceProvider(
        IBundleStore bundleStore,
        ITenantBundleStore? tenantStore)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection().Build());
        services.AddSingleton<ILogger>(NullLogger.Instance);
        services.AddSingleton(bundleStore);
        if (tenantStore is not null)
            services.AddSingleton(tenantStore);
        services.AddSingleton(bundleStore is IJobHistoryStore history ? history : new Mock<IJobHistoryStore>().Object);
        services.AddSingleton(bundleStore is IJobCatalogStore catalog ? catalog : new Mock<IJobCatalogStore>().Object);
        services.AddSingleton(bundleStore is ILineageCatalogStore lineage ? lineage : new Mock<ILineageCatalogStore>().Object);
        services.AddSingleton(bundleStore is IHostMetricsStore metrics ? metrics : new Mock<IHostMetricsStore>().Object);

        var registry = new FunctionRegistry();
        FileFunctions.Register(registry);
        StandardFunctions.Register(registry);
        services.AddSingleton<IFunctionRegistry>(registry);

        services.AddSingleton<ILineageTracker, LineageTracker>();
        services.AddSingleton<IDockerManager, DockerContainerManager>();
        services.AddSingleton<IConnectorRegistry, ConnectorRegistry>();
        services.AddSingleton<EvaluatorComponentRegistry>();
        services.AddSingleton<ISessionMetadataStoreFactory, SqliteSessionMetadataStoreFactory>();
        services.AddSingleton<ISessionStateManager, SessionStateManager>();
        services.AddSingleton<SessionStateManager>(sp => (SessionStateManager)sp.GetRequiredService<ISessionStateManager>());
        services.AddSingleton(new CliContext());
        services.AddSingleton<SecurityService>();
        services.AddSingleton<ISystemResources, DefaultSystemResources>();
        services.AddSingleton<IBufferManager, BufferManager>();
        services.AddSingleton(Options.Create(new BufferManagerOptions()));
        services.AddSingleton<ETL_SQL.Core.Interfaces.ILanguageHelpRegistry>(new ETL_SQL.Core.Metadata.LanguageHelpRegistry());
        services.AddTransient<Evaluator>();

        var handlerAssembly = typeof(DeclareStatementHandler).Assembly;
        foreach (var type in handlerAssembly.GetTypes()
            .Where(t => typeof(IStatementHandler).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract))
        {
            services.AddTransient(typeof(IStatementHandler), type);
            services.AddTransient(type);
        }

        return services.BuildServiceProvider();
    }

    private static async Task<Evaluator> ExecuteScriptAsync(
        IServiceProvider sp,
        ExecutionIdentity identity,
        string sql)
    {
        await using var scope = sp.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<Evaluator>();
        evaluator.ExecutionIdentity = identity;
        var tokens = new Lexer(sql).Tokenize();
        var script = new Parser(tokens, sql).Parse();
        await evaluator.Evaluate(script);
        return evaluator;
    }

    private static async Task SeedDefaultBundleAsync(SqliteConnection conn, string bundleName, string content)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO bundle_versions (tenant_id, bundle_name, version, entry_path, content_hash, published_at, publisher)
            VALUES ('default', $b, 1, 'main.etlsql', 'sha256:legacy', '2026-10-01T00:00:00Z', 'legacy');
            INSERT INTO bundle_files (tenant_id, bundle_name, version, virtual_path, content, content_hash, size_bytes, content_type)
            VALUES ('default', $b, 1, 'main.etlsql', $c, 'sha256:legacy', $len, 'application/etlsql');
            INSERT INTO bundle_dependencies (tenant_id, bundle_name, version, from_path, to_path)
            VALUES ('default', $b, 1, 'main.etlsql', 'lib/helper.etlsql');";
        cmd.Parameters.AddWithValue("$b", bundleName);
        cmd.Parameters.AddWithValue("$c", content);
        cmd.Parameters.AddWithValue("$len", content.Length);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedJobAsync(SqliteConnection conn, string name, string tenantId, string targetPath)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO jobs (id, name, script, interval, unit, is_enabled, job_type, target_path, tenant_id)
            VALUES ($id, $name, $script, 1, 'HOUR', 1, 'Script', $target, $tenant);";
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$script", $"RUN SCRIPT '{targetPath}';");
        cmd.Parameters.AddWithValue("$target", targetPath);
        cmd.Parameters.AddWithValue("$tenant", tenantId);
        await cmd.ExecuteNonQueryAsync();
    }
}
