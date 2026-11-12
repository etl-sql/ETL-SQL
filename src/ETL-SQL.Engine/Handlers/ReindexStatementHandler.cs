using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the REINDEX statement.
/// </summary>
public class ReindexStatementHandler(ILogger logger) : IStatementHandler
{
    private readonly ILogger _logger = logger;
    public Type SupportedStatementType => typeof(ReindexStatement);

    public Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ReindexStatement)statement;

        _logger.Debug("Reindexing {Name}", stmt.Name);
        if (context.EngineContext is Evaluator eval)
        {
            eval.SchemaManager.EvaluateReindex(stmt, context.DataContext.Connections);
        }
        return Task.CompletedTask;
    }
}
