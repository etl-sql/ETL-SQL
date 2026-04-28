using ETL_SQL.Data;
using ETL_SQL.Core.Common.Exceptions;
using System;
using System.Threading.Tasks;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the DROP PROCEDURE statement.
    /// </summary>
    public class DropProcedureStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(DropProcedureStatement);

        public Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (DropProcedureStatement)statement;
            
            if (context.IsWhatIf)
            {
                _logger.WriteLine($"WHAT IF: Would drop procedure {stmt.ProcedureName}", ConsoleColor.Yellow);
                return Task.CompletedTask;
            }

            if (!context.VarContext.RemoveProcedure(stmt.ProcedureName) && !stmt.IfExists)
            {
                throw new ExecutionException($"Procedure not found: {stmt.ProcedureName}");
            }
            return Task.CompletedTask;
        }
    }
}
