using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Quality;
using ETL_SQL.Core.Multitenancy;

namespace ETL_SQL.Core.Data;

public record LineageHistoryEntry(
    long Id,
    DateTime RunAt,
    string? JobName,
    string? ScriptPath,
    string TargetTable,
    string? TargetColumn,
    IReadOnlyList<string> SourceTables,
    string Operation,
    IReadOnlyDictionary<string, string> Tags,
    string? SourceFile,
    int Line,
    IReadOnlyList<string>? SourceColumns = null,
    string? TransformationKind = null,
    string? TransformationExpression = null,
    IReadOnlyList<string>? FunctionsApplied = null,
    string? DerivedFromDescriptions = null,
    string TenantId = "portal-host"
);

public record LineageMissingMetadataEntry(
    string TargetTable,
    string? TargetColumn,
    IReadOnlyList<string> MissingTags,
    IReadOnlyDictionary<string, string> PresentTags,
    DateTime RunAt,
    string? JobName,
    string? ScriptPath
);

public interface ILineageCatalogStore
{
    Task SaveLineageAsync(IEnumerable<LineageEntry> entries, string? jobName, string? scriptPath, DateTime runAt);
    Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTableAsync(string tableName, int limit = 100);

    /// <summary>
    /// Batch variant of <see cref="GetHistoryForTableAsync"/>: fetches up to
    /// <paramref name="limitPerTable"/> entries for each requested table in a single round-trip.
    /// The default implementation falls back to one query per table; database-backed stores
    /// should override it with a single query to avoid N+1 round-trips on lineage/DAG endpoints.
    /// </summary>
    async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTablesAsync(
        IReadOnlyCollection<string> tableNames, int limitPerTable = 100)
    {
        var all = new List<LineageHistoryEntry>();
        foreach (var name in tableNames)
            all.AddRange(await GetHistoryForTableAsync(name, limitPerTable));
        return all;
    }
    Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTagAsync(string tagKey, string? tagValue = null, int limit = 100);
    Task<IEnumerable<LineageMissingMetadataEntry>> GetMissingMetadataAsync(IReadOnlyCollection<string> requiredTags, int limit = 100);
    Task<IEnumerable<LineageHistoryEntry>> GetRecentLineageAsync(int limit = 1000);
    Task<IEnumerable<LineageHistoryEntry>> GetHistoryForJobAsync(string jobName, int limit = 100);
    Task<IEnumerable<LineageHistoryEntry>> GetHistoryForSourceAsync(string sourceName, int limit = 100);
    Task<IEnumerable<LineageHistoryEntry>> GetHistoryForSourceFileAsync(string sourceFile, int limit = 100);
}

/// <summary>
/// A scheduled job. <see cref="Name"/> is the identity — unique per orchestrator, case-insensitive,
/// and never renamed, because an exported configuration script refers to it. What an operator reads
/// is <see cref="DisplayName"/>, which is free to change without disturbing any script.
/// </summary>
/// <param name="Interval">
/// Legacy interval trigger, superseded by cron <see cref="ScheduleDefinition"/>s attached through
/// <see cref="IJobCatalogStore.AddJobScheduleAsync"/>. Removed once the scheduler reads links.
/// </param>
/// <param name="Unit">See <paramref name="Interval"/>.</param>
/// <param name="AtTime">See <paramref name="Interval"/>.</param>
/// <param name="TargetPath">
/// The report path or <c>.etlsql</c> path this job acts on. For a <see cref="JobTargetKind.Report"/>
/// job this is a label: the Portal's report link is authoritative, so a moved report does not leave
/// two disagreeing sources of truth to reconcile.
/// </param>
/// <param name="CreatedBy">
/// The owning principal's key, as carried in the signed identity assertion (<c>user:…</c> or
/// <c>service:…</c>). This is authorization, not just attribution: an owner may manage what they own,
/// and an object with no recorded owner is reachable only by an administrator until it is adopted.
/// Written when the object is created and changed only by an explicit, audited reassignment — never by
/// an edit.
/// </param>
public record JobDefinition(
    string Name,
    string Script,
    int Interval,
    string Unit,
    string? AtTime,
    DateTime? LastRun,
    DateTime? NextRun,
    bool IsEnabled = true,
    int MaxRetries = 0,
    int RetryDelaySeconds = 30,
    string? ScriptHash = null,
    string HashPolicy = "Warn",
    long Version = 1,
    JobTargetKind JobType = JobTargetKind.Script,
    string? TargetPath = null,
    string? DisplayName = null,
    string? Description = null,
    string? Options = null,
    string? CreatedBy = null,
    string? ModifiedBy = null,
    /// <summary>
    /// Immutable server-derived tenant binding. Null identifies a legacy/unbound job, which is not
    /// eligible for tenant sandbox policy resolution.
    /// </summary>
    string? TenantId = null,
    /// <summary>
    /// Surrogate identity, assigned by the store on first insert and stable for the object's life.
    /// Everything that references a job — grants, schedule and notification links, history, state,
    /// metrics — references this rather than <see cref="Name"/>, so a name may be re-used by another
    /// tenant, and a dropped-then-recreated name never inherits the previous object's grants or
    /// watermarks. <see cref="JobId.None"/> on a definition that has not been persisted yet.
    /// </summary>
    JobId Id = default
);

