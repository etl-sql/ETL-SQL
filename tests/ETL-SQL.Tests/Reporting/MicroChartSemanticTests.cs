using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;

namespace ETL_SQL.Tests.Reporting;

public sealed class MicroChartSemanticTests
{
    [Theory]
    [InlineData("line", MarkKind.Line)]
    [InlineData("area", MarkKind.Area)]
    [InlineData("bar", MarkKind.Rect)]
    public void Sparkline_UsesTypedDataAndResolvedPlotPlan(string type, MarkKind mark)
    {
        var bundle = new MicroChartPlanFactory().CreateSparkline("orders-trend",
            [10m, 14.5m, null, 16m, 18m], type, "#123ABC", ["Mon", "Tue", "Wed", "Thu", "Fri"]);

        Assert.Equal(ChartValueKind.Decimal, bundle.Data.Columns.Single(column => column.Name == "value").ValueKind);
        Assert.Equal(mark, Assert.Single(bundle.Plan.Layers).Mark);
        Assert.Collection(bundle.Plan.Nulls.GapRows, row => Assert.Equal(2, row));
        Assert.Contains("first 10", bundle.PlainText);
        Assert.Contains("last 18", bundle.PlainText);
        if (type == "line")
        {
            var svg = new MicroChartPlanFactory().ToManifest(bundle, "sparkline", "table.cell").Svg;
            Assert.Equal(2, svg.Split("<path d=", StringSplitOptions.None).Length - 1);
        }
    }

    [Fact]
    public void Progress_ClampsGeometryButRetainsAccessibleFallback()
    {
        var factory = new MicroChartPlanFactory();
        var bundle = factory.CreateProgress("quota", 1.25m, 0m, 1m, "url(javascript:bad)");
        var manifest = factory.ToManifest(bundle, "progress", "table.cell", 0, 1, "1.25");

        Assert.Equal(1m, bundle.Data.Columns.Single(column => column.Name == "value").Values[0].Decimal);
        Assert.Contains("100%", manifest.PlainText);
        Assert.Contains("fill='#3ba272'", manifest.Svg);
        Assert.DoesNotContain("javascript", manifest.Svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("role='img'", manifest.Svg);
        Assert.Contains("<title", manifest.Svg);
        Assert.Contains("<desc", manifest.Svg);
    }

    [Fact]
    public void Markdown_EmbedsNativeMicroChartImageAndAltText()
    {
        var factory = new MicroChartPlanFactory();
        var micro = factory.ToManifest(factory.CreateSparkline("trend", [1m, 2m, 3m]), "sparkline", "table.cell", 0, 1, "[1,2,3]");
        var visual = new VisualManifest
        {
            Name = "Sales",
            VisualType = "TABLE",
            Columns = ["Region", "Trend"],
            Rows = [["Central", "[1,2,3]"]],
            MicroCharts = [micro]
        };
        var report = new ReportManifest { Visuals = [visual] };

        var markdown = new MarkdownRenderer().Render(report);

        Assert.Contains("data:image/svg+xml;base64,", markdown);
        Assert.Contains("alt=\"Trend: first 1, last 3, range 1–3\"", markdown);
        Assert.DoesNotContain("[1,2,3] |", markdown);
    }

    [Fact]
    public async Task PdfExport_EmbedsCardAndTableMicroChartsWithoutEChartsSsr()
    {
        var factory = new MicroChartPlanFactory();
        var report = new ReportManifest
        {
            Title = "Native micro export",
            Visuals =
            [
                new VisualManifest
                {
                    Name = "KPI", VisualType = "CARD", Columns = ["Value"], Rows = [["42"]],
                    MicroCharts = [factory.ToManifest(factory.CreateSparkline("card-export", [1m, 3m, 2m]), "sparkline", "card.sparkline")]
                },
                new VisualManifest
                {
                    Name = "Goals", VisualType = "TABLE", Columns = ["Team", "Progress"], Rows = [["A", ".75"]],
                    MicroCharts = [factory.ToManifest(factory.CreateProgress("table-export", .75m, 0m, 1m), "progress", "table.cell", 0, 1, ".75")]
                }
            ]
        };

        var bytes = await new PdfExporter().ExportAsync(report);

        Assert.True(bytes.Length > 1_000);
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, bytes[..4]);
    }
}
