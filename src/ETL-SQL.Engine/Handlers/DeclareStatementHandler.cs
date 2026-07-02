using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Engine.Services;

namespace ETL_SQL.Engine.Handlers;
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

        // System (@@) variables are reserved and read-only; a script cannot declare one and thereby
        // shadow an identity variable used for row-level security. See Docs/Design/RowLevelSecurity.md.
        if (SystemVariableProvider.IsSystemVariable(stmt.VariableName))
            throw new ExecutionException($"System variable {stmt.VariableName} is reserved and cannot be declared.");

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
            val = await context.EvaluationContext.EvaluateValue(stmt.InitialValue, new Row(), !stmt.IsSensitive);
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
                _logger.Debug("Variable {VariableName} already exists; overwriting as requested.", stmt.VariableName);
            }
        }

        var metadata = new VariableMetadata
        {
            IsInput = stmt.IsInput,
            IsOutput = stmt.IsOutput,
            IsRequired = stmt.IsRequired,
            IsSensitive = stmt.IsSensitive,
            IsSecret = stmt.IsSecret,
            IsDeclared = true,
            DataType = stmt.DataType
        };
        context.VarContext.DeclareVariable(stmt.VariableName, val, metadata);
    }
}