public record JobHistoryEntry(
    long Id,
    string JobName,
    DateTime StartTime,
    DateTime? EndTime,
    string Status,
    string? ErrorMessage,
    long RowsProcessed = 0,
    long PeakMemoryBytes = 0,
    double CpuTimeSeconds = 0,
    string? ScriptHashAtRunTime = null,
    bool? HashMatched = null,
    /// <summary>Rows removed from output by an <c>@expect</c> QUARANTINE action during this run.</summary>
    long RowsQuarantined = 0,
    /// <summary>Rows that failed a WARN rule but still reached the target during this run.</summary>
    long RowsWarned = 0,
    /// <summary>Compact per-rule failure counts (<c>column:rule=count;…</c>). Counts only — never sample values.</summary>
    string? DataQualityFailures = null,
    /// <summary>Opaque engine session identifier retained only when the run produced resumable state.</summary>
    [property: JsonIgnore] string? SessionId = null,
    /// <summary>Last completed author-declared top-level checkpoint label.</summary>
    string? CheckpointLabel = null,
    /// <summary>
    /// The job this run belongs to. <see cref="JobName"/> is the name it ran under, retained so the
    /// row stays readable after the job is dropped or the name is taken by a different object.
    /// </summary>
    JobId JobId = default,
    /// <summary>Tenant binding copied from the job at run time; null is the unbound (Solo) scope.</summary>
    string? TenantId = null
)
{
    /// <summary>Safe API hint; the opaque session identifier itself is never serialized.</summary>
    public bool HasResumeSession => !string.IsNullOrWhiteSpace(SessionId);
}

/// <summary>
/// Tenant-partitioned lineage contract for a shared control plane. The tenant is a server-derived
/// authority object, never a string accepted from an HTTP request or query parameter.
/// </summary>
public interface ITenantLineageCatalogStore
{
    Task SaveLineageAsync(
        TenantContext tenant,
        IEnumerable<LineageEntry> entries,
        string? jobName,
        string? scriptPath,
        DateTime runAt);

    Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTableAsync(
        TenantContext tenant, string tableName, int limit = 100);
    Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTablesAsync(
        TenantContext tenant, IReadOnlyCollection<string> tableNames, int limitPerTable = 100);
    Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTagAsync(
        TenantContext tenant, string tagKey, string? tagValue = null, int limit = 100);
    Task<IEnumerable<LineageMissingMetadataEntry>> GetMissingMetadataAsync(
        TenantContext tenant, IReadOnlyCollection<string> requiredTags, int limit = 100);
    Task<IEnumerable<LineageHistoryEntry>> GetRecentLineageAsync(
        TenantContext tenant, int limit = 1000);
    Task<IEnumerable<LineageHistoryEntry>> GetHistoryForJobAsync(
        TenantContext tenant, string jobName, int limit = 100);
    Task<IEnumerable<LineageHistoryEntry>> GetHistoryForSourceAsync(
        TenantContext tenant, string sourceName, int limit = 100);
    Task<IEnumerable<LineageHistoryEntry>> GetHistoryForSourceFileAsync(
        TenantContext tenant, string sourceFile, int limit = 100);
}

