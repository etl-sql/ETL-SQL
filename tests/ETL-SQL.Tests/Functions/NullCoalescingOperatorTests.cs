using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Functions
{
    /// <summary>
    /// The ?? null-coalescing shorthand — pure parse-time sugar that lowers to COALESCE, so the
    /// evaluator (and lineage/pushdown) see a plain function call. CASE/COALESCE remain the
    /// documented portable standard; ?? is the ETL-SQL dialect convenience.
    /// </summary>
    public class NullCoalescingOperatorTests
    {
        private static Evaluator NewEvaluator() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        [Fact]
        public async Task Coalesce_NullLeft_ReturnsRight()
        {
            var ev = NewEvaluator();
            Assert.Equal(5m, Convert.ToDecimal(await ev.ExecuteValue("NULL ?? 5", new Row())));
        }

        [Fact]
        public async Task Coalesce_NonNullLeft_ShortCircuitsToLeft()
        {
            var ev = NewEvaluator();
            Assert.Equal(3m, Convert.ToDecimal(await ev.ExecuteValue("3 ?? 5", new Row())));
        }

        [Fact]
        public async Task Coalesce_Chains_LikeMultiArgCoalesce()
        {
            var ev = NewEvaluator();
            Assert.Equal("x", (await ev.ExecuteValue("NULL ?? NULL ?? 'x'", new Row()))?.ToString());
        }

        [Fact]
        public async Task Coalesce_BindsTighterThanComparison_OnBothSides()
        {
            var ev = NewEvaluator();
            // (NULL ?? 0) > -1  — not NULL ?? (0 > -1)
            Assert.Equal(true, await ev.ExecuteValue("NULL ?? 0 > -1", new Row()));
            // 5 = (NULL ?? 5) — ?? consumed on the right of a comparison too
            Assert.Equal(true, await ev.ExecuteValue("5 = NULL ?? 5", new Row()));
        }

        [Fact]
        public async Task Coalesce_BindsLooserThanArithmetic()
        {
            var ev = NewEvaluator();
            // (1 + NULL) ?? 7 — NULL propagates through +, then coalesces to 7
            Assert.Equal(7m, Convert.ToDecimal(await ev.ExecuteValue("1 + NULL ?? 7", new Row())));
        }

        [Fact]
        public async Task Coalesce_OverColumns_InAQuery()
        {
            var ev = NewEvaluator();
            await TestHelpers.Execute(ev, @"
CREATE TABLE #t (v INT);
INSERT INTO #t VALUES (NULL);
INSERT INTO #t VALUES (2);
SELECT v ?? 0 AS filled INTO #out FROM #t;");

            var res = await ev.ExecuteQuery(TestHelpers.Parse("SELECT * FROM #out ORDER BY filled;").Statements[0]).FirstAsync();
            Assert.Equal(2, res.Rows.Count);
            Assert.Equal(0m, Convert.ToDecimal(res.Rows[0]["filled"]));
            Assert.Equal(2m, Convert.ToDecimal(res.Rows[1]["filled"]));
        }
    }
}
