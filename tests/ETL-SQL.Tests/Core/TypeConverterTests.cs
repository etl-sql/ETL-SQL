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

        [Fact]
        public void TestDateTimeOffsetAndPrecisionCasts()
        {
            var offsetStr = "2026-07-02 10:05:51.1234567 -05:00";
            var resultOffset = TypeConverter.Cast(offsetStr, "DATETIMEOFFSET");
            Assert.IsType<DateTimeOffset>(resultOffset);
            var dto = (DateTimeOffset)resultOffset;
            Assert.Equal(2026, dto.Year);
            Assert.Equal(7, dto.Month);
            Assert.Equal(2, dto.Day);
            Assert.Equal(10, dto.Hour);
            Assert.Equal(5, dto.Minute);
            Assert.Equal(51, dto.Second);
            Assert.Equal(1234567, dto.Ticks % 10000000);
            Assert.Equal(TimeSpan.FromHours(-5), dto.Offset);

            var resultDt3 = TypeConverter.Cast(offsetStr, "DATETIME(3)");
            Assert.IsType<DateTime>(resultDt3);
            var dt3 = (DateTime)resultDt3;
            Assert.Equal(123, dt3.Millisecond);
            Assert.Equal(0, (dt3.Ticks % 10000000) % 10000);

            var resultDto5 = TypeConverter.Cast(offsetStr, "DATETIMEOFFSET(5)");
            Assert.IsType<DateTimeOffset>(resultDto5);
            var dto5 = (DateTimeOffset)resultDto5;
            Assert.Equal(1234500, dto5.Ticks % 10000000);
            Assert.Equal(TimeSpan.FromHours(-5), dto5.Offset);
        }
    }
}
