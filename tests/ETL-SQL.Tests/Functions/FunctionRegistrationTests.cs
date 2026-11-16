using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Functions;
using ETL_SQL.Engine.Functions;
using Xunit;

namespace ETL_SQL.Tests.Functions;

/// <summary>
/// FILE_EXISTS and DIRECTORY_EXISTS were registered by both StandardFunctions and FileFunctions with
/// different results (TRUE/FALSE and FALSE for NULL, versus 1/0 and NULL for NULL). The last
/// registration wins and hosts register in different orders, so the CLI and the Orchestrator gave
/// different answers for the same script (ROADMAP FN10).
/// </summary>
public sealed class FunctionRegistrationTests
{
    private sealed class RecordingRegistry : IFunctionRegistry
    {
        private readonly FunctionRegistry _inner = new();
        public List<string> Names { get; } = new();

        public void Register(string name, Func<List<object?>, IExecutionContext, Task<object?>> implementation)
        {
            Names.Add(name);
            _inner.Register(name, implementation);
        }

        public void Register(string name, Func<List<object?>, IExecutionContext, object?> implementation)
        {
            Names.Add(name);
            _inner.Register(name, implementation);
        }

        public Task<object?> ExecuteAsync(string name, List<object?> args, IExecutionContext context) => _inner.ExecuteAsync(name, args, context);
        public void RegisterHelp(string name, string helpText) => _inner.RegisterHelp(name, helpText);
        public string? GetHelp(string name) => _inner.GetHelp(name);
        public IEnumerable<string> GetRegisteredNames() => _inner.GetRegisteredNames();
        public bool IsRegistered(string name) => _inner.IsRegistered(name);
    }

    [Fact]
    public void StandardAndFileFunctions_RegisterEachNameOnce()
    {
        var registry = new RecordingRegistry();
        StandardFunctions.Register(registry);
        FileFunctions.Register(registry);

        var duplicates = registry.Names
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(duplicates.Count == 0, "Registered more than once: " + string.Join(", ", duplicates));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistsFunctions_DoNotDependOnRegistrationOrder(bool standardFirst)
    {
        var registry = new FunctionRegistry();
        if (standardFirst) { StandardFunctions.Register(registry); FileFunctions.Register(registry); }
        else { FileFunctions.Register(registry); StandardFunctions.Register(registry); }

        Assert.Null(await registry.ExecuteAsync("FILE_EXISTS", new List<object?> { null }, null!));
        Assert.Null(await registry.ExecuteAsync("DIRECTORY_EXISTS", new List<object?> { null }, null!));
    }
}
