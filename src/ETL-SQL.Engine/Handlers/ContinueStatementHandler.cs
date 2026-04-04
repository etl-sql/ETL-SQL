using System.Threading.Tasks;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the CONTINUE statement, used to skip the remaining statements in a loop iteration.
    /// </summary>
    public class ContinueStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ContinueStatement);
        /// <summary>Throws a ContinueException to signal skipping to the next loop iteration.</summary>
        public Task Execute(Statement statement, IExecutionContext context)
        {
            throw new ContinueException();
        }
    }
}



