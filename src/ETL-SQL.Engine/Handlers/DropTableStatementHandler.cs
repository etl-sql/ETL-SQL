using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the DROP TABLE statement, delegating the operation to the target data source.
    /// </summary>
    public class DropTableStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(DropTableStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (DropTableStatement)statement;

            _logger.Debug("Dropping table {TableName} on {ConnectionName}", stmt.TargetTable.TableName, stmt.TargetTable.ConnectionName ?? "local");
            if (context.EngineContext is Evaluator eval)
            {
                await eval.SchemaManager.EvaluateDropTable(stmt, context.DataContext.Connections);
            }
        }
    }
}
