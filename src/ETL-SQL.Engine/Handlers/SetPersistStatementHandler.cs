using System;
using System.Threading.Tasks;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Handlers;

public class SetPersistStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(SetPersistStatement);

    public Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (SetPersistStatement)statement;
        context.DataContext.IsPersistentSession = stmt.Enabled;
        context.LoggingContext.Logger.Info("Session persistence set to {Enabled}", stmt.Enabled ? "ON" : "OFF");
        return Task.CompletedTask;
    }
}
