using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ETL_SQL.Tests.Hardening.Performance
{
    /// <summary>
    /// Measures what attaching <c>@expect</c> rules costs a statement, per rule shape, against the
    /// same statement with no rules. Reports rather than asserts a budget: the point is to know
    /// which shapes are expensive and why, and to catch a change that makes one of them much worse.
    ///
    /// <para>Run with <c>--filter "FullyQualifiedName~ColumnQualityCostTests"</c> and
    /// <c>-l "console;verbosity=detailed"</c> to see the table.</para>
    /// </summary>
    [Trait("Category", "Performance")]
    public class ColumnQualityCostTests(ITestOutputHelper output)
    {
        private const int Rows = 50_000;

        [Fact]
        public async Task ReportPerRuleShapeCost()
        {
            var shapes = new (string Name, string Rules)[]
            {
                ("(no rules)", ""),
                ("NOT NULL", "/* @expect: 'NOT NULL'; @fail: 'WARN'; */"),
                ("NOT BLANK", "/* @expect: 'NOT BLANK'; @fail: 'WARN'; */"),
                ("LENGTH BETWEEN", "/* @expect: 'LENGTH BETWEEN 1 AND 40'; @fail: 'WARN'; */"),
                ("IN (list)", "/* @expect: \"IN ('a','b','c')\"; @fail: 'WARN'; */"),
                ("MATCHES", "/* @expect: 'MATCHES ^v[0-9]+$'; @fail: 'WARN'; */"),
                ("CASTABLE AS DECIMAL", "/* @expect: 'CASTABLE AS DECIMAL(18,2)'; @fail: 'WARN'; */"),
                ("BETWEEN (expr)", "/* @expect: 'BETWEEN 0 AND 999999999'; @fail: 'WARN'; */"),
                ("EXPR", "/* @expect: 'EXPR Id >= 0'; @fail: 'WARN'; */"),
                ("UNIQUE", "/* @expect: 'UNIQUE'; @fail: 'WARN'; */"),
                ("UNIQUE_FIRST BY", "/* @expect: 'UNIQUE_FIRST BY Id'; @fail: 'WARN'; */"),
            };

            // One evaluator and one source table for every shape: building a service provider costs
            // more than the thing being measured, and charging it to whichever shape ran first is
            // how a baseline ends up slower than the work it is supposed to bound.
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

            // A wide-ish row so the per-row costs that scale with column count -- row identity,
            // capture schema -- are visible rather than rounded away.
            await Run(eval, $@"
                SELECT
                    Value AS Id,
                    'v' + CAST(Value AS VARCHAR) AS Code,
                    'abcdefghijklmnopqrst' AS Pad1,
                    'uvwxyzabcdefghijklmn' AS Pad2,
                    Value * 2 AS Doubled,
                    'a' AS Bucket
                INTO #src
                FROM UNNEST(GENERATE_SERIES(1, {Rows}));");

            // Warm the shared read/projection path before the first shape is timed. Without this the
            // rule-free baseline pays for first-touch of the whole pipeline and lands slower than
            // the shapes it is supposed to bound, which reads as "rules make it faster".
            for (var warm = 0; warm < 3; warm++)
                await Run(eval, $"SELECT Id, Code, Pad1, Pad2, Doubled, Bucket INTO #prewarm{warm} FROM #src;");

            var baseline = TimeSpan.Zero;
            output.WriteLine($"{Rows:N0} rows, 6 projected columns, min of 3 runs");
            output.WriteLine($"{"shape",-24} {"ms",8} {"vs none",10} {"alloc MB",10}");

            for (var i = 0; i < shapes.Length; i++)
            {
                var (name, rules) = shapes[i];
                var (elapsed, allocated) = await MeasureAsync(eval, rules, i);
                if (i == 0) baseline = elapsed;
                var factor = baseline > TimeSpan.Zero
                    ? $"{elapsed.TotalMilliseconds / baseline.TotalMilliseconds:F2}x"
                    : "-";
                output.WriteLine(
                    $"{name,-24} {elapsed.TotalMilliseconds,8:F0} {factor,10} {allocated / 1024.0 / 1024.0,10:F1}");
            }
        }

        private static async Task<(TimeSpan Elapsed, long Allocated)> MeasureAsync(
            Evaluator eval, string ruleTag, int index)
        {
            string Sql(string target) => ruleTag.Length == 0
                ? $"measure:\nSELECT Id, Code, Pad1, Pad2, Doubled, Bucket INTO {target} FROM #src;"
                : $@"
                measure:
                SELECT
                    Id {ruleTag},
                    Code, Pad1, Pad2, Doubled, Bucket
                INTO {target} FROM #src
                ON FAILURE WARN;";

            // Warm the path once so JIT is not charged to the shape.
            await Run(eval, Sql($"#warm{index}"));

            var best = TimeSpan.MaxValue;
            long bestAllocated = 0;
            for (var run = 0; run < 3; run++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                var before = GC.GetTotalAllocatedBytes(precise: true);
                var sw = Stopwatch.StartNew();
                await Run(eval, Sql($"#dst{index}_{run}"));
                sw.Stop();
                var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

                if (sw.Elapsed >= best) continue;
                best = sw.Elapsed;
                bestAllocated = allocated;
            }
            return (best, bestAllocated);
        }

        /// <summary>
        /// What QUALIFY costs on top of the same windowed query filtered in an outer SELECT. The
        /// alias bridge QUALIFY needs (so it can say <c>QUALIFY rnk &lt;= 1</c>) runs per row, so
        /// this is where a loop-invariant computation left inside it shows up.
        /// </summary>
        [Fact]
        public async Task ReportQualifyCost()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await Run(eval, $@"
                SELECT
                    Value AS Id,
                    Value % 500 AS CustomerId,
                    Value % 97 AS OrderDate,
                    'abcdefghijklmnopqrst' AS Pad1
                INTO #orders
                FROM UNNEST(GENERATE_SERIES(1, {Rows}));");

            const string Windowed = @"
                SELECT Id, CustomerId,
                       ROW_NUMBER() OVER (PARTITION BY CustomerId ORDER BY OrderDate DESC) AS rnk
                INTO {0}
                FROM #orders";

            var cases = new (string Name, string Sql)[]
            {
                ("window only", $"{Windowed};"),
                ("window + QUALIFY", $"{Windowed} QUALIFY rnk <= 3;"),
            };

            output.WriteLine($"{Rows:N0} rows, 500 partitions, min of 3 runs");
            output.WriteLine($"{"shape",-24} {"ms",8} {"alloc MB",10}");

            for (var i = 0; i < cases.Length; i++)
            {
                var (name, sql) = cases[i];
                await Run(eval, string.Format(sql, $"#qwarm{i}"));

                var best = TimeSpan.MaxValue;
                long bestAllocated = 0;
                for (var run = 0; run < 3; run++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    var before = GC.GetTotalAllocatedBytes(precise: true);
                    var sw = Stopwatch.StartNew();
                    await Run(eval, string.Format(sql, $"#q{i}_{run}"));
                    sw.Stop();
                    var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
                    if (sw.Elapsed >= best) continue;
                    best = sw.Elapsed;
                    bestAllocated = allocated;
                }

                output.WriteLine(
                    $"{name,-24} {best.TotalMilliseconds,8:F0} {bestAllocated / 1024.0 / 1024.0,10:F1}");
            }
        }

        private static Task Run(Evaluator eval, string sql) =>
            eval.Evaluate(new Lexer(sql).TokenizeToScript());
    }
}
