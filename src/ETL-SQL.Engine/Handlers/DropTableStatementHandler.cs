using ETL_SQL.Data;
using System.Threading.Tasks;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the DROP TABLE statement, delegating the operation to the target data source.
    /// </summary>
    public class DropTableStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(DropTableStatement);


        /// <summary>Executes the DROP TABLE statement in the current context.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (DropTableStatement)statement;
            
            _logger.Debug($"Dropping table {stmt.TargetTable.TableName} on {stmt.TargetTable.ConnectionName ?? "local"}");
            await context.EvaluateDropTable(stmt);
        }
    }
}
