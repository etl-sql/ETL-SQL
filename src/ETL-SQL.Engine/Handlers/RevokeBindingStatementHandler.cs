using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;

public class RevokeBindingStatementHandler(ILogger logger) : IStatementHandler
{
    public Type SupportedStatementType => typeof(RevokeBindingStatement);

    public Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (RevokeBindingStatement)statement;
        
        if (context.IsWhatIf)
        {
            logger.Info($"WHAT_IF: Revoking {stmt.Permission} on binding '{stmt.BindingName}' from {stmt.PrincipalKind} '{stmt.PrincipalName}'...");
            return Task.CompletedTask;
        }

        logger.Info($"Revoked {stmt.Permission} on binding '{stmt.BindingName}' from {stmt.PrincipalKind} '{stmt.PrincipalName}'.");
        return Task.CompletedTask;
    }
}
