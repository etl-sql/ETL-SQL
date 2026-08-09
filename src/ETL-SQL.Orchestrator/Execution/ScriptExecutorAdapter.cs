using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Observability;
using ETL_SQL.Core.Profiling;
using ETL_SQL.Engine;
using Microsoft.Extensions.Configuration;

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

        public async Task<ScriptExecutionResult> ExecuteTextAsync(string scriptText, string? sessionId = null, CancellationToken cancellationToken = default, string? jobName = null, long queueWaitMs = 0, ETL_SQL.Core.Governance.ExecutionIdentity? executionIdentity = null)
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var startCpu = process.TotalProcessorTime.TotalSeconds;
            var runAt = DateTime.UtcNow;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var workloadKind = string.IsNullOrWhiteSpace(jobName) ? "script" : "job";
            var scriptHash = EngineExecutionObservability.IsTracingEnabled
                ? "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scriptText))).ToLowerInvariant()
                : null;
            using var activity = EngineExecutionObservability.StartExecutionActivity(scriptHash, jobName, CurrentCorrelationId());

            try
            {
                // Override the context's session ID if provided by the orchestrator (CQ-S2)
                if (!string.IsNullOrEmpty(sessionId))
                    _ctx.SessionId = sessionId;

                var session = new ExecutionSession(_serviceProvider, _ctx, _logger);
                var result = await session.ExecuteAsync(scriptText, cancellationToken, jobName, queueWaitMs, executionIdentity);
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
                        _logger.WriteLine($"[lineage catalog] Failed to persist lineage: {SecretRedactor.Redact(ex.Message)}", ConsoleColor.DarkYellow);
                    }
                }

                process.Refresh();
                var endCpu = process.TotalProcessorTime.TotalSeconds;
                var output = new ScriptExecutionResult(result.Success, result.RowsProcessed,
                    result.Success ? null : string.Join("; ", result.Diagnostics.Select(d => d.Message)),
                    process.PeakWorkingSet64, endCpu - startCpu, _ctx.SessionId,
                    result.RowsQuarantined, result.RowsWarned, result.DataQualityFailures,
                    result.DataQualityColumnMetrics, result.DataQualityRuleFailures,
                    CollectStatementMetrics(runFailed: !result.Success));
                sw.Stop();
                EngineExecutionObservability.CompleteExecutionActivity(
                    activity,
                    result.Success ? "success" : "failure",
                    workloadKind,
                    sw.ElapsedMilliseconds,
                    output.RowsProcessed,
                    output.PeakMemoryBytes,
                    output.CpuTimeSeconds,
                    LastEvaluator?.Telemetry.TotalSpilledBytes ?? 0,
                    LastEvaluator?.Telemetry.SpillReadBytes ?? 0);

                return output;
            }
            catch (Exception ex)
            {
                process.Refresh();
                var endCpu = process.TotalProcessorTime.TotalSeconds;
                var output = new ScriptExecutionResult(false, 0, SecretRedactor.Redact(ex.Message),
                    process.PeakWorkingSet64, endCpu - startCpu, _ctx.SessionId,
                    StatementMetrics: CollectStatementMetrics(runFailed: true));
                sw.Stop();
                EngineExecutionObservability.CompleteExecutionActivity(
                    activity,
                    "failure",
                    workloadKind,
                    sw.ElapsedMilliseconds,
                    output.RowsProcessed,
                    output.PeakMemoryBytes,
                    output.CpuTimeSeconds,
                    LastEvaluator?.Telemetry.TotalSpilledBytes ?? 0,
                    LastEvaluator?.Telemetry.SpillReadBytes ?? 0);
                return output;
            }
        }

        private static string? CurrentCorrelationId()
        {
            var value = Activity.Current?.GetTagItem(ObservabilityConventions.Tags.CorrelationId)?.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        /// <summary>
        /// Projects the run's per-statement measurements for the flight recorder.
        ///
        /// <para>The in-process path is the default (<c>UseProcessSpawning</c> is false) and needs
        /// no result envelope: the measurements are already in this process, so they are handed
        /// over directly rather than serialized through a child's stdout.</para>
        ///
        /// <para>The last statement of a failed run is marked failed. Nothing in the engine records
        /// a per-statement outcome, and the statement executing when the run stopped is the closest
        /// honest approximation — it is what an operator opens the run to find. Capping then keeps
        /// every failed statement and fills the rest of the budget with the slowest.</para>
        /// </summary>
        private IReadOnlyList<StatementMetricsPayload>? CollectStatementMetrics(bool runFailed)
        {
            var metrics = LastEvaluator?.Telemetry?.ProfileMetrics;
            if (metrics is null || metrics.Count == 0) return null;

            // How much of a run to keep is a deployment decision, not ours: a 200-job estate and a
            // single nightly load want very different budgets, and both limits drive storage.
            var configuration = _serviceProvider.GetService(typeof(IConfiguration)) as IConfiguration;
            var maxStatements = configuration?.GetValue<int>(
                "Orchestrator:MaxStatementsPerRun", StatementMetricsPayload.DefaultMaxStatements)
                ?? StatementMetricsPayload.DefaultMaxStatements;
            var maxTextLength = configuration?.GetValue<int>(
                "Orchestrator:MaxStatementTextLength", StatementTextNormalizer.DefaultMaxLength)
                ?? StatementTextNormalizer.DefaultMaxLength;

            return StatementMetricsPayload.FromRun(metrics, runFailed, maxStatements, maxTextLength);
        }
    }
}
