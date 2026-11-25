using System.Collections.Concurrent;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Serializes in-process concurrent requests sharing the same (tenant, user, pipeline, idempotencyKey)
/// and stamps each durable <c>Pending</c> claim with a per-process <see cref="InstanceId"/> so rows
/// left <c>Pending</c> across a Portal restart are detected as <c>Unknown</c> without redispatch.
/// </summary>
public sealed class StudioRunIdempotencyCoordinator
{
    public string InstanceId { get; } = Guid.NewGuid().ToString("N");

    private readonly ConcurrentDictionary<string, RefCountedGate> _gates = new(StringComparer.Ordinal);

    public static string BuildKey(string tenantId, int userId, int pipelineId, string idempotencyKey) =>
        $"{tenantId}|{userId}|{pipelineId}|{idempotencyKey}";

    public async Task<IDisposable> AcquireAsync(string compositeKey, CancellationToken ct)
    {
        RefCountedGate gate;
        while (true)
        {
            gate = _gates.GetOrAdd(compositeKey, static _ => new RefCountedGate());
            if (gate.TryAddRef())
                break;
        }

        try
        {
            await gate.Semaphore.WaitAsync(ct);
            return new GateLease(this, compositeKey, gate);
        }
        catch
        {
            ReleaseRef(compositeKey, gate);
            throw;
        }
    }

    private void ReleaseRef(string compositeKey, RefCountedGate gate)
    {
        if (gate.ReleaseRef())
        {
            _gates.TryRemove(new KeyValuePair<string, RefCountedGate>(compositeKey, gate));
            gate.Semaphore.Dispose();
        }
    }

    private sealed class RefCountedGate
    {
        private int _refCount = 1;
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public bool TryAddRef()
        {
            while (true)
            {
                var current = Volatile.Read(ref _refCount);
                if (current <= 0)
                    return false;
                if (Interlocked.CompareExchange(ref _refCount, current + 1, current) == current)
                    return true;
            }
        }

        public bool ReleaseRef() => Interlocked.Decrement(ref _refCount) == 0;
    }

    private sealed class GateLease(
        StudioRunIdempotencyCoordinator owner,
        string compositeKey,
        RefCountedGate gate) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            gate.Semaphore.Release();
            owner.ReleaseRef(compositeKey, gate);
        }
    }
}
