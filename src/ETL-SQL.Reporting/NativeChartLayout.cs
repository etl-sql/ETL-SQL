using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;

namespace ETL_SQL.Reporting;

/// <summary>The three bounded server-resolved layouts available to native charts.</summary>
public enum NativeChartLayoutTier
{
    Compact,
    Standard,
    Wide
}

/// <summary>One explicit container-width band and the fixed plot bounds resolved for that band.</summary>
public sealed record NativeChartLayoutInput(
    NativeChartLayoutTier Tier,
    decimal MinimumContainerWidth,
    decimal? MaximumContainerWidth,
    PlotBounds Bounds);

/// <summary>
/// Explicit backend inputs for responsive native charts. Container widths select one of three
/// bounded canvases; arbitrary viewport dimensions never flow into the semantic resolver.
/// </summary>
public sealed class NativeChartLayoutProfile
{
    public static NativeChartLayoutProfile Default { get; } = new(
        new(NativeChartLayoutTier.Compact, 0m, 479m, new PlotBounds(0m, 0m, 480m, 300m)),
        new(NativeChartLayoutTier.Standard, 480m, 959m, new PlotBounds(0m, 0m, 720m, 420m)),
        new(NativeChartLayoutTier.Wide, 960m, null, new PlotBounds(0m, 0m, 1200m, 600m)));

    private readonly IReadOnlyDictionary<NativeChartLayoutTier, NativeChartLayoutInput> _inputs;

    public NativeChartLayoutProfile(params NativeChartLayoutInput[] inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Length != 3 || inputs.Select(item => item.Tier).Distinct().Count() != 3)
            throw new ArgumentException("Native chart layout requires one COMPACT, STANDARD, and WIDE input.", nameof(inputs));

        var ordered = inputs.OrderBy(item => item.MinimumContainerWidth).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var input = ordered[index];
            if (input.MinimumContainerWidth < 0m || input.Bounds.Width < 120m || input.Bounds.Height < 120m)
                throw new ArgumentOutOfRangeException(nameof(inputs), "Native chart tier bounds must be positive and at least 120 pixels per edge.");
            if (input.MaximumContainerWidth is { } maximum && maximum < input.MinimumContainerWidth)
                throw new ArgumentException("A native chart tier maximum must not precede its minimum.", nameof(inputs));
            if (index > 0)
            {
                var previousMaximum = ordered[index - 1].MaximumContainerWidth;
                if (previousMaximum is null || input.MinimumContainerWidth != previousMaximum.Value + 1m)
                    throw new ArgumentException("Native chart layout tiers must be contiguous.", nameof(inputs));
            }
        }
        if (ordered[^1].MaximumContainerWidth is not null)
            throw new ArgumentException("The WIDE native chart tier must have no upper container bound.", nameof(inputs));

        _inputs = inputs.ToDictionary(item => item.Tier);
    }

    public NativeChartLayoutInput this[NativeChartLayoutTier tier] => _inputs[tier];

    public NativeChartLayoutTier Resolve(decimal containerWidth) =>
        _inputs.Values
            .OrderBy(item => item.MinimumContainerWidth)
            .First(item => item.MaximumContainerWidth is null || containerWidth <= item.MaximumContainerWidth)
            .Tier;

    public NativeChartLayoutManifest Manifest(NativeChartLayoutTier tier)
    {
        var compact = this[NativeChartLayoutTier.Compact];
        var standard = this[NativeChartLayoutTier.Standard];
        var selected = this[tier];
        return new NativeChartLayoutManifest
        {
            Tier = tier.ToString().ToUpperInvariant(),
            CompactMaxWidth = compact.MaximumContainerWidth!.Value,
            StandardMaxWidth = standard.MaximumContainerWidth!.Value,
            Width = selected.Bounds.Width,
            Height = selected.Bounds.Height
        };
    }
}

/// <summary>Re-resolves a native visual for a bounded tier without fetching its data again.</summary>
public static class NativeChartLayoutResolver
{
    private static readonly HashSet<string> FocusedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "TREEMAP", "SUNBURST", "SANKEY", "NETWORK", "MAP", "MATRIX"
    };

    public static bool Supports(VisualManifest visual) =>
        visual.ChartSpec is not null && visual.ChartData is not null || FocusedTypes.Contains(visual.VisualType);

    public static void StampDefault(VisualManifest visual, NativeChartLayoutProfile? profile = null)
    {
        if (Supports(visual))
            visual.Layout = (profile ?? NativeChartLayoutProfile.Default).Manifest(NativeChartLayoutTier.Standard);
    }

    public static void Resolve(VisualManifest visual, NativeChartLayoutTier tier, NativeChartLayoutProfile? profile = null)
    {
        var selectedProfile = profile ?? NativeChartLayoutProfile.Default;
        if (visual.ChartSpec is not null && visual.ChartData is not null)
        {
            var resolver = new PlotPlanResolver();
            visual.PlotPlan = visual.PlotPlan is null
                ? resolver.Resolve(visual.ChartSpec, visual.ChartData, selectedProfile[tier].Bounds, visual.GeographicGeometry)
                : resolver.Relayout(visual.ChartSpec, visual.ChartData, visual.PlotPlan, selectedProfile[tier].Bounds);
            visual.Interaction = visual.PlotPlan.Interaction is null
                ? null
                : InteractionManifest.From(visual.PlotPlan.Interaction);
            visual.NativeSvg = new SvgChartRenderer().Render(visual.PlotPlan);
        }
        else if (FocusedTypes.Contains(visual.VisualType))
        {
            visual.NativeSvg = new SvgChartRenderer().RenderFocused(visual, selectedProfile[tier].Bounds);
        }
        else
        {
            throw new InvalidOperationException($"Visual '{visual.Name}' does not support responsive native layout.");
        }

        visual.Layout = selectedProfile.Manifest(tier);
    }
}
