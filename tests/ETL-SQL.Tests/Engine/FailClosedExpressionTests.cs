using System;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine;

/// <summary>
/// An expression the engine cannot compute must fail. These returned NULL, so bad input read as
/// missing data and the script carried on.
/// </summary>
public sealed class FailClosedExpressionTests
{
    [Theory]
    [InlineData("'abc' - 5", "both operands must be numbers")]
    [InlineData("'abc' * 5", "both operands must be numbers")]
    [InlineData("10 / 'abc'", "both operands must be numbers")]
    [InlineData("10 % 'abc'", "both operands must be numbers")]
    [InlineData("79228162514264337593543950335 * 10", "Arithmetic overflow")]
    [InlineData("EXTRACT(FORTNIGHT FROM CAST('2026-01-01' AS DATETIME))", "does not know the field")]
    [InlineData("EXTRACT(YEAR FROM 'not a date')", "as a date")]
    public async Task ExpressionFails(string expression, string message)
    {
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => Scalar(expression));
        Assert.Contains(message, failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("'abc' + 5", "abc5")]
    [InlineData("'7' - 5", "2")]
    [InlineData("7 * 6", "42")]
    [InlineData("NULL - 5", null)]
    [InlineData("EXTRACT(YEAR FROM CAST('2026-03-04' AS DATETIME))", "2026")]
    [InlineData("EXTRACT(YEAR FROM NULL)", null)]
    public async Task ExpressionStillAnswers(string expression, string? expected)
    {
        Assert.Equal(expected, TypeConverter.ToInvariantText(await Scalar(expression)));
    }

    [Fact]
    public void DateEqualityAndOrderingAgreeWithinOneSecond()
    {
        var early = new DateTime(2026, 1, 1, 10, 0, 0, 100);
        var late = new DateTime(2026, 1, 1, 10, 0, 0, 900);

        Assert.True(EvaluationUtils.CompareConstants(early, late) < 0);
        Assert.False(EvaluationUtils.IsSoftEqual(early, late), "Two times that order differently cannot also be equal.");
        Assert.True(EvaluationUtils.IsSoftEqual(early, new DateTime(2026, 1, 1, 10, 0, 0, 100)));
        Assert.True(EvaluationUtils.IsSoftEqual(new DateTime(2026, 1, 1), "2026-01-01"));
    }

    [Fact]
    public void DatabaseNullIsNullInTextAndCasts()
    {
        Assert.Null(TypeConverter.ToInvariantText(DBNull.Value));
        Assert.Null(EvaluationUtils.CastToType(DBNull.Value, "INT"));
        Assert.Null(EvaluationUtils.CastToType(DBNull.Value, "VARCHAR"));
        Assert.Null(BinaryOperatorFactory.Execute(ETL_SQL.Core.Parser.TokenType.PLUS, 5m, DBNull.Value));
    }

    private static async Task<object?> Scalar(string expression)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await evaluator.Evaluate(new Lexer($"SELECT {expression} AS v;").TokenizeToScript());
        var value = Assert.Single(evaluator.LastResult!.Rows)["v"];
        return value == DBNull.Value ? null : value;
    }
}
