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


        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (CreateTableStatement)statement;
            
            _logger.Debug("Creating table {TableName} on {ConnectionName}", stmt.TargetTable.TableName, stmt.TargetTable.ConnectionName ?? "local");
            if (context.EngineContext is ETL_SQL.Engine.Evaluator eval)
            {
                await eval.SchemaManager.EvaluateCreateTable(stmt, context.DataContext.Connections);
            }
        }
    }
}
