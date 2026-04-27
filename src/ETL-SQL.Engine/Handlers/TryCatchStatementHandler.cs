using System;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;

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
            
            _logger.Debug("Entering TRY block");
            try
            {
                await context.EvaluateStatement(stmt.TryBody);
            }
            catch (Exception ex)
            {
                _logger.Debug("Exception caught in TRY block: {Message}", ex.Message);
                
                int number = 50000;
                int severity = 16;
                int state = 1;
                int line = context.LastError?.Line ?? 0; // EvaluateStatement sets this with statement.Line before rethrowing
                string? message = ex.Message;

                if (ex is ExecutionException ee)
                {
                    number = ee.ErrorNumber;
                    severity = ee.Severity;
                    state = ee.State;
                    if (ee.Line > 0) line = ee.Line; // explicit line wins over statement-level fallback
                }

                var errorInfo = new ErrorInfo(number, message, severity, state, line, null);
                context.LastError = errorInfo;

                var oldActive = context.ActiveException;
                context.ActiveException = errorInfo;

                if (!context.ContainsVariable("@ERROR_MESSAGE"))
                {
                    context.DeclareVariable("@ERROR_MESSAGE", message);
                }
                else
                {
                    context.SetVariable("@ERROR_MESSAGE", message);
                }

                try
                {
                    await context.EvaluateStatement(stmt.CatchBody);
                }
                finally
                {
                    context.ActiveException = oldActive;
                }
            }
        }
    }
}
