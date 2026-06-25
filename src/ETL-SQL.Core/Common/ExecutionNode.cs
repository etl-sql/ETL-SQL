using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace ETL_SQL.Core.Common;
/// <summary>
/// Represents a single statement or block in the execution tree.
/// Thread-safe for parallel execution tracking.
/// </summary>
public class ExecutionNode
{
    private long _rowsProcessed;

    /// <summary>The current active node for the current logical task/thread.</summary>
    public static AsyncLocal<ExecutionNode?> Current { get; } = new();

    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public ExecutionStatus Status { get; set; } = ExecutionStatus.Waiting;

    /// <summary>The number of rows processed by this statement.</summary>
    public long RowsProcessed
    {
        get => Interlocked.Read(ref _rowsProcessed);
        set => Interlocked.Exchange(ref _rowsProcessed, value);
    }

    /// <summary>Ticks when the task started (Stopwatch.GetTimestamp()).</summary>
    public long StartTicks { get; set; }

    /// <summary>Ticks when the task ended (Stopwatch.GetTimestamp()).</summary>
    public long? EndTicks { get; set; }

    /// <summary>IDs of child nodes (sequential or nested statements).</summary>
    public List<Guid> ChildIds { get; } = new();

    /// <summary>Optional error message if the node faulted.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Number of times this node has been restated (reused) during loops.</summary>
    public int IterationCount { get; set; } = 1;

    /// <summary>True when this node is a PARALLEL container whose children run concurrently.</summary>
    public bool IsParallelBlock { get; set; }

    /// <summary>Increments the processed row count atomically.</summary>
    public void IncrementRows(long count = 1) => Interlocked.Add(ref _rowsProcessed, count);

    /// <summary>Calculates elapsed duration in milliseconds based on current or end ticks.</summary>
    public double GetElapsedMs()
    {
        if (StartTicks == 0) return 0;
        var end = EndTicks ?? Stopwatch.GetTimestamp();
        return (end - StartTicks) * 1000.0 / Stopwatch.Frequency;
    }

    /// <summary>Calculates velocity (rows per second).</summary>
    public double GetVelocity()
    {
        var ms = GetElapsedMs();
        if (ms <= 0) return 0;
        return RowsProcessed * 1000.0 / ms;
    }
}
