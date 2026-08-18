using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Portal.Services;
using ETL_SQL.Reporting;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// The Shared topology's interactive-session ceiling. Dedicated deployments materialize
/// <c>MaxReportSessions</c> into per-tenant node configuration; Shared has one node for every tenant,
/// so the quota has to be read back from the control plane and enforced per execution — otherwise it
/// is a number recorded at provisioning time that nothing ever applies.
/// </summary>
[Trait("Category", "Portal")]
public class TenantExecutionQuotaTests
{
    [Fact]
    public async Task TenantAtItsCeilingWaitsAndTheNextReleaseAdmitsIt()
    {
        var admission = new TenantExecutionAdmission();
        var first = await admission.AcquireAsync("tenant-a", 2, default);
        var second = await admission.AcquireAsync("tenant-a", 2, default);
        Assert.Equal(2, admission.ActiveFor("tenant-a"));

        var queued = admission.AcquireAsync("tenant-a", 2, default);
        await Task.Delay(100); // flaky-delay-ok: proves the third execution stays queued at the ceiling
        Assert.False(queued.IsCompleted);

        first.Dispose();
        var third = await queued.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, admission.ActiveFor("tenant-a"));
        second.Dispose();
        third.Dispose();
        Assert.Equal(0, admission.ActiveFor("tenant-a"));
    }

    [Fact]
    public async Task OneTenantAtItsCeilingDoesNotHoldUpAnother()
    {
        var admission = new TenantExecutionAdmission();
        var busy = await admission.AcquireAsync("tenant-busy", 1, default);
        var blocked = admission.AcquireAsync("tenant-busy", 1, default);

        // The whole point of the gate: a saturated tenant must not become everyone's queue.
        var neighbour = await admission.AcquireAsync("tenant-quiet", 1, default)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(blocked.IsCompleted);
        neighbour.Dispose();
        busy.Dispose();
        (await blocked.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [Fact]
    public async Task RaisedCeilingTakesEffectWithoutRestartingTheNode()
    {
        // The limit is read at each admission rather than baked into a fixed-size semaphore, so a
        // tenant upgrade is not pinned to whatever the ceiling was when this node first saw the
        // tenant. A queued waiter still keeps its place: a raised ceiling releases more of the queue,
        // it does not let a later arrival barge past an earlier one.
        var admission = new TenantExecutionAdmission();
        var held = await admission.AcquireAsync("tenant-a", 1, default);
        var queuedAtOldCeiling = admission.AcquireAsync("tenant-a", 1, default);
        var queuedAfterUpgrade = admission.AcquireAsync("tenant-a", 3, default);
        await Task.Delay(100); // flaky-delay-ok: one slot is held, so both must still be waiting
        Assert.False(queuedAtOldCeiling.IsCompleted);
        Assert.False(queuedAfterUpgrade.IsCompleted);

        held.Dispose();

        // Under the old ceiling of one this release could admit a single execution. The raised
        // ceiling admits both, in the order they arrived.
        var first = await queuedAtOldCeiling.WaitAsync(TimeSpan.FromSeconds(5));
        var second = await queuedAfterUpgrade.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, admission.ActiveFor("tenant-a"));

        first.Dispose();
        second.Dispose();
        Assert.Equal(0, admission.ActiveFor("tenant-a"));
    }

    [Fact]
    public async Task LoweredCeilingHoldsBackTheQueueInsteadOfHandingOverAVanishedSlot()
    {
        var admission = new TenantExecutionAdmission();
        var first = await admission.AcquireAsync("tenant-a", 2, default);
        var second = await admission.AcquireAsync("tenant-a", 2, default);
        // The tenant is downgraded to one session while two are running; the next waiter arrives
        // under the new ceiling and must not be admitted while a slot above it is still in use.
        var afterDowngrade = admission.AcquireAsync("tenant-a", 1, default);

        first.Dispose();
        await Task.Delay(100); // flaky-delay-ok: one execution still holds the tenant's only slot
        Assert.False(afterDowngrade.IsCompleted);

        second.Dispose();
        (await afterDowngrade.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [Fact]
    public async Task CancelledWaiterDoesNotConsumeTheSlotItNeverGot()
    {
        var admission = new TenantExecutionAdmission();
        var held = await admission.AcquireAsync("tenant-a", 1, default);
        using var cancellation = new CancellationTokenSource();
        var abandoned = admission.AcquireAsync("tenant-a", 1, cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await abandoned);

        held.Dispose();

        // A cancelled waiter that still held a place in the queue would leak the tenant's capacity.
        var next = await admission.AcquireAsync("tenant-a", 1, default).WaitAsync(TimeSpan.FromSeconds(5));
        next.Dispose();
        Assert.Equal(0, admission.ActiveFor("tenant-a"));
    }

    [Fact]
    public async Task QuotaComesFromTheProvisionedRecordAndOnlyInSharedDeployments()
    {
        var store = new StubLifecycleStore(sessions: 3);
        var shared = new SharedTenantExecutionQuotaSource(Config(sharedTenancy: true), store);

        Assert.Equal(3, await shared.GetMaxConcurrentExecutionsAsync("tenant-a", default));

        // A single-tenant deployment has no per-tenant ceiling and must not query the control plane.
        var single = new SharedTenantExecutionQuotaSource(Config(sharedTenancy: false), store);
        Assert.Null(await single.GetMaxConcurrentExecutionsAsync("tenant-a", default));
        Assert.Equal(1, store.Reads);
    }

    [Fact]
    public async Task UnprovisionedOrDeletedTenantsGetNoInventedCeiling()
    {
        var missing = new SharedTenantExecutionQuotaSource(
            Config(sharedTenancy: true), new StubLifecycleStore(sessions: null));
        Assert.Null(await missing.GetMaxConcurrentExecutionsAsync("tenant-a", default));

        var deleted = new SharedTenantExecutionQuotaSource(
            Config(sharedTenancy: true), new StubLifecycleStore(sessions: 5, state: "Deleted"));
        Assert.Null(await deleted.GetMaxConcurrentExecutionsAsync("tenant-a", default));
    }

    [Fact]
    public async Task QuotaIsCachedButInvalidationMakesAnUpgradeVisible()
    {
        var store = new StubLifecycleStore(sessions: 2);
        var source = new SharedTenantExecutionQuotaSource(
            Config(sharedTenancy: true), store, TimeSpan.FromMinutes(5));

        Assert.Equal(2, await source.GetMaxConcurrentExecutionsAsync("tenant-a", default));
        store.Sessions = 6;
        Assert.Equal(2, await source.GetMaxConcurrentExecutionsAsync("tenant-a", default));
        Assert.Equal(1, store.Reads);

        source.Invalidate("tenant-a");
        Assert.Equal(6, await source.GetMaxConcurrentExecutionsAsync("tenant-a", default));
    }

    private static PortalConfig Config(bool sharedTenancy)
    {
        var config = new PortalConfig();
        config.SharedTenancy.Enabled = sharedTenancy;
        return config;
    }

    private sealed class StubLifecycleStore(int? sessions, string state = "Active") : ISharedTenantLifecycleStore
    {
        public int? Sessions { get; set; } = sessions;
        public int Reads { get; private set; }

        public Task<SharedTenantLifecycleResult> ApplySharedTenantLifecycleAsync(
            TenantContext tenant,
            SharedTenantLifecycleCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        // The quota source resolves one tenant at a time; enumerating the fleet is a rollout-planning
        // concern and must never be reached from the execution admission path.
        public Task<IReadOnlyList<SharedTenantControlPlaneState>> ListSharedTenantStatesAsync(
            FleetInventoryAuthorization authorization,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(
                "Execution admission must not enumerate the tenant population.");

        public Task<SharedTenantControlPlaneState?> GetSharedTenantStateAsync(
            TenantContext tenant,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(Sessions is null
                ? null
                : new SharedTenantControlPlaneState(
                    tenant.Tenant.Value, state, "release-1", 4, 2048, Sessions.Value,
                    1, DateTimeOffset.UtcNow, null, 1));
        }
    }
}
