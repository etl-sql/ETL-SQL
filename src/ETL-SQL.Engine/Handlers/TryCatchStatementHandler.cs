using System;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the TRY...CATCH statement, providing error handling and recovery logic.
    /// </summary>
    public class TryCatchStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(TryCatchStatement);
        /// <summary>Executes the TRY block and branches to CATCH on any exception, setting @ERROR_MESSAGE.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (TryCatchStatement)statement;
            
            Logger.Verbose($"Entering TRY block");
            try
            {
                await context.EvaluateStatement(stmt.TryBody);
            }
            catch (Exception ex)
            {
                Logger.Verbose($"Exception caught in TRY block: {ex.Message}");
                if (!context.ContainsVariable("@ERROR_MESSAGE"))
                {
                    context.DeclareVariable("@ERROR_MESSAGE", ex.Message);
                }
                else
                {
                    context.SetVariable("@ERROR_MESSAGE", ex.Message);
                }
                await context.EvaluateStatement(stmt.CatchBody);
            }
        }
    }
}
