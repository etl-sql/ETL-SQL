using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Data
{
    public record JobDefinition(
        string Name,
        string Script,
        int Interval,
        string Unit,
        string? AtTime,
        DateTime? LastRun,
        DateTime? NextRun,
        bool IsEnabled = true
    );

    public record JobHistoryEntry(
        long Id,
        string JobName,
        DateTime StartTime,
        DateTime? EndTime,
        string Status,
        string? ErrorMessage,
        long RowsProcessed = 0
    );

    public interface IJobHistoryStore
    {
        Task InitializeAsync();
        
        // Job Management
        Task SaveJobAsync(JobDefinition job);
        Task<IEnumerable<JobDefinition>> GetActiveJobsAsync();
        Task DeleteJobAsync(string name);
        Task UpdateJobLastRunAsync(string name, DateTime lastRun, DateTime? nextRun);

        // History Management
        Task<long> LogJobStartAsync(string jobName);
        Task LogJobEndAsync(long entryId, string status, string? errorMessage = null, long rowsProcessed = 0);
        Task<IEnumerable<JobHistoryEntry>> GetHistoryAsync(string? jobName = null, int limit = 100);
    }
}
