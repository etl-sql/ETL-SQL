using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

/// <summary>
/// A report's cell and parameter text never follows the host's locale. Chart labels came out as
/// '1/1/2026 12:00:00 AM' on Windows en-US and '01.01.2026 00:00:00' on a German host, and
/// SOURCE_MIN/MAX could not read the German text back. Owner 2026-11-10: ISO text, with the time
/// left off when it is midnight.
/// </summary>
public sealed class ReportValueTextCultureTests
{
    public static TheoryData<string> Cultures => new() { "en-US", "de-DE", "tr-TR" };

    private const string Script = """
        SELECT CAST('2026-01-01' AS DATE) AS OnDay, CAST('2026-01-01 14:30:05' AS DATETIME) AS AtTime, CAST(1.5 AS FLOAT) AS Amount INTO #t;
        DECLARE @since DATE;
        SET @since = CAST('2026-05-14' AS DATE);
        DECLARE @at DATETIME;
        SET @at = CAST('2026-05-14 07:01:11' AS DATETIME);
        CREATE VISUAL Grid AS TABLE (SOURCE = #t);
        """;

    private static async Task<ReportManifest> BuildUnder(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            evaluator.RedirectOutput = true;
            evaluator.DisplayExecuteTree = false;
            await evaluator.Evaluate(new Parser(new Lexer(Script).Tokenize(), Script).Parse());
            return await new ManifestBuilder(evaluator).BuildAsync("report.rptsql");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task CellTextIsIsoOnEveryHost(string culture)
    {
        var manifest = await BuildUnder(culture);
        var grid = manifest.Visuals.Single(visual => visual.Name == "Grid");
        Assert.Equal(["2026-01-01", "2026-01-01 14:30:05", "1.5"], grid.Rows.Single());
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task ParameterTextIsIsoOnEveryHost(string culture)
    {
        var manifest = await BuildUnder(culture);
        Assert.Equal("2026-05-14", Parameter(manifest, "since"));
        Assert.Equal("2026-05-14 07:01:11", Parameter(manifest, "at"));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void ValueTextDropsOnlyAMidnightTime(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            Assert.Equal("2026-01-01", ReportValueText.Of(new System.DateTime(2026, 1, 1)));
            Assert.Equal("2026-01-01 00:00:00.5", ReportValueText.Of(new System.DateTime(2026, 1, 1, 0, 0, 0, 500)));
            Assert.Equal("2026-01-01 00:00:00 +02:00", ReportValueText.Of(new System.DateTimeOffset(2026, 1, 1, 0, 0, 0, System.TimeSpan.FromHours(2))));
            Assert.Equal("1.5", ReportValueText.Of(1.5m));
            Assert.Equal("[\"2026-01-01\",\"x\"]", ReportParameterBinding.Text(new object?[] { new System.DateTime(2026, 1, 1), "x" }.ToList()));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // Visual queries run while the report is built, after the script has returned; they still read
    // slash dates in the script's SET DATEFORMAT order.
    [Fact]
    public async Task VisualQueriesFollowTheScriptsDateformat()
    {
        const string script = """
            SET DATEFORMAT DMY;
            CREATE VISUAL Grid AS TABLE (SOURCE = (SELECT CAST('08/01/2024' AS DATE) AS OnDay));
            """;
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.RedirectOutput = true;
        evaluator.DisplayExecuteTree = false;
        await evaluator.Evaluate(new Parser(new Lexer(script).Tokenize(), script).Parse());
        var manifest = await new ManifestBuilder(evaluator).BuildAsync("report.rptsql");
        Assert.Equal(["2024-01-08"], manifest.Visuals.Single(visual => visual.Name == "Grid").Rows.Single());
    }

    private static string? Parameter(ReportManifest manifest, string name) =>
        manifest.Parameters.Single(parameter => parameter.Key.TrimStart('@').Equals(name, System.StringComparison.OrdinalIgnoreCase)).Value;
}
