using ETL_SQL.Data;
using ETL_SQL.Core.Common.Exceptions;
using System;
using System.Threading.Tasks;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the DROP FUNCTION statement.
    /// </summary>
    public class DropFunctionStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(DropFunctionStatement);

        public Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (DropFunctionStatement)statement;

            if (context.IsWhatIf)
            {
                _logger.WriteLine($"WHAT IF: Would drop function {stmt.FunctionName}", ConsoleColor.Yellow);
                return Task.CompletedTask;
            }

            if (!context.VarContext.RemoveFunction(stmt.FunctionName) && !stmt.IfExists)
            {
                throw new ExecutionException($"Function not found: {stmt.FunctionName}");
            }
            return Task.CompletedTask;
        }
    }
}