/// <summary>
/// Tenant-qualified job catalog, run history, data-quality evidence, and durable job state for a
/// shared control plane. Implementations must apply the tenant predicate in the provider query;
/// loading deployment-wide evidence and filtering it in a controller is not an isolation boundary.
/// </summary>
public interface ITenantJobEvidenceStore
{
    Task<IEnumerable<JobDefinition>> GetAllJobsAsync(TenantContext tenant);
    Task<JobDefinition?> GetJobAsync(TenantContext tenant, string name);
    Task<IEnumerable<JobHistoryEntry>> GetHistoryAsync(
        TenantContext tenant, string? jobName = null, int limit = 100);
    Task<JobHistoryEntry?> GetHistoryEntryAsync(TenantContext tenant, long entryId);
    Task<IReadOnlyList<ETL_SQL.Core.Profiling.StatementMetricsPayload>> GetJobStatementMetricsAsync(
        TenantContext tenant, long entryId);
    Task<IReadOnlyList<JobDataQualityFailure>> GetDataQualityFailuresForJobAsync(
        TenantContext tenant, string jobName, int limit = 1000);
    Task<IReadOnlyList<JobDataQualityFailure>> GetDataQualityFailuresForRunAsync(
        TenantContext tenant, long entryId, int limit = 1000);
    Task<string?> GetJobStateAsync(TenantContext tenant, string jobName, string key);
    Task SetJobStateAsync(TenantContext tenant, string jobName, string key, string? value);
    Task<IReadOnlyList<JobStateEntry>> GetJobStatesAsync(
        TenantContext tenant, string? jobName = null, int limit = 1000);
}

/// <summary>One normalized, counts-only data-quality failure joined to its run identity.</summary>
/// <summary>
/// A persisted statement measurement joined to the run it belongs to, for the
/// <c>eng.job_statement_metrics</c> read model.
/// </summary>
public sealed record JobStatementMetric(
    long RunId,
    string JobName,
    DateTime StartTime,
    DateTime? EndTime,
    string Status,
    int Ordinal,
    ETL_SQL.Core.Profiling.StatementMetricsPayload Statement);

public sealed record JobDataQualityFailure(
    long RunId,
    string JobName,
    DateTime StartTime,
    DateTime? EndTime,
    string Status,
    string? TargetTable,
    string ColumnName,
    string Rule,
    string Action,
    long FailureCount,
    string? Owner = null);

/// <summary>
/// Counts-only tenant usage attributed at the trusted scheduler boundary. This is billing/operations
/// evidence, never admission authority, and contains no script, parameter, connector target, or row
/// content.
/// </summary>
public sealed record TenantUsageRecord(
    long Id,
    string TenantId,
    long JobHistoryId,
    string WorkloadKind,
    string Status,
    long RowsProcessed,
    long PeakMemoryBytes,
    double CpuTimeSeconds,
    long DurationMs,
    DateTime RecordedAtUtc);

/// <summary>Canonical quality-status projection for one current or persisted run.</summary>
public sealed record JobDataQualityStatus(
    string RunId,
    string? JobName,
    DateTime StartTime,
    DateTime? EndTime,
    string Status,
    long RowsProcessed,
    long RowsWarned,
    long RowsQuarantined,
    int FailedRuleCount,
    DateTimeOffset? FreshestValueUtc,
    string FreshnessState,
    string? ErrorSummary);

/// <summary>Daily-aggregated job execution for one job, retained far longer than raw history.</summary>
public sealed record JobHistoryDailySummary(
    string Day,
    string JobName,
    int RunCount,
    int FailureCount,
    long TotalRows,
    long MaxPeakMemoryBytes);

public interface IJobHistoryStore
{
    Task InitializeAsync();

    // Job Management
    //
    // A job name identifies a job only *within a tenant*, so every name-addressed lookup takes the
    // tenant it is addressed in. Pass null for the unbound (Solo, no signed tenant) scope; that is a
    // real scope of its own and never a wildcard. Everything downstream of a lookup — leases, state,
    // history, metrics — addresses the job by its surrogate Id instead, so there is exactly one point
    // where a name is interpreted.
    Task SaveJobAsync(JobDefinition job);
    Task<bool> TrySaveJobAsync(JobDefinition job, long expectedVersion);
    Task<JobDefinition?> GetJobAsync(string? tenantId, string name);
    Task<JobDefinition?> GetJobByIdAsync(JobId jobId);
    Task<IEnumerable<JobDefinition>> GetActiveJobsAsync();
    Task<IEnumerable<JobDefinition>> GetAllJobsAsync();
    /// <summary>Returns a stable name-ordered page of saved jobs for management APIs.</summary>
    Task<IEnumerable<JobDefinition>> GetJobsPageAsync(int limit = 100, int offset = 0);
    Task DeleteJobAsync(JobId jobId);
    Task<bool> TryDeleteJobAsync(JobId jobId, long expectedVersion);
    Task UpdateJobLastRunAsync(JobId jobId, DateTime lastRun, DateTime? nextRun);

