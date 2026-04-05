using ETL_SQL.Core;
using System;
using System.Threading.Tasks;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the CLEAR SESSION statement, deleting session files and temporary data from disk.
    /// </summary>
    public class ClearSessionStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ClearSessionStatement);

        /// <summary>Executes the CLEAR SESSION statement in the current context.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ClearSessionStatement)statement;
            await context.EvaluateClearSession(stmt);
        }
    }
}
