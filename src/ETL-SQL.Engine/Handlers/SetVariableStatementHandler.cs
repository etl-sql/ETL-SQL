using ETL_SQL.Data;
using ETL_SQL.Core.Common.Exceptions;
using System;
using System.Threading.Tasks;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the SET @variable statement, assigning new values to existing variables.
    /// </summary>
    public class SetVariableStatementHandler : IStatementHandler
    {
        private readonly ILogger _logger;
        public Type SupportedStatementType => typeof(SetVariableStatement);

        public SetVariableStatementHandler(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>Executes the SET statement, evaluating the expression and updating the variable value.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (SetVariableStatement)statement;
            
            _logger.Debug("Setting variable {VariableName}", stmt.VariableName);

            if (!context.VarContext.ContainsVariable(stmt.VariableName))
                throw new ExecutionException($"Variable {stmt.VariableName} must be declared before it can be assigned.");

            var val = await context.EvaluationContext.EvaluateValue(stmt.Value, new Row());
            context.VarContext.SetVariable(stmt.VariableName, val);
        }
    }
}
