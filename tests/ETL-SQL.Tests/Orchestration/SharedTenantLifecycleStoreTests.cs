using ETL_SQL.Core.Data;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Storage;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public sealed class SharedTenantLifecycleStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"etlsql-shared-lifecycle-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task ProvisionIsDurableIdempotentAndTenantBound()
    {
        var store = NewStore();
        var alpha = TenantContext.FromVerifiedCredential("tenant-alpha");
        var command = Command("provision-alpha", SharedTenantLifecycleKind.Provision, "change-1");

        var first = await store.ApplySharedTenantLifecycleAsync(alpha, command);
        var replay = await NewStore().ApplySharedTenantLifecycleAsync(alpha, command);

        Assert.Equal("Completed", first.Status);
        Assert.Equal("Active", replay.State.State);
        Assert.Equal("release-2", replay.State.ActiveRelease);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewStore().ApplySharedTenantLifecycleAsync(
                TenantContext.FromVerifiedCredential("tenant-beta"), command));
    }

    [Fact]
    public async Task UpgradeFencesOnlyTenantJobsDrainsLeaseThenRestoresOnlyPreviouslyEnabledJobs()
    {
        var store = NewStore();
        var alpha = TenantContext.FromVerifiedCredential("tenant-alpha");
        await store.ApplySharedTenantLifecycleAsync(alpha,
            Command("provision-alpha", SharedTenantLifecycleKind.Provision, "change-p"));
        await store.SaveJobAsync(Job("alpha-on", "tenant-alpha", true));
        await store.SaveJobAsync(Job("alpha-off", "tenant-alpha", false));
        await store.SaveJobAsync(Job("beta-on", "tenant-beta", true));
        // Resolved in each job's own tenant: these names belong to tenants, so an unbound lookup is
        // not merely inconvenient here, it would be asking about a different object.
        var alphaOn = (await store.GetJobAsync("tenant-alpha", "alpha-on"))!;
        Assert.NotNull(await store.AcquireJobLeaseAsync(alphaOn.Id, "node-a", TimeSpan.FromMinutes(2)));

        var upgrade = Command("upgrade-alpha", SharedTenantLifecycleKind.Upgrade, "change-u") with
        {
            TargetRelease = "release-3",
            MaxConcurrentJobs = 7
        };
        var draining = await store.ApplySharedTenantLifecycleAsync(alpha, upgrade);

        Assert.Equal("Draining", draining.Status);
        Assert.False((await store.GetJobAsync("tenant-alpha", "alpha-on"))!.IsEnabled);
        Assert.False((await store.GetJobAsync("tenant-alpha", "alpha-off"))!.IsEnabled);
        Assert.True((await store.GetJobAsync("tenant-beta", "beta-on"))!.IsEnabled);

        await store.ReleaseJobLeaseAsync(alphaOn.Id, "node-a");
        Assert.Null(await store.AcquireJobLeaseAsync(
            alphaOn.Id, "stale-scheduler", TimeSpan.FromMinutes(2)));
        var complete = await store.ApplySharedTenantLifecycleAsync(alpha, upgrade);

        Assert.Equal("Completed", complete.Status);
        Assert.Equal("release-3", complete.State.ActiveRelease);
        Assert.Equal(7, complete.State.MaxConcurrentJobs);
        Assert.True((await store.GetJobAsync("tenant-alpha", "alpha-on"))!.IsEnabled);
        Assert.False((await store.GetJobAsync("tenant-alpha", "alpha-off"))!.IsEnabled);
        Assert.True((await store.GetJobAsync("tenant-beta", "beta-on"))!.IsEnabled);
    }

    [Fact]
    public async Task DeletePurgesTenantJobsAndHistoryButLeavesForeignEqualPurposeData()
    {
        var store = NewStore();
        var alpha = TenantContext.FromVerifiedCredential("tenant-alpha");
        var beta = TenantContext.FromVerifiedCredential("tenant-beta");
        await store.ApplySharedTenantLifecycleAsync(alpha,
            Command("provision-alpha", SharedTenantLifecycleKind.Provision, "change-pa"));
        await store.ApplySharedTenantLifecycleAsync(beta,
            Command("provision-beta", SharedTenantLifecycleKind.Provision, "change-pb"));
        await store.SaveJobAsync(Job("alpha-job", "tenant-alpha", true));
        await store.SaveJobAsync(Job("beta-job", "tenant-beta", true));
        // Resolved in each job's own tenant. These two are the whole point of the test — two tenants'
        // objects that would collide on name — so nothing here may go through an unbound lookup.
        var alphaJob = (await store.GetJobAsync("tenant-alpha", "alpha-job"))!;
        var betaJob = (await store.GetJobAsync("tenant-beta", "beta-job"))!;
        var alphaHistory = await store.LogJobStartAsync(alphaJob.Id);
        var betaHistory = await store.LogJobStartAsync(betaJob.Id);
        await store.LogJobEndAsync(alphaHistory, "SUCCESS");
        await store.LogJobEndAsync(betaHistory, "SUCCESS");
        await store.SetJobStateAsync(alphaJob.Id, "dq:quarantine-manifest:same", "alpha");
        await store.SetJobStateAsync(betaJob.Id, "dq:quarantine-manifest:same", "beta");
        await store.RollUpJobHistoryAsync();

        var result = await store.ApplySharedTenantLifecycleAsync(alpha,
            Command("delete-alpha", SharedTenantLifecycleKind.Delete, "change-d"));

        Assert.Equal("Deleted", result.State.State);
        Assert.Null(await store.GetJobAsync("tenant-alpha", "alpha-job"));
        Assert.Empty(await store.GetHistoryForNameAsync("tenant-alpha", "alpha-job"));
        Assert.Null(await store.GetJobStateAsync(alphaJob.Id, "dq:quarantine-manifest:same"));
        // Asked across every tenant, because the deleted job has no identity left to address it by —
        // and asking the whole table is the stronger assertion anyway: the rollup row is gone, not
        // merely unreachable through its own job.
        Assert.DoesNotContain(
            await store.GetJobHistoryDailyAsync(JobId.None, DateTime.UtcNow.AddDays(-1)),
            row => row.JobName == "alpha-job");
        Assert.NotNull(await store.GetJobAsync("tenant-beta", "beta-job"));
        Assert.Single(await store.GetHistoryForNameAsync("tenant-beta", "beta-job"));
        Assert.Equal("beta", await store.GetJobStateAsync(
            betaJob.Id, "dq:quarantine-manifest:same"));
        Assert.Single(await store.GetJobHistoryDailyAsync(betaJob.Id, DateTime.UtcNow.AddDays(-1)));
        Assert.Equal("Active", (await store.GetSharedTenantStateAsync(beta))!.State);
    }

    [Fact]
    public async Task HostFixedOrCallerChangedReplayCannotAdministerSharedLifecycle()
    {
        var store = NewStore();
        var command = Command("provision-alpha", SharedTenantLifecycleKind.Provision, "change-1");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            store.ApplySharedTenantLifecycleAsync(
                TenantContext.FromHostConfiguration("tenant-alpha"), command));

        var alpha = TenantContext.FromVerifiedCredential("tenant-alpha");
        await store.ApplySharedTenantLifecycleAsync(alpha, command);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ApplySharedTenantLifecycleAsync(alpha, command with { MaxStorageMb = 9999 }));
    }

    private RelationalJobHistoryStore NewStore() => new(
        new SqliteOrchestratorDialect($"Data Source={_path}"));

    private static SharedTenantLifecycleCommand Command(
        string operation, SharedTenantLifecycleKind kind, string authorization) => new(
            operation, kind, "platform-operator", authorization, "release-2",
            3, 2048, 4, DateTimeOffset.UtcNow);

    private static JobDefinition Job(string name, string tenant, bool enabled) => new(
        name, "RUN SCRIPT 'job.etlsql';", 1, "DAY", null, null, null,
        IsEnabled: enabled, TenantId: tenant);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }
}
