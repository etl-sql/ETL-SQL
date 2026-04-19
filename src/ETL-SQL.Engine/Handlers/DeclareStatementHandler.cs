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

            object? val = null;
            bool hasInjectedValue = context.VarContext.CurrentVariables.TryGetValue(stmt.VariableName, out var existing) &&
                                    existing != null &&
                                    (!context.VarContext.CurrentMetadata.TryGetValue(stmt.VariableName, out var meta) || !meta.IsDeclared);

            if (hasInjectedValue)
            {
                // Prioritize value injected by CLI or host
                val = context.EvaluationContext.CastToType(existing, stmt.DataType);
            }
            else if (stmt.InitialValue != null)
            {
                val = await context.EvaluationContext.EvaluateValue(stmt.InitialValue, new Row());
                val = context.EvaluationContext.CastToType(val, stmt.DataType);
            }
            else if ((stmt.IsInput || stmt.IsOutput || stmt.IsSensitive) && context.VarContext.CurrentVariables.TryGetValue(stmt.VariableName, out existing))
            {
                // Preserve value passed from RUN SCRIPT or EXECUTE
                val = existing;
            }

            if (context.VarContext.ContainsVariableInCurrentScope(stmt.VariableName))
            {
                if (context.VarContext.CurrentMetadata.TryGetValue(stmt.VariableName, out var existingMeta) && existingMeta.IsDeclared)
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
                IsDeclared = true,
                DataType = stmt.DataType
            };
            context.VarContext.DeclareVariable(stmt.VariableName, val, metadata);
        }
    }
}
