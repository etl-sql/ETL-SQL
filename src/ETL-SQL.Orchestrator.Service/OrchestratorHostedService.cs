using System;
using System.Threading;
using System.Threading.Tasks;
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
            _logger.LogInformation("Orchestrator hosted service starting.");
            _scheduler.Start();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Orchestrator hosted service stopping.");
            _scheduler.Stop();
            return Task.CompletedTask;
        }
    }
}
