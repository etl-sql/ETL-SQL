using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using ETL_SQL.Core;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using Xunit;

namespace ETL_SQL.Tests.Reporting.Conformance;

public sealed class StandardCatalogCartesianMigrationTests
{
    [Fact]
    public void BarAxisSort_UsesResolvedCategoryOrderForBandScaleAndMarks()
    {
        var bindings = ImmutableArray.Create(
            new FieldBinding(FieldChannel.X, "Month", DataSemanticKind.Nominal, "x", Sort: SortDirection.Descending),
            new FieldBinding(FieldChannel.Y, "Revenue", DataSemanticKind.Quantitative, "y"));
        var spec = ChartSpec.Create("sorted-bar", "#months", bindings,
            [new MarkLayerSpec("bars", MarkKind.Rect, 0, bindings, [])],
            new CoordinateSpec(CoordinateKind.Cartesian),
            [new ScaleSpec("x", FieldChannel.X, ScaleKind.Band, false, []), new ScaleSpec("y", FieldChannel.Y, ScaleKind.Linear, true, [])],
            new FormattingSpec("en-US", "UTC", "", []), new NullHandlingSpec(NullValuePolicy.Gap, []),
            new ThemeSpec("default", [new StyleToken("AXIS_SORT", "DESC"), new StyleToken("axis:x:label", "Month"), new StyleToken("axis:y:label", "Revenue")]),
            new AccessibilitySpec("Sorted", null, null, true));
        var data = ChartDataSet.Create("#months", 6,
        [
            new ChartColumn("Month", ChartValueKind.Text, DataSemanticKind.Nominal,
                [ChartValue.From("January"), ChartValue.From("February"), ChartValue.From("March"), ChartValue.From("April"), ChartValue.From("May"), ChartValue.From("June")], []),
            new ChartColumn("Revenue", ChartValueKind.Decimal, DataSemanticKind.Quantitative,
                [ChartValue.From(320m), ChartValue.From(290m), ChartValue.From(410m), ChartValue.From(380m), ChartValue.From(450m), ChartValue.From(500m)], [])
        ]);

        var plan = new PlotPlanResolver().Resolve(spec, data);

        var expected = new[] { "May", "March", "June", "January", "February", "April" };
        Assert.Equal(expected, plan.Scales.Single(scale => scale.Channel == FieldChannel.X).Categories);
        Assert.Equal(expected, plan.Layers.Single().Data.Select(datum =>
            PlotPlanResolver.Display(datum.Channels.Single(channel => channel.Channel == FieldChannel.X).Value)));
        var svg = new SvgChartRenderer().Render(plan);
        Assert.Contains(">Month</text>", svg);
        Assert.Contains(">Revenue</text>", svg);
    }

    [Fact]
    public async Task HorizontalBar_UsesTransposedPlotPlanAcrossBackends()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("hbar_native_plot_plan.rptsql");
        var visual = Assert.Single(manifest.Visuals);
        var plan = Assert.IsType<PlotPlan>(visual.PlotPlan);

