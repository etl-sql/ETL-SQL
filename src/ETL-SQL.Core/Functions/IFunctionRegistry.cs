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
        bool IsRegistered(string name);
    }
}
