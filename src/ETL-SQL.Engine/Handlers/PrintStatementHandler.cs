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

        /// <summary>Executes the PRINT statement, evaluating the message expression and logging it.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (PrintStatement)statement;
            
            var val = await context.EvaluateValue(stmt.Message, new Row());
            _logger.WriteLine(val?.ToString() ?? "NULL", ConsoleColor.White);
        }
    }
}
