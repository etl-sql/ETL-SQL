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

            var localVars = BuildParameterDictionary(funcStmt.Parameters, args.Select(v => ((string?)null, v, (string?)null)).ToList());
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
    public async Task EvaluateProcedure(string name, List<(string? Name, object? Value, string? OutputVariable)> args)
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
            var outputs = OutputBindings(procStmt.Parameters, args);
            _context.VarContext.PushScope(localVars);
            scopePushed = true;
            try
            {
                await _context.EvaluateStatement(procStmt.Body);
            }
            catch (ReturnException)
            {
                // RETURN ends the procedure; OUTPUT parameters are still written back below.
            }

            // The pushed scope is localVars itself, so it holds each parameter's final value.
            _context.VarContext.PopScope();
            scopePushed = false;
            foreach (var (parameter, callerVariable) in outputs)
                _context.VarContext.SetVariable(callerVariable, localVars.GetValueOrDefault(parameter));
        }
        finally
        {
            if (scopePushed)
                _context.VarContext.PopScope();
            _context.CurrentRecursiveDepth--;
        }
    }

    /// <summary>Pairs each OUTPUT argument's procedure parameter with the caller variable it writes back to.</summary>
    private static List<(string Parameter, string CallerVariable)> OutputBindings(
        IReadOnlyList<ParameterDefinition> parameters, IReadOnlyList<(string? Name, object? Value, string? OutputVariable)> args)
    {
        var bindings = new List<(string, string)>();
        var position = 0;
        foreach (var arg in args)
        {
            string? parameter = arg.Name;
            if (parameter is null)
            {
                parameter = position < parameters.Count ? parameters[position].Name : null;
                position++;
            }
            if (arg.OutputVariable is not null && parameter is not null)
                bindings.Add((parameter, arg.OutputVariable));
        }
        return bindings;
    }

    /// <summary>
    /// Builds a name-to-value dictionary from a parameter list and argument values (positional or named).
    /// </summary>
    private static Dictionary<string, object?> BuildParameterDictionary(
        IReadOnlyList<ParameterDefinition> parameters, IReadOnlyList<(string? Name, object? Value, string? OutputVariable)> args)
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

