using System;
using System.Collections.Generic;
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
        /// <summary>Executes the PRINT statement, evaluating and concatenating all provided arguments.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (PrintStatement)statement;
            var values = new List<string>();

            foreach (var arg in stmt.Arguments)
            {
                var val = await context.EvaluateValue(arg, new Row());
                values.Add(val?.ToString() ?? "NULL");
            }

            var message = string.Join(" ", values);

            if (stmt.ShowTimestamp != null && (bool)(await context.EvaluateValue(stmt.ShowTimestamp, new Row()) ?? false))
            {
                string format = (await context.EvaluateValue(stmt.TimestampFormat, new Row()))?.ToString() ?? "yyyy-MM-dd HH:mm:ss";
                message = $"[{DateTime.Now.ToString(format)}] {message}";
            }

            context.Log(message);
        }
    }
}
