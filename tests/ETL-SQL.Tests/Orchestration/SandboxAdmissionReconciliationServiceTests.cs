using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Storage;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public sealed class SandboxAdmissionReconciliationServiceTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"etlsql-admission-reconcile-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task ExpiredLeaseIsReleasedOnlyAfterPositiveDetachProof()
    {
        var ledger = Ledger();
        var token = await ActiveAsync(ledger, "detached", "tenant-a");
        var runtime = new StubRuntime(new Dictionary<string, SandboxRuntimeReconciliationState>
        {
            ["detached"] = SandboxRuntimeReconciliationState.Detached
        });
        var service = new SandboxAdmissionReconciliationService(
            ledger, runtime, ["shared-hardened"]);

        var result = await service.RunOnceAsync(DateTimeOffset.UtcNow.AddMinutes(10));

        Assert.Equal(1, result.ExpiredRetained);
        Assert.Equal(1, result.DetachedReleased);
        Assert.Equal(token, runtime.Observed.Single().FenceToken);
        Assert.Equal(SandboxAdmissionState.Completed, (await ledger.ReadAsync("detached"))!.State);
    }

    [Theory]
    [InlineData(SandboxRuntimeReconciliationState.Running)]
    [InlineData(SandboxRuntimeReconciliationState.Unknown)]
    public async Task NonDetachedRuntimeRemainsRetainedAndConsumesCapacity(
        SandboxRuntimeReconciliationState state)
    {
        var ledger = Ledger();
        await ActiveAsync(ledger, "unresolved", "tenant-a");
        await ledger.EnqueueAsync("waiting", Tenant("tenant-b"), Policy());
        var runtime = new StubRuntime(new Dictionary<string, SandboxRuntimeReconciliationState>
        {
            ["unresolved"] = state
        });
        var service = new SandboxAdmissionReconciliationService(
            ledger, runtime, ["shared-hardened"]);

        var result = await service.RunOnceAsync(DateTimeOffset.UtcNow.AddMinutes(10));

        Assert.Equal(SandboxAdmissionState.Retained, (await ledger.ReadAsync("unresolved"))!.State);
        Assert.Null(await ledger.TryActivateAsync(
            "waiting", "node-b", 1, TimeSpan.FromMinutes(5)));
        Assert.Equal(state == SandboxRuntimeReconciliationState.Running ? 1 : 0, result.StillRunning);
        Assert.Equal(state == SandboxRuntimeReconciliationState.Unknown ? 1 : 0, result.Unknown);
    }

    [Fact]
    public async Task ProbeFailureFailsClosedAndLaterPassCanRelease()
    {
        var ledger = Ledger();
        await ActiveAsync(ledger, "probe-failure", "tenant-a");
        var runtime = new StubRuntime(new Dictionary<string, SandboxRuntimeReconciliationState>(), fail: true);
        var service = new SandboxAdmissionReconciliationService(
            ledger, runtime, ["shared-hardened", "shared-hardened"]);

        var failed = await service.RunOnceAsync(DateTimeOffset.UtcNow.AddMinutes(10));
        Assert.Equal(1, failed.ProbeFailures);
        Assert.Equal(SandboxAdmissionState.Retained, (await ledger.ReadAsync("probe-failure"))!.State);

        runtime.Fail = false;
        runtime.States["probe-failure"] = SandboxRuntimeReconciliationState.Detached;
        var recovered = await service.RunOnceAsync(DateTimeOffset.UtcNow.AddMinutes(11));
        Assert.Equal(1, recovered.DetachedReleased);
        Assert.Equal(SandboxAdmissionState.Completed, (await ledger.ReadAsync("probe-failure"))!.State);
    }

    private static async Task<long> ActiveAsync(
        RelationalSandboxAdmissionLedger ledger,
        string admissionId,
        string tenantId)
    {
        await ledger.EnqueueAsync(admissionId, Tenant(tenantId), Policy());
        return (await ledger.TryActivateAsync(
            admissionId, "node-a", 1, TimeSpan.FromMinutes(1)))!.Value;
    }

    private RelationalSandboxAdmissionLedger Ledger() => new(
        new SqliteOrchestratorDialect($"Data Source={_databasePath};Pooling=False"));

    private static TenantContext Tenant(string tenantId) =>
        TenantContext.FromVerifiedCredential(tenantId);

    private static ResolvedSandboxAdmissionPolicy Policy() => new()
    {
        PoolId = "shared-hardened",
        TenantWeight = 1,
        MaxConcurrentAttempts = 1,
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

    private sealed class StubRuntime(
        Dictionary<string, SandboxRuntimeReconciliationState> states,
        bool fail = false) : ISandboxRuntimeReconciler
    {
        public Dictionary<string, SandboxRuntimeReconciliationState> States { get; } = states;
        public List<SandboxAdmissionLedgerEntry> Observed { get; } = [];
        public bool Fail { get; set; } = fail;

        public Task<SandboxRuntimeReconciliationState> ProbeAsync(
            SandboxAdmissionLedgerEntry admission,
            CancellationToken cancellationToken)
        {
            Observed.Add(admission);
            if (Fail) throw new IOException("provider unavailable");
            return Task.FromResult(States.GetValueOrDefault(
                admission.AdmissionId, SandboxRuntimeReconciliationState.Unknown));
        }
    }
}
