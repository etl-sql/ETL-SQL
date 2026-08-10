using ETL_SQL.Core.Multitenancy;

namespace ETL_SQL.Orchestrator.Execution;

/// <summary>Server-resolved tenant policy for one provider capacity pool.</summary>
public sealed record ResolvedSandboxAdmissionPolicy
{
    public required string PoolId { get; init; }
    public required int TenantWeight { get; init; }
    public required int MaxConcurrentAttempts { get; init; }
    public required int MaxQueuedAttempts { get; init; }

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(PoolId);
        if (PoolId is "." or ".." || PoolId.Length > 128 || PoolId.Contains('/') || PoolId.Contains('\\'))
            throw new ArgumentException("A capacity pool id must be one canonical segment.", nameof(PoolId));
        if (TenantWeight is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(TenantWeight), "Tenant weight must be between 1 and 16.");
        if (MaxConcurrentAttempts <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxConcurrentAttempts));
        if (MaxQueuedAttempts <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxQueuedAttempts));
    }
}

public sealed class SandboxAdmissionControllerOptions
{
    /// <summary>
    /// Provider-owned capacity by pool. A missing pool fails closed; capacity is never borrowed from
    /// another isolation or service-tier pool.
    /// </summary>
    public required IReadOnlyDictionary<string, int> PoolCapacities { get; init; }
}

public sealed class SandboxAdmissionLease
{
    private readonly Func<ValueTask> _release;
    private int _released;

    internal SandboxAdmissionLease(
        string admissionId,
        TenantId tenant,
        string poolId,
        Func<ValueTask> release,
        CancellationToken leaseLost = default)
    {
        AdmissionId = admissionId;
        Tenant = tenant;
        PoolId = poolId;
        _release = release;
        LeaseLost = leaseLost;
    }

    public string AdmissionId { get; }
    public TenantId Tenant { get; }
    public string PoolId { get; }
    /// <summary>Cancelled when durable ownership can no longer be renewed.</summary>
    public CancellationToken LeaseLost { get; }

    public ValueTask ReleaseAsync() =>
        Interlocked.Exchange(ref _released, 1) == 0 ? _release() : ValueTask.CompletedTask;
}

public interface ISandboxAdmissionController
{
    ValueTask<SandboxAdmissionLease> AcquireAsync(
        TenantContext tenant,
        ResolvedSandboxAdmissionPolicy policy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a reservation retained after uncertain runtime teardown. The caller must first prove
    /// through provider reconciliation that the sandbox is stopped and detached.
    /// </summary>
    ValueTask<bool> ReleaseReconciledAsync(string admissionId);
}

/// <summary>
/// Provider-neutral bounded fair admission. Pools are disjoint, per-tenant maximums are enforced,
/// and weighted round-robin limits a tenant to its configured number of consecutive grants while
/// other tenants are waiting.
/// </summary>
public sealed class FairShareSandboxAdmissionController : ISandboxAdmissionController
{
    private readonly object _gate = new();
    private readonly Dictionary<string, PoolState> _pools;
    private readonly Dictionary<string, ActiveLease> _activeLeases = new(StringComparer.Ordinal);

    public FairShareSandboxAdmissionController(SandboxAdmissionControllerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.PoolCapacities);
        if (options.PoolCapacities.Count == 0)
            throw new ArgumentException("At least one sandbox capacity pool is required.", nameof(options));

