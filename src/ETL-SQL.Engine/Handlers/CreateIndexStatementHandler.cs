using ETL_SQL.Data;
using System.Threading.Tasks;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the CREATE INDEX statement, delegating to the target data source.
    /// </summary>
    public class CreateIndexStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(CreateIndexStatement);
        /// <summary>Executes the CREATE INDEX statement in the current context.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (CreateIndexStatement)statement;
            
            Logger.Verbose($"Creating index {stmt.IndexName} on {stmt.TargetTable.TableName}");
            await context.EvaluateCreateIndex(stmt);
        }
    }
}



