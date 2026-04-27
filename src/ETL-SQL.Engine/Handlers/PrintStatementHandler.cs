using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the PRINT statement, outputting messages or expression values to the logger.
    /// </summary>
    public class PrintStatementHandler : IStatementHandler
    {
        private readonly ILogger _logger;
        public Type SupportedStatementType => typeof(PrintStatement);

        public PrintStatementHandler(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>Executes the PRINT statement, evaluating and concatenating all provided arguments.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (PrintStatement)statement;
            var row = new Row();

            var parts = new System.Text.StringBuilder();
            parts.Append((await context.EvaluationContext.EvaluateValue(stmt.Message, row))?.ToString() ?? "NULL");

            if (stmt.ShowTimestamp != null)
                parts.Append(' ').Append((await context.EvaluationContext.EvaluateValue(stmt.ShowTimestamp, row))?.ToString() ?? "NULL");

            if (stmt.TimestampFormat != null)
                parts.Append(' ').Append((await context.EvaluationContext.EvaluateValue(stmt.TimestampFormat, row))?.ToString() ?? "NULL");

            context.LoggingContext.Log(parts.ToString(), ConsoleColor.White);
        }
    }
}
