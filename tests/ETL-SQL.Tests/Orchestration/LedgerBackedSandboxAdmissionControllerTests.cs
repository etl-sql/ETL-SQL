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
        var secondLease = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        await secondLease.ReleaseAsync();
    }

    [Fact]
    public async Task HeartbeatKeepsLiveAdmissionOutOfExpiredReconciliation()
    {
        var ledger = Ledger();
        var controller = Controller(
            "node-a", ledger, leaseDuration: TimeSpan.FromMilliseconds(300));
        var lease = await controller.AcquireAsync(Tenant("tenant-a"), Policy());

        await Task.Delay(450); // flaky-delay-ok: exceeds the original TTL while allowing multiple 100ms renewals

        Assert.Equal(0, await ledger.RetainExpiredAsync(
            DateTimeOffset.UtcNow, "lease expired"));
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
            await Task.Delay(Timeout.InfiniteTimeSpan, lease.LeaseLost).WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True(await controller.ReleaseReconciledAsync(lease.AdmissionId));
        Assert.Equal(
            SandboxAdmissionState.Completed,
            (await ledger.ReadAsync(lease.AdmissionId))!.State);
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
