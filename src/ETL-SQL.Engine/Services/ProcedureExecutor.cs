using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Engine.Services
{
    /// <summary>
    /// Encapsulates the execution of user-defined functions and stored procedures.
    /// Handles scope isolation, parameter binding, and return value extraction.
    /// </summary>
    internal sealed class ProcedureExecutor(VariableScopeManager scopeManager, IExecutionContext context)
    {
        private readonly VariableScopeManager _scopeManager = scopeManager;
        private readonly IExecutionContext _context = context;

        /// <summary>
        /// Evaluates a user-defined function call by binding arguments, executing the body,
        /// and capturing the RETURN value.
        /// </summary>
        public async Task<object?> EvaluateUserDefinedFunction(
            FunctionCallExpression f, System.Collections.Generic.List<object?> args, Row row)
        {
             _context.CurrentRecursiveDepth++;
            _context.IncrementOperationCount(); // Trigger check against limits

            if (!_scopeManager.TryGetFunction(f.FunctionName, out var funcStmt) || funcStmt == null)
            {
                _context.CurrentRecursiveDepth--;
                return args.Count > 0 ? args[0] : null;
            }

            var localVars = BuildParameterDictionary(funcStmt.Parameters, args);
            _context.PushScope(localVars);
            object? result = null;
            try
            {
                await _context.EvaluateStatement(funcStmt.Body);
            }
            catch (ReturnException ex)
            {
                result = ex.Value;
            }
            finally
            {
                _context.PopScope();
                _context.CurrentRecursiveDepth--;
            }
            return result;
        }

        /// <summary>
        /// Executes a stored procedure by binding arguments and running its body in an isolated scope.
        /// </summary>
        public async Task EvaluateProcedure(string name, List<object?> args)
        {
            _context.CurrentRecursiveDepth++;
            _context.IncrementOperationCount(); // Trigger check against limits

            if (!_scopeManager.TryGetProcedure(name, out var procStmt) || procStmt == null)
            {
                _context.CurrentRecursiveDepth--;
                throw new ExecutionException($"Procedure not found: {name}");
            }

            var localVars = BuildParameterDictionary(procStmt.Parameters, args);
            _context.PushScope(localVars);
            try
            {
                await _context.EvaluateStatement(procStmt.Body);
            }
            catch (ReturnException)
            {
                // Procedures do not return values to the caller in standard SQL.
            }
            finally
            {
                _context.PopScope();
                _context.CurrentRecursiveDepth--;
            }
        }

        /// <summary>
        /// Builds a name-to-value dictionary from a parameter list and positional argument values.
        /// </summary>
        private static Dictionary<string, object?> BuildParameterDictionary(
            IReadOnlyList<ParameterDefinition> parameters, IReadOnlyList<object?> args)
        {
            var vars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < parameters.Count; i++)
                vars[parameters[i].Name] = i < args.Count ? args[i] : null;
            return vars;
        }
    }
}
