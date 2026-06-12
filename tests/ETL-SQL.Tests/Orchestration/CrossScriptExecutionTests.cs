using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    public class CrossScriptExecutionTests : IDisposable
    {
        private readonly string _subScriptPath = "sub_test.etlsql";

        public CrossScriptExecutionTests()
        {
            if (File.Exists(_subScriptPath)) File.Delete(_subScriptPath);
        }

        public void Dispose()
        {
            if (File.Exists(_subScriptPath)) File.Delete(_subScriptPath);
        }

        [Fact]
        public async Task TestExecuteScriptWithInputOutput()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = services.GetRequiredService<Evaluator>();

            // Create a sub-script
            string subScriptContent = @"
DECLARE @p1 INT INPUT;
DECLARE @p2 INT INPUT;
DECLARE @p3 INT OUTPUT = 0;
SET @p3 = @p1 + @p2;
";
            await File.WriteAllTextAsync(_subScriptPath, subScriptContent);

            // Parent script
            string parentScript = @"
DECLARE @p1 INT = 10;
DECLARE @p2 INT = 20;
DECLARE @p3 INT = 0;
EXECUTE 'sub_test.etlsql' @p1 INPUT, @p2 INPUT, @p3 OUTPUT;
";
            var tokens = new Lexer(parentScript).Tokenize();
            var script = new Parser(tokens).Parse();

            await evaluator.Evaluate(script);

            Assert.Equal(30, Convert.ToInt32(evaluator.Variables["@p3"]));
        }

        [Fact]
        public async Task TestParallelExecution()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = services.GetRequiredService<Evaluator>();

            string script = @"
CREATE TABLE #ParallelTest (Val INT);
PARALLEL
BEGIN
    INSERT INTO #ParallelTest (Val) VALUES (1);
    INSERT INTO #ParallelTest (Val) VALUES (2);
    INSERT INTO #ParallelTest (Val) VALUES (3);
    INSERT INTO #ParallelTest (Val) VALUES (4);
    INSERT INTO #ParallelTest (Val) VALUES (5);
END
";
            var tokens = new Lexer(script).Tokenize();
            var parsedScript = new Parser(tokens).Parse();

            await evaluator.Evaluate(parsedScript);

            var result = evaluator.Connections["#ParallelTest"] as InMemoryDataSource;
            Assert.NotNull(result);

            // Collect all rows from batches
            var rows = new List<Row>();
            await foreach (var batch in result.ReadBatches())
            {
                rows.AddRange(batch.Rows);
            }

            Assert.Equal(5, rows.Count);
        }
    }
}
