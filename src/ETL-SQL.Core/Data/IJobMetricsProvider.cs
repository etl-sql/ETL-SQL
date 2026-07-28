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
/// Last observed outcome for one <c>ASSERT JOB ... NOTIFY</c> assertion. Hosts persist this by
/// job/assertion key so notification delivery can be transition-based rather than firing on every
/// repeated failure.
/// </summary>
public sealed record AssertJobAlertState(
    bool LastFailed,
    DateTimeOffset? LastFailureAlertedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Durable replay metadata for a quarantine target. V2 replay resolves this manifest, reads
/// released rows from the quarantine table, strips engine-owned <c>__dq_*</c> columns, and resumes
/// the recorded section label with the quarantine rows substituted for the original source.
/// </summary>
public sealed record QuarantineReplayManifest(
    string JobName,
    string? ScriptPath,
    string? SectionLabel,
    string SourceTable,
    string QuarantineTarget,
    bool IsReplayable,
    string? NonReplayableReason,
    IReadOnlyList<string> InputColumns,
    string InputSchemaFingerprint,
    DateTimeOffset UpdatedAtUtc,
    string ReplayMode = "single-table",
    string? ProbeSourceTable = null,
    string? JoinBuildTable = null,
    bool? JoinObservedN1 = null,
    string? JoinNonReplayableReason = null);

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

    Task<QuarantineReplayManifest?> GetQuarantineReplayManifestAsync(
        string jobName,
        string quarantineTarget,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<QuarantineReplayManifest?>(null);

    Task SaveQuarantineReplayManifestAsync(
        QuarantineReplayManifest manifest,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    Task<bool> TryAcquireQuarantineReplayLeaseAsync(
        string jobName,
        string quarantineTarget,
        string owner,
        TimeSpan ttl,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    Task ReleaseQuarantineReplayLeaseAsync(
        string jobName,
        string quarantineTarget,
        string owner,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
