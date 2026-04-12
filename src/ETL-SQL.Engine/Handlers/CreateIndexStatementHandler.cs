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
        private readonly ILogger _logger;
        public Type SupportedStatementType => typeof(CreateIndexStatement);

        public CreateIndexStatementHandler(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>Executes the CREATE INDEX statement in the current context.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (CreateIndexStatement)statement;
            
            _logger.Debug("Creating index {IndexName} on {TableName}", stmt.IndexName, stmt.TargetTable.TableName);
            await context.EvaluateCreateIndex(stmt);
        }
    }
}
