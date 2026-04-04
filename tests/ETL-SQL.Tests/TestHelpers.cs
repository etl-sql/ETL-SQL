using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Data;

namespace ETL_SQL.Tests
{
    public static class TestHelpers
    {
        public static Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            return parser.Parse();
        }

        public static async Task Execute(Evaluator eval, string sql)
        {
            await eval.Evaluate(Parse(sql));
        }
    }

    public class MockDatabaseSource : IDatabaseSource
    {
        public List<string> ExecutedSql { get; } = new();
        public string Dialect { get; set; } = "MSSQL";
        public string Path => "mock://local";

        public IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null)
        {
            ExecutedSql.Add(sql);
            return new[] { new DataTable() }.ToAsyncEnumerable();
        }

        public Task<string> GetVersionAsync() => Task.FromResult("Mock 1.0");
        public HashSet<string> GetSupportedFunctions() => new();
        public Task<IEnumerable<string>> GetTablesAsync() => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetViewsAsync() => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(string tableName) => Task.FromResult(Enumerable.Empty<string>());
        
        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => Enumerable.Empty<DataTable>().ToAsyncEnumerable();
        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches) 
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
