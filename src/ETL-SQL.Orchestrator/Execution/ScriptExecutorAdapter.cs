using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Common;

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

        public ScriptExecutorAdapter(IServiceProvider serviceProvider, CliContext ctx, ILogger logger)
        {
            _serviceProvider = serviceProvider;
            _ctx = ctx;
            _logger = logger;
        }

        public async Task<ScriptExecutionResult> ExecuteTextAsync(string scriptText, CancellationToken cancellationToken = default)
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var startCpu = process.TotalProcessorTime.TotalSeconds;
            
            try
            {
                var session = new ExecutionSession(_serviceProvider, _ctx, _logger);
                var result = await session.ExecuteAsync(scriptText, cancellationToken);
                
                process.Refresh();
                var endCpu = process.TotalProcessorTime.TotalSeconds;
                
                return new ScriptExecutionResult(result.Success, result.RowsProcessed,
                    result.Success ? null : string.Join("; ", result.Diagnostics.Select(d => d.Message)),
                    process.PeakWorkingSet64, endCpu - startCpu);
            }
            catch (Exception ex)
            {
                process.Refresh();
                var endCpu = process.TotalProcessorTime.TotalSeconds;
                return new ScriptExecutionResult(false, 0, ex.Message, process.PeakWorkingSet64, endCpu - startCpu);
            }
        }
    }
}
