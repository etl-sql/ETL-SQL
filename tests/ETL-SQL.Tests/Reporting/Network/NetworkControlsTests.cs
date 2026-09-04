using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using Xunit;

namespace ETL_SQL.Tests.Reporting.Network;

public class NetworkControlsTests
{
    private static CreateVisualStatement ParseVisual(string script)
    {
        var lexer = new Lexer(script);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var statements = new List<Statement>();
        while (parser.Current.Type != TokenType.EOF) statements.Add(parser.ParseStatement());
        return (CreateVisualStatement)statements[0];
    }

    private static (VisualManifest Manifest, string Svg) RenderScript(
        string script,
        List<string>? columns = null,
        List<List<string?>>? rows = null)
    {
        var statement = ParseVisual(script);
        var defaultCols = columns ?? (statement.Mappings.Count > 0
            ? statement.Mappings.Select(m => m.Column).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : ["From", "To", "Weight"]);

        var defaultRows = rows ??
        [
            ["A", "B", "10"],
            ["B", "C", "20"],
            ["C", "D", "30"],
            ["D", "A", "40"]
        ];

        var manifest = new VisualManifest
        {
            Name = statement.Name,
            VisualType = statement.VisualType.ToString().ToUpperInvariant(),
            Columns = defaultCols,
            Rows = defaultRows
        };

        foreach (var opt in statement.Options)
        {
            manifest.Options[opt.Key] = opt.Value;
        }

        foreach (var m in statement.Mappings)
        {
            manifest.Options["mapping:" + m.Role.ToLowerInvariant()] = m.Column;
        }

        var renderer = new SvgChartRenderer();
        var svg = renderer.Render(manifest)!;
        return (manifest, svg);
    }

    [Fact]
    public void NodeSize_ScalesCircleRadiusProportionally()
    {
        var script = @"
CREATE VISUAL TestNetwork AS NETWORK (
    SOURCE = #data,
    MAPPINGS (
        FROM      = NodeFrom,
        TO        = NodeTo,
        VALUE     = Weight,
        NODE_SIZE = Metric
    ),
    OPTIONS (LAYOUT = CIRCULAR)
);";
        var cols = new List<string> { "NodeFrom", "NodeTo", "Weight", "Metric" };
        var rows = new List<List<string?>>
        {
            new() { "SmallNode", "Target1", "5", "10" },
            new() { "BigNode",   "Target2", "5", "100" }
        };

        var (_, svg) = RenderScript(script, cols, rows);

        Assert.NotNull(svg);
        Assert.Contains("<circle", svg);

        var rMatches = Regex.Matches(svg, @"<circle[^>]*\sr='(?<r>[0-9.]+)'");
        var radii = rMatches.Select(m => double.Parse(m.Groups["r"].Value, CultureInfo.InvariantCulture)).ToList();

        Assert.NotEmpty(radii);
        var minR = radii.Min();
        var maxR = radii.Max();

        // Smallest metric gets 5px, largest gets 24px
        Assert.Equal(5.0, minR, 1);
        Assert.Equal(24.0, maxR, 1);
    }

    [Fact]
    public void NodeSize_Unmapped_FallsBackToDefault9px()
    {
        var script = @"
CREATE VISUAL TestNetwork AS NETWORK (
    SOURCE = #data,
    MAPPINGS (
        FROM  = NodeFrom,
        TO    = NodeTo,
        VALUE = Weight
    )
);";
        var cols = new List<string> { "NodeFrom", "NodeTo", "Weight" };
        var rows = new List<List<string?>>
        {
            new() { "A", "B", "10" },
            new() { "B", "C", "20" }
        };

        var (_, svg) = RenderScript(script, cols, rows);

        Assert.NotNull(svg);
        var rMatches = Regex.Matches(svg, @"<circle[^>]*\sr='(?<r>[0-9.]+)'");
        Assert.NotEmpty(rMatches);
        foreach (Match m in rMatches)
        {
            var r = double.Parse(m.Groups["r"].Value, CultureInfo.InvariantCulture);
            Assert.Equal(9.0, r, 1);
        }
    }

