using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;

namespace ETL_SQL.Reporting;

/// <summary>Bounded server-resolved layout canvases available to native charts.</summary>
public enum NativeChartLayoutTier
{
    Compact,
    Standard,
    Wide,
    CompactWide,
    StandardShort,
    WideShort,
    UltraWideShort,
    Square
}

/// <summary>One explicit container-width band and the fixed plot bounds resolved for that band.</summary>
public sealed record NativeChartLayoutInput(
    NativeChartLayoutTier Tier,
    decimal MinimumContainerWidth,
    decimal? MaximumContainerWidth,
    PlotBounds Bounds);

/// <summary>
/// Explicit backend inputs for responsive native charts. Container dimensions select one of a
/// bounded set of canvases; arbitrary viewport dimensions never flow into the semantic resolver.
/// </summary>
public sealed class NativeChartLayoutProfile
{
    private static readonly HashSet<string> RadialTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "PIE", "DONUT", "GAUGE", "RADAR", "SUNBURST"
    };

    private static readonly HashSet<string> MapTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "MAP", "GEO", "CHOROPLETH"
    };

    public static NativeChartLayoutProfile Default { get; } = new(
        new(NativeChartLayoutTier.Compact, 0m, 479m, new PlotBounds(0m, 0m, 480m, 300m)),
        new(NativeChartLayoutTier.Standard, 480m, 959m, new PlotBounds(0m, 0m, 720m, 420m)),
        new(NativeChartLayoutTier.Wide, 960m, null, new PlotBounds(0m, 0m, 1200m, 600m)));

    private readonly IReadOnlyDictionary<NativeChartLayoutTier, NativeChartLayoutInput> _inputs;
    private readonly IReadOnlyDictionary<NativeChartLayoutTier, NativeChartLayoutInput> _allInputs;

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
        var compact = _inputs[NativeChartLayoutTier.Compact];
        var standard = _inputs[NativeChartLayoutTier.Standard];
        var wide = _inputs[NativeChartLayoutTier.Wide];
        var all = new Dictionary<NativeChartLayoutTier, NativeChartLayoutInput>(_inputs)
        {
            [NativeChartLayoutTier.CompactWide] = new(
                NativeChartLayoutTier.CompactWide,
                compact.MinimumContainerWidth,
                compact.MaximumContainerWidth,
                new PlotBounds(0m, 0m, compact.Bounds.Width, 220m)),
            [NativeChartLayoutTier.StandardShort] = new(
                NativeChartLayoutTier.StandardShort,
                standard.MinimumContainerWidth,
                standard.MaximumContainerWidth,
                new PlotBounds(0m, 0m, standard.Bounds.Width, 260m)),
            [NativeChartLayoutTier.WideShort] = new(
                NativeChartLayoutTier.WideShort,
                wide.MinimumContainerWidth,
                wide.MaximumContainerWidth,
                new PlotBounds(0m, 0m, wide.Bounds.Width, 340m)),
            [NativeChartLayoutTier.UltraWideShort] = new(
                NativeChartLayoutTier.UltraWideShort,
                wide.MinimumContainerWidth,
                wide.MaximumContainerWidth,
                new PlotBounds(0m, 0m, 1400m, 260m)),
            [NativeChartLayoutTier.Square] = new(
                NativeChartLayoutTier.Square,
                0m,
                null,
                new PlotBounds(0m, 0m, 520m, 480m))
        };
        _allInputs = all;
    }

    public NativeChartLayoutInput this[NativeChartLayoutTier tier] => _allInputs[tier];

    public NativeChartLayoutTier Resolve(decimal containerWidth) =>
        _inputs.Values
            .OrderBy(item => item.MinimumContainerWidth)
            .First(item => item.MaximumContainerWidth is null || containerWidth <= item.MaximumContainerWidth)
            .Tier;

    public NativeChartLayoutTier Resolve(decimal containerWidth, decimal containerHeight, string? visualType = null)
    {
        var baseTier = Resolve(containerWidth);
        if (containerHeight <= 0m) return baseTier;

        var aspect = containerWidth / containerHeight;
        if (!string.IsNullOrWhiteSpace(visualType))
        {
            var trimmed = visualType.Trim();
            if (MapTypes.Contains(trimmed))
                return baseTier;
            if (RadialTypes.Contains(trimmed))
            {
                return aspect <= 1.35m && containerHeight >= 260m
                    ? NativeChartLayoutTier.Square
                    : baseTier;
            }
        }

        if (aspect <= 1.25m && containerHeight >= 340m)
            return NativeChartLayoutTier.Square;

        return baseTier switch
        {
            NativeChartLayoutTier.Compact => aspect >= 1.95m ? NativeChartLayoutTier.CompactWide : NativeChartLayoutTier.Compact,
            NativeChartLayoutTier.Standard => aspect >= 2.25m ? NativeChartLayoutTier.StandardShort : NativeChartLayoutTier.Standard,
            NativeChartLayoutTier.Wide => aspect >= 4.20m
                ? NativeChartLayoutTier.UltraWideShort
                : aspect >= 2.65m ? NativeChartLayoutTier.WideShort : NativeChartLayoutTier.Wide,
            _ => baseTier
        };
    }

    internal static bool IsAspectLocked(string? visualType)
    {
        if (string.IsNullOrWhiteSpace(visualType)) return false;
        var trimmed = visualType.Trim();
        return MapTypes.Contains(trimmed) || RadialTypes.Contains(trimmed);
    }

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

    /// <summary>
    /// Puts a native visual on the STANDARD canvas.
    /// </summary>
    /// <remarks>
    /// This used to stamp the manifest and stop there, which labelled a chart STANDARD without
    /// drawing it that way: <c>VisualBuilder</c> resolves a plot plan against the resolver's own
    /// 600x350 default, so the SVG stayed 600x350 while the manifest told the browser its canvas
    /// was 720x420. The browser believes the label — its resize observer only asks the server for a
    /// re-layout when the container's tier differs from the stamped one — so every chart in a tile
    /// between 480 and 959 pixels wide kept a canvas 120 pixels narrower than the tier it was
    /// already claiming, and no amount of resizing inside that band corrected it. Resolving here
    /// makes the stamp true.
    /// </remarks>
    public static void StampDefault(VisualManifest visual, NativeChartLayoutProfile? profile = null)
    {
        if (Supports(visual))
            Resolve(visual, NativeChartLayoutTier.Standard, profile);
    }

    /// <summary>
    /// Pre-resolves initial aspect-aware canvases for native charts placed on <c>FIT = 'SCREEN'</c>
    /// pages using each visual's slot width share in <c>STRUCTURE</c>. Gives Studio Preview,
    /// offline snapshots, and first-paint Portal views wide-short canvases immediately without
    /// waiting for a browser <c>/layout</c> round-trip.
    /// </summary>
    public static void ApplyFitPageInitialLayouts(ReportManifest manifest, NativeChartLayoutProfile? profile = null)
    {
        if (manifest.Pages.Count == 0 || manifest.Visuals.Count == 0) return;

        var visualsByName = manifest.Visuals
            .GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var page in manifest.Pages)
        {
            if (page.Options is null
                || !page.Options.TryGetValue("FIT", out var fit)
                || !fit.Trim().Equals("SCREEN", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(page.Structure) || page.SlotMap.Count == 0)
                continue;

            var gridRows = page.Structure
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(r => r.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .Where(r => r.Length > 0)
                .ToArray();
            if (gridRows.Length == 0) continue;

            foreach (var (slot, targetName) in page.SlotMap)
            {
                if (!visualsByName.TryGetValue(targetName, out var visual)) continue;
                if (visual.Layout is not null || !Supports(visual)) continue;
                if (NativeChartLayoutProfile.IsAspectLocked(visual.VisualType)) continue;

                var maxColSpan = 0;
                var totalCols = 0;
                var rowSpan = 0;
                foreach (var row in gridRows)
                {
                    var count = row.Count(token => token.Equals(slot, StringComparison.OrdinalIgnoreCase));
                    if (count > 0)
                    {
                        rowSpan++;
                        if (count > maxColSpan)
                        {
                            maxColSpan = count;
                            totalCols = row.Length;
                        }
                    }
                }
                if (maxColSpan == 0 || totalCols == 0) continue;

                var widthRatio = (decimal)maxColSpan / totalCols;
                var effectiveRows = (decimal)gridRows.Length / Math.Max(1, rowSpan);

                NativeChartLayoutTier? tier = null;
                if (widthRatio >= 0.65m && effectiveRows >= 3m)
                    tier = NativeChartLayoutTier.UltraWideShort;
                else if (widthRatio >= 0.65m)
                    tier = NativeChartLayoutTier.WideShort;
                else if (widthRatio >= 0.35m && effectiveRows >= 3m)
                    tier = NativeChartLayoutTier.StandardShort;
                else if (widthRatio < 0.35m && effectiveRows >= 3m)
                    tier = NativeChartLayoutTier.CompactWide;

                if (tier.HasValue)
                    Resolve(visual, tier.Value, profile);
            }
        }
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
            visual.NativeSvg = new SvgChartRenderer().Render(visual.PlotPlan, PlotSvgRenderOptions.Card);
        }
        else if (FocusedTypes.Contains(visual.VisualType))
        {
            visual.NativeSvg = new SvgChartRenderer().RenderFocusedForCard(visual, selectedProfile[tier].Bounds);
        }
        else
        {
            throw new InvalidOperationException($"Visual '{visual.Name}' does not support responsive native layout.");
        }

        visual.Layout = selectedProfile.Manifest(tier);
    }
}
