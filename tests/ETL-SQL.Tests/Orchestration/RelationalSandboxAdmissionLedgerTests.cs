using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Storage;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public sealed class RelationalSandboxAdmissionLedgerTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"etlsql-admission-ledger-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task QueueAndReservationSurviveStoreRecreation()
    {
        var first = Store();
        Assert.True(await first.EnqueueAsync("admission-1", Tenant("tenant-a"), Policy()));
        var token = await first.TryActivateAsync("admission-1", "node-a", 2, TimeSpan.FromMinutes(5));
        Assert.Equal(1, token);

        var restarted = Store();
        var entry = await restarted.ReadAsync("admission-1");

        Assert.NotNull(entry);
        Assert.Equal("tenant-a", entry.TenantId);
        Assert.Equal(SandboxAdmissionState.Active, entry.State);
        Assert.Equal("node-a", entry.LeaseOwner);
        Assert.Equal(token, entry.FenceToken);
    }

    [Fact]
    public async Task ConcurrentNodesCannotBothActivateQueuedAdmission()
    {
        var first = Store();
        var second = Store();
        Assert.True(await first.EnqueueAsync("admission-race", Tenant("tenant-a"), Policy()));

        var claims = await Task.WhenAll(
            first.TryActivateAsync("admission-race", "node-a", 2, TimeSpan.FromMinutes(5)),
            second.TryActivateAsync("admission-race", "node-b", 2, TimeSpan.FromMinutes(5)));

        Assert.Single(claims, token => token.HasValue);
        var entry = await first.ReadAsync("admission-race");
        Assert.Equal(SandboxAdmissionState.Active, entry!.State);
        Assert.Contains(entry.LeaseOwner, new[] { "node-a", "node-b" });
    }

    [Fact]
    public async Task OwnerAndFenceProtectRenewAndCompletion()
    {
        var store = Store();
        await store.EnqueueAsync("admission-fence", Tenant("tenant-a"), Policy());
        var token = (await store.TryActivateAsync(
            "admission-fence", "node-a", 2, TimeSpan.FromMinutes(5)))!.Value;

        Assert.False(await store.TryRenewAsync(
            "admission-fence", "node-b", token, TimeSpan.FromMinutes(5)));
        Assert.False(await store.TryCompleteAsync("admission-fence", "node-a", token + 1));
        Assert.True(await store.TryRenewAsync(
            "admission-fence", "node-a", token, TimeSpan.FromMinutes(5)));
        Assert.True(await store.TryCompleteAsync("admission-fence", "node-a", token));
        Assert.False(await store.TryCompleteAsync("admission-fence", "node-a", token));
        Assert.Equal(SandboxAdmissionState.Completed, (await store.ReadAsync("admission-fence"))!.State);
    }

    [Fact]
    public async Task ExpiredLeaseBecomesRetainedInsteadOfRequeued()
    {
        var store = Store();
        await store.EnqueueAsync("admission-expired", Tenant("tenant-a"), Policy());
        var token = (await store.TryActivateAsync(
            "admission-expired", "node-a", 2, TimeSpan.FromSeconds(1)))!.Value;

        Assert.Equal(1, await store.RetainExpiredAsync(
            DateTimeOffset.UtcNow.AddMinutes(1), "scheduler lease expired"));

        var retained = await store.ReadAsync("admission-expired");
        Assert.Equal(SandboxAdmissionState.Retained, retained!.State);
        Assert.Null(retained.LeaseOwner);
        Assert.Contains("expired", retained.ReconciliationReason, StringComparison.OrdinalIgnoreCase);
        Assert.Null(await store.TryActivateAsync(
            "admission-expired", "node-b", 2, TimeSpan.FromMinutes(5)));
        Assert.False(await store.ReleaseRetainedAsync("admission-expired", token + 1));
        Assert.True(await store.ReleaseRetainedAsync("admission-expired", token));
        Assert.Equal(SandboxAdmissionState.Completed, (await store.ReadAsync("admission-expired"))!.State);
    }

    [Fact]
    public async Task ExplicitAmbiguousTeardownRequiresFencedReconciliation()
    {
        var store = Store();
        await store.EnqueueAsync("admission-retained", Tenant("tenant-a"), Policy());
        var token = (await store.TryActivateAsync(
            "admission-retained", "node-a", 2, TimeSpan.FromMinutes(5)))!.Value;

        Assert.False(await store.TryRetainAsync(
            "admission-retained", "node-b", token, "wrong owner"));
        Assert.True(await store.TryRetainAsync(
            "admission-retained", "node-a", token, "runtime detach unconfirmed"));
        Assert.False(await store.TryCompleteAsync("admission-retained", "node-a", token));
        Assert.True(await store.ReleaseRetainedAsync("admission-retained", token));
    }

    [Fact]
    public async Task EqualAdmissionIdCannotBeReassignedToAnotherTenant()
    {
        var store = Store();
        Assert.True(await store.EnqueueAsync("same-id", Tenant("tenant-a"), Policy()));
        Assert.False(await store.EnqueueAsync("same-id", Tenant("tenant-b"), Policy()));
        Assert.Equal("tenant-a", (await store.ReadAsync("same-id"))!.TenantId);
    }

    [Fact]
    public async Task OpenQueueOrderSurvivesRestartAndCancellationIsStateChecked()
    {
        var store = Store();
        await store.EnqueueAsync("first", Tenant("tenant-a"), Policy());
        await store.EnqueueAsync("second", Tenant("tenant-b"), Policy());
        await store.EnqueueAsync("third", Tenant("tenant-c"), Policy());
        Assert.True(await store.TryCancelQueuedAsync("second"));
        Assert.False(await store.TryCancelQueuedAsync("second"));

        var restarted = Store();
        var open = await restarted.ListOpenAsync("shared-hardened");

        Assert.Equal(new[] { "first", "third" }, open.Select(entry => entry.AdmissionId));
        var token = await restarted.TryActivateAsync("first", "node-a", 2, TimeSpan.FromMinutes(5));
        Assert.False(await restarted.TryCancelQueuedAsync("first"));
        Assert.NotNull(token);
    }

    [Fact]
    public async Task PoolCapacityIsAtomicAcrossDifferentAdmissionsAndNodes()
    {
        var first = Store();
        var second = Store();
        await first.EnqueueAsync("pool-a", Tenant("tenant-a"), Policy());
        await first.EnqueueAsync("pool-b", Tenant("tenant-b"), Policy());

        var claims = await Task.WhenAll(
            first.TryActivateAsync("pool-a", "node-a", 1, TimeSpan.FromMinutes(5)),
            second.TryActivateAsync("pool-b", "node-b", 1, TimeSpan.FromMinutes(5)));

        Assert.Single(claims, token => token.HasValue);
        var activeId = claims[0].HasValue ? "pool-a" : "pool-b";
        var queuedId = claims[0].HasValue ? "pool-b" : "pool-a";
        var activeOwner = claims[0].HasValue ? "node-a" : "node-b";
        var token = claims.First(value => value.HasValue)!.Value;
        Assert.True(await first.TryCompleteAsync(activeId, activeOwner, token));
        Assert.NotNull(await second.TryActivateAsync(
            queuedId, "node-c", 1, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task TenantMaximumLeavesCapacityAvailableForAnotherTenant()
    {
        var store = Store();
        var onePerTenant = Policy() with { MaxConcurrentAttempts = 1 };
        await store.EnqueueAsync("tenant-a-1", Tenant("tenant-a"), onePerTenant);
        await store.EnqueueAsync("tenant-a-2", Tenant("tenant-a"), onePerTenant);
        await store.EnqueueAsync("tenant-b-1", Tenant("tenant-b"), onePerTenant);

        Assert.NotNull(await store.TryActivateAsync(
            "tenant-a-1", "node-a", 2, TimeSpan.FromMinutes(5)));
        Assert.Null(await store.TryActivateAsync(
            "tenant-a-2", "node-b", 2, TimeSpan.FromMinutes(5)));
        Assert.NotNull(await store.TryActivateAsync(
            "tenant-b-1", "node-b", 2, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task RetainedAttemptContinuesConsumingPoolCapacity()
    {
        var store = Store();
        await store.EnqueueAsync("retained-capacity", Tenant("tenant-a"), Policy());
        await store.EnqueueAsync("waiting-capacity", Tenant("tenant-b"), Policy());
        var token = (await store.TryActivateAsync(
            "retained-capacity", "node-a", 1, TimeSpan.FromMinutes(5)))!.Value;
        Assert.True(await store.TryRetainAsync(
            "retained-capacity", "node-a", token, "detach unconfirmed"));

        Assert.Null(await store.TryActivateAsync(
            "waiting-capacity", "node-b", 1, TimeSpan.FromMinutes(5)));
        Assert.True(await store.ReleaseRetainedAsync("retained-capacity", token));
        Assert.NotNull(await store.TryActivateAsync(
            "waiting-capacity", "node-b", 1, TimeSpan.FromMinutes(5)));
    }

    private RelationalSandboxAdmissionLedger Store() => new(
        new SqliteOrchestratorDialect($"Data Source={_databasePath};Pooling=False"));

    private static TenantContext Tenant(string tenantId) =>
        TenantContext.FromVerifiedCredential(tenantId);

    private static ResolvedSandboxAdmissionPolicy Policy() => new()
    {
        PoolId = "shared-hardened",
        TenantWeight = 1,
        MaxConcurrentAttempts = 2,
        MaxQueuedAttempts = 8
    };

    public void Dispose()
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = _databasePath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
