using System;
using System.Threading.Tasks;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the CREATE PROCEDURE statement, registering the procedure definition in the execution context.
    /// </summary>
    public class CreateProcedureStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(CreateProcedureStatement);
        /// <summary>Executes the CREATE PROCEDURE statement, performing existence checks and registering the definition.</summary>
        public Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (CreateProcedureStatement)statement;

            bool exists = context.VarContext.TryGetProcedure(stmt.ProcedureName, out _);
            if (stmt.Mode == ObjectCreationMode.Alter && !exists)
                throw new ExecutionException($"Procedure {stmt.ProcedureName} does not exist.");
            if (stmt.Mode == ObjectCreationMode.Create && exists)
                throw new ExecutionException($"Procedure {stmt.ProcedureName} already exists.");

            context.VarContext.SetProcedure(stmt.ProcedureName, stmt);
            return Task.CompletedTask;
        }
    }
}
