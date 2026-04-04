using System;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the WAITFOR DELAY statement, pausing execution for the specified duration.
    /// The delay expression should evaluate to a string in 'hh:mm:ss' or 'hh:mm:ss.fff' format.
    /// </summary>
    public class WaitForStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(WaitForStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (WaitForStatement)statement;
            var raw = (await context.EvaluateValue(stmt.DelayExpression, new Row()))?.ToString() ?? "0";

            TimeSpan delay;
            if (!TimeSpan.TryParse(raw, out delay))
                throw new ETL_SQL.Core.Common.Exceptions.ExecutionException(
                    $"WAITFOR DELAY: invalid time format '{raw}'. Expected 'hh:mm:ss' or 'hh:mm:ss.fff'.");

            if (delay < TimeSpan.Zero)
                throw new ETL_SQL.Core.Common.Exceptions.ExecutionException(
                    "WAITFOR DELAY: delay must be non-negative.");

            Logger.Verbose($"[WaitFor] Pausing for {delay}");
            await Task.Delay(delay);
        }
    }
}
