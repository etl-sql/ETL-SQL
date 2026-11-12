using System;
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
/// <c>GREATEST</c> and <c>LEAST</c> compared values with the CLR's own ordering, which throws when
/// two numbers have different CLR types. <c>GENERATE_SERIES</c> read a <c>NULL</c> bound as 0.
/// </summary>
public sealed class StandardLibraryNullAuditTests
{
    [Theory]
    [InlineData("GREATEST(1, 2.5, 2)", "2.5")]
    [InlineData("LEAST(1, 2.5, 2)", "1")]
    [InlineData("GREATEST(LEN('abc'), 2.5)", "3")]
    [InlineData("LEAST(LEN('abc'), 2.5)", "2.5")]
    [InlineData("GREATEST(LEN('abc'), CAST(7 AS BIGINT), CAST(2.5 AS FLOAT))", "7")]
    [InlineData("LEAST(LEN('abc'), CAST(7 AS BIGINT), CAST(2.5 AS FLOAT))", "2.5")]
    [InlineData("GREATEST(CAST(1 AS FLOAT), 2)", "2")]
    [InlineData("LEAST(CAST(1 AS FLOAT), 2)", "1")]
    [InlineData("GREATEST(1, NULL, 3)", "3")]
    [InlineData("LEAST(1, NULL, 3)", "1")]
    [InlineData("GREATEST('apple', 'pear')", "pear")]
    [InlineData("LEAST('apple', 'pear')", "apple")]
    [InlineData("LEAST(CAST('2026-03-01' AS DATE), CAST('2026-01-15' AS DATE)) = CAST('2026-01-15' AS DATE)", "True")]
    public async Task GreatestAndLeastCompareAcrossNumericTypes(string expression, string expected)
    {
        var value = await Scalar($"SELECT {expression} AS v;");
        Assert.Equal(expected, value is IConvertible and not string and not bool and not DateTime
            ? Convert.ToDecimal(value).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            : TypeConverter.ToInvariantText(value));
    }

    [Theory]
    [InlineData("GREATEST(NULL, NULL)")]
    [InlineData("LEAST(NULL, NULL)")]
    [InlineData("GENERATE_SERIES(NULL, 5)")]
    [InlineData("GENERATE_SERIES(1, NULL)")]
    [InlineData("GENERATE_SERIES(1, 5, NULL)")]
    public async Task ANullArgumentGivesNull(string expression)
    {
        Assert.True(await Scalar($"SELECT {expression} AS v;") is null or DBNull);
    }

    [Theory]
    [InlineData("SELECT COUNT(*) AS v FROM UNNEST(GENERATE_SERIES(1, 5));", 5)]
    [InlineData("SELECT COUNT(*) AS v FROM UNNEST(GENERATE_SERIES(5, 1, -2));", 3)]
    [InlineData("SELECT COUNT(*) AS v FROM UNNEST(GENERATE_SERIES(5, 1));", 0)]
    [InlineData("SELECT LENGTH(GENERATE_SERIES(1, 10, 3)) AS v;", 4)]
    public async Task GenerateSeriesStillCounts(string sql, int expected)
    {
        Assert.Equal(expected, Convert.ToInt32(await Scalar(sql)));
    }

    [Fact]
    public async Task GenerateSeriesFailsOnAZeroStep()
    {
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => Scalar("SELECT GENERATE_SERIES(1, 5, 0) AS v;"));
        Assert.Contains("step", failure.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<object?> Scalar(string sql)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await evaluator.Evaluate(new Lexer(sql).TokenizeToScript());
        return Assert.Single(evaluator.LastResult!.Rows)["v"];
    }
}
