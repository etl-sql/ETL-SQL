using System;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Observability;
using ETL_SQL.Orchestrator.Scheduling;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.Orchestrator.Service
{
    /// <summary>
    /// Generic Host wrapper around <see cref="SchedulerService"/>.
    /// Starts the scheduler on host start, stops it on host shutdown.
    /// Registered as a hosted service in <c>Program.cs</c>.
    /// </summary>
    public class OrchestratorHostedService : IHostedService
    {
        private readonly SchedulerService _scheduler;
        private readonly ILogger<OrchestratorHostedService> _logger;

        public OrchestratorHostedService(SchedulerService scheduler, ILogger<OrchestratorHostedService> logger)
        {
            _scheduler = scheduler;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var activity = BackgroundServiceObservability.StartRun(
                "orchestrator", "orchestrator-host", "start");
            var status = "success";
            _logger.LogInformation("Orchestrator hosted service starting.");
            try
            {
                _scheduler.Start();
            }
            catch
            {
                status = "failure";
                throw;
            }
            finally
            {
                sw.Stop();
                BackgroundServiceObservability.CompleteRun(
                    activity,
                    "orchestrator",
                    "orchestrator-host",
                    "start",
                    status,
                    sw.ElapsedMilliseconds);
            }

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var activity = BackgroundServiceObservability.StartRun(
                "orchestrator", "orchestrator-host", "stop");
            var status = "success";
            _logger.LogInformation("Orchestrator hosted service stopping.");
            try
            {
                await _scheduler.StopAsync(cancellationToken);
            }
            catch
            {
                status = "failure";
                throw;
            }
            finally
            {
                sw.Stop();
                BackgroundServiceObservability.CompleteRun(
                    activity,
                    "orchestrator",
                    "orchestrator-host",
                    "stop",
                    status,
                    sw.ElapsedMilliseconds);
            }
        }
    }
}
