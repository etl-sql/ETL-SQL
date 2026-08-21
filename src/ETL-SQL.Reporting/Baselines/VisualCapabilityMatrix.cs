using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ETL_SQL.Core;

namespace ETL_SQL.Reporting.Baselines;

public enum CapabilityLevel { Native, SemanticFallback, ThirdPartyDependency, TemporaryDependency, Unsupported }

public sealed record SurfaceCapability(CapabilityLevel Level, string Implementation, string Notes = "");

public sealed record VisualCapabilityEntry(
    VisualType Type, string Name, string Category,
    SurfaceCapability Browser, SurfaceCapability StaticExport,
    SurfaceCapability PdfEmailExport, SurfaceCapability Terminal,
    string Interactions, bool HasEChartsDependency, string Notes);

/// <summary>
/// Source-backed inventory of the current rendering paths. TemporaryDependency
/// identifies Phase 3 replacement work; it is not a claim of native support.
/// </summary>
public static class VisualCapabilityMatrix
{
    private static readonly HashSet<VisualType> NativeSvgCharts =
    [
        VisualType.Bar, VisualType.HorizontalBar, VisualType.Line,
        VisualType.Pie, VisualType.Donut
    ];

    private static readonly HashSet<VisualType> EChartsCharts =
    [
        VisualType.Bar, VisualType.HorizontalBar, VisualType.Line, VisualType.Scatter,
        VisualType.Pie, VisualType.Donut, VisualType.BoxPlot, VisualType.Treemap,
        VisualType.HeatMap, VisualType.Combo, VisualType.Gauge, VisualType.Funnel,
        VisualType.Waterfall, VisualType.Bubble, VisualType.Radar, VisualType.Candlestick,
        VisualType.Map, VisualType.Gantt, VisualType.Sankey, VisualType.Sunburst,
        VisualType.Network, VisualType.Trellis, VisualType.Matrix
    ];

    private static readonly HashSet<VisualType> TerminalSemanticFallbacks =
    [
        VisualType.Combo, VisualType.Treemap, VisualType.Image, VisualType.Radar,
        VisualType.Map, VisualType.Sankey, VisualType.Sunburst, VisualType.Network
    ];

    private static readonly Lazy<IReadOnlyList<VisualCapabilityEntry>> Entries = new(BuildMatrix);

    public static IReadOnlyList<VisualCapabilityEntry> AllCapabilities => Entries.Value;
    public static IReadOnlySet<VisualType> NativeSvgVisualTypes => NativeSvgCharts;
    public static IReadOnlySet<VisualType> EChartsVisualTypes => EChartsCharts;
    public static IReadOnlySet<VisualType> TerminalFallbackVisualTypes => TerminalSemanticFallbacks;

    public static VisualCapabilityEntry Get(VisualType type) =>
        AllCapabilities.FirstOrDefault(entry => entry.Type == type)
        ?? throw new KeyNotFoundException($"No capability entry found for VisualType.{type}");

    public static string ToMarkdownTable()
    {
        var builder = new StringBuilder();
        builder.AppendLine("| Visual Type | Category | Browser | Static Export | PDF / Email | Terminal | Interactions | ECharts | Notes |");
        builder.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :---: | :--- |");
        foreach (var capability in AllCapabilities)
        {
            builder.AppendLine($"| `{capability.Name}` | {capability.Category} | {Format(capability.Browser)} | {Format(capability.StaticExport)} | {Format(capability.PdfEmailExport)} | {Format(capability.Terminal)} | {capability.Interactions} | {(capability.HasEChartsDependency ? "Yes" : "No")} | {capability.Notes} |");
        }

        return builder.ToString();
    }

    public static string ToJson(bool indented = true) =>
        JsonSerializer.Serialize(AllCapabilities, new JsonSerializerOptions
        {
            WriteIndented = indented,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        });

