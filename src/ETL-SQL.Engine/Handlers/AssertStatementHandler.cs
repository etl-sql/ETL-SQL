using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the ASSERT statement, evaluating a condition and throwing an exception if false.
/// </summary>
public class AssertStatementHandler : IStatementHandler
{
    private readonly ILogger _logger;
    public Type SupportedStatementType => typeof(AssertStatement);

    public AssertStatementHandler(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>Executes the ASSERT statement, evaluating the condition and throwing if it fails.</summary>
    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (AssertStatement)statement;

        _logger.Debug("Evaluating ASSERT condition");
        if (!await context.EvaluateCondition(stmt.Condition, Row.Empty))
        {
            string message = "Assertion failed";
            if (stmt.Message != null)
            {
                var msgVal = await context.EvaluateValue(stmt.Message, Row.Empty);
                message = msgVal?.ToString() ?? message;
            }

            _logger.Error($"[ASSERT FAIL] {message}");

            // Throwing ExecutionException ensures the engine stops and reports the failure properly.
            throw new ExecutionException(message);
        }

        _logger.Debug("ASSERT passed");
    }
}
