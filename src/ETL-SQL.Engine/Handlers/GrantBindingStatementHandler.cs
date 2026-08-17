using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;

public class GrantBindingStatementHandler(ILogger logger) : IStatementHandler
{
    public Type SupportedStatementType => typeof(GrantBindingStatement);

    public Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (GrantBindingStatement)statement;

        if (context.IsWhatIf)
        {
            logger.Info($"WHAT_IF: Granting {stmt.Permission} on binding '{stmt.BindingName}' to {stmt.PrincipalKind} '{stmt.PrincipalName}'...");
            return Task.CompletedTask;
        }

        logger.Info($"Granted {stmt.Permission} on binding '{stmt.BindingName}' to {stmt.PrincipalKind} '{stmt.PrincipalName}'.");
        return Task.CompletedTask;
    }
}
