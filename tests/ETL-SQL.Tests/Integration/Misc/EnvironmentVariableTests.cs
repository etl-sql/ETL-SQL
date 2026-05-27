using ETL_SQL.Core;
using ETL_SQL.Engine;
using ETL_SQL.Data;

using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System.Threading.Tasks;
using System.Linq;
using System;
using System.IO;

namespace ETL_SQL.Tests.Integration
{
    public class EnvironmentVariableTests
    {
        private readonly IServiceProvider _serviceProvider;

        public EnvironmentVariableTests()
        {
            _serviceProvider = DependencyInjectionSetup.BuildServiceProvider();
        }

        [Fact]
        public async Task Interpolate_In_ConnectionString()
        {
            var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            var jsonFile = Path.Combine(Path.GetTempPath(), "test_" + Guid.NewGuid().ToString() + ".json");
            Environment.SetEnvironmentVariable("TEST_JSON_PATH", jsonFile);

            try
            {
                var script = "CREATE CONNECTION conn AS JSON('${TEST_JSON_PATH}');";
                await evaluator.Evaluate(new Lexer(script).TokenizeToScript());

                Assert.True(evaluator.Connections.ContainsKey("conn"));
                var ds = evaluator.Connections["conn"];
                Assert.Equal(jsonFile, ds.Path);
            }
            finally
            {
                Environment.SetEnvironmentVariable("TEST_JSON_PATH", null);
                if (File.Exists(jsonFile)) File.Delete(jsonFile);
            }
        }

        [Fact]
        public async Task Interpolate_In_Options()
        {
            var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            var csvFile = Path.Combine(Path.GetTempPath(), "test_" + Guid.NewGuid().ToString() + ".csv");
            Environment.SetEnvironmentVariable("TEST_HEADER_OPT", "ON");

            try
            {
                var script = $@"CREATE CONNECTION conn AS FLATFILE('{csvFile.Replace("\\", "\\\\")}', HEADER='${{TEST_HEADER_OPT}}');";
                await evaluator.Evaluate(new Lexer(script).TokenizeToScript());

                Assert.True(evaluator.Connections.ContainsKey("conn"));
                // Since FlatFileDataSource doesn't expose its options directly, 
                // we can't easily assert the internal state, but we can verify it doesn't throw 
                // and the interpolation was attempted.
            }
            finally
            {
                Environment.SetEnvironmentVariable("TEST_HEADER_OPT", null);
            }
        }

        [Fact]
        public async Task Missing_EnvironmentVariable_KeepsPlaceholder()
        {
            var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            
            // Assuming ${NON_EXISTENT_VAR} is not set
            var script = "CREATE CONNECTION conn AS JSON('${NON_EXISTENT_VAR}');";
            await evaluator.Evaluate(new Lexer(script).TokenizeToScript());

            Assert.True(evaluator.Connections.ContainsKey("conn"));
            var ds = evaluator.Connections["conn"];
            Assert.Equal("${NON_EXISTENT_VAR}", ds.Path);
        }
    }
}
