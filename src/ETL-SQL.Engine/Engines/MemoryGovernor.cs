using System;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Engines;

/// <summary>
/// Shared RAM-governor helpers for the external (disk-spilling) engines. The ceiling is the
/// process-wide grant pool (<c>Engine:TotalMemoryGrantMB</c>, via <see cref="IMemoryGrantArbiter"/>);
/// 0 means unbounded (governor off). Behaviour when an operator cannot relieve pressure is set by
/// <see cref="IExecutionContext.MemoryGovernorPolicy"/>.
/// </summary>
internal static class MemoryGovernor
{
    /// <summary>The per-operator memory ceiling in bytes. 0 (or less) = disabled.</summary>
    public static long Ceiling(IExecutionContext ctx) => ctx.MemoryArbiter?.TotalBudgetBytes ?? 0;

    /// <summary>
    /// Applied when an operator has exhausted its ability to relieve memory pressure (e.g. a
    /// partition that will not split further). <see cref="MemoryGovernorPolicy.SpillOrFail"/> throws
    /// a clear error rather than letting the build consume all machine RAM;
    /// <see cref="MemoryGovernorPolicy.SpillOnly"/> returns so the caller can churn to completion.
    /// </summary>
    public static void EnforcePolicy(IExecutionContext ctx, string message)
    {
        if (ctx.MemoryGovernorPolicy == MemoryGovernorPolicy.SpillOrFail)
            throw new ExecutionException(message);
    }
}

/// <summary>
/// Samples managed-heap growth since construction (or the last <see cref="Reset"/>), checking at most
/// once per <see cref="Interval"/> calls so the per-row overhead is negligible. Lets the external
/// engines bound an in-memory build by actual memory, not just a row-count threshold. Disabled (always
/// returns false) when constructed with a ceiling &lt;= 0.
/// </summary>
internal sealed class HeapGrowthGuard
{
    private const int Interval = 8192;
    private readonly long _ceiling;
    private long _baseline;
    private int _sinceCheck;

    public HeapGrowthGuard(long ceilingBytes)
    {
        _ceiling = ceilingBytes;
        _baseline = ceilingBytes > 0 ? GC.GetTotalMemory(false) : 0;
    }

    /// <summary>Re-baselines growth tracking (call after a flush/spill that frees the buffered set).</summary>
    public void Reset()
    {
        if (_ceiling > 0) _baseline = GC.GetTotalMemory(false);
        _sinceCheck = 0;
    }

    /// <summary>Call once per accumulated row; returns true when heap growth has exceeded the ceiling.</summary>
    public bool Exceeded()
    {
        if (_ceiling <= 0) return false;
        if (++_sinceCheck < Interval) return false;
        _sinceCheck = 0;
        return GC.GetTotalMemory(false) - _baseline > _ceiling;
    }
}
