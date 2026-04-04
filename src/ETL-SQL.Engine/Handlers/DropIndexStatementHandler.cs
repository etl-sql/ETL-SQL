using ETL_SQL.Data;
using System.Threading.Tasks;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the DROP INDEX statement, delegating to the target data source.
    /// </summary>
    public class DropIndexStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(DropIndexStatement);
        /// <summary>Executes the DROP INDEX statement in the current context.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (DropIndexStatement)statement;
            
            Logger.Verbose($"Dropping index {stmt.IndexName} from {stmt.Table?.TableName ?? "unknown table"}");
            await context.EvaluateDropIndex(stmt);
        }
    }
}



