using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the FOR loop statement, providing iterative execution with numeric ranges and steps.
/// </summary>
public class ForStatementHandler : IStatementHandler
{
    private readonly ILogger _logger;
    public Type SupportedStatementType => typeof(ForStatement);

    public ForStatementHandler(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>Executes the FOR statement, managing the loop variable and body execution.</summary>
    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ForStatement)statement;

        var start = Convert.ToInt32(await context.EvaluateValue(stmt.StartValue, Row.Empty));
        var end = Convert.ToInt32(await context.EvaluateValue(stmt.EndValue, Row.Empty));
        var step = stmt.StepValue != null ? Convert.ToInt32(await context.EvaluateValue(stmt.StepValue, Row.Empty)) : 1;

        _logger.Debug("Starting FOR loop for {VariableName} from {Start} to {End} step {Step}", stmt.VariableName, start, end, step);

        if (!context.VarContext.ContainsVariable(stmt.VariableName))
        {
            context.VarContext.DeclareVariable(stmt.VariableName, (decimal)start);
        }

        for (int i = start; (step > 0 ? i <= end : i >= end); i += step)
        {
            context.VarContext.SetVariable(stmt.VariableName, (decimal)i);
            try
            {
                await context.EvaluateStatement(stmt.Body);
            }
            catch (BreakException)
            {
                break;
            }
            catch (ContinueException)
            {
                continue;
            }
        }
    }
}

