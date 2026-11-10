using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Engine.Handlers;

/// <summary>
/// Handles SET DATEFORMAT = 'MDY'|'DMY': how slash dates in text (08/01/2026) are read for the rest
/// of the script. MDY (1 August) is the default unless Engine:DateFormat says otherwise.
/// </summary>
public class SetDateFormatHandler(ILogger logger) : IStatementHandler
{
    public Type SupportedStatementType => typeof(SetDateFormatStatement);

    public Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (SetDateFormatStatement)statement;
        if (!DateTextOrder.TryParse(stmt.Order, out var order))
            throw new ExecutionException($"Invalid DATEFORMAT value: '{stmt.Order}'. Valid values: MDY, DMY.");

        context.DateOrder = order;
        logger.WriteLine($"Date format set to {DateTextOrder.Name(order)}.", ConsoleColor.Cyan);
        return Task.CompletedTask;
    }
}
