using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Xunit;


namespace ETL_SQL.Tests.Statements
{
    public class WindowFunctionTests
    {
        [Fact]
        public async Task TestRowNumber()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #T (Val INT); INSERT INTO #T VALUES (10), (30), (20);"));

            var res = await ev.EvaluateSelect((SelectStatement)Parse("SELECT Val, ROW_NUMBER() OVER(ORDER BY Val) AS RN FROM #T;").Statements[0]).FirstAsync();
            Assert.Equal(3, res.Rows.Count);
            Assert.Equal(1m, res.Rows[0]["RN"]);
            Assert.Equal(2m, res.Rows[1]["RN"]);
            Assert.Equal(3m, res.Rows[2]["RN"]);
        }

        [Fact]
        public async Task TestLagLead()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #T (ID INT, Val INT); INSERT INTO #T VALUES (1, 100), (2, 200), (3, 300);"));

            var res = await ev.EvaluateSelect((SelectStatement)Parse("SELECT Val, LAG(Val) OVER(ORDER BY ID) AS Prev, LEAD(Val) OVER(ORDER BY ID) AS Next FROM #T;").Statements[0]).FirstAsync();

            // Row 1: Val=100, Prev=null, Next=200
            Assert.Null(res.Rows[0]["Prev"]);
            Assert.Equal(200m, res.Rows[0]["Next"]);

            // Row 2: Val=200, Prev=100, Next=300
            Assert.Equal(100m, res.Rows[1]["Prev"]);
            Assert.Equal(300m, res.Rows[1]["Next"]);

