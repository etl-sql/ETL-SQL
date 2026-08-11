using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.Tests.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine
{
    /// <summary>
    /// A window aggregate must be computed per logical partition, including once the data is large
    /// enough to spill.
    ///
    /// <para>The external window engine hash-partitions rows into buckets, and one of its spill
    /// paths scanned a whole bucket once and wrote that aggregate onto every row in it. That is
    /// sound only when a bucket is the logical partition. With a PARTITION BY of higher cardinality
    /// than the bucket count — the ordinary case — a bucket holds many partitions, so every row
    /// received the bucket's count instead of its own. No error: just wrong numbers.</para>
    ///
    /// <para>These drive it through the engine at a spill threshold low enough to take that path,
    /// so they fail on the defect rather than on a plan choice.</para>
    /// </summary>
    public class WindowPartitionSpillCorrectnessTests
    {
        [Fact]
        public async Task CountOverAHighCardinalityPartition_CountsThePartitionNotTheBucket()
        {
            // Every key is distinct, so every count must be 1. A bucket-wide aggregate would report
            // however many rows shared the bucket.
            var eval = NewSpillingEvaluator();
            await Seed(eval, rows: 400, distinctKeys: 400);

            var result = await TestHelpers.ReadAllRows(eval.ExecuteQuery(
                TestHelpers.Parse("SELECT k, COUNT(*) OVER (PARTITION BY k) AS n FROM #w;").Statements[0]));

            Assert.Equal(400, result.Rows.Count);
            Assert.All(result.Rows, row => Assert.Equal(1m, row["n"]));
        }

        [Fact]
        public async Task SumOverAPartition_IsThePartitionsSum()
        {
            // Two rows per key, each valued 1, so every SUM is 2 regardless of how the keys were
            // distributed across buckets.
            var eval = NewSpillingEvaluator();
            await Seed(eval, rows: 400, distinctKeys: 200);

            var result = await TestHelpers.ReadAllRows(eval.ExecuteQuery(
                TestHelpers.Parse("SELECT k, SUM(v) OVER (PARTITION BY k) AS total FROM #w;").Statements[0]));

            Assert.Equal(400, result.Rows.Count);
            Assert.All(result.Rows, row => Assert.Equal(2m, row["total"]));
        }

        [Fact]
        public async Task AnUnpartitionedWindow_StillAggregatesTheWholeResult()
        {
            // The case the bucket-wide path remains valid for, so restricting it did not silently
            // cost correctness the other way.
            var eval = NewSpillingEvaluator();
            await Seed(eval, rows: 400, distinctKeys: 400);

            var result = await TestHelpers.ReadAllRows(eval.ExecuteQuery(
                TestHelpers.Parse("SELECT k, COUNT(*) OVER () AS n FROM #w;").Statements[0]));

            Assert.Equal(400, result.Rows.Count);
            Assert.All(result.Rows, row => Assert.Equal(400m, row["n"]));
        }

        private static Evaluator NewSpillingEvaluator()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            // Low enough that the bucket passes the deep-spill threshold, which is what selects the
            // path under test. Pinned here rather than inherited so the test means the same thing
            // under the default and the spill lane.
            eval.WindowSpillThreshold = 10;
            return eval;
        }

        private static async Task Seed(Evaluator eval, int rows, int distinctKeys)
        {
            var values = string.Join(", ",
                Enumerable.Range(0, rows).Select(i => $"('k{i % distinctKeys}', 1)"));
            await TestHelpers.Execute(eval, $@"
                CREATE TABLE #w (k VARCHAR(20), v INT);
                INSERT INTO #w (k, v) VALUES {values};");
        }
    }
}
