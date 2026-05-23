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
    public class VariableScopeManager : IVariableContext
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, object?> _variables = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, VariableMetadata> _variableMetadata = new(StringComparer.OrdinalIgnoreCase);
        private readonly Stack<Dictionary<string, object?>> _scopeStack = new();
        private readonly Stack<Dictionary<string, VariableMetadata>> _metadataStack = new();
        
        private readonly Dictionary<string, CreateProcedureStatement> _procedures = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CreateFunctionStatement> _functions = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CreateViewStatement> _views = new(StringComparer.OrdinalIgnoreCase);
 
        public IDictionary<string, object?> Variables { get { lock(_lock) { return _variables; } } }
        public IDictionary<string, VariableMetadata> VariableMetadata { get { lock(_lock) { return _variableMetadata; } } }
 
        /// <summary>Gets the current set of variables in the active scope.</summary>
        public IDictionary<string, object?> CurrentVariables { get { lock(_lock) { return _scopeStack.Count > 0 ? _scopeStack.Peek() : _variables; } } }
 
        /// <summary>Gets the current set of variable metadata in the active scope.</summary>
        public IDictionary<string, VariableMetadata> CurrentMetadata { get { lock(_lock) { return _metadataStack.Count > 0 ? _metadataStack.Peek() : _variableMetadata; } } }
 
        [Obsolete("Use Variables")]
        public IDictionary<string, object?> GlobalVariables => Variables;
        [Obsolete("Use VariableMetadata")]
        public IDictionary<string, VariableMetadata> GlobalMetadata => VariableMetadata;

        /// <summary>Pushes a new variable scope onto the stack.</summary>
        public void PushScope(Dictionary<string, object?> vars, Dictionary<string, VariableMetadata>? metadata = null)
        {
            lock (_lock)
            {
                _scopeStack.Push(vars);
                _metadataStack.Push(metadata ?? new Dictionary<string, VariableMetadata>(StringComparer.OrdinalIgnoreCase));
            }
        }

        /// <summary>Pops the current variable scope from the stack.</summary>
        public void PopScope()
        {
            lock (_lock)
            {
                if (_scopeStack.Count > 0)
                {
                    _scopeStack.Pop();
                    _metadataStack.Pop();
                }
            }
        }

        /// <summary>Checks if a variable exists in any accessible scope.</summary>
        public bool ContainsVariable(string name)
        {
            lock (_lock)
            {
                if (_variables.ContainsKey(name)) return true;
                foreach (var scope in _scopeStack)
                {
                    if (scope.ContainsKey(name)) return true;
                }
                return false;
            }
        }

        /// <summary>Retrieves a variable value from the current scope, falling back to global scope.</summary>
        public object? GetVariable(string name)
        {
            lock (_lock)
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
        }

        /// <summary>Sets a variable value. Updates in the scope where it was first defined. Throws if not found.</summary>
        public void SetVariable(string name, object? value)
        {
            lock (_lock)
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
        }

        /// <summary>Declares a new variable in the current local scope.</summary>
        public void DeclareVariable(string name, object? value, VariableMetadata? metadata = null)
        {
            lock (_lock)
            {
                CurrentVariables[name] = value;
                CurrentMetadata[name] = metadata ?? new VariableMetadata { IsDeclared = true };
            }
        }

        /// <summary>Filters and returns variables from the current scope that match a predicate based on their metadata.</summary>
        public IDictionary<string, (object? Value, VariableMetadata Metadata)> GetVariablesWithMetadata(Func<VariableMetadata, bool>? predicate = null)
        {
            lock (_lock)
            {
                var results = new Dictionary<string, (object? Value, VariableMetadata Metadata)>(StringComparer.OrdinalIgnoreCase);
                var currentVars = CurrentVariables;
                var currentMeta = CurrentMetadata;

                foreach (var kvp in currentMeta)
                {
                    if (predicate == null || predicate(kvp.Value))
                    {
                        if (currentVars.TryGetValue(kvp.Key, out var val))
                        {
                            results[kvp.Key] = (val, kvp.Value);
                        }
                    }
                }
                return results;
            }
        }

        public bool ContainsVariableInCurrentScope(string name)
        {
            lock (_lock)
            {
                return CurrentVariables.ContainsKey(name);
            }
        }

        /// <summary>Resolves an identifier to a value, checking row columns first, then variables.</summary>
        public object? ResolveIdentifier(string name, Row? row)
        {
            if (row != null && row.Columns.TryGetValue(name, out var val)) return val;
            return GetVariable(name);
        }

        /// <summary>Registers a stored procedure.</summary>
        public void SetProcedure(string name, CreateProcedureStatement stmt) { lock(_lock) { _procedures[name] = stmt; } }

        /// <summary>Removes a stored procedure.</summary>
        public bool RemoveProcedure(string name) { lock(_lock) { return _procedures.Remove(name); } }

        /// <summary>Attempts to retrieve a stored procedure by name.</summary>
        public bool TryGetProcedure(string name, out CreateProcedureStatement? stmt) { lock(_lock) { return _procedures.TryGetValue(name, out stmt); } }

        /// <summary>Registers a user-defined function.</summary>
        public void SetFunction(string name, CreateFunctionStatement stmt) { lock(_lock) { _functions[name] = stmt; } }

        /// <summary>Removes a user-defined function.</summary>
        public bool RemoveFunction(string name) { lock(_lock) { return _functions.Remove(name); } }

        /// <summary>Attempts to retrieve a user-defined function by name.</summary>
        public bool TryGetFunction(string name, out CreateFunctionStatement? stmt) { lock(_lock) { return _functions.TryGetValue(name, out stmt); } }

        /// <summary>Registers a session-scoped query view.</summary>
        public void SetView(string name, CreateViewStatement stmt) { lock(_lock) { _views[name] = stmt; } }

        /// <summary>Removes a session-scoped query view.</summary>
        public bool RemoveView(string name) { lock(_lock) { return _views.Remove(name); } }

        /// <summary>Attempts to retrieve a session-scoped query view by name.</summary>
        public bool TryGetView(string name, out CreateViewStatement? stmt) { lock(_lock) { return _views.TryGetValue(name, out stmt); } }

        /// <summary>Returns a snapshot of all session-scoped query views.</summary>
        public IReadOnlyDictionary<string, CreateViewStatement> GetViews()
        {
            lock (_lock)
            {
                return new Dictionary<string, CreateViewStatement>(_views, StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>Creates a snapshot for parallel execution. Child gains copies of all current scopes.</summary>
        public VariableScopeManager Fork()
        {
            lock (_lock)
            {
                var fork = new VariableScopeManager();
                // Shallow copy global variables
                foreach (var kvp in _variables) fork._variables[kvp.Key] = kvp.Value;
                foreach (var kvp in _variableMetadata) fork._variableMetadata[kvp.Key] = kvp.Value;
                
                // Shallow copy procedure/function registries
                foreach (var kvp in _procedures) fork._procedures[kvp.Key] = kvp.Value;
                foreach (var kvp in _functions) fork._functions[kvp.Key] = kvp.Value;
                foreach (var kvp in _views) fork._views[kvp.Key] = kvp.Value;

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
        }

        /// <summary>Captures the global state of the variables for session persistence.</summary>
        public (Dictionary<string, object?>, Dictionary<string, VariableMetadata>) GetGlobalState()
        {
            lock (_lock)
            {
                return (new Dictionary<string, object?>(_variables, StringComparer.OrdinalIgnoreCase),
                        new Dictionary<string, VariableMetadata>(_variableMetadata, StringComparer.OrdinalIgnoreCase));
            }
        }

        /// <summary>Loads the global state of the variables from a session snapshot.</summary>
        public void LoadGlobalState(Dictionary<string, object?> vars, Dictionary<string, VariableMetadata> meta)
        {
            lock (_lock)
            {
                _variables.Clear();
                foreach (var kvp in vars) _variables[kvp.Key] = kvp.Value;
                _variableMetadata.Clear();
                foreach (var kvp in meta) _variableMetadata[kvp.Key] = kvp.Value;
            }
        }

        /// <summary>Purges values of variables flagged as SECRET to reclaim security-sensitive memory.</summary>
        public void PurgeSecretVariables()
        {
            lock (_lock)
            {
                // Purge globals
                var globalSecretKeys = _variableMetadata
                    .Where(kv => kv.Value.IsSecret)
                    .Select(kv => kv.Key)
                    .ToList();

                foreach (var key in globalSecretKeys)
                {
                    _variables[key] = null;
                }

                // Purge all stacked scopes
                var scopeList = _scopeStack.ToList();
                var metaList = _metadataStack.ToList();
                
                for(int i = 0; i < metaList.Count; i++)
                {
                    var scope = scopeList[i];
                    var meta = metaList[i];
                    var localSecrets = meta.Where(kv => kv.Value.IsSecret).Select(kv => kv.Key).ToList();
                    foreach(var key in localSecrets)
                    {
                        scope[key] = null;
                    }
                }
            }
        }

        /// <summary>Merges changes from a forked scope back into this one.</summary>
        public void Merge(VariableScopeManager spawned)
        {
            lock (_lock)
            {
                // Sync ONLY the outermost scope or globals that changed?
                // For now, let's just sync globals as it's common for parallel results
                foreach (var kvp in spawned.Variables) _variables[kvp.Key] = kvp.Value;
            }
        }
        /// <summary>Purges all variables, procedures, functions, and scopes from the context.</summary>
        public void Reset()
        {
            lock (_lock)
            {
                _variables.Clear();
                _variableMetadata.Clear();
                _scopeStack.Clear();
                _metadataStack.Clear();
                _procedures.Clear();
                _functions.Clear();
                _views.Clear();
            }
        }
    }
}