    [Fact]
    public void Directed_RendersArrowheadMarkersAndTrimsEndpoint()
    {
        var script = @"
CREATE VISUAL DirectedGraph AS NETWORK (
    SOURCE = #data,
    MAPPINGS (
        FROM  = NodeFrom,
        TO    = NodeTo,
        VALUE = Weight
    ),
    OPTIONS (
        DIRECTED = ON,
        LAYOUT   = CIRCULAR
    )
);";
        var cols = new List<string> { "NodeFrom", "NodeTo", "Weight" };
        var rows = new List<List<string?>>
        {
            new() { "Src", "Dst", "15" }
        };

        var (_, svg) = RenderScript(script, cols, rows);

        Assert.Contains("<defs><marker id='arrow-", svg);
        Assert.Contains("marker-end='url(#arrow-", svg);
        Assert.Contains("orient='auto-start-reverse'", svg);
        Assert.Contains("Src → Dst: 15", svg);
    }

    [Fact]
    public void Undirected_DoesNotRenderMarkerAndUsesBidirectionalTitle()
    {
        var script = @"
CREATE VISUAL UndirectedGraph AS NETWORK (
    SOURCE = #data,
    MAPPINGS (
        FROM  = NodeFrom,
        TO    = NodeTo,
        VALUE = Weight
    ),
    OPTIONS (
        DIRECTED = OFF
    )
);";
        var cols = new List<string> { "NodeFrom", "NodeTo", "Weight" };
        var rows = new List<List<string?>>
        {
            new() { "Node1", "Node2", "10" }
        };

        var (_, svg) = RenderScript(script, cols, rows);

        Assert.DoesNotContain("<marker", svg);
        Assert.DoesNotContain("marker-end", svg);
        Assert.Contains("Node1 ↔ Node2: 10", svg);
    }

    [Fact]
    public void NodeLabels_CanBeToggledOff()
    {
        var script = @"
CREATE VISUAL NoLabelsGraph AS NETWORK (
    SOURCE = #data,
    MAPPINGS (
        FROM  = NodeFrom,
        TO    = NodeTo,
        VALUE = Weight
    ),
    OPTIONS (
        NODE_LABELS = OFF
    )
);";
        var cols = new List<string> { "NodeFrom", "NodeTo", "Weight" };
        var rows = new List<List<string?>>
        {
            new() { "Alpha", "Beta", "5" }
        };

        var (_, svg) = RenderScript(script, cols, rows);

        // Canvas title text is allowed, but node labels (Alpha, Beta) should not appear in <text>
        Assert.DoesNotContain(">Alpha<", svg);
        Assert.DoesNotContain(">Beta<", svg);
    }

    [Fact]
    public void NodeLabels_MinSizeThreshold_HidesLabelsForSmallNodes()
    {
        var script = @"
CREATE VISUAL ThresholdGraph AS NETWORK (
    SOURCE = #data,
    MAPPINGS (
        FROM      = NodeFrom,
        TO        = NodeTo,
        VALUE     = Weight,
        NODE_SIZE = Metric
    ),
    OPTIONS (
        NODE_LABELS         = ON,
        NODE_LABEL_MIN_SIZE = 15
    )
);";
        var cols = new List<string> { "NodeFrom", "NodeTo", "Weight", "Metric" };
        var rows = new List<List<string?>>
        {
            new() { "TinyNode",  "Target1", "5", "5" },
            new() { "HugeNode",  "Target2", "5", "100" }
        };

        var (_, svg) = RenderScript(script, cols, rows);

        // TinyNode (r = 5px < 15px) must NOT have a text label
        Assert.DoesNotContain(">TinyNode<", svg);
        // HugeNode (r = 24px >= 15px) MUST have a text label
        Assert.Contains(">HugeNode<", svg);
    }