    private static string Format(SurfaceCapability capability) =>
        $"**{capability.Level}** — {capability.Implementation}" +
        (string.IsNullOrWhiteSpace(capability.Notes) ? string.Empty : $" ({capability.Notes})");

    private static List<VisualCapabilityEntry> BuildMatrix() =>
    [
        Chart(VisualType.Bar, "BAR", "Cartesian", "bar", "Click, drill, cross-filter, tooltip"),
        Chart(VisualType.HorizontalBar, "HBAR", "Cartesian", "horizontal bar", "Click, drill, cross-filter, tooltip"),
        Chart(VisualType.Line, "LINE", "Cartesian", "line", "Click, zoom/pan, tooltip"),
        Chart(VisualType.Scatter, "SCATTER", "Cartesian", "scatter", "Click, brush, zoom/pan, tooltip"),
        Chart(VisualType.Pie, "PIE", "Circular", "pie", "Slice select, legend toggle, tooltip"),
        Chart(VisualType.Donut, "DONUT", "Circular", "donut", "Slice select, legend toggle, tooltip"),
        Chart(VisualType.BoxPlot, "BOXPLOT", "Statistical", "box plot", "Tooltip"),
        Chart(VisualType.Treemap, "TREEMAP", "Hierarchical", "treemap", "Drill, zoom, breadcrumb"),
        Chart(VisualType.HeatMap, "HEATMAP", "Matrix / Grid", "heat map", "Cell click, visual-map filter, tooltip"),
        Chart(VisualType.Combo, "COMBO", "Layered", "bar/line combo", "Click, series toggle, tooltip"),
        Control(VisualType.Table, "TABLE", "Tabular", "Tabulator / HTML table", "Markdown, CSV, and static table exporters", "Spectre table", "Sort, filter, pagination, row click", browserLevel: CapabilityLevel.ThirdPartyDependency),
        Control(VisualType.Card, "CARD", "KPI", "native DOM card", "Markdown and static card exporters", "Spectre panel", "Click, navigation"),
        Control(VisualType.Slicer, "SLICER", "Filter / Control", "native DOM control", "omitted from non-browser exports", "Spectre selection summary", "Selection, parameter binding", false),
        Control(VisualType.Text, "TEXT", "Narrative", "native DOM / Markdown", "Markdown and HTML", "plain text / Spectre", "None"),
        Chart(VisualType.Gauge, "GAUGE", "Indicator", "gauge", "Tooltip"),
        Chart(VisualType.Funnel, "FUNNEL", "Flow", "funnel", "Stage select, tooltip"),
        Chart(VisualType.Waterfall, "WATERFALL", "Variance", "waterfall", "Click, tooltip"),
        Control(VisualType.Image, "IMAGE", "Media", "native img element", "HTML image reference", "text placeholder", "Click link"),
        Chart(VisualType.Bubble, "BUBBLE", "Cartesian", "sized scatter", "Click, zoom/pan, tooltip"),
        Chart(VisualType.Radar, "RADAR", "Polar", "radar", "Hover, legend toggle"),
        Chart(VisualType.Candlestick, "CANDLESTICK", "Financial", "candlestick", "Zoom/pan, tooltip"),
        Chart(VisualType.Map, "MAP", "Geographic", "map / GeoJSON", "Region click, zoom/pan, tooltip"),
        Chart(VisualType.Gantt, "GANTT", "Timeline", "custom timeline", "Hover, zoom"),
        Control(VisualType.DatePicker, "DATEPICKER", "Filter / Control", "native date control", "omitted from non-browser exports", "Spectre selection summary", "Date selection, parameter binding", false),
        Control(VisualType.RelDatePicker, "RELDATEPICKER", "Filter / Control", "native relative-date control", "omitted from non-browser exports", "Spectre selection summary", "Preset selection, parameter binding", false),
        Control(VisualType.Slider, "SLIDER", "Filter / Control", "native range control", "omitted from non-browser exports", "Spectre selection summary", "Range input, parameter binding", false),
        Control(VisualType.MultiSelect, "MULTISELECT", "Filter / Control", "native multi-select control", "omitted from non-browser exports", "Spectre selection summary", "Selection, parameter binding", false),
        Control(VisualType.Search, "SEARCH", "Filter / Control", "native search control", "omitted from non-browser exports", "Spectre selection summary", "Text input, parameter binding", false),
        Control(VisualType.Checkbox, "CHECKBOX", "Filter / Control", "native checkbox", "omitted from non-browser exports", "Spectre selection summary", "Toggle, parameter binding", false),
        Control(VisualType.Textbox, "TEXTBOX", "Filter / Control", "native text input", "omitted from non-browser exports", "Spectre selection summary", "Text input, parameter binding", false),
        Control(VisualType.Numberbox, "NUMBERBOX", "Filter / Control", "native number input", "omitted from non-browser exports", "Spectre selection summary", "Numeric input, parameter binding", false),
        Chart(VisualType.Sankey, "SANKEY", "Flow / Network", "sankey", "Node/edge highlight, tooltip"),
        Chart(VisualType.Sunburst, "SUNBURST", "Hierarchical", "sunburst", "Drill, tooltip"),
        Chart(VisualType.Network, "NETWORK", "Graph", "force graph", "Drag, zoom/pan, click"),
        Chart(VisualType.Trellis, "TRELLIS", "Small Multiples", "trellis", "Synchronized tooltip"),
        new(
            VisualType.Matrix, "MATRIX", "Pivot / Matrix",
            new(CapabilityLevel.Native, "native DOM matrix"),
            new(CapabilityLevel.TemporaryDependency, "ECharts SSR matrix", "tabular exporters are also available"),
            new(CapabilityLevel.TemporaryDependency, "static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown"),
            new(CapabilityLevel.Native, "Spectre matrix"),
            "Expand/collapse, sorting, aggregation", true,
            "Browser runtime dispatches MATRIX to renderMatrix; the static ECharts renderer still supports it")
    ];

