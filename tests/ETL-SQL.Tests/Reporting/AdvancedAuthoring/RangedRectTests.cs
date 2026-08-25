using System;
using System.Collections.Immutable;
using System.Linq;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using ETL_SQL.Tests.Reporting.TerminalSemantics;

namespace ETL_SQL.Tests.Reporting.AdvancedAuthoring;

/// <summary>
/// Covers author-supplied interval endpoints on non-stacked <c>RECT</c> layers: a qualitative band, a
/// floating variance bar, and an explicit-bin histogram.
/// </summary>
/// <remarks>
/// Before this lane the channels parsed and lint stayed silent, but both rect paths in
/// <c>PlotPlanSvgRenderer</c> read <c>Y_START</c>/<c>Y_END</c> only under <c>STACK</c> and otherwise forced
/// the start endpoint to zero, so a ranged bar rendered from the baseline with the author's start
/// silently discarded. The geometry assertions below are exact because the plot box is fixed: the default
/// bounds are 600x350 with 60/20/40/60 padding, so the plot area is 520x250 with its origin at (60, 40).
/// </remarks>
public sealed class RangedRectTests
{
    private const decimal PlotLeft = 60m;
    private const decimal PlotTop = 40m;
    private const decimal PlotWidth = 520m;
    private const decimal PlotHeight = 250m;

    /// <summary>Maps a 0-100 domain value onto the vertical axis of the default plot box.</summary>
    private static decimal MapY(decimal value) => PlotTop + PlotHeight - (value / 100m * PlotHeight);

    /// <summary>Maps a 0-100 domain value onto the horizontal axis of the default plot box.</summary>
    private static decimal MapX(decimal value) => PlotLeft + (value / 100m * PlotWidth);