    [Fact]
    public void Layout_ForceIsDeterministic()
    {
        var script = @"
CREATE VISUAL ForceGraph AS NETWORK (
    SOURCE = #data,
    MAPPINGS (
        FROM  = NodeFrom,
        TO    = NodeTo,
        VALUE = Weight
    ),
    OPTIONS (
        LAYOUT    = FORCE,
        REPULSION = 700
    )
);";
        var cols = new List<string> { "NodeFrom", "NodeTo", "Weight" };
        var rows = new List<List<string?>>
        {
            new() { "A", "B", "10" },
            new() { "B", "C", "20" },
            new() { "C", "A", "15" },
            new() { "D", "B", "5" }
        };

        var (_, svg1) = RenderScript(script, cols, rows);
        var (_, svg2) = RenderScript(script, cols, rows);

        Assert.NotNull(svg1);
        Assert.Equal(svg1, svg2);
    }

    [Fact]
    public void FixedCoordinates_PinsNodePositions()
    {
        var script = @"
CREATE VISUAL PinnedGraph AS NETWORK (
    SOURCE = #data,
    MAPPINGS (
        FROM   = NodeFrom,
        TO     = NodeTo,
        VALUE  = Weight,
        NODE_X = PosX,
        NODE_Y = PosY
    )
);";
        var cols = new List<string> { "NodeFrom", "NodeTo", "Weight", "PosX", "PosY" };
        var rows = new List<List<string?>>
        {
            new() { "StartNode", "EndNode", "10", "100", "200" },
            new() { "EndNode",   "ThirdNode", "10", "400", "200" }
        };

        var (_, svg) = RenderScript(script, cols, rows);

        Assert.NotNull(svg);
        // Positions are mapped to plot bounds with StartNode at minX and EndNode at maxX
        Assert.Contains("<circle cx='28' cy='", svg);
        Assert.Contains("<circle cx='572' cy='", svg);
    }

    [Fact]
    public void NodeGroup_AppliesDistinctColorsToCategories()
    {
        var script = @"
CREATE VISUAL GroupedGraph AS NETWORK (
    SOURCE = #data,
    MAPPINGS (
        FROM       = NodeFrom,
        TO         = NodeTo,
        VALUE      = Weight,
        NODE_GROUP = Category
    )
);";
        var cols = new List<string> { "NodeFrom", "NodeTo", "Weight", "Category" };
        var rows = new List<List<string?>>
        {
            new() { "Alice", "Bob", "10", "Sales" },
            new() { "Charlie", "David", "10", "Engineering" }
        };

        var (_, svg) = RenderScript(script, cols, rows);

        var fills = Regex.Matches(svg, @"<circle[^>]*\sfill='(?<fill>[^']+)'")
            .Select(m => m.Groups["fill"].Value)
            .Distinct()
            .ToList();

        // Distinct groups must receive different fill colors
        Assert.True(fills.Count >= 2);
    }

    [Fact]
    public async Task Linter_ValidatesRequiredRolesForNetwork()
    {
        var rule = new VisualMappingCompletenessRule();
        var context = new DefaultLintContext();

        // Missing TO
        var badSql = @"CREATE VISUAL NetIncomplete AS NETWORK (
            SOURCE = #t,
            MAPPINGS (FROM = Src)
        );";
        var badScript = ParseScript(badSql);
        var errors = (await rule.AnalyzeAsync(badScript, context)).ToList();
        Assert.Contains(errors, e => e.Message.Contains("missing the required mapping role: 'TO / TARGET'"));

        // Complete with FROM and TO
        var goodSql = @"CREATE VISUAL NetComplete AS NETWORK (
            SOURCE = #t,
            MAPPINGS (FROM = Src, TO = Dst)
        );";
        var goodScript = ParseScript(goodSql);
        var noErrors = (await rule.AnalyzeAsync(goodScript, context)).ToList();
        Assert.DoesNotContain(noErrors, e => e.Message.Contains("missing the required mapping role"));
    }

    private static Script ParseScript(string sql)
    {
        var lexer = new Lexer(sql);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens, sql);
        return parser.Parse();
    }
}
