using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Data;

/// <summary>
/// One completed run's recorded metrics, as read back for <c>HISTORICAL</c> baselines.
/// </summary>
public sealed record JobRunMetrics(
    long RowsProcessed,
    long RowsQuarantined,
    long RowsWarned);

/// <summary>One completed run's column-level metric for a specific sink column.</summary>
public sealed record ColumnRunMetrics(
    string? TargetTable,
    string ColumnName,
    long TotalRows,
    long NullRows);

/// <summary>
/// Last observed outcome for one <c>ASSERT JOB ... ALERT</c> assertion. Hosts persist this by
/// job/assertion key so alerting can notify on transitions rather than every repeated failure.
/// </summary>
public sealed record AssertJobAlertState(
    bool LastFailed,
    DateTimeOffset? LastFailureAlertedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Narrow Engine→Orchestrator seam giving <c>ASSERT JOB … WITHIN … OF HISTORICAL</c> access to
/// previous runs' recorded metrics. Statement handlers live in the engine and only see
/// <see cref="IExecutionContext"/>; the job-history store lives in the orchestrator. This
/// interface is the whole contract between them.
///
/// It is deliberately absent (null on the context) in pure-engine and CLI contexts: there,
/// <c>HISTORICAL</c> predicates fail with a clear message, while every collector-backed predicate
/// (<c>NULL_PERCENT</c>, <c>QUARANTINE_PERCENT</c>, plain <c>ROW_COUNT</c> compares) still works.
/// </summary>
public interface IJobMetricsProvider
{
    /// <summary>
    /// Returns the most recent successfully completed runs of <paramref name="jobName"/>, newest
    /// first, capped at <paramref name="limit"/>. Runs that failed or are still in flight are
    /// excluded — a failed run's metrics are not a baseline.
    /// </summary>
    Task<IReadOnlyList<JobRunMetrics>> GetRecentRunMetricsAsync(
        string jobName, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns recent successfully completed per-column metrics for <paramref name="columnName"/>,
    /// optionally narrowed to one sink target. Missing storage in an older orchestrator returns an
    /// empty list rather than failing a rolling upgrade.
    /// </summary>
    Task<IReadOnlyList<ColumnRunMetrics>> GetRecentColumnMetricsAsync(
        string jobName,
        string? targetTable,
        string columnName,
        int limit,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ColumnRunMetrics>>(Array.Empty<ColumnRunMetrics>());

    Task<AssertJobAlertState?> GetAssertJobAlertStateAsync(
        string jobName,
        string assertionKey,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<AssertJobAlertState?>(null);

    Task SaveAssertJobAlertStateAsync(
        string jobName,
        string assertionKey,
        AssertJobAlertState state,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
