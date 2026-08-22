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
        ["bar_stable_ordering.rptsql"] = "AE3BF411A034699F107F64712177F5B44BD8AED072FBDE7D0F5BA097C9289AD2",
        ["bar_multi_series_stacked.rptsql"] = "339935672F9C895EDCB71EEC7FB61365F081044643B8A05D4A4A2236FF3E3032",
        ["line_temporal_decimals.rptsql"] = "1C7BF76105EA590E6DD3BE10C34A60D1CCFF609BEC1295083B9995F6AEA1EC07",
        ["line_null_gaps.rptsql"] = "64A0A12FC5C349EECC8CC7E01123BEB8F26C83A280AF719D730B77CD008A90E7",
        ["scatter_multi_series_inferred.rptsql"] = "CB62D95E1177D44ADF3BC8B7ADB0F9AB43C6D9E38966839A11B9B049B623CEC8",
        ["pie_donut_proportions.rptsql"] = "ADD645DDF6C1431B449EAA168DC01A94747071890D782097728F1B2D59FD200E",
        ["combo_dual_axes.rptsql"] = "D31347579AF334D6FF7C45515B8402AA9CC391160472821A9A7A1EC5F2A7FAD3",
        ["rule_statistical_overlays.rptsql"] = "D516D4701B4B062B433AB385A0B2FC261A94F0E8EB216A867BFEE84F61018BAC"
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
