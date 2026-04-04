using System;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the FOR loop statement, providing iterative execution with numeric ranges and steps.
    /// </summary>
    public class ForStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ForStatement);
        /// <summary>Executes the FOR statement, managing the loop variable and body execution.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ForStatement)statement;
            
            var start = Convert.ToInt32(await context.EvaluateValue(stmt.StartValue, new Row()));
            var end = Convert.ToInt32(await context.EvaluateValue(stmt.EndValue, new Row()));
            var step = stmt.StepValue != null ? Convert.ToInt32(await context.EvaluateValue(stmt.StepValue, new Row())) : 1;
            
            Logger.Verbose($"Starting FOR loop for {stmt.VariableName} from {start} to {end} step {step}");

            if (!context.ContainsVariable(stmt.VariableName))
            {
                context.DeclareVariable(stmt.VariableName, (decimal)start);
            }

            for (int i = start; (step > 0 ? i <= end : i >= end); i += step)
            {
                context.SetVariable(stmt.VariableName, (decimal)i);
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
