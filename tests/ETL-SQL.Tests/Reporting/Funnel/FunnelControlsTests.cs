using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using Xunit;

namespace ETL_SQL.Tests.Reporting.Funnel;

public class FunnelControlsTests
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

    private static (ChartSpec Spec, VisualManifest Manifest) ParseAndLower(
        string script,
        List<List<string>>? rows = null,
        List<string>? columns = null)
    {
        var statement = ParseVisual(script);
        var cols = columns ?? ["Stage", "Count"];
        var defaultRows = rows ??
        [
            ["1. Prospects", "1000"],
            ["2. Qualified", "600"],
            ["3. Closed", "150"]
        ];

        var manifest = new VisualManifest
        {
            Name = statement.Name,
            VisualType = statement.VisualType.ToString().ToUpperInvariant(),
            Columns = cols,
            Rows = defaultRows
        };
        foreach (var opt in statement.Options)
        {
            manifest.Options[opt.Key] = opt.Value;
        }

        var lowerer = new NamedVisualChartLowerer();
        var spec = lowerer.Lower(statement, manifest);
        return (spec, manifest);
    }

    private static string RenderToSvg(string script, List<List<string>>? rows = null, List<string>? columns = null)
    {
        var (spec, manifest) = ParseAndLower(script, rows, columns);
        var dataBuilder = new VisualChartDataBuilder();
        var data = dataBuilder.Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 800, 500));
        return new SvgChartRenderer().Render(plan);
    }

    [Fact]
    public void Funnel_Default_SortsValueDescending()
    {
        var script = @"CREATE VISUAL V AS FUNNEL (
            SOURCE = #pipeline,
            MAPPINGS (LABEL = Stage, VALUE = Count)
        );";

        var rows = new List<List<string>>
        {
            new() { "Middle", "300" },
            new() { "Top", "1000" },
            new() { "Bottom", "50" }
        };

        var svg = RenderToSvg(script, rows);

        var topPos = svg.IndexOf("Top", StringComparison.Ordinal);
        var middlePos = svg.IndexOf("Middle", StringComparison.Ordinal);
        var bottomPos = svg.IndexOf("Bottom", StringComparison.Ordinal);

        Assert.True(topPos < middlePos, "Top (1000) should appear before Middle (300)");
        Assert.True(middlePos < bottomPos, "Middle (300) should appear before Bottom (50)");
    }

    [Fact]
    public void Funnel_SortSource_PreservesSourceOrder()
    {
        var script = @"CREATE VISUAL V AS FUNNEL (
            SOURCE = #pipeline,
            MAPPINGS (LABEL = Stage, VALUE = Count),
            OPTIONS (
                SORT = SOURCE
            )
        );";

        var rows = new List<List<string>>
        {
            new() { "Middle", "300" },
            new() { "Top", "1000" },
            new() { "Bottom", "50" }
        };

        var svg = RenderToSvg(script, rows);

        var middlePos = svg.IndexOf("Middle", StringComparison.Ordinal);
        var topPos = svg.IndexOf("Top", StringComparison.Ordinal);
        var bottomPos = svg.IndexOf("Bottom", StringComparison.Ordinal);

        Assert.True(middlePos < topPos, "Middle should precede Top in source order");
        Assert.True(topPos < bottomPos, "Top should precede Bottom in source order");
    }

    [Fact]
    public void Funnel_SortValueAsc_OrdersAscending()
    {
        var script = @"CREATE VISUAL V AS FUNNEL (
            SOURCE = #pipeline,
            MAPPINGS (LABEL = Stage, VALUE = Count),
            OPTIONS (
                SORT = VALUE_ASC
            )
        );";

        var rows = new List<List<string>>
        {
            new() { "Middle", "300" },
            new() { "Top", "1000" },
            new() { "Bottom", "50" }
        };

        var svg = RenderToSvg(script, rows);

        var bottomPos = svg.IndexOf("Bottom", StringComparison.Ordinal);
        var middlePos = svg.IndexOf("Middle", StringComparison.Ordinal);
        var topPos = svg.IndexOf("Top", StringComparison.Ordinal);

        Assert.True(bottomPos < middlePos, "Bottom (50) should precede Middle (300)");
        Assert.True(middlePos < topPos, "Middle (300) should precede Top (1000)");
    }

    [Fact]
    public void Funnel_ShowPercent_StepMode_CalculatesStepConversion()
    {
        var script = @"CREATE VISUAL V AS FUNNEL (
            SOURCE = #pipeline,
            MAPPINGS (LABEL = Stage, VALUE = Count),
            OPTIONS (
                SORT = SOURCE,
                SHOW_PERCENT = ON,
                PERCENT_MODE = STEP,
                DATA_LABELS = ON
            )
        );";

        var rows = new List<List<string>>
        {
            new() { "Leads", "1000" },
            new() { "Qualified", "500" },
            new() { "Won", "250" }
        };

        var svg = RenderToSvg(script, rows);

        Assert.Contains("data-percent-mode='step'", svg);
        Assert.Contains("data-percent='100'", svg);
        Assert.Contains("data-percent='50'", svg);
        // Won is 250 / 500 = 50% step conversion
        Assert.Contains("Won · 250 (50%)", svg);
    }

    [Fact]
    public void Funnel_ShowPercent_TotalMode_CalculatesTotalConversion()
    {
        var script = @"CREATE VISUAL V AS FUNNEL (
            SOURCE = #pipeline,
            MAPPINGS (LABEL = Stage, VALUE = Count),
            OPTIONS (
                SORT = SOURCE,
                SHOW_PERCENT = ON,
                PERCENT_MODE = TOTAL,
                DATA_LABELS = ON
            )
        );";

        var rows = new List<List<string>>
        {
            new() { "Leads", "1000" },
            new() { "Qualified", "500" },
            new() { "Won", "250" }
        };

        var svg = RenderToSvg(script, rows);

        Assert.Contains("data-percent-mode='total'", svg);
        Assert.Contains("Leads · 1000 (100%)", svg);
        Assert.Contains("Qualified · 500 (50%)", svg);
        // Won is 250 / 1000 = 25% total conversion
        Assert.Contains("Won · 250 (25%)", svg);
    }

    [Fact]
    public void Funnel_PyramidShape_InvertsTrapezoidGeometry()
    {
        var script = @"CREATE VISUAL V AS FUNNEL (
            SOURCE = #pipeline,
            MAPPINGS (LABEL = Stage, VALUE = Count),
            OPTIONS (
                FUNNEL_SHAPE = PYRAMID,
                SORT = SOURCE
            )
        );";

        var rows = new List<List<string>>
        {
            new() { "TopApex", "100" },
            new() { "MiddleTier", "400" },
            new() { "BaseTier", "1000" }
        };

        var svg = RenderToSvg(script, rows);

        Assert.Contains("data-shape='pyramid'", svg);
        Assert.Contains("<g class='plot-funnel' data-shape='pyramid'", svg);
    }

    [Theory]
    [InlineData("SORT", "RANDOM", "Invalid SORT 'RANDOM'. Valid values are SOURCE, VALUE_DESC, or VALUE_ASC.")]
    [InlineData("SHOW_PERCENT", "MAYBE", "Invalid SHOW_PERCENT 'MAYBE'. Valid values are ON or OFF.")]
    [InlineData("PERCENT_MODE", "DIAGONAL", "Invalid PERCENT_MODE 'DIAGONAL'. Valid values are STEP or TOTAL.")]
    [InlineData("FUNNEL_SHAPE", "CYLINDER", "Invalid FUNNEL_SHAPE 'CYLINDER'. Valid values are FUNNEL or PYRAMID.")]
    public void Funnel_InvalidOptions_ThrowDescriptiveExceptions(string optKey, string optVal, string expectedMsg)
    {
        var script = $@"CREATE VISUAL BadFunnel AS FUNNEL (
            SOURCE = #pipeline,
            MAPPINGS (LABEL = Stage, VALUE = Count),
            OPTIONS (
                {optKey} = '{optVal}'
            )
        );";

        var ex = Assert.Throws<InvalidOperationException>(() => ParseAndLower(script));
        Assert.Contains(expectedMsg, ex.Message);
    }
}