        Assert.Equal(VisualType.HorizontalBar.ToString().ToUpperInvariant(), visual.VisualType);
        Assert.Equal(CoordinateKind.TransposedCartesian, plan.Coordinate?.Kind);
        Assert.Equal(MarkKind.Rect, Assert.Single(plan.Layers).Mark);
        Assert.Contains("<rect", new SvgChartRenderer().Render(plan));
    }

    [Fact]
    public void HorizontalBar_StackedSeriesShareSlotAndUseCumulativeHorizontalGeometry()
    {
        var bindings = ImmutableArray.Create(
            new FieldBinding(FieldChannel.X, "Month", DataSemanticKind.Nominal, "x"),
            new FieldBinding(FieldChannel.Y, "Revenue", DataSemanticKind.Quantitative, "y"),
            new FieldBinding(FieldChannel.Color, "Region", DataSemanticKind.Nominal, "color"));
        var spec = ChartSpec.Create("stacked-hbar", "#multi", bindings,
            [new MarkLayerSpec("bars", MarkKind.Rect, 0, bindings, [])],
            new CoordinateSpec(CoordinateKind.TransposedCartesian),
            [new ScaleSpec("x", FieldChannel.X, ScaleKind.Band, false, []),
                new ScaleSpec("y", FieldChannel.Y, ScaleKind.Linear, true, []),
                new ScaleSpec("color", FieldChannel.Color, ScaleKind.Ordinal, false, [])],
            new FormattingSpec("en-US", "UTC", "", []), new NullHandlingSpec(NullValuePolicy.Gap, []),
            new ThemeSpec("default", [new StyleToken("STACKED", "ON")]),
            new AccessibilitySpec("Stacked horizontal revenue", null, null, true));
        var data = ChartDataSet.Create("#multi", 4,
        [
            new ChartColumn("Month", ChartValueKind.Text, DataSemanticKind.Nominal,
                [ChartValue.From("Jan"), ChartValue.From("Jan"), ChartValue.From("Feb"), ChartValue.From("Feb")], []),
            new ChartColumn("Revenue", ChartValueKind.Decimal, DataSemanticKind.Quantitative,
                [ChartValue.From(100m), ChartValue.From(120m), ChartValue.From(150m), ChartValue.From(130m)], []),
            new ChartColumn("Region", ChartValueKind.Text, DataSemanticKind.Nominal,
                [ChartValue.From("North"), ChartValue.From("South"), ChartValue.From("North"), ChartValue.From("South")], [])
        ]);

        var plan = new PlotPlanResolver().Resolve(spec, data);
        var svg = new SvgChartRenderer().Render(plan);
        var northJan = RectGeometry(svg, 0);
        var southJan = RectGeometry(svg, 1);

        Assert.Equal(280m, PlotPlanResolver.Number(plan.Scales.Single(scale => scale.Channel == FieldChannel.Y).Domain[^1]));
        Assert.Equal(northJan.Y, southJan.Y);
        Assert.Equal(northJan.Height, southJan.Height);
        Assert.InRange(Math.Abs(southJan.X - (northJan.X + northJan.Width)), 0m, .002m);
    }

    [Fact]
    public void Line_SmoothPathCentersPointsAndAddsDomainHeadroom()
    {
        var bindings = ImmutableArray.Create(
            new FieldBinding(FieldChannel.X, "Date", DataSemanticKind.Nominal, "x"),
            new FieldBinding(FieldChannel.Y, "Total", DataSemanticKind.Quantitative, "y"));
        var spec = ChartSpec.Create("smooth-line", "#totals", bindings,
            [new MarkLayerSpec("primary", MarkKind.Line, 0, bindings, [])],
            new CoordinateSpec(CoordinateKind.Cartesian),
            [new ScaleSpec("x", FieldChannel.X, ScaleKind.Band, false, []),
                new ScaleSpec("y", FieldChannel.Y, ScaleKind.Linear, true, [])],
            new FormattingSpec("en-US", "UTC", "", []), new NullHandlingSpec(NullValuePolicy.Gap, []),
            new ThemeSpec("default", [new StyleToken("SMOOTH", "ON")]),
            new AccessibilitySpec("Smooth line", null, null, true));
        var data = ChartDataSet.Create("#totals", 5,
        [
            new ChartColumn("Date", ChartValueKind.Text, DataSemanticKind.Nominal,
                [ChartValue.From("Jan"), ChartValue.From("Feb"), ChartValue.From("Mar"), ChartValue.From("Apr"), ChartValue.From("May")], []),
            new ChartColumn("Total", ChartValueKind.Decimal, DataSemanticKind.Quantitative,
                [ChartValue.From(220m), ChartValue.From(280m), ChartValue.From(340m), ChartValue.From(430m), ChartValue.From(430m)], [])
        ]);

        var plan = new PlotPlanResolver().Resolve(spec, data);
        var svg = new SvgChartRenderer().Render(plan);

        Assert.Equal(451.5m, PlotPlanResolver.Number(plan.Scales.Single(scale => scale.Channel == FieldChannel.Y).Domain[^1]));
        Assert.Contains(" C ", svg);
        Assert.Contains("cx='112'", svg);
        Assert.Contains("cx='528'", svg);
    }

    [Fact]
    public void Line_StackedSeriesRenderCumulativeFilledBands()
    {
        var bindings = ImmutableArray.Create(
            new FieldBinding(FieldChannel.X, "Month", DataSemanticKind.Nominal, "x"),
            new FieldBinding(FieldChannel.Y, "Revenue", DataSemanticKind.Quantitative, "y"),
            new FieldBinding(FieldChannel.Color, "Region", DataSemanticKind.Nominal, "color"));
        var spec = ChartSpec.Create("stacked-line", "#multi", bindings,
            [new MarkLayerSpec("primary", MarkKind.Line, 0, bindings, [])],
            new CoordinateSpec(CoordinateKind.Cartesian),
            [new ScaleSpec("x", FieldChannel.X, ScaleKind.Band, false, []),
                new ScaleSpec("y", FieldChannel.Y, ScaleKind.Linear, true, []),
                new ScaleSpec("color", FieldChannel.Color, ScaleKind.Ordinal, false, [])],
            new FormattingSpec("en-US", "UTC", "", []), new NullHandlingSpec(NullValuePolicy.Gap, []),
            new ThemeSpec("default", [new StyleToken("STACKED", "ON")]),
            new AccessibilitySpec("Stacked line", null, null, true));
        var data = ChartDataSet.Create("#multi", 4,
        [
            new ChartColumn("Month", ChartValueKind.Text, DataSemanticKind.Nominal,
                [ChartValue.From("Jan"), ChartValue.From("Jan"), ChartValue.From("Feb"), ChartValue.From("Feb")], []),
            new ChartColumn("Revenue", ChartValueKind.Decimal, DataSemanticKind.Quantitative,
                [ChartValue.From(100m), ChartValue.From(120m), ChartValue.From(150m), ChartValue.From(130m)], []),
            new ChartColumn("Region", ChartValueKind.Text, DataSemanticKind.Nominal,
                [ChartValue.From("North"), ChartValue.From("South"), ChartValue.From("North"), ChartValue.From("South")], [])
        ]);

        var svg = new SvgChartRenderer().Render(new PlotPlanResolver().Resolve(spec, data));

        Assert.Equal(2, CountOccurrences(svg, "class='plot-stacked-area'"));
        Assert.Contains("data-series='North'", svg);
        Assert.Contains("data-series='South'", svg);
    }

    [Fact]
    public void Line_RegressionOverlaysResolveFittedValuesAndValidSvg()
    {
        var bindings = ImmutableArray.Create(
            new FieldBinding(FieldChannel.X, "Date", DataSemanticKind.Nominal, "x"),
            new FieldBinding(FieldChannel.Y, "Total", DataSemanticKind.Quantitative, "y"));
        var layers = ImmutableArray.Create(
            new MarkLayerSpec("primary", MarkKind.Line, 0, bindings, []),
            new MarkLayerSpec("linear", MarkKind.Line, 100, [],
                [new StyleToken("overlayType", "Linear"), new StyleToken("lineStyle", "dashed"), new StyleToken("color", "Purple"), new StyleToken("label", "Linear Trend")]),
            new MarkLayerSpec("polynomial", MarkKind.Line, 101, [],
                [new StyleToken("overlayType", "Polynomial"), new StyleToken("parameter", "2"), new StyleToken("lineStyle", "dotted"), new StyleToken("color", "Orange"), new StyleToken("label", "Polynomial Trend")]));
        var spec = ChartSpec.Create("regression-line", "#totals", bindings, layers,
            new CoordinateSpec(CoordinateKind.Cartesian),
            [new ScaleSpec("x", FieldChannel.X, ScaleKind.Band, false, []),
                new ScaleSpec("y", FieldChannel.Y, ScaleKind.Linear, true, [])],
            new FormattingSpec("en-US", "UTC", "", []), new NullHandlingSpec(NullValuePolicy.Gap, []),
            new ThemeSpec("default", []), new AccessibilitySpec("Regression line", null, null, true));
        var data = ChartDataSet.Create("#totals", 5,
        [
            new ChartColumn("Date", ChartValueKind.Text, DataSemanticKind.Nominal,
                [ChartValue.From("Jan"), ChartValue.From("Feb"), ChartValue.From("Mar"), ChartValue.From("Apr"), ChartValue.From("May")], []),
            new ChartColumn("Total", ChartValueKind.Decimal, DataSemanticKind.Quantitative,
                [ChartValue.From(220m), ChartValue.From(280m), ChartValue.From(340m), ChartValue.From(430m), ChartValue.From(430m)], [])
        ]);

        var plan = new PlotPlanResolver().Resolve(spec, data);
        var linear = plan.Layers.Single(layer => layer.Id == "linear").Data.Select(datum => Number(datum, FieldChannel.Y)).ToArray();
        var polynomial = plan.Layers.Single(layer => layer.Id == "polynomial").Data.Select(datum => Number(datum, FieldChannel.Y)).ToArray();
        var svg = new SvgChartRenderer().Render(plan);

        Assert.Equal(new decimal?[] { 226m, 283m, 340m, 397m, 454m }, linear);
        Assert.InRange(polynomial[0]!.Value, 213.14m, 213.15m);
        Assert.Contains("data-overlay-type='Linear'", svg);
        Assert.Contains("data-overlay-type='Polynomial'", svg);
        Assert.Equal(2, CountOccurrences(svg, "class='plot-overlay-label-bg'"));
        Assert.Equal(2, CountOccurrences(svg, "class='plot-overlay-label'"));
        Assert.Equal(2, CountOccurrences(svg, "class='plot-overlay-label-leader'"));
        Assert.Equal(2, CountOccurrences(svg, "text-anchor='start'"));
        XDocument.Parse(svg);
    }

    [Fact]
    public void Scatter_RegressionUsesQuantitativeXAndPadsPointDomain()
    {
        var bindings = ImmutableArray.Create(
            new FieldBinding(FieldChannel.X, "Score", DataSemanticKind.Quantitative, "x"),
            new FieldBinding(FieldChannel.Y, "Rank", DataSemanticKind.Quantitative, "y"));
        var layers = ImmutableArray.Create(
            new MarkLayerSpec("primary", MarkKind.Point, 0, bindings, []),
            new MarkLayerSpec("linear", MarkKind.Line, 100, [],
                [new StyleToken("overlayType", "Linear"), new StyleToken("label", "Linear Fit")]),
            new MarkLayerSpec("polynomial", MarkKind.Line, 101, [],
                [new StyleToken("overlayType", "Polynomial"), new StyleToken("parameter", "2"), new StyleToken("label", "Polynomial Fit")]));
        var spec = ChartSpec.Create("regression-scatter", "#stats", bindings, layers,
            new CoordinateSpec(CoordinateKind.Cartesian),
            [new ScaleSpec("x", FieldChannel.X, ScaleKind.Linear, false, []),
                new ScaleSpec("y", FieldChannel.Y, ScaleKind.Linear, false, [])],
            new FormattingSpec("en-US", "UTC", "", []), new NullHandlingSpec(NullValuePolicy.Skip, []),
            new ThemeSpec("default", []), new AccessibilitySpec("Regression scatter", null, null, true));
        var data = ChartDataSet.Create("#stats", 4,
        [
            new ChartColumn("Score", ChartValueKind.Decimal, DataSemanticKind.Quantitative,
                [ChartValue.From(10m), ChartValue.From(12m), ChartValue.From(20m), ChartValue.From(30m)], []),
            new ChartColumn("Rank", ChartValueKind.Decimal, DataSemanticKind.Quantitative,
                [ChartValue.From(100m), ChartValue.From(144m), ChartValue.From(400m), ChartValue.From(900m)], [])
        ]);

        var plan = new PlotPlanResolver().Resolve(spec, data);
        var polynomial = plan.Layers.Single(layer => layer.Id == "polynomial");
        var svg = new SvgChartRenderer().Render(plan);

        Assert.Equal(new decimal?[] { 10m, 12m, 20m, 30m }, polynomial.Data.Select(datum => Number(datum, FieldChannel.X)));
        Assert.All(polynomial.Data.Select((datum, index) => (Actual: Number(datum, FieldChannel.Y), Expected: data.Columns[1].Values[index].Decimal)),
            pair => Assert.InRange(Math.Abs(pair.Actual!.Value - pair.Expected!.Value), 0m, .001m));
        Assert.Equal(9m, PlotPlanResolver.Number(plan.Scales.Single(scale => scale.Channel == FieldChannel.X).Domain[0]));
        Assert.Equal(31m, PlotPlanResolver.Number(plan.Scales.Single(scale => scale.Channel == FieldChannel.X).Domain[^1]));
        Assert.Equal(2, CountOccurrences(svg, "class='plot-overlay-label-leader'"));
        Assert.Equal(4, CountOccurrences(svg, "class='plot-point'"));
        Assert.Equal(4, CountOccurrences(svg, "<circle"));
        Assert.True(svg.LastIndexOf("class='plot-point'", StringComparison.Ordinal) >
            svg.LastIndexOf("class='plot-overlay'", StringComparison.Ordinal));
        XDocument.Parse(svg);
    }

    [Fact]
    public async Task Bubble_PreservesSizeAndLabelInOnePlotPlan()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("bubble_native_plot_plan.rptsql");
        var visual = Assert.Single(manifest.Visuals);
        var plan = Assert.IsType<PlotPlan>(visual.PlotPlan);
        var layer = Assert.Single(plan.Layers);

        Assert.Equal(VisualType.Bubble.ToString().ToUpperInvariant(), visual.VisualType);
        Assert.Equal(MarkKind.Point, layer.Mark);
        Assert.All(layer.Data, datum =>
        {
            Assert.Contains(datum.Channels, channel => channel.Channel == FieldChannel.Size);
            Assert.Contains(datum.Channels, channel => channel.Channel == FieldChannel.Text);
        });

        var svg = new SvgChartRenderer().Render(plan);
        Assert.Contains("data-row-index='0'", svg);
        Assert.Contains("<title>Market A</title>", svg);
    }

    [Fact]
    public async Task Custom_OrdinalPointsUseSecondaryScaleAndConditionalEncodings()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("custom_ordinal_secondary_points.rptsql");
        var plan = Assert.IsType<PlotPlan>(Assert.Single(manifest.Visuals).PlotPlan);
        var pointLayer = plan.Layers.Single(layer => layer.Mark == MarkKind.Point);

        Assert.All(pointLayer.Data, datum =>
            Assert.Contains(datum.Channels, channel => channel.Channel == FieldChannel.Y2));
        var svg = new SvgChartRenderer().Render(plan);
        Assert.Equal(3, CountOccurrences(svg, "class='plot-point'"));
        Assert.Contains("fill='#C0392B' fill-opacity='0.45'", svg);
        Assert.Contains("fill='#2E86C1' fill-opacity='1'", svg);
        Assert.Contains(">-0.07</text>", svg);
    }

    [Fact]
    public async Task HeatMap_PreservesBothCategoryDomainsAndEveryCell()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("heatmap_native_plot_plan.rptsql");
        var plan = Assert.IsType<PlotPlan>(Assert.Single(manifest.Visuals).PlotPlan);

        Assert.Equal(4, Assert.Single(plan.Layers).Data.Length);
        Assert.Equal(new[] { "AM", "PM" }, plan.Scales.Single(scale => scale.Channel == FieldChannel.X).Categories);
        Assert.Equal(new[] { "Mon", "Tue" }, plan.Scales.Single(scale => scale.Channel == FieldChannel.Y).Categories);
        var styled = plan with
        {
            Style = plan.Style
                .Add(new StyleToken("COLOR:min", "#fff7bc"))
                .Add(new StyleToken("COLOR:max", "#d95f0e"))
                .Add(new StyleToken("DATA_LABELS", "ON"))
        };
        var svg = new SvgChartRenderer().Render(styled);
        Assert.Equal(4, CountOccurrences(svg, "class='plot-heat-cell'"));
        Assert.DoesNotContain("fill='#2563eb'", svg);
        Assert.Matches("fill='#[A-F0-9]{6}' data-row-index='0'", svg);
        Assert.Contains(">12</text>", svg);
    }

    [Fact]
    public async Task Funnel_UsesResolvedRectLayoutAcrossSurfaces()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("funnel_native_plot_plan.rptsql");
        var plan = Assert.IsType<PlotPlan>(Assert.Single(manifest.Visuals).PlotPlan);

        Assert.Equal("funnel", Assert.Single(plan.Layers).Style.Single(token => token.Name == "layout").Value);
        var svg = new SvgChartRenderer().Render(plan);
        Assert.Equal(3, CountOccurrences(svg, "<polygon"));
        Assert.Contains(">Leads</text>", svg);
        Assert.Equal(3, CountOccurrences(svg, "text-anchor='start'"));
        Assert.Equal(3, CountOccurrences(svg, "fill='none' stroke='#9ca3af'"));
    }

    [Fact]
    public async Task Gauge_ResolvesMappedDomainAndGoalAnnotation()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("gauge_native_plot_plan.rptsql");
        var plan = Assert.IsType<PlotPlan>(Assert.Single(manifest.Visuals).PlotPlan);
        var radius = plan.Scales.Single(scale => scale.Channel == FieldChannel.Radius);

        Assert.Equal(0m, PlotPlanResolver.Number(radius.Domain[0]));
        Assert.Equal(100m, PlotPlanResolver.Number(radius.Domain[^1]));
        var svg = new SvgChartRenderer().Render(plan);
        Assert.Contains("Goal: 80", svg);
        Assert.Contains(">73.5</text>", svg);
    }

    [Fact]
    public async Task Gauge_StylesProduceDistinctNativeGeometry()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("gauge_native_plot_plan.rptsql");
        var source = Assert.IsType<PlotPlan>(Assert.Single(manifest.Visuals).PlotPlan);
        PlotPlan WithStyle(string style) => source with { Style = source.Style.Add(new StyleToken("GAUGE_STYLE", style)) };

        var progress = new SvgChartRenderer().Render(source);
        var semiCircle = new SvgChartRenderer().Render(WithStyle("SEMI_CIRCLE"));
        var ring = new SvgChartRenderer().Render(WithStyle("RING"));
        var bar = new SvgChartRenderer().Render(WithStyle("BAR"));

        Assert.Contains("data-gauge-style='PROGRESS'", progress);
        Assert.Contains("data-gauge-style='SEMI_CIRCLE'", semiCircle);
        Assert.Contains("data-gauge-style='RING'", ring);
        Assert.Contains("data-gauge-style='BAR'", bar);
        Assert.Contains("stroke-dasharray=", ring);
        Assert.Contains("<rect class='plot-gauge-value'", bar);
        Assert.Equal(4, new[] { progress, semiCircle, ring, bar }.Distinct(StringComparer.Ordinal).Count());
        XDocument.Parse(progress);
        XDocument.Parse(semiCircle);
        XDocument.Parse(ring);
        XDocument.Parse(bar);
    }

    [Fact]
    public async Task BoxPlot_ComputesStatisticsOnceInPlotPlan()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("boxplot_native_plot_plan.rptsql");
        var plan = Assert.IsType<PlotPlan>(Assert.Single(manifest.Visuals).PlotPlan);
        var north = Assert.Single(plan.Layers).Data[0];

        Assert.Equal(2m, Number(north, FieldChannel.Low));
        Assert.Equal(3.5m, Number(north, FieldChannel.Q1));
        Assert.Equal(5m, Number(north, FieldChannel.Median));
        Assert.Equal(6.5m, Number(north, FieldChannel.Q3));
        Assert.Equal(8m, Number(north, FieldChannel.High));
        var svg = new SvgChartRenderer().Render(plan);
        Assert.Contains("<g data-row-index=", svg);
        Assert.Equal(2, CountOccurrences(svg, "stroke='#1e3a8a'"));
        Assert.Contains(">North</text>", svg);
        Assert.Contains(">South</text>", svg);
        Assert.Contains("2", svg);
        Assert.Contains("8", svg);
    }

    [Fact]
    public async Task Pie_EmitsConfiguredLegendAndPercentageLabels()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("pie_donut_proportions.rptsql");
        var visual = manifest.Visuals.Single(item => item.Name == "LeadSourcePie");
        var plan = Assert.IsType<PlotPlan>(visual.PlotPlan);
        var svg = new SvgChartRenderer().Render(plan);

        Assert.Contains(">Search</text>", svg);
        Assert.Contains(">Direct</text>", svg);
        Assert.Contains("Search: 43%", svg);
        Assert.Contains("Direct: 27%", svg);
        Assert.Equal(4, CountOccurrences(svg, "class='plot-arc-label'"));
        Assert.Equal(4, CountOccurrences(svg, "class='plot-arc-label-leader'"));
    }

    [Fact]
    public async Task Donut_RendersDynamicCenterValueAndRoseGeometry()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("pie_donut_proportions.rptsql");
        var source = Assert.IsType<PlotPlan>(manifest.Visuals.Single(item => item.Name == "LeadSourceDonut").PlotPlan);
        var plan = source with
        {
            Coordinate = source.Coordinate! with { InnerRadius = .55m },
            Style = source.Style
                .Add(new StyleToken("CENTER_VALUE", "{total} qualified"))
                .Add(new StyleToken("CENTER_LABEL", "Total leads"))
                .Add(new StyleToken("ROSE_MODE", "ON"))
        };

        var svg = new SvgChartRenderer().Render(plan);

        Assert.Contains("class='plot-arc-center-value'", svg);
        Assert.Contains(">10400 qualified</text>", svg);
        Assert.Contains("class='plot-arc-center-label'", svg);
        Assert.Contains(">Total leads</text>", svg);
        var radii = Regex.Matches(svg, @" A (?<radius>[0-9.]+) \k<radius> 0")
            .Select(match => match.Groups["radius"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.True(radii.Count > 2);
        XDocument.Parse(svg);
    }

    [Fact]
    public async Task Waterfall_ResolvesRunningIntervalsAndTotalAnchors()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("waterfall_native_plot_plan.rptsql");
        var plan = Assert.IsType<PlotPlan>(Assert.Single(manifest.Visuals).PlotPlan);
        var data = Assert.Single(plan.Layers).Data;

        Assert.Equal((0m, 50m), (Number(data[0], FieldChannel.YStart), Number(data[0], FieldChannel.YEnd)));
        Assert.Equal((50m, 80m), (Number(data[1], FieldChannel.YStart), Number(data[1], FieldChannel.YEnd)));
        Assert.Equal((80m, 60m), (Number(data[2], FieldChannel.YStart), Number(data[2], FieldChannel.YEnd)));
        Assert.Equal((0m, 60m), (Number(data[3], FieldChannel.YStart), Number(data[3], FieldChannel.YEnd)));
        Assert.Equal(4, CountOccurrences(new SvgChartRenderer().Render(plan), "data-row-index="));
    }

    [Fact]
    public async Task Candlestick_PreservesTypedOhlcChannels()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("candlestick_native_plot_plan.rptsql");
        var plan = Assert.IsType<PlotPlan>(Assert.Single(manifest.Visuals).PlotPlan);
        var first = Assert.Single(plan.Layers).Data[0];

        Assert.Equal(10m, Number(first, FieldChannel.Open));
        Assert.Equal(14m, Number(first, FieldChannel.High));
        Assert.Equal(8m, Number(first, FieldChannel.Low));
        Assert.Equal(12m, Number(first, FieldChannel.Close));
        Assert.Contains("O 10, H 14, L 8, C 12", new SvgChartRenderer().Render(plan));
    }

    [Fact]
    public async Task Trellis_UsesSharedFacetPanelsAcrossBackends()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("trellis_native_plot_plan.rptsql");
        var plan = Assert.IsType<PlotPlan>(Assert.Single(manifest.Visuals).PlotPlan);

        Assert.Equal(2, plan.Facets.Length);
        Assert.Equal(new[] { "North", "South" }, plan.Facets.Select(facet => facet.ColumnLabel));
        Assert.All(plan.Facets, facet =>
        {
            Assert.Equal(plan.Scales.Select(scale => scale.Id), facet.Scales.Select(scale => scale.Id));
            Assert.Equal(
                plan.Scales.Single(scale => scale.Channel == FieldChannel.Y).Domain,
                facet.Scales.Single(scale => scale.Channel == FieldChannel.Y).Domain);
        });
        var svg = new SvgChartRenderer().Render(plan);
        Assert.Contains("North", svg);
        Assert.Contains("South", svg);
    }

    [Fact]
    public async Task Gantt_UsesNativeIntervalsProgressMilestoneAndDependencyPath()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("gantt_native_plot_plan.rptsql");
        var plan = Assert.IsType<PlotPlan>(Assert.Single(manifest.Visuals).PlotPlan);

        Assert.Equal(CoordinateKind.TransposedCartesian, plan.Coordinate?.Kind);
        Assert.Equal(3, plan.Layers.SelectMany(layer => layer.Data).Select(datum => datum.RowIndex).Distinct().Count());
        var svg = new SvgChartRenderer().Render(plan);
        Assert.Contains("marker-end='url(#gantt-arrow)'", svg);
        Assert.Contains("Launch", svg);
        Assert.Contains("fill-opacity='.28'", svg);
    }

    [Fact]
    public async Task Radar_ResolvesWideRowsIntoOrderedPolarSeries()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("radar_native_plot_plan.rptsql");
        var visual = Assert.Single(manifest.Visuals);
        Assert.True(visual.PlotPlan is PlotPlan, visual.Error);
        var plan = (PlotPlan)visual.PlotPlan!;

        Assert.Equal(new[] { "Speed", "Reliability", "Efficiency", "Coverage" },
            plan.Scales.Single(scale => scale.Channel == FieldChannel.Theta).Categories);
        Assert.Equal(new[] { "Model A", "Model B" }, plan.Series.Select(series => series.Key));
        Assert.All(plan.Layers, layer => Assert.Equal(4, layer.Data.Length));
        Assert.Equal(6, CountOccurrences(new SvgChartRenderer().Render(plan), "<polygon"));
    }

    private static decimal? Number(ResolvedDatum datum, FieldChannel channel) =>
        PlotPlanResolver.Number(datum.Channels.Single(value => value.Channel == channel).Value);

    private static int CountOccurrences(string value, string fragment) =>
        (value.Length - value.Replace(fragment, string.Empty).Length) / fragment.Length;

    private static (decimal X, decimal Y, decimal Width, decimal Height) RectGeometry(string svg, int rowIndex)
    {
        var match = Regex.Match(svg,
            $"<rect x='(?<x>[^']+)' y='(?<y>[^']+)' width='(?<width>[^']+)' height='(?<height>[^']+)'[^>]*data-row-index='{rowIndex}'");
        Assert.True(match.Success, $"No rectangle found for row {rowIndex}.");
        return (
            decimal.Parse(match.Groups["x"].Value, CultureInfo.InvariantCulture),
            decimal.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture),
            decimal.Parse(match.Groups["width"].Value, CultureInfo.InvariantCulture),
            decimal.Parse(match.Groups["height"].Value, CultureInfo.InvariantCulture));
    }
}
