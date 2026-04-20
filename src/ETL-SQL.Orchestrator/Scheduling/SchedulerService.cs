using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Orchestrator.Execution;

namespace ETL_SQL.Orchestrator.Scheduling
{
    /// <summary>
    /// Background service that manages the scheduling and execution of automated ETL-SQL jobs.
    /// Concurrency is limited by <see cref="JobThrottle"/> — jobs beyond the cap are queued
    /// and executed as slots become available.
    /// </summary>
    public class SchedulerService : IJobManager
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IJobHistoryStore _store;
        private readonly ILogger<SchedulerService> _logger;
        private readonly IConfiguration _configuration;
        private readonly JobThrottle _throttle;
        private readonly ETL_SQL.Core.Execution.ISessionStateManager _sessionManager;
        private CancellationTokenSource? _cts;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<long, CancellationTokenSource> _runningJobs = new();

        public SchedulerService(IServiceProvider serviceProvider, IJobHistoryStore store,
            ILogger<SchedulerService> logger, JobThrottle throttle, IConfiguration configuration,
            ETL_SQL.Core.Execution.ISessionStateManager sessionManager)
        {
            _serviceProvider = serviceProvider;
            _store           = store;
            _logger          = logger;
            _throttle        = throttle;
            _configuration   = configuration;
            _sessionManager  = sessionManager;
        }

        /// <summary>Returns a snapshot of current concurrency metrics.</summary>
        public JobThrottleMetrics GetMetrics() => _throttle.GetMetrics();

        private DateTime _lastMetricsLog = DateTime.MinValue;
        private DateTime _lastSessionReap = DateTime.MinValue;
        private Task? _runTask;

        /// <summary>Starts the background scheduler loop.</summary>
        public void Start()
        {
            _cts = new CancellationTokenSource();
            _lastMetricsLog  = DateTime.Now;
            _lastSessionReap = DateTime.Now;
            _runTask = Task.Run(() => RunAsync(_cts.Token));
            _ = _runTask.ContinueWith(t =>
                _logger.LogError(t.Exception, "Scheduler background task terminated unexpectedly."),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        public void Stop()
        {
            _cts?.Cancel();
        }

        private async Task RunAsync(CancellationToken ct)
        {
            _logger.LogInformation("Scheduler service started.");

            try
            {
                await _store.InitializeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize job history store.");
                return;
            }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var activeJobs = await _store.GetActiveJobsAsync();
                    var now = DateTime.Now;

                    foreach (var job in activeJobs)
                    {
                        if (job.NextRun == null || job.NextRun <= now)
                        {
                            await ExecuteJobAsync(job);
                        }
                    }

                    // Resolve intervals from configuration with safe defaults
                    int metricsIntervalSeconds = _configuration.GetValue<int>("Scheduler:MetricsIntervalSeconds", 60);
                    int sleepIntervalSeconds   = _configuration.GetValue<int>("Scheduler:SleepIntervalSeconds", 30);

                    // 8B-1: Periodic metrics emission
                    if (now - _lastMetricsLog >= TimeSpan.FromSeconds(metricsIntervalSeconds))
                    {
                        var metrics = GetMetrics();
                        _logger.LogInformation("Orchestrator Metrics: ActiveJobs={Active}, QueuedJobs={Queued}, MaxConcurrent={Max}, AvailableSlots={Slots}", 
                            metrics.ActiveJobs, metrics.QueuedJobs, metrics.MaxJobs, metrics.AvailableSlots);
                        _lastMetricsLog = now;
                    }

                    // 8B-2: Periodic session reaping
                    int reapIntervalMinutes = _configuration.GetValue<int>("Scheduler:SessionReapIntervalMinutes", 60);
                    if (now - _lastSessionReap >= TimeSpan.FromMinutes(reapIntervalMinutes))
                    {
                        int retentionDays = _configuration.GetValue<int>("Session:StaleSessionRetentionDays", 7);
                        _logger.LogInformation("Orchestrator: Executing periodic session reap (stale_days={Days})", retentionDays);
                        _sessionManager.ReapStaleSessions(TimeSpan.FromDays(retentionDays));
                        _lastSessionReap = now;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(sleepIntervalSeconds), ct);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in scheduler loop.");
                    // Safety sleep on error to avoid tight loops
                    await Task.Delay(5000, ct);
                }
            }
            _logger.LogInformation("Scheduler service stopped.");
        }

        /// <summary>Kills a running job instance by its HistoryId.</summary>
        public bool KillJob(long historyId)
        {
            if (_runningJobs.TryGetValue(historyId, out var cts))
            {
                cts.Cancel();
                _logger.LogInformation("Cancelled job history record {HistoryId}.", historyId);
                return true;
            }
            _logger.LogWarning("Attempted to kill job {HistoryId} but it was not found as a running job.", historyId);
            return false;
        }

