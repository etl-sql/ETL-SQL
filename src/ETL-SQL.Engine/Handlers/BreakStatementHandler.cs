using System.Threading.Tasks;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the BREAK statement, used to exit from WHILE or FOR loops.
/// </summary>
public class BreakStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(BreakStatement);
    /// <summary>Thows a BreakException to signal loop termination.</summary>
    public Task Execute(Statement statement, IExecutionContext context)
    {
        throw new BreakException();
    }
}



