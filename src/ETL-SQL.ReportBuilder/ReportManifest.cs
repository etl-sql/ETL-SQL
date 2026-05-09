using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ETL_SQL.ReportBuilder
{
    // ════════════════════════════════════════════════════════════════════════
    // ReportManifest — Phase 9B
    //
    // Serialisable POCOs that describe a fully-evaluated report.
    // Produced by ManifestBuilder; consumed by EChartsRenderer,
    // MarkdownRenderer, SvgChartRenderer, SnapshotStore, and the VS Code preview WebviewPanel.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>The root manifest for a single .rptsql report.</summary>
    public class ReportManifest
    {
        /// <summary>Script file path that produced this manifest.</summary>
        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;

        /// <summary>UTC time the manifest was built.</summary>
        [JsonPropertyName("builtAt")]
        public DateTime BuiltAt { get; set; } = DateTime.UtcNow;

        /// <summary>True if this build was triggered by a cross-visual interaction (selection).</summary>
        [JsonPropertyName("isInteraction")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsInteraction { get; set; }

        /// <summary>Optional report title (from SET REPORT TITLE = '...').</summary>
        [JsonPropertyName("title")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Title { get; set; }
        [JsonPropertyName("titleIsMarkdown")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool TitleIsMarkdown { get; set; }

        /// <summary>Optional report description (from SET REPORT DESCRIPTION = '...').</summary>
        [JsonPropertyName("description")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Description { get; set; }
        
        [JsonPropertyName("css")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Css { get; set; }

        [JsonPropertyName("js")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Js { get; set; }

        [JsonPropertyName("htmlHead")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? HtmlHead { get; set; }

        [JsonPropertyName("htmlBody")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? HtmlBody { get; set; }

        [JsonPropertyName("htmlFooter")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? HtmlFooter { get; set; }

        [JsonPropertyName("favicon")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Favicon { get; set; }

        [JsonPropertyName("logo")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Logo { get; set; }

        [JsonPropertyName("background")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Background { get; set; }

        [JsonPropertyName("theme")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Theme { get; set; }

        [JsonPropertyName("navigation")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Navigation { get; set; }

        /// <summary>Named visuals in script-definition order.</summary>
        [JsonPropertyName("visuals")]
        public List<VisualManifest> Visuals { get; set; } = new();

        /// <summary>Named pages in script-definition order.</summary>
        [JsonPropertyName("pages")]
        public List<PageManifest> Pages { get; set; } = new();

        /// <summary>Named datasets (materialized #temp tables).</summary>
        [JsonPropertyName("datasets")]
        public List<DatasetManifest> Datasets { get; set; } = new();

        [JsonPropertyName("containers")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ContainerManifest>? Containers { get; set; }

        [JsonPropertyName("navigations")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<NavigationManifest>? Navigations { get; set; }

        [JsonPropertyName("buttons")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ButtonManifest>? Buttons { get; set; }

        /// <summary>Global parameter values (active session state).</summary>
        [JsonPropertyName("parameters")]
        public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Metadata for report parameters (Phase 3).</summary>
        [JsonPropertyName("parameterMetadata")]
        public Dictionary<string, ParameterMetadataManifest> ParameterMetadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("customThemes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ThemeManifest>? CustomThemes { get; set; }
        
        [JsonPropertyName("telemetry")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TelemetryManifest? Telemetry { get; set; }

        [JsonPropertyName("messages")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<LogEntryManifest>? Messages { get; set; }

        [JsonPropertyName("executionTree")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? ExecutionTree { get; set; }

        [JsonPropertyName("error")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Error { get; set; }
    }

    public record LogEntryManifest(
        [property: JsonPropertyName("message")]   string Message,
        [property: JsonPropertyName("color")]     string? Color,
        [property: JsonPropertyName("timestamp")] DateTime Timestamp
    );

    /// <summary>A single visual with its data snapshot and ECharts config.</summary>
    public class VisualManifest
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("visualType")]
        public string VisualType { get; set; } = string.Empty;

        [JsonPropertyName("titleIsMarkdown")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool TitleIsMarkdown { get; set; }

        [JsonPropertyName("subtitleIsMarkdown")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool SubtitleIsMarkdown { get; set; }
        
        [JsonPropertyName("isMarkdown")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsMarkdown { get; set; }

        /// <summary>Resolved ECharts option JSON object (as a pre-serialised string).</summary>
        [JsonPropertyName("chartConfig")]
        public string? ChartConfig { get; set; }

        [JsonPropertyName("defaultValue")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? DefaultValue { get; set; }
        
        [JsonPropertyName("labelPosition")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LabelPosition { get; set; }

        [JsonPropertyName("min")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Min { get; set; }

        [JsonPropertyName("max")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Max { get; set; }

        [JsonPropertyName("decimals")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Decimals { get; set; }
        
        [JsonPropertyName("placeholder")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Placeholder { get; set; }

        [JsonPropertyName("tooltip")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TooltipManifest? Tooltip { get; set; }

        /// <summary>Column headers for TABLE visuals (and raw data access).</summary>
        [JsonPropertyName("columns")]
        public List<string> Columns { get; set; } = new();

        /// <summary>Data rows — each row is a list of cell values (strings for portability).</summary>
        [JsonPropertyName("rows")]
        public List<List<string?>> Rows { get; set; } = new();

        /// <summary>Subset of rows that should be highlighted (Phase 9E Path B).</summary>
        [JsonPropertyName("highlightRows")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<List<string?>>? HighlightRows { get; set; }

        /// <summary>Row-level background colors applied via FORMATTING rules.</summary>
        [JsonPropertyName("rowStyles")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string?>? RowStyles { get; set; }

        /// <summary>Flat options (title, legend, etc.).</summary>
        [JsonPropertyName("options")]
        public Dictionary<string, string> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Set when data fetch fails; causes the runtime to render an error card.</summary>
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        /// <summary>Click/change action bindings from the ACTIONS clause.</summary>
        [JsonPropertyName("actions")]
        public List<VisualActionManifest> Actions { get; set; } = new();

        [JsonPropertyName("styles")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? Styles { get; set; }

        [JsonPropertyName("seriesDefs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<SeriesDefManifest>? SeriesDefs { get; set; }

        [JsonPropertyName("formattingRules")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<FormattingRuleManifest>? FormattingRules { get; set; }

        [JsonPropertyName("overlays")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<OverlayManifest>? Overlays { get; set; }

        [JsonPropertyName("summaryData")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TableSummaryData? SummaryData { get; set; }

        /// <summary>Grid visibility for TABLE visuals (ALL, NONE, HEADER, etc.).</summary>
        [JsonPropertyName("gridStyle")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? GridStyle { get; set; }

        /// <summary>Chart data labels configuration.</summary>
        [JsonPropertyName("dataLabels")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DataLabelsManifest? DataLabels { get; set; }
    }

    public class DataLabelsManifest
    {
        [JsonPropertyName("show")] public bool Show { get; set; }
        [JsonPropertyName("position")] public string? Position { get; set; }
        [JsonPropertyName("color")] public string? Color { get; set; }
        [JsonPropertyName("fontSize")] public int? FontSize { get; set; }
        [JsonPropertyName("fontWeight")] public string? FontWeight { get; set; }
        [JsonPropertyName("fontFamily")] public string? FontFamily { get; set; }
        [JsonPropertyName("format")] public string? Format { get; set; }
    }

    /// <summary>A serialisable representation of one ACTIONS entry (DRILL_DOWN or SET_PARAMETER).</summary>
    public class VisualActionManifest
    {
        /// <summary>"DRILL_DOWN" or "SET_PARAMETER".</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        /// <summary>"ON_CLICK" or "ON_CHANGE".</summary>
        [JsonPropertyName("trigger")]
        public string Trigger { get; set; } = string.Empty;

        // DRILL_DOWN fields
        [JsonPropertyName("targetVisual")]
        public string? TargetVisual { get; set; }

        [JsonPropertyName("keyColumn")]
        public string? KeyColumn { get; set; }

        // SET_PARAMETER fields
        [JsonPropertyName("parameterName")]
        public string? ParameterName { get; set; }

        [JsonPropertyName("valueExpression")]
        public string? ValueExpression { get; set; }

        // RUN_SCRIPT fields
        [JsonPropertyName("scriptPath")]
        public string? ScriptPath { get; set; }

        [JsonPropertyName("parameters")]
        public Dictionary<string, string>? Parameters { get; set; }
    }

    /// <summary>A layout page with its slot→visual mapping.</summary>
    public class PageManifest
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("structure")]
        public string Structure { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Title { get; set; }

        [JsonPropertyName("titleIsMarkdown")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool TitleIsMarkdown { get; set; }

        [JsonPropertyName("subtitle")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Subtitle { get; set; }

        [JsonPropertyName("subtitleIsMarkdown")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool SubtitleIsMarkdown { get; set; }
        
        [JsonPropertyName("tooltip")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TooltipManifest? Tooltip { get; set; }

        [JsonPropertyName("isHidden")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsHidden { get; set; }

        /// <summary>Auto-refresh interval in seconds (0 = disabled).</summary>
        [JsonPropertyName("refreshIntervalSeconds")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int RefreshIntervalSeconds { get; set; }

        /// <summary>Slot letter → visual name.</summary>
        [JsonPropertyName("slotMap")]
        public Dictionary<string, string> SlotMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("styles")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? Styles { get; set; }
    }

    /// <summary>Metadata for a CREATE DATASET entry.</summary>
    public class DatasetManifest
    {
        [JsonPropertyName("tempTableName")]
        public string TempTableName { get; set; } = string.Empty;

        [JsonPropertyName("refreshInterval")]
        public string? RefreshInterval { get; set; }

        [JsonPropertyName("ttl")]
        public string? Ttl { get; set; }

        [JsonPropertyName("lastRefresh")]
        public DateTime? LastRefresh { get; set; }

        [JsonPropertyName("rowCount")]
        public long RowCount { get; set; }
    }

    public class FormattingRuleManifest
    {
        [JsonPropertyName("condition")] public string Condition { get; set; } = string.Empty;
        [JsonPropertyName("color")]     public string Color     { get; set; } = string.Empty;
    }

    public class OverlayManifest
    {
        [JsonPropertyName("overlayType")] public string OverlayType { get; set; } = string.Empty;
        [JsonPropertyName("parameter")]   public double? Parameter  { get; set; }
        [JsonPropertyName("lineStyle")]   public string LineStyle   { get; set; } = "dashed";
        [JsonPropertyName("color")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Color { get; set; }
        [JsonPropertyName("label")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Label { get; set; }
    }

    public class SeriesDefManifest
    {
        [JsonPropertyName("seriesType")] public string SeriesType { get; set; } = string.Empty;
        [JsonPropertyName("column")]     public string Column { get; set; } = string.Empty;
    }

    public class TableSummaryData
    {
        [JsonPropertyName("aggregates")]
        public List<SummaryItemData> Aggregates { get; set; } = new();

        [JsonPropertyName("grandTotals")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? GrandTotals { get; set; }
    }

    public class SummaryItemData
    {
        [JsonPropertyName("column")]    public string Column { get; set; } = string.Empty;
        [JsonPropertyName("aggregate")] public string Aggregate { get; set; } = string.Empty;
        [JsonPropertyName("value")]     public string Value { get; set; } = string.Empty;
        [JsonPropertyName("alias")]     public string? Alias { get; set; }
    }

    public class ContainerManifest
    {
        [JsonPropertyName("name")]          public string Name { get; set; } = string.Empty;
        [JsonPropertyName("containerType")] public string ContainerType { get; set; } = string.Empty;
        
        [JsonPropertyName("title")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Title { get; set; }

        [JsonPropertyName("titleIsMarkdown")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool TitleIsMarkdown { get; set; }

        [JsonPropertyName("subtitle")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Subtitle { get; set; }

        [JsonPropertyName("subtitleIsMarkdown")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool SubtitleIsMarkdown { get; set; }

        [JsonPropertyName("structure")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Structure { get; set; }

        [JsonPropertyName("slotMap")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? SlotMap { get; set; }

        [JsonPropertyName("styles")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? Styles { get; set; }

        [JsonPropertyName("tooltip")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TooltipManifest? Tooltip { get; set; }
        
        [JsonPropertyName("isCollapsible")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsCollapsible { get; set; }

        [JsonPropertyName("icon")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Icon { get; set; }

        [JsonPropertyName("isPinnable")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsPinnable { get; set; } = true;
    }


    public class NavigationManifest
    {
        [JsonPropertyName("name")]        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("navType")]     public string NavType { get; set; } = string.Empty;
        [JsonPropertyName("orientation")] public string Orientation { get; set; } = "horizontal";
        [JsonPropertyName("defaultPage")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? DefaultPage { get; set; }
        [JsonPropertyName("pages")]       public List<string> Pages { get; set; } = new();
    }

    /// <summary>
    /// Serializable form of a TooltipDefinition.
    /// type = "text" | "container" | "inline"
    /// </summary>
    public class TooltipManifest
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "text";

        [JsonPropertyName("text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Text { get; set; }

        [JsonPropertyName("containerRef")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ContainerRef { get; set; }

        [JsonPropertyName("markdown")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Markdown { get; set; }

        [JsonPropertyName("visuals")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Visuals { get; set; }

        [JsonPropertyName("isMarkdown")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsMarkdown { get; set; }
    }

    public class ButtonManifest
    {
        [JsonPropertyName("name")]       public string Name { get; set; } = string.Empty;
        [JsonPropertyName("buttonType")] public string ButtonType { get; set; } = string.Empty;
        [JsonPropertyName("title")]      public string? Title { get; set; }
        [JsonPropertyName("tooltip")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TooltipManifest? Tooltip { get; set; }
        [JsonPropertyName("options")]    public Dictionary<string, string> Options { get; set; } = new();
        [JsonPropertyName("actions")]    public List<VisualActionManifest> Actions { get; set; } = new();
        [JsonPropertyName("styles")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? Styles { get; set; }
    }

    /// <summary>A custom ECharts theme registered via CREATE THEME.</summary>
    public class ThemeManifest
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Raw ECharts theme JSON object (arbitrary structure).</summary>
        [JsonPropertyName("config")]
        public System.Text.Json.JsonElement Config { get; set; }
    }

    public class TelemetryManifest
    {
        [JsonPropertyName("rowsProcessed")] public long RowsProcessed { get; set; }
        [JsonPropertyName("totalSpilledBytes")] public long TotalSpilledBytes { get; set; }
        [JsonPropertyName("subqueryCacheHits")] public long SubqueryCacheHits { get; set; }
        [JsonPropertyName("subqueryCacheMisses")] public long SubqueryCacheMisses { get; set; }
        [JsonPropertyName("subquerySpillCount")] public int SubquerySpillCount { get; set; }
        [JsonPropertyName("subquerySpilledBytes")] public long SubquerySpilledBytes { get; set; }
        [JsonPropertyName("executionTimeMs")] public long ExecutionTimeMs { get; set; }
    }

    public class ParameterMetadataManifest
    {
        [JsonPropertyName("name")]         public string Name         { get; set; } = string.Empty;
        [JsonPropertyName("type")]         public string Type         { get; set; } = string.Empty;
        [JsonPropertyName("defaultValue")] public string? DefaultValue { get; set; }
        [JsonPropertyName("isRequired")]   public bool IsRequired     { get; set; }
    }
}
