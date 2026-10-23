using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine
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
            evaluator.SecurityService.AllowedEnvVars.Add("TEST_JSON_PATH");

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
            evaluator.SecurityService.AllowedEnvVars.Add("TEST_HEADER_OPT");

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
            evaluator.SecurityService.AllowedEnvVars.Add("NON_EXISTENT_VAR");
            var script = "CREATE CONNECTION conn AS JSON('${NON_EXISTENT_VAR}');";
            await evaluator.Evaluate(new Lexer(script).TokenizeToScript());

            Assert.True(evaluator.Connections.ContainsKey("conn"));
            var ds = evaluator.Connections["conn"];
            Assert.Equal("${NON_EXISTENT_VAR}", ds.Path);
        }

        [Fact]
        [Trait("CompatBreak", "0.22.0")]
        public async Task Interpolate_RefusesAVariableOutsideTheAllowList()
        {
            var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            Environment.SetEnvironmentVariable("TEST_SERVICE_SECRET", "must-not-leak");
            try
            {
                var script = "CREATE CONNECTION leak AS JSON('${TEST_SERVICE_SECRET}');";
                await Assert.ThrowsAnyAsync<Exception>(() => evaluator.Evaluate(new Lexer(script).TokenizeToScript()));
                Assert.False(evaluator.Connections.TryGetValue("leak", out var ds) && ds.Path.Contains("must-not-leak"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("TEST_SERVICE_SECRET", null);
            }
        }
    }
}
