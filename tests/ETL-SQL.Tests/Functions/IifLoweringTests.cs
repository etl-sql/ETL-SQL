using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Functions
{
    /// <summary>
    /// IIF(a, b, c) is lowered to CASE WHEN a THEN b ELSE c END at parse time — matching T-SQL,
    /// where IIF is defined as CASE shorthand. That gives it short-circuit evaluation (previously
    /// the runtime function evaluated all three arguments eagerly) and lets it push down to every
    /// connector as universal CASE.
    /// </summary>
    public class IifLoweringTests
    {
        private static Evaluator NewEvaluator() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        [Fact]
        public void Parser_LowersIif_ToCaseExpression()
        {
            var script = new Parser(new Lexer("SELECT IIF(x > 0, 'pos', 'neg') AS s FROM #t;").Tokenize()).Parse();
            var select = Assert.IsType<SelectStatement>(script.Statements[0]);
            var expr = select.Columns[0].Expression;
            var caseExpr = Assert.IsType<CaseExpression>(expr);
            Assert.Single(caseExpr.WhenClauses);
            Assert.NotNull(caseExpr.ElseResult);
        }

        [Fact]
        public async Task Iif_ShortCircuits_UntakenBranchNeverEvaluated()
        {
            var ev = NewEvaluator();
            // Before the lowering this threw: the runtime function evaluated 1/0 eagerly.
            var res = await ev.ExecuteValue("IIF(1 = 1, 42, 1 / 0)", new Row());
            Assert.Equal(42m, Convert.ToDecimal(res));
        }

        [Fact]
        public async Task Iif_TrueAndFalseBranches_SelectCorrectly()
        {
            var ev = NewEvaluator();
            Assert.Equal("yes", (await ev.ExecuteValue("IIF(2 > 1, 'yes', 'no')", new Row()))?.ToString());
            Assert.Equal("no", (await ev.ExecuteValue("IIF(2 < 1, 'yes', 'no')", new Row()))?.ToString());
        }

        [Fact]
        public async Task Iif_NullCondition_SelectsFalseBranch_LikeCase()
        {
            var ev = NewEvaluator();
            // NULL/UNKNOWN condition → ELSE branch, standard CASE behavior (and T-SQL IIF behavior).
            Assert.Equal("no", (await ev.ExecuteValue("IIF(NULL = 1, 'yes', 'no')", new Row()))?.ToString());
        }

        [Fact]
        public async Task Iif_OverRows_InAQuery()
        {
            var ev = NewEvaluator();
            await TestHelpers.Execute(ev, @"
CREATE TABLE #t (v INT);
INSERT INTO #t VALUES (0);
INSERT INTO #t VALUES (5);
SELECT IIF(v = 0, 0, 100 / v) AS safe_div INTO #out FROM #t;");

            var res = await ev.ExecuteQuery(TestHelpers.Parse("SELECT * FROM #out ORDER BY safe_div;").Statements[0]).FirstAsync();
            Assert.Equal(2, res.Rows.Count);
            Assert.Equal(0m, Convert.ToDecimal(res.Rows[0]["safe_div"]));
            Assert.Equal(20m, Convert.ToDecimal(res.Rows[1]["safe_div"]));
        }

        [Fact]
        public async Task Iif_Nested_ComposesLikeCase()
        {
            var ev = NewEvaluator();
            var res = await ev.ExecuteValue("IIF(1 = 2, 'a', IIF(3 > 2, 'b', 'c'))", new Row());
            Assert.Equal("b", res?.ToString());
        }
    }
}
