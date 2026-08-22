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
        ["bar_stable_ordering.rptsql"] = "F8450E9C33F20B5D747FD5CEA465FD57309439952676031E8D28EDDBBE7BC778",
        ["bar_multi_series_stacked.rptsql"] = "599191C8C4432F6A64A92CDC058B059D5649D8DA0F02A66CD9B25C693A9B1C10",
        ["line_temporal_decimals.rptsql"] = "93C64C0E6F22E6A286D5414B59DA87B6097F93CC9CC8ED42CBC326CFCBBD41DA",
        ["line_null_gaps.rptsql"] = "DC5AAC81456C550C72791ACBE27A2F86DC4BC87A95F763970F96A03A2F950D9C",
        ["scatter_multi_series_inferred.rptsql"] = "79EFA584CC57B17177467ED27B155D3427DFFF35F159B5FA5B16DDDFB196AE2A",
        ["pie_donut_proportions.rptsql"] = "D2C53F2B21B5929479BCD985975E760955D0022BAD5D3A8D80660AD52F69AF1D",
        ["combo_dual_axes.rptsql"] = "24718F91F1D38CFD19BB4E9C19A03405823AC97DC86D6397600105F724ADEB4E",
        ["rule_statistical_overlays.rptsql"] = "36ECE0EA3A71E2C369DE4C6DA7BBA21675100C06D6715F681594B31513D785AF"
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
            "10B98B3E6AFD59641C466C67D18D8F231A1B1C1B59EED97F44C2F0BE2B92964E",
            "2FFDC98A492C94CA4356AD9390A81F442EBDC97239BEEF3951E6BC06DBA8F58C",
            "2976C14C5B1BB0AD42870E56C5190AE3841B86B590C45B9D69E87FD34B280250",
            "6E4D572BB3C9FB38C30371A5790E2997D293EA1B0DFABF66E146E08650B96BA7"
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

        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5), $"1,000 native micro-chart renders took {timer.Elapsed}."); // flaky-time-bound-ok: native micro-chart rendering throughput assertion
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

        Assert.Equal("5B8810452FB0D9CC6ED41B0A1AD6AB34402504495AB044ABCFFA50B5485FF471",
            Hash(new MarkdownRenderer().Render(report)));
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
