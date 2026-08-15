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
    long NullRows,
    DateTimeOffset? MaxTimestampUtc = null);

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
    string? JoinNonReplayableReason = null,
    // ── Target provenance, written at capture time ───────────────────────────
    // Appended, all nullable, so a manifest written by an older engine still deserializes. Absent
    // means "unknown", which classifies the target as view-only — the same backward-compatibility
    // shape the replay-mode fields above used. Guessing provenance from the target string would
    // mean opening a production connection on an inference.
    /// <summary>Shared-connection alias the target lives behind, when it is catalog-backed.</summary>
    string? TargetConnectionAlias = null,
    /// <summary>Connector type of that alias, needed to bootstrap a preview session.</summary>
    string? TargetConnectorType = null,
    /// <summary>
    /// True only when capture proved the alias came from the governed shared-connection catalog.
    /// A script-local connection is not previewable: the Portal has no governed way to open it.
    /// </summary>
    bool? TargetIsCatalogBacked = null);

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
    // Every method here is addressed by job *name*, because that is what the script says:
    // ASSERT JOB 'daily_load' names a job the author typed. A name identifies a job only within a
    // tenant, so each call carries the tenant the script is running in, and the implementation
    // resolves the pair to an identity exactly once. Nothing below this seam sees a name.

    /// <summary>
    /// Returns the most recent successfully completed runs of <paramref name="jobName"/> in
    /// <paramref name="tenantId"/> (null being the unbound Solo scope), newest first, capped at
    /// <paramref name="limit"/>. Runs that failed or are still in flight are excluded — a failed
    /// run's metrics are not a baseline.
    /// </summary>
    Task<IReadOnlyList<JobRunMetrics>> GetRecentRunMetricsAsync(
        string? tenantId, string jobName, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns recent successfully completed per-column metrics for <paramref name="columnName"/>,
    /// optionally narrowed to one sink target. Missing storage in an older orchestrator returns an
    /// empty list rather than failing a rolling upgrade.
    /// </summary>
    Task<IReadOnlyList<ColumnRunMetrics>> GetRecentColumnMetricsAsync(
        string? tenantId,
        string jobName,
        string? targetTable,
        string columnName,
        int limit,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ColumnRunMetrics>>(Array.Empty<ColumnRunMetrics>());

    Task<AssertJobAlertState?> GetAssertJobAlertStateAsync(
        string? tenantId,
        string jobName,
        string assertionKey,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<AssertJobAlertState?>(null);

    Task SaveAssertJobAlertStateAsync(
        string? tenantId,
        string jobName,
        string assertionKey,
        AssertJobAlertState state,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    Task<QuarantineReplayManifest?> GetQuarantineReplayManifestAsync(
        string? tenantId,
        string jobName,
        string quarantineTarget,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<QuarantineReplayManifest?>(null);

    Task SaveQuarantineReplayManifestAsync(
        string? tenantId,
        QuarantineReplayManifest manifest,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    Task<bool> TryAcquireQuarantineReplayLeaseAsync(
        string? tenantId,
        string jobName,
        string quarantineTarget,
        string owner,
        TimeSpan ttl,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    Task ReleaseQuarantineReplayLeaseAsync(
        string? tenantId,
        string jobName,
        string quarantineTarget,
        string owner,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
