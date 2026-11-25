namespace ETL_SQL.Portal.Services;

/// <summary>Coalesces same-key submissions on this Portal. The database claim coordinates across instances.</summary>
public sealed class StudioRunIdempotencyCoordinator
{
    public string InstanceId { get; } = Guid.NewGuid().ToString("N");

    private readonly Dictionary<(string TenantId, int UserId, int PipelineId, string Key), Gate> _gates = [];

    internal int ActiveGateCount
    {
        get { lock (_gates) return _gates.Count; }
    }

    public async Task<IDisposable> AcquireAsync(
        string tenantId, int userId, int pipelineId, string key, CancellationToken ct)
    {
        var scope = (tenantId, userId, pipelineId, key);
        Gate gate;
        lock (_gates)
        {
            if (!_gates.TryGetValue(scope, out gate!))
            {
                gate = new Gate();
                _gates.Add(scope, gate);
            }
            gate.References++;
        }

        try
        {
            await gate.Semaphore.WaitAsync(ct);
            return new GateLease(this, scope, gate);
        }
        catch
        {
            ReleaseReference(scope, gate);
            throw;
        }
    }

    private void ReleaseReference((string TenantId, int UserId, int PipelineId, string Key) scope, Gate gate)
    {
        lock (_gates)
        {
            if (--gate.References == 0)
            {
                _gates.Remove(scope);
                gate.Semaphore.Dispose();
            }
        }
    }

    private sealed class Gate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int References { get; set; }
    }

    private sealed class GateLease(
        StudioRunIdempotencyCoordinator owner,
        (string TenantId, int UserId, int PipelineId, string Key) scope,
        Gate gate) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            gate.Semaphore.Release();
            owner.ReleaseReference(scope, gate);
        }
    }
}
