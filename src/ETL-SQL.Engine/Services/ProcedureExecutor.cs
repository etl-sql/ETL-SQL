using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Services;
/// <summary>
/// Encapsulates the execution of user-defined functions and stored procedures.
/// Handles scope isolation, parameter binding, and return value extraction.
/// </summary>
public sealed class ProcedureExecutor(VariableScopeManager scopeManager, IExecutionContext context)
{
    private readonly VariableScopeManager _scopeManager = scopeManager;
    private readonly IExecutionContext _context = context;

    /// <summary>
    /// Evaluates a user-defined function call by binding arguments, executing the body,
    /// and capturing the RETURN value.
    /// </summary>
    public async ValueTask<object?> EvaluateUserDefinedFunction(
        FunctionCallExpression f, System.Collections.Generic.List<object?> args, Row row)
    {
        _context.CurrentRecursiveDepth++;
        var scopePushed = false;
        try
        {
            _context.IncrementOperationCount(OperationType.EngineInternal); // Trigger check against limits

            if (!_scopeManager.TryGetFunction(f.FunctionName, out var funcStmt) || funcStmt == null)
            {
                throw new ExecutionException($"Unknown function: {f.FunctionName}. If this is a database-specific function, check that the query is being pushed down to the remote source (e.g. by avoiding local-only operations like joins with CSV files).");
            }

            var localVars = BuildParameterDictionary(funcStmt.Parameters, args.Select(v => ((string?)null, v)).ToList());
            _context.VarContext.PushScope(localVars);
            scopePushed = true;
            object? result = null;
            try
            {
                await _context.EvaluateStatement(funcStmt.Body);
            }
            catch (ReturnException ex)
            {
                result = ex.Value;
            }
            return result;
        }
        finally
        {
            if (scopePushed)
                _context.VarContext.PopScope();
            _context.CurrentRecursiveDepth--;
        }
    }

    /// <summary>
    /// Executes a stored procedure by binding arguments and running its body in an isolated scope.
    /// </summary>
    public async Task EvaluateProcedure(string name, List<(string? Name, object? Value)> args)
    {
        _context.CurrentRecursiveDepth++;
        var scopePushed = false;
        try
        {
            _context.IncrementOperationCount(OperationType.EngineInternal); // Trigger check against limits

            if (!_scopeManager.TryGetProcedure(name, out var procStmt) || procStmt == null)
            {
                throw new ExecutionException($"Procedure not found: {name}");
            }

            var localVars = BuildParameterDictionary(procStmt.Parameters, args);
            _context.VarContext.PushScope(localVars);
            scopePushed = true;
            try
            {
                await _context.EvaluateStatement(procStmt.Body);
            }
            catch (ReturnException)
            {
                // Procedures do not return values to the caller in standard SQL.
            }
        }
        finally
        {
            if (scopePushed)
                _context.VarContext.PopScope();
            _context.CurrentRecursiveDepth--;
        }
    }

    /// <summary>
    /// Builds a name-to-value dictionary from a parameter list and argument values (positional or named).
    /// </summary>
    private static Dictionary<string, object?> BuildParameterDictionary(
        IReadOnlyList<ParameterDefinition> parameters, IReadOnlyList<(string? Name, object? Value)> args)
    {
        var vars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        // Initialize with nulls or default values
        foreach (var p in parameters) vars[p.Name] = null;

        // 1. First, apply positional arguments
        int pos = 0;
        foreach (var arg in args.Where(a => a.Name == null))
        {
            if (pos < parameters.Count)
            {
                vars[parameters[pos].Name] = arg.Value;
                pos++;
            }
        }

        // 2. Then, apply named arguments (overwriting any positional ones if they clash, though usually they won't in valid SQL)
        foreach (var arg in args.Where(a => a.Name != null))
        {
            vars[arg.Name!] = arg.Value;
        }

        return vars;
    }
}

