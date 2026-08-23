using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Handlers;

public class CreateBookmarkStatementHandler(ILogger logger) : IStatementHandler
{
    private readonly ILogger _logger = logger;
    public Type SupportedStatementType => typeof(CreateBookmarkStatement);

    public Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (CreateBookmarkStatement)statement;

        if (context.ReportContext.BookmarkDefinitions.ContainsKey(stmt.Name))
            throw new ExecutionException($"Bookmark '{stmt.Name}' already exists. Duplicate bookmark identifiers are not allowed.", null, stmt.Line, stmt.Column);

        if (stmt.IsDefault && context.ReportContext.BookmarkDefinitions.Values.Any(b => b.IsDefault))
        {
            var existing = context.ReportContext.BookmarkDefinitions.Values.First(b => b.IsDefault);
            throw new ExecutionException($"Bookmark '{stmt.Name}' cannot be DEFAULT because '{existing.Name}' is already the default bookmark. Only one author bookmark may be DEFAULT.", null, stmt.Line, stmt.Column);
        }

        context.ReportContext.BookmarkDefinitions[stmt.Name] = stmt;

        _logger.Debug("Bookmark '{BookmarkName}' registered{Default}.", stmt.Name, stmt.IsDefault ? " (DEFAULT)" : "");
        context.Log($"Bookmark '{stmt.Name}' created{(stmt.IsDefault ? " (default)" : "")}.");

        return Task.CompletedTask;
    }
}
