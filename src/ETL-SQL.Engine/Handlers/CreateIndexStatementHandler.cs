using ETL_SQL.Data;
using System;
using System.Threading.Tasks;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the CREATE INDEX statement.
    /// </summary>
    public class CreateIndexStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(CreateIndexStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (CreateIndexStatement)statement;
            
            _logger.Debug("Creating index {IndexName} on {ConnectionName}", stmt.IndexName, stmt.TargetTable.ConnectionName ?? "local");
            if (context.EngineContext is Evaluator eval)
            {
                await eval.SchemaManager.EvaluateCreateIndex(stmt, context.DataContext.Connections);
            }
        }
    }
}
