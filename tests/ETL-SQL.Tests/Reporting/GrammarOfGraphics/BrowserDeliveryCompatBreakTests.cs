using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Tests.Reporting.Conformance;
using Xunit;

namespace ETL_SQL.Tests.Reporting.GrammarOfGraphics;

/// <summary>
/// Old versus new behaviour for the two v0.19 compatibility breaks recorded in
/// <c>BREAKING_CHANGES.md</c>: the browser payload no longer carries the semantic contracts, and a
/// CUSTOM chart cross-filters on its resolved X binding rather than on column zero.
/// </summary>
[Trait("CompatBreak", "0.19")]
public sealed class BrowserDeliveryCompatBreakTests
{
    [Fact]
    public async Task Old_ManifestSerializationCarriedFiveRepresentationsOfOneChart()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("custom_crossfilter_offset_key.rptsql");

        // The pre-v0.19 wire payload: plain serialization of the server's working object.
        var old = JsonSerializer.Serialize(manifest);

        Assert.Contains("\"chartSpec\"", old, StringComparison.Ordinal);
        Assert.Contains("\"chartData\"", old, StringComparison.Ordinal);
        Assert.Contains("\"plotPlan\"", old, StringComparison.Ordinal);
    }

    [Fact]
    public async Task New_BrowserDeliveryCarriesOnlyWhatTheBrowserReads()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("custom_crossfilter_offset_key.rptsql");

        var old = JsonSerializer.Serialize(manifest);
        var browser = BrowserDeliveryProjection.Serialize(manifest);

        Assert.DoesNotContain("\"chartSpec\"", browser, StringComparison.Ordinal);
        Assert.DoesNotContain("\"chartData\"", browser, StringComparison.Ordinal);
        Assert.DoesNotContain("\"plotPlan\"", browser, StringComparison.Ordinal);
        Assert.Contains("\"nativeSvg\"", browser, StringComparison.Ordinal);
        Assert.Contains("\"interaction\"", browser, StringComparison.Ordinal);
        Assert.True(browser.Length < old.Length / 2,
            $"browser payload {browser.Length} B is not materially smaller than the {old.Length} B server object.");
    }

    [Fact]
    public async Task Old_CustomCrossFilterWouldHaveLandedOnTheFirstSourceColumn()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("custom_crossfilter_offset_key.rptsql");
        var visual = manifest.Visuals.Single(item => item.Name == "RegionalRevenue");

        // The old browser rule: options['mapping:x'] ... falling through to visual.columns[0].
        // A CUSTOM visual has no MAPPINGS clause, so every mapping lookup misses.
        Assert.DoesNotContain(visual.Options.Keys, key => key.StartsWith("mapping:", StringComparison.OrdinalIgnoreCase));
        var wouldHaveBeen = visual.Columns[0];
        Assert.Equal("Revenue", wouldHaveBeen);
    }

    [Fact]
    public async Task New_CustomCrossFilterUsesTheResolvedEncodingKey()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("custom_crossfilter_offset_key.rptsql");
        var visual = manifest.Visuals.Single(item => item.Name == "RegionalRevenue");

        Assert.Equal("Region", visual.Interaction!.Key);
        Assert.NotEqual(visual.Columns[0], visual.Interaction.Key);
    }
}
