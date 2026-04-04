using ETL_SQL.Data;
using ETL_SQL.Core.Common.Exceptions;
using System;
using System.Threading.Tasks;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the RAISERROR statement, allowing scripts to log informational messages or throw execution errors with custom severity.
    /// </summary>
    public class RaiseErrorStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(RaiseErrorStatement);
        /// <summary>Executes the RAISERROR statement, evaluating the message and severity, and potentially throwing an exception.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (RaiseErrorStatement)statement;
            
            var msg = (await context.EvaluateValue(stmt.Message, new Row()))?.ToString() ?? "NULL";
            int severity = 2;
            if (decimal.TryParse((await context.EvaluateValue(stmt.Severity, new Row()))?.ToString(), out var s)) severity = (int)s;
            var fullMsg = $"[{(severity >= 4 ? "ERROR" : "INFO")}] {msg}";
            Common.Logger.WriteLine(fullMsg, severity >= 4 ? ConsoleColor.Red : ConsoleColor.White);
            if (severity >= 4) throw new ExecutionException(fullMsg);
        }
    }
}



