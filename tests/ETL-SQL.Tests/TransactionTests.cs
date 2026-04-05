using Xunit;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;

namespace ETL_SQL.Tests
{
    public class TransactionTests
    {
        [Fact]
        public async Task TestNestedRollback()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            evaluator.Connections["#test"] = new InMemoryDataSource();
            
            string script = @"
                BEGIN TRANSACTION;
                INSERT INTO #test (id, val) VALUES (1, 'Outer');
                BEGIN TRANSACTION;
                INSERT INTO #test (id, val) VALUES (2, 'Inner');
                ROLLBACK TRANSACTION;
                -- Both should be gone if a full rollback happened, 
                -- or just Inner if partial. Standard ETL-SQL ROLLBACK aborts all.
            ";

            var lexer = new Lexer(script);
            var parser = new Parser(lexer.Tokenize());
            await evaluator.Evaluate(parser.Parse());

            var mem = (InMemoryDataSource)evaluator.Connections["#test"];
            int rowCount = 0;
            await foreach (var batch in mem.ReadBatches()) rowCount += batch.Rows.Count;
            
            Assert.Equal(0, rowCount);
        }

        [Fact]
        public async Task TestInnerCommitOuterRollback()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            evaluator.Connections["#test"] = new InMemoryDataSource();
            
            string script = @"
                BEGIN TRANSACTION;
                INSERT INTO #test (id, val) VALUES (1, 'Outer');
                BEGIN TRANSACTION;
                INSERT INTO #test (id, val) VALUES (2, 'Inner');
                COMMIT TRANSACTION; -- Inner commit
                ROLLBACK TRANSACTION; -- Outer rollback should revert BOTH
            ";

            var lexer = new Lexer(script);
            var parser = new Parser(lexer.Tokenize());
            await evaluator.Evaluate(parser.Parse());

            var mem = (InMemoryDataSource)evaluator.Connections["#test"];
            int rowCount = 0;
            await foreach (var batch in mem.ReadBatches()) rowCount += batch.Rows.Count;
            
            Assert.Equal(0, rowCount);
        }
        [Fact]
        public async Task TestCommitInsideTry()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            evaluator.Connections["#test"] = new InMemoryDataSource();
            
            string script = @"
                BEGIN TRANSACTION;
                BEGIN TRY
                    INSERT INTO #test (id, val) VALUES (1, 'Success');
                    COMMIT TRANSACTION;
                END TRY
                BEGIN CATCH
                    ROLLBACK TRANSACTION;
                END CATCH;
            ";

            await evaluator.Evaluate(new Lexer(script).TokenizeToScript());

            var mem = (InMemoryDataSource)evaluator.Connections["#test"];
            int rowCount = 0;
            await foreach (var batch in mem.ReadBatches()) rowCount += batch.Rows.Count;
            
            Assert.Equal(1, rowCount);
        }

        [Fact]
        public async Task TestRollbackInsideCatch()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            evaluator.Connections["#test"] = new InMemoryDataSource();
            
            string script = @"
                BEGIN TRANSACTION;
                BEGIN TRY
                    INSERT INTO #test (id, val) VALUES (1, 'Before Error');
                    THROW 'Force Error';
                    INSERT INTO #test (id, val) VALUES (2, 'After Error');
                    COMMIT TRANSACTION;
                END TRY
                BEGIN CATCH
                    ROLLBACK TRANSACTION;
                END CATCH;
            ";

            await evaluator.Evaluate(new Lexer(script).TokenizeToScript());

            var mem = (InMemoryDataSource)evaluator.Connections["#test"];
            int rowCount = 0;
            await foreach (var batch in mem.ReadBatches()) rowCount += batch.Rows.Count;
            
            Assert.Equal(0, rowCount);
        }

        [Fact]
        public async Task TestTranCount()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            
            await evaluator.Evaluate(new Lexer("BEGIN TRANSACTION;").TokenizeToScript());
            Assert.Equal(1, Convert.ToInt32(evaluator.GetVariable("@@TRANCOUNT")));

            await evaluator.Evaluate(new Lexer("BEGIN TRAN;").TokenizeToScript());
            Assert.Equal(2, Convert.ToInt32(evaluator.GetVariable("@@TRANCOUNT")));

            await evaluator.Evaluate(new Lexer("COMMIT;").TokenizeToScript());
            Assert.Equal(1, Convert.ToInt32(evaluator.GetVariable("@@TRANCOUNT")));

            await evaluator.Evaluate(new Lexer("ROLLBACK;").TokenizeToScript());
            Assert.Equal(0, Convert.ToInt32(evaluator.GetVariable("@@TRANCOUNT")));
        }
        [Fact]
        public async Task TestExternalSqlRollback()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var mock = new MockTransactionalDataSource();
            evaluator.Connections["sql"] = mock;

            string script = @"
                BEGIN TRANSACTION;
                INSERT INTO sql.table (id) VALUES (1);
                ROLLBACK TRANSACTION;
            ";

            await evaluator.Evaluate(new Lexer(script).TokenizeToScript());

            Assert.True(mock.BeginCalled, "BeginTransactionAsync should have been called");
            Assert.True(mock.RollbackCalled, "RollbackAsync should have been called");
            Assert.False(mock.CommitCalled, "CommitAsync should NOT have been called");
        }

        private class MockTransactionalDataSource : ITransactionalDataSource, IDatabaseSource
        {
            public bool BeginCalled { get; private set; }
            public bool CommitCalled { get; private set; }
            public bool RollbackCalled { get; private set; }

            public Task BeginTransactionAsync() { BeginCalled = true; return Task.CompletedTask; }
            public Task CommitAsync() { CommitCalled = true; return Task.CompletedTask; }
            public Task RollbackAsync() { RollbackCalled = true; return Task.CompletedTask; }

            public string ConnectionString => "MOCK";
            public string Path => "MOCK";
            public string Dialect => "MSSQL";
            public Dictionary<string, string>? Options => null;
            public IDataSource WithTable(string tableName) => this;
            public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) { yield break; }
            public Task WriteBatches(IAsyncEnumerable<DataTable> batches) => Task.CompletedTask;
            public async IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null) { yield break; }
            public Task<string> GetVersionAsync() => Task.FromResult("Mock 1.0");
            public HashSet<string> GetSupportedFunctions() => new();
            public Task<IEnumerable<string>> GetTablesAsync() => Task.FromResult(Enumerable.Empty<string>());
            public Task<IEnumerable<string>> GetViewsAsync() => Task.FromResult(Enumerable.Empty<string>());
            public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult(Enumerable.Empty<string>());
            public Task<IEnumerable<string>> GetColumnsAsync(string tableName) => Task.FromResult(Enumerable.Empty<string>());
            public object? Snapshot() => null;
            public void Restore(object? snapshot) { }
            public Task TruncateAsync() => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
