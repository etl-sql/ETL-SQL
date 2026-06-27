using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Benchmarks;
using Xunit;

namespace ETL_SQL.Benchmarks.Tests
{
    public class BenchSetupTest
    {
        [Fact]
        public async Task TestSetup()
        {
            var bench = new TpcHBenchmarks();
            await bench.Setup();
            Assert.NotNull(bench);
        }

        [Fact]
        public async Task TestRunQ1()
        {
            var bench = new TpcHBenchmarks();
            await bench.Setup();
            await bench.RunQ1();
            var result = bench.LastResult;
            Assert.NotNull(result);
            // Q1 groups by (l_returnflag, l_linestatus) — seeder produces R, A, N flags × F, O statuses → up to 6 groups
            Assert.True(result.Rows.Count > 0, "Q1 should return at least one pricing summary group");
            Assert.Contains(result.ColumnNames, c => c.Equals("sum_qty", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task TestRunQ6()
        {
            var bench = new TpcHBenchmarks();
            await bench.Setup();
            await bench.RunQ6();
            var result = bench.LastResult;
            Assert.NotNull(result);
            Assert.True(result.Rows.Count > 0, "Q6 should return a revenue row (seeder covers 1994 date range)");
            Assert.Contains(result.ColumnNames, c => c.Equals("revenue", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task TestRunQ3()
        {
            var bench = new TpcHBenchmarks();
            await bench.Setup();
            await bench.RunQ3();
            var result = bench.LastResult;
            Assert.NotNull(result);
            Assert.Contains(result.ColumnNames, c => c.Equals("revenue", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.ColumnNames, c => c.Equals("o_orderdate", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.ColumnNames, c => c.Equals("o_shippriority", StringComparison.OrdinalIgnoreCase));
            Assert.True(result.Rows.Count > 0, "Q3 should return at least one shipping-priority group");
        }

        [Fact]
        public async Task TestRunQ5()
        {
            var bench = new TpcHBenchmarks();
            await bench.Setup();
            await bench.RunQ5();
            var result = bench.LastResult;
            Assert.NotNull(result);
            Assert.Contains(result.ColumnNames, c => c.Equals("n_name", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.ColumnNames, c => c.Equals("revenue", StringComparison.OrdinalIgnoreCase));
            // May return 0 rows at SF=0.01 if no customer/supplier share an Asian nation in 1994 — that is a valid result.
        }

        [Fact]
        public async Task TestRunQ12()
        {
            var bench = new TpcHBenchmarks();
            await bench.Setup();
            await bench.RunQ12();
            var result = bench.LastResult;
            Assert.NotNull(result);
            Assert.Contains(result.ColumnNames, c => c.Equals("l_shipmode", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.ColumnNames, c => c.Equals("high_line_count", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.ColumnNames, c => c.Equals("low_line_count", StringComparison.OrdinalIgnoreCase));
            Assert.True(result.Rows.Count > 0, "Q12 should return MAIL and/or SHIP rows from 1994 receipt dates");
        }

        [Fact]
        public async Task TestRunQ14()
        {
            var bench = new TpcHBenchmarks();
            await bench.Setup();
            await bench.RunQ14();
            var result = bench.LastResult;
            Assert.NotNull(result);
            Assert.Contains(result.ColumnNames, c => c.Equals("promo_revenue", StringComparison.OrdinalIgnoreCase));
            Assert.True(result.Rows.Count == 1, "Q14 returns a single scalar promotion percentage");
        }

        // ── SelectShape benchmark sanity tests ─────────────────────────────────────

        [Fact]
        public async Task SelectShape_Setup()
        {
            var bench = new SelectShapeBenchmarks();
            await bench.Setup();
            Assert.NotNull(bench);
        }

        [Fact]
        public async Task SelectShape_SimpleFilter()
        {
            var bench = new SelectShapeBenchmarks();
            await bench.Setup();
            await bench.SimpleFilter();
            var result = bench.LastResult;
            Assert.NotNull(result);
            Assert.True(result.Rows.Count > 0, "SimpleFilter should return rows where score > 50");
            Assert.Contains(result.ColumnNames, c => c.Equals("id", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.ColumnNames, c => c.Equals("score", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task SelectShape_Distinct()
        {
            var bench = new SelectShapeBenchmarks();
            await bench.Setup();
            await bench.Distinct();
            var result = bench.LastResult;
            Assert.NotNull(result);
            Assert.Equal(5, result.Rows.Count); // seeder has exactly 5 categories
            Assert.Contains(result.ColumnNames, c => c.Equals("category", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task SelectShape_LimitedSort()
        {
            var bench = new SelectShapeBenchmarks();
            await bench.Setup();
            await bench.LimitedSort();
            var result = bench.LastResult;
            Assert.NotNull(result);
            Assert.Equal(100, result.Rows.Count);
            // Verify descending order
            var scores = result.Rows.Select(r => Convert.ToInt32(r["score"])).ToList();
            for (int i = 1; i < scores.Count; i++)
                Assert.True(scores[i - 1] >= scores[i], "Scores should be in descending order");
        }

        [Fact]
        public async Task SelectShape_WindowQualify()
        {
            var bench = new SelectShapeBenchmarks();
            await bench.Setup();
            await bench.WindowQualify();
            var result = bench.LastResult;
            Assert.NotNull(result);
            Assert.Equal(5, result.Rows.Count); // one top row per category
            Assert.Contains(result.ColumnNames, c => c.Equals("category", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.ColumnNames, c => c.Equals("score", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task SelectShape_UnionAll()
        {
            var bench = new SelectShapeBenchmarks();
            await bench.Setup();
            await bench.UnionAll();
            var result = bench.LastResult;
            Assert.NotNull(result);
            Assert.True(result.Rows.Count > 0, "UnionAll should return rows from both branches");
            Assert.Contains(result.ColumnNames, c => c.Equals("id", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.ColumnNames, c => c.Equals("score", StringComparison.OrdinalIgnoreCase));
        }

        // ── SELECT pipeline spill-streaming benchmark sanity tests ────────────────

        [Fact]
        public async Task SelectPipeline_ExternalAggregateLimit()
        {
            var bench = new SelectPipelineBenchmarks(5_000);
            await bench.Setup();
            await bench.ExternalAggregateLimit();
            var result = bench.LastResult;
            Assert.NotNull(result);
            Assert.Equal(5, result.Rows.Count);
            Assert.Contains(result.ColumnNames, c => c.Equals("Grp", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.ColumnNames, c => c.Equals("Total", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task SelectPipeline_ExternalAggregateOrderLimit()
        {
            var bench = new SelectPipelineBenchmarks(5_000);
            await bench.Setup();
            await bench.ExternalAggregateOrderLimit();
            var result = bench.LastResult;
            Assert.NotNull(result);
            Assert.Equal(5, result.Rows.Count);
            Assert.Contains(result.ColumnNames, c => c.Equals("Grp", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.ColumnNames, c => c.Equals("Total", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task SelectPipeline_ExternalAggregateFullOrder()
        {
            var bench = new SelectPipelineBenchmarks(5_000);
            await bench.Setup();
            await bench.ExternalAggregateFullOrder();
            var result = bench.LastResult;
            Assert.NotNull(result);
            Assert.Equal(100, result.Rows.Count);
            var totals = result.Rows.Select(r => Convert.ToDecimal(r["Total"])).ToList();
            Assert.Equal(totals.OrderByDescending(v => v), totals);
        }

        [Fact]
        public async Task SelectPipeline_ExternalWindowQualifyLimit()
        {
            var bench = new SelectPipelineBenchmarks(5_000);
            await bench.Setup();
            await bench.ExternalWindowQualifyLimit();
            var result = bench.LastResult;
            Assert.NotNull(result);
            Assert.Equal(10, result.Rows.Count);
            Assert.Contains(result.ColumnNames, c => c.Equals("rn", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task SelectPipeline_ExternalWindowRunningState()
        {
            var bench = new SelectPipelineBenchmarks(5_000);
            await bench.Setup();
            await bench.ExternalWindowRunningState();
            var result = bench.LastResult;
            Assert.NotNull(result);
            Assert.Equal(100, result.Rows.Count);
            Assert.Contains(result.ColumnNames, c => c.Equals("RunningTotal", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.ColumnNames, c => c.Equals("PreviousTwo", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task SelectPipeline_RemainingMaterializationBoundaries()
        {
            var bench = new SelectPipelineBenchmarks(5_000);
            await bench.Setup();

            await bench.JoinAggregate();
            Assert.Equal(100, bench.LastResult!.Rows.Count);

            await bench.JoinWindow();
            Assert.Equal(200, bench.LastResult!.Rows.Count);

            await bench.HighCardinalityDistinct();
            Assert.Equal(5_000, bench.LastResult!.Rows.Count);

            await bench.TopPercent();
            Assert.Equal(500, bench.LastResult!.Rows.Count);

            await bench.WithTies();
            Assert.True(bench.LastResult!.Rows.Count >= 100);
        }

        [Fact]
        public async Task RuntimeServices_SetupAndOperations()
        {
            var bench = new RuntimeServiceBenchmarks();
            await bench.Setup();
            try
            {
                await bench.SnapshotSave();
                Assert.NotNull(await bench.SnapshotLoad());
                Assert.Contains("Runtime Benchmark", bench.ReportRender());
                await bench.OrchestratorSlot();
            }
            finally
            {
                await bench.Cleanup();
            }
        }

        // ── TPC-H snapshot test ─────────────────────────────────────────────────

        /// <summary>
        /// Snapshot test: runs Q1 at SF=0.1 with the fixed rng seed and asserts the result is identical
        /// to the baseline captured in tests/tpch_data/expected/q1_sf01.json.
        /// On first run (file absent) the result is written as the new baseline and the test passes.
        /// </summary>
        [Fact]
        public async Task TestQ1DeterministicAtSF01()
        {
            var bench = new TpcHBenchmarks(0.1);
            await bench.Setup();
            await bench.RunQ1();
            var result = bench.LastResult;
            Assert.NotNull(result);
            Assert.True(result.Rows.Count > 0, "Q1 at SF=0.1 must return at least one pricing-summary group");

            // Serialize to a list-of-dicts (column name → string value) for stable JSON comparison.
            var rows = result.Rows.Select(row =>
                result.ColumnNames.ToDictionary(
                    col => col,
                    col => row[col]?.ToString() ?? ""
                )
            ).ToList();

            var json = JsonSerializer.Serialize(
                new { query = "Q1", scaleFactor = 0.1, groups = rows },
                new JsonSerializerOptions { WriteIndented = true }
            );

            // Walk up from the assembly bin directory to find tests/tpch_data/expected.
            var asmDir = Path.GetDirectoryName(typeof(BenchSetupTest).Assembly.Location)!;
            var expected = Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "..", "tpch_data", "expected", "q1_sf01.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(expected)!);

            if (!File.Exists(expected))
            {
                File.WriteAllText(expected, json);
                return; // baseline written — pass on first run
            }

            var baseline = File.ReadAllText(expected);
            Assert.Equal(baseline.Trim(), json.Trim());
        }
    }
}
