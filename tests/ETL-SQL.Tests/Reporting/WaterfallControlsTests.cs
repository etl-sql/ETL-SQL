using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

public class WaterfallControlsTests
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

    private static (ChartSpec Spec, VisualManifest Manifest) ParseAndLower(string script, List<List<string>>? rows = null, List<string>? columns = null)
    {
        var statement = ParseVisual(script);
        var cols = columns ?? (statement.Mappings.Count > 0 ? statement.Mappings.Select(m => m.Column).ToList() : ["item", "amount"]);
        var defaultRows = rows ??
        [
            ["Opening", "50", "1", "0"],
            ["Revenue", "30", "0", "0"],
            ["COGS", "-20", "0", "0"],
            ["Gross Margin", "0", "0", "1"],
            ["OpEx", "-15", "0", "0"],
            ["Closing", "45", "1", "0"]
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

    [Fact]
    public void Waterfall_Default_RendersVerticalWithConnectorLinesAndDefaultColors()
    {
        var script = @"CREATE VISUAL DefaultWF AS WATERFALL (
            SOURCE = #data,
            MAPPINGS (NAME = item, VALUE = amount, TOTAL = is_total)
        );";

        var rows = new List<List<string>>
        {
            new() { "Opening", "100", "1" },
            new() { "Sales", "50", "0" },
            new() { "Discounts", "-20", "0" },
            new() { "Final", "130", "1" }
        };

        var (spec, manifest) = ParseAndLower(script, rows, ["item", "amount", "is_total"]);
        Assert.Equal(CoordinateKind.Cartesian, spec.Coordinate.Kind);

        var dataBuilder = new VisualChartDataBuilder();
        var data = dataBuilder.Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 600, 400));
        var svg = new SvgChartRenderer().Render(plan);

        Assert.NotNull(svg);
        Assert.Contains("stroke='#9ca3af' stroke-dasharray='3 2'", svg);
        Assert.Contains("fill='#2980b9'", svg); // Total color
        Assert.Contains("fill='#27ae60'", svg); // Positive color
        Assert.Contains("fill='#e74c3c'", svg); // Negative color
    }

    [Fact]
    public void Waterfall_ConnectorLinesOff_OmitsConnectorLines()
    {
        var script = @"CREATE VISUAL NoConnectorsWF AS WATERFALL (
            SOURCE = #data,
            MAPPINGS (NAME = item, VALUE = amount, TOTAL = is_total),
            OPTIONS (
                CONNECTOR_LINES = OFF
            )
        );";

        var rows = new List<List<string>>
        {
            new() { "Start", "50", "1" },
            new() { "Add", "20", "0" },
            new() { "End", "70", "1" }
        };

        var (spec, manifest) = ParseAndLower(script, rows, ["item", "amount", "is_total"]);
        var dataBuilder = new VisualChartDataBuilder();
        var data = dataBuilder.Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 600, 400));
        var svg = new SvgChartRenderer().Render(plan);

        Assert.NotNull(svg);
        Assert.DoesNotContain("stroke-dasharray='3 2'", svg);
    }

    [Fact]
    public void Waterfall_ConnectorLinesCustomColorAndWidth_AppliesToLines()
    {
        var script = @"CREATE VISUAL CustomConnectorsWF AS WATERFALL (
            SOURCE = #data,
            MAPPINGS (NAME = item, VALUE = amount, TOTAL = is_total),
            OPTIONS (
                CONNECTOR_LINES = ON,
                CONNECTOR_LINE_COLOR = '#6366f1',
                CONNECTOR_LINE_WIDTH = 2
            )
        );";

        var rows = new List<List<string>>
        {
            new() { "Start", "50", "1" },
            new() { "Add", "20", "0" },
            new() { "End", "70", "1" }
        };

        var (spec, manifest) = ParseAndLower(script, rows, ["item", "amount", "is_total"]);
        var dataBuilder = new VisualChartDataBuilder();
        var data = dataBuilder.Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 600, 400));
        var svg = new SvgChartRenderer().Render(plan);

        Assert.NotNull(svg);
        Assert.Contains("stroke='#6366f1'", svg);
        Assert.Contains("stroke-width='2'", svg);
    }

    [Fact]
    public void Waterfall_SubtotalMapping_RendersSubtotalBarAndPreservesRunningTotal()
    {
        var script = @"CREATE VISUAL SubtotalBridge AS WATERFALL (
            SOURCE = #data,
            MAPPINGS (NAME = item, VALUE = amount, TOTAL = is_tot, SUBTOTAL = is_sub)
        );";

        var rows = new List<List<string>>
        {
            new() { "Gross Sales", "100", "0", "0" },
            new() { "Returns", "-10", "0", "0" },
            new() { "Net Sales", "0", "0", "1" }, // Subtotal: delta=0, ends at running 90
            new() { "Shipping", "15", "0", "0" }, // Continues from 90 -> 105
            new() { "Total", "105", "1", "0" }   // Total: ends at 105
        };

        var (spec, manifest) = ParseAndLower(script, rows, ["item", "amount", "is_tot", "is_sub"]);
        var dataBuilder = new VisualChartDataBuilder();
        var data = dataBuilder.Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 600, 400));

        var layer = plan.Layers.First();
        // Row 0: Gross Sales 0 -> 100
        Assert.Equal(0m, PlotPlanResolver.Number(layer.Data[0].Channels.First(c => c.Channel == FieldChannel.YStart).Value));
        Assert.Equal(100m, PlotPlanResolver.Number(layer.Data[0].Channels.First(c => c.Channel == FieldChannel.YEnd).Value));

        // Row 1: Returns 100 -> 90
        Assert.Equal(100m, PlotPlanResolver.Number(layer.Data[1].Channels.First(c => c.Channel == FieldChannel.YStart).Value));
        Assert.Equal(90m, PlotPlanResolver.Number(layer.Data[1].Channels.First(c => c.Channel == FieldChannel.YEnd).Value));

        // Row 2: Net Sales (Subtotal) 0 -> 90
        Assert.Equal(0m, PlotPlanResolver.Number(layer.Data[2].Channels.First(c => c.Channel == FieldChannel.YStart).Value));
        Assert.Equal(90m, PlotPlanResolver.Number(layer.Data[2].Channels.First(c => c.Channel == FieldChannel.YEnd).Value));
        Assert.Equal("SUBTOTAL", layer.Data[2].Channels.First(c => c.Channel == FieldChannel.Text).DisplayValue);

        // Row 3: Shipping 90 -> 105
        Assert.Equal(90m, PlotPlanResolver.Number(layer.Data[3].Channels.First(c => c.Channel == FieldChannel.YStart).Value));
        Assert.Equal(105m, PlotPlanResolver.Number(layer.Data[3].Channels.First(c => c.Channel == FieldChannel.YEnd).Value));

        // Row 4: Total 0 -> 105
        Assert.Equal(0m, PlotPlanResolver.Number(layer.Data[4].Channels.First(c => c.Channel == FieldChannel.YStart).Value));
        Assert.Equal(105m, PlotPlanResolver.Number(layer.Data[4].Channels.First(c => c.Channel == FieldChannel.YEnd).Value));
        Assert.Equal("TOTAL", layer.Data[4].Channels.First(c => c.Channel == FieldChannel.Text).DisplayValue);

        var svg = new SvgChartRenderer().Render(plan);
        Assert.NotNull(svg);
        Assert.Contains("fill='#475569'", svg); // Default subtotal color
    }

    [Fact]
    public void Waterfall_SubtotalInTotalColumn_IdentifiedAsSubtotal()
    {
        var script = @"CREATE VISUAL SubtotalInColBridge AS WATERFALL (
            SOURCE = #data,
            MAPPINGS (NAME = item, VALUE = amount, TOTAL = total_type)
        );";

        var rows = new List<List<string>>
        {
            new() { "Rev A", "50", "" },
            new() { "Rev B", "30", "" },
            new() { "Subtotal", "0", "SUBTOTAL" },
            new() { "Taxes", "-10", "" },
            new() { "Net", "70", "TOTAL" }
        };

        var (spec, manifest) = ParseAndLower(script, rows, ["item", "amount", "total_type"]);
        var dataBuilder = new VisualChartDataBuilder();
        var data = dataBuilder.Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 600, 400));

        var layer = plan.Layers.First();
        Assert.Equal("SUBTOTAL", layer.Data[2].Channels.First(c => c.Channel == FieldChannel.Text).DisplayValue);
        Assert.Equal("TOTAL", layer.Data[4].Channels.First(c => c.Channel == FieldChannel.Text).DisplayValue);

        var svg = new SvgChartRenderer().Render(plan);
        Assert.NotNull(svg);
        Assert.Contains("fill='#475569'", svg); // Subtotal color
    }

    [Fact]
    public void Waterfall_HorizontalOrientation_RendersTransposedCartesianWithVerticalConnectors()
    {
        var script = @"CREATE VISUAL HorizontalWF AS WATERFALL (
            SOURCE = #data,
            MAPPINGS (NAME = item, VALUE = amount, TOTAL = is_total),
            OPTIONS (
                ORIENTATION = HORIZONTAL
            )
        );";

        var rows = new List<List<string>>
        {
            new() { "Start", "50", "1" },
            new() { "Add", "20", "0" },
            new() { "Drop", "-10", "0" },
            new() { "End", "60", "1" }
        };

        var (spec, manifest) = ParseAndLower(script, rows, ["item", "amount", "is_total"]);
        Assert.Equal(CoordinateKind.TransposedCartesian, spec.Coordinate.Kind);

        var dataBuilder = new VisualChartDataBuilder();
        var data = dataBuilder.Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 600, 400));
        var svg = new SvgChartRenderer().Render(plan);

        Assert.NotNull(svg);
        Assert.Contains("text-anchor='end'", svg); // Left category labels
        Assert.Contains("stroke-dasharray='3 2'", svg); // Vertical connectors
    }

    [Fact]
    public void Waterfall_CustomColors_AppliesToAllBarTypes()
    {
        var script = @"CREATE VISUAL ColoredWF AS WATERFALL (
            SOURCE = #data,
            MAPPINGS (NAME = item, VALUE = amount, TOTAL = is_tot, SUBTOTAL = is_sub),
            OPTIONS (
                COLOR_TOTAL = '#1e3a8a',
                COLOR_SUBTOTAL = '#0284c7',
                COLOR_UP = '#10b981',
                COLOR_DOWN = '#f43f5e'
            )
        );";

        var rows = new List<List<string>>
        {
            new() { "Start", "100", "0", "0" },
            new() { "Drop", "-20", "0", "0" },
            new() { "Mid", "0", "0", "1" },
            new() { "Final", "80", "1", "0" }
        };

        var (spec, manifest) = ParseAndLower(script, rows, ["item", "amount", "is_tot", "is_sub"]);
        var dataBuilder = new VisualChartDataBuilder();
        var data = dataBuilder.Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 600, 400));
        var svg = new SvgChartRenderer().Render(plan);

        Assert.NotNull(svg);
        Assert.Contains("fill='#10b981'", svg); // COLOR_UP
        Assert.Contains("fill='#f43f5e'", svg); // COLOR_DOWN
        Assert.Contains("fill='#0284c7'", svg); // COLOR_SUBTOTAL
        Assert.Contains("fill='#1e3a8a'", svg); // COLOR_TOTAL
    }

    [Fact]
    public void Waterfall_InvalidOptions_ThrowDescriptiveExceptions()
    {
        var invalidConnectorScript = @"CREATE VISUAL V1 AS WATERFALL (
            SOURCE = #data,
            MAPPINGS (NAME = item, VALUE = amount),
            OPTIONS (CONNECTOR_LINES = MAYBE)
        );";
        Assert.Throws<InvalidOperationException>(() => ParseAndLower(invalidConnectorScript));

        var connectorOnBarScript = @"CREATE VISUAL V2 AS BAR (
            SOURCE = #data,
            MAPPINGS (X = item, Y = amount),
            OPTIONS (CONNECTOR_LINES = ON)
        );";
        Assert.Throws<InvalidOperationException>(() => ParseAndLower(connectorOnBarScript));

        var invalidOrientationScript = @"CREATE VISUAL V3 AS WATERFALL (
            SOURCE = #data,
            MAPPINGS (NAME = item, VALUE = amount),
            OPTIONS (ORIENTATION = DIAGONAL)
        );";
        Assert.Throws<InvalidOperationException>(() => ParseAndLower(invalidOrientationScript));

        var orientationOnPieScript = @"CREATE VISUAL V4 AS PIE (
            SOURCE = #data,
            MAPPINGS (LABEL = item, VALUE = amount),
            OPTIONS (ORIENTATION = HORIZONTAL)
        );";
        Assert.Throws<InvalidOperationException>(() => ParseAndLower(orientationOnPieScript));
    }
}
