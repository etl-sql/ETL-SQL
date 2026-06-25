using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the RETURN statement, exiting from a script, procedure, or function with an optional value.
/// </summary>
public class ReturnStatementHandler : IStatementHandler
{
    private readonly ILogger _logger;
    public Type SupportedStatementType => typeof(ReturnStatement);

    public ReturnStatementHandler(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>Evaluates the return value and throws a ReturnException to signal script exit.</summary>
    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ReturnStatement)statement;

        _logger.Debug("Executing RETURN");
        var val = stmt.ReturnValue != null ? await context.EvaluateValue(stmt.ReturnValue, new Row()) : null;
        throw new ReturnException(val);
    }
}