    private static string N(decimal value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void RangedRect_PlacesBothEndpointsInThePlanAndTheRenderedGeometry()
    {
        var plan = ResolveBands("CARTESIAN");
        var layer = plan.Layers.Single(item => item.Id == "band_fair");

        Assert.Equal(50m, PlotPlanResolver.Number(layer.Data[0].Channels.Single(channel => channel.Channel == FieldChannel.YStart).Value));
        Assert.Equal(75m, PlotPlanResolver.Number(layer.Data[0].Channels.Single(channel => channel.Channel == FieldChannel.YEnd).Value));

        var svg = new SvgChartRenderer().Render(plan);

        // The band spans 50..75 of a 0..100 domain: it starts a quarter of the way down and is a quarter tall.
        Assert.Contains($"y='{N(MapY(75m))}'", svg);
        Assert.Contains($"height='{N(MapY(50m) - MapY(75m))}'", svg);
        Assert.Contains("class='plot-range-rect'", svg);
        // A start endpoint discarded back to zero would produce a rect three times this tall.
        Assert.DoesNotContain($"height='{N(MapY(0m) - MapY(75m))}'", svg);
        Assert.Contains("<title>50 to 75</title>", svg);
    }

    [Fact]
    public void RangedRect_ScaleDomainIncludesBothEndpointsWithoutAnExplicitDomain()
    {
        const string sql = """
            CREATE VISUAL Variance AS CUSTOM (
              SOURCE = #variance,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (spread = RECT (ENCODINGS (
                  X = Period (TYPE = NOMINAL),
                  Y_START = Low (TYPE = QUANTITATIVE),
                  Y_END = High (TYPE = QUANTITATIVE)
                )))
              )
            );
            """;
        var manifest = new VisualManifest
        {
            Name = "Variance",
            Columns = ["Period", "Low", "High"],
            Rows = [["Q1", "-40", "12"], ["Q2", "6", "88"]]
        };
        var plan = Resolve(sql, manifest);

        var domain = plan.Scales.Single(scale => scale.Channel == FieldChannel.Y).Domain
            .Select(PlotPlanResolver.Number).ToArray();

        // Neither endpoint is a plain Y binding, so the domain proves both participate in inference.
        Assert.Equal(-40m, domain[0]);
        Assert.Equal(88m, domain[^1]);
    }

    [Fact]
    public void RangedRect_TransposedResolvesTheHorizontalSpanFromTheEndpoints()
    {
        var plan = ResolveBands("TRANSPOSED_CARTESIAN");

        var svg = new SvgChartRenderer().Render(plan);

        // The 0..50 band starts on the axis; the 50..75 band starts halfway across and is a quarter wide.
        // A discarded start endpoint would have drawn the second band from the axis too.
        Assert.Contains($"class='plot-range-rect' x='{N(MapX(0m))}' y=", svg);
        Assert.Contains($"width='{N(MapX(50m) - MapX(0m))}'", svg);
        Assert.Contains($"class='plot-range-rect' x='{N(MapX(50m))}' y=", svg);
        Assert.Contains($"width='{N(MapX(75m) - MapX(50m))}'", svg);
        Assert.DoesNotContain($"width='{N(MapX(75m) - MapX(0m))}'", svg);
    }

    [Fact]
    public void RangedX_ExplicitBinHistogram_DrawsAbuttingBinsWithoutACategoryBand()
    {
        const string sql = """
            CREATE VISUAL Histogram AS CUSTOM (
              SOURCE = #bins,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                SCALES (
                  bin_scale = LINEAR (CHANNEL = X, INCLUDE_ZERO = ON, MIN = 0, MAX = 100),
                  count_scale = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON, MIN = 0, MAX = 100)
                ),
                LAYERS (bins = RECT (ENCODINGS (
                  X_START = BinStart (TYPE = QUANTITATIVE, SCALE = bin_scale),
                  X_END = BinEnd (TYPE = QUANTITATIVE, SCALE = bin_scale),
                  Y = Frequency (TYPE = QUANTITATIVE, SCALE = count_scale)
                )))
              )
            );
            """;
        var manifest = new VisualManifest
        {
            Name = "Histogram",
            Columns = ["BinStart", "BinEnd", "Frequency"],
            Rows = [["0", "25", "40"], ["25", "50", "60"], ["50", "100", "20"]]
        };
        var plan = Resolve(sql, manifest);

        var svg = new SvgChartRenderer().Render(plan);

        // Bins own their horizontal extent, so they abut exactly and the last one is twice as wide.
        Assert.Contains($"class='plot-range-rect' x='{N(MapX(0m))}' y='{N(MapY(40m))}' width='{N(MapX(25m) - MapX(0m))}'", svg);
        Assert.Contains($"class='plot-range-rect' x='{N(MapX(25m))}' y='{N(MapY(60m))}' width='{N(MapX(50m) - MapX(25m))}'", svg);
        Assert.Contains($"class='plot-range-rect' x='{N(MapX(50m))}' y='{N(MapY(20m))}' width='{N(MapX(100m) - MapX(50m))}'", svg);
    }

    [Fact]
    public void RangedRect_TerminalFallbackAndSemanticFallbackKeepBothEndpoints()
    {
        var plan = ResolveBands("CARTESIAN");

        var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan, 100), 100).NormalizedText;
        Assert.Contains("ranged bars", terminal);
        Assert.Contains("50 to 75", terminal);

