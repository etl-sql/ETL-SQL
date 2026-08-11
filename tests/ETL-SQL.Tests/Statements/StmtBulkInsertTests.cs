using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    public class BulkInsertTests
    {
        private readonly Evaluator _evaluator;
        private readonly ServiceProvider _serviceProvider;

        public BulkInsertTests()
        {
            _serviceProvider = (ServiceProvider)DependencyInjectionSetup.BuildServiceProvider();
            _evaluator = _serviceProvider.GetRequiredService<Evaluator>();
        }

        [Fact]
        public async Task TestBulkInsertCsv()
        {
            // 1. Setup Data
            string csvPath = Path.GetTempFileName() + ".csv";
            File.WriteAllLines(csvPath, new[]
            {
                "ID,Name,Age",
                "1,Alice,30",
                "2,Bob,25",
                "3,Charlie,35"
            });

            try
            {
                // 2. Execute Script
                string script = $@"
                    CREATE TABLE #BulkTest (ID INT, Name VARCHAR(50), Age INT);
                    BULK INSERT #BulkTest FROM '{csvPath.Replace("\\", "\\\\")}'
                    WITH (FIELDTERMINATOR = ',', FIRSTROW = 2);
                    SELECT * FROM #BulkTest;
                ";

                var parser = new Parser(new Lexer(script).Tokenize());
                await _evaluator.Evaluate(parser.Parse());
                var result = _evaluator.LastResult as DataTable;

                // 3. Assert
                Assert.NotNull(result);
                Assert.Equal(3, result.Rows.Count);
                Assert.Equal("Alice", result.Rows[0]["Name"]);
                Assert.Equal(25, (int)decimal.Parse(result.Rows[1]["Age"].ToString()));
            }
            finally
            {
                if (File.Exists(csvPath)) File.Delete(csvPath);
            }
        }

        [Fact]
        public async Task TestBulkInsertCustomTerminators()
        {
            // 1. Setup Data
            string txtPath = Path.GetTempFileName() + ".txt";
            File.WriteAllText(txtPath, "1|Alice\n2|Bob\n3|Charlie");

            try
            {
                // 2. Execute Script
                string script = $@"
                    CREATE TABLE #BulkTestCustom (ID INT, Name VARCHAR(50));
                    BULK INSERT #BulkTestCustom FROM '{txtPath.Replace("\\", "\\\\")}'
                    WITH (FIELDTERMINATOR = '|', ROWTERMINATOR = '\n', FIRSTROW = 1);
                    SELECT * FROM #BulkTestCustom;
                ";

                var parser = new Parser(new Lexer(script).Tokenize());
                await _evaluator.Evaluate(parser.Parse());
                var result = _evaluator.LastResult as DataTable;

                // 3. Assert
                Assert.NotNull(result);
                Assert.Equal(3, result.Rows.Count);
                Assert.Equal("Alice", result.Rows[0]["Name"]);
                Assert.Equal("2", result.Rows[1]["ID"].ToString());
            }
            finally
            {
                if (File.Exists(txtPath)) File.Delete(txtPath);
            }
        }

        [Fact]
        public async Task TestBulkInsertWithBatchSizeAndMaxErrors()
        {
            // 1. Setup Data
            string csvPath = Path.GetTempFileName() + ".csv";
            var lines = new List<string> { "ID,Name" };
            for (int i = 1; i <= 100; i++) lines.Add($"{i},Name{i}");
            File.WriteAllLines(csvPath, lines);

            try
            {
                // 2. Execute Script
                string script = $@"
                    CREATE TABLE #BulkBatchTest (ID INT, Name VARCHAR(50));
                    BULK INSERT #BulkBatchTest FROM '{csvPath.Replace("\\", "\\\\")}'
                    WITH (BATCHSIZE = 10, FIRSTROW = 2);
                    SELECT COUNT(*) as Total FROM #BulkBatchTest;
                ";

                var parser = new Parser(new Lexer(script).Tokenize());
                await _evaluator.Evaluate(parser.Parse());
                var result = _evaluator.LastResult as DataTable;

                // 3. Assert
                Assert.NotNull(result);
                Assert.Equal(100, Convert.ToInt32(result.Rows[0]["Total"]));
            }
            finally
            {
                if (File.Exists(csvPath)) File.Delete(csvPath);
            }
        }

        [Fact]
        public async Task TestBulkLoadSynonym()
        {
            // 1. Setup Data
            string csvPath = Path.GetTempFileName() + ".csv";
            File.WriteAllLines(csvPath, new[] { "1,Alice", "2,Bob" });

            try
            {
                // 2. Execute Script (Using BULK LOAD instead of BULK INSERT)
                string script = $@"
                    CREATE TABLE #BulkLoadTest (ID INT, Name VARCHAR(50));
                    BULK LOAD #BulkLoadTest FROM '{csvPath.Replace("\\", "\\\\")}'
                    WITH (FIELDTERMINATOR = ',');
                    SELECT COUNT(*) as Total FROM #BulkLoadTest;
                ";

                var parser = new Parser(new Lexer(script).Tokenize());
                await _evaluator.Evaluate(parser.Parse());
                var result = _evaluator.LastResult as DataTable;

                // 3. Assert
                Assert.NotNull(result);
                Assert.Equal(2, Convert.ToInt32(result.Rows[0]["Total"]));
            }
            finally
            {
                if (File.Exists(csvPath)) File.Delete(csvPath);
            }
        }

        [Fact]
        public async Task TestBulkInsertMissingFile()
        {
            string script = @"
                CREATE TABLE #MissingFileTest (ID INT);
                BULK INSERT #MissingFileTest FROM 'non_existent_file_12345.csv';
            ";

            var parser = new Parser(new Lexer(script).Tokenize());

            // This previously asserted a silent success, because the flat-file reader yields no
            // batches for a path that does not exist and the test was written to whatever the code
            // did — its own comment said "or it might throw if engine enforces existence". A load
            // whose source is absent must fail: zero rows and a green run is indistinguishable from
            // a file that was legitimately empty, and the table is wrong rather than merely empty.
            var ex = await Assert.ThrowsAsync<ExecutionException>(
                () => _evaluator.Evaluate(parser.Parse()));

            Assert.Contains("not found", ex.Message, System.StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, _evaluator.Telemetry.RowsProcessed);
        }
    }
}
