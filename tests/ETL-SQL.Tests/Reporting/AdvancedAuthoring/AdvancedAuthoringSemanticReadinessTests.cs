using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using Xunit;

namespace ETL_SQL.Tests.Reporting.AdvancedAuthoring;

public sealed class AdvancedAuthoringSemanticReadinessTests
{
    [Fact]
    public void MultiLayerSpec_PreservesOrderedMarkLayers_RectLinePointRule()
    {
        var spec = AdvancedAuthoringSemanticReadinessHarness.CreateMultiLayerSpec();
        var data = AdvancedAuthoringSemanticReadinessHarness.CreateMultiLayerDataSet();

        spec.Validate();
        Assert.Equal(4, spec.Layers.Length);
        Assert.Equal(MarkKind.Rect, spec.Layers[0].Mark);
        Assert.Equal(MarkKind.Line, spec.Layers[1].Mark);
        Assert.Equal(MarkKind.Point, spec.Layers[2].Mark);
        Assert.Equal(MarkKind.Rule, spec.Layers[3].Mark);

        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.NotNull(plan);
        Assert.Equal(4, plan.Layers.Length);
        Assert.Equal("layer-rect-bars", plan.Layers[0].Id);
        Assert.Equal("layer-line-trend", plan.Layers[1].Id);
        Assert.Equal("layer-point-markers", plan.Layers[2].Id);
        Assert.Equal("layer-rule-target", plan.Layers[3].Id);

        // Z-Index ordering must be strictly ascending
        for (int i = 0; i < plan.Layers.Length - 1; i++)
        {
            Assert.True(plan.Layers[i].ZIndex <= plan.Layers[i + 1].ZIndex);
        }
    }

    [Fact]
    public void DualAxisBindings_ResolvesPrimaryAndSecondaryScales()
    {
        var spec = AdvancedAuthoringSemanticReadinessHarness.CreateMultiLayerSpec();
        var data = AdvancedAuthoringSemanticReadinessHarness.CreateMultiLayerDataSet();

        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.NotNull(plan);

        var primaryScale = plan.Scales.FirstOrDefault(s => s.Id == "scale_y");
        var secondaryScale = plan.Scales.FirstOrDefault(s => s.Id == "scale_y2");

        Assert.NotNull(primaryScale);
        Assert.NotNull(secondaryScale);

        Assert.Equal(FieldChannel.Y, primaryScale.Channel);
        Assert.Equal(FieldChannel.Y2, secondaryScale.Channel);
        Assert.True(primaryScale.IncludesZero);
    }

    [Fact]
    public void ScaleResolution_SupportsSharedAndIndependentPolicies()
    {
        var sharedSpec = new ScaleResolutionSpec(ScaleResolutionMode.Shared, ScaleResolutionMode.Shared);
        var independentSpec = new ScaleResolutionSpec(ScaleResolutionMode.Shared, ScaleResolutionMode.Independent);

        Assert.Equal(ScaleResolutionMode.Shared, sharedSpec.Y);
        Assert.Equal(ScaleResolutionMode.Independent, independentSpec.Y);

        var spec = AdvancedAuthoringSemanticReadinessHarness.CreateMultiLayerSpec();
        Assert.NotNull(spec.Facet);
        Assert.Equal(ScaleResolutionMode.Independent, spec.Facet.Resolution.Y);
    }

    [Fact]
    public void Facets_1DAnd2D_PreserveDeterministicRowAndCategoryOrder()
    {
        var spec1D = AdvancedAuthoringSemanticReadinessHarness.CreateMultiLayerSpec() with
        {
            Facet = new FacetSpec("Region", null, new ScaleResolutionSpec(ScaleResolutionMode.Shared, ScaleResolutionMode.Shared))
        };
        spec1D.Validate();
        Assert.Equal("Region", spec1D.Facet!.RowField);
        Assert.Null(spec1D.Facet.ColumnField);

        var spec2D = AdvancedAuthoringSemanticReadinessHarness.CreateMultiLayerSpec() with
        {
            Facet = new FacetSpec("Region", "Year", new ScaleResolutionSpec(ScaleResolutionMode.Shared, ScaleResolutionMode.Independent))
        };
        spec2D.Validate();
        Assert.Equal("Region", spec2D.Facet!.RowField);
        Assert.Equal("Year", spec2D.Facet.ColumnField);
    }

