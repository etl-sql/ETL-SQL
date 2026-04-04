using ETL_SQL.Data;
using System.Threading.Tasks;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the DROP FUNCTION statement, removing the function definition from the execution context.
    /// </summary>
    public class DropFunctionStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(DropFunctionStatement);
        /// <summary>Executes the DROP FUNCTION statement in the current context.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (DropFunctionStatement)statement;
            
            context.EvaluateDropFunction(stmt);
            await Task.CompletedTask;
        }
    }
}