    private static VisualCapabilityEntry Chart(VisualType type, string name, string category, string chartKind, string interactions)
    {
        var nativeSvg = NativeSvgCharts.Contains(type);
        var terminalFallback = TerminalSemanticFallbacks.Contains(type);
        return new(
            type, name, category,
            new(CapabilityLevel.TemporaryDependency, $"ECharts {chartKind}"),
            nativeSvg
                ? new(CapabilityLevel.Native, "SvgChartRenderer")
                : new(CapabilityLevel.TemporaryDependency, "ECharts SSR SVG", "SvgChartRenderer emits a semantic placeholder if SSR is unavailable"),
            new(CapabilityLevel.TemporaryDependency, "static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown"),
            terminalFallback
                ? new(CapabilityLevel.SemanticFallback, "textual summary / placeholder")
                : new(CapabilityLevel.Native, "Spectre terminal renderer"),
            interactions, true,
            nativeSvg ? "Native static SVG exists; browser rendering still uses ECharts" : "PlotPlan migration target");
    }

    private static VisualCapabilityEntry Control(
        VisualType type, string name, string category, string browser,
        string staticExport, string terminal, string interactions, bool exported = true,
        CapabilityLevel browserLevel = CapabilityLevel.Native) =>
        new(
            type, name, category,
            new(browserLevel, browser),
            new(exported ? CapabilityLevel.Native : CapabilityLevel.Unsupported, staticExport),
            new(exported ? CapabilityLevel.Native : CapabilityLevel.Unsupported,
                exported ? "static PDF and email attachment formats" : "interactive control is not exported"),
            new(TerminalSemanticFallbacks.Contains(type) ? CapabilityLevel.SemanticFallback : CapabilityLevel.Native, terminal),
            interactions, false,
            exported ? "Non-ECharts rendering path" : "Interactive-only visual; terminal shows current selection state");
}
