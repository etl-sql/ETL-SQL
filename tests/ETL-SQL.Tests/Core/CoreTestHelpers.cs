using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Data;
using Xunit;

namespace ETL_SQL.Tests.Core
{
    public static class TestHelpers
    {
        public static Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens, sql);
            return parser.Parse();
        }

        public static async Task Execute(Evaluator eval, string sql)
        {
            await eval.Evaluate(Parse(sql));
        }

        public static void AssertRowsMatch(DataTable result, params object[][] expected)
        {
            Assert.Equal(expected.Length, result.Rows.Count);
            for (int i = 0; i < expected.Length; i++)
            {
                var row = result.Rows[i];
                var expectedRow = expected[i];
                Assert.Equal(expectedRow.Length, result.ColumnNames.Count);
                for (int j = 0; j < expectedRow.Length; j++)
                {
                    var actualVal = row[result.ColumnNames[j]];
                    var expectedVal = expectedRow[j];
                    
                    // Simple numeric normalization for testing (everything to decimal if numeric)
                    if (actualVal is decimal d1 && expectedVal is int i1) Assert.Equal(d1, (decimal)i1);
                    else if (actualVal is decimal d2 && expectedVal is double db1) Assert.Equal(d2, (decimal)db1);
                    else Assert.Equal(expectedVal, actualVal);
                }
            }
        }
    }

    public class MockDatabaseSource : IDatabaseSource
    {
        public List<string> ExecutedSql { get; } = new();
        public List<DataTable> SeededResults { get; } = new();
        public string Dialect { get; set; } = "MSSQL";
        public bool SupportsSqlPushdown => true;
        public string ConnectionString => "mock://local";
        public string Path => "mock://local";
        public Dictionary<string, string>? Options => null;
        public string ConnectorType => "MOCK";

        public IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null)
        {
            ExecutedSql.Add(sql);
            if (SeededResults.Any())
            {
                var copy = SeededResults.ToList();
                SeededResults.Clear(); // Return each once per mock setup
                return copy.ToAsyncEnumerable();
            }
            return new[] { new DataTable() }.ToAsyncEnumerable();
        }

        public Task<string> GetVersionAsync() => Task.FromResult("Mock 1.0");
        public HashSet<string> GetSupportedFunctions() => new();
        public Task<IEnumerable<string>> GetTablesAsync() => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetViewsAsync() => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(string tableName) => Task.FromResult(Enumerable.Empty<string>());
        
        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => Enumerable.Empty<DataTable>().ToAsyncEnumerable();
        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) 
        {
            ExecutedSql.Add("INSERT INTO TargetTable (BATCH TRANSFER)");
            await foreach (var batch in batches) { }
        }
        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult(Enumerable.Empty<string>());
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => this;

        public async ValueTask DisposeAsync() => await Task.CompletedTask;
    }
}
