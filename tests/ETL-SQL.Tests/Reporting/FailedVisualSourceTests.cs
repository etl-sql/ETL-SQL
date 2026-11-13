using System;
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
/// A visual whose source fails shows the error on its own card. The report build used to look
/// clean all the same: no report error and no message, so nothing but the card said it failed.
/// </summary>
public sealed class FailedVisualSourceTests
{
    private const string Data = "SELECT 'West' AS region, 5 AS amt INTO #sales; ";
    private const string Page = " CREATE PAGE P AS DASHBOARD (LAYOUT (STRUCTURE = 'A B', MAP ('A' = Good, 'B' = Bad)));";
    private const string Good = "CREATE VISUAL Good AS TABLE (SOURCE = (SELECT region, amt FROM #sales)); ";

    [Theory]
    [InlineData("SELECT regoin FROM #sales", "Unknown column 'regoin'")]
    [InlineData("SELECT regoin FROM (SELECT * FROM #sales) s", "Unknown column 'regoin'")]
    [InlineData("SELECT 1 / 0 AS x FROM #sales", "Divide by zero")]
    public async Task AFailedSourceIsReportedOnTheReport(string source, string error)
    {
        var manifest = await Build(Data + Good + $"CREATE VISUAL Bad AS TABLE (SOURCE = ({source}));" + Page);

        var bad = manifest.Visuals.Single(v => v.Name == "Bad");
        Assert.Contains(error, bad.Error);
        Assert.Equal("One or more visuals failed to build.", manifest.Error);
        var message = Assert.Single(manifest.Messages!, m => m.Message.Contains(error, StringComparison.Ordinal));
        Assert.StartsWith("Visual 'Bad' failed:", message.Message);
        Assert.Equal("Red", message.Color);

        // The other visual still has its rows.
        var good = manifest.Visuals.Single(v => v.Name == "Good");
        Assert.Null(good.Error);
        Assert.Single(good.Rows);
    }

    [Fact]
    public async Task ACleanReportHasNoError()
    {
        var manifest = await Build(Data + Good +
            "CREATE VISUAL Bad AS TABLE (SOURCE = (SELECT amt FROM #sales));" + Page);

        Assert.Null(manifest.Error);
        Assert.All(manifest.Visuals, v => Assert.Null(v.Error));
        Assert.DoesNotContain(manifest.Messages ?? [], m => m.Message.Contains("failed", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<ReportManifest> Build(string sql)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.RedirectOutput = true;
        evaluator.DisplayExecuteTree = false;
        await evaluator.Evaluate(new Parser(new Lexer(sql).Tokenize(), sql).Parse());
        return await new ManifestBuilder(evaluator).BuildAsync("failed-visual.rptsql");
    }
}
