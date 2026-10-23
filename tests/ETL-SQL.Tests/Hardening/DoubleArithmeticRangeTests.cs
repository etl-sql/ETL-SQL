using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Hardening
{
    /// <summary>
    /// Arithmetic converts to decimal, whose range ends near 7.9e28. A double beyond it failed that
    /// conversion, and "+" then concatenated the two values as text ("1E+301") while the other
    /// operators returned NULL. In-range values keep their decimal result.
    /// </summary>
    [Trait("CompatBreak", "0.22.0")]
    public class DoubleArithmeticRangeTests
    {
        [Fact]
        public void OutOfDecimalRangeDouble_IsStillArithmetic()
        {
            Assert.Equal(1e30 + 1, BinaryOperatorFactory.Execute(TokenType.PLUS, 1e30, 1m));
            Assert.Equal(1e30 - 1, BinaryOperatorFactory.Execute(TokenType.MINUS, 1e30, 1m));
            Assert.Equal(2e30, BinaryOperatorFactory.Execute(TokenType.STAR, 1e30, 2m));
            Assert.Equal(5e29, BinaryOperatorFactory.Execute(TokenType.SLASH, 1e30, 2m));
        }

        [Fact]
        public void NonFiniteDoubleResult_IsNull_LikeDecimalOverflow()
        {
            Assert.Null(BinaryOperatorFactory.Execute(TokenType.STAR, 1e300, 1e300));
            Assert.Null(BinaryOperatorFactory.Execute(TokenType.PLUS, double.NaN, 1m));
        }

        [Fact]
        public void InRangeValues_KeepDecimalResults() =>
            Assert.Equal(0.3m, BinaryOperatorFactory.Execute(TokenType.PLUS, 0.1, 0.2m));

        [Fact]
        public void TextStillConcatenates() =>
            Assert.Equal("ab", BinaryOperatorFactory.Execute(TokenType.PLUS, "a", "b"));
    }
}
