using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Common;

namespace ETL_SQL.Tests.Analysis.Analysis
{
    public class LineageExecuteTests
    {
        [Fact]
        public void TestStaticLineage_ExecutePushdown()
        {
            var sql = "EXECUTE m INTO #emp BEGIN SELECT * FROM dbo.Employee END";
            var script = TestHelpers.Parse(sql);
            
            var tracker = new LineageTracker(NullLogger.Instance);
            var analyzer = new LineageAnalyzer(tracker);
            analyzer.Analyze(script);
            
            var entries = tracker.GetFullLineage().ToList();
            Assert.Contains(entries, e => e.Operation == "EXECUTE PUSHDOWN" && e.TargetTable == "#emp" && e.SourceTables.Contains("m.dbo.Employee"));
        }

        [Fact]
        public async Task TestDynamicLineage_ExecutePushdown()
        {
            var eval = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            
            // Mock connection
            var mock = new MockDatabaseSourceWithSchema();
            eval.Connections["m"] = mock;
            
            var sql = "EXECUTE m INTO #emp BEGIN SELECT * FROM dbo.Employee END";
            await TestHelpers.Execute(eval, sql);
            
            var lineage = eval.LineageTracker.GetFullLineage().ToList();
            
            // Verify ACTUAL lineage recorded by the handler
            Assert.Contains(lineage, e => e.Operation == "EXECUTE PUSHDOWN (ACTUAL)" && e.TargetTable == "#emp");
            Assert.Contains(lineage, e => e.Operation == "EXECUTE PUSHDOWN COLUMN (ACTUAL)" && e.TargetTable == "#emp" && e.TargetColumn == "id");
            Assert.Contains(lineage, e => e.Operation == "EXECUTE PUSHDOWN COLUMN (ACTUAL)" && e.TargetTable == "#emp" && e.TargetColumn == "name");
        }

        private class MockDatabaseSourceWithSchema : IDatabaseSource
        {
            public string Dialect => "MSSQL";
            public bool SupportsSqlPushdown => true;
            public string ConnectionString => "mock://local";
            public string Path => "mock://local";
            public Dictionary<string, string>? Options => null;
            public string ConnectorType => "MOCK";
            public IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?> parameters = null)
            {
                var dt = new DataTable();
                dt.SetColumns(new[] { "id", "name", "email" });
                return new[] { dt }.ToAsyncEnumerable();
            }

            public Task<string> GetVersionAsync() => Task.FromResult("Mock 1.0");
            public HashSet<string> GetSupportedFunctions() => new();
            public Task<IEnumerable<string>> GetTablesAsync() => Task.FromResult(Enumerable.Empty<string>());
            public Task<IEnumerable<string>> GetViewsAsync() => Task.FromResult(Enumerable.Empty<string>());
            public Task<IEnumerable<string>> GetColumnsAsync(string tableName) => Task.FromResult(Enumerable.Empty<string>());
            public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => Enumerable.Empty<DataTable>().ToAsyncEnumerable();
            public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => Task.CompletedTask;
            public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult(Enumerable.Empty<string>());
            public object? Snapshot() => null;
            public void Restore(object? snapshot) { }
            public IDataSource WithTable(string tableName) => this;
            public async ValueTask DisposeAsync() => await Task.CompletedTask;
            public Task TruncateAsync() => Task.CompletedTask;
        }
    }
}
