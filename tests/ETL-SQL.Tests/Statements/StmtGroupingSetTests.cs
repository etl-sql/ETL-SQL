using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ETL_SQL.Tests.Statements
{
    /// <summary>
    /// Tests for GROUP BY GROUPING SETS, ROLLUP, and CUBE.
    /// </summary>
    public class GroupingSetTests
    {
        private readonly ITestOutputHelper _output;

        public GroupingSetTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static Evaluator NewEvaluator() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        private static Script Parse(string sql) =>
            new Parser(new Lexer(sql).Tokenize()).Parse();

        // ─── Helper: build a 2-col (Region, Product) DataTable in a temp table ─────

        private static async Task PopulateSalesTable(Evaluator e)
        {
            var script = @"
                CREATE TABLE #sales (Region VARCHAR(50), Product VARCHAR(50), Amount DECIMAL);
                INSERT INTO #sales VALUES ('East', 'Apples', 10);
                INSERT INTO #sales VALUES ('East', 'Bananas', 20);
                INSERT INTO #sales VALUES ('West', 'Apples', 30);
                INSERT INTO #sales VALUES ('West', 'Bananas', 40);
                INSERT INTO #sales VALUES ('West', 'Cherries', 50);
            ";
            e.RedirectOutput = true;
            await e.Evaluate(Parse(script));
        }

        // ─── Parser unit tests ───────────────────────────────────────────────────

        [Fact]
        public void Parse_GroupBy_Rollup()
        {
            var script = Parse("SELECT Region, SUM(Amount) FROM #t GROUP BY ROLLUP(Region);");
            var stmt = Assert.IsType<SelectStatement>(script.Statements[0]);
            Assert.NotNull(stmt.GroupingSet);
            Assert.Equal(GroupingSetType.Rollup, stmt.GroupingSet!.Type);
            Assert.Single(stmt.GroupingSet.GroupSets);
            Assert.Single(stmt.GroupingSet.GroupSets[0]);
        }

        [Fact]
        public void Parse_GroupBy_Cube()
        {
            var script = Parse("SELECT Region, Product, SUM(Amount) FROM #t GROUP BY CUBE(Region, Product);");
            var stmt = Assert.IsType<SelectStatement>(script.Statements[0]);
            Assert.NotNull(stmt.GroupingSet);
            Assert.Equal(GroupingSetType.Cube, stmt.GroupingSet!.Type);
            Assert.Equal(2, stmt.GroupingSet.GroupSets[0].Count);
        }

        [Fact]
        public void Parse_GroupBy_GroupingSets()
        {
            var script = Parse("SELECT Region, Product, SUM(Amount) FROM #t GROUP BY GROUPING SETS((Region, Product),(Region),());");
            var stmt = Assert.IsType<SelectStatement>(script.Statements[0]);
            Assert.NotNull(stmt.GroupingSet);
            Assert.Equal(GroupingSetType.GroupingSets, stmt.GroupingSet!.Type);
            Assert.Equal(3, stmt.GroupingSet.GroupSets.Count);
            Assert.Equal(2, stmt.GroupingSet.GroupSets[0].Count);
            Assert.Single(stmt.GroupingSet.GroupSets[1]);
            Assert.Empty(stmt.GroupingSet.GroupSets[2]);
        }

        [Fact]
        public void Parse_PlainGroupBy_Unchanged()
        {
            var script = Parse("SELECT Region, SUM(Amount) FROM #t GROUP BY Region;");
            var stmt = Assert.IsType<SelectStatement>(script.Statements[0]);
            Assert.Null(stmt.GroupingSet);
            Assert.NotNull(stmt.GroupBy);
            Assert.Single(stmt.GroupBy!);
        }

        // ─── Execution tests ─────────────────────────────────────────────────────

        [Fact]
        public async Task Execute_Rollup_ProducesDetailAndSubtotals()
        {
            var e = NewEvaluator();
            await PopulateSalesTable(e);

            // ROLLUP(Region, Product) should produce:
            //   Per (Region, Product) — 5 rows
            //   Per Region subtotals  — 2 rows (East, West)
            //   Grand total           — 1 row
            // Total = 8 rows
            var sql = "SELECT Region, Product, SUM(Amount) AS Total FROM #sales GROUP BY ROLLUP(Region, Product) ORDER BY Region, Product;";
            await e.Evaluate(Parse(sql));

            Assert.NotNull(e.LastResult);
            _output.WriteLine($"ROLLUP rows: {e.LastResult!.TotalRowsMatched}");
            Assert.Equal(8, e.LastResult.TotalRowsMatched);
        }

        [Fact]
        public async Task Execute_Rollup_SingleColumn_GrandTotalRow()
        {
            var e = NewEvaluator();
            await PopulateSalesTable(e);

            // ROLLUP(Region) → 2 region rows + 1 grand total = 3
            var sql = "SELECT Region, SUM(Amount) AS Total FROM #sales GROUP BY ROLLUP(Region);";
            await e.Evaluate(Parse(sql));

            Assert.NotNull(e.LastResult);
            _output.WriteLine($"ROLLUP(Region) rows: {e.LastResult!.TotalRowsMatched}");
            Assert.Equal(3, e.LastResult.TotalRowsMatched);

            // The grand total row should have NULL Region and Total = 150
            var grandTotal = e.LastResult.Rows
                .FirstOrDefault(r => r["Region"] == null || r["Region"]?.ToString() == "");
            Assert.NotNull(grandTotal);
            Assert.Equal(150m, Convert.ToDecimal(grandTotal!["Total"]));
        }

        [Fact]
        public async Task Execute_Cube_ProducesAllCombinations()
        {
            var e = NewEvaluator();
            await PopulateSalesTable(e);

            // CUBE(Region, Product): 2^2 = 4 subsets: (R,P),(R),(P),()
            // (East,Apples)=10, (East,Bananas)=20, (West,Apples)=30, (West,Bananas)=40, (West,Cherries)=50
            // Per (R,P): 5 rows
            // Per Region only: East=30, West=120 → 2 rows
            // Per Product only: Apples=40, Bananas=60, Cherries=50 → 3 rows
            // Grand total: 150 → 1 row
            // Total = 11 rows
            var sql = "SELECT Region, Product, SUM(Amount) AS Total FROM #sales GROUP BY CUBE(Region, Product);";
            await e.Evaluate(Parse(sql));

            Assert.NotNull(e.LastResult);
            _output.WriteLine($"CUBE rows: {e.LastResult!.TotalRowsMatched}");
            Assert.Equal(11, e.LastResult.TotalRowsMatched);
        }

        [Fact]
        public async Task Execute_GroupingSets_ExplicitSets()
        {
            var e = NewEvaluator();
            await PopulateSalesTable(e);

            // GROUPING SETS((Region, Product),(Region),()): 5 + 2 + 1 = 8
            var sql = "SELECT Region, Product, SUM(Amount) AS Total FROM #sales GROUP BY GROUPING SETS((Region, Product),(Region),());";
            await e.Evaluate(Parse(sql));

            Assert.NotNull(e.LastResult);
            _output.WriteLine($"GROUPING SETS rows: {e.LastResult!.TotalRowsMatched}");
            Assert.Equal(8, e.LastResult.TotalRowsMatched);
        }

        [Fact]
        public async Task Execute_GroupingSets_GrandTotalAmountCorrect()
        {
            var e = NewEvaluator();
            await PopulateSalesTable(e);

            var sql = "SELECT Region, SUM(Amount) AS Total FROM #sales GROUP BY GROUPING SETS((Region),());";
            await e.Evaluate(Parse(sql));

            Assert.NotNull(e.LastResult);
            // Rows: East=30, West=120, GrandTotal=150 → 3 rows
            Assert.Equal(3, e.LastResult!.TotalRowsMatched);

            var grandTotal = e.LastResult.Rows.FirstOrDefault(r => r["Region"] == null || r["Region"]?.ToString() == "");
            Assert.NotNull(grandTotal);
            Assert.Equal(150m, Convert.ToDecimal(grandTotal!["Total"]));
        }
    }
}
