using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using ETL_SQL.Core;

namespace ETL_SQL.Reporting.Baselines;

/// <summary>
/// Represents the capability and multi-surface rendering status for a specific visual type.
/// </summary>
public record VisualCapabilityEntry(
    VisualType Type,
    string Name,
    string Category,
    string BrowserRendering,
    string SvgStaticExport,
    string PdfEmailExport,
    string Terminal,
    string Interactions,
    bool HasEChartsDependency,
    string Notes);

/// <summary>
/// Authoritative capability matrix covering every visual type in ETL-SQL and its current
/// multi-surface support status (Browser, SVG/Static, PDF/Email, Terminal, Interactions, ECharts).
/// </summary>
public static class VisualCapabilityMatrix
{
    private static readonly Lazy<IReadOnlyList<VisualCapabilityEntry>> _entries = new(BuildMatrix);

    public static IReadOnlyList<VisualCapabilityEntry> AllCapabilities => _entries.Value;

    public static VisualCapabilityEntry Get(VisualType type) =>
        AllCapabilities.FirstOrDefault(e => e.Type == type)
        ?? throw new KeyNotFoundException($"No capability entry found for VisualType.{type}");

    public static string ToMarkdownTable()
    {
        var sb = new StringBuilder();
        sb.AppendLine("| Visual Type | Category | Browser Rendering | SVG / Static Export | PDF / Email | Terminal | Interactions | ECharts Dep | Notes |");
        sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :---: | :--- |");

        foreach (var c in AllCapabilities)
        {
            var echartsBadge = c.HasEChartsDependency ? "Yes" : "No";
            sb.AppendLine($"| `{c.Name}` | {c.Category} | {c.BrowserRendering} | {c.SvgStaticExport} | {c.PdfEmailExport} | {c.Terminal} | {c.Interactions} | {echartsBadge} | {c.Notes} |");
        }

        return sb.ToString();
    }

