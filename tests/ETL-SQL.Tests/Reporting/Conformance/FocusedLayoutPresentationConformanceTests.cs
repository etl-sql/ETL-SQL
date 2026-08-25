using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using Xunit;

namespace ETL_SQL.Tests.Reporting.Conformance;

/// <summary>
/// Side-by-side proof that the six focused layout modules present like a <c>PlotPlan</c> visual.
///
/// The geometry stays focused on purpose. What must not diverge is presentation: the same theme, the
/// same series colours, an accessible name and description, the resolved interaction key, and an
/// explicit authored canvas. A focused module carrying its own palette array is exactly the drift
/// these tests exist to catch.
/// </summary>
public sealed class FocusedLayoutPresentationConformanceTests
{
    private const string Fixture = "focused_layout_shared_presentation.rptsql";

    [Fact]
    public async Task FocusedTreemap_UsesTheSameSeriesColoursAsThePlanBackedBar()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync(Fixture);

        var bar = Visual(manifest, "SharedPaletteBar");
        var treemap = Visual(manifest, "SharedPaletteTreemap");

        Assert.NotNull(bar.PlotPlan);
        Assert.Null(treemap.PlotPlan);

        var planColours = bar.PlotPlan!.Palette
            .ToDictionary(item => item.SeriesKey, item => item.Color, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(4, planColours.Count);

        var svg = treemap.NativeSvg;
        Assert.NotNull(svg);

        foreach (var (category, colour) in planColours)
        {
            var tile = Regex.Match(svg!, $"<g data-row-index='\\d+'><rect class='treemap-tile'[^>]*fill='(?<fill>#[0-9a-fA-F]{{3,6}})'[^>]*/><title>{Regex.Escape(category)}:");
            Assert.True(tile.Success, $"No treemap tile found for '{category}' in the focused layout SVG.");
            Assert.Equal(colour, tile.Groups["fill"].Value);
        }
    }

    [Fact]
    public async Task FocusedTreemap_HonoursTheAuthoredCanvasAndCarriesAnAccessibleDescription()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync(Fixture);
        var svg = Visual(manifest, "SharedPaletteTreemap").NativeSvg!;

        Assert.Contains("width='720'", svg);
        Assert.Contains("height='420'", svg);
        Assert.Contains("viewBox='0 0 720 420'", svg);
        Assert.Contains("role='img'", svg);
        Assert.Contains("aria-label='Focused Layout Categories'", svg);
        Assert.Contains("<desc>Focused Layout Categories.", svg);
    }

    [Fact]
    public async Task FocusedTreemap_StampsTheResolvedInteractionKeyRatherThanAPositionalGuess()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync(Fixture);
        var treemap = Visual(manifest, "SharedPaletteTreemap");

        Assert.NotNull(treemap.Interaction);
        Assert.Equal("Category", treemap.Interaction!.Key);
        Assert.Contains("data-interaction-key='Category'", treemap.NativeSvg!);
        Assert.Contains("data-interaction-highlight='CATEGORICAL'", treemap.NativeSvg!);
    }

    [Theory]
    [InlineData("TREEMAP")]
    [InlineData("SUNBURST")]
    [InlineData("SANKEY")]
    [InlineData("NETWORK")]
    [InlineData("MAP")]
    [InlineData("MATRIX")]
    public void EveryFocusedLayout_ResolvesItsPresentationFromTheSharedInputs(string visualType)
    {
        var visual = FocusedSample(visualType);
        var svg = new SvgChartRenderer().Render(visual);

        Assert.NotNull(svg);
        Assert.Contains("role='img'", svg);
        Assert.Contains("<desc>", svg);
        Assert.Contains("width='480'", svg);
        Assert.Contains("height='300'", svg);
        // The dark surface token has to reach the canvas, not just the CSS around it.
        Assert.Contains("fill='#1f2430'", svg);
    }

    [Theory]
    [InlineData("TREEMAP")]
    [InlineData("SUNBURST")]
    [InlineData("SANKEY")]
    [InlineData("NETWORK")]
    public void EveryColouredFocusedLayout_TakesTheFirstSeriesColourFromTheSharedPalette(string visualType)
    {
        var svg = new SvgChartRenderer().Render(FocusedSample(visualType))!;

        Assert.Contains(ChartPalette.Default(0), svg);
        Assert.Contains(ChartPalette.Default(1), svg);
    }

    [Fact]
    public void FocusedLayoutInputs_ClampAnAbsurdAuthoredCanvasInsteadOfEmittingIt()
    {
        var visual = FocusedSample("TREEMAP");
        visual.Options["WIDTH"] = "999999";
        visual.Options["HEIGHT"] = "0";

        var bounds = FocusedLayoutInputs.ResolveBounds(visual);

        Assert.Equal(4000m, bounds.Width);
        Assert.Equal(120m, bounds.Height);
    }

    [Fact]
    public void FocusedLayoutInputs_IgnoreRelativeSizesRatherThanInventingAViewport()
    {
        var visual = FocusedSample("TREEMAP");
        visual.Options.Remove("WIDTH");
        visual.Options.Remove("HEIGHT");
        visual.Styles = new Dictionary<string, string> { ["WIDTH"] = "100%", ["HEIGHT"] = "400px" };

        var bounds = FocusedLayoutInputs.ResolveBounds(visual);

        Assert.Equal(FocusedLayoutInputs.DefaultBounds.Width, bounds.Width);
        Assert.Equal(400m, bounds.Height);
    }

    private static VisualManifest Visual(ReportManifest manifest, string name) =>
        manifest.Visuals.First(visual => visual.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A minimal two-series sample of each focused layout on a dark, explicitly sized canvas.</summary>
    private static VisualManifest FocusedSample(string visualType)
    {
        var (columns, rows, mappings) = visualType switch
        {
            "TREEMAP" => (new[] { "Name", "Value" },
                new[] { new[] { "Alpha", "120" }, new[] { "Beta", "80" } },
                new[] { ("name", "Name"), ("value", "Value") }),
            "SUNBURST" => (new[] { "Level1", "Level2", "Value" },
                new[] { new[] { "Alpha", "North", "120" }, new[] { "Beta", "South", "80" } },
                new[] { ("level1", "Level1"), ("level2", "Level2"), ("value", "Value") }),
            "SANKEY" => (new[] { "From", "To", "Value" },
                new[] { new[] { "Alpha", "Beta", "12" } },
                new[] { ("from", "From"), ("to", "To"), ("value", "Value") }),
            "NETWORK" => (new[] { "From", "To", "Weight" },
                new[] { new[] { "Alpha", "Beta", "3" } },
                new[] { ("from", "From"), ("to", "To"), ("value", "Weight") }),
            "MAP" => (new[] { "Region", "Value" },
                new[] { new[] { "France", "12" }, new[] { "Spain", "8" } },
                new[] { ("region", "Region"), ("value", "Value") }),
            _ => (new[] { "Region", "Quarter" },
                new[] { new[] { "Alpha", "Q1" }, new[] { "Beta", "Q2" } },
                Array.Empty<(string, string)>())
        };

        var visual = new VisualManifest
        {
            Name = visualType + " Focused",
            VisualType = visualType,
            Columns = [.. columns],
            Rows = [.. rows.Select(row => new List<string?>(row))],
            Styles = new Dictionary<string, string> { ["THEME"] = "dark" }
        };
        visual.Options["WIDTH"] = "480";
        visual.Options["HEIGHT"] = "300";
        foreach (var (role, column) in mappings) visual.Options["mapping:" + role] = column;
        return visual;
    }
}
