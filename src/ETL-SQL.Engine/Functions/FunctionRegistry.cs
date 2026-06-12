using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core.Functions;

namespace ETL_SQL.Engine.Functions
{
    /// <summary>
    /// Stores and manages a registry of executable functions available within the engine.
    /// </summary>
    public class FunctionRegistry : IFunctionRegistry
    {
        private readonly Dictionary<string, string> _helpTexts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Func<List<object?>, IExecutionContext, Task<object?>>> _functions = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Registers a new asynchronous function implementation.</summary>
        public void Register(string name, Func<List<object?>, IExecutionContext, Task<object?>> implementation)
        {
            _functions[name] = implementation;
        }

        public void Register(string name, Func<List<object?>, IExecutionContext, object?> implementation)
        {
            _functions[name] = (args, ctx) => Task.FromResult(implementation(args, ctx));
        }

        public void RegisterHelp(string name, string helpText)
        {
            _helpTexts[name] = helpText;
        }

        public string? GetHelp(string name)
        {
            return _helpTexts.TryGetValue(name, out var text) ? text : null;
        }

        public async Task<object?> ExecuteAsync(string name, List<object?> args, IExecutionContext context)
        {
            if (_functions.TryGetValue(name, out var func))
            {
                return await func(args, context);
            }
            return null;
        }

        public IEnumerable<string> GetRegisteredNames() => _functions.Keys;

        public bool IsRegistered(string name) => _functions.ContainsKey(name);
    }
}
