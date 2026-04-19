using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.App;

namespace ETL_SQL.Tests.Operations.Operations
{
    public class ForeachPushdownTests
    {
        private static Evaluator NewEvaluator() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        [Fact]
        public async Task Foreach_RemoteSql_PushesOffsetFetch()
        {
            var e = NewEvaluator();
            var mockDb = new MockDatabaseSource("REMOTE_DB");
            e.Connections["remote"] = mockDb;
            e.ForeachPageSize = 5;

            var script = @"
FOREACH @row IN (SELECT Id, Name FROM remote.Users ORDER BY Id)
BEGIN
    PRINT @row.Name;
END";
            
            var tokens = new Lexer(script).Tokenize();
            var program = new Parser(tokens).Parse();
            await e.Evaluate(program);

            // Verify that we received paged queries (QueryCompiler parameterizes literals as @pN)
            Assert.Contains(mockDb.CapturedSql, s => s.Contains("OFFSET") && s.Contains("ROWS FETCH NEXT") && s.Contains("ROWS ONLY"));
            Assert.True(mockDb.CapturedSql.Count >= 2, "Expected at least 2 paged queries");
            
            // Should have made exactly 3 calls (0-4, 5-9, 10-14 -> return 0 rows)
            Assert.Equal(3, mockDb.CapturedSql.Count);
        }

        [Fact]
        public async Task Foreach_NoOrderBy_FallsBackToStreaming()
        {
            var e = NewEvaluator();
            var mockDb = new MockDatabaseSource("REMOTE_DB");
            e.Connections["remote"] = mockDb;
            e.ForeachPageSize = 5;

            // No ORDER BY clause
            var script = @"
FOREACH @row IN (SELECT Id, Name FROM remote.Users)
BEGIN
    PRINT @row.Name;
END";
            
            var tokens = new Lexer(script).Tokenize();
            var program = new Parser(tokens).Parse();
            await e.Evaluate(program);

            // Verify that we received a single streaming query without OFFSET/FETCH
            Assert.Single(mockDb.CapturedSql);
            Assert.DoesNotContain("OFFSET", mockDb.CapturedSql[0]);
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
                dt.SetColumns(new[] { "Id", "Name" });
                
                // Return 5 rows for the first two pages, then 0 for the last one
                if (CapturedSql.Count <= 2)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        var r = new Row(dt.Schema);
                        r["Id"] = i;
                        r["Name"] = "User" + i;
                        await dt.AddRowAsync(r);
                    }
                    yield return dt;
                }
                else
                {
                    yield return dt; // Empty page
                }
            }

            public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) 
            {
                yield return new DataTable(); // Return empty to avoid crash if called
            }
            public Task WriteBatches(IAsyncEnumerable<DataTable> batches) => throw new NotImplementedException();
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
