using System.Collections.Concurrent;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Storage;

namespace ETL_SQL.Orchestrator.Execution;

public sealed class LedgerBackedSandboxAdmissionOptions
{
    public required string NodeId { get; init; }
    public required IReadOnlyDictionary<string, int> PoolCapacities { get; init; }
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan ActivationPollInterval { get; init; } = TimeSpan.FromMilliseconds(100);
}

/// <summary>
/// Connects process-local weighted fair ordering to the relational HA authority. Queue intent is
/// durable before local waiting begins; a locally selected attempt runs only after the ledger
/// transaction reserves pool and tenant capacity and returns a fence token.
/// </summary>
public sealed class LedgerBackedSandboxAdmissionController : ISandboxAdmissionController
{
    private readonly FairShareSandboxAdmissionController _fairShare;
    private readonly ISandboxAdmissionLedger _ledger;
    private readonly LedgerBackedSandboxAdmissionOptions _options;
    private readonly ConcurrentDictionary<string, ActiveBinding> _active = new(StringComparer.Ordinal);

    public LedgerBackedSandboxAdmissionController(
        FairShareSandboxAdmissionController fairShare,
        ISandboxAdmissionLedger ledger,
        LedgerBackedSandboxAdmissionOptions options)
    {
        _fairShare = fairShare ?? throw new ArgumentNullException(nameof(fairShare));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.NodeId);
        ArgumentNullException.ThrowIfNull(options.PoolCapacities);
        if (options.LeaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.LeaseDuration));
        if (options.ActivationPollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.ActivationPollInterval));
    }

    public async ValueTask<SandboxAdmissionLease> AcquireAsync(
        TenantContext tenant,
        ResolvedSandboxAdmissionPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        if (!_options.PoolCapacities.TryGetValue(policy.PoolId, out var poolCapacity) || poolCapacity <= 0)
        {
            throw new InvalidOperationException(
                $"Sandbox capacity pool '{policy.PoolId}' is unavailable; admission cannot borrow another pool.");
        }

        var admissionId = Guid.NewGuid().ToString("N");
        if (!await _ledger.EnqueueAsync(admissionId, tenant, policy, cancellationToken))
            throw new InvalidOperationException("A unique durable sandbox admission could not be enqueued.");

        SandboxAdmissionLease? localLease = null;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                localLease = await _fairShare.AcquireAsync(tenant, policy, cancellationToken);
                var fenceToken = await _ledger.TryActivateAsync(
                    admissionId,
                    _options.NodeId,
                    poolCapacity,
                    _options.LeaseDuration,
                    cancellationToken);
                if (fenceToken.HasValue)
                {
                    var binding = new ActiveBinding(localLease, fenceToken.Value);
                    if (!_active.TryAdd(admissionId, binding))
                        throw new InvalidOperationException("The durable admission is already active on this node.");
                    binding.Heartbeat = RenewAsync(admissionId, binding);
                    return new SandboxAdmissionLease(
                        admissionId,
                        tenant.Tenant,
                        policy.PoolId,
                        () => ReleaseActiveAsync(admissionId, binding),
                        binding.LeaseLost.Token);
                }

                await localLease.ReleaseAsync();
                localLease = null;
                await Task.Delay(_options.ActivationPollInterval, cancellationToken);
            }
        }
        catch
        {
            if (localLease is not null)
                await localLease.ReleaseAsync();
            await _ledger.TryCancelQueuedAsync(admissionId, CancellationToken.None);
            throw;
        }
    }

    public async ValueTask<bool> ReleaseReconciledAsync(string admissionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(admissionId);
        _active.TryGetValue(admissionId, out var binding);
        if (binding is not null)
            await StopHeartbeatAsync(binding);

        var entry = await _ledger.ReadAsync(admissionId);
        if (entry is null)
            return false;

        var released = entry.State switch
        {
            SandboxAdmissionState.Active when !string.IsNullOrWhiteSpace(entry.LeaseOwner) =>
                await RetainThenReleaseAsync(entry),
            SandboxAdmissionState.Retained =>
                await _ledger.ReleaseRetainedAsync(admissionId, entry.FenceToken),
            SandboxAdmissionState.Queued =>
                await _ledger.TryCancelQueuedAsync(admissionId),
            SandboxAdmissionState.Completed or SandboxAdmissionState.Cancelled => true,
            _ => false
        };

        if (!released)
            return false;

        if (binding is not null)
        {
            await _fairShare.ReleaseReconciledAsync(binding.LocalLease.AdmissionId);
            _active.TryRemove(admissionId, out _);
            binding.Dispose();
        }
        return true;
    }

    private async ValueTask ReleaseActiveAsync(string admissionId, ActiveBinding binding)
    {
        if (!_active.TryGetValue(admissionId, out var current) || !ReferenceEquals(current, binding))
            return;

        await StopHeartbeatAsync(binding);
        if (!await _ledger.TryCompleteAsync(
                admissionId, _options.NodeId, binding.FenceToken, CancellationToken.None))
        {
            binding.LeaseLost.Cancel();
            throw new InvalidOperationException(
                "Durable sandbox admission completion was not fenced to this node; capacity remains retained.");
        }

        await binding.LocalLease.ReleaseAsync();
        _active.TryRemove(admissionId, out _);
        binding.Dispose();
    }

    private async Task RenewAsync(string admissionId, ActiveBinding binding)
    {
        var interval = TimeSpan.FromTicks(Math.Max(1, _options.LeaseDuration.Ticks / 3));
        try
        {
            while (!binding.StopHeartbeat.IsCancellationRequested)
            {
                await Task.Delay(interval, binding.StopHeartbeat.Token);
                if (!await _ledger.TryRenewAsync(
                        admissionId,
                        _options.NodeId,
                        binding.FenceToken,
                        _options.LeaseDuration,
                        binding.StopHeartbeat.Token))
                {
                    binding.LeaseLost.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (binding.StopHeartbeat.IsCancellationRequested)
        {
        }
        catch
        {
            binding.LeaseLost.Cancel();
        }
    }

    private static async Task StopHeartbeatAsync(ActiveBinding binding)
    {
        binding.StopHeartbeat.Cancel();
        if (binding.Heartbeat is not null)
            await binding.Heartbeat;
    }

    private async Task<bool> RetainThenReleaseAsync(SandboxAdmissionLedgerEntry entry)
    {
        if (!await _ledger.TryRetainAsync(
                entry.AdmissionId,
                entry.LeaseOwner!,
                entry.FenceToken,
                "provider reconciliation confirmed runtime teardown"))
            return false;
        return await _ledger.ReleaseRetainedAsync(entry.AdmissionId, entry.FenceToken);
    }

    private sealed class ActiveBinding(SandboxAdmissionLease localLease, long fenceToken) : IDisposable
    {
        public SandboxAdmissionLease LocalLease { get; } = localLease;
        public long FenceToken { get; } = fenceToken;
        public CancellationTokenSource StopHeartbeat { get; } = new();
        public CancellationTokenSource LeaseLost { get; } = new();
        public Task? Heartbeat { get; set; }

        public void Dispose()
        {
            StopHeartbeat.Dispose();
            LeaseLost.Dispose();
        }
    }
}
