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
    string Interactions, bool HasExternalChartDependency, string Notes);

/// <summary>
/// Source-backed inventory of current browser, export, and terminal rendering paths.
/// </summary>
public static class VisualCapabilityMatrix
{
    private static readonly HashSet<VisualType> NativeSvgCharts =
    [
        VisualType.Bar, VisualType.HorizontalBar, VisualType.Line, VisualType.Scatter,
        VisualType.Bubble, VisualType.HeatMap, VisualType.Funnel, VisualType.Gauge,
        VisualType.BoxPlot, VisualType.Waterfall, VisualType.Candlestick,
        VisualType.Trellis,
        VisualType.Gantt,
        VisualType.Radar,
        VisualType.Pie, VisualType.Donut, VisualType.Combo, VisualType.Custom,
        VisualType.Treemap, VisualType.Sunburst, VisualType.Sankey, VisualType.Network,
        VisualType.Map, VisualType.Matrix
    ];

    private static readonly HashSet<VisualType> MigratedPlotPlanCharts =
    [
        VisualType.Bar, VisualType.HorizontalBar, VisualType.Line, VisualType.Scatter,
        VisualType.Bubble, VisualType.HeatMap, VisualType.Funnel, VisualType.Gauge,
        VisualType.BoxPlot, VisualType.Waterfall, VisualType.Candlestick,
        VisualType.Trellis,
        VisualType.Gantt,
        VisualType.Radar,
        VisualType.Pie, VisualType.Donut, VisualType.Combo, VisualType.Custom
    ];

    private static readonly HashSet<VisualType> TerminalSemanticFallbacks =
    [
        VisualType.Treemap, VisualType.Image, VisualType.Radar,
        VisualType.Map, VisualType.Sankey, VisualType.Sunburst, VisualType.Network
    ];

    private static readonly Lazy<IReadOnlyList<VisualCapabilityEntry>> Entries = new(BuildMatrix);

    public static IReadOnlyList<VisualCapabilityEntry> AllCapabilities => Entries.Value;
    public static IReadOnlySet<VisualType> NativeSvgVisualTypes => NativeSvgCharts;
    public static IReadOnlySet<VisualType> ExternalChartDependencyVisualTypes { get; } = new HashSet<VisualType>();
    public static IReadOnlySet<VisualType> TerminalFallbackVisualTypes => TerminalSemanticFallbacks;

    public static VisualCapabilityEntry Get(VisualType type) =>
        AllCapabilities.FirstOrDefault(entry => entry.Type == type)
        ?? throw new KeyNotFoundException($"No capability entry found for VisualType.{type}");

