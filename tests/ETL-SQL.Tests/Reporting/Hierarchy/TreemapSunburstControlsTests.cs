using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using Xunit;

namespace ETL_SQL.Tests.Reporting.Hierarchy;

public class TreemapSunburstControlsTests
{
    private static VisualManifest BuildManifest(
        string script,
        string visualType,
        List<string> columns,
        List<List<string?>> rows)
    {
        var lexer = new Lexer(script);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var statements = new List<Statement>();
        while (parser.Current.Type != TokenType.EOF) statements.Add(parser.ParseStatement());
        var stmt = (CreateVisualStatement)statements[0];

        var manifest = new VisualManifest
        {
            Name = stmt.Name,
            VisualType = visualType,
            Columns = columns,
            Rows = rows
        };

        foreach (var opt in stmt.Options)
        {
            manifest.Options[opt.Key] = opt.Value;
        }
        foreach (var m in stmt.Mappings)
        {
            manifest.Options["mapping:" + m.Role.ToLowerInvariant()] = m.Column;
        }

        return manifest;
    }

    private static string RenderSvg(VisualManifest manifest)
    {
        var renderer = new SpecializedNativeSvgRenderer();
        return renderer.Render(manifest) ?? string.Empty;
    }

    [Fact]
    public void Treemap_ColorMapping_SetsIndependentTileFill()
    {
        var script = @"CREATE VISUAL V AS TREEMAP (
            SOURCE = #t,
            MAPPINGS (
                NAME  = Category,
                VALUE = Amount,
                COLOR = HexCode
            )
        );";

        var manifest = BuildManifest(
            script,
            "TREEMAP",
            ["Category", "Amount", "HexCode"],
            [
                ["Software", "100", "#2563eb"],
                ["Hardware", "80", "#10b981"]
            ]);

        var svg = RenderSvg(manifest);