            // Row 3: Val=300, Prev=200, Next=null
            Assert.Equal(200m, res.Rows[2]["Prev"]);
            Assert.Null(res.Rows[2]["Next"]);
        }

        [Fact]
        public async Task TestFirstLastValue()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #T (Grp INT, Val INT); INSERT INTO #T VALUES (1, 10), (1, 20), (2, 100), (2, 200);"));

            var res = await ev.EvaluateSelect((SelectStatement)Parse("SELECT Grp, Val, FIRST_VALUE(Val) OVER(PARTITION BY Grp ORDER BY Val) AS First, LAST_VALUE(Val) OVER(PARTITION BY Grp ORDER BY Val) AS Last FROM #T;").Statements[0]).FirstAsync();

            // Grp 1
            Assert.Equal(10m, res.Rows[0]["First"]);
            Assert.Equal(20m, res.Rows[0]["Last"]);

            // Grp 2
            Assert.Equal(100m, res.Rows[2]["First"]);
            Assert.Equal(200m, res.Rows[2]["Last"]);
        }

        [Fact]
        public async Task TestWindowAggregates()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #T (Val INT); INSERT INTO #T VALUES (10), (20), (30);"));

            var res = await ev.EvaluateSelect((SelectStatement)Parse("SELECT Val, SUM(Val) OVER() AS WSum, MIN(Val) OVER() AS WMin, MAX(Val) OVER() AS WMax, AVG(Val) OVER() AS WAvg, COUNT(*) OVER() AS WCount FROM #T;").Statements[0]).FirstAsync();

            foreach (var row in res.Rows)
            {
                Assert.Equal(60m, row["WSum"]);
                Assert.Equal(10m, row["WMin"]);
                Assert.Equal(30m, row["WMax"]);
                Assert.Equal(20m, row["WAvg"]);
                Assert.Equal(3m, row["WCount"]);
            }
        }

        [Fact]
        public async Task TestWindowPartitions()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #T (ID INT, Grp INT, Val INT); INSERT INTO #T VALUES (1, 1, 100), (2, 1, 200), (3, 2, 1000), (4, 2, 2000);"));

            var res = await ev.EvaluateSelect((SelectStatement)Parse("SELECT ID, ROW_NUMBER() OVER(PARTITION BY Grp ORDER BY ID) AS RN, SUM(Val) OVER(PARTITION BY Grp) AS GrpSum FROM #T;").Statements[0]).FirstAsync();

            // Grp 1
            Assert.Equal(1m, res.Rows[0]["RN"]);
            Assert.Equal(300m, res.Rows[0]["GrpSum"]);
            Assert.Equal(2m, res.Rows[1]["RN"]);
            Assert.Equal(1m, res.Rows[2]["RN"]);
            Assert.Equal(3000m, res.Rows[2]["GrpSum"]);
            Assert.Equal(2m, res.Rows[3]["RN"]);
        }

        [Fact]
        public async Task TestRankAndDenseRank()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #R (Val INT); INSERT INTO #R VALUES (10), (10), (20), (30), (30), (40);"));

            var res = await ev.EvaluateSelect((SelectStatement)Parse("SELECT Val, RANK() OVER(ORDER BY Val) AS Rnk, DENSE_RANK() OVER(ORDER BY Val) AS DRnk FROM #R;").Statements[0]).FirstAsync();

            // Expected for RANK: 1, 1, 3, 4, 4, 6
            Assert.Equal(1m, res.Rows[0]["Rnk"]);
            Assert.Equal(1m, res.Rows[1]["Rnk"]);
            Assert.Equal(3m, res.Rows[2]["Rnk"]);
            Assert.Equal(4m, res.Rows[3]["Rnk"]);
            Assert.Equal(4m, res.Rows[4]["Rnk"]);
            Assert.Equal(6m, res.Rows[5]["Rnk"]);

            // Expected for DENSE_RANK: 1, 1, 2, 3, 3, 4
            Assert.Equal(1m, res.Rows[0]["DRnk"]);
            Assert.Equal(1m, res.Rows[1]["DRnk"]);
            Assert.Equal(2m, res.Rows[2]["DRnk"]);
            Assert.Equal(3m, res.Rows[3]["DRnk"]);
            Assert.Equal(3m, res.Rows[4]["DRnk"]);
            Assert.Equal(4m, res.Rows[5]["DRnk"]);
        }

        [Fact]
        public async Task TestWindowFraming()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #F (ID INT, Val INT); INSERT INTO #F VALUES (1, 10), (2, 20), (3, 30), (4, 40);"));

            // ROWS BETWEEN 1 PRECEDING AND CURRENT ROW
            // Row 1: 10 (WSum=10)
            // Row 2: 10+20 (WSum=30)
            // Row 3: 20+30 (WSum=50)
            // Row 4: 30+40 (WSum=70)
            var res = await ev.EvaluateSelect((SelectStatement)Parse("SELECT Val, SUM(Val) OVER(ORDER BY ID ROWS BETWEEN 1 PRECEDING AND CURRENT ROW) AS WSum FROM #F;").Statements[0]).FirstAsync();

            Assert.Equal(10m, res.Rows[0]["WSum"]);
            Assert.Equal(30m, res.Rows[1]["WSum"]);
            Assert.Equal(50m, res.Rows[2]["WSum"]);
            Assert.Equal(70m, res.Rows[3]["WSum"]);

            // ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW (Cumulative Sum)
            var resCum = await ev.EvaluateSelect((SelectStatement)Parse("SELECT Val, SUM(Val) OVER(ORDER BY ID ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS CumSum FROM #F;").Statements[0]).FirstAsync();
            Assert.Equal(10m, resCum.Rows[0]["CumSum"]);
            Assert.Equal(30m, resCum.Rows[1]["CumSum"]);
            Assert.Equal(60m, resCum.Rows[2]["CumSum"]);
            Assert.Equal(100m, resCum.Rows[3]["CumSum"]);
        }

        [Fact]
        public async Task TestWindowGroupsFrame()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #G (K INT, Val INT); INSERT INTO #G VALUES (1, 10), (1, 20), (2, 30), (3, 40), (3, 50);"));

            var res = await ev.EvaluateSelect((SelectStatement)Parse(
                "SELECT K, Val, SUM(Val) OVER(ORDER BY K GROUPS BETWEEN 1 PRECEDING AND CURRENT ROW) AS GSum FROM #G;")
                .Statements[0]).FirstAsync();

            Assert.Equal(30m, res.Rows[0]["GSum"]);
            Assert.Equal(30m, res.Rows[1]["GSum"]);
            Assert.Equal(60m, res.Rows[2]["GSum"]);
            Assert.Equal(120m, res.Rows[3]["GSum"]);
            Assert.Equal(120m, res.Rows[4]["GSum"]);
        }

        [Fact]
        public async Task TestWindowFrameExclusion()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #E (K INT, Val INT); INSERT INTO #E VALUES (1, 10), (1, 20), (2, 30), (3, 40), (3, 50);"));

            var currentRow = await ev.EvaluateSelect((SelectStatement)Parse(
                "SELECT Val, SUM(Val) OVER(ORDER BY K ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW EXCLUDE CURRENT ROW) AS S FROM #E;")
                .Statements[0]).FirstAsync();
            Assert.Null(currentRow.Rows[0]["S"]);  // frame = {} after EXCLUDE CURRENT ROW → SUM(empty) = NULL
            Assert.Equal(10m, currentRow.Rows[1]["S"]);
            Assert.Equal(30m, currentRow.Rows[2]["S"]);

            var group = await ev.EvaluateSelect((SelectStatement)Parse(
                "SELECT Val, SUM(Val) OVER(ORDER BY K RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW EXCLUDE GROUP) AS S FROM #E;")
                .Statements[0]).FirstAsync();
            Assert.Null(group.Rows[0]["S"]);  // K=1 group excluded from frame → SUM(empty) = NULL
            Assert.Null(group.Rows[1]["S"]);  // K=1 group excluded from frame → SUM(empty) = NULL
            Assert.Equal(30m, group.Rows[2]["S"]);
            Assert.Equal(60m, group.Rows[3]["S"]);
            Assert.Equal(60m, group.Rows[4]["S"]);

            var ties = await ev.EvaluateSelect((SelectStatement)Parse(
                "SELECT Val, SUM(Val) OVER(ORDER BY K RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW EXCLUDE TIES) AS S FROM #E;")
                .Statements[0]).FirstAsync();
            Assert.Equal(10m, ties.Rows[0]["S"]);
            Assert.Equal(20m, ties.Rows[1]["S"]);
            Assert.Equal(60m, ties.Rows[2]["S"]);
            Assert.Equal(100m, ties.Rows[3]["S"]);
            Assert.Equal(110m, ties.Rows[4]["S"]);
        }

        [Fact]
        public async Task TestPercentRankAndCumeDist()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #PR (Val INT); INSERT INTO #PR VALUES (10), (20), (30), (40);"));

            var res = await ev.EvaluateSelect((SelectStatement)Parse(
                "SELECT Val, PERCENT_RANK() OVER(ORDER BY Val) AS PR, CUME_DIST() OVER(ORDER BY Val) AS CD FROM #PR;")
                .Statements[0]).FirstAsync();

            // PERCENT_RANK: (rank-1)/(N-1) = 0, 1/3, 2/3, 1
            Assert.Equal(0m, res.Rows[0]["PR"]);
            Assert.Equal(1m / 3m, (decimal)res.Rows[1]["PR"]!);
            Assert.Equal(2m / 3m, (decimal)res.Rows[2]["PR"]!);
            Assert.Equal(1m, res.Rows[3]["PR"]);

            // CUME_DIST: peer_end_pos+1 / N = 0.25, 0.5, 0.75, 1.0
            Assert.Equal(0.25m, res.Rows[0]["CD"]);
            Assert.Equal(0.5m, res.Rows[1]["CD"]);
            Assert.Equal(0.75m, res.Rows[2]["CD"]);
            Assert.Equal(1.0m, res.Rows[3]["CD"]);
        }

        [Fact]
        public async Task TestCumeDist_WithTies()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #CD (Val INT); INSERT INTO #CD VALUES (10), (10), (20);"));

            var res = await ev.EvaluateSelect((SelectStatement)Parse(
                "SELECT Val, CUME_DIST() OVER(ORDER BY Val) AS CD FROM #CD;")
                .Statements[0]).FirstAsync();

            // Both 10s share the same peer group ending at index 1 → CD = 2/3
            Assert.Equal(2m / 3m, (decimal)res.Rows[0]["CD"]!);
            Assert.Equal(2m / 3m, (decimal)res.Rows[1]["CD"]!);
            Assert.Equal(1m, res.Rows[2]["CD"]);
        }

        [Fact]
        public async Task TestNthValue()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #NV (ID INT, Val INT); INSERT INTO #NV VALUES (1, 100), (2, 200), (3, 300);"));

            var res = await ev.EvaluateSelect((SelectStatement)Parse(
                "SELECT Val, NTH_VALUE(Val, 2) OVER(ORDER BY ID ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) AS NV FROM #NV;")
                .Statements[0]).FirstAsync();

            // All rows see partition of all 3 rows; 2nd value = 200
            Assert.Equal(200m, res.Rows[0]["NV"]);
            Assert.Equal(200m, res.Rows[1]["NV"]);
            Assert.Equal(200m, res.Rows[2]["NV"]);
        }

        [Fact]
        public async Task TestPercentileCont()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #P (Val DECIMAL(10,2)); INSERT INTO #P VALUES (1), (2), (3), (4);"));

            // Median (0.5): row_number = 0.5 * 3 = 1.5 → interpolate between 2 and 3 = 2.5
            var res = await ev.EvaluateSelect((SelectStatement)Parse(
                "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY Val) AS Med FROM #P GROUP BY (SELECT 1);")
                .Statements[0]).FirstAsync();

            Assert.Equal(2.5m, Convert.ToDecimal(res.Rows[0]["Med"]));
        }

        [Fact]
        public async Task TestPercentileDisc()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #PD (Val DECIMAL(10,2)); INSERT INTO #PD VALUES (1), (2), (3), (4);"));

            // Median (0.5): first row where cume_dist >= 0.5 → 2nd row (val=2, cd=0.5)
            var res = await ev.EvaluateSelect((SelectStatement)Parse(
                "SELECT PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY Val) AS Med FROM #PD GROUP BY (SELECT 1);")
                .Statements[0]).FirstAsync();

            Assert.Equal(2m, Convert.ToDecimal(res.Rows[0]["Med"]));
        }

        [Fact]
        public async Task TestLag_ThreeArgDefault()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #T (ID INT, Val INT); INSERT INTO #T VALUES (1, 100), (2, 200), (3, 300);"));

            var res = await ev.EvaluateSelect((SelectStatement)Parse(
                "SELECT Val, LAG(Val, 1, -1) OVER(ORDER BY ID) AS Prev FROM #T;")
                .Statements[0]).FirstAsync();

            // Row 1 has no predecessor: default -1 applies
            Assert.Equal(-1m, res.Rows[0]["Prev"]);
            Assert.Equal(100m, res.Rows[1]["Prev"]);
            Assert.Equal(200m, res.Rows[2]["Prev"]);
        }

        [Fact]
        public async Task TestLead_ThreeArgDefault()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #T2 (ID INT, Val INT); INSERT INTO #T2 VALUES (1, 100), (2, 200), (3, 300);"));

            var res = await ev.EvaluateSelect((SelectStatement)Parse(
                "SELECT Val, LEAD(Val, 1, 0) OVER(ORDER BY ID) AS Next FROM #T2;")
                .Statements[0]).FirstAsync();

            Assert.Equal(200m, res.Rows[0]["Next"]);
            Assert.Equal(300m, res.Rows[1]["Next"]);
            // Row 3 has no successor: default 0 applies
            Assert.Equal(0m, res.Rows[2]["Next"]);
        }

        [Fact]
        public async Task TestEmbeddedWindowFunction_InBinaryExpression()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #Revenue (Month INT, Revenue INT); INSERT INTO #Revenue VALUES (1, 100), (2, 150), (3, 200);"));

            // Revenue - LAG(Revenue, 1, 0) OVER (ORDER BY Month) as Delta
            var res = await ev.EvaluateSelect((SelectStatement)Parse(
                "SELECT Month, Revenue, Revenue - LAG(Revenue, 1, 0) OVER(ORDER BY Month) AS Delta FROM #Revenue;")
                .Statements[0]).FirstAsync();

            Assert.Equal(3, res.Rows.Count);
            // Month 1: Revenue=100, no previous (default 0), Delta = 100 - 0 = 100
            Assert.Equal(100m, res.Rows[0]["Delta"]);
            // Month 2: Revenue=150, previous=100, Delta = 150 - 100 = 50
            Assert.Equal(50m, res.Rows[1]["Delta"]);
            // Month 3: Revenue=200, previous=150, Delta = 200 - 150 = 50
            Assert.Equal(50m, res.Rows[2]["Delta"]);
        }

        [Fact]
        public async Task TestQualifyClause()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #Q (Dept VARCHAR, Name VARCHAR, Salary INT); " +
                                  "INSERT INTO #Q VALUES ('Eng', 'Alice', 100), ('Eng', 'Bob', 200), ('Sales', 'Charlie', 150);"));

            // Should return only the highest salary per department
            var res = await ev.EvaluateSelect((SelectStatement)Parse(
                "SELECT Dept, Name, Salary, RANK() OVER(PARTITION BY Dept ORDER BY Salary DESC) as rnk FROM #Q QUALIFY rnk = 1;")
                .Statements[0]).FirstAsync();

            Assert.Equal(2, res.Rows.Count);
            Assert.Contains(res.Rows, r => r["Name"].ToString() == "Bob");
            Assert.Contains(res.Rows, r => r["Name"].ToString() == "Charlie");
        }

        [Fact]
        public async Task TestWindowFilterClause()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #F (Val INT); INSERT INTO #F VALUES (10), (100), (20), (200);"));

            // SUM(Val) FILTER (WHERE Val > 50)
            // Rows: 10 (skipped), 100 (included), 20 (skipped), 200 (included)
            // Cumulative Sums: 0, 100, 100, 300
            var res = await ev.EvaluateSelect((SelectStatement)Parse(
                "SELECT Val, SUM(Val) FILTER (WHERE Val > 50) OVER(ORDER BY Val) AS FilteredSum FROM #F;")
                .Statements[0]).FirstAsync();

            Assert.Equal(4, res.Rows.Count);
            Assert.Null(res.Rows[0]["FilteredSum"]);   // 10 — no values pass Val > 50 → SUM(empty) = NULL
            Assert.Null(res.Rows[1]["FilteredSum"]);   // 20 — no values pass Val > 50 → SUM(empty) = NULL
            Assert.Equal(100m, res.Rows[2]["FilteredSum"]); // 100
            Assert.Equal(300m, res.Rows[3]["FilteredSum"]); // 200
        }

        private static Script Parse(string source)
        {
            var lexer = new Lexer(source);
            return new Parser(lexer.Tokenize()).Parse();
        }
    }
}
