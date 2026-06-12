using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core;

namespace ETL_SQL.Core.Data
{
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
        Task<IEnumerable<LineageHistoryEntry>> GetHistoryForJobAsync(string jobName, int limit = 100);
        Task<IEnumerable<LineageHistoryEntry>> GetHistoryForSourceAsync(string sourceName, int limit = 100);
        Task<IEnumerable<LineageHistoryEntry>> GetHistoryForSourceFileAsync(string sourceFile, int limit = 100);
    }

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
        string HashPolicy = "Warn"
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
        bool? HashMatched = null
    );

    public interface IJobHistoryStore
    {
        Task InitializeAsync();

        // Job Management
        Task SaveJobAsync(JobDefinition job);
        Task<JobDefinition?> GetJobAsync(string name);
        Task<IEnumerable<JobDefinition>> GetActiveJobsAsync();
        Task<IEnumerable<JobDefinition>> GetAllJobsAsync();
        Task DeleteJobAsync(string name);
        Task UpdateJobLastRunAsync(string name, DateTime lastRun, DateTime? nextRun);

        // Execution lease (P1.1). A scheduler instance must claim a job before running it so that
        // concurrent scheduler processes sharing one store produce exactly one execution per due
        // occurrence. A lease that is not renewed before it expires may be reclaimed by another
        // owner (crash recovery — the occurrence reruns, i.e. at-least-once semantics).
        Task<bool> TryAcquireJobLeaseAsync(string jobName, string owner, TimeSpan duration);
        Task<bool> TryRenewJobLeaseAsync(string jobName, string owner, TimeSpan duration);
        Task ReleaseJobLeaseAsync(string jobName, string owner);

        // History Management
        Task<long> LogJobStartAsync(string jobName);
        Task LogJobEndAsync(long entryId, string status, string? errorMessage = null, long rowsProcessed = 0, long peakMemoryBytes = 0, double cpuTimeSeconds = 0, string? scriptHashAtRunTime = null, bool? hashMatched = null);
        Task<IEnumerable<JobHistoryEntry>> GetHistoryAsync(string? jobName = null, int limit = 100);
    }
}
