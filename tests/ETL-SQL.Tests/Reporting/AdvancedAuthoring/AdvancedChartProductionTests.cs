using System.Collections.Immutable;
using System.Linq;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.ReportHosting;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using ETL_SQL.Tests.Reporting;

namespace ETL_SQL.Tests.Reporting.AdvancedAuthoring;

public sealed class AdvancedChartProductionTests
{
    [Fact]
    public void ParsedCustomChart_LowersIntoExecutableRendererNeutralContracts()
    {
        const string sql = """
            CREATE VISUAL Native AS CUSTOM (
              SOURCE = #prepared,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                SCALES (x = BAND (CHANNEL = X), y = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON)),
                LAYERS (bars = RECT (ENCODINGS (
                  X = Category (TYPE = NOMINAL, SCALE = x),
                  Y = Value (TYPE = QUANTITATIVE, SCALE = y)
                )))
              )
            );
            """;
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var manifest = new VisualManifest { Name = "Native", Columns = ["Category", "Value"], Rows = [["A", "12"], ["B", "20"]] };

        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        var data = new VisualChartDataBuilder().Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data);

        Assert.Equal(MarkKind.Rect, Assert.Single(spec.Layers).Mark);
        Assert.Equal(2, Assert.Single(plan.Layers).Data.Length);
        Assert.Contains("<rect", new SvgChartRenderer().Render(plan));
    }

    [Fact]
    public void ConditionalEncodingsAndIndependentFacetScales_AreResolvedOnceForEverySurface()
    {
        var condition = new EncodingConditionSpec(
            ConditionalEncodingChannel.Color,
            new EncodingPredicate(PredicateKind.Comparison,
                new PredicateOperand(PredicateOperandKind.Field, "Value", null),
                new PredicateOperand(PredicateOperandKind.Literal, null, ChartValue.From(0m)),
                ComparisonKind.LessThan),
            ChartValue.From("#b91c1c"), ChartValue.From("#2563eb"));
        var spec = ChartSpec.Create("conditional-facets", "#prepared",
            [
                new FieldBinding(FieldChannel.X, "Category", DataSemanticKind.Nominal, "x"),
                new FieldBinding(FieldChannel.Y, "Value", DataSemanticKind.Quantitative, "y"),
                new FieldBinding(FieldChannel.Row, "Region", DataSemanticKind.Nominal)
            ],
            [new MarkLayerSpec("bars", MarkKind.Rect, 0,
                [new FieldBinding(FieldChannel.X, "Category", DataSemanticKind.Nominal, "x"), new FieldBinding(FieldChannel.Y, "Value", DataSemanticKind.Quantitative, "y")], [])
                { Conditions = [condition] }],
            new CoordinateSpec(CoordinateKind.Cartesian),
            [new ScaleSpec("x", FieldChannel.X, ScaleKind.Band, false, []), new ScaleSpec("y", FieldChannel.Y, ScaleKind.Linear, true, [])],
            new FormattingSpec("en-US", "UTC", "", []), new NullHandlingSpec(NullValuePolicy.Gap, []),
            new ThemeSpec("default", []), new AccessibilitySpec("Conditional facets", null, null, true),
            facet: new FacetSpec("Region", null, new ScaleResolutionSpec(ScaleResolutionMode.Shared, ScaleResolutionMode.Independent)),
            scaleResolution: new ScaleResolutionSpec(ScaleResolutionMode.Shared, ScaleResolutionMode.Independent));
        var data = ChartDataSet.Create("#prepared", 4,
        [
            new ChartColumn("Category", ChartValueKind.Text, DataSemanticKind.Nominal, [ChartValue.From("A"), ChartValue.From("B"), ChartValue.From("A"), ChartValue.From("B")], []),
            new ChartColumn("Value", ChartValueKind.Decimal, DataSemanticKind.Quantitative, [ChartValue.From(-5m), ChartValue.From(10m), ChartValue.From(100m), ChartValue.From(200m)], []),
            new ChartColumn("Region", ChartValueKind.Text, DataSemanticKind.Nominal, [ChartValue.From("East"), ChartValue.From("East"), ChartValue.From("West"), ChartValue.From("West")], [])
        ]);

        var plan = new PlotPlanResolver().Resolve(spec, data);

        Assert.Equal(2, plan.Facets.Length);
        Assert.NotEqual(plan.Facets[0].Scales.Single(scale => scale.Id == "y").Domain,
            plan.Facets[1].Scales.Single(scale => scale.Id == "y").Domain);
        Assert.Equal("#b91c1c", plan.Layers[0].Data[0].Encodings.Single().Value.Text);
        Assert.Equal("#2563eb", plan.Layers[0].Data[1].Encodings.Single().Value.Text);
        Assert.Contains("East", new SvgChartRenderer().Render(plan));
        Assert.Contains("#b91c1c", new SvgChartRenderer().Render(new VisualManifest { PlotPlan = plan }));
        Assert.NotNull(PlotPlanTerminalRenderer.Render(plan));
    }

    [Fact]
    public async Task AnalysisRejectsConditionsOnConnectedMarks()
    {
        const string sql = """
            CREATE VISUAL BadLine AS CUSTOM (
              SOURCE = #prepared,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                SCALES (x = BAND (CHANNEL = X), y = LINEAR (CHANNEL = Y)),
                LAYERS (trend = LINE (
                  ENCODINGS (X = Category (TYPE = NOMINAL, SCALE = x), Y = Value (TYPE = QUANTITATIVE, SCALE = y)),
                  CONDITIONS (COLOR WHEN Value < 0 THEN '#b91c1c')
                ))
              )
            );
            """;
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);

        var diagnostics = await new AdvancedChartAuthoringRule().AnalyzeAsync(script, new DefaultLintContext());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "RPT-CHART" && diagnostic.Message.Contains("connected LINE"));
    }

    [Fact]
    public void ContractSerializationPreservesConditionsAndFacetPanels()
    {
        var layer = new MarkLayerSpec("points", MarkKind.Point, 0, [], [])
        {
            Conditions = [new EncodingConditionSpec(ConditionalEncodingChannel.Size,
                new EncodingPredicate(PredicateKind.Truthy, new PredicateOperand(PredicateOperandKind.Literal, null, ChartValue.From(true))),
                ChartValue.From(8m))]
        };
        var spec = ChartSpec.Create("serialized", "#data", [], [layer], new CoordinateSpec(CoordinateKind.Cartesian), [],
            new FormattingSpec("en-US", "UTC", "", []), new NullHandlingSpec(NullValuePolicy.Gap, []),
            new ThemeSpec("default", []), new AccessibilitySpec("Serialized", null, null, true));

        var json = ChartContractSerializer.Serialize(spec);
        var roundTrip = ChartContractSerializer.DeserializeChartSpec(json);

        Assert.Single(roundTrip.Layers[0].Conditions);
        Assert.Equal(ConditionalEncodingChannel.Size, roundTrip.Layers[0].Conditions[0].Channel);
    }

    [Fact]
    public void AreaTextAndTransposedCoordinates_RenderAcrossBackends()
    {
        var bindings = ImmutableArray.Create(
            new FieldBinding(FieldChannel.X, "Category", DataSemanticKind.Nominal, "x"),
            new FieldBinding(FieldChannel.Y, "Value", DataSemanticKind.Quantitative, "y"),
            new FieldBinding(FieldChannel.Text, "Label", DataSemanticKind.Nominal));
        var spec = ChartSpec.Create("transposed", "#data", bindings,
            [new MarkLayerSpec("area", MarkKind.Area, 0, bindings, []), new MarkLayerSpec("labels", MarkKind.Text, 1, bindings, [])],
            new CoordinateSpec(CoordinateKind.TransposedCartesian),
            [new ScaleSpec("x", FieldChannel.X, ScaleKind.Band, false, []), new ScaleSpec("y", FieldChannel.Y, ScaleKind.Linear, true, [])],
            new FormattingSpec("en-US", "UTC", "", []), new NullHandlingSpec(NullValuePolicy.Gap, []),
            new ThemeSpec("default", []), new AccessibilitySpec("Transposed", null, null, true));
        var data = ChartDataSet.Create("#data", 2,
        [
            new ChartColumn("Category", ChartValueKind.Text, DataSemanticKind.Nominal, [ChartValue.From("A"), ChartValue.From("B")], []),
            new ChartColumn("Value", ChartValueKind.Decimal, DataSemanticKind.Quantitative, [ChartValue.From(2m), ChartValue.From(8m)], []),
            new ChartColumn("Label", ChartValueKind.Text, DataSemanticKind.Nominal, [ChartValue.From("low"), ChartValue.From("high")], [])
        ]);
        var plan = new PlotPlanResolver().Resolve(spec, data);

        var svg = new SvgChartRenderer().Render(new VisualManifest { PlotPlan = plan });

        Assert.Contains("<path", svg);
        Assert.Contains(">high</text>", svg);
        Assert.NotNull(PlotPlanTerminalRenderer.Render(plan));
    }

    [Fact]
    public void PolarArcConditionalColor_RendersAcrossBackends()
    {
        var bindings = ImmutableArray.Create(
            new FieldBinding(FieldChannel.Theta, "Kind", DataSemanticKind.Nominal, "theta"),
            new FieldBinding(FieldChannel.Radius, "Amount", DataSemanticKind.Quantitative, "radius"));
        var condition = new EncodingConditionSpec(ConditionalEncodingChannel.Color,
            new EncodingPredicate(PredicateKind.Comparison,
                new PredicateOperand(PredicateOperandKind.Field, "Amount", null),
                new PredicateOperand(PredicateOperandKind.Literal, null, ChartValue.From(5m)), ComparisonKind.GreaterThan),
            ChartValue.From("#b91c1c"));
        var spec = ChartSpec.Create("polar", "#data", bindings,
            [new MarkLayerSpec("arcs", MarkKind.Arc, 0, bindings, []) { Conditions = [condition] }],
            new CoordinateSpec(CoordinateKind.Polar, InnerRadius: .4m),
            [new ScaleSpec("theta", FieldChannel.Theta, ScaleKind.Ordinal, false, []), new ScaleSpec("radius", FieldChannel.Radius, ScaleKind.Linear, true, [])],
            new FormattingSpec("en-US", "UTC", "", []), new NullHandlingSpec(NullValuePolicy.Skip, []),
            new ThemeSpec("default", []), new AccessibilitySpec("Polar", null, null, true));
        var data = ChartDataSet.Create("#data", 2,
        [
            new ChartColumn("Kind", ChartValueKind.Text, DataSemanticKind.Nominal, [ChartValue.From("A"), ChartValue.From("B")], []),
            new ChartColumn("Amount", ChartValueKind.Decimal, DataSemanticKind.Quantitative, [ChartValue.From(3m), ChartValue.From(7m)], [])
        ]);
        var plan = new PlotPlanResolver().Resolve(spec, data);

        Assert.Contains("#b91c1c", new SvgChartRenderer().Render(plan));
        Assert.Contains("#b91c1c", new SvgChartRenderer().Render(new VisualManifest { PlotPlan = plan }));
        Assert.Contains(plan.Fallback.Items, item => item.Detail?.Contains("conditional Color") == true);
        Assert.NotNull(PlotPlanTerminalRenderer.Render(plan));
    }

    [Fact]
    public async Task KitchenSink39_RendersAllThreeCustomVisuals()
    {
        var scriptPath = @"C:\Users\chuck\scratch\ETL-SQL\samples\10_Kitchen_Sinks\39_CUSTOM_LAYERS.rptsql";
        await using var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
        var manifest = await service.GetManifestAsync();
        Assert.NotNull(manifest);
        Assert.Equal(3, manifest.Visuals.Count);

        var perf = manifest.Visuals.First(v => v.Name == "PerformanceOverview");
        var bullet = manifest.Visuals.First(v => v.Name == "BulletKpiCards");
        var scatter = manifest.Visuals.First(v => v.Name == "CustomerScatter");

        Assert.NotNull(perf.NativeSvg);
        Assert.NotNull(bullet.NativeSvg);
        Assert.NotNull(scatter.NativeSvg);
    }

    [Fact]
    public async Task KitchenSink01_OverlayBar_RendersCompositeTerminalOutput()
    {
        var scriptPath = @"C:\Users\chuck\scratch\ETL-SQL\samples\10_Kitchen_Sinks\01_BAR.rptsql";
        await using var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
        var manifest = await service.GetManifestAsync();
        Assert.NotNull(manifest);
        var overlayBar = manifest.Visuals.First(v => v.Name == "OverlayBar");
        var plan = Assert.IsType<PlotPlan>(overlayBar.PlotPlan);
        var renderable = PlotPlanTerminalRenderer.Render(plan, 80);
        var text = ETL_SQL.Tests.Reporting.TerminalSemantics.TerminalSnapshotHarness.CaptureSnapshot(renderable, 80).NormalizedText;
        Assert.Contains("Revenue", text);
        Assert.Contains("Monthly Goal", text);
        Assert.Contains("2-Month Moving Avg", text);
        Assert.Contains("Jan", text);
        Assert.Contains("Apr", text);
    }

    [Fact]
    public async Task KitchenSink04_Scatter_RendersAllFourVisualsInTerminal()
    {
        var scriptPath = @"C:\Users\chuck\scratch\ETL-SQL\samples\10_Kitchen_Sinks\04_SCATTER.rptsql";
        await using var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
        var manifest = await service.GetManifestAsync();
        Assert.NotNull(manifest);

        foreach (var visual in manifest.Visuals)
        {
            var plan = Assert.IsType<PlotPlan>(visual.PlotPlan);
            var renderable = PlotPlanTerminalRenderer.Render(plan, 80);
            var text = ETL_SQL.Tests.Reporting.TerminalSemantics.TerminalSnapshotHarness.CaptureSnapshot(renderable, 80).NormalizedText;
            Assert.NotNull(text);
            Assert.NotEmpty(text);
            Assert.DoesNotContain("Error", text);
        }
    }

    [Fact]
    public async Task KitchenSink11_Heatmap_RendersAllThreeVisualsInTerminal()
    {
        var scriptPath = @"C:\Users\chuck\scratch\ETL-SQL\samples\10_Kitchen_Sinks\11_HEATMAP.rptsql";
        await using var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
        var manifest = await service.GetManifestAsync();
        Assert.NotNull(manifest);

        foreach (var visual in manifest.Visuals)
        {
            var plan = Assert.IsType<PlotPlan>(visual.PlotPlan);
            var renderable = PlotPlanTerminalRenderer.Render(plan, 80);
            var text = ETL_SQL.Tests.Reporting.TerminalSemantics.TerminalSnapshotHarness.CaptureSnapshot(renderable, 80).NormalizedText;
            Assert.NotNull(text);
            Assert.NotEmpty(text);
            Assert.Contains("08:00", text);
            Assert.Contains("Mon", text);
            Assert.Contains("Fri", text);
            Assert.Contains("2D Heatmap Grid", text);
        }
    }
}