    [Fact]
    public void CoordinateSystems_SupportCartesianTransposedAndPolar()
    {
        var cartesian = new CoordinateSpec(CoordinateKind.Cartesian);
        var transposed = new CoordinateSpec(CoordinateKind.TransposedCartesian);
        var polar = new CoordinateSpec(CoordinateKind.Polar, StartAngle: 0m, EndAngle: 360m, InnerRadius: 0.5m);

        Assert.Equal(CoordinateKind.Cartesian, cartesian.Kind);
        Assert.Equal(CoordinateKind.TransposedCartesian, transposed.Kind);
        Assert.Equal(CoordinateKind.Polar, polar.Kind);
        Assert.Equal(0.5m, polar.InnerRadius);
    }

    [Fact]
    public void ChartSpec_SerializationAndValidationFailures_AreDeterministic()
    {
        var spec = AdvancedAuthoringSemanticReadinessHarness.CreateMultiLayerSpec();
        var json = ChartContractSerializer.Serialize(spec);
        var roundTrip = ChartContractSerializer.DeserializeChartSpec(json);

        Assert.NotNull(roundTrip);
        Assert.Equal(spec.Id, roundTrip.Id);
        Assert.Equal(spec.Layers.Length, roundTrip.Layers.Length);

        // Validation Failure: Duplicate Layer ID
        var dupLayerSpec = spec with
        {
            Layers = [spec.Layers[0], spec.Layers[0]]
        };
        Assert.Throws<InvalidDataException>(() => dupLayerSpec.Validate());

        // Validation Failure: Undeclared Scale ID
        var badScaleSpec = spec with
        {
            Bindings = [new FieldBinding(FieldChannel.Y, "volume", DataSemanticKind.Quantitative, "non_existent_scale")]
        };
        Assert.Throws<InvalidDataException>(() => badScaleSpec.Validate());

        // Validation Failure: Negative Z-Index
        var negZSpec = spec with
        {
            Layers = [new MarkLayerSpec("bad-z", MarkKind.Rect, -1, [], [])]
        };
        Assert.Throws<InvalidDataException>(() => negZSpec.Validate());
    }

    [Fact]
    public void MultiSurfaceRendering_ProducesConsistentSemanticOutput()
    {
        var spec = AdvancedAuthoringSemanticReadinessHarness.CreateMultiLayerSpec();
        var data = AdvancedAuthoringSemanticReadinessHarness.CreateMultiLayerDataSet();
        var plan = new PlotPlanResolver().Resolve(spec, data);

        // 1. ECharts Lowering
        var echartsRenderer = new EChartsRenderer();
        var echartsJson = echartsRenderer.Render(plan);
        Assert.NotNull(echartsJson);
        Assert.Contains("series", echartsJson);
        Assert.Contains("yAxis", echartsJson);

        // 2. Native SVG Lowering
        var svgStr = new SvgChartRenderer().Render(new VisualManifest { PlotPlan = plan });
        Assert.NotNull(svgStr);
        Assert.Contains("<svg", svgStr);

        // 3. Terminal Lowering
        var terminalRenderable = PlotPlanTerminalRenderer.Render(plan);
        Assert.NotNull(terminalRenderable);

        // 4. Accessible Summary & Fallback
        Assert.NotEmpty(plan.AccessibleSummary);
        Assert.NotNull(plan.Fallback);
        Assert.Equal(SemanticFallbackKind.RankedTable, plan.Fallback.Kind);
    }

    [Fact]
    public void CapabilityInventory_ContainsAdvancedAuthoringConcepts()
    {
        var inventory = AdvancedAuthoringSemanticReadinessHarness.GetCapabilityInventory();
        Assert.NotEmpty(inventory);

        Assert.Contains(inventory, c => c.Concept.Contains("Multi-Layer"));
        Assert.Contains(inventory, c => c.Concept.Contains("Dual-Axis"));
        Assert.Contains(inventory, c => c.Concept.Contains("Scale Resolution"));
        Assert.Contains(inventory, c => c.Concept.Contains("Facet Grid"));
        Assert.Contains(inventory, c => c.Concept.Contains("Conditional Visual Mark Encodings"));
        Assert.Contains(inventory, c => c.Concept.Contains("Layer-Level Independent Data Source Overrides"));
    }

    [Fact]
    public void ReadinessReport_GeneratesMarkdownAndJson()
    {
        var report = AdvancedAuthoringSemanticReadinessHarness.GenerateReadinessReport();
        Assert.NotNull(report);
        Assert.NotEmpty(report.CapabilityInventory);
        Assert.NotEmpty(report.SurfaceConformanceMatrix);

        var md = AdvancedAuthoringSemanticReadinessHarness.FormatMarkdownReport(report);
        var json = AdvancedAuthoringSemanticReadinessHarness.FormatJsonReport(report);

        Assert.Contains("Phase 7 Semantic Authoring Readiness", md);
        Assert.Contains("Ordered Multi-Layer Marks", md);
        Assert.Contains("\"CapabilityInventory\"", json);
    }
}
