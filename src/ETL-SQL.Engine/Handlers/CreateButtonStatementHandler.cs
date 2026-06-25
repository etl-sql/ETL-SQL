using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles CREATE BUTTON statements.
/// Registers interactive dashboard controls in the report context.
/// </summary>
public class CreateButtonStatementHandler(ILogger logger) : IStatementHandler
{
    private readonly ILogger _logger = logger;
    public Type SupportedStatementType => typeof(CreateButtonStatement);

    public Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (CreateButtonStatement)statement;

        if (stmt.Mode == ObjectCreationMode.Create && context.ReportContext.ButtonDefinitions.ContainsKey(stmt.Name))
        {
            throw new ExecutionException($"Button '{stmt.Name}' already exists. Use CREATE OR ALTER or DROP BUTTON first.", null, stmt.Line, stmt.Column);
        }

        context.ReportContext.ButtonDefinitions[stmt.Name] = stmt;

        _logger.Debug("Button '{ButtonName}' ({ButtonType}) registered.", stmt.Name, stmt.ButtonType);
        context.Log($"Button '{stmt.Name}' {(stmt.Mode == ObjectCreationMode.CreateOrAlter ? "updated" : "created")}.");

        return Task.CompletedTask;
    }
}

