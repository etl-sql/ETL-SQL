using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the THROW statement, explicitly failing script execution with a custom error message.
    /// </summary>
    public class ThrowStatementHandler : IStatementHandler
    {
        private readonly ILogger _logger;
        public Type SupportedStatementType => typeof(ThrowStatement);

        public ThrowStatementHandler(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>Executes the THROW statement, evaluating the message and raising an ExecutionException.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ThrowStatement)statement;

            _logger.Debug("Executing THROW");

            const string defaultMessage = "An explicit error was thrown.";

            if (stmt.Message == null && stmt.ErrorNumber == null && stmt.State == null)
            {
                // Bare THROW; (re-throw)
                if (context.LastError != null)
                {
                    throw new ExecutionException(
                        context.LastError.Message,
                        null,
                        context.LastError.Line,
                        0,
                        context.LastError.Number,
                        context.LastError.Severity,
                        context.LastError.State);
                }
                throw new ExecutionException(defaultMessage);
            }

            int number = stmt.ErrorNumber != null ? Convert.ToInt32(await context.EvaluateValue(stmt.ErrorNumber, new Row())) : 50000;
            string message = stmt.Message != null ? (await context.EvaluateValue(stmt.Message, new Row()))?.ToString() ?? defaultMessage : defaultMessage;
            int state = stmt.State != null ? Convert.ToInt32(await context.EvaluateValue(stmt.State, new Row())) : 1;

            throw new ExecutionException(message, null, 0, 0, number, 16, state);
        }
    }
}
