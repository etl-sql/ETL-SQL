using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Functions;

namespace ETL_SQL.Engine.Functions
{
    public static class HelpExtensions
    {
        public static void RegisterWithHelp(this IFunctionRegistry registry, string name, Func<List<object?>, IExecutionContext, object?> implementation, string help)
        {
            registry.Register(name, implementation);
            registry.RegisterHelp(name, help);
        }

        public static void RegisterWithHelp(this IFunctionRegistry registry, string name, Func<List<object?>, IExecutionContext, Task<object?>> implementation, string help)
        {
            registry.Register(name, implementation);
            registry.RegisterHelp(name, help);
        }
    }
}
