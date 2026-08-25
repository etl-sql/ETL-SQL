using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using ETL_SQL.Reporting.Semantics;

namespace ETL_SQL.Reporting.Semantics.Runtime;

/// <summary>
/// The shared presentation inputs a focused native layout module renders against.
///
/// TREEMAP, SUNBURST, SANKEY, NETWORK, MAP, and MATRIX keep their own geometry — squarified tiles,
/// radial arcs, ranked flows — because forcing them through <c>PlotPlan</c> would buy nothing. What
/// they must not keep is a parallel set of *presentation* decisions. Theme, series colour,
/// accessible naming, the resolved interaction key, and canvas size are resolved once here, from
/// the same tokens and the same palette rule <c>PlotPlanResolver</c> uses, so a focused visual and a
/// plan-backed visual on one page agree.
///
/// Sizing is an explicit authored input (<c>OPTIONS (WIDTH = n, HEIGHT = n)</c>), never derived from
/// a viewport. Responsive tiers are a separate, later feature with their own backend inputs.
/// </summary>
public sealed record FocusedLayoutInputs(
    string Title,
    string AccessibleDescription,
    ThemeSpec Theme,
    PlotBounds Bounds,
    ImmutableArray<PaletteAssignment> Palette,
    FocusedInteractionInputs Interaction)
{
    /// <summary>The canvas every focused module has always drawn on, and still defaults to.</summary>
    public static readonly PlotBounds DefaultBounds = new(0m, 0m, 600m, 350m);

    /// <summary>Smallest canvas a focused layout will accept, so a typo cannot produce a 0x0 SVG.</summary>
    private const decimal MinimumEdge = 120m;

    /// <summary>Largest canvas a focused layout will accept; an absurd authored size is clamped, not honoured.</summary>
    private const decimal MaximumEdge = 4000m;

    public double Width => (double)Bounds.Width;

    public double Height => (double)Bounds.Height;

    /// <summary>True when the visual declares a dark theme, so surface tokens flip together.</summary>
    public bool IsDark => Theme.Name.Contains("dark", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Token("MODE"), "DARK", StringComparison.OrdinalIgnoreCase);

    /// <summary>Chart background.</summary>
    public string Surface => Paint(Token("BACKGROUND") ?? Token("BACKGROUND_COLOR"), IsDark ? "#1f2430" : "#ffffff");

    /// <summary>Primary text colour, used for the title and any label drawn on the surface.</summary>
    public string OnSurface => Paint(Token("TEXT_COLOR") ?? Token("COLOR"), IsDark ? "#e6e8ee" : "#1f2937");

    /// <summary>Secondary text and connector colour.</summary>
    public string Muted => Paint(Token("MUTED_COLOR"), IsDark ? "#94a3b8" : "#64748b");

    /// <summary>Divider colour between tiles, cells, and header rows.</summary>
    public string Divider => Paint(Token("GRID_COLOR"), IsDark ? "#2b3242" : "#ffffff");

    /// <summary>
    /// Text drawn on top of a palette fill. The categorical palette is the same mid-tone set in
    /// both themes, so this does not flip with the surface.
    /// </summary>
    public string OnAccent => "#ffffff";

    /// <summary>Header fill for tabular focused layouts.</summary>
    public string HeaderFill => Paint(Token("HEADER_COLOR"), IsDark ? "#2b3242" : "#e2e8f0");

    /// <summary>Alternating body fills for tabular focused layouts.</summary>
    public (string Even, string Odd) BandFills => IsDark ? ("#232a38", "#262e3e") : ("#f8fafc", "#f1f5f9");

    /// <summary>
    /// The colour for a named series. Authored <c>COLOR:&lt;key&gt;</c> tokens win; everything else
    /// falls to the shared default at that position — the same rule the plan-backed path applies.
    /// </summary>
    public string Color(string? seriesKey, int index)
    {
        if (!string.IsNullOrWhiteSpace(seriesKey))
        {
            var assigned = Palette.FirstOrDefault(item =>
                item.SeriesKey.Equals(seriesKey, StringComparison.OrdinalIgnoreCase))?.Color;
            if (ChartPalette.IsSafePaint(assigned)) return assigned!;
        }
        return ChartPalette.Default(index);
    }

    /// <summary>Resolves the shared inputs for a visual, seeding the palette from its own series keys.</summary>
    public static FocusedLayoutInputs From(VisualManifest visual, IEnumerable<string>? seriesKeys = null)
    {
        var theme = ChartStyleTokens.Theme(visual);
        var keys = (seriesKeys ?? []).Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
        var palette = keys
            .Select((key, index) => new PaletteAssignment(key, ChartPalette.Resolve(theme.Tokens, key, index)))
            .ToImmutableArray();
        var title = visual.Options.GetValueOrDefault("title") ?? visual.Name;

        return new FocusedLayoutInputs(
            title,
            Describe(visual, title),
            theme,
            ResolveBounds(visual),
            palette,
            FocusedInteractionInputs.From(visual));
    }

    /// <summary>
    /// The authored canvas: <c>OPTIONS (WIDTH = n, HEIGHT = n)</c>, in CSS pixels. Percentage and
    /// other relative values are layout, not canvas, so they fall back to the default rather than
    /// silently producing a viewport-dependent plan.
    /// </summary>
    public static PlotBounds ResolveBounds(VisualManifest visual) => new(
        0m, 0m,
        Edge(visual, "WIDTH", DefaultBounds.Width),
        Edge(visual, "HEIGHT", DefaultBounds.Height));

    private static decimal Edge(VisualManifest visual, string name, decimal fallback)
    {
        var raw = visual.Options.GetValueOrDefault(name) ?? visual.Styles?.GetValueOrDefault(name);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        var trimmed = raw.Trim();
        if (trimmed.EndsWith("px", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[..^2].Trim();
        return decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, MinimumEdge, MaximumEdge)
            : fallback;
    }

    private static string Describe(VisualManifest visual, string title)
    {
        var summary = visual.SemanticFallback?.Summary ?? visual.SemanticFallback?.Heading;
        return string.IsNullOrWhiteSpace(summary) ? title : $"{title}. {summary}";
    }

    private string? Token(string name) => Theme.Tokens.IsDefaultOrEmpty
        ? null
        : Theme.Tokens.FirstOrDefault(token => token.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static string Paint(string? candidate, string fallback) =>
        ChartPalette.IsSafePaint(candidate) ? candidate!.Trim() : fallback;
}

/// <summary>
/// The compact resolved interaction metadata a focused module stamps onto its marks, so the browser
/// raises a cross-filter from a focused visual with the same resolved key a plan-backed visual uses.
/// </summary>
public sealed record FocusedInteractionInputs(string? Key, string? ValueKey, string Select, string Highlight)
{
    /// <summary>Nothing selectable: the visual declared no cross-filter.</summary>
    public static readonly FocusedInteractionInputs None = new(null, null, "NONE", "NONE");

    /// <summary>Whether a click on a mark can raise a selection at all.</summary>
    public bool IsSelectable => !string.Equals(Select, "NONE", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(Key);

    public static FocusedInteractionInputs From(VisualManifest visual) => visual.Interaction is { } resolved
        ? new FocusedInteractionInputs(resolved.Key, resolved.ValueKey, resolved.Select, resolved.Highlight)
        : None;
}
