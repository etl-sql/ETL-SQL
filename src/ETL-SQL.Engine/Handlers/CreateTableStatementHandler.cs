using ETL_SQL.Data;
using System.Threading.Tasks;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the CREATE TABLE statement, delegating the operation to the target data source.
    /// </summary>
    public class CreateTableStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(CreateTableStatement);


        /// <summary>Executes the CREATE TABLE statement in the current context.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (CreateTableStatement)statement;
            
            _logger.Debug("Creating table {TableName} on {ConnectionName}", stmt.TargetTable.TableName, stmt.TargetTable.ConnectionName ?? "local");
            await context.EvaluateCreateTable(stmt);
        }
    }
}
