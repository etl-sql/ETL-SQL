using ETL_SQL.Data;
using ETL_SQL.Core.Common.Exceptions;
using System;
using System.Threading.Tasks;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the RAISERROR statement, allowing scripts to log informational messages or throw execution errors with custom severity.
    /// </summary>
    public class RaiseErrorStatementHandler : IStatementHandler
    {
        private readonly ILogger _logger;
        public Type SupportedStatementType => typeof(RaiseErrorStatement);

        public RaiseErrorStatementHandler(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>Executes the RAISERROR statement, evaluating the message and severity, and potentially throwing an exception.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (RaiseErrorStatement)statement;
            
            var msg = (await context.EvaluateValue(stmt.Message, new Row()))?.ToString() ?? "NULL";
            int severity = 2;
            if (decimal.TryParse((await context.EvaluateValue(stmt.Severity, new Row()))?.ToString(), out var s)) severity = (int)s;
            var fullMsg = $"[{(severity >= 4 ? "ERROR" : "INFO")}] {msg}";
            _logger.WriteLine(fullMsg, severity >= 4 ? ConsoleColor.Red : ConsoleColor.White);
            if (severity >= 4) throw new ExecutionException(fullMsg);
        }
    }
}
