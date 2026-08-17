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

    [Fact]
    public async Task TenantLifecycleCancelsAndPurgesOnlyItsPartitionButRetainsAmbiguousWork()
    {
        var store = Store();
        await store.EnqueueAsync("alpha-queued", Tenant("tenant-a"), Policy());
        await store.EnqueueAsync("alpha-active", Tenant("tenant-a"), Policy());
        await store.EnqueueAsync("beta-queued", Tenant("tenant-b"), Policy());
        var token = (await store.TryActivateAsync(
            "alpha-active", "node-a", 3, TimeSpan.FromMinutes(5)))!.Value;

        Assert.Equal(1, await store.CancelTenantQueuedAsync(Tenant("tenant-a")));
        var alphaOpen = await store.ListTenantOpenAsync(Tenant("tenant-a"));
        Assert.Equal("alpha-active", Assert.Single(alphaOpen).AdmissionId);
        Assert.Equal(SandboxAdmissionState.Queued,
            (await store.ReadAsync("beta-queued"))!.State);
        Assert.Equal(1, await store.PurgeTenantTerminalAsync(Tenant("tenant-a")));
        Assert.Null(await store.ReadAsync("alpha-queued"));
        Assert.NotNull(await store.ReadAsync("alpha-active"));

        Assert.True(await store.TryRetainAsync(
            "alpha-active", "node-a", token, "runtime detach uncertain"));
        Assert.Equal(SandboxAdmissionState.Retained,
            Assert.Single(await store.ListTenantOpenAsync(Tenant("tenant-a"))).State);
    }

    [Fact]
    public async Task FreedSlotGoesToTheWaitingTenantEvenWhenAnotherNodeAsksFirst()
    {
        // Two ledger instances are two orchestrator nodes on one shared authority.
        var nodeA = Store();
        var nodeB = Store();
        var policy = Policy() with { MaxConcurrentAttempts = 4 };
        foreach (var id in new[] { "a1", "a2", "a3" })
            await nodeA.EnqueueAsync(id, Tenant("tenant-a"), policy);
        await nodeB.EnqueueAsync("b1", Tenant("tenant-b"), policy);

        var first = (await nodeA.TryActivateAsync("a1", "node-a", 2, Lease))!.Value;
        Assert.NotNull(await nodeA.TryActivateAsync("a2", "node-a", 2, Lease));
        // Both queued attempts advertise a live claim while the pool is full.
        Assert.Null(await nodeB.TryActivateAsync("b1", "node-b", 2, Lease));
        Assert.Null(await nodeA.TryActivateAsync("a3", "node-a", 2, Lease));

        Assert.True(await nodeA.TryCompleteAsync("a1", "node-a", first));

        // The slot is free and tenant-a is under its own maximum, so only cluster-global fairness can
        // refuse it: tenant-b has been served less of this shared pool.
        Assert.Null(await nodeA.TryActivateAsync("a3", "node-a", 2, Lease));
        Assert.NotNull(await nodeB.TryActivateAsync("b1", "node-b", 2, Lease));

        var selection = await nodeA.PeekEligibleAsync("shared-hardened");
        Assert.Equal("a3", selection.EligibleAdmissionId);
        Assert.Equal("tenant-a", selection.EligibleTenantId);
    }

    [Fact]
    public async Task TenantWeightBuysProportionalGrantsInsteadOfUnconditionalPriority()
    {
        var store = Store();
        var heavy = Policy() with { TenantWeight = 4, MaxConcurrentAttempts = 4, MaxQueuedAttempts = 12 };
        var light = Policy() with { TenantWeight = 1, MaxConcurrentAttempts = 4, MaxQueuedAttempts = 12 };
        // Both tenants stay backlogged for the whole run, so the split reflects weight and nothing else.
        for (var index = 1; index <= 10; index++)
            await store.EnqueueAsync($"heavy-{index}", Tenant("tenant-heavy"), heavy);
        for (var index = 1; index <= 10; index++)
            await store.EnqueueAsync($"light-{index}", Tenant("tenant-light"), light);

        // One slot, handed out ten times: every attempt claims, the winner runs and completes.
        var granted = new List<string>();
        for (var round = 0; round < 10; round++)
        {
            var heavyId = $"heavy-{granted.Count(id => id.StartsWith("heavy", StringComparison.Ordinal)) + 1}";
            var lightId = $"light-{granted.Count(id => id.StartsWith("light", StringComparison.Ordinal)) + 1}";
            var heavyToken = await store.TryActivateAsync(heavyId, "node-a", 1, Lease);
            var lightToken = await store.TryActivateAsync(lightId, "node-b", 1, Lease);
            Assert.True(heavyToken.HasValue ^ lightToken.HasValue, "exactly one tenant may hold the only slot");
            var (winner, owner, token) = heavyToken.HasValue
                ? (heavyId, "node-a", heavyToken.Value)
                : (lightId, "node-b", lightToken!.Value);
            granted.Add(winner);
            Assert.True(await store.TryCompleteAsync(winner, owner, token));
        }

        var heavyGrants = granted.Count(id => id.StartsWith("heavy", StringComparison.Ordinal));
        // Weight 4 against weight 1 is a share, not a veto: the light tenant is never starved out.
        Assert.Equal(8, heavyGrants);
        Assert.Equal(2, granted.Count - heavyGrants);
    }

    [Fact]
    public async Task AbandonedQueueClaimStopsBlockingThePoolOnceItGoesStale()
    {
        var live = Store(TimeSpan.FromMinutes(5));
        var policy = Policy() with { MaxConcurrentAttempts = 4 };
        await live.EnqueueAsync("gone-1", Tenant("tenant-gone"), policy);
        await live.EnqueueAsync("busy-1", Tenant("tenant-busy"), policy);
        await live.EnqueueAsync("busy-2", Tenant("tenant-busy"), policy);

        var token = (await live.TryActivateAsync("busy-1", "node-b", 1, Lease))!.Value;
        Assert.Null(await live.TryActivateAsync("gone-1", "node-gone", 1, Lease));
        Assert.True(await live.TryCompleteAsync("busy-1", "node-b", token));

        // While the departed node's claim is fresh it legitimately outranks the busy tenant.
        Assert.Null(await live.TryActivateAsync("busy-2", "node-b", 1, Lease));

        // The node never came back. Once its claim is stale the pool must not stay idle behind it.
        var afterStaleness = Store(TimeSpan.FromMilliseconds(1));
        await Task.Delay(20);
        Assert.NotNull(await afterStaleness.TryActivateAsync("busy-2", "node-b", 1, Lease));
        Assert.Equal(SandboxAdmissionState.Queued, (await live.ReadAsync("gone-1"))!.State);
    }

    [Fact]
    public async Task TenantAtItsConcurrencyMaximumIsNoCandidateAndBlocksNobodyElse()
    {
        var store = Store();
        var onePerTenant = Policy() with { MaxConcurrentAttempts = 1 };
        await store.EnqueueAsync("capped-1", Tenant("tenant-capped"), onePerTenant);
        await store.EnqueueAsync("capped-2", Tenant("tenant-capped"), onePerTenant);
        await store.EnqueueAsync("other-1", Tenant("tenant-other"), onePerTenant);

        Assert.NotNull(await store.TryActivateAsync("capped-1", "node-a", 3, Lease));
        Assert.Null(await store.TryActivateAsync("capped-2", "node-a", 3, Lease));

        // The capped tenant's claimed head sits at the front of the durable queue but is not a
        // candidate, so it cannot hold the pool against a tenant behind it.
        var selection = await store.PeekEligibleAsync("shared-hardened");
        Assert.Null(selection.EligibleAdmissionId);
        Assert.DoesNotContain("tenant-capped", selection.ContendingTenantIds);
        Assert.NotNull(await store.TryActivateAsync("other-1", "node-b", 3, Lease));
    }

    [Fact]
    public async Task PeekEligibleReportsSelectionWithoutGrantingOrChargingIt()
    {
        var store = Store();
        await store.EnqueueAsync("peek-1", Tenant("tenant-a"), Policy());
        await store.EnqueueAsync("peek-2", Tenant("tenant-b"), Policy());

        // A never-polled admission is not a contender; only a live claim competes for capacity.
        Assert.Null((await store.PeekEligibleAsync("shared-hardened")).EligibleAdmissionId);

        var token = (await store.TryActivateAsync("peek-1", "node-a", 1, Lease))!.Value;
        Assert.Null(await store.TryActivateAsync("peek-2", "node-b", 1, Lease));
        var peeked = await store.PeekEligibleAsync("shared-hardened");
        Assert.Equal("peek-2", peeked.EligibleAdmissionId);
        Assert.Equal(new[] { "tenant-b" }, peeked.ContendingTenantIds);

        // Peeking is diagnostic: it neither grants the slot nor charges the tenant's fair share.
        Assert.Equal(SandboxAdmissionState.Queued, (await store.ReadAsync("peek-2"))!.State);
        Assert.Equal("peek-2", (await store.PeekEligibleAsync("shared-hardened")).EligibleAdmissionId);
        Assert.True(await store.TryCompleteAsync("peek-1", "node-a", token));
        Assert.NotNull(await store.TryActivateAsync("peek-2", "node-b", 1, Lease));
    }

    [Fact]
    public async Task QueueDepthCeilingIsFleetWideNotPerNode()
    {
        var nodeA = Store();
        var nodeB = Store();
        var policy = Policy() with { MaxQueuedAttempts = 2 };

        await nodeA.EnqueueAsync("depth-1", Tenant("tenant-a"), policy);
        // A second node has no idea what the first one queued; only the shared authority does.
        await nodeB.EnqueueAsync("depth-2", Tenant("tenant-a"), policy);
        var refused = await Assert.ThrowsAsync<SandboxQueueDepthExceededException>(
            async () => await nodeB.EnqueueAsync("depth-3", Tenant("tenant-a"), policy));

        Assert.Equal("tenant-a", refused.TenantId);
        Assert.Equal(2, refused.MaxQueuedAttempts);
        Assert.Null(await nodeA.ReadAsync("depth-3"));
        // Another tenant's depth is its own, and leaving the queue frees a place.
        await nodeB.EnqueueAsync("other-1", Tenant("tenant-b"), policy);
        Assert.True(await nodeA.TryCancelQueuedAsync("depth-1"));
        await nodeB.EnqueueAsync("depth-3", Tenant("tenant-a"), policy);
        Assert.Equal(SandboxAdmissionState.Queued, (await nodeA.ReadAsync("depth-3"))!.State);
    }

    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    private RelationalSandboxAdmissionLedger Store(TimeSpan? claimFreshness = null) => new(
        new SqliteOrchestratorDialect($"Data Source={_databasePath};Pooling=False"), claimFreshness);

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
