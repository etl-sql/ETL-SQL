using System.Collections.Generic;
using System.Linq;
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
