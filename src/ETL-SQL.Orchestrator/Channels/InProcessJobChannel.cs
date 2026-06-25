using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Common;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.Orchestrator.Channels
{
    /// <summary>
    /// In-process implementation of <see cref="IJobChannel"/>. Executes jobs directly via
    /// <see cref="IScriptExecutor"/> without any IPC. Used for local/dev mode and as the
    /// fallback when the Orchestrator Service is not running.
    /// </summary>
    public class InProcessJobChannel : IJobChannel
    {
        private readonly IScriptExecutor _executor;
        private readonly ILogger<InProcessJobChannel> _logger;

        private readonly ConcurrentDictionary<string, JobEntry> _jobs = new();

        public InProcessJobChannel(IScriptExecutor executor, ILogger<InProcessJobChannel> logger)
        {
            _executor = executor;
            _logger = logger;
        }

        public Task<string> SubmitJobAsync(JobSubmitRequest request, CancellationToken ct = default)
        {
            var jobId = Guid.NewGuid().ToString("N")[..8];
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var entry = new JobEntry(jobId, cts);
            _jobs[jobId] = entry;

            _logger.LogInformation("InProcess job {JobId} submitted (label={Label})", jobId, request.Label);

            _ = RunJobAsync(entry, request, cts.Token);

            return Task.FromResult(jobId);
        }

        public Task CancelJobAsync(string jobId, CancellationToken ct = default)
        {
            if (_jobs.TryGetValue(jobId, out var entry))
            {
                _logger.LogInformation("Cancelling in-process job {JobId}", jobId);
                entry.Cts.Cancel();
                entry.Status = JobRunStatus.Cancelled;
            }
            return Task.CompletedTask;
        }

        public Task<JobStatusResponse> GetStatusAsync(string jobId, CancellationToken ct = default)
        {
            if (!_jobs.TryGetValue(jobId, out var entry))
                return Task.FromResult(new JobStatusResponse { JobId = jobId, Status = JobRunStatus.Failed, ErrorMessage = "Job not found." });

            return Task.FromResult(new JobStatusResponse
            {
                JobId = jobId,
                Status = entry.Status,
                RowsProcessed = entry.RowsProcessed,
                ExecutionTimeMs = entry.ExecutionTimeMs,
                PeakMemoryBytes = entry.PeakMemoryBytes,
                CpuTimeSeconds = entry.CpuTimeSeconds,
                ErrorMessage = entry.ErrorMessage
            });
        }

        private async Task RunJobAsync(JobEntry entry, JobSubmitRequest request, CancellationToken ct)
        {
            entry.Status = JobRunStatus.Running;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var result = await _executor.ExecuteTextAsync(
                    request.ScriptText,
                    request.SessionId,
                    ct,
                    request.GetLineageJobName(entry.JobId));
                entry.RowsProcessed = result.RowsProcessed;
                entry.PeakMemoryBytes = result.PeakMemoryBytes;
                entry.CpuTimeSeconds = result.CpuTimeSeconds;
                entry.Status = result.Success ? JobRunStatus.Completed : JobRunStatus.Failed;
                entry.ErrorMessage = result.ErrorMessage;
                _logger.LogInformation("InProcess job {JobId} {Status} in {ElapsedMs}ms", entry.JobId, entry.Status, sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                entry.Status = JobRunStatus.Cancelled;
                _logger.LogInformation("InProcess job {JobId} cancelled", entry.JobId);
            }
            catch (Exception ex)
            {
                entry.Status = JobRunStatus.Failed;
                entry.ErrorMessage = SecretRedactor.Redact(ex.Message);
                _logger.LogError("InProcess job {JobId} failed: {Message}. StackTrace: {Stack}",
                    entry.JobId, entry.ErrorMessage, SecretRedactor.Redact(ex.StackTrace));
            }
            finally
            {
                sw.Stop();
                entry.ExecutionTimeMs = sw.ElapsedMilliseconds;
            }
        }

        private sealed class JobEntry(string jobId, CancellationTokenSource cts)
        {
            public string JobId { get; } = jobId;
            public CancellationTokenSource Cts { get; } = cts;
            public JobRunStatus Status { get; set; } = JobRunStatus.Queued;
            public long RowsProcessed { get; set; }
            public long ExecutionTimeMs { get; set; }
            public long PeakMemoryBytes { get; set; }
            public double CpuTimeSeconds { get; set; }
            public string? ErrorMessage { get; set; }
        }
    }
}