        private async Task ExecuteJobAsync(JobDefinition job)
        {
            _logger.LogInformation("Job runner: {JobName} starting execution cycle (MaxRetries={Max}).", job.Name, job.MaxRetries);

            string? sessionId = null;
            int maxAttempts = Math.Max(1, job.MaxRetries + 1);
            ScriptExecutionResult? lastResult = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                long historyId = 0;
                try
                {
                    historyId = await _store.LogJobStartAsync(job.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to log job start for {JobName}.", job.Name);
                }

                try
                {
                    // Acquire a concurrency slot — waits if the cap is reached.
                    using var slot = await _throttle.AcquireAsync(job.Name);

                    using var scope = _serviceProvider.CreateScope();
                    // Inject IScriptExecutor — decoupled from the concrete Evaluator class.
                    var executor = scope.ServiceProvider.GetRequiredService<IScriptExecutor>();

                    using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(_cts?.Token ?? default);
                    if (historyId > 0) _runningJobs[historyId] = jobCts;

                    try
                    {
                        lastResult = await executor.ExecuteTextAsync(job.Script, sessionId, jobCts.Token);
                        
                        // Capture sessionId for potential persistence in retry (CQ-S2)
                        sessionId = lastResult.SessionId;

                        if (lastResult.Success)
                        {
                            _logger.LogInformation("Job {JobName} finished successfully on attempt {Attempt}. (RAM: {Mem} bytes, CPU: {Cpu}s)", 
                                job.Name, attempt, lastResult.PeakMemoryBytes, lastResult.CpuTimeSeconds);
                            
                            if (historyId > 0)
                                await _store.LogJobEndAsync(historyId, "SUCCESS", rowsProcessed: lastResult.RowsProcessed, 
                                    peakMemoryBytes: lastResult.PeakMemoryBytes, cpuTimeSeconds: lastResult.CpuTimeSeconds);
                                    
                            break; // Done
                        }
                        else
                        {
                            _logger.LogWarning("Job {JobName} finished with failure on attempt {Attempt}/{Max}: {Error}", 
                                job.Name, attempt, maxAttempts, lastResult.ErrorMessage);
                            
                            if (historyId > 0)
                                await _store.LogJobEndAsync(historyId, "FAILURE", lastResult.ErrorMessage, 
                                    peakMemoryBytes: lastResult.PeakMemoryBytes, cpuTimeSeconds: lastResult.CpuTimeSeconds);
                        }
                    }
                    finally
                    {
                        if (historyId > 0) _runningJobs.TryRemove(historyId, out _);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing job {JobName} on attempt {Attempt}.", job.Name, attempt);
                    if (historyId > 0)
                    {
                        await _store.LogJobEndAsync(historyId, "FAILURE", ex.Message);
                    }
                    lastResult = new ScriptExecutionResult(false, 0, ex.Message);
                }

                if (attempt < maxAttempts)
                {
                    // Exponential backoff: delay * 2^(attempt-1)
                    int backoffSeconds = (int)Math.Pow(2, attempt - 1) * job.RetryDelaySeconds;
                    backoffSeconds = Math.Min(backoffSeconds, 3600); // Cap at 1 hour

                    _logger.LogInformation("Job {JobName} failed. Retrying in {Delay}s (Backoff). Session: {SessionId}", 
                        job.Name, backoffSeconds, sessionId);
                    
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), _cts?.Token ?? default);
                    }
                    catch (TaskCanceledException) { break; }
                }
            }

            var nextRun = CalculateNextRun(job);
            try
            {
                await _store.UpdateJobLastRunAsync(job.Name, DateTime.Now, nextRun);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update last run info for {JobName}.", job.Name);
            }
        }

        private DateTime CalculateNextRun(JobDefinition job)
        {
            var now = DateTime.Now;
            var interval = job.Interval;
            var unit = job.Unit.ToUpper();

            DateTime next = now;
            switch (unit)
            {
                case "SECOND": next = now.AddSeconds(interval); break;
                case "MINUTE": next = now.AddMinutes(interval); break;
                case "HOUR": next = now.AddHours(interval); break;
                case "DAY": next = now.AddDays(interval); break;
                default: next = now.AddHours(1); break;
            }

            if (!string.IsNullOrEmpty(job.AtTime) && TimeSpan.TryParse(job.AtTime, out var atTime))
            {
                if (unit == "DAY")
                {
                    next = next.Date.Add(atTime);
                    if (next <= now) next = next.AddDays(1);
                }
            }

            return next;
        }
    }
}
