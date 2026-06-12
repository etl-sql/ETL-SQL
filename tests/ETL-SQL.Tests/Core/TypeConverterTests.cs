using System;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using Xunit;

namespace ETL_SQL.Tests.Core
{
    public class TypeConverterTests
    {
        [Theory]
        [InlineData("123", "INT", 123)]
        [InlineData("123.45", "DECIMAL", 123.45)]
        [InlineData("true", "BIT", true)]
        [InlineData("2023-01-01", "DATE", "2023-01-01")]
        [InlineData("612495f8-8e32-4c4d-993e-17e18a244e6b", "GUID", "612495f8-8e32-4c4d-993e-17e18a244e6b")]
        public void TestSuccessfulCasts(object value, string typeName, object expected)
        {
            var result = TypeConverter.Cast(value, typeName);

            if (typeName == "DATE")
            {
                Assert.Equal(DateTime.Parse(expected.ToString()), (DateTime)result);
            }
            else if (typeName == "GUID")
            {
                Assert.Equal(Guid.Parse(expected.ToString()), (Guid)result);
            }
            else if (typeName == "DECIMAL" || typeName == "INT")
            {
                // INT and DECIMAL both store as decimal at runtime — the engine uses decimal as its
                // universal numeric type. INT vs DECIMAL matters for schema validation, not CLR type.
                Assert.Equal(Convert.ToDecimal(expected), Convert.ToDecimal(result));
            }
            else
            {
                Assert.Equal(expected, result);
            }
        }

        [Theory]
        [InlineData("not-a-number", "INT")]
        [InlineData("not-a-date", "DATE")]
        [InlineData("not-a-guid", "GUID")]
        [InlineData("not-a-bool", "BIT")]
        public void TestFailingCasts(object value, string typeName)
        {
            var ex = Assert.Throws<ExecutionException>(() => TypeConverter.Cast(value, typeName));
            Assert.Contains($"Failed to cast value '{value}' to type '{typeName}'", ex.Message);
        }

        [Fact]
        public void TestUnknownTypeReturnsOriginalValue()
        {
            var value = "some-value";
            var result = TypeConverter.Cast(value, "UNKNOWN_TYPE");
            Assert.Equal(value, result);
        }

        [Fact]
        public void TestVarbinaryCast()
        {
            var base64 = "SGVsbG8="; // "Hello"
            var result = TypeConverter.Cast(base64, "VARBINARY");
            Assert.IsType<byte[]>(result);
            Assert.Equal("Hello", System.Text.Encoding.UTF8.GetString((byte[])result));
        }
    }
}
