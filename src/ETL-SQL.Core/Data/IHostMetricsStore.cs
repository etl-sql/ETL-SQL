using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Data;

/// <summary>
/// One sampled point of whole-host utilization for a node, distinct from per-job process cost.
/// Answers "is the box running out of headroom" (memory, CPU, and — most outage-critical — free disk).
/// </summary>
public sealed record HostMetricSample(
    string NodeId,
    DateTime CapturedAt,
    double MemoryLoadPercent,
    double ProcessCpuPercent,
    double? HostCpuPercent,
    long StateDiskFreeBytes,
    long SpillDiskFreeBytes);

/// <summary>
/// Append-only time series of host utilization samples. Kept separate from <see cref="IJobHistoryStore"/>
/// so that store stays cohesive. See Docs/Design/HostUtilizationAndCapacityPlanning.md.
/// </summary>
public interface IHostMetricsStore
{
    /// <summary>Records one host-utilization sample.</summary>
    Task AppendHostMetricAsync(HostMetricSample sample);

    /// <summary>
    /// Returns samples captured at or after <paramref name="since"/>, newest first, optionally for a
    /// single node. Capped by <paramref name="limit"/>.
    /// </summary>
    Task<IReadOnlyList<HostMetricSample>> GetHostMetricsAsync(string? nodeId, DateTime since, int limit = 1000);

    /// <summary>Deletes samples older than <paramref name="maxAge"/>; returns rows removed.</summary>
    Task<int> PruneHostMetricsAsync(TimeSpan maxAge);
}
