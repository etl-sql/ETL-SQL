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
        int Line
    );

    public interface ILineageCatalogStore
    {
        Task SaveLineageAsync(IEnumerable<LineageEntry> entries, string? jobName, string? scriptPath, DateTime runAt);
        Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTableAsync(string tableName, int limit = 100);
        Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTagAsync(string tagKey, string? tagValue = null, int limit = 100);
        Task<IEnumerable<LineageHistoryEntry>> GetHistoryForJobAsync(string jobName, int limit = 100);
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

        // History Management
        Task<long> LogJobStartAsync(string jobName);
        Task LogJobEndAsync(long entryId, string status, string? errorMessage = null, long rowsProcessed = 0, long peakMemoryBytes = 0, double cpuTimeSeconds = 0, string? scriptHashAtRunTime = null, bool? hashMatched = null);
        Task<IEnumerable<JobHistoryEntry>> GetHistoryAsync(string? jobName = null, int limit = 100);
    }
}
