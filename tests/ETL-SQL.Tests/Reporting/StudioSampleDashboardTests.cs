using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Tests.Reporting.Conformance;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

/// <summary>
/// Runs the script behind Studio Home's <b>Start with sample data</b> and checks it produces a
/// dashboard with something on it.
///
/// <para>Parsing is not the bar. The seed exists so a first session ends with a working report
/// rather than an empty canvas, and a script that parses can still evaluate to a page with no
/// visuals, or to visuals with no rows — which is exactly the blank screen the seed is meant to
/// replace, arriving without an error message. So this executes it against the in-memory sample
/// connector and asserts the three tiles a dashboard is made of — a KPI number, a chart, and a
/// table — are present, mapped into the page, and carrying data.</para>
///
/// <para>The script lives in a JavaScript string literal that no compiler reads, which is why it is
/// pulled from the canonical contracts module rather than copied here: a copy would keep passing
/// after the seed itself was broken.</para>
/// </summary>
[Trait("Category", "Reporting")]
public sealed class StudioSampleDashboardTests
{
    private static string SampleDashboardScript()
    {
        var contracts = Path.Combine(
            RepresentativeVisualConformanceHarness.GetRepoRoot(),
            "src", "ETL-SQL.ReportRuntime", "Resources", "Shared", "designer", "studio-contracts.js");

        var table = Regex.Match(
            File.ReadAllText(contracts),
            @"const\s+STUDIO_STARTER_SCRIPTS\s*=\s*Object\.freeze\(\{(?<body>.*?)\}\);",
            RegexOptions.Singleline);
        Assert.True(table.Success, "STUDIO_STARTER_SCRIPTS was not found in studio-contracts.js.");

        var report = Regex.Match(table.Groups["body"].Value, @"report:\s*`(?<script>[^`]*)`", RegexOptions.Singleline);
        Assert.True(report.Success, "The report starter script was not found in STUDIO_STARTER_SCRIPTS.");
        return report.Groups["script"].Value;
    }

    [Fact]
    public async Task SampleDashboard_RunsAndFillsAKpiChartAndTable()
    {
        var script = SampleDashboardScript();

        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileScriptAsync(
            script, Path.Combine(Path.GetTempPath(), "studio_sample_dashboard.rptsql"),
            "The Studio sample dashboard");

        var page = Assert.Single(manifest.Pages);
        Assert.Equal("DASHBOARD", page.Mode, ignoreCase: true);

        var card = manifest.Visuals.SingleOrDefault(visual =>
            visual.VisualType.Equals("CARD", StringComparison.OrdinalIgnoreCase));
        var chart = manifest.Visuals.SingleOrDefault(visual =>
            visual.VisualType.Equals("BAR", StringComparison.OrdinalIgnoreCase));
        var table = manifest.Visuals.SingleOrDefault(visual =>
            visual.VisualType.Equals("TABLE", StringComparison.OrdinalIgnoreCase));

        Assert.True(card is not null, "The sample dashboard no longer seeds a KPI card.");
        Assert.True(chart is not null, "The sample dashboard no longer seeds a chart.");
        Assert.True(table is not null, "The sample dashboard no longer seeds a table.");

        // Rows, not just declarations: a tile bound to an empty #temp table renders as the same
        // blank space as no tile at all.
        foreach (var visual in new[] { card!, chart!, table! })
        {
            Assert.True(visual.Rows.Count > 0, $"The sample dashboard's {visual.Name} tile came back with no rows.");
        }

        // A visual the page does not place is invisible however good its data is.
        foreach (var visual in new[] { card!, chart!, table! })
        {
            Assert.Contains(visual.Name, page.SlotMap.Values, StringComparer.OrdinalIgnoreCase);
        }
    }
}
