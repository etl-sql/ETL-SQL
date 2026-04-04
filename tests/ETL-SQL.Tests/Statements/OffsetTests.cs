using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;


namespace ETL_SQL.Tests
{
    public class OffsetTests
    {
        private Evaluator GetEvaluator()
        {
            return DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        }

        private Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            return new Parser(tokens).Parse();
        }

        [Fact]
        public async Task TestOffsetOnly()
        {
            var ev = GetEvaluator();
            await ev.Evaluate(Parse(@"
                CREATE TABLE #data (ID INT, Name STRING);
                INSERT INTO #data VALUES (1, 'A'), (2, 'B'), (3, 'C'), (4, 'D'), (5, 'E');
            "));

            // Skip 2 rows. Result should be 3, 4, 5
            var res = await ev.ExecuteQuery(Parse("SELECT ID FROM #data ORDER BY ID OFFSET 2;").Statements[0]).FirstAsync();
            Assert.Equal(3, res.Rows.Count);
            Assert.Equal(3m, res.Rows[0]["ID"]);
            Assert.Equal(5m, res.Rows[2]["ID"]);
        }

        [Fact]
        public async Task TestOffsetAndLimit()
        {
            var ev = GetEvaluator();
            await ev.Evaluate(Parse(@"
                CREATE TABLE #data (ID INT);
                INSERT INTO #data VALUES (1), (2), (3), (4), (5);
            "));

            // Skip 1, take 2. Result should be 2, 3
            var res = await ev.ExecuteQuery(Parse("SELECT ID FROM #data ORDER BY ID LIMIT 2 OFFSET 1;").Statements[0]).FirstAsync();
            Assert.Equal(2, res.Rows.Count);
            Assert.Equal(2m, res.Rows[0]["ID"]);
            Assert.Equal(3m, res.Rows[1]["ID"]);
        }

        [Fact]
        public async Task TestOffsetRowsSyntax()
        {
            var ev = GetEvaluator();
            await ev.Evaluate(Parse(@"
                CREATE TABLE #data (ID INT);
                INSERT INTO #data VALUES (1), (2), (3);
            "));

            var res = await ev.ExecuteQuery(Parse("SELECT ID FROM #data ORDER BY ID OFFSET 1 ROWS;").Statements[0]).FirstAsync();
            Assert.Equal(2, res.Rows.Count);
            Assert.Equal(2m, res.Rows[0]["ID"]);
        }

        [Fact]
        public async Task TestOffsetWithVariable()
        {
            var ev = GetEvaluator();
            await ev.Evaluate(Parse(@"
                CREATE TABLE #data (ID INT);
                INSERT INTO #data VALUES (1), (2), (3), (4), (5);
                DECLARE @off INT = 3;
            "));

            var res = await ev.ExecuteQuery(Parse("SELECT ID FROM #data ORDER BY ID OFFSET @off;").Statements[0]).FirstAsync();
            Assert.Equal(2, res.Rows.Count);
            Assert.Equal(4m, res.Rows[0]["ID"]);
        }

        [Fact]
        public async Task TestOffsetWithGroupBy()
        {
            var ev = GetEvaluator();
            await ev.Evaluate(Parse(@"
                CREATE TABLE #data (Cat STRING, Val INT);
                INSERT INTO #data VALUES ('A', 10), ('B', 20), ('C', 30), ('D', 40);
            "));

            // Groups A, B, C, D. Sort by Cat. Offset 2. Result: C, D
            var res = await ev.ExecuteQuery(Parse("SELECT Cat, SUM(Val) AS S FROM #data GROUP BY Cat ORDER BY Cat OFFSET 2;").Statements[0]).FirstAsync();
            Assert.Equal(2, res.Rows.Count);
            Assert.Equal("C", res.Rows[0]["Cat"]?.ToString());
            Assert.Equal("D", res.Rows[1]["Cat"]?.ToString());
        }

        [Fact]
        public async Task TestGlobalOrderByDescending()
        {
            var ev = GetEvaluator();
            await ev.Evaluate(Parse(@"
                CREATE TABLE #data (ID INT);
                INSERT INTO #data VALUES (1), (3), (2);
            "));

            var res = await ev.ExecuteQuery(Parse("SELECT ID FROM #data ORDER BY ID DESC;").Statements[0]).FirstAsync();
            Assert.Equal(3, res.Rows.Count);
            Assert.Equal(3m, res.Rows[0]["ID"]);
            Assert.Equal(2m, res.Rows[1]["ID"]);
            Assert.Equal(1m, res.Rows[2]["ID"]);
        }
    }
}
