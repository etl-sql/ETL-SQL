using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the IF...ELSE IF...ELSE statement, providing conditional logic for script execution.
/// </summary>
public class IfStatementHandler : IStatementHandler
{
    private readonly ILogger _logger;
    public Type SupportedStatementType => typeof(IfStatement);

    public IfStatementHandler(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>Executes the IF statement, evaluating conditions and branching to the appropriate block.</summary>
    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (IfStatement)statement;

        _logger.Debug("Evaluating IF condition");
        if (await context.EvaluateCondition(stmt.Condition, new Row()))
        {
            await context.EvaluateStatement(stmt.IfBody);
            return;
        }

        if (stmt.ElseIfClauses != null)
        {
            foreach (var elseif in stmt.ElseIfClauses)
            {
                _logger.Debug("Evaluating ELSE IF condition");
                if (await context.EvaluateCondition(elseif.Condition, new Row()))
                {
                    await context.EvaluateStatement(elseif.Body);
                    return;
                }
            }
        }

        if (stmt.ElseBody != null)
        {
            _logger.Debug("Executing ELSE block");
            await context.EvaluateStatement(stmt.ElseBody);
        }
    }
}