        _pools = new Dictionary<string, PoolState>(StringComparer.Ordinal);
        foreach (var (poolId, capacity) in options.PoolCapacities)
        {
            var policy = new ResolvedSandboxAdmissionPolicy
            {
                PoolId = poolId,
                TenantWeight = 1,
                MaxConcurrentAttempts = 1,
                MaxQueuedAttempts = 1
            };
            policy.Validate();
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "Pool capacity must be positive.");
            _pools.Add(poolId, new PoolState(capacity));
        }
    }

    public ValueTask<SandboxAdmissionLease> AcquireAsync(
        TenantContext tenant,
        ResolvedSandboxAdmissionPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        Waiter waiter;
        lock (_gate)
        {
            if (!_pools.TryGetValue(policy.PoolId, out var pool))
            {
                throw new InvalidOperationException(
                    $"Sandbox capacity pool '{policy.PoolId}' is unavailable; admission cannot borrow another pool.");
            }

            var tenantId = tenant.Tenant.Value;
            if (!pool.Tenants.TryGetValue(tenantId, out var tenantState))
            {
                tenantState = new TenantState(policy);
                pool.Tenants.Add(tenantId, tenantState);
            }
            else if (tenantState.Weight != policy.TenantWeight ||
                     tenantState.MaxConcurrent != policy.MaxConcurrentAttempts ||
                     tenantState.MaxQueued != policy.MaxQueuedAttempts)
            {
                throw new InvalidOperationException(
                    "Admission policy changed while tenant work is active or queued; fence and reconcile the old policy first.");
            }

            if (tenantState.Queued >= tenantState.MaxQueued)
                throw new InvalidOperationException("The tenant sandbox queue is at its configured limit.");

            waiter = new Waiter(tenant.Tenant, policy.PoolId);
            tenantState.Queue.Enqueue(waiter);
            tenantState.Queued++;
            if (!tenantState.InRotation)
            {
                pool.Rotation.AddLast(tenantId);
                tenantState.InRotation = true;
            }

            Dispatch(pool, policy.PoolId);
        }

        if (cancellationToken.CanBeCanceled)
        {
            waiter.Cancellation = cancellationToken.Register(() => Cancel(waiter, cancellationToken));
            if (waiter.Completion.Task.IsCompleted)
                waiter.Cancellation.Dispose();
        }

        return AwaitLeaseAsync(waiter);
    }

    private async ValueTask<SandboxAdmissionLease> AwaitLeaseAsync(Waiter waiter)
    {
        var lease = await waiter.Completion.Task;
        waiter.Cancellation.Dispose();
        return lease;
    }

    private void Cancel(Waiter waiter, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (waiter.State != WaiterState.Queued)
                return;
            waiter.State = WaiterState.Cancelled;
            var pool = _pools[waiter.PoolId];
            var tenant = pool.Tenants[waiter.Tenant.Value];
            tenant.Queued--;
            waiter.Completion.TrySetCanceled(cancellationToken);
            Dispatch(pool, waiter.PoolId);
        }
    }

    private void Dispatch(PoolState pool, string poolId)
    {
        while (pool.Active < pool.Capacity && pool.Rotation.Count > 0)
        {
            var candidates = pool.Rotation.Count;
            var granted = false;
            for (var index = 0; index < candidates; index++)
            {
                var tenantId = pool.Rotation.First!.Value;
                pool.Rotation.RemoveFirst();
                var tenant = pool.Tenants[tenantId];
                PruneCancelled(tenant);

                if (tenant.Queue.Count == 0)
                {
                    tenant.InRotation = false;
                    tenant.Credits = 0;
                    RemoveTenantWhenIdle(pool, tenantId, tenant);
                    continue;
                }

                if (tenant.Active >= tenant.MaxConcurrent)
                {
                    pool.Rotation.AddLast(tenantId);
                    continue;
                }

                if (tenant.Credits == 0)
                    tenant.Credits = tenant.Weight;

                var waiter = tenant.Queue.Dequeue();
                tenant.Queued--;
                tenant.Active++;
                tenant.Credits--;
                pool.Active++;
                waiter.State = WaiterState.Admitted;

                PruneCancelled(tenant);
                if (tenant.Queue.Count > 0)
                {
                    if (tenant.Credits > 0)
                        pool.Rotation.AddFirst(tenantId);
                    else
                        pool.Rotation.AddLast(tenantId);
                }
                else
                {
                    tenant.InRotation = false;
                    tenant.Credits = 0;
                }

                var admissionId = Guid.NewGuid().ToString("N");
                var lease = new SandboxAdmissionLease(
                    admissionId,
                    waiter.Tenant,
                    poolId,
                    () => ReleaseAsync(admissionId));
                _activeLeases.Add(admissionId, new ActiveLease(poolId, tenantId));
                waiter.Completion.TrySetResult(lease);
                granted = true;
                break;
            }

            if (!granted)
                break;
        }
    }

    public ValueTask<bool> ReleaseReconciledAsync(string admissionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(admissionId);
        return ReleaseCoreAsync(admissionId);
    }

    private async ValueTask ReleaseAsync(string admissionId) =>
        _ = await ReleaseCoreAsync(admissionId);

    private ValueTask<bool> ReleaseCoreAsync(string admissionId)
    {
        lock (_gate)
        {
            if (!_activeLeases.Remove(admissionId, out var activeLease))
                return ValueTask.FromResult(false);

            var pool = _pools[activeLease.PoolId];
            var tenant = pool.Tenants[activeLease.TenantId];
            tenant.Active--;
            pool.Active--;
            if (tenant.Active < 0 || pool.Active < 0)
                throw new InvalidOperationException("Sandbox admission capacity was released more than once.");

            if (tenant.Queue.Count > 0 && !tenant.InRotation)
            {
                pool.Rotation.AddLast(activeLease.TenantId);
                tenant.InRotation = true;
            }

            RemoveTenantWhenIdle(pool, activeLease.TenantId, tenant);
            Dispatch(pool, activeLease.PoolId);
        }
        return ValueTask.FromResult(true);
    }

    private static void PruneCancelled(TenantState tenant)
    {
        while (tenant.Queue.TryPeek(out var waiter) && waiter.State == WaiterState.Cancelled)
            tenant.Queue.Dequeue();
    }

    private static void RemoveTenantWhenIdle(PoolState pool, string tenantId, TenantState tenant)
    {
        if (tenant.Active == 0 && tenant.Queued == 0 && tenant.Queue.Count == 0 && !tenant.InRotation)
            pool.Tenants.Remove(tenantId);
    }

    private sealed class PoolState(int capacity)
    {
        public int Capacity { get; } = capacity;
        public int Active { get; set; }
        public Dictionary<string, TenantState> Tenants { get; } = new(StringComparer.Ordinal);
        public LinkedList<string> Rotation { get; } = [];
    }

    private sealed class TenantState(ResolvedSandboxAdmissionPolicy policy)
    {
        public int Weight { get; } = policy.TenantWeight;
        public int MaxConcurrent { get; } = policy.MaxConcurrentAttempts;
        public int MaxQueued { get; } = policy.MaxQueuedAttempts;
        public int Active { get; set; }
        public int Queued { get; set; }
        public int Credits { get; set; }
        public bool InRotation { get; set; }
        public Queue<Waiter> Queue { get; } = new();
    }

    private sealed class Waiter(TenantId tenant, string poolId)
    {
        public TenantId Tenant { get; } = tenant;
        public string PoolId { get; } = poolId;
        public WaiterState State { get; set; } = WaiterState.Queued;
        public TaskCompletionSource<SandboxAdmissionLease> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenRegistration Cancellation { get; set; }
    }

    private enum WaiterState
    {
        Queued,
        Admitted,
        Cancelled
    }

    private sealed record ActiveLease(string PoolId, string TenantId);
}
