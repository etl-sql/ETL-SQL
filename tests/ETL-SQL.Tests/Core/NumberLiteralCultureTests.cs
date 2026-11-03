using System;
using System.Globalization;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using Xunit;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// Script numbers are culture-invariant: a host running under de-DE (where '.' groups thousands)
/// must still read 1.234 as one point two three four. Hex literals never crash the lexer.
/// </summary>
public sealed class NumberLiteralCultureTests
{
    private static T UnderCulture<T>(string name, Func<T> action)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(name);
        try { return action(); }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static Expression ParseExpression(string input) =>
        new Parser(new Lexer(input).Tokenize()).ParseExpression();

    [Fact]
    public void DecimalLiteral_IsCultureInvariant()
    {
        var literal = UnderCulture("de-DE", () => Assert.IsType<LiteralExpression>(ParseExpression("1.234")));
        Assert.Equal(1.234m, literal.Value);
    }

    [Theory]
    [InlineData("DECIMAL", "1.5", 1.5)]
    [InlineData("FLOAT", "1.5", 1.5)]
    [InlineData("INT", "1234", 1234)]
    public void CastFromString_IsCultureInvariant(string type, string text, double expected)
    {
        var value = UnderCulture("de-DE", () => TypeConverter.Cast(text, type));
        Assert.Equal(expected, Convert.ToDouble(value, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SoftEqualityAndComparison_AreCultureInvariant()
    {
        UnderCulture("de-DE", () =>
        {
            Assert.True(EvaluationUtils.IsSoftEqual("1.5", 1.5m));
            Assert.False(EvaluationUtils.IsSoftEqual("1.5", 15m));
            Assert.True(EvaluationUtils.CompareConstants(1.5m, "1.6") < 0);
            return 0;
        });
    }

    [Fact]
    public void BareHexPrefix_IsASyntaxErrorNotACrash()
    {
        Assert.Throws<SyntaxException>(() => new Lexer("SELECT 0x").Tokenize());
        Assert.Throws<SyntaxException>(() => new Lexer("SELECT 0x;").Tokenize());
    }

    [Fact]
    public void HexLiteral_IsUnsignedAndRangeChecked()
    {
        var token = new Lexer("0xFFFFFFFFFFFFFFFF").Tokenize().First();
        Assert.Equal("18446744073709551615", token.Value);
        Assert.Equal("255", new Lexer("0xff").Tokenize().First().Value);
        Assert.Throws<SyntaxException>(() => new Lexer("0x" + new string('F', 30)).Tokenize());
    }
}
