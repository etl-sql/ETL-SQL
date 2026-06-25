using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    public class OrchestrationTests
    {


        [Fact]
        public async Task TestRunScriptWithVariables()
        {
            string subScriptPath = "sub_test.etlsql";
            string subScript = @"
DECLARE @input int PASSWORD;
DECLARE @output int OUTPUT;
SET @output = @input * 2;
";
            await File.WriteAllTextAsync(subScriptPath, subScript);

            try
            {
                string mainScript = $@"
DECLARE @val int = 10;
DECLARE @result int;
RUN SCRIPT '{subScriptPath}' WITH (@input = @val);
SET @result = @output;
";
                var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                await evaluator.Evaluate(TestHelpers.Parse(mainScript));


                var result = evaluator.GetVariable("@result");
                Assert.Equal(20m, Convert.ToDecimal(result));
            }
            finally
            {
                if (File.Exists(subScriptPath)) File.Delete(subScriptPath);
            }
        }

        [Fact]
        public async Task TestMultipleOutputs()
        {
            string subScriptPath = "sub_multi.etlsql";
            string subScript = @"
DECLARE @a int PASSWORD, @b int PASSWORD;
DECLARE @sum int OUTPUT, @diff int OUTPUT;
SET @sum = @a + @b;
SET @diff = @a - @b;
";
            await File.WriteAllTextAsync(subScriptPath, subScript);

            try
            {
                string mainScript = $@"
RUN SCRIPT '{subScriptPath}' WITH (@a = 100, @b = 30);
";
                var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                await evaluator.Evaluate(TestHelpers.Parse(mainScript));


                var sum = evaluator.GetVariable("@sum");
                var diff = evaluator.GetVariable("@diff");

                Assert.Equal(130m, Convert.ToDecimal(sum));
                Assert.Equal(70m, Convert.ToDecimal(diff));
            }
            finally
            {
                if (File.Exists(subScriptPath)) File.Delete(subScriptPath);
            }
        }

        [Fact]
        public async Task RunScript_Reparses_When_File_Changes()
        {
            var subScriptPath = Path.Combine(Path.GetTempPath(), $"sub_cache_{Guid.NewGuid():N}.etlsql");
            var escapedPath = subScriptPath.Replace("\\", "\\\\");
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

            try
            {
                await File.WriteAllTextAsync(subScriptPath, "DECLARE @output int OUTPUT; SET @output = 1;");
                await evaluator.Evaluate(TestHelpers.Parse($"RUN SCRIPT '{escapedPath}';"));
                Assert.Equal(1m, Convert.ToDecimal(evaluator.GetVariable("@output")));

                await File.WriteAllTextAsync(subScriptPath, "DECLARE @output int OUTPUT; SET @output = 222;");
                File.SetLastWriteTimeUtc(subScriptPath, DateTime.UtcNow.AddSeconds(2));

                await evaluator.Evaluate(TestHelpers.Parse($"RUN SCRIPT '{escapedPath}';"));
                Assert.Equal(222m, Convert.ToDecimal(evaluator.GetVariable("@output")));
            }
            finally
            {
                if (File.Exists(subScriptPath)) File.Delete(subScriptPath);
            }
        }

        [Fact]
        public async Task TestRunScriptErrorPropagation()
        {
            string subScriptPath = "sub_error.etlsql";
            string subScript = "THROW 'SubScript Error';";
            await File.WriteAllTextAsync(subScriptPath, subScript);

            try
            {
                string mainScript = $"RUN SCRIPT '{subScriptPath}';";
                var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                var script = TestHelpers.Parse(mainScript);


                var ex = await Assert.ThrowsAsync<ExecutionException>(async () => await evaluator.Evaluate(script));
                Assert.Contains("SubScript Error", ex.Message);
            }
            finally
            {
                if (File.Exists(subScriptPath)) File.Delete(subScriptPath);
            }
        }

        [Fact]
        public async Task TestRunScriptFileNotFound()
        {
            string mainScript = "RUN SCRIPT 'non_existent_file.etlsql';";
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var script = TestHelpers.Parse(mainScript);

            var ex = await Assert.ThrowsAsync<ExecutionException>(async () => await evaluator.Evaluate(script));
            Assert.Contains("non_existent_file.etlsql", ex.Message);
        }

        [Fact]
        public async Task RunScriptFileNotFound_RestoresRecursiveDepth()
        {
            string mainScript = "RUN SCRIPT 'non_existent_file.etlsql';";
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

            await Assert.ThrowsAsync<ExecutionException>(async () => await evaluator.Evaluate(TestHelpers.Parse(mainScript)));

            Assert.Equal(0, evaluator.CurrentRecursiveDepth);
        }

        [Fact]
        public async Task RunScriptParseFailure_RestoresCurrentScriptPath()
        {
            var previousPath = Path.Combine(Path.GetTempPath(), "parent.etlsql");
            var subScriptPath = Path.Combine(Path.GetTempPath(), $"sub_bad_{Guid.NewGuid():N}.etlsql");
            await File.WriteAllTextAsync(subScriptPath, "RUN SCRIPT 'missing.etlsql' WITH (@x = 1");

            try
            {
                var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                evaluator.CurrentScriptPath = previousPath;

                await Assert.ThrowsAnyAsync<Exception>(async () =>
                    await evaluator.Evaluate(TestHelpers.Parse($"RUN SCRIPT '{subScriptPath.Replace("\\", "\\\\")}';")));

                Assert.Equal(previousPath, evaluator.CurrentScriptPath);
                Assert.Equal(0, evaluator.CurrentRecursiveDepth);
            }
            finally
            {
                if (File.Exists(subScriptPath)) File.Delete(subScriptPath);
            }
        }
    }
}
