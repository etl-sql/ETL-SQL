using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// The right operand of LIKE, ESCAPE, BETWEEN, a bare IN and IS DISTINCT FROM is read at the same
/// level as the right operand of <c>=</c>: concatenation, <c>??</c> and shifts are allowed, as
/// <c>docs/grammar.ebnf</c> says. They were read at the additive level only, so
/// <c>name LIKE '%' || @q || '%'</c> failed to parse.
/// </summary>
public sealed class ComparisonOperandParsingTests
{
    [Theory]
    [InlineData("SELECT 'abc' LIKE 'a' || '%' AS v;", true)]
    [InlineData("SELECT 'abc' NOT LIKE '%' || 'z' || '%' AS v;", true)]
    [InlineData("SELECT 'a%c' LIKE 'a!%' || 'c' ESCAPE NULL ?? '!' AS v;", true)]
    [InlineData("SELECT 5 BETWEEN NULL ?? 1 AND 2 + 3 AS v;", true)]
    [InlineData("SELECT 5 BETWEEN 1 AND NULL ?? 4 AS v;", false)]
    [InlineData("SELECT 'b' BETWEEN 'a' || '' AND 'c' AS v;", true)]
    [InlineData("SELECT 1 IS DISTINCT FROM NULL ?? 1 AS v;", false)]
    [InlineData("SELECT 8 = 1 << 3 AS v;", true)]
    [InlineData("SELECT 8 BETWEEN 1 << 3 AND 9 AS v;", true)]
    public async Task AnOperandTakesConcatenationCoalesceAndShift(string sql, bool expected)
    {
        Assert.Equal(expected, ToBool(await Scalar(sql)));
    }

    [Fact]
    public async Task BetweenStillSplitsItsBoundsAtAnd()
    {
        Assert.True(ToBool(await Scalar("SELECT 2 BETWEEN 1 AND 3 AND 1 = 1 AS v;")));
        Assert.False(ToBool(await Scalar("SELECT 2 BETWEEN 1 AND 3 AND 1 = 0 AS v;")));
    }

    [Fact]
    public void ABareNotExistsIsStillTheNegatedExistsNode()
    {
        var select = Assert.IsType<SelectStatement>(Parse("SELECT 1 WHERE NOT EXISTS (SELECT 1);"));
        var exists = Assert.IsType<ExistsExpression>(select.WhereClause);
        Assert.True(exists.IsNot);
    }

    [Fact]
    public async Task NotNegatesAComparisonThatStartsWithExists()
    {
        // NOT binds looser than =, so this is NOT (EXISTS (...) = FALSE). It failed to parse.
        var select = Assert.IsType<SelectStatement>(Parse("SELECT NOT EXISTS (SELECT 1) = FALSE AS v;"));
        Assert.IsType<UnaryExpression>(select.Columns[0].Expression);
        Assert.True(ToBool(await Scalar("SELECT NOT EXISTS (SELECT 1) = FALSE AS v;")));
    }

    private static Statement Parse(string sql)
    {
        var script = new ETL_SQL.Core.Parser.Parser(new ETL_SQL.Core.Parser.Lexer(sql).Tokenize()).Parse();
        Assert.DoesNotContain(script.Diagnostics, d => d.Severity == ETL_SQL.Core.Common.DiagnosticSeverity.Error);
        return Assert.Single(script.Statements);
    }

    private static bool ToBool(object? value) => value switch
    {
        bool b => b,
        null => false,
        _ => System.Convert.ToDecimal(value) != 0m,
    };

    private static async Task<object?> Scalar(string sql)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await evaluator.Evaluate(new ETL_SQL.Core.Parser.Lexer(sql).TokenizeToScript());
        return evaluator.LastResult!.Rows.Single()["v"];
    }
}
