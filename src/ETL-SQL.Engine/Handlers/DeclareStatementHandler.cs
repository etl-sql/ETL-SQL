using ETL_SQL.Data;
using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the DECLARE statement, adding new variables to the execution context.
    /// Supports INPUT, OUTPUT, and SENSITIVE metadata.
    /// </summary>
    public class DeclareStatementHandler : IStatementHandler
    {
        private readonly ILogger _logger;
        public Type SupportedStatementType => typeof(DeclareStatement);

        public DeclareStatementHandler(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>Executes the DECLARE statement, initializing the variable and its metadata.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (DeclareStatement)statement;
            
            _logger.Debug("Declaring variable {VariableName} as {DataType}", stmt.VariableName, stmt.DataType);

            var variables = (IVariableContext)context;
            var evaluator = (IEvaluationContext)context;

            object? val = null;
            if (stmt.InitialValue != null)
            {
                val = await evaluator.EvaluateValue(stmt.InitialValue, new Row());
                val = evaluator.CastToType(val, stmt.DataType);
            }
            else if ((stmt.IsInput || stmt.IsOutput || stmt.IsSensitive) && variables.CurrentVariables.TryGetValue(stmt.VariableName, out var existing))
            {
                // Preserve value passed from RUN SCRIPT or EXECUTE
                val = existing;
            }

            if (variables.ContainsVariableInCurrentScope(stmt.VariableName))
            {
                if (variables.CurrentMetadata.TryGetValue(stmt.VariableName, out var existingMeta) && existingMeta.IsDeclared)
                {
                    throw new ExecutionException($"Variable {stmt.VariableName} has already been declared in this scope (Line {stmt.Line}, Col {stmt.Column}).");
                }
                // Allowed if it was just injected as a parameter (IsDeclared = false)
            }

            var metadata = new VariableMetadata 
            { 
                IsInput = stmt.IsInput, 
                IsOutput = stmt.IsOutput, 
                IsSensitive = stmt.IsSensitive,
                IsDeclared = true 
            };
            variables.DeclareVariable(stmt.VariableName, val, metadata);
        }
    }
}
