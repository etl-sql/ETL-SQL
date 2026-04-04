using ETL_SQL.Data;
using System.Threading.Tasks;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the DROP PROCEDURE statement, removing the procedure definition from the execution context.
    /// </summary>
    public class DropProcedureStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(DropProcedureStatement);
        /// <summary>Executes the DROP PROCEDURE statement in the current context.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (DropProcedureStatement)statement;
            
            context.EvaluateDropProcedure(stmt);
            await Task.CompletedTask;
        }
    }
}



