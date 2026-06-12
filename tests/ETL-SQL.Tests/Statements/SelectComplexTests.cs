using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Tests.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    /// <summary>
    /// Phase 0 correctness fixtures for SELECT edge cases not covered by existing tests.
    /// These lock in current engine semantics BEFORE streaming/optimizer changes land so
    /// regressions are immediately visible.
    ///
    /// Topics: TOP PERCENT, ORDER BY alias/ordinal+DESC, UNION NULL semantics,
    /// UNION ALL + OFFSET/LIMIT, ROLLUP + HAVING, FULL OUTER JOIN, LEFT JOIN anti-join.
    /// </summary>
    public class SelectComplexTests
    {
        private static Evaluator Ev() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        private static async Task<DataTable> Q(Evaluator ev, string sql)
        {
            var stmt = TestHelpers.Parse(sql).Statements[0];
            var result = new DataTable();
            await foreach (var batch in ev.ExecuteQuery(stmt))
            {
                if (result.ColumnNames.Count == 0) result.SetColumns(batch.ColumnNames);
                foreach (var row in batch.Rows) await result.AddRowAsync(row);
            }
            return result;
        }

        // ─── Group 1: TOP PERCENT ─────────────────────────────────────────────────

        [Fact]
        public async Task TopPercent_ExactHalf_ReturnsCeiling()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #t (id INT);
                INSERT INTO #t VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10);
            ");
            // 50% of 10 = ceiling(5.0) = 5 rows
            var r = await Q(ev, "SELECT TOP 50 PERCENT id FROM #t ORDER BY id;");
            Assert.Equal(5, r.Rows.Count);
            Assert.Equal(1m, r.Rows[0]["id"]);
            Assert.Equal(5m, r.Rows[4]["id"]);
        }

        [Fact]
        public async Task TopPercent_FractionalResult_RoundsUp()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #t (id INT);
                INSERT INTO #t VALUES (1),(2),(3);
            ");
            // 33% of 3 = ceiling(0.99) = 1 row
            var r = await Q(ev, "SELECT TOP 33 PERCENT id FROM #t ORDER BY id;");
            Assert.Single(r.Rows);
            Assert.Equal(1m, r.Rows[0]["id"]);
        }

        // ─── Group 2: ORDER BY edge cases ────────────────────────────────────────

        [Fact]
        public async Task OrderBy_SelectAlias_SortsOnComputedValue()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #t (a INT, b INT);
                INSERT INTO #t VALUES (1,3),(2,1),(3,2);
            ");
            // total = a + b: (1→4), (2→3), (3→5) — sorted ascending by alias
            var r = await Q(ev, "SELECT a, a + b AS total FROM #t ORDER BY total;");
            Assert.Equal(3, r.Rows.Count);
            Assert.Equal(2m, r.Rows[0]["a"]);   // total=3
            Assert.Equal(1m, r.Rows[1]["a"]);   // total=4
            Assert.Equal(3m, r.Rows[2]["a"]);   // total=5
        }

        [Fact]
        public async Task OrderBy_OrdinalDescending_SortsCorrectly()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #t (a INT, b INT);
                INSERT INTO #t VALUES (2,1),(1,2),(3,3);
            ");
            // ORDER BY 1 DESC = ORDER BY a DESC
            var r = await Q(ev, "SELECT a, b FROM #t ORDER BY 1 DESC;");
            Assert.Equal(3m, r.Rows[0]["a"]);
            Assert.Equal(2m, r.Rows[1]["a"]);
            Assert.Equal(1m, r.Rows[2]["a"]);
        }

        [Fact]
        public async Task OrderBy_AliasFromAggregate_SortsGroups()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #t (cat VARCHAR, val INT);
                INSERT INTO #t VALUES ('A',10),('A',20),('B',5),('B',15);
            ");
            // s: A=30, B=20 — sorted ascending by s
            var r = await Q(ev, "SELECT cat, SUM(val) AS s FROM #t GROUP BY cat ORDER BY s;");
            Assert.Equal(2, r.Rows.Count);
            Assert.Equal("B", r.Rows[0]["cat"]);  // s=20
            Assert.Equal("A", r.Rows[1]["cat"]);  // s=30
        }

        [Fact]
        public async Task OrderBy_MultiColumn_MixedDirections()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #t (grp INT, val INT);
                INSERT INTO #t VALUES (1,30),(1,10),(2,20),(2,40);
            ");
            // ORDER BY grp ASC, val DESC: group 1 first → 30,10; group 2 → 40,20
            var r = await Q(ev, "SELECT grp, val FROM #t ORDER BY grp ASC, val DESC;");
            Assert.Equal(4, r.Rows.Count);
            Assert.Equal(1m, r.Rows[0]["grp"]); Assert.Equal(30m, r.Rows[0]["val"]);
            Assert.Equal(1m, r.Rows[1]["grp"]); Assert.Equal(10m, r.Rows[1]["val"]);
            Assert.Equal(2m, r.Rows[2]["grp"]); Assert.Equal(40m, r.Rows[2]["val"]);
            Assert.Equal(2m, r.Rows[3]["grp"]); Assert.Equal(20m, r.Rows[3]["val"]);
        }

        // ─── Group 3: UNION / UNION ALL semantics ────────────────────────────────

        [Fact]
        public async Task Union_NullRows_TreatedAsEqual_Deduplicated()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #t1 (val INT); INSERT INTO #t1 VALUES (NULL),(1),(2);
                CREATE TABLE #t2 (val INT); INSERT INTO #t2 VALUES (NULL),(2),(3);
            ");
            // UNION deduplicates: two NULLs → one NULL; two 2s → one 2
            var r = await Q(ev, "SELECT val FROM #t1 UNION SELECT val FROM #t2 ORDER BY val;");
            // Engine sorts NULLs first; 4 distinct values: NULL, 1, 2, 3
            Assert.Equal(4, r.Rows.Count);
            Assert.Null(r.Rows[0]["val"]);
            Assert.Equal(1m, r.Rows[1]["val"]);
            Assert.Equal(2m, r.Rows[2]["val"]);
            Assert.Equal(3m, r.Rows[3]["val"]);
        }

        [Fact]
        public async Task UnionAll_WithOffsetLimit_ViaSubquery()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #t (val INT);
                INSERT INTO #t VALUES (1),(2),(3);
            ");
            // UNION ALL inside a subquery so ORDER BY / OFFSET / LIMIT apply to the combined result.
            // (Direct UNION ALL + ORDER BY attaches clauses to the rightmost SELECT only;
            //  use a subquery wrapper for correct outer-level OFFSET/LIMIT semantics.)
            // Source 1: 1,2,3 | Source 2 (val+10): 11,12,13
            // Combined sorted: 1,2,3,11,12,13 → LIMIT 2 OFFSET 2 → 3,11
            var r = await Q(ev, @"
                SELECT val FROM (
                    SELECT val FROM #t
                    UNION ALL
                    SELECT val + 10 FROM #t
                ) AS combined
                ORDER BY val
                LIMIT 2
                OFFSET 2;");
            Assert.Equal(2, r.Rows.Count);
            Assert.Equal(3m, r.Rows[0]["val"]);
            Assert.Equal(11m, r.Rows[1]["val"]);
        }

        // ─── Group 4: ROLLUP with HAVING ─────────────────────────────────────────

        [Fact]
        public async Task Rollup_HavingFiltersGroupsAndGrandTotal()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #t (cat VARCHAR, val INT);
                INSERT INTO #t VALUES ('A',10),('A',20),('B',30),('B',40);
            ");
            // ROLLUP groups: A=30, B=70, grand total=100
            // HAVING SUM(val) >= 70 keeps B(70) and grand total(100), drops A(30)
            var r = await Q(ev, @"
                SELECT cat, SUM(val) AS total
                FROM #t
                GROUP BY ROLLUP(cat)
                HAVING SUM(val) >= 70;");
            Assert.Equal(2, r.Rows.Count);
            // Row with cat='B'
            var bRow = r.Rows.FirstOrDefault(row => row["cat"]?.ToString() == "B");
            Assert.NotNull(bRow);
            Assert.Equal(70m, bRow["total"]);
            // Grand total row (cat IS NULL)
            var grandRow = r.Rows.FirstOrDefault(row => row["cat"] == null || row["cat"] == DBNull.Value);
            Assert.NotNull(grandRow);
            Assert.Equal(100m, grandRow["total"]);
        }

        // ─── Group 5: Outer join edge cases ──────────────────────────────────────

        [Fact]
        public async Task FullOuterJoin_PreservesUnmatchedRowsFromBothSides()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #left  (id INT, name VARCHAR);
                CREATE TABLE #right (id INT, score INT);
                INSERT INTO #left  VALUES (1,'A'),(2,'B');
                INSERT INTO #right VALUES (2,200),(3,300);
            ");
            // id=1: left only  → (name='A', score=NULL)
            // id=2: both sides → (name='B', score=200)
            // id=3: right only → (name=NULL, score=300)
            var r = await Q(ev, @"
                SELECT l.name, r.score
                FROM #left  AS l
                FULL OUTER JOIN #right AS r ON l.id = r.id
                ORDER BY COALESCE(l.id, r.id);");
            Assert.Equal(3, r.Rows.Count);
            Assert.Equal("A", r.Rows[0]["name"]); Assert.Null(r.Rows[0]["score"]);
            Assert.Equal("B", r.Rows[1]["name"]); Assert.Equal(200m, r.Rows[1]["score"]);
            Assert.Null(r.Rows[2]["name"]); Assert.Equal(300m, r.Rows[2]["score"]);
        }

        [Fact]
        public async Task LeftJoin_WhereRightIsNull_ActsAsAntiJoin()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #left  (id INT, name VARCHAR);
                CREATE TABLE #right (id INT, score INT);
                INSERT INTO #left  VALUES (1,'A'),(2,'B'),(3,'C');
                INSERT INTO #right VALUES (1,100),(2,200);
            ");
            // id=3 has no match in right → WHERE r.score IS NULL selects only 'C'
            var r = await Q(ev, @"
                SELECT l.name
                FROM #left AS l
                LEFT JOIN #right AS r ON l.id = r.id
                WHERE r.score IS NULL
                ORDER BY l.name;");
            Assert.Single(r.Rows);
            Assert.Equal("C", r.Rows[0]["name"]);
        }

        [Fact]
        public async Task LeftJoin_WhereOnRightColumn_NotNull_EffectivelyInnerJoin()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #left  (id INT, name VARCHAR);
                CREATE TABLE #right (id INT, score INT);
                INSERT INTO #left  VALUES (1,'A'),(2,'B'),(3,'C');
                INSERT INTO #right VALUES (1,100),(2,200);
            ");
            // WHERE r.score > 0 filters out the NULL row, making it behave like INNER JOIN
            var r = await Q(ev, @"
                SELECT l.name, r.score
                FROM #left AS l
                LEFT JOIN #right AS r ON l.id = r.id
                WHERE r.score > 0
                ORDER BY l.id;");
            Assert.Equal(2, r.Rows.Count);
            Assert.Equal("A", r.Rows[0]["name"]);
            Assert.Equal("B", r.Rows[1]["name"]);
        }

        // ─── Group 6: QUALIFY after window ────────────────────────────────────────

        [Fact]
        public async Task Qualify_RowNumber_FiltersToTopPerPartition()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #t (cat VARCHAR, val INT);
                INSERT INTO #t VALUES ('A',10),('A',30),('A',20),('B',5),('B',15);
            ");
            // Window engine only evaluates functions listed in SELECT; include rn so QUALIFY can filter on it.
            var r = await Q(ev, @"
                SELECT cat, val, ROW_NUMBER() OVER (PARTITION BY cat ORDER BY val DESC) AS rn
                FROM #t
                QUALIFY rn = 1
                ORDER BY cat;");
            Assert.Equal(2, r.Rows.Count);
            Assert.Equal("A", r.Rows[0]["cat"]); Assert.Equal(30m, r.Rows[0]["val"]);
            Assert.Equal("B", r.Rows[1]["cat"]); Assert.Equal(15m, r.Rows[1]["val"]);
        }

        [Fact]
        public async Task Qualify_Alias_ReferencesWindowResultByAlias()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #t (id INT, grp INT, val INT);
                INSERT INTO #t VALUES (1,1,100),(2,1,200),(3,1,300),(4,2,10),(5,2,20);
            ");
            // QUALIFY with an alias referencing the window column
            var r = await Q(ev, @"
                SELECT id, grp, val, RANK() OVER (PARTITION BY grp ORDER BY val DESC) AS rnk
                FROM #t
                QUALIFY rnk <= 2
                ORDER BY grp, rnk;");
            Assert.Equal(4, r.Rows.Count);
            // grp 1: val 300 (rnk=1), 200 (rnk=2)
            Assert.Equal(300m, r.Rows[0]["val"]); Assert.Equal(1m, r.Rows[0]["rnk"]);
            Assert.Equal(200m, r.Rows[1]["val"]); Assert.Equal(2m, r.Rows[1]["rnk"]);
            // grp 2: val 20 (rnk=1), 10 (rnk=2)
            Assert.Equal(20m, r.Rows[2]["val"]); Assert.Equal(1m, r.Rows[2]["rnk"]);
            Assert.Equal(10m, r.Rows[3]["val"]); Assert.Equal(2m, r.Rows[3]["rnk"]);
        }

        // ─── Group 7: CUBE grouping set ───────────────────────────────────────────

        [Fact]
        public async Task Cube_TwoColumns_ProducesAllCombinations()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #t (region VARCHAR, cat VARCHAR, val INT);
                INSERT INTO #t VALUES ('East','A',10),('East','B',20),('West','A',30),('West','B',40);
            ");
            // CUBE(region, cat) produces: (E,A),(E,B),(W,A),(W,B),(E,null),(W,null),(null,A),(null,B),(null,null)
            var r = await Q(ev, @"
                SELECT region, cat, SUM(val) AS total
                FROM #t
                GROUP BY CUBE(region, cat);");
            // All 4 combos + 2 region subtotals + 2 cat subtotals + 1 grand total = 9 rows
            Assert.Equal(9, r.Rows.Count);
            // Grand total row: region=null, cat=null, total=100
            var grand = r.Rows.FirstOrDefault(row =>
                (row["region"] == null || row["region"] == DBNull.Value) &&
                (row["cat"] == null || row["cat"] == DBNull.Value));
            Assert.NotNull(grand);
            Assert.Equal(100m, grand["total"]);
        }

        // ─── Group 8: NULL semantics in DISTINCT ──────────────────────────────────

        [Fact]
        public async Task Distinct_NullTreatedAsSingleGroup()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #t (val INT);
                INSERT INTO #t VALUES (NULL),(NULL),(1),(1),(2);
            ");
            // Two NULLs → one NULL; two 1s → one 1; 2 stays
            var r = await Q(ev, "SELECT DISTINCT val FROM #t ORDER BY val;");
            Assert.Equal(3, r.Rows.Count);
            Assert.Null(r.Rows[0]["val"]);       // NULL first
            Assert.Equal(1m, r.Rows[1]["val"]);
            Assert.Equal(2m, r.Rows[2]["val"]);
        }

        [Fact]
        public async Task Distinct_MultiColumnWithNull_CollapsesOnAllColumns()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #t (a INT, b INT);
                INSERT INTO #t VALUES (1,NULL),(1,NULL),(2,3),(2,3),(1,2);
            ");
            // Three distinct combos: (1,null), (2,3), (1,2)
            var r = await Q(ev, "SELECT DISTINCT a, b FROM #t ORDER BY a, b;");
            Assert.Equal(3, r.Rows.Count);
        }

        // ─── Group 9: WITH TIES ───────────────────────────────────────────────────

        [Fact]
        public async Task Top_WithTies_IncludesTiedRows()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #t (id INT, score INT);
                INSERT INTO #t VALUES (1,100),(2,90),(3,90),(4,80);
            ");
            // TOP n WITH TIES is the supported syntax; LIMIT n WITH TIES is not parsed.
            // Top 2 scores are 100 and 90; since two rows tie at 90, all 3 qualifying rows are returned.
            var r = await Q(ev, "SELECT TOP 2 WITH TIES id, score FROM #t ORDER BY score DESC;");
            Assert.Equal(3, r.Rows.Count);
            Assert.Equal(100m, r.Rows[0]["score"]);
            Assert.All(r.Rows.Skip(1), row => Assert.Equal(90m, row["score"]));
        }
    }
}
