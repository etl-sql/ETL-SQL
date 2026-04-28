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
            
            if (stmt.Target is VariableExpression vExpr)
            {
                var varName = vExpr.Name;
                _logger.Debug("Setting variable {VariableName}", varName);

                if (!context.VarContext.ContainsVariable(varName))
                    throw new ExecutionException($"Variable {varName} must be declared before it can be assigned.");

                context.VarContext.VariableMetadata.TryGetValue(varName, out var targetMeta);
                bool decrypt = !(targetMeta?.IsSensitive ?? false);
                var val = await context.EvaluationContext.EvaluateValue(stmt.Value, new Row(), decrypt);
                context.VarContext.SetVariable(varName, val);
            }
            else if (stmt.Target is MemberAccessExpression ma)
            {
                if (ma.Expression is not VariableExpression baseVarExpr)
                    throw new ExecutionException("Only variable properties can be assigned in SET statements.");

                var baseVarName = baseVarExpr.Name;
                _logger.Debug("Setting property {MemberName} on variable {VariableName}", ma.MemberName, baseVarName);

                if (!context.VarContext.ContainsVariable(baseVarName))
                    throw new ExecutionException($"Variable {baseVarName} must be declared before it can be assigned.");

                var baseVal = context.VarContext.GetVariable(baseVarName);
                var newVal = await context.EvaluationContext.EvaluateValue(stmt.Value, new Row());

                if (baseVal is MinMaxValue mm)
                {
                    if (ma.MemberName.Equals("MIN", StringComparison.OrdinalIgnoreCase))
                        context.VarContext.SetVariable(baseVarName, mm with { Min = newVal });
                    else if (ma.MemberName.Equals("MAX", StringComparison.OrdinalIgnoreCase))
                        context.VarContext.SetVariable(baseVarName, mm with { Max = newVal });
                    else
                        throw new ExecutionException($"Property '{ma.MemberName}' is not valid for MINMAX type.");
                }
                else
                {
                    throw new ExecutionException($"Variable '{baseVarName}' of type {baseVal?.GetType()?.Name ?? "NULL"} does not support property assignment.");
                }
            }
            else
            {
                throw new ExecutionException("Invalid assignment target in SET statement.");
            }
        }
    }
}


