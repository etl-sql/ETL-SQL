using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using ETL_SQL.Tests.Reporting.Conformance;
using Xunit;

namespace ETL_SQL.Tests.Reporting.GrammarOfGraphics;

/// <summary>
/// End-to-end coverage for the v0.19 interaction migration: authoring lowers into
/// <see cref="InteractionSpec"/>, <see cref="PlotPlanResolver"/> resolves it, and browser clients
/// receive only the compact <c>InteractionManifest</c> projected from the plan.
/// </summary>
public sealed class ChartInteractionContractTests
{
    // ── Lowering: named and CUSTOM authoring reach the same canonical contract ─────────────

    [Fact]
    public async Task NamedVisualAuthoring_LowersActionsAndInteractionsIntoInteractionSpec()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("bar_stable_ordering.rptsql");
        var spec = manifest.Visuals.First(visual => visual.ChartSpec is not null).ChartSpec!;

        Assert.NotNull(spec.Interactions);
        // The canonical contract carries selections, not just trigger bindings. Before this migration
        // the named lowerer always emitted an empty Selections array.
        Assert.All(spec.Interactions.Selections, selection => Assert.NotEmpty(selection.Fields));
    }

    [Fact]
    public async Task CustomAuthoring_LowersIntoInteractionSpecFromResolvedEncodings()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("custom_crossfilter_offset_key.rptsql");
        var visual = manifest.Visuals.Single(item => item.Name == "RegionalRevenue");

        var selection = Assert.Single(visual.ChartSpec!.Interactions.Selections);
        Assert.Equal(SelectionMode.Multiple, selection.Mode);
        // CUSTOM has no MAPPINGS clause; the key can only have come from the X encoding.
        Assert.Equal("Region", selection.Fields[0]);
        Assert.Contains(visual.ChartSpec.Interactions.Bindings,
            binding => binding.Trigger.Equals("ON_SELECT", StringComparison.OrdinalIgnoreCase) &&
                binding.Effect == InteractionEffect.Highlight);
    }

    // ── Cross-filter key: the defect this migration closes ────────────────────────────────

    [Fact]
    public async Task CustomCrossFilter_ResolvesTheXBindingEvenWhenItIsNotTheFirstSourceColumn()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("custom_crossfilter_offset_key.rptsql");
        var visual = manifest.Visuals.Single(item => item.Name == "RegionalRevenue");

        // The fixture's first source column is Revenue. Falling through to columns[0] — the shipped
        // behaviour before this change — cross-filtered on a revenue amount.
        Assert.Equal("Revenue", visual.Columns[0]);
        Assert.Equal("Region", visual.PlotPlan!.Interaction!.Key);
        Assert.Equal("Revenue", visual.PlotPlan.Interaction.ValueKey);
        Assert.Equal("Region", visual.Interaction!.Key);
        Assert.Equal("MULTIPLE", visual.Interaction.Select);
        Assert.Equal("HIGHLIGHT", visual.Interaction.Effect);
        // A declared selection over baseline-anchored bars resolves to a proportional treatment.
        Assert.Equal("PROPORTIONAL", visual.Interaction.Highlight);
    }

    [Fact]
    public void ResolvedKey_IsNullRatherThanAGuessWhenTheKeyColumnIsAbsentFromTheData()
    {
        var spec = Spec(key: "Region", measure: "Revenue");
        var resolved = ChartInteractionResolver.Resolve(spec, new[] { "Revenue", "Orders" }, SelectionHighlightMode.Proportional);

        // A wrong filter column is a wrong answer. No key means no cross-filter, never column zero.
        Assert.Null(resolved.Key);
        Assert.Equal("Revenue", resolved.ValueKey);
    }

    // ── Required mark semantics resolved server-side ──────────────────────────────────────

    [Fact]
    public async Task VerticalBars_ResolveAValueExtentAndPublishItOnEveryMark()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("bar_stable_ordering.rptsql");
        var visual = manifest.Visuals.First(item => item.PlotPlan is not null);

        var layer = visual.PlotPlan!.Layers.First(item => item.Mark == MarkKind.Rect);
        Assert.Equal(MarkExtentAxis.Y, layer.ExtentAxis);
        Assert.Equal(MarkExtentAnchor.End, layer.ExtentAnchor);
        Assert.Equal(SelectionHighlightMode.Proportional,
            ChartInteractionResolver.HighlightFor(visual.PlotPlan.Layers));
        Assert.Contains("data-extent-axis='y'", visual.NativeSvg);
        Assert.Contains("data-extent-anchor='end'", visual.NativeSvg);
        // The fixture declares no INTERACTIONS, so the capability is resolved but inert.
        Assert.Equal(SelectionMode.None, visual.PlotPlan.Interaction!.Selection);
        Assert.Equal(SelectionHighlightMode.None, visual.PlotPlan.Interaction.Highlight);
    }

    [Fact]
    public async Task TransposedBars_ResolveAHorizontalExtent()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("hbar_native_plot_plan.rptsql");
        var visual = manifest.Visuals.First(item => item.PlotPlan is not null);

        var layer = visual.PlotPlan!.Layers.First(item => item.Mark == MarkKind.Rect);
        Assert.Equal(MarkExtentAxis.X, layer.ExtentAxis);
        Assert.Equal(MarkExtentAnchor.Start, layer.ExtentAnchor);
        Assert.Contains("data-extent-axis='x'", visual.NativeSvg);
    }

    [Fact]
    public async Task RangedRects_DeclareNoValueExtentBecauseTheirHeightIsASpan()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("custom_ranged_rect_bands.rptsql");
        var visual = manifest.Visuals.First(item => item.PlotPlan is not null);

        // An author-supplied Y_START/Y_END owns both endpoints, so nothing may read its height as a
        // value and draw a proportional share inside it.
        Assert.Contains(visual.PlotPlan!.Layers, layer => layer.ExtentAxis == MarkExtentAxis.None);
        Assert.DoesNotContain("plot-range-rect' x=", RangedMarksWithExtent(visual.NativeSvg!));
    }

    [Fact]
    public async Task NonRectangularMarks_DeclareNoValueExtent()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("scatter_multi_series_inferred.rptsql");
        var visual = manifest.Visuals.First(item => item.PlotPlan is not null);

        Assert.All(visual.PlotPlan!.Layers, layer => Assert.Equal(MarkExtentAxis.None, layer.ExtentAxis));
        Assert.DoesNotContain("data-extent-axis", visual.NativeSvg);
        Assert.Equal(SelectionHighlightMode.Categorical,
            ChartInteractionResolver.HighlightFor(visual.PlotPlan.Layers));
    }

    [Fact]
    public void ProportionalHighlight_DegradesToCategoricalWithoutAResolvedMeasure()
    {
        var spec = Spec(key: "Region", measure: null);
        var resolved = ChartInteractionResolver.Resolve(spec, new[] { "Region" }, SelectionHighlightMode.Proportional);

        Assert.Equal(SelectionHighlightMode.Categorical, resolved.Highlight);
    }

    // ── Action semantics ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Actions_LowerIntoResolvedInteractionTriggersWithTheirTargets()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("custom_crossfilter_offset_key.rptsql");
        var visual = manifest.Visuals.Single(item => item.Name == "RegionalRevenue");

        var triggers = visual.PlotPlan!.Interaction!.Triggers;
        Assert.Contains(triggers, trigger => trigger.Trigger == "ON_SELECT" && trigger.Effect == InteractionEffect.Highlight);
        Assert.All(triggers, trigger => Assert.Equal(trigger.Trigger.ToUpperInvariant(), trigger.Trigger));
    }

    [Fact]
    public void SetParameterAction_ResolvesItsParameterOntoTheTrigger()
    {
        var statement = new ETL_SQL.Core.CreateVisualStatement
        {
            Name = "Clickable",
            VisualType = ETL_SQL.Core.VisualType.Bar,
            Source = new ETL_SQL.Core.VisualSourceExpression { TempTableName = "#rows" },
            Interactions = { new ETL_SQL.Core.VisualInteraction { Key = "ON_SELECT", Value = "FILTER" } },
            Actions =
            {
                new ETL_SQL.Core.SetParameterAction
                {
                    Trigger = "ON_CLICK", ParameterName = "@region", ValueExpression = "Region"
                }
            }
        };

        var spec = ChartInteractionResolver.Lower(statement, [Binding(FieldChannel.X, "Region"), Binding(FieldChannel.Y, "Revenue")]);

        var setParameter = Assert.Single(spec.Bindings, binding => binding.Effect == InteractionEffect.SetParameter);
        Assert.Equal("@region", setParameter.Parameter);
        Assert.Contains(spec.Bindings, binding => binding.Trigger == "ON_SELECT" && binding.Effect == InteractionEffect.Filter);
    }

    // ── Tabular visuals ───────────────────────────────────────────────────────────────────

    [Fact]
    public void TabularVisuals_ResolveWithoutAChartContractAndNeverGuessTheKey()
    {
        var statement = new ETL_SQL.Core.CreateVisualStatement
        {
            Name = "Rows",
            VisualType = ETL_SQL.Core.VisualType.Table,
            Source = new ETL_SQL.Core.VisualSourceExpression { TempTableName = "#rows" },
            Mappings = { new ETL_SQL.Core.VisualMapping { Role = "X", Column = "Region" } }
        };

        var withColumn = ChartInteractionResolver.ResolveTabular(statement, new[] { "Revenue", "Region" });
        Assert.Equal("Region", withColumn.Key);
        Assert.Equal(SelectionMode.Multiple, withColumn.Selection);

        var withoutColumn = ChartInteractionResolver.ResolveTabular(statement, new[] { "Revenue", "Orders" });
        Assert.Null(withoutColumn.Key);
    }

    // ── Browser delivery payload shape ────────────────────────────────────────────────────

    [Fact]
    public async Task BrowserDelivery_CarriesRowsSvgAndTheInteractionManifestOnly()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("custom_crossfilter_offset_key.rptsql");

        using var document = JsonDocument.Parse(BrowserDeliveryProjection.Serialize(manifest));
        var visual = document.RootElement.GetProperty("visuals")[0];

        Assert.False(visual.TryGetProperty("chartSpec", out _));
        Assert.False(visual.TryGetProperty("chartData", out _));
        Assert.False(visual.TryGetProperty("plotPlan", out _));
        Assert.False(visual.TryGetProperty("interactions", out _));

        Assert.True(visual.TryGetProperty("rows", out _));
        Assert.True(visual.TryGetProperty("nativeSvg", out _));
        Assert.Equal("Region", visual.GetProperty("interaction").GetProperty("key").GetString());
    }

    [Fact]
    public async Task BrowserDelivery_LeavesTheServerObjectIntactForRenderersAndTests()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("custom_crossfilter_offset_key.rptsql");
        _ = BrowserDeliveryProjection.Serialize(manifest);

        // Projection is a serialization decision, not a mutation: PDF, markdown, and terminal
        // rendering still resolve against the full contracts after a browser payload is produced.
        var visual = manifest.Visuals.Single(item => item.Name == "RegionalRevenue");
        Assert.NotNull(visual.ChartSpec);
        Assert.NotNull(visual.ChartData);
        Assert.NotNull(visual.PlotPlan);
    }

    [Fact]
    public async Task AuthorizedDiagnosticOutput_RetainsTheFullSemanticContracts()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("custom_crossfilter_offset_key.rptsql");

        using var document = JsonDocument.Parse(BrowserDeliveryProjection.SerializeAuthorizedDiagnostic(manifest));
        var visual = document.RootElement.GetProperty("visuals")[0];

        Assert.True(visual.TryGetProperty("chartSpec", out _));
        Assert.True(visual.TryGetProperty("plotPlan", out _));
    }

    [Fact]
    public async Task DiagnosticContracts_AreNotReachableThroughTheNormalBrowserOptions()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("custom_crossfilter_offset_key.rptsql");

        var browser = JsonSerializer.Serialize(manifest, BrowserDeliveryProjection.Options);
        var diagnostic = JsonSerializer.Serialize(manifest, BrowserDeliveryProjection.AuthorizedDiagnosticOptions);

        // The two option sets must not be the same object: a shared instance would let any caller
        // that reaches the browser options serialize a diagnostic payload by accident.
        Assert.NotSame(BrowserDeliveryProjection.Options, BrowserDeliveryProjection.AuthorizedDiagnosticOptions);
        Assert.DoesNotContain("\"plotPlan\"", browser, StringComparison.Ordinal);
        Assert.Contains("\"plotPlan\"", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoredSnapshotJson_IsReprojectedBeforeItReachesABrowser()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("custom_crossfilter_offset_key.rptsql");
        var stored = JsonSerializer.Serialize(manifest);
        Assert.Contains("\"plotPlan\"", stored, StringComparison.Ordinal);

        var projected = BrowserDeliveryProjection.ProjectStoredJson(stored);

        Assert.DoesNotContain("\"plotPlan\"", projected, StringComparison.Ordinal);
        Assert.DoesNotContain("\"chartSpec\"", projected, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(projected);
        Assert.Equal("Region", document.RootElement.GetProperty("visuals")[0]
            .GetProperty("interaction").GetProperty("key").GetString());
    }

    [Fact]
    public void EveryVisualManifestProperty_IsDeliberatelyClassifiedForBrowserDelivery()
    {
        // The wire contract may not grow by accident. A new VisualManifest property is either
        // delivered on purpose or listed as server-only; either way someone decided.
        var known = new HashSet<string>(BrowserDeliveryProjection.ServerOnlyVisualProperties, StringComparer.Ordinal);
        var delivered = typeof(VisualManifest).GetProperties()
            .Where(property => property.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), true)
                .Cast<System.Text.Json.Serialization.JsonIgnoreAttribute>()
                .All(attribute => attribute.Condition != System.Text.Json.Serialization.JsonIgnoreCondition.Always))
            .Select(property => property.Name)
            .ToArray();

        Assert.All(known, name => Assert.Contains(name, delivered));
        Assert.Equal(4, known.Count);
    }

    // ── Interaction refresh ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task InteractionRefresh_KeepsTheResolvedInteractionContractOnTheVisual()
    {
        var (_, manifest, evaluator) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("custom_crossfilter_offset_key.rptsql");
        var before = manifest.Visuals.Single(item => item.Name == "RegionalRevenue").Interaction!.Key;

        await ReportInteractionRefresher.RefreshAffectedVisualsAsync(
            evaluator, manifest, new[] { ("@Region", "North") }, isInteraction: true);

        var after = manifest.Visuals.Single(item => item.Name == "RegionalRevenue");
        Assert.NotNull(after.Interaction);
        Assert.Equal(before, after.Interaction!.Key);
        Assert.DoesNotContain("\"plotPlan\"", BrowserDeliveryProjection.Serialize(manifest), StringComparison.Ordinal);
    }

    // ── Serialization stability ───────────────────────────────────────────────────────────

    [Fact]
    public void InteractionManifest_HasAStableCompactWireShape()
    {
        var resolved = new ResolvedInteraction(
            SelectionMode.Multiple, InteractionEffect.Filter, SelectionHighlightMode.Proportional, [])
        { Key = "Region", ValueKey = "Revenue" };

        var json = JsonSerializer.Serialize(InteractionManifest.From(resolved), BrowserDeliveryProjection.Options);

        Assert.Equal(
            "{\"key\":\"Region\",\"valueKey\":\"Revenue\",\"select\":\"MULTIPLE\",\"effect\":\"FILTER\",\"highlight\":\"PROPORTIONAL\"}",
            json);
    }

    [Fact]
    public void InertInteraction_OmitsItsOptionalKeys()
    {
        var json = JsonSerializer.Serialize(
            InteractionManifest.From(ResolvedInteraction.Inert), BrowserDeliveryProjection.Options);

        Assert.Equal("{\"select\":\"NONE\",\"effect\":\"HIGHLIGHT\",\"highlight\":\"NONE\"}", json);
    }

    [Fact]
    public async Task ResolvedInteraction_SurvivesAPlotPlanRoundTrip()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync("custom_crossfilter_offset_key.rptsql");
        var plan = manifest.Visuals.Single(item => item.Name == "RegionalRevenue").PlotPlan!;

        var roundTripped = ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan));

        // ImmutableArray members make record equality reference-based, so compare the resolved
        // fields the browser projection actually reads.
        Assert.Equal(plan.Interaction!.Key, roundTripped.Interaction!.Key);
        Assert.Equal(plan.Interaction.ValueKey, roundTripped.Interaction.ValueKey);
        Assert.Equal(plan.Interaction.Selection, roundTripped.Interaction.Selection);
        Assert.Equal(plan.Interaction.Effect, roundTripped.Interaction.Effect);
        Assert.Equal(plan.Interaction.Highlight, roundTripped.Interaction.Highlight);
        Assert.Equal(
            plan.Interaction.Triggers.Select(trigger => (trigger.Trigger, trigger.Effect, trigger.Target, trigger.Parameter)),
            roundTripped.Interaction.Triggers.Select(trigger => (trigger.Trigger, trigger.Effect, trigger.Target, trigger.Parameter)));
        Assert.Equal(plan.Layers.Select(layer => layer.ExtentAxis), roundTripped.Layers.Select(layer => layer.ExtentAxis));
        Assert.Equal(plan.Layers.Select(layer => layer.ExtentAnchor), roundTripped.Layers.Select(layer => layer.ExtentAnchor));
    }

    private static string RangedMarksWithExtent(string svg) => string.Join('\n', svg
        .Split('\n')
        .Where(line => line.Contains("plot-range-rect", StringComparison.Ordinal) &&
            line.Contains("data-extent-axis", StringComparison.Ordinal)));

    private static FieldBinding Binding(FieldChannel channel, string field) =>
        new(channel, field, channel == FieldChannel.Y ? DataSemanticKind.Quantitative : DataSemanticKind.Nominal);

    private static ChartSpec Spec(string key, string? measure)
    {
        var bindings = measure is null
            ? new[] { Binding(FieldChannel.X, key) }
            : [Binding(FieldChannel.X, key), Binding(FieldChannel.Y, measure)];
        return ChartSpec.Create("interaction-spec", "#rows", [.. bindings],
            [new MarkLayerSpec("bars", MarkKind.Rect, 0, [.. bindings], [])],
            new CoordinateSpec(CoordinateKind.Cartesian),
            [],
            new FormattingSpec("en-US", "UTC", "-", []),
            new NullHandlingSpec(NullValuePolicy.Gap, []),
            new ThemeSpec("default", []),
            new AccessibilitySpec("Interaction", null, null, true),
            interactions: new InteractionSpec(
                [new SelectionSpec("selection", SelectionMode.Multiple,
                    measure is null ? [key] : [key, measure])],
                [new InteractionBinding("ON_SELECT", InteractionEffect.Highlight)]));
    }
}