        Assert.Contains("fill='#2563eb'", svg);
        Assert.Contains("fill='#10b981'", svg);
    }

    [Fact]
    public void Treemap_ShowBreadcrumb_RendersBreadcrumbHeader()
    {
        var script = @"CREATE VISUAL V AS TREEMAP (
            SOURCE = #t,
            MAPPINGS (
                NAME  = Category,
                VALUE = Amount
            ),
            OPTIONS (
                SHOW_BREADCRUMB = ON
            )
        );";

        var manifest = BuildManifest(
            script,
            "TREEMAP",
            ["Category", "Amount"],
            [
                ["Alpha", "100"],
                ["Beta", "50"]
            ]);

        var svg = RenderSvg(manifest);

        Assert.Contains("class='hierarchy-breadcrumb'", svg);
        Assert.Contains("data-show-breadcrumb='true'", svg);
        Assert.Contains("All &gt;", svg);
    }

    [Fact]
    public void Treemap_LabelMinSize_SuppressesLabelsOnSmallTiles()
    {
        var script = @"CREATE VISUAL V AS TREEMAP (
            SOURCE = #t,
            MAPPINGS (
                NAME  = Category,
                VALUE = Amount
            ),
            OPTIONS (
                LABEL_MIN_SIZE = 500
            )
        );";

        var manifest = BuildManifest(
            script,
            "TREEMAP",
            ["Category", "Amount"],
            [
                ["Alpha", "100"],
                ["Beta", "50"]
            ]);

        var svg = RenderSvg(manifest);

        // When min size is larger than any tile, no tile labels should be rendered
        Assert.DoesNotContain("class='treemap-label'", svg);
    }

    [Fact]
    public void Treemap_LabelOverflow_Wrap_GeneratesTspans()
    {
        var script = @"CREATE VISUAL V AS TREEMAP (
            SOURCE = #t,
            MAPPINGS (
                NAME  = Category,
                VALUE = Amount
            ),
            OPTIONS (
                LABEL_MIN_SIZE = 20,
                LABEL_OVERFLOW = WRAP
            )
        );";

        var manifest = BuildManifest(
            script,
            "TREEMAP",
            ["Category", "Amount"],
            [
                ["Enterprise Cloud Infrastructure Platform", "200"],
                ["Hardware", "200"],
                ["Services", "200"],
                ["Software", "200"],
                ["Consulting", "200"]
            ]);

        var svg = RenderSvg(manifest);

        Assert.Contains("data-overflow='wrap'", svg);
        Assert.Contains("<tspan", svg);
    }

    [Fact]
    public void Treemap_LabelOverflow_Hidden_SuppressesOverflowLabels()
    {
        var script = @"CREATE VISUAL V AS TREEMAP (
            SOURCE = #t,
            MAPPINGS (
                NAME  = Category,
                VALUE = Amount
            ),
            OPTIONS (
                LABEL_MIN_SIZE = 20,
                LABEL_OVERFLOW = HIDDEN
            )
        );";

        // Extremely long title in a small tile
        var manifest = BuildManifest(
            script,
            "TREEMAP",
            ["Category", "Amount"],
            [
                ["ExtremelyLongCategoryNameThatWillExceedAnyNormalTileBoundaryEasily", "10"],
                ["Tiny", "1000"]
            ]);

        var svg = RenderSvg(manifest);

        // The long label should be suppressed from tile labels (not rendered as a treemap-label)
        Assert.DoesNotContain("class='treemap-label' data-overflow='clip'>ExtremelyLong", svg);
        Assert.DoesNotContain("class='treemap-label' data-overflow='wrap'>ExtremelyLong", svg);
        // The short label fits and is rendered
        Assert.Contains("class='treemap-label'", svg);
        Assert.Contains("Tiny", svg);
    }

    [Fact]
    public void Sunburst_ColorMapping_SetsIndependentWedgeFill()
    {
        var script = @"CREATE VISUAL V AS SUNBURST (
            SOURCE = #t,
            MAPPINGS (
                LEVEL1 = Cat,
                LEVEL2 = SubCat,
                VALUE  = Amount,
                COLOR  = HexCode
            )
        );";

        var manifest = BuildManifest(
            script,
            "SUNBURST",
            ["Cat", "SubCat", "Amount", "HexCode"],
            [
                ["Tech", "Mobile", "100", "#3b82f6"],
                ["Tech", "Cloud", "80", "#06b6d4"]
            ]);

        var svg = RenderSvg(manifest);

        Assert.Contains("fill='#3b82f6'", svg);
        Assert.Contains("fill='#06b6d4'", svg);
    }

    [Fact]
    public void Sunburst_ShowBreadcrumb_RendersBreadcrumbHeader()
    {
        var script = @"CREATE VISUAL V AS SUNBURST (
            SOURCE = #t,
            MAPPINGS (
                LEVEL1 = Cat,
                LEVEL2 = SubCat,
                VALUE  = Amount
            ),
            OPTIONS (
                SHOW_BREADCRUMB = ON
            )
        );";

        var manifest = BuildManifest(
            script,
            "SUNBURST",
            ["Cat", "SubCat", "Amount"],
            [
                ["Tech", "Mobile", "100"],
                ["Retail", "Apparel", "80"]
            ]);

        var svg = RenderSvg(manifest);

        Assert.Contains("class='hierarchy-breadcrumb'", svg);
        Assert.Contains("data-show-breadcrumb='true'", svg);
        Assert.Contains("All &gt;", svg);
    }

    [Theory]
    [InlineData("TREEMAP", "SHOW_BREADCRUMB", "MAYBE", "Invalid SHOW_BREADCRUMB 'MAYBE'. Valid values are ON or OFF.")]
    [InlineData("SUNBURST", "SHOW_BREADCRUMB", "UNKNOWN", "Invalid SHOW_BREADCRUMB 'UNKNOWN'. Valid values are ON or OFF.")]
    [InlineData("TREEMAP", "LABEL_MIN_SIZE", "-10", "Invalid LABEL_MIN_SIZE '-10'. Value must be a non-negative number.")]
    [InlineData("TREEMAP", "LABEL_OVERFLOW", "SQUISH", "Invalid LABEL_OVERFLOW 'SQUISH'. Valid values are CLIP, WRAP, or HIDDEN.")]
    public void TreemapSunburst_InvalidOptions_ThrowDescriptiveExceptions(string visualType, string optKey, string optVal, string expectedMsg)
    {
        var script = $@"CREATE VISUAL V AS {visualType} (
            SOURCE = #t,
            MAPPINGS (NAME = Cat, VALUE = Amount, LEVEL1 = Cat),
            OPTIONS (
                {optKey} = '{optVal}'
            )
        );";

        var manifest = BuildManifest(
            script,
            visualType,
            ["Cat", "Amount"],
            [["Tech", "100"]]);

        var ex = Assert.Throws<InvalidOperationException>(() => RenderSvg(manifest));
        Assert.Contains(expectedMsg, ex.Message);
    }
}
