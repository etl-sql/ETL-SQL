using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using Xunit;

namespace ETL_SQL.Tests.Reporting.Sankey;

public class SankeyControlsTests
{
    private static VisualManifest BuildSankeyManifest(
        string script,
        List<List<string?>>? rows = null,
        List<string>? columns = null)
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
            VisualType = "SANKEY",
            Columns = columns ?? ["FromNode", "ToNode", "FlowValue"],
            Rows = rows ??
            [
                ["Total", "Eng", "100"],
                ["Total", "Sales", "80"],
                ["Eng", "Dev", "60"],
                ["Eng", "QA", "40"],
                ["Sales", "Direct", "50"],
                ["Sales", "Channel", "30"]
            ]
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

    private static string RenderSankeySvg(
        string script,
        List<List<string?>>? rows = null,
        List<string>? columns = null)
    {
        var manifest = BuildSankeyManifest(script, rows, columns);
        var renderer = new SpecializedNativeSvgRenderer();
        return renderer.Render(manifest) ?? string.Empty;
    }

    [Fact]
    public void Sankey_Default_JustifiesLeafNodes()
    {
        // Graph:
        // Root -> Intermediate -> LeafC
        // Root -> LeafD (direct sink from Root)
        var script = @"CREATE VISUAL V AS SANKEY (
            SOURCE = #flow,
            MAPPINGS (FROM = Src, TO = Dst, VALUE = Val)
        );";

        var rows = new List<List<string?>>
        {
            new() { "Root", "Intermediate", "100" },
            new() { "Intermediate", "LeafC", "100" },
            new() { "Root", "LeafD", "50" }
        };

        var svg = RenderSankeySvg(script, rows, ["Src", "Dst", "Val"]);

        Assert.Contains("data-node-align='justify'", svg);

        // Find rect X coordinates for LeafC and LeafD
        var leafCMatch = Regex.Match(svg, @"<rect x='([^']+)' y='[^']+' width='16' height='20' rx='2' fill='[^']+' data-node='LeafC'");
        var leafDMatch = Regex.Match(svg, @"<rect x='([^']+)' y='[^']+' width='16' height='20' rx='2' fill='[^']+' data-node='LeafD'");

        Assert.True(leafCMatch.Success, "LeafC rect should be present");
        Assert.True(leafDMatch.Success, "LeafD rect should be present");

