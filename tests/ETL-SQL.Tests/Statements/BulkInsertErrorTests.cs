using ETL_SQL.Core;
using ETL_SQL.Engine;
using ETL_SQL.Data;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.Tests
{
    public class BulkInsertErrorTests
    {
        private readonly Evaluator _evaluator;
        private readonly ServiceProvider _serviceProvider;

        public BulkInsertErrorTests()
        {
            _serviceProvider = (ServiceProvider)DependencyInjectionSetup.BuildServiceProvider();
            _evaluator = _serviceProvider.GetRequiredService<Evaluator>();
        }

        private class FailingDataSource : IDataSource
        {
            public int SuccessfulWrites { get; private set; }
            public int FailedWrites { get; private set; }
            public List<Row> Rows { get; } = new List<Row>();
            public string Path => "FAIL_MOCK";
            public Dictionary<string, string>? Options => null;

            public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
            {
                var dt = new DataTable();
                dt.SetColumns(new[] { "ID", "Name" });
                foreach (var row in Rows) dt.AddRow(row);
                yield return dt;
                await Task.CompletedTask;
            }

            public async Task WriteBatches(IAsyncEnumerable<DataTable> batches)
            {
                await foreach (var batch in batches)
                {
                    // Fail if any row has Name = "FAIL"
                    foreach (var row in batch.Rows)
                    {
                        if (row["Name"]?.ToString() == "FAIL")
                        {
                            FailedWrites++;
                            throw new Exception("Simulated row failure");
                        }
                    }
                    Rows.AddRange(batch.Rows);
                    SuccessfulWrites++;
                }
            }

            public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)new[] { "ID", "Name" });
            public object? Snapshot() => null;
            public void Restore(object? snapshot) { }
            public IDataSource WithTable(string tableName) => this;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            public Task TruncateAsync() { Rows.Clear(); return Task.CompletedTask; }
        }

        [Fact]
        public async Task TestBulkInsertWithMaxErrors()
        {
            // 1. Setup Data
            string csvPath = System.IO.Path.GetTempFileName() + ".csv";
            File.WriteAllLines(csvPath, new[]
            {
                "ID,Name",
                "1,Alice",
                "2,FAIL", // This should fail
                "3,Bob",
                "4,FAIL", // This should fail
                "5,Charlie"
            });

            var failingDs = new FailingDataSource();
            _evaluator.Connections["ErrorTable"] = failingDs;

            try
            {
                // 2. Execute Script with MAXERRORS = 2
                // Note: The whole batch (5 rows) will likely fail initially, 
                // then it fallback to row-by-row.
                string script = $@"
                    BULK INSERT ErrorTable FROM '{csvPath.Replace("\\", "\\\\")}'
                    WITH (FIELDTERMINATOR = ',', FIRSTROW = 2, MAXERRORS = 2, BATCHSIZE = 10);
                ";

                var parser = new Parser(new Lexer(script).Tokenize());
                await _evaluator.Evaluate(parser.Parse());

                // 3. Assert
                Assert.Equal(3, failingDs.Rows.Count); // Alice, Bob, Charlie should be in
                Assert.Equal(3, failingDs.FailedWrites); // 1 batch failure + 2 individual row failures
                Assert.Equal(3, (int)_evaluator.RowsProcessed);
            }
            finally
            {
                if (File.Exists(csvPath)) File.Delete(csvPath);
            }
        }

        [Fact]
        public async Task TestBulkInsertExceedingMaxErrors()
        {
            // 1. Setup Data
            string csvPath = System.IO.Path.GetTempFileName() + ".csv";
            File.WriteAllLines(csvPath, new[]
            {
                "ID,Name",
                "1,Alice",
                "2,FAIL",
                "3,FAIL",
                "4,FAIL", // 3rd failure
                "5,Bob"
            });

            var failingDs = new FailingDataSource();
            _evaluator.Connections["ErrorTableOver"] = failingDs;

            try
            {
                // 2. Execute Script with MAXERRORS = 1 (should fail on 2nd error)
                string script = $@"
                    BULK INSERT ErrorTableOver FROM '{csvPath.Replace("\\", "\\\\")}'
                    WITH (FIELDTERMINATOR = ',', FIRSTROW = 2, MAXERRORS = 1);
                ";

                var parser = new Parser(new Lexer(script).Tokenize());
                
                var ex = await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(async () => 
                    await _evaluator.Evaluate(parser.Parse()));
                
                Assert.Contains("Max errors (1) exceeded", ex.Message);
            }
            finally
            {
                if (File.Exists(csvPath)) File.Delete(csvPath);
            }
        }
    }
}
