using System;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the WHILE statement, supporting iterative execution with support for BREAK and CONTINUE.
    /// </summary>
    public class WhileStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(WhileStatement);
        /// <summary>Executes the WHILE statement, managing loop iteration and control flow exceptions.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (WhileStatement)statement;
            
            
            Logger.Verbose($"Starting WHILE loop");
            while (true)
            {
                Logger.Verbose($"Evaluating WHILE condition");
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



