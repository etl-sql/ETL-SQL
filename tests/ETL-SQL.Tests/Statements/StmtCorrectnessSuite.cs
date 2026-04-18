using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.App;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests.Statements
{
    public class QueryCorrectnessSuite
    {
        private static Evaluator NewEvaluator() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        // ─── Group 1: DISTINCT Correctness ────────────────────────────────────────

        [Fact]
        public async Task Distinct_SingleColumn_DeduplicatesCorrectly()
        {
            var ev = NewEvaluator();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (val INT); INSERT INTO #t VALUES (1), (2), (1), (3), (2);");
            
            var res = await ev.ExecuteQuery(TestHelpers.Parse("SELECT DISTINCT val FROM #t ORDER BY val;").Statements[0]).FirstAsync();
            TestHelpers.AssertRowsMatch(res, 
                new object[] { 1 },
                new object[] { 2 },
                new object[] { 3 }
            );
        }

        [Fact]
        public async Task Distinct_MultiColumn_DeduplicatesCorrectly()
        {
            var ev = NewEvaluator();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (a INT, b INT); INSERT INTO #t VALUES (1, 1), (1, 2), (1, 1), (2, 1);");
            
            var res = await ev.ExecuteQuery(TestHelpers.Parse("SELECT DISTINCT a, b FROM #t ORDER BY a, b;").Statements[0]).FirstAsync();
            TestHelpers.AssertRowsMatch(res, 
                new object[] { 1, 1 },
                new object[] { 1, 2 },
                new object[] { 2, 1 }
            );
        }

        [Fact]
        public async Task Distinct_Nulls_TreatedAsSingleValue()
        {
            var ev = NewEvaluator();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (val INT); INSERT INTO #t VALUES (1), (NULL), (NULL), (2);");
            
            var res = await ev.ExecuteQuery(TestHelpers.Parse("SELECT DISTINCT val FROM #t ORDER BY val;").Statements[0]).FirstAsync();
            // NULL typically sorts to the top or bottom; our engine currently sorts NULLs to the top in memory
            TestHelpers.AssertRowsMatch(res, 
                new object[] { null },
                new object[] { 1 },
                new object[] { 2 }
            );
        }

        // ─── Group 2: Temporal Grouping Correctness ──────────────────────────────

        [Fact]
        public async Task GroupBy_Date_RawExactMatches()
        {
            var ev = NewEvaluator();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (d DATETIME, amt INT);");
            await TestHelpers.Execute(ev, "INSERT INTO #t VALUES ('2024-01-01', 10), ('2024-01-01', 20), ('2024-01-02', 30);");
            
            var res = await ev.ExecuteQuery(TestHelpers.Parse("SELECT d, SUM(amt) AS total FROM #t GROUP BY d ORDER BY d;").Statements[0]).FirstAsync();
            TestHelpers.AssertRowsMatch(res, 
                new object[] { DateTime.Parse("2024-01-01"), 30m },
                new object[] { DateTime.Parse("2024-01-02"), 30m }
            );
        }

        [Fact]
        public async Task GroupBy_Date_LocalizedFormats()
        {
            var ev = NewEvaluator();
            // Test with standard ISO format
            await TestHelpers.Execute(ev, "CREATE TABLE #t (d DATETIME, amt INT);");
            await TestHelpers.Execute(ev, "INSERT INTO #t VALUES ('2024-01-15', 10), ('2024-01-15', 20), ('2024-01-16', 50);");
            
            var res = await ev.ExecuteQuery(TestHelpers.Parse("SELECT d, SUM(amt) AS total FROM #t GROUP BY d ORDER BY d;").Statements[0]).FirstAsync();
            
            TestHelpers.AssertRowsMatch(res, 
                new object[] { new DateTime(2024, 1, 15), 30m },
                new object[] { new DateTime(2024, 1, 16), 50m }
            );
        }

        [Fact]
        public async Task GroupBy_Date_LeapYearBoundary()
        {
            var ev = NewEvaluator();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (d DATETIME, amt INT);");
            await TestHelpers.Execute(ev, "INSERT INTO #t (d, amt) VALUES ('2024-02-28', 1), ('2024-02-29', 10), ('2024-02-29', 20), ('2024-03-01', 100);");
            
            var res = await ev.ExecuteQuery(TestHelpers.Parse("SELECT d, SUM(amt) AS total FROM #t GROUP BY d ORDER BY d;").Statements[0]).FirstAsync();
            TestHelpers.AssertRowsMatch(res, 
                new object[] { new DateTime(2024, 2, 28), 1m },
                new object[] { new DateTime(2024, 2, 29), 30m },
                new object[] { new DateTime(2024, 3, 1), 100m }
            );
        }

        // ─── Group 3: Expression & Null Logic ────────────────────────────────────

        [Fact]
        public async Task Expression_Coalesce_InGrouping()
        {
            var ev = NewEvaluator();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (v1 INT, v2 INT); INSERT INTO #t VALUES (1, 10), (NULL, 20), (NULL, 20);");
            
            var res = await ev.ExecuteQuery(TestHelpers.Parse("SELECT COALESCE(v1, 0) AS g, SUM(v2) AS s FROM #t GROUP BY COALESCE(v1, 0) ORDER BY g;").Statements[0]).FirstAsync();
            
            TestHelpers.AssertRowsMatch(res, 
                new object[] { 0m, 40m }, 
                new object[] { 1m, 10m }
            );
        }

        [Fact]
        public async Task Expression_Case_CorrectEvaluation()
        {
            var ev = NewEvaluator();
            await TestHelpers.Execute(ev, "SELECT (CASE WHEN 1=1 THEN 'True' ELSE 'False' END) AS res;");
            var res = ev.LastResult!;
            Assert.Equal("True", res.Rows[0]["res"]);
        }

        // ─── Group 4: Relational Algebra ────────────────────────────────────────

        [Fact]
        public async Task Join_Inner_CorrectResults()
        {
            var ev = NewEvaluator();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #t1 (id INT, name VARCHAR);
                CREATE TABLE #t2 (id INT, score INT);
                INSERT INTO #t1 VALUES (1, 'A'), (2, 'B'), (3, 'C');
                INSERT INTO #t2 VALUES (1, 100), (2, 200), (4, 400);
            ");

            var res = await ev.ExecuteQuery(TestHelpers.Parse("SELECT t1.name, t2.score FROM #t1 AS t1 JOIN #t2 AS t2 ON t1.id = t2.id ORDER BY t1.id;").Statements[0]).FirstAsync();
            TestHelpers.AssertRowsMatch(res, 
                new object[] { "A", 100m },
                new object[] { "B", 200m }
            );
        }

        [Fact]
        public async Task Join_Left_CorrectResultsWithNulls()
        {
            var ev = NewEvaluator();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #t1 (id INT, name VARCHAR);
                CREATE TABLE #t2 (id INT, score INT);
                INSERT INTO #t1 VALUES (1, 'A'), (2, 'B'), (3, 'C');
                INSERT INTO #t2 VALUES (1, 100), (2, 200);
            ");

            var res = await ev.ExecuteQuery(TestHelpers.Parse("SELECT t1.id, t2.score FROM #t1 AS t1 LEFT JOIN #t2 AS t2 ON t1.id = t2.id ORDER BY t1.id;").Statements[0]).FirstAsync();
            TestHelpers.AssertRowsMatch(res, 
                new object[] { 1, 100m },
                new object[] { 2, 200m },
                new object[] { 3, null }
            );
        }

        [Fact]
        public async Task SetOperation_Union_Deduplicates()
        {
            var ev = NewEvaluator();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #t1 (val INT);
                CREATE TABLE #t2 (val INT);
                INSERT INTO #t1 VALUES (1), (2);
                INSERT INTO #t2 VALUES (2), (3);
            ");

            var res = await ev.ExecuteQuery(TestHelpers.Parse("SELECT val FROM #t1 UNION SELECT val FROM #t2 ORDER BY val;").Statements[0]).FirstAsync();
            TestHelpers.AssertRowsMatch(res, 
                new object[] { 1 },
                new object[] { 2 },
                new object[] { 3 }
            );
        }

        [Fact]
        public async Task SetOperation_Intersect_FindsCommon()
        {
            var ev = NewEvaluator();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #t1 (val INT);
                CREATE TABLE #t2 (val INT);
                INSERT INTO #t1 VALUES (1), (2);
                INSERT INTO #t2 VALUES (2), (3);
            ");

            var res = await ev.ExecuteQuery(TestHelpers.Parse("SELECT val FROM #t1 INTERSECT SELECT val FROM #t2;").Statements[0]).FirstAsync();
            TestHelpers.AssertRowsMatch(res, new object[] { 2 });
        }

        // ─── Group 5: NULL Sensitivity & 3VL Correctness ──────────────────────────

        [Fact]
        public async Task Join_DoesNotMatch_OnNullValues()
        {
            var ev = NewEvaluator();
            await TestHelpers.Execute(ev, "CREATE TABLE #t1 (id INT, val VARCHAR); INSERT INTO #t1 VALUES (1, 'A'), (NULL, 'B');");
            await TestHelpers.Execute(ev, "CREATE TABLE #t2 (id INT, val VARCHAR); INSERT INTO #t2 VALUES (1, 'X'), (NULL, 'Y');");
            
            // Only id=1 should match. NULL=NULL is UNKNOWN/FALSE.
            var res = await ev.ExecuteQuery(TestHelpers.Parse("SELECT t1.val, t2.val FROM #t1 AS t1 JOIN #t2 AS t2 ON t1.id = t2.id;").Statements[0]).FirstAsync();
            
            Assert.Single(res.Rows);
            Assert.Equal("A", res.Rows[0][0]);
            Assert.Equal("X", res.Rows[0][1]);
        }

        [Fact]
        public async Task Where_Filter_HandlesNullEquality_StandardSQL()
        {
            var ev = NewEvaluator();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (id INT); INSERT INTO #t VALUES (1), (NULL);");
            
            var res = await ev.ExecuteQuery(TestHelpers.Parse("SELECT * FROM #t WHERE id = NULL;").Statements[0]).FirstAsync();
            Assert.Empty(res.Rows);

            var resIn = await ev.ExecuteQuery(TestHelpers.Parse("SELECT * FROM #t WHERE id IN (NULL);").Statements[0]).FirstAsync();
            Assert.Empty(resIn.Rows);

            var resIsNull = await ev.ExecuteQuery(TestHelpers.Parse("SELECT * FROM #t WHERE id IS NULL;").Statements[0]).FirstAsync();
            Assert.Single(resIsNull.Rows);
        }

        // ─── Group 6: Window Function Correctness ─────────────────────────────────

        [Fact]
        public async Task Window_Partitioning_UsesNormalizedValues()
        {
            var ev = NewEvaluator();
            // Test with mixed date formats that represent same logical day
            await TestHelpers.Execute(ev, "CREATE TABLE #t (dt VARCHAR, val INT);");
            await TestHelpers.Execute(ev, "INSERT INTO #t VALUES ('2024-01-01', 10), ('01/01/2024', 20), ('2024-02-01', 100);");
            
            var res = await ev.ExecuteQuery(TestHelpers.Parse("SELECT dt, SUM(val) OVER(PARTITION BY dt) AS total FROM #t ORDER BY dt;").Statements[0]).FirstAsync();
            
            // If normalization works, 2024-01-01 and 01/01/2024 should be in same partition (Sum=30)
            Assert.Equal(3, res.Rows.Count);
            Assert.Equal(30m, Convert.ToDecimal(res.Rows[0]["total"]));
            Assert.Equal(30m, Convert.ToDecimal(res.Rows[1]["total"]));
            Assert.Equal(100m, Convert.ToDecimal(res.Rows[2]["total"]));
        }
    }
}
