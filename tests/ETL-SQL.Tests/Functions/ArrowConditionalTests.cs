using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Functions
{
    /// <summary>
    /// The => arrow conditional — ETL-SQL dialect shorthand that lowers at parse time to CASE:
    /// cond => a : b is CASE WHEN cond THEN a ELSE b END, and chains flatten into one CASE with
    /// multiple WHEN arms. The final else is required; a dangling arrow is a syntax error.
    /// </summary>
    public class ArrowConditionalTests
    {
        private static Evaluator NewEvaluator() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        [Fact]
        public void Parser_LowersArrow_ToCaseExpression()
        {
            var script = new Parser(new Lexer("SELECT x > 0 => 'pos' : 'neg' AS s FROM #t;").Tokenize()).Parse();
            var select = Assert.IsType<SelectStatement>(script.Statements[0]);
            var caseExpr = Assert.IsType<CaseExpression>(select.Columns[0].Expression);
            Assert.Single(caseExpr.WhenClauses);
            Assert.NotNull(caseExpr.ElseResult);
        }

        [Fact]
        public void Parser_FlattensChain_IntoOneCaseWithMultipleWhens()
        {
            var script = new Parser(new Lexer(
                "SELECT score >= 90 => 'A' : score >= 80 => 'B' : score >= 70 => 'C' : 'F' AS grade FROM #t;").Tokenize()).Parse();
            var select = Assert.IsType<SelectStatement>(script.Statements[0]);
            var caseExpr = Assert.IsType<CaseExpression>(select.Columns[0].Expression);
            Assert.Equal(3, caseExpr.WhenClauses.Count); // one flat CASE, not nested CASEs
            Assert.NotNull(caseExpr.ElseResult);
        }

        [Fact]
        public void DanglingArrow_WithoutElse_IsASyntaxError()
        {
            var ex = Record.Exception(() =>
                new Parser(new Lexer("SELECT x > 0 => 'pos' FROM #t;").Tokenize()).Parse());
            // Whole-file Parse collects diagnostics rather than always throwing; accept either a
            // thrown SyntaxException or an error diagnostic — but it must NOT parse cleanly.
            if (ex == null)
            {
                var script = new Parser(new Lexer("SELECT x > 0 => 'pos' FROM #t;").Tokenize()).Parse();
                Assert.Contains(script.Diagnostics, d => d.Severity == ETL_SQL.Core.Common.DiagnosticSeverity.Error);
            }
        }

        [Fact]
        public async Task TwoBranch_SelectsCorrectly()
        {
            var ev = NewEvaluator();
            Assert.Equal("pos", (await ev.ExecuteValue("5 > 0 => 'pos' : 'neg'", new Row()))?.ToString());
            Assert.Equal("neg", (await ev.ExecuteValue("-5 > 0 => 'pos' : 'neg'", new Row()))?.ToString());
        }

        [Fact]
        public async Task Chain_WalksArmsInOrder_ThenElse()
        {
            var ev = NewEvaluator();
            Assert.Equal("B", (await ev.ExecuteValue("85 >= 90 => 'A' : 85 >= 80 => 'B' : 'F'", new Row()))?.ToString());
            Assert.Equal("F", (await ev.ExecuteValue("10 >= 90 => 'A' : 10 >= 80 => 'B' : 'F'", new Row()))?.ToString());
        }

        [Fact]
        public async Task BindsBelowOr_WholeBooleanIsTheCondition()
        {
            var ev = NewEvaluator();
            // (FALSE OR TRUE) => 'x' : 'y' — not FALSE OR (TRUE => 'x' : 'y')
            Assert.Equal("x", (await ev.ExecuteValue("1 = 2 OR 2 = 2 => 'x' : 'y'", new Row()))?.ToString());
        }

        [Fact]
        public async Task ShortCircuits_LikeCase()
        {
            var ev = NewEvaluator();
            // The untaken branch (1/0) is never evaluated — it lowered to CASE.
            Assert.Equal(42m, Convert.ToDecimal(await ev.ExecuteValue("1 = 1 => 42 : 1 / 0", new Row())));
        }

        [Fact]
        public async Task OverRows_InAQuery()
        {
            var ev = NewEvaluator();
            await TestHelpers.Execute(ev, @"
CREATE TABLE #t (v INT);
INSERT INTO #t VALUES (95);
INSERT INTO #t VALUES (85);
INSERT INTO #t VALUES (40);
SELECT v >= 90 => 'A' : v >= 80 => 'B' : 'F' AS grade INTO #out FROM #t;");

            var res = await ev.ExecuteQuery(TestHelpers.Parse("SELECT grade FROM #out ORDER BY grade;").Statements[0]).FirstAsync();
            Assert.Equal(new[] { "A", "B", "F" }, res.Rows.Select(r => r["grade"]?.ToString()).ToArray());
        }
    }
}
