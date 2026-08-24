using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using ETL_SQL.Reporting.Semantics;

namespace ETL_SQL.Reporting
{
    // ════════════════════════════════════════════════════════════════════════
    // ReportManifest — Phase 9B
    //
    // Serialisable POCOs that describe a fully-evaluated report.
    // Produced by ManifestBuilder; consumed by browser and export renderers,
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

        [JsonPropertyName("styles")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? Styles { get; set; }

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

        [JsonPropertyName("bookmarks")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<BookmarkManifest>? Bookmarks { get; set; }

        /// <summary>Global parameter values (active session state).</summary>
        [JsonPropertyName("parameters")]
        public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Author-time validated cascade graph in stable topological order.</summary>
        [JsonPropertyName("cascadeGraph")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CascadeGraphManifest? CascadeGraph { get; set; }

        /// <summary>The most recently committed atomic parameter transition.</summary>
        [JsonPropertyName("cascadeTransaction")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CascadeTransactionManifest? CascadeTransaction { get; set; }

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

        /// <summary>
        /// The resolved presentation state (active page + VISIBLE/COLLAPSED) the client should apply
        /// after a server-side atomic bookmark/saved-view application. Present only on the single
        /// manifest published by that operation; the client applies it as one deterministic swap.
        /// </summary>
        [JsonPropertyName("appliedState")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ETL_SQL.Core.Reporting.ResolvedReportState? AppliedState { get; set; }

        /// <summary>Warnings from reconciling a bookmark/saved-view envelope against the current report.</summary>
        [JsonPropertyName("stateWarnings")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? StateWarnings { get; set; }
    }

    public record LogEntryManifest(
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("color")] string? Color,
        [property: JsonPropertyName("timestamp")] DateTime Timestamp
    );

    /// <summary>A single visual with its data snapshot and native rendering payload.</summary>
    public class VisualManifest
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("visualType")]
        public string VisualType { get; set; } = string.Empty;

        [JsonPropertyName("fetch")]
        public string Fetch { get; set; } = "AUTO";

        [JsonPropertyName("titleIsMarkdown")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool TitleIsMarkdown { get; set; }

        [JsonPropertyName("subtitleIsMarkdown")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool SubtitleIsMarkdown { get; set; }

        [JsonPropertyName("isMarkdown")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsMarkdown { get; set; }

        /// <summary>When true, the visual was declared with VISIBLE = OFF and its data was not fetched.
        /// The runtime renders a placeholder until the user clicks Run.</summary>
        [JsonPropertyName("isHidden")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsHidden { get; set; }

        [JsonPropertyName("printLayout")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public PrintLayoutOverrideManifest? PrintLayout { get; set; }

        /// <summary>Legacy generic chart configuration slot; native charts leave this null.</summary>
        [JsonPropertyName("chartConfig")]
        public string? ChartConfig { get; set; }

        /// <summary>Renderer-neutral author intent for migrated named visuals.</summary>
        [JsonPropertyName("chartSpec")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ChartSpec? ChartSpec { get; set; }

        /// <summary>Typed chart values retained separately from formatted display rows.</summary>
        [JsonPropertyName("chartData")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ChartDataSet? ChartData { get; set; }

        /// <summary>Resolved semantic plan consumed by every migrated backend.</summary>
        [JsonPropertyName("plotPlan")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public PlotPlan? PlotPlan { get; set; }

        /// <summary>Native SVG payload used by browser and static delivery surfaces.</summary>
        [JsonPropertyName("nativeSvg")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? NativeSvg { get; set; }

        /// <summary>Validated engine-resolved custom map path; never serialized to clients.</summary>
        [JsonIgnore]
        public string? ResolvedMapFile { get; set; }

        /// <summary>
        /// Ordered non-graphical interpretation shared by terminals, assistive technology,
        /// and plain-text/static delivery paths.
        /// </summary>
        [JsonPropertyName("semanticFallback")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public SemanticFallback? SemanticFallback { get; set; }

        /// <summary>Resolved semantic plans and fallbacks for CARD/TABLE micro-charts.</summary>
        [JsonPropertyName("microCharts")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<MicroChartManifest>? MicroCharts { get; set; }

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

        [JsonPropertyName("rowDetail")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public RowDetailManifest? RowDetail { get; set; }

        /// <summary>Column headers for TABLE visuals (and raw data access).</summary>
        [JsonPropertyName("columns")]
        public List<string> Columns { get; set; } = new();

        /// <summary>Data rows — each row is a list of cell values (strings for portability).</summary>
        [JsonPropertyName("rows")]
        public List<List<string?>> Rows { get; set; } = new();

        /// <summary>Cascade configuration and retained LOCAL option vector.</summary>
        [JsonPropertyName("cascade")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CascadeVisualManifest? Cascade { get; set; }

        /// <summary>Original engine values used to build typed chart data; never emitted to clients.</summary>
        [JsonIgnore]
        internal List<Dictionary<string, object?>> RawRows { get; } = new();

        /// <summary>Deferred row payload for large visuals stored outside the manifest.</summary>
        [JsonPropertyName("rowsSource")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public VisualRowsSourceManifest? RowsSource { get; set; }

        /// <summary>Subset of rows that should be highlighted (Phase 9E Path B).</summary>
        [JsonPropertyName("highlightRows")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<List<string?>>? HighlightRows { get; set; }

        /// <summary>Captured row-detail binding keys before mapping projection.</summary>
        [JsonPropertyName("rowDetailKeys")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<Dictionary<string, object?>>? RowDetailKeys { get; set; }

        /// <summary>Row-level background colors applied via FORMATTING rules.</summary>
        [JsonPropertyName("rowStyles")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string?>? RowStyles { get; set; }

        /// <summary>Row-level font colors applied via FORMATTING rules (FONT_COLOR clause).</summary>
        [JsonPropertyName("rowFontStyles")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string?>? RowFontStyles { get; set; }

        /// <summary>Per-column format and alignment metadata (TABLE visual).</summary>
        [JsonPropertyName("columnMeta")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ColumnMetaManifest?>? ColumnMeta { get; set; }

        /// <summary>Flat options (title, legend, etc.).</summary>
        [JsonPropertyName("options")]
        public Dictionary<string, string> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Set when data fetch fails; causes the runtime to render an error card.</summary>
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        /// <summary>Click/change action bindings from the ACTIONS clause.</summary>
        [JsonPropertyName("actions")]
        public List<VisualActionManifest> Actions { get; set; } = new();

        /// <summary>Cross-visual selection/filter/highlight behavior from INTERACTIONS.</summary>
        [JsonPropertyName("interactions")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? Interactions { get; set; }

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

        /// <summary>Active DRILL_IN state, if any. Null when the chart is at root level.</summary>
        [JsonPropertyName("drillState")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public VisualDrillStateManifest? DrillState { get; set; }
    }

    public sealed class CascadeGraphManifest
    {
        [JsonPropertyName("order")]
        public List<string> Order { get; set; } = new();

        [JsonPropertyName("edges")]
        public List<CascadeEdgeManifest> Edges { get; set; } = new();
    }

    public sealed record CascadeEdgeManifest(
        [property: JsonPropertyName("parentParameter")] string ParentParameter,
        [property: JsonPropertyName("childParameter")] string ChildParameter);

    public sealed class CascadeVisualManifest
    {
        [JsonPropertyName("mode")] public string Mode { get; set; } = "LOCAL";
        [JsonPropertyName("producedParameter")] public string ProducedParameter { get; set; } = string.Empty;
        [JsonPropertyName("valueColumn")] public string? ValueColumn { get; set; }
        [JsonPropertyName("parents")] public List<CascadeParentManifest> Parents { get; set; } = new();
        [JsonPropertyName("invalid")] public string Invalid { get; set; } = "CLEAR";
        [JsonPropertyName("null")] public string Null { get; set; } = "ALL";
        [JsonPropertyName("allValue")] public string AllValue { get; set; } = "*";
        [JsonPropertyName("multiselect")] public string MultiSelect { get; set; } = "ANY";

        [JsonPropertyName("sourceColumns")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? SourceColumns { get; set; }

        [JsonPropertyName("sourceRows")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<List<string?>>? SourceRows { get; set; }
    }

    public sealed record CascadeParentManifest(
        [property: JsonPropertyName("parameter")] string Parameter,
        [property: JsonPropertyName("column")] string Column);

    public sealed class CascadeTransactionManifest
    {
        [JsonPropertyName("committedAt")] public DateTime CommittedAt { get; set; }
        [JsonPropertyName("changedParameters")] public List<string> ChangedParameters { get; set; } = new();
        [JsonPropertyName("refreshedVisuals")] public List<string> RefreshedVisuals { get; set; } = new();
    }

    public sealed class MicroChartManifest
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("kind")]
        public string Kind { get; set; } = string.Empty;

        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("rowIndex")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? RowIndex { get; set; }

        [JsonPropertyName("columnIndex")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? ColumnIndex { get; set; }

        [JsonPropertyName("sourceValue")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SourceValue { get; set; }

        /// <summary>Authoritative server-side plan. The browser receives its native SVG projection,
        /// avoiding one verbose contract payload per table cell.</summary>
        [JsonIgnore]
        public PlotPlan PlotPlan { get; set; } = null!;

        [JsonPropertyName("svg")]
        public string Svg { get; set; } = string.Empty;

        [JsonPropertyName("plainText")]
        public string PlainText { get; set; } = string.Empty;

        [JsonPropertyName("accessibleLabel")]
        public string AccessibleLabel { get; set; } = string.Empty;
    }

    /// <summary>Lazy row source metadata for large browser-rendered visuals.</summary>
    public class VisualRowsSourceManifest
    {
        [JsonPropertyName("format")]
        public string Format { get; set; } = "json";

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("arrowUrl")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ArrowUrl { get; set; }

        [JsonPropertyName("rowCount")]
        public int RowCount { get; set; }

        [JsonPropertyName("columns")]
        public List<string> Columns { get; set; } = new();
    }

    /// <summary>Sent to the browser for a visual that has an active DRILL_IN state.</summary>
    public class VisualDrillStateManifest
    {
        [JsonPropertyName("hierarchy")]
        public string[] Hierarchy { get; set; } = [];

        [JsonPropertyName("path")]
        public List<DrillPathSegment> Path { get; set; } = [];

        [JsonPropertyName("currentLevel")]
        public string CurrentLevel { get; set; } = "";

        [JsonPropertyName("canDrillUp")]
        public bool CanDrillUp { get; set; }
    }

    public class DrillPathSegment
    {
        [JsonPropertyName("column")] public string Column { get; set; } = "";
        [JsonPropertyName("value")] public string Value { get; set; } = "";
    }

    public class DataBarMetaManifest
    {
        [JsonPropertyName("color")] public string Color { get; set; } = string.Empty;
        [JsonPropertyName("min")] public double Min { get; set; }
        [JsonPropertyName("max")] public double Max { get; set; }
    }

    public class RowDetailManifest
    {
        [JsonPropertyName("targetName")]
        public string TargetName { get; set; } = string.Empty;

        [JsonPropertyName("limit")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Limit { get; set; }

        [JsonPropertyName("bindings")]
        public List<RowDetailBindingManifest> Bindings { get; set; } = new();
    }

    public class RowDetailBindingManifest
    {
        [JsonPropertyName("parentColumn")]
        public string ParentColumn { get; set; } = string.Empty;

        [JsonPropertyName("childParameter")]
        public string ChildParameter { get; set; } = string.Empty;
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

        [JsonPropertyName("keyColumns")]
        public string[]? KeyColumns { get; set; }

        // DRILL_IN fields
        [JsonPropertyName("hierarchy")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? Hierarchy { get; set; }

        // SET_PARAMETER fields
        [JsonPropertyName("parameterName")]
        public string? ParameterName { get; set; }

        [JsonPropertyName("valueExpression")]
        public string? ValueExpression { get; set; }

        [JsonPropertyName("valueSource")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ValueSource { get; set; }

        [JsonPropertyName("valueColumn")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ValueColumn { get; set; }

        [JsonPropertyName("literalValue")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LiteralValue { get; set; }

        // RUN_SCRIPT fields
        [JsonPropertyName("scriptPath")]
        public string? ScriptPath { get; set; }

        [JsonPropertyName("parameters")]
        public Dictionary<string, string>? Parameters { get; set; }

        [JsonPropertyName("targetReport")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? TargetReport { get; set; }

        [JsonPropertyName("targetPage")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? TargetPage { get; set; }

        [JsonPropertyName("parameterColumns")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? ParameterColumns { get; set; }

        [JsonPropertyName("literalParameters")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? LiteralParameters { get; set; }

        // SET_UI_STATE fields
        [JsonPropertyName("targets")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Targets { get; set; }

        [JsonPropertyName("key")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Key { get; set; }

        [JsonPropertyName("value")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Value { get; set; }

        // APPLY_BOOKMARK fields
        [JsonPropertyName("bookmarkName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? BookmarkName { get; set; }
    }

    /// <summary>
    /// Serialized author bookmark from CREATE BOOKMARK. The resolved presentation state lives in the
    /// shared <see cref="ETL_SQL.Core.Reporting.ResolvedReportState"/> envelope so author bookmarks and
    /// Portal saved views apply through one contract.
    /// </summary>
    public class BookmarkManifest
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Title { get; set; }

        [JsonPropertyName("isDefault")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsDefault { get; set; }

        /// <summary>The versioned, typed resolved-state envelope this bookmark applies.</summary>
        [JsonPropertyName("state")]
        public ETL_SQL.Core.Reporting.ResolvedReportState State { get; set; } = new();
    }

    /// <summary>A layout page with its slot→visual mapping.</summary>
    public class PageManifest
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("structure")]
        public string Structure { get; set; } = string.Empty;

        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "DASHBOARD";

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

        [JsonPropertyName("printLayout")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public PageLayoutDefinitionManifest? PrintLayout { get; set; }

        [JsonPropertyName("physicalPages")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<PhysicalPageModel>? PhysicalPages { get; set; }
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
        [JsonPropertyName("color")] public string Color { get; set; } = string.Empty;
        [JsonPropertyName("fontColor")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FontColor { get; set; }
    }

    public class ColumnMetaManifest
    {
        [JsonPropertyName("format")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Format { get; set; }

        [JsonPropertyName("align")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Align { get; set; }

        [JsonPropertyName("hidden")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Hidden { get; set; }

        [JsonPropertyName("dataBar")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool DataBar { get; set; }

        [JsonPropertyName("dataBarColor")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? DataBarColor { get; set; }

        [JsonPropertyName("dataBarMin")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? DataBarMin { get; set; }

        [JsonPropertyName("dataBarMax")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? DataBarMax { get; set; }

        [JsonPropertyName("colorScaleFrom")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ColorScaleFrom { get; set; }

        [JsonPropertyName("colorScaleTo")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ColorScaleTo { get; set; }

        [JsonPropertyName("colorScaleMin")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? ColorScaleMin { get; set; }

        [JsonPropertyName("colorScaleMax")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? ColorScaleMax { get; set; }

        [JsonPropertyName("cellRenderer")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CellRenderer { get; set; }  // "image" | "hyperlink" | "sparkline"

        [JsonPropertyName("imageWidth")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? ImageWidth { get; set; }

        [JsonPropertyName("hyperlinkLabel")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? HyperlinkLabel { get; set; }

        [JsonPropertyName("sparklineType")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SparklineType { get; set; }  // "line" | "bar" | "area"
    }

    public class OverlayManifest
    {
        [JsonPropertyName("overlayType")] public string OverlayType { get; set; } = string.Empty;
        [JsonPropertyName("parameter")] public double? Parameter { get; set; }
        [JsonPropertyName("lineStyle")] public string LineStyle { get; set; } = "dashed";
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
        [JsonPropertyName("column")] public string Column { get; set; } = string.Empty;
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
        [JsonPropertyName("column")] public string Column { get; set; } = string.Empty;
        [JsonPropertyName("aggregate")] public string Aggregate { get; set; } = string.Empty;
        [JsonPropertyName("value")] public string Value { get; set; } = string.Empty;
        [JsonPropertyName("alias")] public string? Alias { get; set; }
    }

    public class ContainerManifest
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
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

        [JsonPropertyName("isHidden")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsHidden { get; set; }
    }


    public class NavigationManifest
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("navType")] public string NavType { get; set; } = string.Empty;
        [JsonPropertyName("orientation")] public string Orientation { get; set; } = "horizontal";
        [JsonPropertyName("defaultPage")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? DefaultPage { get; set; }
        [JsonPropertyName("pages")] public List<string> Pages { get; set; } = new();
    }

    /// <summary>
    /// Serializable form of a TooltipDefinition.
    /// type = "text" | "container" | "inline"
    /// </summary>
    /// <remarks>
    /// <see cref="Mode"/> is the field consumers switch on. It carries the accepted
    /// detail-surface contract across the wire so the browser runtime, the static
    /// renderers, and the screen-reader projection all pick the same behaviour without
    /// re-deriving it from <see cref="Type"/>:
    /// <list type="bullet">
    ///   <item><description><c>tooltip</c> — transient, non-interactive text. Rendered as
    ///   <c>role="tooltip"</c> with <c>aria-describedby</c>; never focusable.</description></item>
    ///   <item><description><c>popover</c> — persistent, focusable detail carrying formatted
    ///   content or visuals. Rendered as a labelled dialog; pinned on activation.</description></item>
    /// </list>
    /// Older manifests predate <see cref="Mode"/>; consumers must fall back to deriving it
    /// from <see cref="Type"/> so previously published reports keep working.
    /// </remarks>
    public class TooltipManifest
    {
        /// <summary>Transient text tooltip mode value for <see cref="Mode"/>.</summary>
        public const string TooltipMode = "tooltip";

        /// <summary>Persistent focusable detail popover mode value for <see cref="Mode"/>.</summary>
        public const string PopoverMode = "popover";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "text";

        /// <summary>
        /// The detail surface this tooltip projects to: <c>tooltip</c> or <c>popover</c>.
        /// See the remarks on <see cref="TooltipManifest"/>.
        /// </summary>
        /// <remarks>
        /// Deliberately nullable with no default. Defaulting it to <see cref="TooltipMode"/>
        /// would make a manifest published before this field existed deserialize as an
        /// explicit transient tooltip, so the <see cref="Type"/> fallback would never run and
        /// an older container popover would be silently downgraded to text. Absence has to
        /// stay distinguishable from an explicit choice.
        /// </remarks>
        [JsonPropertyName("mode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Mode { get; set; }

        /// <summary>
        /// Visual names this surface renders, resolved statically through any referenced
        /// container graph. Lets static renderers and the screen-reader projection describe
        /// the detail without expanding it.
        /// </summary>
        [JsonPropertyName("resolvedVisuals")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? ResolvedVisuals { get; set; }

        /// <summary>
        /// The one-line description a surface that cannot be hovered shows instead of the
        /// detail itself. Computed once by <see cref="DetailSurfaceProjection.Describe"/> so
        /// the browser's print output and the static exporters cannot drift into two
        /// different wordings of the same fallback.
        /// </summary>
        [JsonPropertyName("staticSummary")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? StaticSummary { get; set; }

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
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("buttonType")] public string ButtonType { get; set; } = string.Empty;
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("tooltip")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TooltipManifest? Tooltip { get; set; }
        [JsonPropertyName("options")] public Dictionary<string, string> Options { get; set; } = new();
        [JsonPropertyName("actions")] public List<VisualActionManifest> Actions { get; set; } = new();
        [JsonPropertyName("styles")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? Styles { get; set; }
    }

    /// <summary>A custom report theme registered via CREATE THEME.</summary>
    public class ThemeManifest
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Raw native theme JSON object.</summary>
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
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
        [JsonPropertyName("defaultValue")] public string? DefaultValue { get; set; }
        [JsonPropertyName("isRequired")] public bool IsRequired { get; set; }
    }

    public class PageLayoutDefinitionManifest
    {
        [JsonPropertyName("pageSize")] public string? PageSize { get; set; }
        [JsonPropertyName("customWidth")] public decimal? CustomWidth { get; set; }
        [JsonPropertyName("customHeight")] public decimal? CustomHeight { get; set; }
        [JsonPropertyName("orientation")] public string? Orientation { get; set; }
        [JsonPropertyName("units")] public string? Units { get; set; }
        [JsonPropertyName("marginTop")] public decimal? MarginTop { get; set; }
        [JsonPropertyName("marginRight")] public decimal? MarginRight { get; set; }
        [JsonPropertyName("marginBottom")] public decimal? MarginBottom { get; set; }
        [JsonPropertyName("marginLeft")] public decimal? MarginLeft { get; set; }
        [JsonPropertyName("overflow")] public string? Overflow { get; set; }
    }

    public class PrintLayoutOverrideManifest
    {
        [JsonPropertyName("pageBreakBefore")] public bool? PageBreakBefore { get; set; }
        [JsonPropertyName("pageBreakAfter")] public bool? PageBreakAfter { get; set; }
        [JsonPropertyName("keepTogether")] public bool? KeepTogether { get; set; }
        [JsonPropertyName("excludeFromPrint")] public bool? ExcludeFromPrint { get; set; }
    }
}
