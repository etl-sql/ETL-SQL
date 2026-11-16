using System;
using System.Threading.Tasks;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;

/// <summary>
/// <c>CREATE TOOL</c> and <c>DROP TOOL</c> parse, but no host stores tools from a script: the tool
/// catalog is machine-protected administrator configuration written by <c>etl-sql admin machine tool set</c>.
/// Without these handlers the statements failed with a generic "no handler" error, so a script
/// author could not tell that the documented statement does not register anything.
/// </summary>
internal static class ToolCatalogStatementMessage
{
    public const string Text =
        "Tools are registered by an administrator in the machine tool catalog, not from a script. " +
        "Use 'etl-sql admin machine tool set --name <name> --type EXECUTABLE|CONTAINER --option KEY=VALUE' " +
        "(and 'etl-sql admin machine tool delete --name <name>'), then call the tool with EXECUTE TOOL.";
}

public sealed class CreateToolStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(CreatePortalToolStatement);

    public Task Execute(Statement statement, IExecutionContext context) =>
        throw new ExecutionException(
            $"CREATE TOOL is not supported. {ToolCatalogStatementMessage.Text}", null, statement.Line, statement.Column);
}

public sealed class DropToolStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(DropPortalToolStatement);

    public Task Execute(Statement statement, IExecutionContext context) =>
        throw new ExecutionException(
            $"DROP TOOL is not supported. {ToolCatalogStatementMessage.Text}", null, statement.Line, statement.Column);
}
