using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine;

/// <summary>
/// A number compared with text that is not a number fell back to comparing their text, so
/// <c>9 &lt; 10</c>, <c>10 &lt; '5x'</c> and <c>'5x' &lt; 9</c> all held and a sort had no right answer.
/// Values now order by kind first: numbers, then dates, then other text.
/// </summary>
public sealed class MixedTypeOrderingTests
{
    public static IEnumerable<object?[]> Values() =>
        new object?[] { 9m, 10m, "5x", "#1", "abc", "10", "9.5", 2.5d, 7L, "", "N/A", new DateTime(2026, 1, 2), "2026-01-01", "zebra", "(4)" }
            .Select(v => new[] { v });

    [Fact]
    public void TheOrderHasNoCycles()
    {
        var values = Values().Select(v => v[0]).ToArray();
        foreach (var a in values)
            foreach (var b in values)
            {
                Assert.Equal(-Math.Sign(EvaluationUtils.CompareConstants(a, b)), Math.Sign(EvaluationUtils.CompareConstants(b, a)));
                foreach (var c in values)
                    if (EvaluationUtils.CompareConstants(a, b) < 0 && EvaluationUtils.CompareConstants(b, c) < 0)
                        Assert.True(EvaluationUtils.CompareConstants(a, c) < 0, $"{a} < {b} < {c}, but not {a} < {c}.");
            }
    }

    [Theory]
    [InlineData("5x")]
    [InlineData("#1")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("N/A")]
    public void ANumberOrdersBeforeTextThatIsNotANumber(string text)
    {
        foreach (var number in new object[] { 9m, 10m, 2.5d, 7L, "10", "-3" })
        {
            Assert.True(EvaluationUtils.CompareConstants(number, text) < 0, $"{number} should order before '{text}'.");
            Assert.True(EvaluationUtils.CompareConstants(text, number) > 0, $"'{text}' should order after {number}.");
        }
    }

    [Fact]
    public void ADateOrdersAfterNumbersAndBeforeOtherText()
    {
        var date = new DateTime(2026, 1, 2);
        Assert.True(EvaluationUtils.CompareConstants(5m, date) < 0);
        Assert.True(EvaluationUtils.CompareConstants(date, "abc") < 0);
        Assert.True(EvaluationUtils.CompareConstants("2026-01-01", date) < 0);
        Assert.True(EvaluationUtils.CompareConstants("2026-01-03", "abc") < 0);
    }

    [Fact]
    public void SameKindStillOrdersAsBefore()
    {
        Assert.True(EvaluationUtils.CompareConstants(9m, 10m) < 0);
        Assert.True(EvaluationUtils.CompareConstants("9", "10") < 0);
        Assert.True(EvaluationUtils.CompareConstants(2.5d, 7L) < 0);
        Assert.Equal(0, EvaluationUtils.CompareConstants("5", 5m));
        Assert.True(EvaluationUtils.CompareConstants("apple", "Pear") < 0);
        Assert.Equal(0, EvaluationUtils.CompareConstants("abc", "ABC"));
        Assert.True(EvaluationUtils.CompareConstants("ABC", "abc", caseSensitive: true) != 0);
        Assert.True(EvaluationUtils.CompareConstants(null, 1m) < 0);
    }

    [Fact]
    public async Task OrderByPutsNumbersBeforeText()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await evaluator.Evaluate(new Lexer(
            "CREATE TABLE #m (v VARCHAR); INSERT INTO #m VALUES ('10'), ('5x'), ('9'), ('#1'), ('abc'), ('2'); " +
            "SELECT v FROM #m ORDER BY v;").TokenizeToScript());
        Assert.Equal("2,9,10,#1,5x,abc", string.Join(",", evaluator.LastResult!.Rows.Select(r => TypeConverter.ToInvariantText(r["v"]))));
    }
}
