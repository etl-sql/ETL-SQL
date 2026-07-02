using System;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

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
    public static long Ceiling(IExecutionContext ctx)
    {
        var processBytes = ctx.MemoryArbiter?.TotalBudgetBytes ?? 0;
        var operatorBytes = ctx.OperatorMemoryGrantMB > 0
            ? (long)ctx.OperatorMemoryGrantMB * 1024 * 1024
            : 0;
        if (processBytes <= 0) return operatorBytes;
        if (operatorBytes <= 0) return processBytes;
        return Math.Min(processBytes, operatorBytes);
    }

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
/// Bounds an in-memory build by <b>precise byte accounting</b>: callers add an estimated tuple
/// footprint as each row enters the build (via <see cref="Add"/>) and the guard trips deterministically
/// once the running total crosses the ceiling. This replaces the old GC-heap sampling approach, which
/// was process-wide (it could not attribute memory to this operator and was perturbed by other
/// operators and uncollected garbage) and reactive (it detected pressure only <i>after</i> over-allocating).
/// Disabled (always returns false) when constructed with a ceiling &lt;= 0.
/// </summary>
internal sealed class MemoryBudgetGuard
{
    private readonly long _ceiling;
    private long _bytes;

    public MemoryBudgetGuard(long ceilingBytes)
    {
        _ceiling = ceilingBytes;
    }

    /// <summary>True when a ceiling is configured (governor on).</summary>
    public bool Enabled => _ceiling > 0;

    /// <summary>Bytes accumulated against the budget so far.</summary>
    public long BytesAccumulated => _bytes;

    /// <summary>Adds an estimated footprint (in bytes) for a tuple just added to the in-memory build.</summary>
    public void Add(long bytes)
    {
        if (_ceiling > 0 && bytes > 0) _bytes += bytes;
    }

    /// <summary>Resets the running total (call after a flush/spill that frees the buffered set).</summary>
    public void Reset() => _bytes = 0;

    /// <summary>Returns true once accumulated bytes exceed the ceiling.</summary>
    public bool Exceeded() => _ceiling > 0 && _bytes > _ceiling;
}

/// <summary>
/// Byte-footprint estimates for the build structures the external engines hold in memory (hash-table
/// keys and sort/dedup key arrays). Reuses <see cref="Row.EstimateValueBytes"/> so every estimate
/// uses the same per-value sizing.
/// </summary>
internal static class RowMemory
{
    /// <summary>Estimated managed-heap cost of a <see cref="CompoundKey"/> held in a hash set / dictionary.</summary>
    public static long EstimateKeyBytes(CompoundKey key)
    {
        long bytes = 32; // CompoundKey object + values array header (approximate)
        for (int i = 0; i < key.Length; i++)
            bytes += 8 + Row.EstimateValueBytes(key[i]);
        return bytes;
    }

    /// <summary>Estimated managed-heap cost of a raw value array (e.g. a sort key tuple).</summary>
    public static long EstimateValuesBytes(object?[] values)
    {
        long bytes = 24; // array header (approximate)
        for (int i = 0; i < values.Length; i++)
            bytes += 8 + Row.EstimateValueBytes(values[i]);
        return bytes;
    }
}
