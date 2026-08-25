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
        ["bar_stable_ordering.rptsql"] = "F44399723133A8C78E60015311ACC87CBA227FA2638802C7B64D64DADB1D1BFB",
        ["bar_multi_series_stacked.rptsql"] = "ECC3DCF47C213FB773B51144BE02C1157E22776AD96DA17BC57C6E3454F0C395",
        ["line_temporal_decimals.rptsql"] = "4B3E7774F04A849DABE789D178B1960F43CD7B425C86F87AE94B31FDDCCAFB4D",
        ["line_null_gaps.rptsql"] = "ACD69082B93E3E41436574A622BFA7E75C2B99B6A409952AE630A621F970D6A7",
        ["scatter_multi_series_inferred.rptsql"] = "58985BBE9930F105CEC5CD6AEB23138106C816C871B5B15E22A9544B99ACDE83",
        ["pie_donut_proportions.rptsql"] = "C19159B66C3F92A7A358735758EFD3EF76073F1AA79764C920B3A9D7C1D77E36",
        ["combo_dual_axes.rptsql"] = "ACCF38D699DEAB727D4B6F2F30D98291EE8F19D88F60E8E0B253FF766720ED9B",
        ["rule_statistical_overlays.rptsql"] = "5B22447D8BD56EFCC3B26B631697884ABCB7A79B1347BBC27114B0C408FB70F8",
        ["custom_ranged_rect_bands.rptsql"] = "4DE6B133C56B2CFC2F3AB4D16929DE72308C3576306ADB346950E03624E9B0EF",
        ["custom_ranged_rect_histogram.rptsql"] = "35ADAACED03E0C9434FF65BD0CEB3319A9650601005AD306C7BAB241B5980ECA"
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
