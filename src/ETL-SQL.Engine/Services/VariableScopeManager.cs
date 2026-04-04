using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Services
{
    /// <summary>
    /// Manages variable scopes, procedure registries, and function registries for the ETL-SQL engine.
    /// Handles scope pushing/popping and identifier resolution.
    /// </summary>
    public class VariableScopeManager
    {
        private readonly Dictionary<string, object?> _variables = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, VariableMetadata> _variableMetadata = new(StringComparer.OrdinalIgnoreCase);
        private readonly Stack<Dictionary<string, object?>> _scopeStack = new();
        private readonly Stack<Dictionary<string, VariableMetadata>> _metadataStack = new();
        
        private readonly Dictionary<string, CreateProcedureStatement> _procedures = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CreateFunctionStatement> _functions = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Gets the current set of variables in the active scope.</summary>
        public IDictionary<string, object?> CurrentVariables => _scopeStack.Count > 0 ? _scopeStack.Peek() : _variables;

        /// <summary>Gets the current set of variable metadata in the active scope.</summary>
        public IDictionary<string, VariableMetadata> CurrentMetadata => _metadataStack.Count > 0 ? _metadataStack.Peek() : _variableMetadata;

        /// <summary>Gets the global (session-level) variable dictionary.</summary>
        public IDictionary<string, object?> GlobalVariables => _variables;

        /// <summary>Pushes a new variable scope onto the stack.</summary>
        public void PushScope(Dictionary<string, object?> vars, Dictionary<string, VariableMetadata>? metadata = null)
        {
            _scopeStack.Push(vars);
            _metadataStack.Push(metadata ?? new Dictionary<string, VariableMetadata>(StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>Pops the current variable scope from the stack.</summary>
        public void PopScope()
        {
            if (_scopeStack.Count > 0)
            {
                _scopeStack.Pop();
                _metadataStack.Pop();
            }
        }

        /// <summary>Checks if a variable exists in any accessible scope.</summary>
        public bool ContainsVariable(string name)
        {
            if (_variables.ContainsKey(name)) return true;
            foreach (var scope in _scopeStack)
            {
                if (scope.ContainsKey(name)) return true;
            }
            return false;
        }

        /// <summary>Retrieves a variable value from the current scope, falling back to global scope.</summary>
        public object? GetVariable(string name)
        {
            // Search from top of stack down (local shadowing)
            foreach (var scope in _scopeStack)
            {
                if (scope.TryGetValue(name, out var val)) return val;
            }
            // Fall back to global session variables
            if (_variables.TryGetValue(name, out var gval)) return gval;
            return null;
        }

        /// <summary>Sets a variable value. Updates in the scope where it was first defined. Throws if not found.</summary>
        public void SetVariable(string name, object? value)
        {
            // Try to find existing definition in stack to update
            foreach (var scope in _scopeStack)
            {
                if (scope.ContainsKey(name))
                {
                    scope[name] = value;
                    return;
                }
            }

            // Try global
            if (_variables.ContainsKey(name))
            {
                _variables[name] = value;
                return;
            }

            throw new KeyNotFoundException($"Variable {name} must be declared before it can be assigned.");
        }

        /// <summary>Declares a new variable in the current local scope.</summary>
        public void DeclareVariable(string name, object? value, VariableMetadata? metadata = null)
        {
            CurrentVariables[name] = value;
            CurrentMetadata[name] = metadata ?? new VariableMetadata { IsDeclared = true };
        }

        /// <summary>Filters and returns variables from the current scope that match a predicate based on their metadata.</summary>
        public Dictionary<string, object?> GetVariablesWithMetadata(Func<VariableMetadata, bool> predicate)
        {
            var results = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var currentVars = CurrentVariables;
            var currentMeta = CurrentMetadata;

            foreach (var kvp in currentMeta)
            {
                if (predicate(kvp.Value))
                {
                    if (currentVars.TryGetValue(kvp.Key, out var val))
                    {
                        results[kvp.Key] = val;
                    }
                }
            }
            return results;
        }

        /// <summary>Resolves an identifier to a value, checking row columns first, then variables.</summary>
        public object? ResolveIdentifier(string name, Row? row)
        {
            if (row != null && row.Columns.TryGetValue(name, out var val)) return val;
            return GetVariable(name);
        }

        /// <summary>Registers a stored procedure.</summary>
        public void SetProcedure(string name, CreateProcedureStatement stmt) => _procedures[name] = stmt;

        /// <summary>Removes a stored procedure.</summary>
        public bool RemoveProcedure(string name) => _procedures.Remove(name);

        /// <summary>Attempts to retrieve a stored procedure by name.</summary>
        public bool TryGetProcedure(string name, out CreateProcedureStatement? stmt) => _procedures.TryGetValue(name, out stmt);

        /// <summary>Registers a user-defined function.</summary>
        public void SetFunction(string name, CreateFunctionStatement stmt) => _functions[name] = stmt;

        /// <summary>Removes a user-defined function.</summary>
        public bool RemoveFunction(string name) => _functions.Remove(name);

        /// <summary>Attempts to retrieve a user-defined function by name.</summary>
        public bool TryGetFunction(string name, out CreateFunctionStatement? stmt) => _functions.TryGetValue(name, out stmt);

        /// <summary>Creates a snapshot for parallel execution. Child gains copies of all current scopes.</summary>
        public VariableScopeManager Fork()
        {
            var fork = new VariableScopeManager();
            // Shallow copy global variables
            foreach (var kvp in _variables) fork._variables[kvp.Key] = kvp.Value;
            foreach (var kvp in _variableMetadata) fork._variableMetadata[kvp.Key] = kvp.Value;
            
            // Shallow copy procedure/function registries
            foreach (var kvp in _procedures) fork._procedures[kvp.Key] = kvp.Value;
            foreach (var kvp in _functions) fork._functions[kvp.Key] = kvp.Value;

            // Reconstruct the scope stack as copies
            var scopes = _scopeStack.ToArray();
            var metas = _metadataStack.ToArray();
            Array.Reverse(scopes);
            Array.Reverse(metas);
            for (int i = 0; i < scopes.Length; i++)
            {
                fork.PushScope(new Dictionary<string, object?>(scopes[i], StringComparer.OrdinalIgnoreCase), 
                              new Dictionary<string, VariableMetadata>(metas[i], StringComparer.OrdinalIgnoreCase));
            }
            return fork;
        }

        /// <summary>Merges changes from a forked scope back into this one.</summary>
        public void Merge(VariableScopeManager spawned)
        {
            // Sync ONLY the outermost scope or globals that changed?
            // For now, let's just sync globals as it's common for parallel results
            foreach (var kvp in spawned._variables) _variables[kvp.Key] = kvp.Value;
        }
    }
}
