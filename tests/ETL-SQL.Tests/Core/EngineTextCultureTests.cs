using System;
using System.Globalization;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// The engine's text never follows the host's locale. A value that becomes text without a FORMAT
/// (concatenation, a string function's argument) reads like CAST AS STRING, casing is invariant,
/// and text read as a date means the same date on every host. Each case runs under en-US (the
/// Windows CI culture), de-DE (comma decimal, day-first dates) and tr-TR (dotted and dotless i).
/// </summary>
public sealed class EngineTextCultureTests
{
    public static TheoryData<string> Cultures => new() { "en-US", "de-DE", "tr-TR" };

    private static async Task<object?> EvalUnder(string culture, string expression)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var name = "@res_" + Guid.NewGuid().ToString("N");
            await evaluator.Evaluate(new Parser(new Lexer($"DECLARE {name} ANY; SET {name} = ({expression});").Tokenize()).Parse());
            return evaluator.Variables[name];
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // 'Run ' || GETDATE() gave '1/8/2024 12:00:00 AM' on Windows en-US and '08.01.2024 00:00:00'
    // on a German host, while CAST AS STRING already gave ISO text.
    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task ConcatenatingADateGivesTheCastText(string culture)
    {
        const string date = "CAST('2024-01-08 09:30:05' AS DATETIME)";
        Assert.Equal("Run 2024-01-08 09:30:05", await EvalUnder(culture, $"'Run ' || {date}"));
        Assert.Equal("Run 2024-01-08 09:30:05", await EvalUnder(culture, $"'Run ' + {date}"));
        Assert.Equal("Run 2024-01-08 09:30:05", await EvalUnder(culture, $"CONCAT('Run ', {date})"));
        Assert.Equal("a|2024-01-08 09:30:05|1.5", await EvalUnder(culture, $"CONCAT_WS('|', 'a', {date}, CAST(1.5 AS FLOAT))"));
        Assert.Equal("v1.5", await EvalUnder(culture, "'v' || CAST(1.5 AS FLOAT)"));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task StringFunctionsReadADateAsTheCastText(string culture)
    {
        Assert.Equal("2024-01-08", await EvalUnder(culture, "LEFT(CAST('2024-01-08' AS DATE), 10)"));
        Assert.Equal("2024/01/08 00:00:00", await EvalUnder(culture, "REPLACE(CAST('2024-01-08' AS DATE), '-', '/')"));
    }

    // Under tr-TR, culture casing turns 'i' into the dotted capital 'İ' and 'I' into the dotless 'ı'.
    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task CasingIsInvariant(string culture)
    {
        Assert.Equal("ISTANBUL", await EvalUnder(culture, "UPPER('istanbul')"));
        Assert.Equal("istanbul", await EvalUnder(culture, "LOWER('ISTANBUL')"));
        Assert.Equal("Istanbul Izmir", await EvalUnder(culture, "INITCAP('istanbul IZMIR')"));
    }

    // '08/01/2024' is 8 January to CAST on every host (day first). DATEPART and = used the host's
    // own parse, so en-US read it as 1 August.
    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task TextReadAsADateMeansTheSameDateEverywhere(string culture)
    {
        Assert.Equal(new DateTime(2024, 1, 8), await EvalUnder(culture, "CAST('08/01/2024' AS DATE)"));
        Assert.Equal(1m, Convert.ToDecimal(await EvalUnder(culture, "DATEPART(month, '08/01/2024')"), CultureInfo.InvariantCulture));
        Assert.Equal(1m, Convert.ToDecimal(await EvalUnder(culture, "EXTRACT(MONTH FROM '08/01/2024')"), CultureInfo.InvariantCulture));
        Assert.Equal("January", await EvalUnder(culture, "DATENAME(month, '08/01/2024')"));
        Assert.Equal("Monday", await EvalUnder(culture, "DATENAME(weekday, '08/01/2024')"));
        Assert.Equal(true, await EvalUnder(culture, "'08/01/2024' = CAST('2024-01-08' AS DATE)"));
    }

    // Owner 2026-11-10: slash dates are day first on every path, as CAST always read them. DATEDIFF,
    // comparisons and DATETIMEOFFSET read '08/01/2024' month first (1 August). A slash date whose
    // first number cannot be a month (01/25/2024) still reads month first.
    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task SlashDatesAreDayFirstOnEveryPath(string culture)
    {
        Assert.Equal(1m, Convert.ToDecimal(await EvalUnder(culture, "DATEDIFF(day, '08/01/2024', '2024-01-09')"), CultureInfo.InvariantCulture));
        Assert.Equal(true, await EvalUnder(culture, "'08/01/2024' < CAST('2024-01-09' AS DATE)"));
        Assert.Equal(new DateTime(2024, 1, 8, 10, 30, 0), await EvalUnder(culture, "CAST('08/01/2024 10:30' AS DATETIME)"));
        Assert.Equal(new DateTime(2024, 1, 25), await EvalUnder(culture, "CAST('01/25/2024' AS DATE)"));
        var offset = Assert.IsType<DateTimeOffset>(await EvalUnder(culture, "CAST('08/01/2024' AS DATETIMEOFFSET)"));
        Assert.Equal(new DateTime(2024, 1, 8), offset.DateTime);
    }

    // A German host read '8. Januar 2024' as a date while every other host refused it.
    [Theory]
    [MemberData(nameof(Cultures))]
    public void HostLanguageDateTextIsRefusedEverywhere(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            Assert.Throws<ExecutionException>(() => TypeConverter.Cast("8. Januar 2024", "DATE"));
            Assert.Throws<ExecutionException>(() => TypeConverter.Cast("8. Januar 2024", "DATETIME"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
