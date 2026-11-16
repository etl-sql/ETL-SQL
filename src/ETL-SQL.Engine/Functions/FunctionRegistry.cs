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

        /// <summary>
        /// Functions that return NULL when any argument is NULL, as SQL defines them. Each used to
        /// answer for itself, and many answered with an empty string, 0, 1 or an exception. The value
        /// is the first argument checked: DATEPART and DATEDIFF take a date-part word first.
        /// </summary>
        private static readonly Dictionary<string, int> NullPropagating = new(StringComparer.OrdinalIgnoreCase)
        {
            ["REPLACE"] = 0,
            ["SUBSTRING"] = 0,
            ["SUBSTR"] = 0,
            ["LEFT"] = 0,
            ["RIGHT"] = 0,
            ["CHARINDEX"] = 0,
            ["INSTR"] = 0,
            ["STUFF"] = 0,
            ["STR"] = 0,
            ["QUOTENAME"] = 0,
            ["TRANSLATE"] = 0,
            ["PATINDEX"] = 0,
            ["STRING_ESCAPE"] = 0,
            ["LOG"] = 0,
            ["LOG10"] = 0,
            ["ATAN2"] = 0,
            ["ROUND"] = 0,
            ["DATETIMEFROMPARTS"] = 0,
            ["TIMEFROMPARTS"] = 0,
            ["EOMONTH"] = 0,
            ["DATEPART"] = 1,
            ["DATEDIFF"] = 1,
            ["HASHBYTES"] = 0,
            ["SIMILARITY"] = 0,
            ["LEVENSHTEIN"] = 0,
        };

        /// <summary>Registers a new asynchronous function implementation.</summary>
        public void Register(string name, Func<List<object?>, IExecutionContext, Task<object?>> implementation)
        {
            if (NullPropagating.TryGetValue(name, out var firstChecked))
            {
                var inner = implementation;
                implementation = (args, ctx) =>
                {
                    for (var index = firstChecked; index < args.Count; index++)
                        if (args[index] is null or DBNull) return Task.FromResult<object?>(null);
                    return inner(args, ctx);
                };
            }
            _functions[name] = implementation;
        }

        public void Register(string name, Func<List<object?>, IExecutionContext, object?> implementation)
        {
            Register(name, (args, ctx) => Task.FromResult(implementation(args, ctx)));
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
