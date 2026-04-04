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

        public async Task<object?> ExecuteAsync(string name, List<object?> args, IExecutionContext context)
        {
            if (_functions.TryGetValue(name, out var func))
            {
                return await func(args, context);
            }
            return null;
        }

        public bool IsRegistered(string name) => _functions.ContainsKey(name);
    }
}
