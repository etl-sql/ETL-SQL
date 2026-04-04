using System.Threading.Tasks;
using System.Threading.Tasks;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the COMMIT TRANSACTION statement, persisting all changes made during the current transaction.
    /// </summary>
    public class CommitTransactionStatementHandler : IStatementHandler
    {
        public System.Type SupportedStatementType => typeof(CommitTransactionStatement);

        /// <summary>Commits the active transaction in the current execution context.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            await context.CommitTransaction();
        }
    }
}
