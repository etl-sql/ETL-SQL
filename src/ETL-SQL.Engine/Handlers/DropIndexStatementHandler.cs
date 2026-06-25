using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the DROP INDEX statement.
/// </summary>
public class DropIndexStatementHandler(ILogger logger) : IStatementHandler
{
    private readonly ILogger _logger = logger;
    public Type SupportedStatementType => typeof(DropIndexStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (DropIndexStatement)statement;
        string connName = stmt.Table?.ConnectionName ?? stmt.Table?.TableName ?? "local";

        _logger.Debug("Dropping index {IndexName} on {ConnectionName}", stmt.IndexName, connName);
        if (context.EngineContext is Evaluator eval)
        {
            await eval.SchemaManager.EvaluateDropIndex(stmt, context.DataContext.Connections);
        }
    }
}
