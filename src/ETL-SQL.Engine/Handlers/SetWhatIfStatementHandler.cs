using System;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the SET WHAT_IF ON/OFF statement, enabling or disabling dry-run execution.
    /// </summary>
    public class SetWhatIfStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(SetWhatIfStatement);


        /// <summary>Executes the SET WHAT_IF statement, updating the evaluator's state.</summary>
        public Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (SetWhatIfStatement)statement;
            context.IsWhatIf = stmt.Enabled;
            _logger.WriteLine($"Dry-run mode (WHAT_IF) is now {(stmt.Enabled ? "ON" : "OFF")}.", ConsoleColor.Cyan);
            return Task.CompletedTask;
        }
    }
}
