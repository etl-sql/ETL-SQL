using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ETL_SQL.Tests.Orchestration;

public sealed class SandboxAdmissionHostingTests
{
    [Fact]
    public void DisabledHostDoesNotReplaceTheExecutionPath()
    {
        var services = new ServiceCollection();
        services.AddSandboxAdmissionHosting(Configuration(enabled: false));

        using var provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<ISandboxAdmissionController>());
        Assert.Empty(provider.GetServices<IHostedService>());
    }

    [Fact]
    public void EnabledHostRequiresEnvironmentRuntimeReconciler()
    {
        var services = BaseServices(new StubLedger());
        services.AddSandboxAdmissionHosting(Configuration(enabled: true));

        using var provider = services.BuildServiceProvider();
        var error = Assert.Throws<InvalidOperationException>(() => provider.GetServices<IHostedService>().ToArray());
        Assert.Contains(nameof(ISandboxRuntimeReconciler), error.Message);
    }

    [Fact]
    public async Task EnabledHostRegistersDurableControllerAndRunsReconciliationLoop()
    {
        var ledger = new StubLedger();
        var services = BaseServices(ledger);
        services.AddSingleton<ISandboxRuntimeReconciler>(new DetachedRuntime());
        services.AddSandboxAdmissionHosting(Configuration(enabled: true));

        await using var provider = services.BuildServiceProvider();
        Assert.IsType<LedgerBackedSandboxAdmissionController>(
            provider.GetRequiredService<ISandboxAdmissionController>());
        var hosted = Assert.Single(provider.GetServices<IHostedService>());

        await hosted.StartAsync(CancellationToken.None);
        await ledger.ReconciliationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await hosted.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-1")]
    public void EnabledHostRejectsMissingOrInvalidPoolCapacity(string? capacity)
    {
        var values = Settings(enabled: true);
        if (capacity is null)
            values.Remove("Orchestration:SandboxAdmission:PoolCapacities:shared-hardened");
        else
            values["Orchestration:SandboxAdmission:PoolCapacities:shared-hardened"] = capacity;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Assert.ThrowsAny<Exception>(() => new ServiceCollection().AddSandboxAdmissionHosting(configuration));
    }

    private static ServiceCollection BaseServices(ISandboxAdmissionLedger ledger)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(ledger);
        return services;
    }

    private static IConfiguration Configuration(bool enabled) =>
        new ConfigurationBuilder().AddInMemoryCollection(Settings(enabled)).Build();

    private static Dictionary<string, string?> Settings(bool enabled) => new()
    {
        ["Orchestration:SandboxAdmission:Enabled"] = enabled.ToString(),
        ["Orchestration:SandboxAdmission:PoolCapacities:shared-hardened"] = "2",
        ["Orchestration:SandboxAdmission:LeaseSeconds"] = "30",
        ["Orchestration:SandboxAdmission:ActivationPollMilliseconds"] = "5",
        ["Orchestration:SandboxAdmission:ReconciliationSeconds"] = "0.01"
    };

    private sealed class DetachedRuntime : ISandboxRuntimeReconciler
    {
        public Task<SandboxRuntimeReconciliationState> ProbeAsync(
            SandboxAdmissionLedgerEntry admission,
            CancellationToken cancellationToken) =>
            Task.FromResult(SandboxRuntimeReconciliationState.Detached);
    }

    private sealed class StubLedger : ISandboxAdmissionLedger
    {
        public TaskCompletionSource ReconciliationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<int> RetainExpiredAsync(DateTimeOffset now, string reason, CancellationToken cancellationToken = default)
        {
            ReconciliationObserved.TrySetResult();
            return Task.FromResult(0);
        }

        public Task<IReadOnlyList<SandboxAdmissionLedgerEntry>> ListOpenAsync(
            string poolId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SandboxAdmissionLedgerEntry>>([]);

        public Task<bool> EnqueueAsync(string admissionId, TenantContext tenant,
            ResolvedSandboxAdmissionPolicy policy, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
        public Task<long?> TryActivateAsync(string admissionId, string leaseOwner, int poolCapacity,
            TimeSpan leaseDuration, CancellationToken cancellationToken = default) => Task.FromResult<long?>(1);
        public Task<bool> TryRenewAsync(string admissionId, string leaseOwner, long fenceToken,
            TimeSpan leaseDuration, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> TryCompleteAsync(string admissionId, string leaseOwner, long fenceToken,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> TryRetainAsync(string admissionId, string leaseOwner, long fenceToken, string reason,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> ReleaseRetainedAsync(string admissionId, long fenceToken,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> TryCancelQueuedAsync(string admissionId,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<SandboxAdmissionLedgerEntry?> ReadAsync(string admissionId,
            CancellationToken cancellationToken = default) => Task.FromResult<SandboxAdmissionLedgerEntry?>(null);
    }
}
