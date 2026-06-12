using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Engine;

namespace ETL_SQL.Orchestrator.Execution
{
    /// <summary>
    /// <see cref="IScriptExecutor"/> implementation — thin adapter used by
    /// <see cref="ETL_SQL.Orchestrator.Scheduling.SchedulerService"/> for job execution.
    /// Wraps <see cref="ExecutionSession"/> and maps its rich result to the lightweight
    /// <see cref="ScriptExecutionResult"/> record expected by the scheduler.
    /// </summary>
    public class ScriptExecutorAdapter : IScriptExecutor
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly CliContext _ctx;
        private readonly ILogger _logger;
        private readonly ILineageCatalogStore _catalog;
        public Evaluator? LastEvaluator { get; private set; }

        public ScriptExecutorAdapter(IServiceProvider serviceProvider, CliContext ctx, ILogger logger, ILineageCatalogStore catalog)
        {
            _serviceProvider = serviceProvider;
            _ctx = ctx;
            _logger = logger;
            _catalog = catalog;
        }

        public async Task<ScriptExecutionResult> ExecuteTextAsync(string scriptText, string? sessionId = null, CancellationToken cancellationToken = default, string? jobName = null)
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var startCpu = process.TotalProcessorTime.TotalSeconds;
            var runAt = DateTime.UtcNow;

            try
            {
                // Override the context's session ID if provided by the orchestrator (CQ-S2)
                if (!string.IsNullOrEmpty(sessionId))
                    _ctx.SessionId = sessionId;

                var session = new ExecutionSession(_serviceProvider, _ctx, _logger);
                var result = await session.ExecuteAsync(scriptText, cancellationToken);
                LastEvaluator = session.LastEvaluator;

                // Persist lineage to the cross-run catalog (fire-and-forget errors so they never fail the job)
                if (LastEvaluator != null)
                {
                    try
                    {
                        var lineage = LastEvaluator.LineageTracker.GetFullLineage().ToList();
                        if (lineage.Count > 0 && jobName != null)
                            await _catalog.SaveLineageAsync(lineage, jobName, null, runAt);
                    }
                    catch (Exception ex)
                    {
                        _logger.WriteLine($"[lineage catalog] Failed to persist lineage: {ex.Message}", ConsoleColor.DarkYellow);
                    }
                }

                process.Refresh();
                var endCpu = process.TotalProcessorTime.TotalSeconds;

                return new ScriptExecutionResult(result.Success, result.RowsProcessed,
                    result.Success ? null : string.Join("; ", result.Diagnostics.Select(d => d.Message)),
                    process.PeakWorkingSet64, endCpu - startCpu, _ctx.SessionId);
            }
            catch (Exception ex)
            {
                process.Refresh();
                var endCpu = process.TotalProcessorTime.TotalSeconds;
                return new ScriptExecutionResult(false, 0, ex.Message, process.PeakWorkingSet64, endCpu - startCpu, _ctx.SessionId);
            }
        }
    }
}
