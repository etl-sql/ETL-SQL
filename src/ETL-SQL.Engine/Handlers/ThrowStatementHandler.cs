using ETL_SQL.Data;
using ETL_SQL.Core.Common.Exceptions;
using System.Threading.Tasks;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the THROW statement, explicitly failing script execution with a custom error message.
    /// </summary>
    public class ThrowStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ThrowStatement);
        /// <summary>Executes the THROW statement, evaluating the message and raising an ExecutionException.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ThrowStatement)statement;
            
            Logger.Verbose($"Executing THROW");
            object? msgObj = stmt.Message != null ? await context.EvaluateValue(stmt.Message, new Row()) : "An explicit error was thrown.";
            throw new ExecutionException(msgObj?.ToString() ?? "An explicit error was thrown.");
        }
    }
}



