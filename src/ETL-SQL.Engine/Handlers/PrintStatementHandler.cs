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
        public Type SupportedStatementType => typeof(PrintStatement);
        /// <summary>Executes the PRINT statement, evaluating the message expression and logging it.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (PrintStatement)statement;
            
            
            var val = await context.EvaluateValue(stmt.Message, new Row());
            Logger.WriteLine(val?.ToString() ?? "NULL", ConsoleColor.White);
        }
    }
}



