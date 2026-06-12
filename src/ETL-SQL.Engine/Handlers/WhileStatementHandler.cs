using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the WHILE statement, supporting iterative execution with support for BREAK and CONTINUE.
    /// </summary>
    public class WhileStatementHandler : IStatementHandler
    {
        private readonly ILogger _logger;
        public Type SupportedStatementType => typeof(WhileStatement);

        public WhileStatementHandler(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>Executes the WHILE statement, managing loop iteration and control flow exceptions.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (WhileStatement)statement;

            _logger.Debug("Starting WHILE loop");
            while (true)
            {
                _logger.Debug("Evaluating WHILE condition");
                var conditionResult = await context.EvaluateValue(stmt.Condition, new Row());
                bool condition = conditionResult is bool b && b;

                if (!condition) break;

                try
                {
                    await context.EvaluateStatement(stmt.Body);
                }
                catch (BreakException)
                {
                    break;
                }
                catch (ContinueException)
                {
                    continue;
                }
            }
        }
    }
}
