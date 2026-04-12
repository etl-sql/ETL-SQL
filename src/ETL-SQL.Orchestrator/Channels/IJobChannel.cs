using System.Threading;
using System.Threading.Tasks;

namespace ETL_SQL.Orchestrator.Channels
{
    /// <summary>
    /// Transport-agnostic interface for submitting ad-hoc jobs to an Orchestrator instance.
    /// Implementations: <see cref="InProcessJobChannel"/> (local dev / in-process fallback),
    /// <see cref="HttpJobChannelClient"/> (production, connects to ETL-SQL-OrchestratorService).
    /// </summary>
    public interface IJobChannel
    {
        /// <summary>Submits a script for immediate execution. Returns the assigned job ID.</summary>
        Task<string> SubmitJobAsync(JobSubmitRequest request, CancellationToken ct = default);

        /// <summary>Requests cancellation of a running or queued job.</summary>
        Task CancelJobAsync(string jobId, CancellationToken ct = default);

        /// <summary>Returns the current status of a job.</summary>
        Task<JobStatusResponse> GetStatusAsync(string jobId, CancellationToken ct = default);
    }

    public class JobSubmitRequest
    {
        /// <summary>The ETL-SQL script text to execute.</summary>
        public required string ScriptText  { get; set; }
        /// <summary>Optional session ID for correlation and logging.</summary>
        public string? SessionId           { get; set; }
        /// <summary>Optional human-readable label shown in SHOW JOBS output.</summary>
        public string? Label               { get; set; }
    }

    public class JobStatusResponse
    {
        public required string JobId       { get; set; }
        public required JobRunStatus Status { get; set; }
        public long     RowsProcessed      { get; set; }
        public long     ExecutionTimeMs    { get; set; }
        public string?  ErrorMessage       { get; set; }
    }

    public enum JobRunStatus
    {
        Queued,
        Running,
        Completed,
        Failed,
        Cancelled
    }
}