    // Execution lease (P1.1). A scheduler instance must claim a job before running it so that
    // concurrent scheduler processes sharing one store produce exactly one execution per due
    // occurrence. A lease that is not renewed before it expires may be reclaimed by another
    // owner (crash recovery — the occurrence reruns, i.e. at-least-once semantics).
    Task<bool> TryAcquireJobLeaseAsync(JobId jobId, string owner, TimeSpan duration);
    Task<bool> TryRenewJobLeaseAsync(JobId jobId, string owner, TimeSpan duration);
    Task ReleaseJobLeaseAsync(JobId jobId, string owner);

    // Fencing tokens (P1.8). Each successful lease acquisition stamps the job with a strictly
    // increasing fence token. A node that was paused/partitioned, lost its lease, and later resumes
    // still holds an old token; the durable completion write (TryUpdateJobLastRunFenced) carries the
    // token and the store rejects it because a newer owner has already advanced the token — so a
    // stale writer can never clobber a newer one's scheduling state (Gap #5).
    Task<long?> AcquireJobLeaseAsync(JobId jobId, string owner, TimeSpan duration);
    Task<bool> ValidateFenceTokenAsync(JobId jobId, long fenceToken);
    Task<bool> TryUpdateJobLastRunFencedAsync(JobId jobId, DateTime lastRun, DateTime? nextRun, long fenceToken);

    // History Management
    Task<long> LogJobStartAsync(JobId jobId);

    /// <summary>
    /// Records the start of a run that is <b>not</b> a job — an ad-hoc <c>run</c> of a script, audited
    /// because <c>Engine:AuditAdHocRuns</c> is on.
    ///
    /// <para>Separate from <see cref="LogJobStartAsync"/> on purpose. Such a run has no job row and so
    /// no identity, and the alternative — letting a caller pass a script filename as though it were an
    /// identity — is precisely the confusion the typed <see cref="Data.JobId"/> exists to prevent. The
    /// row is written with no job binding and carries <paramref name="label"/> for display.</para>
    /// </summary>
    Task<long> LogAdHocRunStartAsync(string label, string? tenantId = null);
    Task LogJobEndAsync(long entryId, string status, string? errorMessage = null, long rowsProcessed = 0, long peakMemoryBytes = 0, double cpuTimeSeconds = 0, string? scriptHashAtRunTime = null, bool? hashMatched = null, long rowsQuarantined = 0, long rowsWarned = 0, string? dataQualityFailures = null);
    /// <summary>Attaches opaque named-checkpoint resume metadata after an execution attempt.</summary>
    Task UpdateJobResumeMetadataAsync(long entryId, string? sessionId, string? checkpointLabel) => Task.CompletedTask;
    /// <summary>
    /// Imports one completed historical run while preserving its original timestamps. Implementations
    /// must be idempotent for the run's job/start/end tuple and return the target run id.
    /// </summary>
    Task<long> ImportJobHistoryAsync(JobHistoryEntry entry);
    Task SaveJobColumnMetricsAsync(long entryId, IEnumerable<DataQualityColumnMetric> metrics) => Task.CompletedTask;

    /// <summary>
    /// Persists the run's per-statement measurements — the flight recorder.
    ///
    /// <para>Statement text arriving here is already normalized by
    /// <c>StatementMetricsPayload.From</c>: this store is shared, and a run's history is read by
    /// operators who are a different principal from whoever ran the script, so raw statement text
    /// with its literal values must never reach it.</para>
    ///
    /// <para>Default no-op so a store that does not implement the flight recorder still satisfies
    /// the interface, matching the column-metrics precedent above.</para>
    /// </summary>
    Task SaveJobStatementMetricsAsync(
        long entryId, IEnumerable<ETL_SQL.Core.Profiling.StatementMetricsPayload> statements) => Task.CompletedTask;

    /// <summary>Persists one idempotent counts-only usage row for a tenant-bound run attempt.</summary>
    Task SaveTenantUsageAsync(TenantUsageRecord usage) => Task.CompletedTask;

