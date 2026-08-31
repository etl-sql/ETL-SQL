using System;
using System.Collections.Generic;
using System.IO;
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
    /// Golden-output regression suite. Each test asserts exact row count, column values,
    /// and where noted CLR type identity. A failure here means a language feature that
    /// previously worked has broken.
    /// </summary>
    public class StmtGoldenTests
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

        // ─── Group 1: SELECT Correctness ─────────────────────────────────────────

        [Fact]
        public async Task Select_LiteralTypes_IntDecimalString()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, "SELECT 42 AS n, 3.14 AS d, 'hello' AS s;");
            var r = ev.LastResult as DataTable;
            Assert.NotNull(r);
            Assert.Equal(42m, r!.Rows[0]["n"]);
            Assert.Equal(3.14m, r.Rows[0]["d"]);
            Assert.Equal("hello", r.Rows[0]["s"]);
        }

        [Fact]
        public async Task Select_DatePreserved_NotConvertedToDecimal()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (d DATETIME); INSERT INTO #t VALUES ('2024-03-15');");
            var r = await Q(ev, "SELECT d FROM #t;");
            Assert.Single(r.Rows);
            Assert.IsType<DateTime>(r.Rows[0]["d"]);
            Assert.Equal(new DateTime(2024, 3, 15), r.Rows[0]["d"]);
        }

        [Fact]
        public async Task Select_DecimalArithmetic_Precise()
        {
            var ev = Ev();
            var r = await Q(ev, "SELECT 0.1 + 0.2 AS result;");
            Assert.Equal(0.3m, r.Rows[0]["result"]);
        }

        [Fact]
        public async Task Select_NullPropagation_ArithAndConcat()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (a INT, b INT); INSERT INTO #t VALUES (1, NULL);");
            var r = await Q(ev, "SELECT a + b AS arith, a || b AS concat FROM #t;");
            Assert.Null(r.Rows[0]["arith"]);
            Assert.Null(r.Rows[0]["concat"]);
        }

        [Fact]
        public async Task Select_ConcatenationSupportsAliasAndNumericCoercion()
        {
            var ev = Ev();
            var r = await Q(ev, "SELECT 'Dept ' || 7 AS cat;");

            Assert.Equal("Dept 7", r.Rows[0]["cat"]);
        }

        [Fact]
        public async Task Select_StringFunctions_UpperLowerLen()
        {
            var ev = Ev();
            var r = await Q(ev, "SELECT UPPER('hello') AS u, LOWER('WORLD') AS l, LEN('abc') AS n;");
            Assert.Equal("HELLO", r.Rows[0]["u"]);
            Assert.Equal("world", r.Rows[0]["l"]);
            Assert.Equal(3m, r.Rows[0]["n"]);
        }

        [Fact]
        public async Task Select_TopN_LimitsRows()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (v INT); INSERT INTO #t VALUES (1),(2),(3),(4),(5);");
            var r = await Q(ev, "SELECT TOP 3 v FROM #t ORDER BY v;");
            Assert.Equal(3, r.Rows.Count);
            Assert.Equal(1m, r.Rows[0]["v"]);
            Assert.Equal(3m, r.Rows[2]["v"]);
        }

        [Fact]
        public async Task Select_Distinct_PreservesTypes()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (d DATETIME); INSERT INTO #t VALUES ('2024-01-01'),('2024-01-01'),('2024-02-01');");
            var r = await Q(ev, "SELECT DISTINCT d FROM #t ORDER BY d;");
            Assert.Equal(2, r.Rows.Count);
            Assert.IsType<DateTime>(r.Rows[0]["d"]);
        }

        [Fact]
        public async Task Select_OrderByPositional()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (a INT, b INT); INSERT INTO #t VALUES (2,1),(1,2),(3,3);");
            var r = await Q(ev, "SELECT a, b FROM #t ORDER BY 2;");
            Assert.Equal(1m, r.Rows[0]["b"]);
            Assert.Equal(2m, r.Rows[1]["b"]);
            Assert.Equal(3m, r.Rows[2]["b"]);
        }

        [Fact]
        public async Task Select_ScalarSubquery_InProjection()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (v INT); INSERT INTO #t VALUES (10),(20),(30);");
            var r = await Q(ev, "SELECT (SELECT MAX(v) FROM #t) AS mx;");
            Assert.Equal(30m, r.Rows[0]["mx"]);
        }

        [Fact]
        public async Task Select_CaseExpression_AllBranchesCorrect()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (n INT); INSERT INTO #t VALUES (1),(2),(3);");
            var r = await Q(ev, "SELECT CASE WHEN n = 1 THEN 'one' WHEN n = 2 THEN 'two' ELSE 'other' END AS label FROM #t ORDER BY n;");
            Assert.Equal("one", r.Rows[0]["label"]);
            Assert.Equal("two", r.Rows[1]["label"]);
            Assert.Equal("other", r.Rows[2]["label"]);
        }

        // ─── Group 2: GROUP BY Correctness ───────────────────────────────────────

        [Fact]
        public async Task GroupBy_StringKey_CountCorrect()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (cat VARCHAR, v INT); INSERT INTO #t VALUES ('A',1),('B',2),('A',3),('B',4);");
            var r = await Q(ev, "SELECT cat, COUNT(*) AS n, SUM(v) AS s FROM #t GROUP BY cat ORDER BY cat;");
            TestHelpers.AssertRowsMatch(r,
                new object[] { "A", 2m, 4m },
                new object[] { "B", 2m, 6m }
            );
        }

        [Fact]
        public async Task GroupBy_DateKey_PreservedAsDateTime()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (d DATETIME, amt INT); INSERT INTO #t VALUES ('2024-01-01',10),('2024-01-01',20),('2024-01-02',30);");
            var r = await Q(ev, "SELECT d, SUM(amt) AS total FROM #t GROUP BY d ORDER BY d;");
            Assert.IsType<DateTime>(r.Rows[0]["d"]);
            Assert.Equal(new DateTime(2024, 1, 1), r.Rows[0]["d"]);
            Assert.Equal(30m, r.Rows[0]["total"]);
            Assert.Equal(30m, r.Rows[1]["total"]);
        }

        [Fact]
        public async Task GroupBy_DecimalSum_Accurate()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (cat VARCHAR, v DECIMAL); INSERT INTO #t VALUES ('X', 0.1), ('X', 0.2), ('X', 0.3);");
            var r = await Q(ev, "SELECT cat, SUM(v) AS s FROM #t GROUP BY cat;");
            Assert.Equal(0.6m, r.Rows[0]["s"]);
        }

        [Fact]
        public async Task GroupBy_MultiColumn_CorrectCombinations()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (a VARCHAR, b INT, v INT); INSERT INTO #t VALUES ('A',1,10),('A',2,20),('B',1,30),('A',1,40);");
            var r = await Q(ev, "SELECT a, b, SUM(v) AS s FROM #t GROUP BY a, b ORDER BY a, b;");
            Assert.Equal(3, r.Rows.Count);
            Assert.Equal(50m, r.Rows[0]["s"]); // A,1 -> 10+40
            Assert.Equal(20m, r.Rows[1]["s"]); // A,2
            Assert.Equal(30m, r.Rows[2]["s"]); // B,1
        }

        [Fact]
        public async Task GroupBy_Having_FiltersGroups()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (cat VARCHAR, v INT); INSERT INTO #t VALUES ('A',1),('A',2),('B',5),('C',1);");
            var r = await Q(ev, "SELECT cat, SUM(v) AS s FROM #t GROUP BY cat HAVING SUM(v) > 2 ORDER BY cat;");
            Assert.Equal(2, r.Rows.Count);
            Assert.Equal("A", r.Rows[0]["cat"]);
            Assert.Equal("B", r.Rows[1]["cat"]);
        }

        [Fact]
        public async Task GroupBy_NullKey_GroupedTogether()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (k VARCHAR, v INT); INSERT INTO #t VALUES (NULL,1),(NULL,2),('X',3);");
            var r = await Q(ev, "SELECT k, SUM(v) AS s FROM #t GROUP BY k ORDER BY k;");
            Assert.Equal(2, r.Rows.Count);
            Assert.Null(r.Rows[0]["k"]);
            Assert.Equal(3m, r.Rows[0]["s"]);
        }

        [Fact]
        public async Task GroupBy_WithOrderBy_Sorted()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (cat VARCHAR, v INT); INSERT INTO #t VALUES ('C',1),('A',2),('B',3);");
            var r = await Q(ev, "SELECT cat, SUM(v) AS s FROM #t GROUP BY cat ORDER BY cat DESC;");
            Assert.Equal("C", r.Rows[0]["cat"]);
            Assert.Equal("B", r.Rows[1]["cat"]);
            Assert.Equal("A", r.Rows[2]["cat"]);
        }

        // ─── Group 3: JOIN Correctness ────────────────────────────────────────────

        [Fact]
        public async Task Join_Inner_OnlyMatchingRows()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #a (id INT, name VARCHAR);
                CREATE TABLE #b (id INT, score INT);
                INSERT INTO #a VALUES (1,'Alice'),(2,'Bob'),(3,'Carol');
                INSERT INTO #b VALUES (1,100),(2,200),(4,400);
            ");
            var r = await Q(ev, "SELECT a.name, b.score FROM #a a JOIN #b b ON a.id = b.id ORDER BY a.id;");
            Assert.Equal(2, r.Rows.Count);
            Assert.Equal("Alice", r.Rows[0]["name"]);
            Assert.Equal(200m, r.Rows[1]["score"]);
        }

        [Fact]
        public async Task Join_Left_NullsForUnmatched()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #a (id INT, name VARCHAR);
                CREATE TABLE #b (id INT, score INT);
                INSERT INTO #a VALUES (1,'Alice'),(2,'Bob'),(3,'Carol');
                INSERT INTO #b VALUES (1,100),(2,200);
            ");
            var r = await Q(ev, "SELECT a.name, b.score FROM #a a LEFT JOIN #b b ON a.id = b.id ORDER BY a.id;");
            Assert.Equal(3, r.Rows.Count);
            Assert.Null(r.Rows[2]["score"]);
        }

        [Fact]
        public async Task Join_Right_NullsForUnmatched()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #a (id INT, name VARCHAR);
                CREATE TABLE #b (id INT, score INT);
                INSERT INTO #a VALUES (1,'Alice'),(2,'Bob');
                INSERT INTO #b VALUES (1,100),(3,300);
            ");
            var r = await Q(ev, "SELECT a.name, b.score FROM #a a RIGHT JOIN #b b ON a.id = b.id ORDER BY b.id;");
            Assert.Equal(2, r.Rows.Count);
            Assert.Equal("Alice", r.Rows[0]["name"]);
            Assert.Null(r.Rows[1]["name"]);
            Assert.Equal(300m, r.Rows[1]["score"]);
        }

        [Fact]
        public async Task Join_ThreeTables_CorrectCartesianFilter()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #emp (id INT, dept_id INT, name VARCHAR);
                CREATE TABLE #dept (id INT, dept_name VARCHAR);
                CREATE TABLE #loc (dept_id INT, city VARCHAR);
                INSERT INTO #emp VALUES (1,10,'Alice'),(2,20,'Bob');
                INSERT INTO #dept VALUES (10,'Eng'),(20,'Sales');
                INSERT INTO #loc VALUES (10,'NYC'),(20,'LA');
            ");
            var r = await Q(ev, @"
                SELECT e.name, d.dept_name, l.city
                FROM #emp e
                JOIN #dept d ON e.dept_id = d.id
                JOIN #loc l ON e.dept_id = l.dept_id
                ORDER BY e.id;
            ");
            Assert.Equal(2, r.Rows.Count);
            Assert.Equal("Alice", r.Rows[0]["name"]);
            Assert.Equal("Eng", r.Rows[0]["dept_name"]);
            Assert.Equal("NYC", r.Rows[0]["city"]);
        }

        [Fact]
        public async Task Join_Cross_ProducesCartesianProduct()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #colors (c VARCHAR); INSERT INTO #colors VALUES ('R'),('G');
                CREATE TABLE #sizes  (s VARCHAR); INSERT INTO #sizes  VALUES ('S'),('M'),('L');
            ");
            var r = await Q(ev, "SELECT c, s FROM #colors CROSS JOIN #sizes ORDER BY c, s;");
            Assert.Equal(6, r.Rows.Count);
        }

        // ─── Group 4: Window Functions ────────────────────────────────────────────

        [Fact]
        public async Task Window_RowNumber_PartitionedCorrectly()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (grp VARCHAR, v INT); INSERT INTO #t VALUES ('A',3),('A',1),('B',5),('A',2),('B',4);");
            var r = await Q(ev, "SELECT grp, v, ROW_NUMBER() OVER (PARTITION BY grp ORDER BY v) AS rn FROM #t ORDER BY grp, v;");
            var aRows = r.Rows.Where(row => row["grp"]?.ToString() == "A").ToList();
            Assert.Equal(1m, aRows[0]["rn"]);
            Assert.Equal(2m, aRows[1]["rn"]);
            Assert.Equal(3m, aRows[2]["rn"]);
        }

        [Fact]
        public async Task Window_Rank_TiesGetSameRank()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (v INT); INSERT INTO #t VALUES (10),(20),(20),(30);");
            var r = await Q(ev, "SELECT v, RANK() OVER (ORDER BY v) AS rk FROM #t ORDER BY v;");
            Assert.Equal(1m, r.Rows[0]["rk"]);
            Assert.Equal(2m, r.Rows[1]["rk"]);
            Assert.Equal(2m, r.Rows[2]["rk"]);
            Assert.Equal(4m, r.Rows[3]["rk"]);
        }

        [Fact]
        public async Task Window_RunningSum_Cumulative()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (v INT); INSERT INTO #t VALUES (10),(20),(30);");
            var r = await Q(ev, "SELECT v, SUM(v) OVER (ORDER BY v ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS running FROM #t ORDER BY v;");
            Assert.Equal(10m, r.Rows[0]["running"]);
            Assert.Equal(30m, r.Rows[1]["running"]);
            Assert.Equal(60m, r.Rows[2]["running"]);
        }

        [Fact]
        public async Task Window_LagLead_CorrectOffsets()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (v INT); INSERT INTO #t VALUES (10),(20),(30);");
            var r = await Q(ev, "SELECT v, LAG(v) OVER (ORDER BY v) AS prev, LEAD(v) OVER (ORDER BY v) AS nxt FROM #t ORDER BY v;");
            Assert.Null(r.Rows[0]["prev"]);
            Assert.Equal(10m, r.Rows[1]["prev"]);
            Assert.Equal(30m, r.Rows[1]["nxt"]);
            Assert.Null(r.Rows[2]["nxt"]);
        }

        [Fact]
        public async Task Window_TypePreservation_DecimalSumStaysDecimal()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (grp VARCHAR, v DECIMAL); INSERT INTO #t VALUES ('A',1.5),('A',2.5);");
            var r = await Q(ev, "SELECT grp, SUM(v) OVER (PARTITION BY grp) AS s FROM #t ORDER BY grp;");
            Assert.IsType<decimal>(r.Rows[0]["s"]);
            Assert.Equal(4.0m, r.Rows[0]["s"]);
        }

        // ─── Group 5: CTE and Subqueries ─────────────────────────────────────────

        [Fact]
        public async Task Cte_Simple_ProducesCorrectResult()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (v INT); INSERT INTO #t VALUES (1),(2),(3),(4),(5);");
            var r = await Q(ev, "WITH cte AS (SELECT v FROM #t WHERE v > 2) SELECT SUM(v) AS s FROM cte;");
            Assert.Equal(12m, r.Rows[0]["s"]);
        }

        [Fact]
        public async Task Cte_Recursive_CountsToTen()
        {
            var ev = Ev();
            var r = await Q(ev, @"
                WITH RECURSIVE nums AS (
                    SELECT 1 AS n
                    UNION ALL
                    SELECT n + 1 FROM nums WHERE n < 10
                )
                SELECT COUNT(*) AS cnt FROM nums;
            ");
            Assert.Equal(10m, r.Rows[0]["cnt"]);
        }

        [Fact]
        public async Task Subquery_In_FiltersCorrectly()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #orders (id INT, cust_id INT);
                CREATE TABLE #custs  (id INT, name VARCHAR);
                INSERT INTO #orders VALUES (1,10),(2,20),(3,10);
                INSERT INTO #custs  VALUES (10,'Alice'),(20,'Bob'),(30,'Carol');
            ");
            var r = await Q(ev, "SELECT name FROM #custs WHERE id IN (SELECT cust_id FROM #orders) ORDER BY name;");
            Assert.Equal(2, r.Rows.Count);
            Assert.Equal("Alice", r.Rows[0]["name"]);
            Assert.Equal("Bob", r.Rows[1]["name"]);
        }

        [Fact]
        public async Task Subquery_Exists_MatchesPresence()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #orders (id INT, cust_id INT);
                CREATE TABLE #custs  (id INT, name VARCHAR);
                INSERT INTO #orders VALUES (1,10),(2,10);
                INSERT INTO #custs  VALUES (10,'Alice'),(20,'Bob');
            ");
            var r = await Q(ev, "SELECT name FROM #custs c WHERE EXISTS (SELECT 1 FROM #orders o WHERE o.cust_id = c.id) ORDER BY name;");
            Assert.Single(r.Rows);
            Assert.Equal("Alice", r.Rows[0]["name"]);
        }

        // ─── Group 6: Flatfile I/O ────────────────────────────────────────────────

        [Fact]
        public async Task Flatfile_InsertCreatesFile()
        {
            string path = Path.Combine(Path.GetTempPath(), $"golden_csv_{Guid.NewGuid():N}.csv");
            try
            {
                var ev = Ev();
                await TestHelpers.Execute(ev, $@"
                    CREATE TABLE #src (id INT, name VARCHAR);
                    INSERT INTO #src VALUES (1,'Alice'),(2,'Bob');
                    CREATE CONNECTION csv_out AS FLATFILE('{EscPath(path)}', HEADER='ON');
                    INSERT INTO csv_out SELECT * FROM #src;
                ");
                Assert.True(File.Exists(path), "CSV file was not created");
            }
            finally { TryDelete(path); }
        }

        [Fact]
        public async Task Flatfile_RowCountMatches()
        {
            string path = Path.Combine(Path.GetTempPath(), $"golden_csv_{Guid.NewGuid():N}.csv");
            try
            {
                var ev = Ev();
                await TestHelpers.Execute(ev, $@"
                    CREATE TABLE #src (id INT, name VARCHAR);
                    INSERT INTO #src VALUES (1,'Alice'),(2,'Bob'),(3,'Carol');
                    CREATE CONNECTION csv_out AS FLATFILE('{EscPath(path)}', HEADER='ON');
                    INSERT INTO csv_out SELECT * FROM #src;
                ");
                var lines = await File.ReadAllLinesAsync(path);
                Assert.Equal(4, lines.Length); // header + 3 data rows
            }
            finally { TryDelete(path); }
        }

        [Fact]
        public async Task Flatfile_SelectReadsBack_CorrectRows()
        {
            string path = Path.Combine(Path.GetTempPath(), $"golden_csv_{Guid.NewGuid():N}.csv");
            try
            {
                var ev = Ev();
                await TestHelpers.Execute(ev, $@"
                    CREATE TABLE #src (id INT, name VARCHAR);
                    INSERT INTO #src VALUES (1,'Alice'),(2,'Bob'),(3,'Carol');
                    CREATE CONNECTION csv_out AS FLATFILE('{EscPath(path)}', HEADER='ON');
                    INSERT INTO csv_out SELECT * FROM #src;
                ");

                var ev2 = Ev();
                await TestHelpers.Execute(ev2, $@"
                    CREATE CONNECTION csv_in AS FLATFILE('{EscPath(path)}', HEADER='ON');
                ");
                var r = await Q(ev2, "SELECT * FROM csv_in ORDER BY id;");
                Assert.Equal(3, r.Rows.Count);
                Assert.Equal("Alice", r.Rows[0]["name"]);
                Assert.Equal("Carol", r.Rows[2]["name"]);
            }
            finally { TryDelete(path); }
        }

        [Fact]
        public async Task Flatfile_HeaderPresent_InFile()
        {
            string path = Path.Combine(Path.GetTempPath(), $"golden_csv_{Guid.NewGuid():N}.csv");
            try
            {
                var ev = Ev();
                await TestHelpers.Execute(ev, $@"
                    CREATE TABLE #src (product_id INT, product_name VARCHAR);
                    INSERT INTO #src VALUES (1,'Widget');
                    CREATE CONNECTION csv_out AS FLATFILE('{EscPath(path)}', HEADER='ON');
                    INSERT INTO csv_out SELECT * FROM #src;
                ");
                var firstLine = (await File.ReadAllLinesAsync(path))[0];
                Assert.Contains("product_id", firstLine, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("product_name", firstLine, StringComparison.OrdinalIgnoreCase);
            }
            finally { TryDelete(path); }
        }

        [Fact]
        public async Task Flatfile_TypeRoundtrip_StringAndNumeric()
        {
            string path = Path.Combine(Path.GetTempPath(), $"golden_csv_{Guid.NewGuid():N}.csv");
            try
            {
                var ev = Ev();
                await TestHelpers.Execute(ev, $@"
                    CREATE TABLE #src (id INT, label VARCHAR, price DECIMAL);
                    INSERT INTO #src VALUES (7,'Gadget',19.99);
                    CREATE CONNECTION csv_out AS FLATFILE('{EscPath(path)}', HEADER='ON');
                    INSERT INTO csv_out SELECT * FROM #src;
                ");

                var ev2 = Ev();
                await TestHelpers.Execute(ev2, $@"
                    CREATE CONNECTION csv_in AS FLATFILE('{EscPath(path)}', HEADER='ON');
                ");
                var r = await Q(ev2, "SELECT * FROM csv_in;");
                Assert.Single(r.Rows);
                Assert.Equal("Gadget", r.Rows[0]["label"]);
                Assert.Equal(19.99m, Convert.ToDecimal(r.Rows[0]["price"]));
            }
            finally { TryDelete(path); }
        }

        [Fact]
        public async Task Flatfile_FixedWidth_WriteAndReadRoundtrip()
        {
            string path = Path.Combine(Path.GetTempPath(), $"golden_fw_{Guid.NewGuid():N}.txt");
            try
            {
                var ev = Ev();
                await TestHelpers.Execute(ev, $@"
                    CREATE TABLE #Layout (EmpId CHAR(5), EmpName VARCHAR(20), Salary CHAR(10));
                    INSERT INTO #Layout VALUES ('00001','Alice','   75000');
                    INSERT INTO #Layout VALUES ('00002','Bob  ','   50000');
                    CREATE CONNECTION fw_out AS FLATFILE('{EscPath(path)}', FORMAT='FIXED', TEMPLATE=#Layout);
                    INSERT INTO fw_out SELECT EmpId, EmpName, Salary FROM #Layout;
                ");

                Assert.True(File.Exists(path), "Fixed-width file was not created");

                // Read back using same layout template
                var ev2 = Ev();
                await TestHelpers.Execute(ev2, $@"
                    CREATE TABLE #ReadLayout (EmpId CHAR(5), EmpName VARCHAR(20), Salary CHAR(10));
                    CREATE CONNECTION fw_in AS FLATFILE('{EscPath(path)}', FORMAT='FIXED', TEMPLATE=#ReadLayout, TRIM='ON', HEADER='ON');
                ");
                var r = await Q(ev2, "SELECT * FROM fw_in ORDER BY EmpId;");
                Assert.Equal(2, r.Rows.Count);
                Assert.Equal("00001", r.Rows[0]["EmpId"].ToString()!.Trim());
                Assert.Equal("Alice", r.Rows[0]["EmpName"].ToString()!.Trim());
                Assert.Equal("75000", r.Rows[0]["Salary"].ToString()!.Trim());
                Assert.Equal("00002", r.Rows[1]["EmpId"].ToString()!.Trim());
                Assert.Equal("Bob", r.Rows[1]["EmpName"].ToString()!.Trim());
            }
            finally { TryDelete(path); }
        }

        [Fact]
        public async Task Flatfile_FixedWidth_LineLengthMatchesSchema()
        {
            // Each line must be exactly sum-of-column-widths characters.
            // If the write path doesn't pad fields this test catches it immediately.
            string path = Path.Combine(Path.GetTempPath(), $"golden_fw_{Guid.NewGuid():N}.txt");
            const int expectedLineLength = 5 + 20 + 10; // EmpId + EmpName + Salary
            try
            {
                var ev = Ev();
                await TestHelpers.Execute(ev, $@"
                    CREATE TABLE #Layout (EmpId CHAR(5), EmpName VARCHAR(20), Salary CHAR(10));
                    INSERT INTO #Layout VALUES ('00001','Alice','   75000');
                    INSERT INTO #Layout VALUES ('00002','Bob','  50000');
                    CREATE CONNECTION fw_out AS FLATFILE('{EscPath(path)}', FORMAT='FIXED', TEMPLATE=#Layout);
                    INSERT INTO fw_out SELECT EmpId, EmpName, Salary FROM #Layout;
                ");

                var lines = await File.ReadAllLinesAsync(path);
                // Default HEADER='ON': 1 header + 2 data rows, all padded to the same fixed width
                Assert.Equal(3, lines.Length);
                foreach (var line in lines)
                    Assert.Equal(expectedLineLength, line.Length);
            }
            finally { TryDelete(path); }
        }

        [Fact]
        public async Task Flatfile_FixedWidth_FieldsAtCorrectOffsets()
        {
            // Verifies the byte layout: wrong padding shifts every field after the first.
            // EmpId: chars 0–4, EmpName: 5–24, Salary: 25–34
            string path = Path.Combine(Path.GetTempPath(), $"golden_fw_{Guid.NewGuid():N}.txt");
            try
            {
                var ev = Ev();
                await TestHelpers.Execute(ev, $@"
                    CREATE TABLE #Layout (EmpId CHAR(5), EmpName VARCHAR(20), Salary CHAR(10));
                    INSERT INTO #Layout VALUES ('A0001','Christopher','     99999');
                    CREATE CONNECTION fw_out AS FLATFILE('{EscPath(path)}', FORMAT='FIXED', TEMPLATE=#Layout);
                    INSERT INTO fw_out SELECT EmpId, EmpName, Salary FROM #Layout;
                ");

                var lines = await File.ReadAllLinesAsync(path);
                var header = lines[0];
                var data = lines[1]; // lines[0] is the fixed-width header row
                Assert.Equal(35, header.Length);
                Assert.Equal(35, data.Length);
                // Header: column names padded to their declared widths
                Assert.Equal("EmpId", header[..5].TrimEnd());
                Assert.Equal("EmpName", header[5..25].TrimEnd());
                Assert.Equal("Salary", header[25..35].TrimEnd());
                // Data: values padded to column widths, fields at exact offsets
                Assert.Equal("A0001", data[..5]);
                Assert.Equal("Christopher         ", data[5..25]);
                Assert.Equal("     99999", data[25..35]);
            }
            finally { TryDelete(path); }
        }

        // ─── Group 7: SET Operations and MERGE ───────────────────────────────────

        [Fact]
        public async Task SetOp_UnionAll_IncludesDuplicates()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #a (v INT); INSERT INTO #a VALUES (1),(2);
                CREATE TABLE #b (v INT); INSERT INTO #b VALUES (2),(3);
            ");
            var r = await Q(ev, "SELECT v FROM #a UNION ALL SELECT v FROM #b ORDER BY v;");
            Assert.Equal(4, r.Rows.Count);
        }

        [Fact]
        public async Task SetOp_Union_RemovesDuplicates()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #a (v INT); INSERT INTO #a VALUES (1),(2);
                CREATE TABLE #b (v INT); INSERT INTO #b VALUES (2),(3);
            ");
            var r = await Q(ev, "SELECT v FROM #a UNION SELECT v FROM #b ORDER BY v;");
            Assert.Equal(3, r.Rows.Count);
            Assert.Equal(1m, r.Rows[0]["v"]);
            Assert.Equal(3m, r.Rows[2]["v"]);
        }

        [Fact]
        public async Task SetOp_Except_ExcludesSecondSet()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #a (v INT); INSERT INTO #a VALUES (1),(2),(3);
                CREATE TABLE #b (v INT); INSERT INTO #b VALUES (2),(3);
            ");
            var r = await Q(ev, "SELECT v FROM #a EXCEPT SELECT v FROM #b;");
            Assert.Single(r.Rows);
            Assert.Equal(1m, r.Rows[0]["v"]);
        }

        [Fact]
        public async Task Merge_Matched_UpdatesRows()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #target (id INT, v INT);
                CREATE TABLE #source (id INT, v INT);
                INSERT INTO #target VALUES (1,10),(2,20);
                INSERT INTO #source VALUES (1,99),(3,30);
            ");
            await TestHelpers.Execute(ev, @"
                MERGE #target AS t
                USING #source AS s ON t.id = s.id
                WHEN MATCHED THEN UPDATE SET t.v = s.v
                WHEN NOT MATCHED THEN INSERT (id, v) VALUES (s.id, s.v);
            ");
            var r = await Q(ev, "SELECT * FROM #target ORDER BY id;");
            Assert.Equal(3, r.Rows.Count);
            Assert.Equal(99m, r.Rows[0]["v"]); // id=1 updated
            Assert.Equal(20m, r.Rows[1]["v"]); // id=2 unchanged
            Assert.Equal(30m, r.Rows[2]["v"]); // id=3 inserted
        }

        // ─── Group 8: Variables and Control Flow ─────────────────────────────────

        [Fact]
        public async Task Variable_InWhere_FiltersCorrectly()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (v INT); INSERT INTO #t VALUES (1),(2),(3),(4),(5);");
            await TestHelpers.Execute(ev, "DECLARE @threshold INT; SET @threshold = 3;");
            var r = await Q(ev, "SELECT v FROM #t WHERE v > @threshold ORDER BY v;");
            Assert.Equal(2, r.Rows.Count);
            Assert.Equal(4m, r.Rows[0]["v"]);
            Assert.Equal(5m, r.Rows[1]["v"]);
        }

        [Fact]
        public async Task ControlFlow_IfElse_CorrectBranch()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                DECLARE @res VARCHAR;
                IF 10 > 5 BEGIN
                    SET @res = 'yes';
                END ELSE BEGIN
                    SET @res = 'no';
                END
            ");
            Assert.Equal("yes", ev.Variables["@res"]);
        }

        [Fact]
        public async Task ControlFlow_While_AccumulatesCorrectly()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                DECLARE @i INT; SET @i = 0;
                DECLARE @sum INT; SET @sum = 0;
                WHILE @i < 5 BEGIN
                    SET @sum = @sum + @i;
                    SET @i = @i + 1;
                END
            ");
            Assert.Equal(10m, ev.Variables["@sum"]); // 0+1+2+3+4
        }

        [Fact]
        public async Task ControlFlow_TryCatch_CatchesError()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                DECLARE @caught VARCHAR; SET @caught = 'no';
                BEGIN TRY
                    SELECT 1/0 AS bad;
                END TRY
                BEGIN CATCH
                    SET @caught = 'yes';
                END CATCH
            ");
            Assert.Equal("yes", ev.Variables["@caught"]);
        }

        [Fact]
        public async Task ControlFlow_Foreach_IteratesAllRows()
        {
            var ev = Ev();
            await TestHelpers.Execute(ev, @"
                CREATE TABLE #items (item VARCHAR);
                INSERT INTO #items VALUES ('apple'),('banana'),('cherry');
                DECLARE @count INT; SET @count = 0;
                FOREACH @row IN (SELECT item FROM #items) BEGIN
                    SET @count = @count + 1;
                END
            ");
            Assert.Equal(3m, ev.Variables["@count"]);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static string EscPath(string path) => path.Replace("\\", "\\\\");

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
