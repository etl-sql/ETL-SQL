using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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

public class HeatmapControlsTests
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
        var cols = columns ?? (statement.Mappings.Count > 0 ? statement.Mappings.Select(m => m.Column).ToList() : ["x_dim", "y_dim", "val"]);
        var defaultRows = rows ??
        [
            ["Jan", "North", "100"],
            ["Jan", "South", "200"],
            ["Feb", "North", "300"],
            ["Feb", "South", "400"]
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
    public void Heatmap_DivergingColorScale_WithMidpointAndColors_CreatesDivergingColorRange()
    {
        var script = @"CREATE VISUAL ProfitHeatmap AS HEATMAP (
            SOURCE = #matrix,
            MAPPINGS (X = month, Y = region, VALUE = profit),
            OPTIONS (
                COLOR_LOW = '#dc2626',
                COLOR_MID = '#ffffff',
                COLOR_HIGH = '#16a34a',
                MIDPOINT = 0
            )
        );";

        var rows = new List<List<string>>
        {
            new() { "Jan", "North", "-50" },
            new() { "Jan", "South", "100" },
            new() { "Feb", "North", "0" },
            new() { "Feb", "South", "50" }
        };

        var (spec, manifest) = ParseAndLower(script, rows, ["month", "region", "profit"]);
        var valueScale = spec.Scales.First(s => s.Channel == FieldChannel.Size);
        Assert.NotNull(valueScale.ColorRange);
        Assert.Equal(ColorRangeKind.Diverging, valueScale.ColorRange.Kind);
        Assert.Equal("#dc2626", valueScale.ColorRange.Low);
        Assert.Equal("#ffffff", valueScale.ColorRange.Mid);
        Assert.Equal("#16a34a", valueScale.ColorRange.High);
        Assert.Equal(0m, valueScale.ColorRange.Midpoint);

        var dataBuilder = new VisualChartDataBuilder();
        var data = dataBuilder.Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 600, 400));
        var svg = new SvgChartRenderer().Render(plan);

        Assert.NotNull(svg);
        Assert.Contains("plot-heat-cell", svg);
    }

    [Fact]
    public void Heatmap_PositionalColorsOption_SetsLowMidHigh()
    {
        var script = @"CREATE VISUAL CustomHeatmap AS HEATMAP (
            SOURCE = #matrix,
            MAPPINGS (X = col_x, Y = row_y, VALUE = metric),
            OPTIONS (
                COLORS = ('#ef4444', '#f8fafc', '#22c55e'),
                MIDPOINT = 50
            )
        );";

        var (spec, manifest) = ParseAndLower(script);
        var valueScale = spec.Scales.First(s => s.Channel == FieldChannel.Size);
        Assert.NotNull(valueScale.ColorRange);
        Assert.Equal(ColorRangeKind.Diverging, valueScale.ColorRange.Kind);
        Assert.Equal("#ef4444", valueScale.ColorRange.Low);
        Assert.Equal("#f8fafc", valueScale.ColorRange.Mid);
        Assert.Equal("#22c55e", valueScale.ColorRange.High);
        Assert.Equal(50m, valueScale.ColorRange.Midpoint);
    }

    [Fact]
    public void Heatmap_CellBorderOff_RemovesBordersAndGaps()
    {
        var script = @"CREATE VISUAL BorderlessHeatmap AS HEATMAP (
            SOURCE = #matrix,
            MAPPINGS (X = col_x, Y = row_y, VALUE = metric),
            OPTIONS (
                CELL_BORDER = OFF
            )
        );";

        var (spec, manifest) = ParseAndLower(script);
        var dataBuilder = new VisualChartDataBuilder();
        var data = dataBuilder.Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 600, 400));
        var svg = new SvgChartRenderer().Render(plan);

        Assert.NotNull(svg);
        Assert.DoesNotContain("stroke=", svg);
    }

    [Fact]
    public void Heatmap_CellBorderCustomColorAndWidth_AppliesStrokeToCells()
    {
        var script = @"CREATE VISUAL BorderedHeatmap AS HEATMAP (
            SOURCE = #matrix,
            MAPPINGS (X = col_x, Y = row_y, VALUE = metric),
            OPTIONS (
                CELL_BORDER = ON,
                CELL_BORDER_COLOR = '#334155',
                CELL_BORDER_WIDTH = 2
            )
        );";

        var (spec, manifest) = ParseAndLower(script);
        var dataBuilder = new VisualChartDataBuilder();
        var data = dataBuilder.Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 600, 400));
        var svg = new SvgChartRenderer().Render(plan);

        Assert.NotNull(svg);
        Assert.Contains("stroke='#334155'", svg);
        Assert.Contains("stroke-width='2'", svg);
    }

    [Fact]
    public void Heatmap_XSortAlpha_SortsXCategoriesAlphabetically()
    {
        var script = @"CREATE VISUAL SortedXHeatmap AS HEATMAP (
            SOURCE = #matrix,
            MAPPINGS (X = col_x, Y = row_y, VALUE = metric),
            OPTIONS (
                X_SORT = ALPHA
            )
        );";

        var rows = new List<List<string>>
        {
            new() { "Zebra", "R1", "10" },
            new() { "Apple", "R1", "20" },
            new() { "Mango", "R1", "30" }
        };

        var (spec, manifest) = ParseAndLower(script, rows, ["col_x", "row_y", "metric"]);
        var dataBuilder = new VisualChartDataBuilder();
        var data = dataBuilder.Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 600, 400));

        var xScale = plan.Scales.First(s => s.Channel == FieldChannel.X);
        Assert.Equal(new[] { "Apple", "Mango", "Zebra" }, xScale.Categories.ToArray());
    }

    [Fact]
    public void Heatmap_XSortValueDesc_SortsXCategoriesByDescendingSum()
    {
        var script = @"CREATE VISUAL SortedXValHeatmap AS HEATMAP (
            SOURCE = #matrix,
            MAPPINGS (X = col_x, Y = row_y, VALUE = metric),
            OPTIONS (
                X_SORT = VALUE_DESC
            )
        );";

        var rows = new List<List<string>>
        {
            new() { "CatA", "R1", "10" },
            new() { "CatA", "R2", "5" },
            new() { "CatB", "R1", "100" },
            new() { "CatB", "R2", "50" },
            new() { "CatC", "R1", "30" },
            new() { "CatC", "R2", "10" }
        };

        var (spec, manifest) = ParseAndLower(script, rows, ["col_x", "row_y", "metric"]);
        var dataBuilder = new VisualChartDataBuilder();
        var data = dataBuilder.Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 600, 400));

        var xScale = plan.Scales.First(s => s.Channel == FieldChannel.X);
        Assert.Equal(new[] { "CatB", "CatC", "CatA" }, xScale.Categories.ToArray());
    }

    [Fact]
    public void Heatmap_YSortAlpha_SortsYCategoriesAlphabetically()
    {
        var script = @"CREATE VISUAL SortedYHeatmap AS HEATMAP (
            SOURCE = #matrix,
            MAPPINGS (X = col_x, Y = row_y, VALUE = metric),
            OPTIONS (
                Y_SORT = ALPHA
            )
        );";

        var rows = new List<List<string>>
        {
            new() { "C1", "Zulu", "10" },
            new() { "C1", "Alpha", "20" },
            new() { "C1", "Bravo", "30" }
        };

        var (spec, manifest) = ParseAndLower(script, rows, ["col_x", "row_y", "metric"]);
        var dataBuilder = new VisualChartDataBuilder();
        var data = dataBuilder.Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 600, 400));

        var yScale = plan.Scales.First(s => s.Channel == FieldChannel.Y);
        Assert.Equal(new[] { "Alpha", "Bravo", "Zulu" }, yScale.Categories.ToArray());
    }

    [Fact]
    public void Heatmap_YSortValueDesc_SortsYCategoriesByDescendingSum()
    {
        var script = @"CREATE VISUAL SortedYValHeatmap AS HEATMAP (
            SOURCE = #matrix,
            MAPPINGS (X = col_x, Y = row_y, VALUE = metric),
            OPTIONS (
                Y_SORT = VALUE_DESC
            )
        );";

        var rows = new List<List<string>>
        {
            new() { "C1", "R1", "10" },
            new() { "C2", "R1", "20" },
            new() { "C1", "R2", "100" },
            new() { "C2", "R2", "200" },
            new() { "C1", "R3", "50" },
            new() { "C2", "R3", "40" }
        };

        var (spec, manifest) = ParseAndLower(script, rows, ["col_x", "row_y", "metric"]);
        var dataBuilder = new VisualChartDataBuilder();
        var data = dataBuilder.Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 600, 400));

        var yScale = plan.Scales.First(s => s.Channel == FieldChannel.Y);
        Assert.Equal(new[] { "R2", "R3", "R1" }, yScale.Categories.ToArray());
    }

    [Fact]
    public void Heatmap_NullColor_RendersExplicitColorOnMissingIntersection()
    {
        var script = @"CREATE VISUAL SparseHeatmap AS HEATMAP (
            SOURCE = #matrix,
            MAPPINGS (X = col_x, Y = row_y, VALUE = metric),
            OPTIONS (
                NULL_COLOR = '#64748b'
            )
        );";

        var rows = new List<List<string>>
        {
            new() { "C1", "R1", "10" },
            new() { "C2", "R1", "20" },
            new() { "C1", "R2", "30" }
        };

        var (spec, manifest) = ParseAndLower(script, rows, ["col_x", "row_y", "metric"]);
        var dataBuilder = new VisualChartDataBuilder();
        var data = dataBuilder.Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 600, 400));
        var svg = new SvgChartRenderer().Render(plan);

        Assert.NotNull(svg);
        Assert.Contains("plot-heat-cell plot-heat-cell-null", svg);
        Assert.Contains("fill='#64748b'", svg);
    }

    [Fact]
    public void Heatmap_InvalidOptions_ThrowDescriptiveExceptions()
    {
        var invalidMidpointScript = @"CREATE VISUAL V1 AS HEATMAP (
            SOURCE = #data,
            MAPPINGS (X = a, Y = b, VALUE = c),
            OPTIONS (MIDPOINT = 'not_a_number')
        );";
        Assert.Throws<InvalidOperationException>(() => ParseAndLower(invalidMidpointScript));

        var midpointOnBarScript = @"CREATE VISUAL V2 AS BAR (
            SOURCE = #data,
            MAPPINGS (X = a, Y = b),
            OPTIONS (MIDPOINT = 0)
        );";
        Assert.Throws<InvalidOperationException>(() => ParseAndLower(midpointOnBarScript));

        var invalidBorderScript = @"CREATE VISUAL V3 AS HEATMAP (
            SOURCE = #data,
            MAPPINGS (X = a, Y = b, VALUE = c),
            OPTIONS (CELL_BORDER = MAYBE)
        );";
        Assert.Throws<InvalidOperationException>(() => ParseAndLower(invalidBorderScript));

        var invalidSortScript = @"CREATE VISUAL V4 AS HEATMAP (
            SOURCE = #data,
            MAPPINGS (X = a, Y = b, VALUE = c),
            OPTIONS (X_SORT = RANDOM)
        );";
        Assert.Throws<InvalidOperationException>(() => ParseAndLower(invalidSortScript));
    }
}
