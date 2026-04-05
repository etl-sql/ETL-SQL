using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Functions
{
    public interface IFunctionRegistry
    {
        void Register(string name, Func<List<object?>, IExecutionContext, Task<object?>> implementation);
        void Register(string name, Func<List<object?>, IExecutionContext, object?> implementation);
        Task<object?> ExecuteAsync(string name, List<object?> args, IExecutionContext context);
        void RegisterHelp(string name, string helpText);
        string? GetHelp(string name);
        IEnumerable<string> GetRegisteredNames();
        bool IsRegistered(string name);
    }
}