    public static string ToMarkdownTable()
    {
        var builder = new StringBuilder();
        builder.AppendLine("| Visual Type | Category | Browser | Static Export | PDF / Email | Terminal | Interactions | External Chart Runtime | Notes |");
        builder.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :---: | :--- |");
        foreach (var capability in AllCapabilities)
        {
            builder.AppendLine($"| `{capability.Name}` | {capability.Category} | {Format(capability.Browser)} | {Format(capability.StaticExport)} | {Format(capability.PdfEmailExport)} | {Format(capability.Terminal)} | {capability.Interactions} | {(capability.HasExternalChartDependency ? "Yes" : "No")} | {capability.Notes} |");
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
        Chart(VisualType.Line, "LINE", "Cartesian", "line", "Click, cross-filter, tooltip"),
        Chart(VisualType.Scatter, "SCATTER", "Cartesian", "scatter", "Click, cross-filter, tooltip"),
        Chart(VisualType.Pie, "PIE", "Circular", "pie", "Slice click, cross-filter, tooltip"),
        Chart(VisualType.Donut, "DONUT", "Circular", "donut", "Slice click, cross-filter, tooltip"),
        Chart(VisualType.BoxPlot, "BOXPLOT", "Statistical", "box plot", "Tooltip"),
        Chart(VisualType.Treemap, "TREEMAP", "Hierarchical", "treemap", "Rect click, drill context, tooltip"),
        Chart(VisualType.HeatMap, "HEATMAP", "Matrix / Grid", "heat map", "Cell click, cross-filter, tooltip"),
        Chart(VisualType.Combo, "COMBO", "Layered", "bar/line combo", "Click, cross-filter, tooltip"),
        Chart(VisualType.Custom, "CUSTOM", "Advanced / Layered", "advanced chart", "Click, cross-filter, tooltip"),
        Control(VisualType.Table, "TABLE", "Tabular", "Tabulator / HTML table", "Markdown, CSV, and static table exporters", "Spectre table", "Sort, filter, pagination, row click", browserLevel: CapabilityLevel.ThirdPartyDependency),
        Control(VisualType.Card, "CARD", "KPI", "native DOM card", "Markdown and static card exporters", "Spectre panel", "Click, navigation"),
        Control(VisualType.Slicer, "SLICER", "Filter / Control", "native DOM control", "omitted from non-browser exports", "Spectre selection summary", "Selection, parameter binding", false),
        Control(VisualType.Text, "TEXT", "Narrative", "native DOM / Markdown", "Markdown and HTML", "plain text / Spectre", "None"),
        Chart(VisualType.Gauge, "GAUGE", "Indicator", "gauge", "Tooltip"),
        Chart(VisualType.Funnel, "FUNNEL", "Flow", "funnel", "Stage select, tooltip"),
        Chart(VisualType.Waterfall, "WATERFALL", "Variance", "waterfall", "Click, tooltip"),
        Control(VisualType.Image, "IMAGE", "Media", "native img element", "HTML image reference", "text placeholder", "Click link"),
        Chart(VisualType.Bubble, "BUBBLE", "Cartesian", "sized scatter", "Click, cross-filter, tooltip"),
        Chart(VisualType.Radar, "RADAR", "Polar", "radar", "Click, tooltip"),
        Chart(VisualType.Candlestick, "CANDLESTICK", "Financial", "candlestick", "Click, tooltip"),
        Chart(VisualType.Map, "MAP", "Geographic", "map / GeoJSON", "Region/point click, cross-filter, tooltip"),
        Chart(VisualType.Gantt, "GANTT", "Timeline", "timeline", "Task click, tooltip"),
        Control(VisualType.DatePicker, "DATEPICKER", "Filter / Control", "native date control", "omitted from non-browser exports", "Spectre selection summary", "Date selection, parameter binding", false),
        Control(VisualType.RelDatePicker, "RELDATEPICKER", "Filter / Control", "native relative-date control", "omitted from non-browser exports", "Spectre selection summary", "Preset selection, parameter binding", false),
        Control(VisualType.Slider, "SLIDER", "Filter / Control", "native range control", "omitted from non-browser exports", "Spectre selection summary", "Range input, parameter binding", false),
        Control(VisualType.MultiSelect, "MULTISELECT", "Filter / Control", "native multi-select control", "omitted from non-browser exports", "Spectre selection summary", "Selection, parameter binding", false),
        Control(VisualType.Search, "SEARCH", "Filter / Control", "native search control", "omitted from non-browser exports", "Spectre selection summary", "Text input, parameter binding", false),
        Control(VisualType.Checkbox, "CHECKBOX", "Filter / Control", "native checkbox", "omitted from non-browser exports", "Spectre selection summary", "Toggle, parameter binding", false),
        Control(VisualType.Textbox, "TEXTBOX", "Filter / Control", "native text input", "omitted from non-browser exports", "Spectre selection summary", "Text input, parameter binding", false),
        Control(VisualType.Numberbox, "NUMBERBOX", "Filter / Control", "native number input", "omitted from non-browser exports", "Spectre selection summary", "Numeric input, parameter binding", false),
        Chart(VisualType.Sankey, "SANKEY", "Flow / Network", "sankey", "Link click, tooltip"),
        Chart(VisualType.Sunburst, "SUNBURST", "Hierarchical", "sunburst", "Arc click, drill context, tooltip"),
        Chart(VisualType.Network, "NETWORK", "Graph", "network", "Link click, tooltip"),
        Chart(VisualType.Trellis, "TRELLIS", "Small Multiples", "trellis", "Mark click, tooltip"),
        new(
            VisualType.Matrix, "MATRIX", "Pivot / Matrix",
            new(CapabilityLevel.Native, "native SVG matrix"),
            new(CapabilityLevel.Native, "native SVG matrix and tabular exporters"),
            new(CapabilityLevel.Native, "native SVG to PDF; email attaches PDF/CSV/Markdown"),
            new(CapabilityLevel.Native, "Spectre matrix"),
            "Row click", false,
            "Native SVG matrix with semantic table fallbacks"),
        new(
            VisualType.Html, "HTML", "Template / Bespoke",
            new(CapabilityLevel.Unsupported, "runtime rendering not implemented"),
            new(CapabilityLevel.Unsupported, "static export not implemented"),
            new(CapabilityLevel.Unsupported, "PDF and email export not implemented"),
            new(CapabilityLevel.Unsupported, "terminal fallback not wired"),
            "Not implemented", false,
            "Parser, formatter, evaluator, sanitizer, and initial manifest projection only")
    ];

    private static VisualCapabilityEntry Chart(VisualType type, string name, string category, string chartKind, string interactions)
    {
        var nativeSvg = NativeSvgCharts.Contains(type);
        var migrated = MigratedPlotPlanCharts.Contains(type);
        var terminalFallback = TerminalSemanticFallbacks.Contains(type);
        return new(
            type, name, category,
            new(CapabilityLevel.Native, migrated ? $"PlotPlan native SVG {chartKind}" : $"specialized native SVG {chartKind}"),
            new(CapabilityLevel.Native, migrated ? "PlotPlan native SVG" : "specialized native SVG renderer"),
            new(CapabilityLevel.Native, "native SVG to static PDF; email attaches PDF/Markdown"),
            migrated
                ? new(CapabilityLevel.Native, "PlotPlan semantic terminal renderer")
                : terminalFallback
                ? new(CapabilityLevel.SemanticFallback, TerminalFallbackDescription(type))
                : new(CapabilityLevel.Native, "Spectre terminal renderer"),
            interactions, false,
            migrated ? "Shared renderer-neutral PlotPlan path" : "Approved focused native layout module");
    }

    private static string TerminalFallbackDescription(VisualType type) => type switch
    {
        VisualType.Map => "ranked regional breakdown",
        VisualType.Sankey => "transition and source drop-off table",
        VisualType.Treemap or VisualType.Sunburst => "ordered proportional hierarchy",
        VisualType.Network => "node-degree and connection summary",
        VisualType.Radar => "ordered dimension/value table",
        _ => "ordered accessible summary"
    };

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
            exported ? "Native rendering path" : "Interactive-only visual; terminal shows current selection state");
}
