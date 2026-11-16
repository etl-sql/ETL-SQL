using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Storage;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public sealed class LedgerBackedSandboxAdmissionControllerTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"etlsql-ledger-controller-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task AcquireAndReleaseDriveDurableTerminalState()
    {
        var ledger = Ledger();
        var controller = Controller("node-a", ledger);

        var lease = await controller.AcquireAsync(Tenant("tenant-a"), Policy());
        var active = await ledger.ReadAsync(lease.AdmissionId);
        Assert.Equal(SandboxAdmissionState.Active, active!.State);
        Assert.Equal("node-a", active.LeaseOwner);

        await lease.ReleaseAsync();
        Assert.Equal(
            SandboxAdmissionState.Completed,
            (await ledger.ReadAsync(lease.AdmissionId))!.State);
    }

    [Fact]
    public async Task SeparateNodesCannotExceedDurablePoolCapacity()
    {
        var ledger = Ledger();
        var first = Controller("node-a", ledger, poolCapacity: 1);
        var second = Controller("node-b", Ledger(), poolCapacity: 1);
        var firstLease = await first.AcquireAsync(Tenant("tenant-a"), Policy());
        var waiting = second.AcquireAsync(Tenant("tenant-b"), Policy()).AsTask();
        await Task.Delay(150); // flaky-delay-ok: proves the second node remains queued while the only durable slot is held
        Assert.False(waiting.IsCompleted);

        await firstLease.ReleaseAsync();
        var secondLease = await waiting.WaitAsync(HangGuard);
        await secondLease.ReleaseAsync();
    }

    [Fact]
    public async Task HeartbeatKeepsLiveAdmissionOutOfExpiredReconciliation()
    {
        var ledger = Ledger();
        var controller = Controller(
            "node-a", ledger, leaseDuration: TimeSpan.FromMilliseconds(300));
        var lease = await controller.AcquireAsync(Tenant("tenant-a"), Policy());
        var original = (await ledger.ReadAsync(lease.AdmissionId))!.LeaseExpiresUtc!.Value;

        // Wait for a heartbeat to move the lease past its original expiry, then reconcile at that
        // original instant. A fixed 450 ms sleep raced the renewals on a slow runner; this checks
        // the same thing however long a renewal takes.
        await WaitUntilAsync(async () =>
            (await ledger.ReadAsync(lease.AdmissionId))!.LeaseExpiresUtc > original);

        Assert.Equal(0, await ledger.RetainExpiredAsync(original, "lease expired"));
        Assert.Equal(SandboxAdmissionState.Active, (await ledger.ReadAsync(lease.AdmissionId))!.State);
        await lease.ReleaseAsync();
    }

    [Fact]
    public async Task LostFenceCancelsExecutionAuthorityAndRequiresReconciliation()
    {
        var ledger = Ledger();
        var controller = Controller(
            "node-a", ledger, leaseDuration: TimeSpan.FromMilliseconds(150));
        var lease = await controller.AcquireAsync(Tenant("tenant-a"), Policy());
        var entry = await ledger.ReadAsync(lease.AdmissionId);
        Assert.True(await ledger.TryRetainAsync(
            lease.AdmissionId,
            "node-a",
            entry!.FenceToken,
            "simulated ownership loss"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Task.Delay(Timeout.InfiniteTimeSpan, lease.LeaseLost).WaitAsync(HangGuard));
        Assert.True(await controller.ReleaseReconciledAsync(lease.AdmissionId));
        Assert.Equal(
            SandboxAdmissionState.Completed,
            (await ledger.ReadAsync(lease.AdmissionId))!.State);
    }

    [Fact]
    public async Task RestartResumeAdoptsExistingQueuedAdmissionId()
    {
        var ledger = Ledger();
        await ledger.EnqueueAsync("persisted-queue", Tenant("tenant-a"), Policy());
        var queued = await ledger.ReadAsync("persisted-queue");
        var replacement = Controller("node-replacement", Ledger());

        var lease = await replacement.ResumeQueuedAsync(queued!, Tenant("tenant-a"));

        Assert.Equal("persisted-queue", lease.AdmissionId);
        var active = await ledger.ReadAsync("persisted-queue");
        Assert.Equal(SandboxAdmissionState.Active, active!.State);
        Assert.Equal("node-replacement", active.LeaseOwner);
        await lease.ReleaseAsync();
    }

    [Fact]
    public async Task RestartResumeRejectsTenantSwapAndNonQueuedAuthority()
    {
        var ledger = Ledger();
        await ledger.EnqueueAsync("tenant-bound", Tenant("tenant-a"), Policy());
        var queued = await ledger.ReadAsync("tenant-bound");
        var replacement = Controller("node-replacement", ledger);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await replacement.ResumeQueuedAsync(queued!, Tenant("tenant-b")));
        Assert.Equal(SandboxAdmissionState.Queued, (await ledger.ReadAsync("tenant-bound"))!.State);

        await ledger.TryActivateAsync(
            "tenant-bound", "old-node", 1, TimeSpan.FromMinutes(5));
        var active = await ledger.ReadAsync("tenant-bound");
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await replacement.ResumeQueuedAsync(active!, Tenant("tenant-a")));
    }

    [Fact]
    public async Task InterruptedRestartRecoveryLeavesDurableQueueForAnotherNode()
    {
        var ledger = Ledger();
        var holder = Controller("node-holder", ledger);
        var active = await holder.AcquireAsync(Tenant("tenant-a"), Policy());
        await ledger.EnqueueAsync("recover-later", Tenant("tenant-b"), Policy());
        var queued = await ledger.ReadAsync("recover-later");
        var replacement = Controller("node-replacement", Ledger());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await replacement.ResumeQueuedAsync(queued!, Tenant("tenant-b"), cancellation.Token));

        Assert.Equal(SandboxAdmissionState.Queued, (await ledger.ReadAsync("recover-later"))!.State);
        await active.ReleaseAsync();
    }

    [Fact]
    public async Task WaitingNodeFailsFastWhenItsDurableAdmissionIsRevoked()
    {
        var ledger = Ledger();
        var holder = Controller("node-holder", ledger);
        var waiter = Controller("node-waiter", Ledger());
        var held = await holder.AcquireAsync(Tenant("tenant-a"), Policy());
        var waiting = waiter.AcquireAsync(Tenant("tenant-b"), Policy()).AsTask();
        // The second node must have enqueued before its queued work can be revoked; a fixed 150 ms
        // sleep assumed it had.
        await WaitUntilAsync(async () => (await ledger.ListTenantOpenAsync(Tenant("tenant-b"))).Count == 1);
        Assert.False(waiting.IsCompleted);

        var queued = (await ledger.ListTenantOpenAsync(Tenant("tenant-b"))).Single();
        // A tenant lifecycle fence revokes queued work while a node is still waiting on it.
        Assert.Equal(1, await ledger.CancelTenantQueuedAsync(Tenant("tenant-b")));

        var revoked = await Assert.ThrowsAsync<SandboxAdmissionRevokedException>(
            async () => await waiting.WaitAsync(HangGuard));
        Assert.Equal(queued.AdmissionId, revoked.AdmissionId);
        Assert.Equal(SandboxAdmissionState.Cancelled, revoked.State);
        await held.ReleaseAsync();
    }

    // Every renewal and poll opens the SQLite file without pooling. The coverage lane on a Windows
    // runner once took over 3 s to observe a 150 ms lease loss (CI 42488694536), so the guards only
    // catch hangs; the expected paths finish as soon as the state changes.
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(30);

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + HangGuard;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Condition not met within {HangGuard.TotalSeconds:0} s.");
            await Task.Delay(25); // flaky-delay-ok: polling interval for a condition with a hang guard
        }
    }

    private RelationalSandboxAdmissionLedger Ledger() => new(
        new SqliteOrchestratorDialect($"Data Source={_databasePath};Pooling=False"));

    private static LedgerBackedSandboxAdmissionController Controller(
        string nodeId,
        ISandboxAdmissionLedger ledger,
        int poolCapacity = 1,
        TimeSpan? leaseDuration = null)
    {
        var capacities = new Dictionary<string, int> { ["shared-hardened"] = poolCapacity };
        var fair = new FairShareSandboxAdmissionController(
            new SandboxAdmissionControllerOptions { PoolCapacities = capacities });
        return new LedgerBackedSandboxAdmissionController(
            fair,
            ledger,
            new LedgerBackedSandboxAdmissionOptions
            {
                NodeId = nodeId,
                PoolCapacities = capacities,
                LeaseDuration = leaseDuration ?? TimeSpan.FromMinutes(2),
                ActivationPollInterval = TimeSpan.FromMilliseconds(20)
            });
    }

    private static ResolvedSandboxAdmissionPolicy Policy() => new()
    {
        PoolId = "shared-hardened",
        TenantWeight = 1,
        MaxConcurrentAttempts = 1,
        MaxQueuedAttempts = 8
    };

    private static TenantContext Tenant(string tenantId) =>
        TenantContext.FromVerifiedCredential(tenantId);

    public void Dispose()
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = _databasePath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