    public static string ToJson(bool indented = true)
    {
        return JsonSerializer.Serialize(AllCapabilities, new JsonSerializerOptions
        {
            WriteIndented = indented,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private static List<VisualCapabilityEntry> BuildMatrix()
    {
        return new List<VisualCapabilityEntry>
        {
            // ── Cartesian Charts ──────────────────────────────────────────────
            new(
                VisualType.Bar,
                "BAR",
                "Cartesian Chart",
                "ECharts (bar)",
                "Supported (SvgChartRenderer)",
                "Supported (Chromium / Static)",
                "Supported (Bar / Braille)",
                "Click, Drill, Cross-filter, Tooltip",
                true,
                "Supports grouped, stacked, overlays, and custom colors"
            ),
            new(
                VisualType.HorizontalBar,
                "HBAR",
                "Cartesian Chart",
                "ECharts (bar, inverted)",
                "Supported (SvgChartRenderer)",
                "Supported (Chromium / Static)",
                "Supported (Bar / Braille)",
                "Click, Drill, Cross-filter, Tooltip",
                true,
                "Horizontal layout orientation"
            ),
            new(
                VisualType.Line,
                "LINE",
                "Cartesian Chart",
                "ECharts (line)",
                "Supported (SvgChartRenderer)",
                "Supported (Chromium / Static)",
                "Supported (BrailleCanvas)",
                "Click, Zoom/Pan, Tooltip",
                true,
                "Supports smooth curves, step lines, area fill, overlays"
            ),
            new(
                VisualType.Scatter,
                "SCATTER",
                "Cartesian Chart",
                "ECharts (scatter)",
                "Supported (SvgChartRenderer)",
                "Supported (Chromium / Static)",
                "Supported (BrailleCanvas)",
                "Click, Zoom/Pan, Brush, Tooltip",
                true,
                "Supports X, Y, Size, and Color dimension mappings"
            ),
            new(
                VisualType.Combo,
                "COMBO",
                "Cartesian (Layered)",
                "ECharts (multi-series bar/line)",
                "Supported (SvgChartRenderer)",
                "Supported (Chromium / Static)",
                "Supported (Braille / Text)",
                "Click, Series toggle, Tooltip",
                true,
                "Combines multiple BAR and LINE series with dual axes"
            ),
            new(
                VisualType.Waterfall,
                "WATERFALL",
                "Cartesian (Variance)",
                "ECharts (custom bar)",
                "Supported (SvgChartRenderer)",
                "Supported (Chromium / Static)",
                "Unsupported",
                "Click, Tooltip",
                true,
                "Step-wise incremental variance breakdown"
            ),
            new(
                VisualType.Bubble,
                "BUBBLE",
                "Cartesian (3D)",
                "ECharts (scatter with symbol size)",
                "Supported (SvgChartRenderer)",
                "Supported (Chromium / Static)",
                "Supported (BrailleCanvas)",
                "Zoom/Pan, Hover, Click",
                true,
                "Multi-dimensional bubble chart with coordinate scaling"
            ),
            new(
                VisualType.Candlestick,
                "CANDLESTICK",
                "Financial",
                "ECharts (candlestick/k-line)",
                "Supported (SvgChartRenderer)",
                "Supported (Chromium / Static)",
                "Unsupported",
                "Zoom/Pan, Data zoom slider, Hover",
                true,
                "Open, Close, High, Low financial visualization"
            ),

            // ── Radial / Polar Charts ─────────────────────────────────────────
            new(
                VisualType.Pie,
                "PIE",
                "Circular / Polar",
                "ECharts (pie)",
                "Supported (SvgChartRenderer)",
                "Supported (Chromium / Static)",
                "Supported (Text / Braille)",
                "Slice select, Legend toggle, Tooltip",
                true,
                "Proportional breakdown with label formatting"
            ),
            new(
                VisualType.Donut,
                "DONUT",
                "Circular / Polar",
                "ECharts (pie with inner radius)",
                "Supported (SvgChartRenderer)",
                "Supported (Chromium / Static)",
                "Supported (Text / Braille)",
                "Slice select, Center metric text, Tooltip",
                true,
                "Donut variation with configurable inner hole radius"
            ),
            new(
                VisualType.Radar,
                "RADAR",
                "Polar / Multi-axis",
                "ECharts (radar)",
                "Supported (SvgChartRenderer)",
                "Supported (Chromium / Static)",
                "Unsupported",
                "Hover, Legend toggle",
                true,
                "Spider / web multi-metric polygon analysis"
            ),
            new(
                VisualType.Sunburst,
                "SUNBURST",
                "Hierarchical Radial",
                "ECharts (sunburst)",
                "Supported (SvgChartRenderer)",
                "Supported (Chromium / Static)",
                "Unsupported",
                "Multi-level drill-down, Tooltip",
                true,
                "Multi-level hierarchical ring visualization"
            ),

            // ── Statistical & Distribution ───────────────────────────────────
            new(
                VisualType.BoxPlot,
                "BOXPLOT",
                "Statistical",
                "ECharts (boxplot)",
                "Supported (SvgChartRenderer)",
                "Supported (Chromium / Static)",
                "Unsupported",
                "Tooltip (quartiles, outliers, min/max)",
                true,
                "Distribution box and whisker analysis"
            ),
            new(
                VisualType.HeatMap,
                "HEATMAP",
                "Matrix / Grid",
                "ECharts (heatmap)",
                "Supported (SvgChartRenderer)",
                "Supported (Chromium / Static)",
                "Unsupported",
                "Click, Cell hover, VisualMap filtering",
                true,
                "2D density / cross-tab color matrix"
            ),
            new(
                VisualType.Funnel,
                "FUNNEL",
                "Flow / Conversion",
                "ECharts (funnel)",
                "Supported (SvgChartRenderer)",
                "Supported (Chromium / Static)",
                "Unsupported",
                "Click, Stage select, Tooltip",
                true,
                "Conversion pipeline stage visualization"
            ),

            // ── Hierarchical & Network ────────────────────────────────────────
            new(
                VisualType.Treemap,
                "TREEMAP",
                "Hierarchical",
                "ECharts (treemap)",
                "Supported (SvgChartRenderer)",
                "Supported (Chromium / Static)",
                "Unsupported",
                "Drill down, Zoom, Breadcrumb navigation",
                true,
                "Proportional nested area partitioning"
            ),
            new(
                VisualType.Sankey,
                "SANKEY",
                "Flow / Network",
                "ECharts (sankey)",
                "Supported (SvgChartRenderer)",
                "Supported (Chromium / Static)",
                "Unsupported",
                "Node/Edge highlight, Hover tooltip",
                true,
                "Directed energy/cost/conversion flow mapping"
            ),
            new(
                VisualType.Network,
                "NETWORK",
                "Graph / Topology",
                "ECharts (graph with force layout)",
                "Supported (SvgChartRenderer)",
                "Supported (Chromium / Static)",
                "Unsupported",
                "Node dragging, Zoom/Pan, Click",
                true,
                "Node-link relational topology diagram"
            ),
            new(
                VisualType.Trellis,
                "TRELLIS",
                "Small Multiples",
                "ECharts (grid multiples)",
                "Supported (SvgChartRenderer)",
                "Supported (Chromium / Static)",
                "Unsupported",
                "Synchronized tooltip and crosshair",
                true,
                "Subdivided multi-panel charts partitioned by dimension"
            ),

            // ── Geographic ───────────────────────────────────────────────────
            new(
                VisualType.Map,
                "MAP",
                "Geographic",
                "ECharts (map / geojson)",
                "Supported (SvgChartRenderer)",
                "Supported (Chromium / Static)",
                "Unsupported",
                "Region click, Zoom/Pan, GeoJSON binding",
                true,
                "Choropleth and point mapping with bundled GeoJSON files"
            ),
            new(
                VisualType.Gantt,
                "GANTT",
                "Schedule / Timeline",
                "ECharts (custom timeline bar)",
                "Supported (SvgChartRenderer)",
                "Supported (Chromium / Static)",
                "Unsupported",
                "Hover, Milestone drill, Zoom",
                true,
                "Project task and schedule timeline visualization"
            ),

            // ── Tabular & Data Grid ───────────────────────────────────────────
            new(
                VisualType.Table,
                "TABLE",
                "Tabular",
                "Tabulator / HTML Table",
                "Supported (Markdown / CSV / HTML)",
                "Supported (HTML / Static Table)",
                "Supported (Spectre Table)",
                "Sort, Column filter, Pagination, Row click",
                false,
                "Client-side sorting, formatting, and pagination via Tabulator"
            ),
            new(
                VisualType.Matrix,
                "MATRIX",
                "Pivot / Matrix",
                "Tabulator / HTML Matrix",
                "Supported (Markdown / CSV / HTML)",
                "Supported (HTML / Static Table)",
                "Supported (Spectre Table)",
                "Row/Column expand, Sorting, Aggregations",
                false,
                "Cross-tabular pivot view with hierarchically grouped headers"
            ),

            // ── Indicators & Structural ───────────────────────────────────────
            new(
                VisualType.Gauge,
                "GAUGE",
                "Indicator / Radial",
                "ECharts (gauge)",
                "Supported (SvgChartRenderer)",
                "Supported (Chromium / Static)",
                "Supported (Gauge bar)",
                "Parameter binding",
                true,
                "Single-value progress and target threshold indicator"
            ),
            new(
                VisualType.Card,
                "CARD",
                "KPI / Metric",
                "Native DOM Card",
                "Supported (SVG / Markdown / HTML)",
                "Supported (HTML / Static Card)",
                "Supported (Spectre Panel)",
                "Click, Navigation link",
                false,
                "Summary headline number with trend and title"
            ),
            new(
                VisualType.Text,
                "TEXT",
                "Content / Narrative",
                "Native DOM (Markdown HTML)",
                "Supported (Markdown / HTML)",
                "Supported (HTML / Markdown)",
                "Supported (Plain text / Spectre)",
                "None",
                false,
                "Markdown and rich narrative text display"
            ),
            new(
                VisualType.Image,
                "IMAGE",
                "Media / Asset",
                "Native DOM (img)",
                "Supported (HTML img tag)",
                "Supported (HTML img tag)",
                "Unsupported",
                "Click link",
                false,
                "Static or dynamic URL image rendering"
            ),

            // ── Interactive Filters & Controls ────────────────────────────────
            new(
                VisualType.Slicer,
                "SLICER",
                "Filter / Control",
                "Native DOM (Buttons/Chips)",
                "Unsupported (Omitted in static export)",
                "Unsupported",
                "Unsupported",
                "Single/Multi selection, Parameter binding",
                false,
                "Interactive categorical filter control"
            ),
            new(
                VisualType.DatePicker,
                "DATEPICKER",
                "Filter / Control",
                "Native DOM (Flatpickr)",
                "Unsupported (Omitted in static export)",
                "Unsupported",
                "Unsupported",
                "Date picker, Parameter binding",
                false,
                "Interactive calendar date selector"
            ),
            new(
                VisualType.RelDatePicker,
                "RELDATEPICKER",
                "Filter / Control",
                "Native DOM (Relative Date Menu)",
                "Unsupported (Omitted in static export)",
                "Unsupported",
                "Unsupported",
                "Relative preset selection (Today, M-1, etc.)",
                false,
                "Relative rolling date filter selector"
            ),
            new(
                VisualType.Slider,
                "SLIDER",
                "Filter / Control",
                "Native DOM (Range input)",
                "Unsupported (Omitted in static export)",
                "Unsupported",
                "Unsupported",
                "Range drag, Parameter binding",
                false,
                "Numeric slider filter control"
            ),
            new(
                VisualType.MultiSelect,
                "MULTISELECT",
                "Filter / Control",
                "Native DOM (Dropdown multiselect)",
                "Unsupported (Omitted in static export)",
                "Unsupported",
                "Unsupported",
                "Checkbox selection, Parameter binding",
                false,
                "Multi-option categorical filter dropdown"
            ),
            new(
                VisualType.Search,
                "SEARCH",
                "Filter / Control",
                "Native DOM (Search input)",
                "Unsupported (Omitted in static export)",
                "Unsupported",
                "Unsupported",
                "Text input search, Parameter binding",
                false,
                "Full-text search box filter"
            ),
            new(
                VisualType.Checkbox,
                "CHECKBOX",
                "Filter / Control",
                "Native DOM (Checkbox)",
                "Unsupported (Omitted in static export)",
                "Unsupported",
                "Unsupported",
                "Toggle boolean, Parameter binding",
                false,
                "Boolean toggle filter control"
            ),
            new(
                VisualType.Textbox,
                "TEXTBOX",
                "Filter / Control",
                "Native DOM (Text input)",
                "Unsupported (Omitted in static export)",
                "Unsupported",
                "Unsupported",
                "Text entry, Parameter binding",
                false,
                "Arbitrary text input parameter control"
            ),
            new(
                VisualType.Numberbox,
                "NUMBERBOX",
                "Filter / Control",
                "Native DOM (Number input)",
                "Unsupported (Omitted in static export)",
                "Unsupported",
                "Unsupported",
                "Numeric entry, Parameter binding",
                false,
                "Numeric parameter input control"
            )
        };
    }
}
