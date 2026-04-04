using ETL_SQL.Data;
using ETL_SQL.Core.Common.Exceptions;
using System;
using System.Threading.Tasks;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the CREATE FUNCTION statement, registering the function definition in the execution context.
    /// </summary>
    public class CreateFunctionStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(CreateFunctionStatement);
        /// <summary>Executes the CREATE FUNCTION statement, performing existence checks and registering the definition.</summary>
        public Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (CreateFunctionStatement)statement;
            
            
            bool exists = context.FunctionExists(stmt.FunctionName);
            if (stmt.Mode == ObjectCreationMode.Alter && !exists)
                throw new ExecutionException($"Function {stmt.FunctionName} does not exist.");
            if (stmt.Mode == ObjectCreationMode.Create && exists)
                throw new ExecutionException($"Function {stmt.FunctionName} already exists.");

            context.EvaluateCreateFunction(stmt);
            return Task.CompletedTask;
        }
    }
}