        var detail = plan.Fallback.Items.Select(item => item.Detail).Where(value => value is not null).ToList();
        Assert.Contains(detail, value => value!.Contains("range 50 to 75", StringComparison.Ordinal));
    }

    [Fact]
    public void StackedRect_KeepsResolverComputedBaselines_AndIgnoresTheRangedPath()
    {
        var plan = ResolveStacked();
        var svg = new SvgChartRenderer().Render(plan);

        var first = plan.Layers.Single(item => item.Id == "first");
        var second = plan.Layers.Single(item => item.Id == "second");

        // Stacking still synthesizes the endpoints, and the second layer starts where the first ends.
        Assert.Equal(0m, PlotPlanResolver.Number(first.Data[0].Channels.Single(channel => channel.Channel == FieldChannel.YStart).Value));
        Assert.Equal(30m, PlotPlanResolver.Number(first.Data[0].Channels.Single(channel => channel.Channel == FieldChannel.YEnd).Value));
        Assert.Equal(30m, PlotPlanResolver.Number(second.Data[0].Channels.Single(channel => channel.Channel == FieldChannel.YStart).Value));
        Assert.Equal(70m, PlotPlanResolver.Number(second.Data[0].Channels.Single(channel => channel.Channel == FieldChannel.YEnd).Value));

        // A stacked layer is never an authored range, so it keeps the plain rect markup and full band width.
        Assert.DoesNotContain("class='plot-range-rect'", svg);
        Assert.Contains($"y='{N(MapY(70m))}'", svg);
        Assert.Contains($"height='{N(MapY(30m) - MapY(70m))}'", svg);
    }

    [Fact]
    public void MixedStackedAndRangedLayers_KeepBothContributionsInTheDomain()
    {
        const string sql = """
            CREATE VISUAL Mixed AS CUSTOM (
              SOURCE = #mixed,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (
                  stacked = RECT (INHERIT_ENCODINGS = OFF, ENCODINGS (
                    X = Period (TYPE = NOMINAL),
                    Y = Amount (TYPE = QUANTITATIVE, STACK = ZERO)
                  )),
                  band = RECT (INHERIT_ENCODINGS = OFF, ENCODINGS (
                    X = Period (TYPE = NOMINAL),
                    Y_START = Low (TYPE = QUANTITATIVE),
                    Y_END = High (TYPE = QUANTITATIVE)
                  ))
                )
              )
            );
            """;
        var manifest = new VisualManifest
        {
            Name = "Mixed",
            Columns = ["Period", "Amount", "Low", "High"],
            Rows = [["Q1", "10", "-5", "42"], ["Q2", "20", "0", "18"]]
        };
        var plan = Resolve(sql, manifest);

        var domain = plan.Scales.Single(scale => scale.Channel == FieldChannel.Y).Domain
            .Select(PlotPlanResolver.Number).ToArray();

        // The stacked baselines replace the raw measures, but the ranged layer's endpoints still bound the axis.
        Assert.Equal(-5m, domain[0]);
        Assert.Equal(42m, domain[^1]);
    }

    [Theory]
    [InlineData("missing endpoint", "requires both endpoints in Y_START/Y_END")]
    [InlineData("mismatched types", "requires matching quantitative or temporal endpoint types")]
    [InlineData("value channel", "cannot combine Y or Y2 with Y_START/Y_END")]
    [InlineData("position channel", "cannot combine X or X2 with X_START/X_END")]
    [InlineData("stack on an endpoint", "STACK requires a quantitative")]
    public void ChartSpecContract_RejectsAmbiguousRangedRectShapes(string shape, string expected)
    {
        var x = new FieldBinding(FieldChannel.X, "Category", DataSemanticKind.Nominal, "x");
        var y = new FieldBinding(FieldChannel.Y, "Value", DataSemanticKind.Quantitative, "y");
        var start = new FieldBinding(FieldChannel.YStart, "Low", DataSemanticKind.Quantitative, "y");
        var end = new FieldBinding(FieldChannel.YEnd, "High", DataSemanticKind.Quantitative, "y");
        // The interval rules are asserted without scale ids where a scale-kind mismatch would fire first.
        var bindings = shape switch
        {
            "missing endpoint" => ImmutableArray.Create(x, start),
            "mismatched types" => ImmutableArray.Create(x,
                new FieldBinding(FieldChannel.YStart, "Low", DataSemanticKind.Quantitative),
                new FieldBinding(FieldChannel.YEnd, "High", DataSemanticKind.Nominal)),
            "value channel" => ImmutableArray.Create(x, y, start, end),
            "position channel" => ImmutableArray.Create(x, y,
                new FieldBinding(FieldChannel.XStart, "Low", DataSemanticKind.Quantitative),
                new FieldBinding(FieldChannel.XEnd, "High", DataSemanticKind.Quantitative)),
            "stack on an endpoint" => ImmutableArray.Create(x, start with { Stack = StackMode.Zero }, end),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        var failure = Assert.ThrowsAny<Exception>(() => RectSpec(bindings).Validate());

        Assert.Contains(expected, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChartSpecContract_AcceptsARangedRectWithoutAValueChannel()
    {
        var bindings = ImmutableArray.Create(
            new FieldBinding(FieldChannel.X, "Category", DataSemanticKind.Nominal, "x"),
            new FieldBinding(FieldChannel.YStart, "Low", DataSemanticKind.Quantitative, "y"),
            new FieldBinding(FieldChannel.YEnd, "High", DataSemanticKind.Quantitative, "y"));

        RectSpec(bindings).Validate();
    }

    private static ChartSpec RectSpec(ImmutableArray<FieldBinding> bindings) => ChartSpec.Create(
        "ranged", "#data", bindings,
        [new MarkLayerSpec("band", MarkKind.Rect, 0, bindings, [])],
        new CoordinateSpec(CoordinateKind.Cartesian),
        [new ScaleSpec("x", FieldChannel.X, ScaleKind.Band, false, []),
            new ScaleSpec("y", FieldChannel.Y, ScaleKind.Linear, false, [])],
        new FormattingSpec("en-US", "UTC", "", []),
        new NullHandlingSpec(NullValuePolicy.Gap, []),
        new ThemeSpec("default", []),
        new AccessibilitySpec("Ranged", null, null, true));

    /// <summary>Three abutting qualitative bands over a fixed 0-100 domain, as a bullet card would author them.</summary>
    private static PlotPlan ResolveBands(string coordinate)
    {
        var sql = $$"""
            CREATE VISUAL Bands AS CUSTOM (
              SOURCE = #bands,
              CHART (
                COORDINATE (TYPE = {{coordinate}}),
                SCALES (
                  metric_scale = BAND (CHANNEL = X, INCLUDE_ZERO = OFF, ORDER = SOURCE),
                  val_scale = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON, MIN = 0, MAX = 100)
                ),
                LAYERS (
                  band_poor = RECT (Z_INDEX = 1, ENCODINGS (
                    X = Metric (TYPE = NOMINAL, SCALE = metric_scale),
                    Y_START = PoorStart (TYPE = QUANTITATIVE, SCALE = val_scale),
                    Y_END = PoorEnd (TYPE = QUANTITATIVE, SCALE = val_scale)
                  )),
                  band_fair = RECT (Z_INDEX = 2, ENCODINGS (
                    X = Metric (TYPE = NOMINAL, SCALE = metric_scale),
                    Y_START = FairStart (TYPE = QUANTITATIVE, SCALE = val_scale),
                    Y_END = FairEnd (TYPE = QUANTITATIVE, SCALE = val_scale)
                  ))
                )
              )
            );
            """;
        var manifest = new VisualManifest
        {
            Name = "Bands",
            Columns = ["Metric", "PoorStart", "PoorEnd", "FairStart", "FairEnd"],
            Rows = [["Revenue", "0", "50", "50", "75"]]
        };
        return Resolve(sql, manifest);
    }

    private static PlotPlan ResolveStacked()
    {
        const string sql = """
            CREATE VISUAL Stacked AS CUSTOM (
              SOURCE = #stacked,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                SCALES (
                  metric_scale = BAND (CHANNEL = X, INCLUDE_ZERO = OFF, ORDER = SOURCE),
                  val_scale = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON, MIN = 0, MAX = 100)
                ),
                LAYERS (
                  first = RECT (INHERIT_ENCODINGS = OFF, ENCODINGS (
                    X = Metric (TYPE = NOMINAL, SCALE = metric_scale),
                    Y = Base (TYPE = QUANTITATIVE, SCALE = val_scale, STACK = ZERO)
                  )),
                  second = RECT (INHERIT_ENCODINGS = OFF, Z_INDEX = 1, ENCODINGS (
                    X = Metric (TYPE = NOMINAL, SCALE = metric_scale),
                    Y = Extra (TYPE = QUANTITATIVE, SCALE = val_scale, STACK = ZERO)
                  ))
                )
              )
            );
            """;
        var manifest = new VisualManifest
        {
            Name = "Stacked",
            Columns = ["Metric", "Base", "Extra"],
            Rows = [["Revenue", "30", "40"]]
        };
        return Resolve(sql, manifest);
    }

    private static PlotPlan Resolve(string sql, VisualManifest manifest)
    {
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return new PlotPlanResolver().Resolve(spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