    /// <summary>Returns usage for exactly one server-owned tenant partition.</summary>
    Task<IReadOnlyList<TenantUsageRecord>> GetTenantUsageAsync(
        string tenantId, DateTime? fromUtc = null, int limit = 1000) =>
        Task.FromResult<IReadOnlyList<TenantUsageRecord>>([]);

    /// <summary>
    /// Drops statement detail earlier than the run record it belongs to.
    ///
    /// <para>Statement detail is the bulk of a run's rows, and a successful run stops being
    /// interesting long before its history entry does — an operator asks "what did last night's
    /// failure do" far longer than "which statement was slowest three weeks ago on a run that
    /// worked". Failed runs are retained longer than successes for that reason, and both windows
    /// are deployment settings rather than fixed values.</para>
    /// </summary>
    Task<int> PruneStatementMetricsAsync(TimeSpan successMaxAge, TimeSpan failedMaxAge) => Task.FromResult(0);

    /// <summary>
    /// Recent statement measurements across runs, newest run first, for the <c>eng.*</c> read model.
    ///
    /// <para>Solo has no Portal, so the durable timeline has to be reachable as an engine catalog
    /// table or the smallest profile silently loses a capability that Team gains — the same reason
    /// <c>eng.job_history</c> and <c>eng.data_quality_failures</c> exist.</para>
    /// </summary>
    Task<IReadOnlyList<JobStatementMetric>> GetStatementMetricsAsync(int limit = 1000) =>
        Task.FromResult<IReadOnlyList<JobStatementMetric>>([]);

