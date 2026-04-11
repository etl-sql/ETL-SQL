using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Orchestrator.Scheduling
{
    /// <summary>
    /// Background service that manages the scheduling and execution of automated ETL-SQL jobs.
    /// </summary>
    public class SchedulerService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IJobHistoryStore _store;
        private readonly ILogger<SchedulerService> _logger;
        private CancellationTokenSource? _cts;

        public SchedulerService(IServiceProvider serviceProvider, IJobHistoryStore store, ILogger<SchedulerService> logger)
        {
            _serviceProvider = serviceProvider;
            _store = store;
            _logger = logger;
        }

        private Task? _runTask;

        /// <summary>Starts the background scheduler loop.</summary>
        public void Start()
        {
            _cts = new CancellationTokenSource();
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
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in scheduler loop.");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
            _logger.LogInformation("Scheduler service stopped.");
        }

        private async Task ExecuteJobAsync(JobDefinition job)
        {
            _logger.LogInformation("Executing job: {JobName}", job.Name);

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
                using var scope = _serviceProvider.CreateScope();
                // Inject IScriptExecutor — decoupled from the concrete Evaluator class.
                var executor = scope.ServiceProvider.GetRequiredService<IScriptExecutor>();

                var result = await executor.ExecuteTextAsync(job.Script);

                if (result.Success)
                {
                    _logger.LogInformation("Job {JobName} finished successfully.", job.Name);
                    if (historyId > 0)
                        await _store.LogJobEndAsync(historyId, "SUCCESS", rowsProcessed: result.RowsProcessed);
                }
                else
                {
                    _logger.LogWarning("Job {JobName} finished with failure: {Error}", job.Name, result.ErrorMessage);
                    if (historyId > 0)
                        await _store.LogJobEndAsync(historyId, "FAILURE", result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing job {JobName}.", job.Name);
                if (historyId > 0)
                {
                    await _store.LogJobEndAsync(historyId, "FAILURE", ex.Message);
                }
            }
            finally
            {
                var nextRun = CalculateNextRun(job);
                try
                {
                    await _store.UpdateJobLastRunAsync(job.Name, DateTime.Now, nextRun);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update last run for {JobName}.", ex.Message);
                }
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
