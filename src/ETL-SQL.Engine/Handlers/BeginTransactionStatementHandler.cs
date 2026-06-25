using System.Threading.Tasks;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the BEGIN TRANSACTION statement, initiating a new transaction context in the engine.
/// </summary>
public class BeginTransactionStatementHandler : IStatementHandler
{
    public System.Type SupportedStatementType => typeof(BeginTransactionStatement);

    /// <summary>Starts a new transaction in the current execution context.</summary>
    public async Task Execute(Statement statement, IExecutionContext context)
    {
        await context.BeginTransaction();
    }
}