    /// <summary>Reads back a run's statement timeline, in execution order.</summary>
    Task<IReadOnlyList<ETL_SQL.Core.Profiling.StatementMetricsPayload>> GetJobStatementMetricsAsync(long entryId) =>
        Task.FromResult<IReadOnlyList<ETL_SQL.Core.Profiling.StatementMetricsPayload>>([]);
    Task SaveJobDataQualityFailuresAsync(long entryId, IEnumerable<DataQualityRuleFailureMetric> failures) => Task.CompletedTask;
    Task<IReadOnlyList<JobDataQualityFailure>> GetDataQualityFailuresAsync(int limit = 1000) =>
        Task.FromResult<IReadOnlyList<JobDataQualityFailure>>(Array.Empty<JobDataQualityFailure>());
    async Task<IReadOnlyList<JobDataQualityFailure>> GetDataQualityFailuresForJobAsync(string jobName, int limit = 1000) =>
        (await GetDataQualityFailuresAsync(Math.Max(limit, 1000)))
            .Where(row => row.JobName.Equals(jobName, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .ToList();
    /// <summary>Reads normalized, counts-only data-quality failures for one run.</summary>
    async Task<IReadOnlyList<JobDataQualityFailure>> GetDataQualityFailuresForRunAsync(long entryId, int limit = 1000) =>
        (await GetDataQualityFailuresAsync(Math.Max(limit, 1000)))
            .Where(row => row.RunId == entryId)
            .Take(limit)
            .ToList();
    Task<IReadOnlyList<JobDataQualityStatus>> GetDataQualityStatusesAsync(int limit = 1000) =>
        Task.FromResult<IReadOnlyList<JobDataQualityStatus>>(Array.Empty<JobDataQualityStatus>());
    Task<IReadOnlyList<ColumnRunMetrics>> GetRecentColumnMetricsAsync(JobId jobId, string? targetTable, string columnName, int limit = 100) =>
        Task.FromResult<IReadOnlyList<ColumnRunMetrics>>(Array.Empty<ColumnRunMetrics>());
    Task<IEnumerable<JobHistoryEntry>> GetHistoryAsync(JobId jobId = default, int limit = 100);

    /// <summary>
    /// Runs recorded under one job <em>name</em> within a tenant, newest first.
    ///
    /// <para>Distinct from <see cref="GetHistoryAsync(JobId, int)"/>, and not a convenience wrapper
    /// around it: history outlives the job it belongs to, so after a <c>DROP JOB</c> there is no
    /// identity left to resolve the name to, and an id-addressed read would report that a job which
    /// ran for a year had never run at all. The tenant predicate is what keeps a shared name from
    /// reaching another tenant's runs.</para>
    /// </summary>
    Task<IEnumerable<JobHistoryEntry>> GetHistoryForNameAsync(string? tenantId, string jobName, int limit = 100);
    /// <summary>Reads one durable run by identity, or null when it has expired or never existed.</summary>
    async Task<JobHistoryEntry?> GetHistoryEntryAsync(long entryId) =>
        (await GetHistoryAsync(JobId.None, 1000)).FirstOrDefault(row => row.Id == entryId);
    /// <summary>Returns a completion-time page for bounded incremental pollers.</summary>
    Task<IEnumerable<JobHistoryEntry>> GetCompletedHistoryAsync(
        DateTime completedAfter, DateTime completedThrough, int limit = 1000, int offset = 0);

    /// <summary>
    /// Deletes completed job-history rows older than <paramref name="maxAge"/> (in-flight RUNNING rows
    /// are never pruned), bounding unbounded table growth. Returns the number of rows removed.
    /// </summary>
    Task<int> PruneHistoryAsync(TimeSpan maxAge);

    /// <summary>
    /// Marks orphaned RUNNING rows — jobs whose <c>StartTime</c> is older than
    /// <paramref name="maxRuntime"/> with no completion recorded — as INTERRUPTED, so a crash that
    /// prevented the completion write does not leave a row RUNNING forever (unprunable and invisible to
    /// failure reporting). Self-healing: if such a job is in fact still running, its eventual
    /// completion write overwrites INTERRUPTED with the real terminal status. Returns rows updated.
    /// </summary>
    Task<int> ReconcileStaleRunningAsync(TimeSpan maxRuntime);

    /// <summary>
    /// Recomputes the daily job-history roll-up for every day still present in the raw table
    /// (idempotent) so trend survives raw-history pruning. Run before <see cref="PruneHistoryAsync"/>.
    /// Returns the number of (day, job) summary rows written.
    /// </summary>
    Task<int> RollUpJobHistoryAsync();

    /// <summary>Returns daily job summaries on/after <paramref name="sinceDay"/>, newest first.</summary>
    Task<IReadOnlyList<JobHistoryDailySummary>> GetJobHistoryDailyAsync(JobId jobId, DateTime sinceDay, int limit = 1000);

    /// <summary>Deletes daily job summaries older than <paramref name="maxAge"/>; returns rows removed.</summary>
    Task<int> PruneJobHistoryDailyAsync(TimeSpan maxAge);

    // State Management
    Task<string?> GetJobStateAsync(JobId jobId, string key);
    Task SetJobStateAsync(JobId jobId, string key, string? value);

    /// <summary>
    /// Reads a host-scoped operational marker — the outcome of the last backup or restore drill, and
    /// similar deployment-wide evidence the Portal reports on.
    ///
    /// <para>These share the job-state table but are not job state: there is no job called
    /// <c>admin-backup</c>, and there never was. They previously reached the table by passing that
    /// label where a job's name went, which worked only for as long as job state was name-addressed.
    /// Giving them their own named surface says what they are, and keeps <see cref="Data.JobId"/>
    /// meaning one thing.</para>
    /// </summary>
    /// <param name="scope">
    /// Host-level area the marker belongs to (<c>backup</c>, <c>restore</c>). Stored under a reserved
    /// namespace that cannot collide with any job identity.
    /// </param>
    Task<string?> GetHostStateAsync(string scope, string key);

    /// <summary>Writes a host-scoped operational marker. See <see cref="GetHostStateAsync"/>.</summary>
    Task SetHostStateAsync(string scope, string key, string? value);

    /// <summary>
    /// Enumerates saved job-state entries (watermarks, markers), optionally for a single job —
    /// the read surface behind <c>SHOW JOB STATE</c>. Unlike <see cref="GetJobStateAsync"/>, which is
    /// scoped to one known key, this lets an administrator inspect any job's state without knowing
    /// its keys in advance. Ordered by job then key; capped by <paramref name="limit"/>.
    /// </summary>
    Task<IReadOnlyList<JobStateEntry>> GetJobStatesAsync(JobId jobId = default, int limit = 1000);
}

/// <summary>One saved job-state key/value pair (see SET_JOB_STATE / GET_JOB_STATE).</summary>
public sealed record JobStateEntry(string JobName, string StateKey, string? StateValue, DateTime UpdatedAt);

public interface IJobScheduleQueryStore
{
    Task<IEnumerable<JobDefinition>> GetDueJobsAsync(DateTime now);
}
