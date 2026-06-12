using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Operations
{
    public class OpForeachSafetyTests
    {
        private static Evaluator NewEvaluator() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        [Fact]
        public async Task Foreach_UpdateInBody_TriggersSafePagedPath()
        {
            var e = NewEvaluator();
            var mockDb = new MockDatabaseSource("REMOTE_DB");
            e.Connections["remote"] = mockDb;
            e.ForeachPageSize = 10;

            // Loop body contains an UPDATE to the same table -> Unsafe for streaming
            var script = @"
FOREACH @row IN (SELECT Id FROM remote.Users ORDER BY Id)
BEGIN
    UPDATE remote.Users SET Name = 'Modified' WHERE Id = @row.Id;
END";

            var tokens = new Lexer(script).Tokenize();
            var program = new Parser(tokens).Parse();
            await e.Evaluate(program);

            // Verify that we received paged queries (using OFFSET/FETCH)
            Assert.Contains(mockDb.CapturedSql, s => s.Contains("OFFSET"));
            Assert.Contains(mockDb.CapturedSql, s => s.Contains("UPDATE"));
        }

        [Fact]
        public async Task Foreach_ReadOnlyBody_TriggersFastStreamingPath()
        {
            var e = NewEvaluator();
            var mockDb = new MockDatabaseSource("REMOTE_DB");
            e.Connections["remote"] = mockDb;
            e.ForeachPageSize = 0;

            // Loop body is read-only -> Safe for high-speed streaming
            var script = @"
FOREACH @row IN (SELECT Id FROM remote.Users ORDER BY Id)
BEGIN
    PRINT @row.Id;
END";

            var tokens = new Lexer(script).Tokenize();
            var program = new Parser(tokens).Parse();
            await e.Evaluate(program);

            // Verify that we received a single streaming query (no OFFSET)
            Assert.Contains(mockDb.CapturedSql, s => s.Contains("SELECT") && s.Contains("Users"));
            Assert.DoesNotContain(mockDb.CapturedSql, s => s.Contains("OFFSET"));
        }

        [Fact]
        public async Task Foreach_OpaqueCallInBody_TriggersSafePagedPath()
        {
            var e = NewEvaluator();
            var mockDb = new MockDatabaseSource("REMOTE_DB");
            e.Connections["remote"] = mockDb;
            e.ForeachPageSize = 10;

            // Loop body contains a statement that we pessimistically assume might have side effects
            // EXECUTE is always treated as unsafe. 
            // We use a script that just checks if the SAFE path was chosen by looking for OFFSET.
            var script = @"
CREATE PROCEDURE some_proc AS PRINT 'mock';
FOREACH @row IN (SELECT Id FROM remote.Users ORDER BY Id)
BEGIN
    EXECUTE some_proc; 
END";

            var tokens = new Lexer(script).Tokenize();
            var program = new Parser(tokens).Parse();

            // To avoid "Table not found" for other_table, we add it to mock connections
            e.Connections["other_table"] = mockDb;

            await e.Evaluate(program);

            Assert.Contains(mockDb.CapturedSql, s => s.Contains("OFFSET"));
        }

        private class MockDatabaseSource : IDatabaseSource
        {
            public List<string> CapturedSql { get; } = new();
            public string ConnectorType => "MOCK";
            public string Path => "mock";
            public Dictionary<string, string>? Options => null;
            public string ConnectionString => "mock_conn";
            public string Dialect => "MSSQL";
            public bool SupportsSqlPushdown => true;

            public MockDatabaseSource(string name) { }

            public async IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null)
            {
                CapturedSql.Add(sql);
                var dt = new DataTable();
                dt.SetColumns(new[] { "Id" });

                if (sql.Trim().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                {
                    // Return 1 row for the first page, empty for subsequent pages to terminate loop
                    if (!sql.Contains("OFFSET 10"))
                    {
                        var r = new Row(dt.Schema);
                        r["Id"] = 1;
                        await dt.AddRowAsync(r);
                    }
                }

                yield return dt;
            }

            public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
            {
                await foreach (var batch in ExecuteRawSql("SELECT * FROM [Users]")) yield return batch;
            }
            public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => Task.CompletedTask;
            public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult(Enumerable.Empty<string>());
            public Task<IEnumerable<string>> GetColumnsAsync(string tableName) => Task.FromResult(Enumerable.Empty<string>());
            public Task<IEnumerable<string>> GetTablesAsync() => Task.FromResult(Enumerable.Empty<string>());
            public Task<IEnumerable<string>> GetViewsAsync() => Task.FromResult(Enumerable.Empty<string>());
            public Task<string> GetVersionAsync() => Task.FromResult("1.0");
            public HashSet<string> GetSupportedFunctions() => new();
            public object? Snapshot() => null;
            public void Restore(object? snapshot) { }
            public IDataSource WithTable(string tableName) => this;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
