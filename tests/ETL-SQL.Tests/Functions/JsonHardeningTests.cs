using Xunit;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine.Functions;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests.Functions.Functions
{
    public class JsonHardeningTests
    {
        private readonly IExecutionContext _ctx;

        public JsonHardeningTests()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            _ctx = services.GetRequiredService<Evaluator>();
        }

        [Fact]
        public void TestIsJson_InvalidInput_ReturnsZero()
        {
            // ISJSON should return 0 for invalid JSON according to established rules
            var args = new List<object?> { "{ invalid json }" };
            var result = InvokeScalar("ISJSON", args);
            Assert.Equal(0m, result);
        }

        [Fact]
        public void TestJsonValue_InvalidInput_ReturnsNull()
        {
            var args = new List<object?> { "{ invalid json }", "$.name" };
            var result = InvokeScalar("JSON_VALUE", args);
            Assert.Null(result);
        }

        [Fact]
        public void TestJsonQuery_InvalidInput_ReturnsNull()
        {
            var args = new List<object?> { "{ invalid json }", "$.items" };
            var result = InvokeScalar("JSON_QUERY", args);
            Assert.Null(result);
        }

        [Fact]
        public async Task TestJsonTable_InvalidInput_ReturnsEmptyTable()
        {
            var args = new List<object?> { "{ invalid json }", "$.items" };
            var result = await InvokeTable("JSON_TABLE", args);
            Assert.Empty(result.Rows);
        }

        [Fact]
        public async Task TestOpenJson_InvalidInput_ReturnsEmptyTable()
        {
            var args = new List<object?> { "{ invalid json }" };
            var result = await InvokeTable("OPENJSON", args);
            Assert.Empty(result.Rows);
        }

        private object? InvokeScalar(string name, List<object?> args)
        {
            var registry = new FunctionRegistry();
            JsonFunctions.Register(registry);
            return registry.ExecuteAsync(name, args, _ctx).GetAwaiter().GetResult();
        }

        private async Task<DataTable> InvokeTable(string name, List<object?> args)
        {
            var registry = new FunctionRegistry();
            JsonFunctions.Register(registry);
            var result = await registry.ExecuteAsync(name, args, _ctx);
            return (DataTable)(result ?? new DataTable());
        }
    }
}
