using ETL_SQL.Data;
using System.Threading.Tasks;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the DROP CONNECTION statement, removing registered connections from the execution context.
    /// </summary>
    public class DropConnectionStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(DropConnectionStatement);
        /// <summary>Executes the DROP CONNECTION statement in the current context.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (DropConnectionStatement)statement;
            
            await context.EvaluateDropConnection(stmt);
        }
    }
}



