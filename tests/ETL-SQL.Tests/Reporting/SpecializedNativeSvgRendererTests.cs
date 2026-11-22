using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ETL_SQL.Reporting;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

public sealed class SpecializedNativeSvgRendererTests
{
    [Theory]
    [MemberData(nameof(Cases))]
    public void SpecializedCatalog_RendersDeterministicNativeSvg(VisualManifest visual, string marker)
    {
        var first = new SvgChartRenderer().Render(visual);
        var second = new SvgChartRenderer().Render(visual);

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.Contains("<svg", first);
        Assert.Contains(marker, first);
        Assert.DoesNotContain("placeholder", first, System.StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<object[]> Cases()
    {
        yield return [Visual("TREEMAP", ["Name", "Parent", "Value"], [["Platform", "", "100"], ["Portal", "Platform", "60"], ["Engine", "Platform", "40"]], ("name", "Name"), ("parent", "Parent"), ("value", "Value")), "Portal"];
        yield return [Visual("SUNBURST", ["Level1", "Level2", "Value"], [["North", "Retail", "60"], ["North", "Online", "40"]], ("level1", "Level1"), ("level2", "Level2"), ("value", "Value")), "North / Retail"];
        yield return [Visual("SANKEY", ["From", "To", "Value"], [["Extract", "Transform", "8"], ["Transform", "Load", "6"]], ("from", "From"), ("to", "To"), ("value", "Value")), "Extract"];
        yield return [Visual("NETWORK", ["From", "To", "Weight"], [["A", "B", "2"], ["B", "C", "3"]], ("from", "From"), ("to", "To"), ("value", "Weight")), "circle"];
        yield return [Visual("MAP", ["Longitude", "Latitude", "Value", "Label"], [["-93.2", "44.9", "10", "Minneapolis"]], ("lon", "Longitude"), ("lat", "Latitude"), ("value", "Value"), ("label", "Label"), ("MODE", "POINTS")), "Minneapolis"];
        yield return [Visual("MATRIX", ["Region", "Quarter", "Revenue"], [["North", "Q1", "10"], ["South", "Q2", "12"]]), "Revenue"];
    }

    [Fact]
    public void Treemap_SquarifiesTilesAndAppliesCategoryPalette()
    {
        var visual = Visual("TREEMAP", ["Name", "Value"],
            [["Electronics", "1500"], ["Furniture", "1200"], ["Clothing", "900"], ["Toys", "400"], ["Books", "300"], ["Garden", "600"]],
            ("name", "Name"), ("value", "Value"));
        visual.Options["COLOR:ELECTRONICS"] = "#123456";

        var svg = new SvgChartRenderer().Render(visual)!;
        var tiles = Regex.Matches(svg, "<rect class='treemap-tile'[^>]+>");

        Assert.Equal(6, tiles.Count);
        Assert.Contains("fill='#123456'", svg);
        Assert.Contains(tiles.Cast<Match>(), match =>
            Regex.Match(match.Value, " y='(?<value>[0-9.]+)'").Groups["value"].Value is { Length: > 0 } value &&
            double.Parse(value, System.Globalization.CultureInfo.InvariantCulture) > 38d);
    }

    [Fact]
    public void Map_WithoutZoomOrCenter_FitsTheGeometryToTheChart()
    {
        // The projection used to map the whole world onto the chart, so a US_STATES map was
        // drawn at world scale: about a third of the width, in the left half (2026-11-21).
        var visual = Visual("MAP", ["State", "Revenue"], [["California", "1250000"], ["Texas", "980000"]],
            ("region", "State"), ("value", "Revenue"));
        visual.Options["MAP_NAME"] = "US_STATES";

        var svg = new SvgChartRenderer().Render(visual)!;
        var (width, height) = Size(svg);
        var (minX, maxX, minY, maxY) = PathExtent(svg);

        Assert.True(minX >= 0 && maxX <= width && minY >= 38 && maxY <= height, $"map leaves the chart: x {minX}..{maxX}, y {minY}..{maxY} in {width}x{height}");
        var fillsWidth = (maxX - minX) / width;
        var fillsHeight = (maxY - minY) / (height - 38);
        Assert.True(fillsWidth > .85 || fillsHeight > .85, $"map fills {fillsWidth:P0} of the width and {fillsHeight:P0} of the height");
        Assert.InRange((minX + maxX) / 2 / width, .45, .55);
    }

    [Fact]
    public void Map_UsStates_DrawsAlaskaAndHawaiiAsInsets()
    {
        // At their true positions Alaska took much of the card and the lower 48 about half its width.
        var svg = new SvgChartRenderer().Render(UsStatesChoropleth())!;
        var (width, height) = Size(svg);
        var regions = TitledPaths(svg);
        var all = Extent(regions.SelectMany(r => r.Points));
        var lower48 = Extent(regions.Where(r => r.Name is not ("Alaska" or "Hawaii" or "Puerto Rico")).SelectMany(r => r.Points));
        var puertoRico = Extent(regions.Where(r => r.Name == "Puerto Rico").SelectMany(r => r.Points));
        Assert.True(puertoRico.MinY >= lower48.MinY && puertoRico.MaxY <= lower48.MaxY, $"Puerto Rico {puertoRico} sits outside the lower 48 {lower48}");
        var texas = Extent(regions.Where(r => r.Name == "Texas").SelectMany(r => r.Points));

        Assert.True(all.MinX >= 0 && all.MaxX <= width && all.MinY >= 0 && all.MaxY <= height, $"map leaves the chart: {all} in {width}x{height}");
        Assert.True((lower48.MaxX - lower48.MinX) / (all.MaxX - all.MinX) > .90 && (lower48.MaxY - lower48.MinY) / (all.MaxY - all.MinY) > .90,
            $"lower 48 {lower48} do not set the map extent {all}");
        Assert.True((lower48.MaxX - lower48.MinX) / width > .60, $"lower 48 span {(lower48.MaxX - lower48.MinX) / width:P0} of the width");
        foreach (var inset in new[] { "Alaska", "Hawaii" })
        {
            var e = Extent(regions.Where(r => r.Name == inset).SelectMany(r => r.Points));
            Assert.True(e.MaxY > texas.MinY && e.MinX < texas.MaxX, $"{inset} {e} is not below and left of Texas {texas}");
        }
    }

    [Fact]
    public void Map_UsStatesPoints_AnchorageMarkerLandsOnAlaskaInset()
    {
        // POINTS and choropleth fit the same outline into the same card, so the choropleth names the shapes.
        var shapes = TitledPaths(new SvgChartRenderer().Render(UsStatesChoropleth())!);
        var points = Visual("MAP", ["Longitude", "Latitude", "Value", "Label"],
            [["-149.90", "61.22", "10", "Anchorage"], ["-122.33", "47.61", "10", "Seattle"]],
            ("lon", "Longitude"), ("lat", "Latitude"), ("value", "Value"), ("label", "Label"), ("MODE", "POINTS"));
        points.Options["MAP_NAME"] = "us_states";
        var svg = new SvgChartRenderer().Render(points)!;

        foreach (var (row, state) in new[] { (0, "Alaska"), (1, "Washington") })
        {
            var circle = Regex.Match(svg, $"<circle data-row-index='{row}' cx='(?<x>[-0-9.]+)' cy='(?<y>[-0-9.]+)'");
            var (x, y) = (Number(circle.Groups["x"].Value), Number(circle.Groups["y"].Value));
            var e = Extent(shapes.Where(s => s.Name == state).SelectMany(s => s.Points));
            Assert.True(x >= e.MinX && x <= e.MaxX && y >= e.MinY && y <= e.MaxY, $"{state} marker ({x}, {y}) is off its shape {e}");
        }
    }

    private static VisualManifest UsStatesChoropleth()
    {
        var visual = Visual("MAP", ["State", "Revenue"],
            [["California", "1250000"], ["Texas", "980000"], ["Alaska", "120000"], ["Hawaii", "150000"]],
            ("region", "State"), ("value", "Revenue"));
        visual.Options["MAP_NAME"] = "US_STATES";
        return visual;
    }

    private static List<(string Name, List<(double X, double Y)> Points)> TitledPaths(string svg) =>
        Regex.Matches(svg, "<path[^>]* d='(?<d>[^']+)'[^>]*><title>(?<t>[^<:]+)").Cast<Match>()
            .Select(m => (m.Groups["t"].Value.Trim(), Regex.Matches(m.Groups["d"].Value, "[ML] (?<x>[-0-9.]+) (?<y>[-0-9.]+)").Cast<Match>()
                .Select(p => (Number(p.Groups["x"].Value), Number(p.Groups["y"].Value))).ToList()))
            .ToList();

    private static (double MinX, double MaxX, double MinY, double MaxY) Extent(IEnumerable<(double X, double Y)> points)
    {
        var list = points.ToList();
        Assert.NotEmpty(list);
        return (list.Min(p => p.X), list.Max(p => p.X), list.Min(p => p.Y), list.Max(p => p.Y));
    }

    [Fact]
    public void Map_WithZoomAndCenter_KeepsTheExplicitView()
    {
        var visual = Visual("MAP", ["Longitude", "Latitude", "Value", "Label"], [["-93.2", "44.9", "10", "Minneapolis"]],
            ("lon", "Longitude"), ("lat", "Latitude"), ("value", "Value"), ("label", "Label"), ("MODE", "POINTS"));
        visual.Options["ZOOM"] = "4";
        visual.Options["CENTER"] = "(44.9, -93.2)";

        var svg = new SvgChartRenderer().Render(visual)!;
        var (width, height) = Size(svg);
        var circle = Regex.Match(svg, "<circle data-row-index='0' cx='(?<x>[-0-9.]+)' cy='(?<y>[-0-9.]+)'");

        // Centred on the point: the explicit view is unchanged.
        Assert.Equal(10 + (width - 20) / 2, Number(circle.Groups["x"].Value), 1);
        Assert.Equal(38 + (height - 38 - 8) / 2, Number(circle.Groups["y"].Value), 1);
    }

    [Fact]
    public void Sankey_LastColumnLabelsSitLeftOfTheirNodes()
    {
        // Labels were always drawn right of the node, so the last column ran off the chart.
        var visual = Visual("SANKEY", ["From", "To", "Value"],
            [["Organic Search", "Free Trial Signup", "8"], ["Free Trial Signup", "Paid Enterprise Plan", "6"]],
            ("from", "From"), ("to", "To"), ("value", "Value"));

        var svg = new SvgChartRenderer().Render(visual)!;
        var (width, _) = Size(svg);
        var last = Regex.Match(svg, "<rect x='(?<x>[-0-9.]+)'[^>]*data-node='Paid Enterprise Plan'/><text x='(?<tx>[-0-9.]+)'[^>]*text-anchor='end'");

        Assert.True(last.Success, "last-column label should be anchored at its end");
        Assert.True(Number(last.Groups["tx"].Value) < Number(last.Groups["x"].Value));
        var first = Regex.Match(svg, "data-node='Organic Search'/><text x='(?<tx>[-0-9.]+)'");
        Assert.True(Number(first.Groups["tx"].Value) < width / 2);
        Assert.DoesNotContain("data-node='Organic Search'/><text x='0' ", svg);
    }

    private static double Number(string value) => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private static (double Width, double Height) Size(string svg)
    {
        var match = Regex.Match(svg, "viewBox='0 0 (?<w>[0-9.]+) (?<h>[0-9.]+)'");
        return (Number(match.Groups["w"].Value), Number(match.Groups["h"].Value));
    }

    private static (double MinX, double MaxX, double MinY, double MaxY) PathExtent(string svg)
    {
        var points = Regex.Matches(svg, "[ML] (?<x>[-0-9.]+) (?<y>[-0-9.]+)").Cast<Match>()
            .Select(m => (X: Number(m.Groups["x"].Value), Y: Number(m.Groups["y"].Value))).ToList();
        Assert.NotEmpty(points);
        return (points.Min(p => p.X), points.Max(p => p.X), points.Min(p => p.Y), points.Max(p => p.Y));
    }

    private static VisualManifest Visual(string type, string[] columns, string?[][] rows, params (string Key, string Value)[] options)
    {
        var visual = new VisualManifest
        {
            Name = type + " Native",
            VisualType = type,
            Columns = [.. columns],
            Rows = [.. rows.Select(row => new List<string?>(row))]
        };
        foreach (var (key, value) in options)
            visual.Options[key.Equals("MODE", System.StringComparison.OrdinalIgnoreCase) ? key : "mapping:" + key] = value;
        return visual;
    }
}
