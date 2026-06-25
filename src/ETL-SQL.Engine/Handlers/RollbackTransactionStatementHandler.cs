using System.Threading.Tasks;
using System.Threading.Tasks;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the ROLLBACK TRANSACTION statement, reverting changes to the last snapshot or a named savepoint.
/// </summary>
public class RollbackTransactionStatementHandler : IStatementHandler
{
    public System.Type SupportedStatementType => typeof(RollbackTransactionStatement);

    /// <summary>Rolls back the active transaction or to a specific savepoint.</summary>
    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var rollback = (RollbackTransactionStatement)statement;
        await context.RollbackTransaction(rollback.Name);
    }
}
