using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Quality;

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
    string? DerivedFromDescriptions = null
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
/// Attribution passed through by the Portal, not authorization — the Orchestrator's API
/// authenticates with a single shared key and has no identity model. See
/// <c>ROADMAP.md → Orchestrator — Per-Object Authorization</c>.
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
    string? ModifiedBy = null
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
    string? DataQualityFailures = null
);

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
    Task SaveJobAsync(JobDefinition job);
    Task<bool> TrySaveJobAsync(JobDefinition job, long expectedVersion);
    Task<JobDefinition?> GetJobAsync(string name);
    Task<IEnumerable<JobDefinition>> GetActiveJobsAsync();
    Task<IEnumerable<JobDefinition>> GetAllJobsAsync();
    /// <summary>Returns a stable name-ordered page of saved jobs for management APIs.</summary>
    Task<IEnumerable<JobDefinition>> GetJobsPageAsync(int limit = 100, int offset = 0);
    Task DeleteJobAsync(string name);
    Task<bool> TryDeleteJobAsync(string name, long expectedVersion);
    Task UpdateJobLastRunAsync(string name, DateTime lastRun, DateTime? nextRun);

    // Execution lease (P1.1). A scheduler instance must claim a job before running it so that
    // concurrent scheduler processes sharing one store produce exactly one execution per due
    // occurrence. A lease that is not renewed before it expires may be reclaimed by another
    // owner (crash recovery — the occurrence reruns, i.e. at-least-once semantics).
    Task<bool> TryAcquireJobLeaseAsync(string jobName, string owner, TimeSpan duration);
    Task<bool> TryRenewJobLeaseAsync(string jobName, string owner, TimeSpan duration);
    Task ReleaseJobLeaseAsync(string jobName, string owner);

    // Fencing tokens (P1.8). Each successful lease acquisition stamps the job with a strictly
    // increasing fence token. A node that was paused/partitioned, lost its lease, and later resumes
    // still holds an old token; the durable completion write (TryUpdateJobLastRunFenced) carries the
    // token and the store rejects it because a newer owner has already advanced the token — so a
    // stale writer can never clobber a newer one's scheduling state (Gap #5).
    Task<long?> AcquireJobLeaseAsync(string jobName, string owner, TimeSpan duration);
    Task<bool> ValidateFenceTokenAsync(string jobName, long fenceToken);
    Task<bool> TryUpdateJobLastRunFencedAsync(string name, DateTime lastRun, DateTime? nextRun, long fenceToken);

    // History Management
    Task<long> LogJobStartAsync(string jobName);
    Task LogJobEndAsync(long entryId, string status, string? errorMessage = null, long rowsProcessed = 0, long peakMemoryBytes = 0, double cpuTimeSeconds = 0, string? scriptHashAtRunTime = null, bool? hashMatched = null, long rowsQuarantined = 0, long rowsWarned = 0, string? dataQualityFailures = null);
    Task SaveJobColumnMetricsAsync(long entryId, IEnumerable<DataQualityColumnMetric> metrics) => Task.CompletedTask;
    Task<IReadOnlyList<ColumnRunMetrics>> GetRecentColumnMetricsAsync(string jobName, string? targetTable, string columnName, int limit = 100) =>
        Task.FromResult<IReadOnlyList<ColumnRunMetrics>>(Array.Empty<ColumnRunMetrics>());
    Task<IEnumerable<JobHistoryEntry>> GetHistoryAsync(string? jobName = null, int limit = 100);
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
    Task<IReadOnlyList<JobHistoryDailySummary>> GetJobHistoryDailyAsync(string? jobName, DateTime sinceDay, int limit = 1000);

    /// <summary>Deletes daily job summaries older than <paramref name="maxAge"/>; returns rows removed.</summary>
    Task<int> PruneJobHistoryDailyAsync(TimeSpan maxAge);

    // State Management
    Task<string?> GetJobStateAsync(string jobName, string key);
    Task SetJobStateAsync(string jobName, string key, string? value);

    /// <summary>
    /// Enumerates saved job-state entries (watermarks, markers), optionally for a single job —
    /// the read surface behind <c>SHOW JOB STATE</c>. Unlike <see cref="GetJobStateAsync"/>, which is
    /// scoped to one known key, this lets an administrator inspect any job's state without knowing
    /// its keys in advance. Ordered by job then key; capped by <paramref name="limit"/>.
    /// </summary>
    Task<IReadOnlyList<JobStateEntry>> GetJobStatesAsync(string? jobName = null, int limit = 1000);
}

/// <summary>One saved job-state key/value pair (see SET_JOB_STATE / GET_JOB_STATE).</summary>
public sealed record JobStateEntry(string JobName, string StateKey, string? StateValue, DateTime UpdatedAt);

public interface IJobScheduleQueryStore
{
    Task<IEnumerable<JobDefinition>> GetDueJobsAsync(DateTime now);
}
