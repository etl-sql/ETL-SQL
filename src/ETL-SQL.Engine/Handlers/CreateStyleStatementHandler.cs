using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles CREATE STYLE statements. Registers the named style in the report context
/// so it can be referenced by CREATE VISUAL / PAGE / CONTAINER via STYLE = &lt;name&gt;.
/// </summary>
public class CreateStyleStatementHandler(ILogger logger) : IStatementHandler
{
    private readonly ILogger _logger = logger;
    public Type SupportedStatementType => typeof(CreateStyleStatement);

    public Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (CreateStyleStatement)statement;

        var alreadyExists = context.ReportContext.StyleDefinitions.ContainsKey(stmt.Name);
        if (stmt.Mode == ObjectCreationMode.Create && alreadyExists)
        {
            throw new Core.Common.Exceptions.ExecutionException($"Style '{stmt.Name}' already exists. Use CREATE OR ALTER or DROP STYLE first.", null, stmt.Line, stmt.Column);
        }

        context.ReportContext.StyleDefinitions[stmt.Name] = stmt;

        _logger.Debug("Registered style '{StyleName}' with {Count} properties.", stmt.Name, stmt.Styles.Count);
        context.Log($"Style '{stmt.Name}' {(alreadyExists ? "updated" : "registered")}.");
        return Task.CompletedTask;
    }
}
