using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using ETL_SQL.Core;
using ETL_SQL.Orchestrator.Channels;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Scheduling;

namespace ETL_SQL.Orchestrator.Service
{
    /// <summary>
    /// Minimal-API endpoints exposed by the Orchestrator Service over HTTP.
    /// Clients use <see cref="HttpJobChannelClient"/> to call these.
    ///
    /// Routes:
    ///   POST   /jobs          — submit a script for ad-hoc execution
    ///   DELETE /jobs/{id}     — cancel a running or queued job
    ///   GET    /jobs/{id}     — get the status of a job
    ///   GET    /health        — liveness probe (always 200 OK)
    /// </summary>
    public static class JobApiEndpoints
    {
        // In-memory job registry — in Phase 7 this moves to a persistent store.
        private static readonly ConcurrentDictionary<string, JobEntry> _jobs = new();

        public static void MapJobApi(this IEndpointRouteBuilder app)
        {
            app.MapGet("/health", () => Results.Ok(new { Status = "Healthy" }))
               .WithName("health");

            app.MapPost("/jobs", async (JobSubmitRequest request, IScriptExecutor executor, ILogger<Program> logger, CancellationToken ct) =>
            {
                var jobId = Guid.NewGuid().ToString("N")[..8];
                var cts   = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var entry = new JobEntry(jobId, cts);
                _jobs[jobId] = entry;

                logger.LogInformation("Job {JobId} submitted (label={Label})", jobId, request.Label);
                _ = RunJobAsync(entry, request, executor, logger, cts.Token);

                return Results.Accepted($"/jobs/{jobId}", new { JobId = jobId });
            })
            .WithName("submitJob");

            app.MapDelete("/jobs/{id}", (string id, ILogger<Program> logger) =>
            {
                if (!_jobs.TryGetValue(id, out var entry))
                    return Results.NotFound(new { Error = $"Job '{id}' not found." });

                logger.LogInformation("Cancelling job {JobId}", id);
                entry.Cts.Cancel();
                entry.Status = JobRunStatus.Cancelled;
                return Results.Ok(new { JobId = id, Status = "Cancelled" });
            })
            .WithName("cancelJob");

            // GET /metrics — Prometheus-style concurrency metrics
            app.MapGet("/metrics", (SchedulerService scheduler, ChildProcessTracker tracker) =>
            {
                var m = scheduler.GetMetrics();
                return Results.Ok(new
                {
                    active_jobs     = m.ActiveJobs,
                    queued_jobs     = m.QueuedJobs,
                    max_jobs        = m.MaxJobs,
                    available_slots = m.AvailableSlots,
                    active_processes = tracker.ActiveCount
                });
            })
            .WithName("getMetrics");

            app.MapGet("/jobs/{id}", (string id) =>
            {
                if (!_jobs.TryGetValue(id, out var entry))
                    return Results.NotFound(new { Error = $"Job '{id}' not found." });

                return Results.Ok(new JobStatusResponse
                {
                    JobId           = entry.JobId,
                    Status          = entry.Status,
                    RowsProcessed   = entry.RowsProcessed,
                    ExecutionTimeMs = entry.ExecutionTimeMs,
                    ErrorMessage    = entry.ErrorMessage
                });
            })
            .WithName("getJobStatus");
        }

        private static async Task RunJobAsync(JobEntry entry, JobSubmitRequest request,
            IScriptExecutor executor, ILogger logger, CancellationToken ct)
        {
            entry.Status = JobRunStatus.Running;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var result = await executor.ExecuteTextAsync(request.ScriptText, cancellationToken: ct);
                entry.RowsProcessed = result.RowsProcessed;
                entry.Status        = result.Success ? JobRunStatus.Completed : JobRunStatus.Failed;
                entry.ErrorMessage  = result.ErrorMessage;
                logger.LogInformation("Job {JobId} {Status} in {ElapsedMs}ms, rows={Rows}",
                    entry.JobId, entry.Status, sw.ElapsedMilliseconds, result.RowsProcessed);
            }
            catch (OperationCanceledException)
            {
                entry.Status = JobRunStatus.Cancelled;
                logger.LogInformation("Job {JobId} was cancelled", entry.JobId);
            }
            catch (Exception ex)
            {
                entry.Status       = JobRunStatus.Failed;
                entry.ErrorMessage = ex.Message;
                logger.LogError(ex, "Job {JobId} failed unexpectedly", entry.JobId);
            }
            finally
            {
                sw.Stop();
                entry.ExecutionTimeMs = sw.ElapsedMilliseconds;
            }
        }

        private sealed class JobEntry(string jobId, CancellationTokenSource cts)
        {
            public string                  JobId           { get; } = jobId;
            public CancellationTokenSource Cts             { get; } = cts;
            public JobRunStatus            Status          { get; set; } = JobRunStatus.Queued;
            public long                    RowsProcessed   { get; set; }
            public long                    ExecutionTimeMs { get; set; }
            public string?                 ErrorMessage    { get; set; }
        }
    }
}
