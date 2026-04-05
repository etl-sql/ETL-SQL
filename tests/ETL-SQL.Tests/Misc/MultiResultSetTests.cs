using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Data;
using ETL_SQL.Engine;

using ETL_SQL.Common;
using ETL_SQL.Core;
using Microsoft.Extensions.DependencyInjection;

using ETL_SQL.Engine.Handlers;
using ETL_SQL.App;

namespace ETL_SQL.Tests
{
    public class MultiResultSetTests
    {
        private readonly Evaluator _evaluator;
        private readonly IServiceProvider _serviceProvider;

        public MultiResultSetTests()
        {
            _serviceProvider = DependencyInjectionSetup.BuildServiceProvider();
            _evaluator = _serviceProvider.GetRequiredService<Evaluator>();
        }

        [Fact]
        public async Task TestMultiResultSetExecution()
        {
            var mockDb = new MockMultiResultDb();
            _evaluator.Connections["MyDb"] = mockDb;

            string script = @"
                EXECUTE ( 'SELECT 1; SELECT 2' ) AT MyDb;
                
                DECLARE @count INT = 0;
                FOREACH @rs IN @@RESULTSETS
                BEGIN
                    SET @count = @count + 1;
                END
                
                -- Verify we have 2 result sets
                IF @count != 2 THROW 'Expected 2 result sets, got ' + CAST(@count AS STRING);
            ";

            var tokens = new Lexer(script).Tokenize();
            var parser = new Parser(tokens);
            var program = parser.Parse();

            await _evaluator.Evaluate(program);

            Assert.Equal(2, _evaluator.LastResultSets.Count);
        }
    }

    public class MockMultiResultDb : IDatabaseSource
    {
        public string ConnectionString => "MockDB";
        public string Path => "MockDB";
        public string Dialect => "MSSQL";

        public async IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null)
        {
            // Result Set 1
            var rs1 = new DataTable { ResultSetIndex = 0 };
            rs1.ColumnNames.Add("Col1");
            var row1 = new Row(); row1["Col1"] = 1; rs1.Rows.Add(row1);
            yield return rs1;

            // Result Set 2
            var rs2 = new DataTable { ResultSetIndex = 1 };
            rs2.ColumnNames.Add("ColA");
            var row2 = new Row(); row2["ColA"] = "A"; rs2.Rows.Add(row2);
            yield return rs2;
        }

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) { yield break; }
        public Task WriteBatches(IAsyncEnumerable<DataTable> batches) => Task.CompletedTask;
        public Task<string> GetVersionAsync() => Task.FromResult("Mock 1.0");
        public HashSet<string> GetSupportedFunctions() => new();
        public Task<IEnumerable<string>> GetTablesAsync() => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetViewsAsync() => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(string tableName) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult(Enumerable.Empty<string>());
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => this;
        public Task TruncateAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
