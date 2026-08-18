using System.Collections.Concurrent;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Resolves how many concurrent report executions one tenant may hold. Null means the tenant has no
/// per-tenant ceiling — a single-tenant deployment, or a tenant with no provisioned Shared record —
/// and only the node-wide execution cap applies.
/// </summary>
public interface ITenantExecutionQuotaSource
{
    ValueTask<int?> GetMaxConcurrentExecutionsAsync(string tenantId, CancellationToken cancellationToken);
}

/// <summary>
/// Per-tenant ceiling on concurrent interactive/refresh executions in a Shared deployment.
///
/// <para>The node-wide <c>MaxConcurrentReportExecutions</c> cap is not tenant-aware: without this,
/// one tenant's sessions can occupy every slot on the node and the <c>MaxReportSessions</c> quota
/// recorded when the tenant was provisioned is never read back at runtime.</para>
///
/// <para>The limit is read at each admission rather than baked into a fixed semaphore, so a tenant
/// upgrade takes effect on the next execution instead of being pinned to whatever the ceiling was
/// when the node first saw that tenant.</para>
/// </summary>
public sealed class TenantExecutionAdmission
{
    private readonly ConcurrentDictionary<string, TenantState> _tenants = new(StringComparer.Ordinal);

    /// <summary>
    /// Waits until the tenant is below <paramref name="limit"/> concurrent executions. Dispose the
    /// returned permit to release the slot. A non-positive limit means "no ceiling".
    /// </summary>
    public Task<IDisposable> AcquireAsync(string tenantId, int limit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (limit <= 0)
            return Task.FromResult<IDisposable>(NullPermit.Instance);

        cancellationToken.ThrowIfCancellationRequested();
        var state = _tenants.GetOrAdd(tenantId, _ => new TenantState());
        Waiter waiter;
        lock (state.Sync)
        {
            // Queued work goes first even when a later caller would fit, so a steady arrival rate
            // cannot starve whoever is already waiting.
            if (state.Active < limit && state.Waiters.Count == 0)
            {
                state.Active++;
                return Task.FromResult<IDisposable>(new Permit(this, tenantId));
            }

            waiter = new Waiter(limit);
            state.Waiters.Enqueue(waiter);
        }

        return AwaitPermitAsync(state, waiter, cancellationToken);
    }

    /// <summary>Concurrent executions this node currently attributes to a tenant.</summary>
    public int ActiveFor(string tenantId)
    {
        if (!_tenants.TryGetValue(tenantId, out var state))
            return 0;
        lock (state.Sync)
        {
            return state.Active;
        }
    }

    private async Task<IDisposable> AwaitPermitAsync(
        TenantState state,
        Waiter waiter,
        CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(() =>
        {
            lock (state.Sync)
            {
                // The waiter stays in the queue; it is skipped on dispatch once completed. Removing it
                // here would need an O(n) rebuild under the lock on every cancellation.
                waiter.Completion.TrySetCanceled(cancellationToken);
            }
        });
        return await waiter.Completion.Task.ConfigureAwait(false);
    }

    private void Release(string tenantId)
    {
        if (!_tenants.TryGetValue(tenantId, out var state))
            return;

        List<Waiter>? admitted = null;
        lock (state.Sync)
        {
            state.Active--;

            // Each waiter carries the ceiling it was admitted against, so dispatch reflects the
            // tenant's current quota: a raised ceiling releases more of the queue at once, and a
            // lowered one holds the rest back instead of handing over a slot that no longer exists.
            while (state.Waiters.Count > 0)
            {
                var candidate = state.Waiters.Peek();
                if (candidate.Completion.Task.IsCompleted)
                {
                    state.Waiters.Dequeue(); // cancelled while queued
                    continue;
                }

                if (state.Active >= candidate.Limit)
                    break;

                state.Waiters.Dequeue();
                state.Active++;
                (admitted ??= []).Add(candidate);
            }

            if (state.Active == 0 && state.Waiters.Count == 0)
                _tenants.TryRemove(tenantId, out _);
        }

        if (admitted is null) return;
        foreach (var waiter in admitted)
        {
            if (!waiter.Completion.TrySetResult(new Permit(this, tenantId)))
                Release(tenantId); // it was cancelled between the peek and the hand-off
        }
    }

    /// <summary>Every field is read and written under <see cref="Sync"/>.</summary>
    private sealed class TenantState
    {
        public readonly object Sync = new();
        public readonly Queue<Waiter> Waiters = new();
        public int Active;
    }

    private sealed class Waiter(int limit)
    {
        public int Limit { get; } = limit;
        public TaskCompletionSource<IDisposable> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Permit(TenantExecutionAdmission owner, string tenantId) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                owner.Release(tenantId);
        }
    }

    private sealed class NullPermit : IDisposable
    {
        public static readonly NullPermit Instance = new();
        public void Dispose() { }
    }
}
