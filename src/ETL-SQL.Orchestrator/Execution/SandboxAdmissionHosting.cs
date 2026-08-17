using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.Orchestrator.Execution;

public sealed class SandboxAdmissionHostOptions
{
    public bool Enabled { get; init; }
    public IReadOnlyDictionary<string, int> PoolCapacities { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan ActivationPollInterval { get; init; } = TimeSpan.FromMilliseconds(100);
    public TimeSpan ReconciliationInterval { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>How long a queued admission may go unclaimed before the fleet reclaims it.</summary>
    public TimeSpan AbandonedQueueHorizon { get; init; } =
        SandboxAdmissionReconciliationService.DefaultAbandonedQueueHorizon;

    internal void Validate()
    {
        if (!Enabled)
            return;
        if (PoolCapacities.Count == 0)
            throw new InvalidOperationException("Enabled sandbox admission requires at least one capacity pool.");
        foreach (var (pool, capacity) in PoolCapacities)
        {
            var policy = new ResolvedSandboxAdmissionPolicy
            {
                PoolId = pool,
                TenantWeight = 1,
                MaxConcurrentAttempts = 1,
                MaxQueuedAttempts = 1
            };
            policy.Validate();
            if (capacity <= 0)
                throw new InvalidOperationException("Sandbox admission pool capacities must be positive.");
        }
        if (LeaseDuration <= TimeSpan.Zero || ActivationPollInterval <= TimeSpan.Zero ||
            ReconciliationInterval <= TimeSpan.Zero || AbandonedQueueHorizon <= TimeSpan.Zero)
            throw new InvalidOperationException("Sandbox admission host intervals must be positive.");
        // Reclaiming a queue entry faster than a live node can re-poll it would cancel running work's
        // place in line and call it recovery.
        if (AbandonedQueueHorizon <= ActivationPollInterval)
            throw new InvalidOperationException(
                "The abandoned-queue horizon must be longer than the activation poll interval, " +
                "otherwise a live waiter is reclaimed as abandoned.");
    }

    internal static SandboxAdmissionHostOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("Orchestration:SandboxAdmission");
        var pools = section.GetSection("PoolCapacities").GetChildren().ToDictionary(
            child => child.Key,
            child => int.TryParse(child.Value, out var capacity) ? capacity : 0,
            StringComparer.Ordinal);
        return new SandboxAdmissionHostOptions
        {
            Enabled = section.GetValue("Enabled", false),
            PoolCapacities = pools,
            LeaseDuration = TimeSpan.FromSeconds(section.GetValue("LeaseSeconds", 120d)),
            ActivationPollInterval = TimeSpan.FromMilliseconds(section.GetValue("ActivationPollMilliseconds", 100d)),
            ReconciliationInterval = TimeSpan.FromSeconds(section.GetValue("ReconciliationSeconds", 30d)),
            AbandonedQueueHorizon = TimeSpan.FromSeconds(section.GetValue(
                "AbandonedQueueSeconds",
                SandboxAdmissionReconciliationService.DefaultAbandonedQueueHorizon.TotalSeconds))
        };
    }
}

/// <summary>Long-running, fail-closed retained-admission reconciliation loop.</summary>
public sealed class SandboxAdmissionReconciliationHostedService(
    SandboxAdmissionReconciliationService reconciliation,
    SandboxAdmissionHostOptions options,
    ILogger<SandboxAdmissionReconciliationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await reconciliation.RunOnceAsync(DateTimeOffset.UtcNow, stoppingToken);
                logger.LogInformation(
                    "Sandbox admission reconciliation completed: expired={Expired}, released={Released}, " +
                    "running={Running}, unknown={Unknown}, failures={Failures}, conflicts={Conflicts}, " +
                    "abandonedQueued={AbandonedQueued}.",
                    result.ExpiredRetained, result.DetachedReleased, result.StillRunning,
                    result.Unknown, result.ProbeFailures, result.FenceConflicts,
                    result.AbandonedQueuedCancelled);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Sandbox admission reconciliation pass failed; capacity remains retained.");
            }

            try
            {
                await Task.Delay(options.ReconciliationInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}

public static class SandboxAdmissionServiceCollectionExtensions
{
    /// <summary>
    /// Enables durable sandbox admission for long-running hosts. An enabled host must separately
    /// register its environment-owned <see cref="ISandboxRuntimeReconciler"/>; missing provider
    /// reconciliation is a dependency-injection startup failure, never an optimistic detach result.
    /// </summary>
    public static IServiceCollection AddSandboxAdmissionHosting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = SandboxAdmissionHostOptions.FromConfiguration(configuration);
        options.Validate();
        if (!options.Enabled)
            return services;

        services.AddSingleton(options);
        services.AddSingleton(new SandboxAdmissionControllerOptions
        {
            PoolCapacities = options.PoolCapacities
        });
        services.AddSingleton<FairShareSandboxAdmissionController>();
        services.AddSingleton(sp => new LedgerBackedSandboxAdmissionOptions
        {
            NodeId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}",
            PoolCapacities = options.PoolCapacities,
            LeaseDuration = options.LeaseDuration,
            ActivationPollInterval = options.ActivationPollInterval
        });
        services.AddSingleton<LedgerBackedSandboxAdmissionController>();
        services.AddSingleton<ISandboxAdmissionController>(sp =>
            sp.GetRequiredService<LedgerBackedSandboxAdmissionController>());
        services.AddSingleton(sp => new SandboxAdmissionReconciliationService(
            sp.GetRequiredService<ETL_SQL.Orchestrator.Storage.ISandboxAdmissionLedger>(),
            sp.GetRequiredService<ISandboxRuntimeReconciler>(),
            options.PoolCapacities.Keys.ToArray(),
            options.AbandonedQueueHorizon));
        services.AddHostedService<SandboxAdmissionReconciliationHostedService>();
        return services;
    }
}
