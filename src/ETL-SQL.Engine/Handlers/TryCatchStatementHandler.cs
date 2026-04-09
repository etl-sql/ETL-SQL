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
        private readonly ILogger _logger;
        public Type SupportedStatementType => typeof(TryCatchStatement);

        public TryCatchStatementHandler(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>Executes the TRY block and branches to CATCH on any exception, setting @ERROR_MESSAGE.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (TryCatchStatement)statement;
            
            _logger.Debug($"Entering TRY block");
            try
            {
                await context.EvaluateStatement(stmt.TryBody);
            }
            catch (Exception ex)
            {
                _logger.Debug($"Exception caught in TRY block: {ex.Message}");
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
