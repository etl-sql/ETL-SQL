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

namespace ETL_SQL.Tests.Reporting.Candlestick;

public class CandlestickControlsTests
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
        var cols = columns ?? (statement.Mappings.Count > 0 ? statement.Mappings.Select(m => m.Column).ToList() : ["TradeDate", "OpenVal", "HighVal", "LowVal", "CloseVal"]);
        var defaultRows = rows ??
        [
            ["2026-05-01", "100", "110", "95", "105"],
            ["2026-05-02", "105", "115", "102", "103"],
            ["2026-05-03", "103", "112", "99", "111"],
            ["2026-05-04", "111", "114", "105", "108"]
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
    public void Candlestick_WickColor_Override_AppliesToAllShadows()
    {
        var script = @"CREATE VISUAL StyledCandle AS CANDLESTICK (
            SOURCE = #market,
            MAPPINGS (X = TradeDate, OPEN = OpenVal, HIGH = HighVal, LOW = LowVal, CLOSE = CloseVal),
            OPTIONS (
                WICK_COLOR = '#334155',
                COLOR_UP = '#16a34a',
                COLOR_DOWN = '#dc2626'
            )
        );";

        var svg = RenderToSvg(script);

        Assert.Contains("stroke='#334155'", svg);
        Assert.Contains("fill='#16a34a'", svg);
        Assert.Contains("fill='#dc2626'", svg);
    }

    [Fact]
    public void Candlestick_WickColor_Asymmetric_AppliesUpAndDown()
    {
        var script = @"CREATE VISUAL AsymCandle AS CANDLESTICK (
            SOURCE = #market,
            MAPPINGS (X = TradeDate, OPEN = OpenVal, HIGH = HighVal, LOW = LowVal, CLOSE = CloseVal),
            OPTIONS (
                COLOR_UP = '#10b981',
                COLOR_DOWN = '#f43f5e',
                WICK_COLOR_UP = '#047857',
                WICK_COLOR_DOWN = '#be123c'
            )
        );";

        var svg = RenderToSvg(script);

        Assert.Contains("stroke='#047857'", svg);
        Assert.Contains("stroke='#be123c'", svg);
    }

    [Fact]
    public void Candlestick_VolumeMapping_EmitsVolumeLayer_RendersBars()
    {
        var script = @"CREATE VISUAL VolumeCandle AS CANDLESTICK (
            SOURCE = #market,
            MAPPINGS (X = TradeDate, OPEN = OpenVal, HIGH = HighVal, LOW = LowVal, CLOSE = CloseVal, VOLUME = VolumeVal),
            OPTIONS (
                VOLUME_COLOR = '#64748b'
            )
        );";

        var cols = new List<string> { "TradeDate", "OpenVal", "HighVal", "LowVal", "CloseVal", "VolumeVal" };
        var rows = new List<List<string>>
        {
            new() { "2026-05-01", "100", "110", "95", "105", "12500" },
            new() { "2026-05-02", "105", "115", "102", "103", "18000" }
        };

        var (spec, _) = ParseAndLower(script, rows, cols);
        Assert.Equal(2, spec.Layers.Length);
        Assert.Equal("volume", spec.Layers[0].Id);
        Assert.Equal("primary", spec.Layers[1].Id);

        var svg = RenderToSvg(script, rows, cols);
        Assert.Contains("plot-volume-bars", svg);
        Assert.Contains("Volume: 12500", svg);
        Assert.Contains("Volume: 18000", svg);
        Assert.Contains("fill='#64748b'", svg);
    }

    [Fact]
    public void Candlestick_Overlays_MovingAverage_RendersPolyline()
    {
        var script = @"CREATE VISUAL MACandle AS CANDLESTICK (
            SOURCE = #market,
            MAPPINGS (X = TradeDate, OPEN = OpenVal, HIGH = HighVal, LOW = LowVal, CLOSE = CloseVal),
            OVERLAYS (
                MOVING_AVG(2) AS SOLID WITH (COLOR = '#f59e0b', LABEL = '2-Day SMA')
            )
        );";

        var svg = RenderToSvg(script);

        Assert.Contains("plot-overlay-line", svg);
        Assert.Contains("data-overlay-type='MovingAvg'", svg);
        Assert.Contains("stroke='#f59e0b'", svg);
    }

    [Fact]
    public void Candlestick_Overlays_Goal_RendersReferenceLine()
    {
        var script = @"CREATE VISUAL GoalCandle AS CANDLESTICK (
            SOURCE = #market,
            MAPPINGS (X = TradeDate, OPEN = OpenVal, HIGH = HighVal, LOW = LowVal, CLOSE = CloseVal),
            OVERLAYS (
                GOAL(110) AS DASHED WITH (COLOR = '#ef4444', LABEL = 'Target')
            )
        );";

        var svg = RenderToSvg(script);

        Assert.Contains("plot-reference-line", svg);
        Assert.Contains("stroke='#ef4444'", svg);
        Assert.Contains("Target: 110", svg);
    }

    [Fact]
    public void Candlestick_Overlays_ReferenceBand_RendersBand()
    {
        var script = @"CREATE VISUAL BandCandle AS CANDLESTICK (
            SOURCE = #market,
            MAPPINGS (X = TradeDate, OPEN = OpenVal, HIGH = HighVal, LOW = LowVal, CLOSE = CloseVal),
            OVERLAYS (
                REFERENCE_BAND (LOW = 100, HIGH = 105, COLOR = '#94a3b8', LABEL = 'Channel')
            )
        );";

        var svg = RenderToSvg(script);

        Assert.Contains("fill='#94a3b8'", svg);
        Assert.Contains("fill-opacity='0.2'", svg);
    }

    [Theory]
    [InlineData("COLOR_UP")]
    [InlineData("COLOR_DOWN")]
    [InlineData("WICK_COLOR")]
    [InlineData("WICK_COLOR_UP")]
    [InlineData("WICK_COLOR_DOWN")]
    [InlineData("VOLUME_COLOR")]
    public void Candlestick_EmptyOptions_ThrowDescriptiveExceptions(string optionKey)
    {
        var script = $@"CREATE VISUAL EmptyOptCandle AS CANDLESTICK (
            SOURCE = #market,
            MAPPINGS (X = TradeDate, OPEN = OpenVal, HIGH = HighVal, LOW = LowVal, CLOSE = CloseVal),
            OPTIONS (
                {optionKey} = '   '
            )
        );";

        var ex = Assert.Throws<InvalidOperationException>(() => ParseAndLower(script));
        Assert.Contains($"Candlestick option '{optionKey}' cannot be empty.", ex.Message);
    }
}
