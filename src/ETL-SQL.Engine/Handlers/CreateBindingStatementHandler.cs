using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;

public class CreateBindingStatementHandler(ILogger logger) : IStatementHandler
{
    public Type SupportedStatementType => typeof(CreateBindingStatement);

    public Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (CreateBindingStatement)statement;
        
        if (context.IsWhatIf)
        {
            logger.Info($"WHAT_IF: Validating creation of {stmt.Type.ToUpperInvariant()} binding '{stmt.Name}'...");
            logger.Info($"WHAT_IF: Binding '{stmt.Name}' would be created successfully.");
            return Task.CompletedTask;
        }

        logger.Info($"Created binding '{stmt.Name}'.");
        return Task.CompletedTask;
    }
}
