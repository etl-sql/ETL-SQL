using ETL_SQL.Orchestrator.Storage;

namespace ETL_SQL.Orchestrator.Execution;

public enum SandboxRuntimeReconciliationState
{
    Detached,
    Running,
    Unknown
}

/// <summary>
/// Environment-owned runtime probe. Implementations locate provider work by the server-generated
/// admission id and must return <see cref="SandboxRuntimeReconciliationState.Detached"/> only after
/// proving the runtime is stopped and all assignment mounts are detached.
/// </summary>
public interface ISandboxRuntimeReconciler
{
    Task<SandboxRuntimeReconciliationState> ProbeAsync(
        SandboxAdmissionLedgerEntry admission,
        CancellationToken cancellationToken);
}

public sealed record SandboxReconciliationResult(
    int ExpiredRetained,
    int DetachedReleased,
    int StillRunning,
    int Unknown,
    int ProbeFailures,
    int FenceConflicts,
    int AbandonedQueuedCancelled = 0);

/// <summary>
/// Performs one provider-neutral reconciliation pass. Lease expiry alone never releases capacity;
/// retained work is released only after the runtime provider positively proves detachment.
/// </summary>
public sealed class SandboxAdmissionReconciliationService
{
    /// <summary>
    /// How long a queued admission may go unclaimed before the fleet treats it as abandoned. It is far
    /// longer than any dispatch poll interval, so a live waiter is never mistaken for a dead one.
    /// </summary>
    public static readonly TimeSpan DefaultAbandonedQueueHorizon = TimeSpan.FromMinutes(10);

    private readonly ISandboxAdmissionLedger _ledger;
    private readonly ISandboxRuntimeReconciler _runtime;
    private readonly IReadOnlyList<string> _poolIds;
    private readonly TimeSpan _abandonedQueueHorizon;

    public SandboxAdmissionReconciliationService(
        ISandboxAdmissionLedger ledger,
        ISandboxRuntimeReconciler runtime,
        IReadOnlyCollection<string> poolIds,
        TimeSpan? abandonedQueueHorizon = null)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        ArgumentNullException.ThrowIfNull(poolIds);
        _poolIds = poolIds.Distinct(StringComparer.Ordinal).ToArray();
        if (_poolIds.Count == 0 || _poolIds.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one nonblank sandbox capacity pool is required.", nameof(poolIds));
        _abandonedQueueHorizon = abandonedQueueHorizon ?? DefaultAbandonedQueueHorizon;
        if (_abandonedQueueHorizon <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(abandonedQueueHorizon), "The abandoned-queue horizon must be positive.");
    }

    public async Task<SandboxReconciliationResult> RunOnceAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var expired = await _ledger.RetainExpiredAsync(
            now,
            "admission lease expired; runtime reconciliation required",
            cancellationToken);
        var abandoned = await _ledger.CancelAbandonedQueuedAsync(
            now,
            _abandonedQueueHorizon,
            "queued admission abandoned; no node claimed it within the recovery horizon",
            cancellationToken);
        var released = 0;
        var running = 0;
        var unknown = 0;
        var probeFailures = 0;
        var fenceConflicts = 0;

        foreach (var poolId in _poolIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var open = await _ledger.ListOpenAsync(poolId, cancellationToken);
            foreach (var admission in open.Where(entry => entry.State == SandboxAdmissionState.Retained))
            {
                SandboxRuntimeReconciliationState state;
                try
                {
                    state = await _runtime.ProbeAsync(admission, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    probeFailures++;
                    continue;
                }

                switch (state)
                {
                    case SandboxRuntimeReconciliationState.Detached:
                        if (await _ledger.ReleaseRetainedAsync(
                                admission.AdmissionId, admission.FenceToken, cancellationToken))
                            released++;
                        else
                            fenceConflicts++;
                        break;
                    case SandboxRuntimeReconciliationState.Running:
                        running++;
                        break;
                    default:
                        unknown++;
                        break;
                }
            }
        }

        return new SandboxReconciliationResult(
            expired, released, running, unknown, probeFailures, fenceConflicts, abandoned);
    }
}
