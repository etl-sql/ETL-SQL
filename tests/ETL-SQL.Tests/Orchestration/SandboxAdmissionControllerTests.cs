using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Execution;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public sealed class SandboxAdmissionControllerTests
{
    [Fact]
    public async Task PerTenantMaximumDoesNotBlockAnotherTenant()
    {
        var controller = Controller(("shared", 2));
        var policy = Policy("shared", maxConcurrent: 1);
        var firstA = await controller.AcquireAsync(Tenant("tenant-a"), policy);
        var queuedA = controller.AcquireAsync(Tenant("tenant-a"), policy).AsTask();

        var firstB = await controller.AcquireAsync(Tenant("tenant-b"), policy);

        Assert.False(queuedA.IsCompleted);
        await firstA.ReleaseAsync();
        var secondA = await queuedA;
        await secondA.ReleaseAsync();
        await firstB.ReleaseAsync();
    }

    [Fact]
    public async Task WeightedRoundRobinBoundsConsecutiveGrants()
    {
        var controller = Controller(("shared", 1));
        var policyA = Policy("shared", weight: 2, maxQueued: 8);
        var policyB = Policy("shared", weight: 1, maxQueued: 8);
        var seed = await controller.AcquireAsync(Tenant("tenant-a"), policyA);

        var a1 = controller.AcquireAsync(Tenant("tenant-a"), policyA).AsTask();
        var a2 = controller.AcquireAsync(Tenant("tenant-a"), policyA).AsTask();
        var a3 = controller.AcquireAsync(Tenant("tenant-a"), policyA).AsTask();
        var b1 = controller.AcquireAsync(Tenant("tenant-b"), policyB).AsTask();

        await seed.ReleaseAsync();
        var leaseA1 = await a1;
        Assert.False(b1.IsCompleted);
        await leaseA1.ReleaseAsync();
        var leaseA2 = await a2;
        Assert.False(b1.IsCompleted);
        await leaseA2.ReleaseAsync();

        var leaseB1 = await b1;
        Assert.False(a3.IsCompleted);
        await leaseB1.ReleaseAsync();
        var leaseA3 = await a3;
        await leaseA3.ReleaseAsync();
    }

    [Fact]
    public async Task IsolationPoolsNeverBorrowCapacity()
    {
        var controller = Controller(("shared-hardened", 1), ("dedicated", 1));
        var sharedPolicy = Policy("shared-hardened");
        var dedicatedPolicy = Policy("dedicated");
        var shared = await controller.AcquireAsync(Tenant("tenant-a"), sharedPolicy);
        var blockedShared = controller.AcquireAsync(Tenant("tenant-b"), sharedPolicy).AsTask();

        var dedicated = await controller.AcquireAsync(Tenant("tenant-b"), dedicatedPolicy);

        Assert.False(blockedShared.IsCompleted);
        await dedicated.ReleaseAsync();
        await shared.ReleaseAsync();
        var nextShared = await blockedShared;
        await nextShared.ReleaseAsync();
    }

    [Fact]
    public async Task TenantQueueLimitAppliesBackpressure()
    {
        var controller = Controller(("shared", 1));
        var policy = Policy("shared", maxQueued: 1);
        var active = await controller.AcquireAsync(Tenant("tenant-a"), policy);
        var queued = controller.AcquireAsync(Tenant("tenant-a"), policy).AsTask();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await controller.AcquireAsync(Tenant("tenant-a"), policy));

        Assert.Contains("queue", error.Message, StringComparison.OrdinalIgnoreCase);
        await active.ReleaseAsync();
        var next = await queued;
        await next.ReleaseAsync();
    }

    [Fact]
    public async Task CancelledWaiterDoesNotConsumeNextSlot()
    {
        var controller = Controller(("shared", 1));
        var policy = Policy("shared", maxQueued: 4);
        var active = await controller.AcquireAsync(Tenant("tenant-a"), policy);
        using var cancellation = new CancellationTokenSource();
        var cancelled = controller.AcquireAsync(Tenant("tenant-a"), policy, cancellation.Token).AsTask();
        var next = controller.AcquireAsync(Tenant("tenant-a"), policy).AsTask();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        await active.ReleaseAsync();
        var admitted = await next;
        await admitted.ReleaseAsync();
    }

    [Fact]
    public async Task MissingPoolFailsClosedInsteadOfFallingBack()
    {
        var controller = Controller(("shared", 1));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await controller.AcquireAsync(Tenant("tenant-a"), Policy("dedicated")));
        Assert.Contains("cannot borrow", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static FairShareSandboxAdmissionController Controller(
        params (string Pool, int Capacity)[] pools) =>
        new(new SandboxAdmissionControllerOptions
        {
            PoolCapacities = pools.ToDictionary(item => item.Pool, item => item.Capacity)
        });

    private static ResolvedSandboxAdmissionPolicy Policy(
        string pool,
        int weight = 1,
        int maxConcurrent = 1,
        int maxQueued = 4) => new()
        {
            PoolId = pool,
            TenantWeight = weight,
            MaxConcurrentAttempts = maxConcurrent,
            MaxQueuedAttempts = maxQueued
        };

    private static TenantContext Tenant(string tenantId) =>
        TenantContext.FromVerifiedCredential(tenantId);
}
