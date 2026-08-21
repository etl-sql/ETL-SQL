using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Semantics.Runtime;
using ETL_SQL.Tests.Reporting.Conformance;
using Xunit.Abstractions;

namespace ETL_SQL.Tests.Reporting;

public sealed class NativeSvgGeometryGoldenTests(ITestOutputHelper output)
{
    private static readonly Dictionary<string, string> RepresentativeGoldens = new(StringComparer.Ordinal)
    {
        ["bar_stable_ordering.rptsql"] = "E21F49C4F6B5570F94CEC0622F0F89DDEC4F1D9ABFA1D8EC471C61A4F70A5116",
        ["bar_multi_series_stacked.rptsql"] = "B293031EAF158FFC71B4A8A05A9EF9684DE73FDD3A2434BA16230ECBF347DCF6",
        ["line_temporal_decimals.rptsql"] = "A839E52942126F5AE46F88D33E74BBEBD46959159510B9D6CD94C9B021B33626",
        ["line_null_gaps.rptsql"] = "DC45C90A9E4E12E18B1CD0ACCFD3864EE0AAC6E8AFBE059F0D2560153924641C",
        ["scatter_multi_series_inferred.rptsql"] = "B3C21C339EE47A3429CC53681199028633F879376C64E991475E89FA3F912B93",
        ["pie_donut_proportions.rptsql"] = "9FF2A7B3D9DDB13921CDFC983F11042E43663CB2336A9E49CDA7FA695BAEF24D",
        ["combo_dual_axes.rptsql"] = "6786E672B85F95234919B20E41B919B855EC1CB4BFD48E0768C9E14B2A444724",
        ["rule_statistical_overlays.rptsql"] = "15709C20D4B9F214AA93896EDFC70D24BB7582397DF9A18B55FF9D9A16C34AC9"
    };

    [Fact]
    public async Task RepresentativeNativeSvg_GeometryMatchesApprovedGoldens()
    {
        var differences = new List<string>();
        foreach (var (fixture, expected) in RepresentativeGoldens)
        {
            var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync(fixture);
            var actual = Hash(RepresentativeVisualConformanceHarness.RenderSvg(manifest, manifest.Visuals.First().Name)!);
            if (!actual.Equals(expected, StringComparison.Ordinal)) differences.Add($"{fixture}={actual}");
        }
        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
    }

    [Fact]
    public void MicroChartGeometry_IsDeterministicAndWithinMeasuredRenderBudget()
    {
        var factory = new MicroChartPlanFactory();
        var samples = new[]
        {
            factory.ToManifest(factory.CreateSparkline("golden-line", [1m, 4m, null, 3m, 8m]), "sparkline", "card.sparkline"),
            factory.ToManifest(factory.CreateSparkline("golden-area", [2m, 5m, 3m, 7m], "area"), "sparkline", "table.cell"),
            factory.ToManifest(factory.CreateSparkline("golden-bar", [2m, 5m, 3m, 7m], "bar"), "sparkline", "table.cell"),
            factory.ToManifest(factory.CreateProgress("golden-progress", .72m, 0m, 1m), "progress", "table.cell")
        };
        var expected = new[]
        {
            "CDF4C1002FEBC0BC88CD249402BEC30A6A7B5245F50D90998123AFA0CC73DF98",
            "E163FB89017B9A26A6FE417E8DC69D37B0C08BC5A79B7283E48E9D810A6B1E5A",
            "63B4BAF9D2987778FF2B74F6CCF20CEE243433718F0F413813E59C83842A9B9F",
            "719F793D59EF9DD1F037C0B3F92C2C2D0475F6D35D50FF33B1E9E604C9EBBA78"
        };
        var actualHashes = samples.Select(sample => Hash(sample.Svg)).ToArray();
        Assert.True(expected.SequenceEqual(actualHashes), string.Join(Environment.NewLine,
            actualHashes.Select((hash, index) => $"micro-{index}={hash}")));

        const int iterations = 1_000;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        for (var index = 0; index < iterations; index++)
        {
            var bundle = factory.CreateSparkline($"cost-{index}", [1m, 3m, 2m, 5m, 8m]);
            _ = factory.ToManifest(bundle, "sparkline", "table.cell");
        }
        timer.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        output.WriteLine($"native-micro iterations={iterations} elapsed_ms={timer.Elapsed.TotalMilliseconds:0.###} allocated_bytes={allocated}");

        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5), $"1,000 native micro-chart renders took {timer.Elapsed}.");
        Assert.True(allocated < 100_000_000, $"1,000 native micro-chart renders allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void NativeMicroChartMarkdownExport_MatchesApprovedSnapshot()
    {
        var factory = new MicroChartPlanFactory();
        var report = new ReportManifest
        {
            Title = "Micro export",
            BuiltAt = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc),
            Visuals =
            [
                new VisualManifest
                {
                    Name = "KPI", VisualType = "CARD", Columns = ["Value"], Rows = [["42"]],
                    MicroCharts = [factory.ToManifest(factory.CreateSparkline("snapshot-card", [2m, 5m, 4m]), "sparkline", "card.sparkline")]
                },
                new VisualManifest
                {
                    Name = "Goals", VisualType = "TABLE", Columns = ["Team", "Goal"], Rows = [["North", ".6"]],
                    MicroCharts = [factory.ToManifest(factory.CreateProgress("snapshot-progress", .6m, 0m, 1m), "progress", "table.cell", 0, 1, ".6")]
                }
            ]
        };

        Assert.Equal("5E5C530375B026F65B04819A2CCE18BFF6D39B4A192B37E30D6BB1D075018104",
            Hash(new MarkdownRenderer().Render(report)));
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
