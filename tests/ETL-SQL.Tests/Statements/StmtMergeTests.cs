using Xunit;
using ETL_SQL.Core;

using ETL_SQL.Engine;
using ETL_SQL.Data;
using ETL_SQL.App;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;

namespace ETL_SQL.Tests.Statements
{
    public class MergeTests : IAsyncLifetime
    {
        private Evaluator _evaluator;
        private IServiceProvider _serviceProvider;

        public async Task InitializeAsync()
        {
            _serviceProvider = DependencyInjectionSetup.BuildServiceProvider();
            _evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            await Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            await _evaluator.DisposeAsync();
        }

        [Fact]
        public async Task Merge_UpdateInsert_Works()
        {
            // Setup Target
            var script1 = new Lexer(@"
                CREATE TABLE #Target (ID INT, Name STRING, Val INT);
                INSERT INTO #Target VALUES (1, 'Alice', 10), (2, 'Bob', 20);
            ").TokenizeToScript();
            await _evaluator.Evaluate(script1);

            // Setup Source
            var script2 = new Lexer(@"
                CREATE TABLE #Source (ID INT, Name STRING, Val INT);
                INSERT INTO #Source VALUES (1, 'Alice', 100), (3, 'Charlie', 30);
            ").TokenizeToScript();
            await _evaluator.Evaluate(script2);

            // Merge
            var mergeScript = new Lexer(@"
                MERGE INTO #Target AS T
                USING #Source AS S
                ON T.ID = S.ID
                WHEN MATCHED THEN
                    UPDATE SET Val = S.Val
                WHEN NOT MATCHED THEN
                    INSERT (ID, Name, Val) VALUES (S.ID, S.Name, S.Val);
            ").TokenizeToScript();
            await _evaluator.Evaluate(mergeScript);

            // Verify
            var selectScript = new Lexer("SELECT * FROM #Target ORDER BY ID;").TokenizeToScript();
            await _evaluator.Evaluate(selectScript);
            var result = _evaluator.LastResult;

            Assert.Equal(3, result.Rows.Count);
            Assert.NotNull(result.Rows[0]["Val"]);
            Assert.Equal(100, Convert.ToInt32(result.Rows[0]["Val"])); // Updated
            Assert.NotNull(result.Rows[1]["Val"]);
            Assert.Equal(20, Convert.ToInt32(result.Rows[1]["Val"]));  // Unchanged
            Assert.NotNull(result.Rows[2]["Val"]);
            Assert.Equal(30, Convert.ToInt32(result.Rows[2]["Val"]));  // Inserted
            Assert.Equal("Charlie", result.Rows[2]["Name"]);
        }

        [Fact]
        public async Task Merge_Delete_Works()
        {
            // Setup
            var setup = new Lexer(@"
                CREATE TABLE #T1 (ID INT, Status STRING);
                INSERT INTO #T1 VALUES (1, 'Active'), (2, 'Active'), (3, 'Old');
                CREATE TABLE #S1 (ID INT, Action STRING);
                INSERT INTO #S1 VALUES (1, 'Keep'), (3, 'Delete');
            ").TokenizeToScript();
            await _evaluator.Evaluate(setup);

            // Merge with condition
            var merge = new Lexer(@"
                MERGE #T1 AS T
                USING #S1 AS S
                ON T.ID = S.ID
                WHEN MATCHED AND S.Action = 'Delete' THEN
                    DELETE
                WHEN MATCHED THEN
                    UPDATE SET Status = 'Updated';
            ").TokenizeToScript();
            await _evaluator.Evaluate(merge);

            // Verify
            await _evaluator.Evaluate(new Lexer("SELECT * FROM #T1 ORDER BY ID;").TokenizeToScript());
            var result = _evaluator.LastResult;

            Assert.Equal(2, result.Rows.Count);
            Assert.NotNull(result.Rows[0]["ID"]);
            Assert.Equal(1, Convert.ToInt32(result.Rows[0]["ID"]));
            Assert.Equal("Updated", result.Rows[0]["Status"]);
            Assert.NotNull(result.Rows[1]["ID"]);
            Assert.Equal(2, Convert.ToInt32(result.Rows[1]["ID"]));
            Assert.Equal("Active", result.Rows[1]["Status"]);
        }

        [Fact]
        public async Task Merge_NotMatchedBySource_Works()
        {
             // Setup
            var setup = new Lexer(@"
                CREATE TABLE #T2 (ID INT, Name STRING);
                INSERT INTO #T2 VALUES (1, 'A'), (2, 'B'), (3, 'C');
                CREATE TABLE #S2 (ID INT);
                INSERT INTO #S2 VALUES (1), (2);
            ").TokenizeToScript();
            await _evaluator.Evaluate(setup);

            // Merge - Delete target rows not in source
            var merge = new Lexer(@"
                MERGE #T2 AS T
                USING #S2 AS S
                ON T.ID = S.ID
                WHEN NOT MATCHED BY SOURCE THEN
                    DELETE;
            ").TokenizeToScript();
            await _evaluator.Evaluate(merge);

            // Verify
            await _evaluator.Evaluate(new Lexer("SELECT * FROM #T2 ORDER BY ID;").TokenizeToScript());
            var result = _evaluator.LastResult;

            Assert.Equal(2, result.Rows.Count);
            Assert.DoesNotContain(result.Rows, r => {
                Assert.NotNull(r["ID"]);
                return Convert.ToInt32(r["ID"]) == 3;
            });
        }

        [Fact]
        public async Task Merge_SubquerySource_Works()
        {
            await _evaluator.Evaluate(new Lexer("CREATE TABLE #Final (K INT, V STRING);").TokenizeToScript());

            var merge = new Lexer(@"
                MERGE #Final AS T
                USING (SELECT 1 AS K, 'New' AS V) AS S
                ON T.K = S.K
                WHEN NOT MATCHED THEN
                    INSERT (K, V) VALUES (S.K, S.V);
            ").TokenizeToScript();
            await _evaluator.Evaluate(merge);

            await _evaluator.Evaluate(new Lexer("SELECT * FROM #Final;").TokenizeToScript());
            Assert.Single(_evaluator.LastResult.Rows);
            Assert.Equal("New", _evaluator.LastResult.Rows[0]["V"]);
        }
    }
}
