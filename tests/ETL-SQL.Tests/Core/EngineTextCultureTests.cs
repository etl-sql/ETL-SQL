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

    private static async Task<object?> EvalUnder(string culture, string expression, string prefix = "",
        System.Collections.Generic.Dictionary<string, string?>? config = null)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider(config).GetRequiredService<Evaluator>();
            var name = "@res_" + Guid.NewGuid().ToString("N");
            await evaluator.Evaluate(new Parser(new Lexer($"{prefix} DECLARE {name} ANY; SET {name} = ({expression});").Tokenize()).Parse());
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
        Assert.Equal("2024/01/08", await EvalUnder(culture, "REPLACE(CAST('2024-01-08' AS DATE), '-', '/')"));
        Assert.Equal("2024-01-08", await EvalUnder(culture, "CAST(CAST('2024-01-08' AS DATE) AS STRING)"));
        Assert.Equal("2024-01-08 00:00:00", await EvalUnder(culture, "CAST(CAST('2024-01-08 00:00:00' AS DATETIME) AS STRING)"));
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

    // Oracle and Postgres start a word after any character that is not a letter or digit, so
    // INITCAP('o''neil') is O'Neil. .NET title case kept the apostrophe inside the word (O'neil).
    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task InitcapStartsAWordAfterAnyNonAlphanumeric(string culture)
    {
        Assert.Equal("O'Neil Mcdonald-Smith A1b C_D", await EvalUnder(culture, "INITCAP('o''neil MCDONALD-smith a1B c_d')"));
        Assert.Equal("  Hello,World 2nd", await EvalUnder(culture, "INITCAP('  hello,WORLD 2ND')"));
    }

    // Owner 2026-11-10: slash dates are month first by default, as SQL Server (us_english) and
    // PostgreSQL read them: '08/01/2024' is 1 August. CAST read it day first (8 January) and
    // DATEPART and = used the host's own parse, so the answer depended on the path and the host.
    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task SlashDatesAreMonthFirstByDefaultOnEveryPath(string culture)
    {
        Assert.Equal(new DateOnly(2024, 8, 1), await EvalUnder(culture, "CAST('08/01/2024' AS DATE)"));
        Assert.Equal(8m, Convert.ToDecimal(await EvalUnder(culture, "DATEPART(month, '08/01/2024')"), CultureInfo.InvariantCulture));
        Assert.Equal(8m, Convert.ToDecimal(await EvalUnder(culture, "EXTRACT(MONTH FROM '08/01/2024')"), CultureInfo.InvariantCulture));
        Assert.Equal("August", await EvalUnder(culture, "DATENAME(month, '08/01/2024')"));
        Assert.Equal("Thursday", await EvalUnder(culture, "DATENAME(weekday, '08/01/2024')"));
        Assert.Equal(true, await EvalUnder(culture, "'08/01/2024' = CAST('2024-08-01' AS DATE)"));
        Assert.Equal(1m, Convert.ToDecimal(await EvalUnder(culture, "DATEDIFF(day, '08/01/2024', '2024-08-02')"), CultureInfo.InvariantCulture));
        Assert.Equal(true, await EvalUnder(culture, "'08/01/2024' < CAST('2024-08-02' AS DATE)"));
        Assert.Equal(new DateTime(2024, 8, 1, 10, 30, 0), await EvalUnder(culture, "CAST('08/01/2024 10:30' AS DATETIME)"));
        Assert.Equal(new DateTime(2024, 8, 1, 22, 30, 0), await EvalUnder(culture, "CAST('8/1/2024 10:30 PM' AS DATETIME)"));
        var offset = Assert.IsType<DateTimeOffset>(await EvalUnder(culture, "CAST('08/01/2024' AS DATETIMEOFFSET)"));
        Assert.Equal(new DateTime(2024, 8, 1), offset.DateTime);
        // A first number that cannot be a month reads day first rather than failing.
        Assert.Equal(new DateOnly(2024, 1, 25), await EvalUnder(culture, "CAST('25/01/2024' AS DATE)"));
        Assert.Equal(new DateOnly(2024, 8, 1), await EvalUnder(culture, "CAST('8/1/24' AS DATE)"));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task SetDateformatDmyReadsSlashDatesDayFirstOnEveryPath(string culture)
    {
        foreach (var set in new[] { "SET DATEFORMAT DMY;", "SET DATEFORMAT = 'dmy';", "SET DATEFORMAT dmy" })
        {
            Assert.Equal(new DateOnly(2024, 1, 8), await EvalUnder(culture, "CAST('08/01/2024' AS DATE)", set));
            Assert.Equal(1m, Convert.ToDecimal(await EvalUnder(culture, "DATEPART(month, '08/01/2024')", set), CultureInfo.InvariantCulture));
            Assert.Equal(1m, Convert.ToDecimal(await EvalUnder(culture, "DATEDIFF(day, '08/01/2024', '2024-01-09')", set), CultureInfo.InvariantCulture));
            Assert.Equal(true, await EvalUnder(culture, "'08/01/2024' = CAST('2024-01-08' AS DATE)", set));
            Assert.Equal(new DateOnly(2024, 1, 25), await EvalUnder(culture, "CAST('01/25/2024' AS DATE)", set));
        }
        // ISO text means the same date under either order.
        Assert.Equal(new DateOnly(2024, 8, 1), await EvalUnder(culture, "CAST('2024-08-01' AS DATE)", "SET DATEFORMAT DMY;"));
        // A later SET in the same script changes it back.
        Assert.Equal(new DateOnly(2024, 8, 1), await EvalUnder(culture, "CAST('08/01/2024' AS DATE)", "SET DATEFORMAT DMY; SET DATEFORMAT MDY;"));
    }

    [Fact]
    public async Task AppsettingsSetsTheDefaultAndSetOverridesIt()
    {
        var dmy = new System.Collections.Generic.Dictionary<string, string?> { ["Engine:DateFormat"] = "DMY" };
        Assert.Equal(new DateOnly(2024, 1, 8), await EvalUnder("en-US", "CAST('08/01/2024' AS DATE)", config: dmy));
        Assert.Equal(new DateOnly(2024, 8, 1), await EvalUnder("en-US", "CAST('08/01/2024' AS DATE)", "SET DATEFORMAT MDY;", dmy));

        var bad = new System.Collections.Generic.Dictionary<string, string?> { ["Engine:DateFormat"] = "YDM" };
        var error = await Assert.ThrowsAnyAsync<Exception>(() => EvalUnder("en-US", "1", config: bad));
        Assert.Contains("Engine:DateFormat must be MDY or DMY", error.ToString());
    }

    [Fact]
    public async Task AnUnknownDateformatIsRefused()
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(() => EvalUnder("en-US", "1", "SET DATEFORMAT YDM;"));
        Assert.Contains("Valid values: MDY, DMY", error.Message);
    }

    // DATEFORMAT is not reserved: a column named DateFormat still works.
    [Fact]
    public async Task AColumnNamedDateFormatStillParses() =>
        Assert.Equal("x", await EvalUnder("en-US", "SELECT DateFormat FROM (SELECT 'x' AS DateFormat) t"));

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
