using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;


namespace ETL_SQL.Tests.Statements
{
    public class NtileTests
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
        public async Task TestNtilePerfectSplit()
        {
            var ev = GetEvaluator();
            await ev.Evaluate(Parse(@"
                CREATE TABLE #t (V INT);
                INSERT INTO #t VALUES (1), (2), (3), (4);
            "));

            var res = await ev.ExecuteQuery(Parse("SELECT V, NTILE(2) OVER(ORDER BY V) AS B FROM #t;").Statements[0]).FirstAsync();
            Assert.Equal(4, res.Rows.Count);
            Assert.NotNull(res.Rows[0]["B"]);
            Assert.Equal(1m, Convert.ToDecimal(res.Rows[0]["B"]));
            Assert.NotNull(res.Rows[1]["B"]);
            Assert.Equal(1m, Convert.ToDecimal(res.Rows[1]["B"]));
            Assert.NotNull(res.Rows[2]["B"]);
            Assert.Equal(2m, Convert.ToDecimal(res.Rows[2]["B"]));
            Assert.NotNull(res.Rows[3]["B"]);
            Assert.Equal(2m, Convert.ToDecimal(res.Rows[3]["B"]));
        }

        [Fact]
        public async Task TestNtileUnevenSplit()
        {
            var ev = GetEvaluator();
            await ev.Evaluate(Parse(@"
                CREATE TABLE #t (V INT);
                INSERT INTO #t VALUES (1), (2), (3), (4);
            "));

            // NTILE(3) on 4 rows. 4/3 = 1 rem 1.
            // Bucket 1: 2 rows. Bucket 2: 1 row. Bucket 3: 1 row.
            var res = await ev.ExecuteQuery(Parse("SELECT V, NTILE(3) OVER(ORDER BY V) AS B FROM #t;").Statements[0]).FirstAsync();
            Assert.Equal(4, res.Rows.Count);
            Assert.NotNull(res.Rows[0]["B"]);
            Assert.Equal(1m, Convert.ToDecimal(res.Rows[0]["B"]));
            Assert.NotNull(res.Rows[1]["B"]);
            Assert.Equal(1m, Convert.ToDecimal(res.Rows[1]["B"]));
            Assert.NotNull(res.Rows[2]["B"]);
            Assert.Equal(2m, Convert.ToDecimal(res.Rows[2]["B"]));
            Assert.NotNull(res.Rows[3]["B"]);
            Assert.Equal(3m, Convert.ToDecimal(res.Rows[3]["B"]));
        }

        [Fact]
        public async Task TestNtileMoreBucketsThanRows()
        {
            var ev = GetEvaluator();
            await ev.Evaluate(Parse(@"
                CREATE TABLE #t (D INT);
                INSERT INTO #t VALUES (10), (20), (30);
            "));

            // NTILE(10) on 3 rows. Each row gets its own bucket.
            var res = await ev.ExecuteQuery(Parse("SELECT D, NTILE(10) OVER(ORDER BY D) AS B FROM #t;").Statements[0]).FirstAsync();
            Assert.Equal(3, res.Rows.Count);
            Assert.NotNull(res.Rows[0]["B"]);
            Assert.Equal(1m, Convert.ToDecimal(res.Rows[0]["B"]));
            Assert.NotNull(res.Rows[1]["B"]);
            Assert.Equal(2m, Convert.ToDecimal(res.Rows[1]["B"]));
            Assert.NotNull(res.Rows[2]["B"]);
            Assert.Equal(3m, Convert.ToDecimal(res.Rows[2]["B"]));
        }

        [Fact]
        public async Task TestNtileSingleBucket()
        {
            var ev = GetEvaluator();
            await ev.Evaluate(Parse(@"
                CREATE TABLE #t (X INT);
                INSERT INTO #t VALUES (1), (2), (3), (4), (5);
            "));

            var res = await ev.ExecuteQuery(Parse("SELECT X, NTILE(1) OVER(ORDER BY X) AS B FROM #t;").Statements[0]).FirstAsync();
            Assert.All(res.Rows, r =>
            {
                Assert.NotNull(r["B"]);
                Assert.Equal(1m, Convert.ToDecimal(r["B"]));
            });
        }

        [Fact]
        public async Task TestNtilePartitioned()
        {
            var ev = GetEvaluator();
            await ev.Evaluate(Parse(@"
                CREATE TABLE #t (Grp STRING, ID INT);
                INSERT INTO #t VALUES ('A', 1), ('A', 2), ('B', 3), ('B', 4);
            "));

            var res = await ev.ExecuteQuery(Parse("SELECT Grp, ID, NTILE(2) OVER(PARTITION BY Grp ORDER BY ID) AS B FROM #t;").Statements[0]).FirstAsync();
            Assert.Equal(4, res.Rows.Count);

            // Grp A
            var rA1 = res.Rows.First(r => r["Grp"]?.ToString() == "A" && Convert.ToDecimal(r["ID"]) == 1m);
            Assert.NotNull(rA1["B"]);
            Assert.Equal(1m, Convert.ToDecimal(rA1["B"]));

            var rA2 = res.Rows.First(r => r["Grp"]?.ToString() == "A" && Convert.ToDecimal(r["ID"]) == 2m);
            Assert.NotNull(rA2["B"]);
            Assert.Equal(2m, Convert.ToDecimal(rA2["B"]));

            // Grp B
            var rB1 = res.Rows.First(r => r["Grp"]?.ToString() == "B" && Convert.ToDecimal(r["ID"]) == 3m);
            Assert.NotNull(rB1["B"]);
            Assert.Equal(1m, Convert.ToDecimal(rB1["B"]));

            var rB2 = res.Rows.First(r => r["Grp"]?.ToString() == "B" && Convert.ToDecimal(r["ID"]) == 4m);
            Assert.NotNull(rB2["B"]);
            Assert.Equal(2m, Convert.ToDecimal(rB2["B"]));
        }
    }
}
