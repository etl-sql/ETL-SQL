using System;

namespace ETL_SQL.Core;
/// <summary>
/// Process-wide arbiter that bounds the <em>total</em> committed in-memory buffer footprint
/// of concurrently executing queries. Each query acquires an <see cref="IMemoryGrantLease"/>
/// and reports its peak buffered size; when the summed footprint of all active leases would
/// exceed the configured budget, an operator is told to spill to disk instead of buffering.
///
/// This complements the per-operator <c>OperatorMemoryGrantMB</c> grant: that bounds a single
/// operator, this bounds the sum across operators and across concurrent Orchestrator jobs.
/// </summary>
public interface IMemoryGrantArbiter
{
    /// <summary>Total budget in bytes. A value &lt;= 0 means "unbounded" (the arbiter never forces a spill).</summary>
    long TotalBudgetBytes { get; }

    /// <summary>Currently reserved bytes summed across all active leases.</summary>
    long ReservedBytes { get; }

    /// <summary>Begins a per-query lease. Dispose releases the query's reserved footprint.</summary>
    IMemoryGrantLease AcquireLease();
}

/// <summary>A per-query reservation slot in an <see cref="IMemoryGrantArbiter"/>.</summary>
public interface IMemoryGrantLease : IDisposable
{
    /// <summary>
    /// Registers this query's current peak buffered footprint and returns <c>true</c> when
    /// keeping it in memory would push the global reserved total over budget — in which case
    /// the caller should spill (the footprint is then NOT grown, since the rows go to disk).
    /// Always returns <c>false</c> for an unbounded arbiter.
    /// </summary>
    bool RegisterAndCheckSpill(long bytes);
}

/// <summary>Default <see cref="IMemoryGrantArbiter"/> implementation with a configurable budget.</summary>
public sealed class MemoryGrantArbiter : IMemoryGrantArbiter
{
    /// <summary>
    /// Process-wide shared instance. Budget defaults to 0 (unbounded) and is set from
    /// <c>Engine:TotalMemoryGrantMB</c> during evaluator initialization. Exposed as the
    /// default <c>IExecutionContext.MemoryArbiter</c> so all execution contexts coordinate
    /// through one budget without per-context wiring.
    /// </summary>
    public static readonly MemoryGrantArbiter Shared = new();

    private readonly object _gate = new();
    private long _reservedBytes;

    /// <summary>Total budget in bytes; &lt;= 0 disables bounding. Settable so the shared instance can be configured at startup.</summary>
    public long TotalBudgetBytes { get; set; }

    public MemoryGrantArbiter(long totalBudgetBytes = 0) => TotalBudgetBytes = totalBudgetBytes;

    public long ReservedBytes { get { lock (_gate) return _reservedBytes; } }

    public IMemoryGrantLease AcquireLease() => new Lease(this);

    private sealed class Lease : IMemoryGrantLease
    {
        private readonly MemoryGrantArbiter _arbiter;
        private long _reported;
        private bool _disposed;

        public Lease(MemoryGrantArbiter arbiter) => _arbiter = arbiter;

        public bool RegisterAndCheckSpill(long bytes)
        {
            if (_arbiter.TotalBudgetBytes <= 0) return false; // unbounded
            if (bytes <= _reported) return false;             // not growing — no new pressure

            lock (_arbiter._gate)
            {
                long prospective = _arbiter._reservedBytes - _reported + bytes;
                if (prospective > _arbiter.TotalBudgetBytes)
                    return true; // staying in memory would breach the budget → spill, do not commit

                _arbiter._reservedBytes += bytes - _reported;
                _reported = bytes;
                return false;
            }
        }

        public void Dispose()
        {
            lock (_arbiter._gate)
            {
                if (_disposed) return;
                _disposed = true;
                _arbiter._reservedBytes -= _reported;
                _reported = 0;
            }
        }
    }
}

/// <summary>An arbiter that never bounds — used wherever no global budget should apply.</summary>
public sealed class UnlimitedMemoryGrantArbiter : IMemoryGrantArbiter
{
    public static readonly UnlimitedMemoryGrantArbiter Instance = new();
    public long TotalBudgetBytes => 0;
    public long ReservedBytes => 0;
    public IMemoryGrantLease AcquireLease() => NoOpLease.Instance;

    private sealed class NoOpLease : IMemoryGrantLease
    {
        public static readonly NoOpLease Instance = new();
        public bool RegisterAndCheckSpill(long bytes) => false;
        public void Dispose() { }
    }
}
