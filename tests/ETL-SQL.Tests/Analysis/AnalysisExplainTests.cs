using Xunit;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Core.Parser;
using ETL_SQL.App;
using System;

namespace ETL_SQL.Tests.Analysis
{
    public class ExplainAnalyzeTests
    {
        private async Task<Evaluator> GetEvaluator()
        {
            var provider = DependencyInjectionSetup.BuildServiceProvider();
            return provider.GetRequiredService<Evaluator>();
        }

        private Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            return new Parser(tokens).Parse();
        }

        [Fact]
        public async Task TestExplainAnalyze_DisplaysActualRows()
        {
            var eval = await GetEvaluator();
            var sql = @"
                CREATE TABLE #T (ID INT);
                INSERT INTO #T (ID) VALUES (1), (2), (3);
                EXPLAIN ANALYZE SELECT * FROM #T;
            ";

            await eval.Evaluate(Parse(sql));

            var plan = eval.LastResult;
            Assert.NotNull(plan);
            Assert.Contains("Actual Rows", plan.ColumnNames);
            
            // The last row should have the actual row count (3)
            var lastRow = plan.Rows.Last();
            Assert.Equal(3L, Convert.ToInt64(lastRow["Actual Rows"]));
        }

        [Fact]
        public async Task TestExplain_NoAnalyze_HasNoActualColumns()
        {
            var eval = await GetEvaluator();
            var sql = @"
                CREATE TABLE #T (ID INT);
                EXPLAIN SELECT * FROM #T;
            ";

            await eval.Evaluate(Parse(sql));

            var plan = eval.LastResult;
            Assert.NotNull(plan);
            Assert.DoesNotContain("Actual Rows", plan.ColumnNames);
        }

        [Fact]
        public async Task TestExplainAnalyze_WithJoins()
        {
            var eval = await GetEvaluator();
            var sql = @"
                CREATE TABLE #A (ID INT);
                CREATE TABLE #B (ID INT);
                INSERT INTO #A VALUES (1), (2);
                INSERT INTO #B VALUES (1);
                EXPLAIN ANALYZE SELECT * FROM #A JOIN #B ON #A.ID = #B.ID;
            ";

            await eval.Evaluate(Parse(sql));

            var plan = eval.LastResult;
            Assert.NotNull(plan);
            var lastRow = plan.Rows.Last();
            Assert.Equal(1L, Convert.ToInt64(lastRow["Actual Rows"]));
        }
        [Fact]
        public async Task TestExplain_IntoTempTable()
        {
            var eval = await GetEvaluator();
            var sql = @"
                CREATE TABLE #T (ID INT);
                EXPLAIN SELECT * FROM #T INTO #PlanTable;
                SELECT * FROM #PlanTable;
            ";

            await eval.Evaluate(Parse(sql));

            var results = eval.LastResult;
            Assert.NotNull(results);
            Assert.Contains("Operation", results.ColumnNames);
            Assert.Contains("Cost", results.ColumnNames);
        }
    }
}
