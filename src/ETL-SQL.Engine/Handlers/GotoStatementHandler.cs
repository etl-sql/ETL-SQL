using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the GOTO statement by throwing a GotoException containing the target label name.
/// </summary>
public class GotoStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(GotoStatement);

    public Task Execute(Statement statement, IExecutionContext context)
    {
        var gotoStmt = (GotoStatement)statement;
        throw new GotoException(gotoStmt.LabelName);
    }
}
