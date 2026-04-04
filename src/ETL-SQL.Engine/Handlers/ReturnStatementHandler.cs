using ETL_SQL.Data;
using System.Threading.Tasks;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the RETURN statement, exiting from a script, procedure, or function with an optional value.
    /// </summary>
    public class ReturnStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ReturnStatement);
        /// <summary>Evaluates the return value and throws a ReturnException to signal script exit.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ReturnStatement)statement;
            
            Logger.Verbose($"Executing RETURN");
            var val = stmt.ReturnValue != null ? await context.EvaluateValue(stmt.ReturnValue, new Row()) : null;
            throw new ReturnException(val);
        }
    }
}



