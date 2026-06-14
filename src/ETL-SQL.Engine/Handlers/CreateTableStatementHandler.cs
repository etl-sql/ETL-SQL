using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the CREATE TABLE statement, delegating the operation to the target data source.
    /// After table creation, seeds the LineageTracker with any inline <c>/* @tag: value */</c>
    /// metadata declared on column definitions so that tags are visible to SHOW TAGS,
    /// SHOW LINEAGE, and LINEAGE_TAG() without requiring a separate TAG or CREATE TAG statement.
    /// </summary>
    public class CreateTableStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(CreateTableStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (CreateTableStatement)statement;
            var tableName = stmt.TargetTable.TableName;

            _logger.Debug("Creating table {TableName} on {ConnectionName}", tableName, stmt.TargetTable.ConnectionName ?? "local");
            if (context.EngineContext is ETL_SQL.Engine.Evaluator eval)
            {
                await eval.SchemaManager.EvaluateCreateTable(stmt, context.DataContext.Connections);
            }

            // Seed the LineageTracker with any inline column-level tags declared in the
            // CREATE TABLE definition. This mirrors what CREATE TAG and TAG ... WITH() do,
            // so tags such as /*@d: ...*/ /*@pii: true*/ are immediately queryable.
            foreach (var col in stmt.Columns)
            {
                if (col.Metadata.Count > 0)
                    context.LineageTracker.ApplyTags(tableName, col.ColumnName, col.Metadata);
            }
        }
    }
}
