using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using System;
using System.Threading.Tasks;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles DROP VIEW for session-scoped query aliases.
    /// </summary>
    public class DropViewStatementHandler(ILogger logger) : IStatementHandler
    {
        public Type SupportedStatementType => typeof(DropViewStatement);

        public Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (DropViewStatement)statement;

            if (context.IsWhatIf)
            {
                logger.WriteLine($"WHAT IF: Would drop view {stmt.ViewName}", ConsoleColor.Yellow);
                return Task.CompletedTask;
            }

            if (!context.VarContext.RemoveView(stmt.ViewName) && !stmt.IfExists)
                throw new ExecutionException($"View not found: {stmt.ViewName}");

            return Task.CompletedTask;
        }
    }
}