        // In JUSTIFY mode, LeafD is pushed to the same rightmost column as LeafC
        Assert.Equal(leafCMatch.Groups[1].Value, leafDMatch.Groups[1].Value);
    }

    [Fact]
    public void Sankey_NodeAlign_Left_PositionsByTopologicalDepth()
    {
        var script = @"CREATE VISUAL V AS SANKEY (
            SOURCE = #flow,
            MAPPINGS (FROM = Src, TO = Dst, VALUE = Val),
            OPTIONS (
                NODE_ALIGN = LEFT
            )
        );";

        var rows = new List<List<string?>>
        {
            new() { "Root", "Intermediate", "100" },
            new() { "Intermediate", "LeafC", "100" },
            new() { "Root", "LeafD", "50" }
        };

        var svg = RenderSankeySvg(script, rows, ["Src", "Dst", "Val"]);

        Assert.Contains("data-node-align='left'", svg);

        var interMatch = Regex.Match(svg, @"<rect x='([^']+)' y='[^']+' width='16' height='20' rx='2' fill='[^']+' data-node='Intermediate'");
        var leafDMatch = Regex.Match(svg, @"<rect x='([^']+)' y='[^']+' width='16' height='20' rx='2' fill='[^']+' data-node='LeafD'");

        Assert.True(interMatch.Success);
        Assert.True(leafDMatch.Success);

        // In LEFT mode, LeafD has depth 1 (same depth as Intermediate)
        Assert.Equal(interMatch.Groups[1].Value, leafDMatch.Groups[1].Value);
    }

    [Fact]
    public void Sankey_NodeAlign_Right_SnapsToSinks()
    {
        var script = @"CREATE VISUAL V AS SANKEY (
            SOURCE = #flow,
            MAPPINGS (FROM = Src, TO = Dst, VALUE = Val),
            OPTIONS (
                NODE_ALIGN = RIGHT
            )
        );";

        var rows = new List<List<string?>>
        {
            new() { "Root", "Intermediate", "100" },
            new() { "Intermediate", "LeafC", "100" },
            new() { "Root", "LeafD", "50" }
        };

        var svg = RenderSankeySvg(script, rows, ["Src", "Dst", "Val"]);

        Assert.Contains("data-node-align='right'", svg);

        var leafCMatch = Regex.Match(svg, @"<rect x='([^']+)' y='[^']+' width='16' height='20' rx='2' fill='[^']+' data-node='LeafC'");
        var leafDMatch = Regex.Match(svg, @"<rect x='([^']+)' y='[^']+' width='16' height='20' rx='2' fill='[^']+' data-node='LeafD'");

        Assert.True(leafCMatch.Success);
        Assert.True(leafDMatch.Success);

        // In RIGHT mode, LeafD and LeafC are both sinks (height 0), so they align at maxRank
        Assert.Equal(leafCMatch.Groups[1].Value, leafDMatch.Groups[1].Value);
    }

    [Fact]
    public void Sankey_NodeAlign_Center_CentersIntermediateNodes()
    {
        var script = @"CREATE VISUAL V AS SANKEY (
            SOURCE = #flow,
            MAPPINGS (FROM = Src, TO = Dst, VALUE = Val),
            OPTIONS (
                NODE_ALIGN = CENTER
            )
        );";

        var rows = new List<List<string?>>
        {
            new() { "A", "B", "10" },
            new() { "B", "C", "10" }
        };

        var svg = RenderSankeySvg(script, rows, ["Src", "Dst", "Val"]);

        Assert.Contains("data-node-align='center'", svg);
    }

    [Fact]
    public void Sankey_LinkOpacity_ControlsStrokeOpacity()
    {
        var script = @"CREATE VISUAL V AS SANKEY (
            SOURCE = #flow,
            MAPPINGS (FROM = Src, TO = Dst, VALUE = Val),
            OPTIONS (
                LINK_OPACITY = 0.85
            )
        );";

        var rows = new List<List<string?>>
        {
            new() { "A", "B", "100" }
        };

        var svg = RenderSankeySvg(script, rows, ["Src", "Dst", "Val"]);

        Assert.Contains("data-link-opacity='0.85'", svg);
        Assert.Contains("stroke-opacity='0.85'", svg);
    }

    [Fact]
    public void Sankey_NodePadding_ControlsVerticalGap()
    {
        var script = @"CREATE VISUAL V AS SANKEY (
            SOURCE = #flow,
            MAPPINGS (FROM = Src, TO = Dst, VALUE = Val),
            OPTIONS (
                NODE_PADDING = 25
            )
        );";

        var rows = new List<List<string?>>
        {
            new() { "Root", "BranchA", "50" },
            new() { "Root", "BranchB", "50" }
        };

        var svg = RenderSankeySvg(script, rows, ["Src", "Dst", "Val"]);

        Assert.Contains("data-node-padding='25'", svg);

        var matchA = Regex.Match(svg, @"<rect x='[^']+' y='([^']+)' width='16' height='20' rx='2' fill='[^']+' data-node='BranchA'");
        var matchB = Regex.Match(svg, @"<rect x='[^']+' y='([^']+)' width='16' height='20' rx='2' fill='[^']+' data-node='BranchB'");

        Assert.True(matchA.Success);
        Assert.True(matchB.Success);

        var yA = double.Parse(matchA.Groups[1].Value);
        var yB = double.Parse(matchB.Groups[1].Value);

        // Height is 20, plus padding 25 => center-to-center or top-to-top distance should be 45
        Assert.Equal(45.0, Math.Abs(yB - yA), 1);
    }

    [Fact]
    public void Sankey_NodeColor_Mapping_SetsNodeFill()
    {
        var script = @"CREATE VISUAL V AS SANKEY (
            SOURCE = #flow,
            MAPPINGS (
                FROM       = Src,
                TO         = Dst,
                VALUE      = Val,
                NODE_COLOR = CustomColor
            )
        );";

        var rows = new List<List<string?>>
        {
            new() { "Engineering", "Infra", "100", "#2563eb" },
            new() { "Marketing", "Ads", "50", "#10b981" }
        };

        var svg = RenderSankeySvg(script, rows, ["Src", "Dst", "Val", "CustomColor"]);

        Assert.Contains("fill='#2563eb' data-node='Engineering'", svg);
        Assert.Contains("fill='#10b981' data-node='Marketing'", svg);
    }

    [Theory]
    [InlineData("NODE_ALIGN", "SLANTED", "Invalid NODE_ALIGN 'SLANTED'. Valid values are LEFT, RIGHT, CENTER, or JUSTIFY.")]
    [InlineData("LINK_OPACITY", "2.5", "Invalid LINK_OPACITY '2.5'. Valid values are between 0.0 and 1.0.")]
    [InlineData("LINK_OPACITY", "-0.1", "Invalid LINK_OPACITY '-0.1'. Valid values are between 0.0 and 1.0.")]
    [InlineData("NODE_PADDING", "-5", "Invalid NODE_PADDING '-5'. Value must be a non-negative number.")]
    public void Sankey_InvalidOptions_ThrowDescriptiveExceptions(string optKey, string optVal, string expectedMsg)
    {
        var script = $@"CREATE VISUAL BadSankey AS SANKEY (
            SOURCE = #flow,
            MAPPINGS (FROM = Src, TO = Dst, VALUE = Val),
            OPTIONS (
                {optKey} = '{optVal}'
            )
        );";

        var ex = Assert.Throws<InvalidOperationException>(() => RenderSankeySvg(script));
        Assert.Contains(expectedMsg, ex.Message);
    }
}
