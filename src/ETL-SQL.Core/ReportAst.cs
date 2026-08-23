using System.Collections.Generic;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Formatting;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Core;
// ════════════════════════════════════════════════════════════════════════════
// Report-SQL AST — Phase 9A
//
// AstNode and Statement are records, so all derived types must be records too.
// ════════════════════════════════════════════════════════════════════════════

// ── Tooltip ───────────────────────────────────────────────────────────────

/// <summary>
/// A tooltip that can be plain text, a reference to an existing named container,
/// or an inline anonymous container (optional markdown + visual list).
/// </summary>
public record TooltipDefinition
{
    public Expression? PlainText { get; init; }
    public string? ContainerRef { get; init; }
    public string? InlineMarkdown { get; init; }
    public List<string>? InlineVisuals { get; init; }

    public static TooltipDefinition Text(Expression text) =>
        new() { PlainText = text };

    public static TooltipDefinition Container(string containerName) =>
        new() { ContainerRef = containerName };

    public static TooltipDefinition Inline(string? markdown, List<string> visuals) =>
        new() { InlineMarkdown = markdown, InlineVisuals = visuals };
}

// ── Enumerations ──────────────────────────────────────────────────────────

public enum ReportObjectType
{
    Visual,
    Page,
    Dataset,
    Container,
    Navigation,
    Style,
    Button,
    Template,
    Theme,
    Bookmark
}

public enum OverlayType
{
    Goal, Average, MovingAvg, Linear, Exponential, Logarithmic, Polynomial, Power
}

public enum OverlayLineStyle { Solid, Dashed, Dotted }

public record VisualOverlay : AstNode
{
    public required OverlayType OverlayType { get; init; }
    public double? Parameter { get; init; }  // GOAL value, MOVING_AVG window, POLYNOMIAL degree
    public OverlayLineStyle LineStyle { get; init; } = OverlayLineStyle.Dashed;
    public string? Color { get; init; }
    public string? Label { get; init; }
}

public enum VisualType
{
    Bar, Line, Scatter, Pie, Table, Card, Slicer,
    Donut, HorizontalBar, BoxPlot, Treemap, HeatMap, Text, Combo,
    DatePicker, RelDatePicker, Slider, MultiSelect, Search,
    Gauge, Funnel, Waterfall, Image,
    Bubble, Radar, Candlestick,
    Map, Gantt,
    Checkbox, Textbox, Numberbox,
    Sankey, Sunburst, Network, Trellis, Matrix, Custom
}

public enum PageMode
{
    Dashboard,
    Paginated
}

public enum VisualFetchMode
{
    Auto,
    OnLoad,
    OnRun
}

public enum CascadeMode { Local, Live }
public enum CascadeInvalidSelectionPolicy { Clear, First, Error }
public enum CascadeNullSelectionPolicy { All, Match }
public enum CascadeMultiSelectPolicy { Any, All }

public record CascadeParentBinding(string ParameterName, string ColumnName) : AstNode;

public record CascadeDefinition : AstNode
{
    public required CascadeMode Mode { get; init; }
    public List<CascadeParentBinding> Parents { get; init; } = new();
    public CascadeInvalidSelectionPolicy InvalidSelection { get; init; } = CascadeInvalidSelectionPolicy.Clear;
    public CascadeNullSelectionPolicy NullSelection { get; init; } = CascadeNullSelectionPolicy.All;
    public string AllValue { get; init; } = "*";
    public CascadeMultiSelectPolicy MultiSelect { get; init; } = CascadeMultiSelectPolicy.Any;
    public override string ToSql() => AstSerializer.Format(this);
}

public enum DatasetEncryptionMode
{
    None,
    MachineBound,   // ENCRYPT = MACHINE  (DPAPI on Windows; machine-unique key on Linux/Mac)
    Password,       // ENCRYPT = PASSWORD  + PASSWORD = '...'
    KeyFile         // ENCRYPT = KEYFILE   + KEYFILE  = '...'
}

// ── Sub-nodes (all must be records since AstNode is a record) ────────────

/// <summary>
/// Source expression for a visual: either an inline query (SELECT or UNION ALL) or a &dataset reference.
/// Exactly one of InlineSelect / TempTableName is set; the other is null.
/// </summary>
public record VisualSourceExpression : AstNode
{
    public Statement? InlineSelect { get; init; }
    public string? TempTableName { get; init; }
    public bool IsInlineSelect => InlineSelect != null;
    public override string ToSql() => AstSerializer.Format(this);
}

public record VisualMapping : AstNode
{
    public required string Role { get; init; }
    public required new string Column { get; init; }
    public string? Format { get; init; }
    public string? Align { get; init; }
    public string? DisplayName { get; init; }
    public bool DataBar { get; init; }
    public string? DataBarColor { get; init; }
    public string? ColorScaleFrom { get; init; }
    public string? ColorScaleTo { get; init; }
    // Phase 3A: cell renderers
    public string? CellRenderer { get; init; }  // "image" | "hyperlink"
    public int? ImageWidth { get; init; }
    public string? HyperlinkLabel { get; init; }
    // Phase 3B: sparkline virtual column
    public List<string>? SparklineColumns { get; init; }
    public string? SparklineType { get; init; }  // "line" | "bar" | "area"
    // Phase 4: CARD sparkline source and TABLE progress micro-chart intent.
    public string? SparklineSource { get; init; }
    public string? SparklineXColumn { get; init; }
    public string? SparklineYColumn { get; init; }
    public bool ProgressBar { get; init; }
    public decimal? ProgressMinimum { get; init; }
    public decimal? ProgressMaximum { get; init; }
    public string? ProgressColor { get; init; }
    public bool Hidden { get; init; }
    public override string ToSql() => AstSerializer.Format(this);
}

public record VisualOption : AstNode
{
    public required string Key { get; init; }
    public required string Value { get; init; }
    public override string ToSql() => AstSerializer.Format(this);
}

public record VisualInteraction : AstNode
{
    public required string Key { get; init; }
    public required string Value { get; init; }
    public override string ToSql() => AstSerializer.Format(this);
}

public record AxisOptions : AstNode
{
    public required string Axis { get; init; }  // "X" or "Y"
    public List<VisualOption> Options { get; init; } = new();
    public override string ToSql() => AstSerializer.Format(this);
}

public abstract record VisualAction : AstNode
{
    public required string Trigger { get; init; }
    public override string ToSql() => "UNKNOWN ACTION";
}

public record SetParameterAction : VisualAction
{
    public required string ParameterName { get; init; }
    public required string ValueExpression { get; init; }
    public override string ToSql() => AstSerializer.Format(this);
}

public record DrillDownAction : VisualAction
{
    public required string TargetVisual { get; init; }
    public required string[] KeyColumns { get; init; }
    public override string ToSql() => AstSerializer.Format(this);
}

public record DrillInAction : VisualAction
{
    public required string[] Hierarchy { get; init; }
    public override string ToSql() => AstSerializer.Format(this);
}

public record RunScriptAction : VisualAction
{
    public required string ScriptPath { get; init; }
    public Dictionary<string, string> Parameters { get; init; } = new();
    public override string ToSql() => AstSerializer.Format(this);
}

public record ClearFiltersAction : VisualAction
{
    public override string ToSql() => "CLEAR_FILTERS";
}

public record ApplyParametersAction : VisualAction
{
    public override string ToSql() => "APPLY_PARAMETERS";
}

public record ReportCommandAction : VisualAction
{
    public required string Command { get; init; }
    public override string ToSql() => Command;
}

public record DrillReportAction : VisualAction
{
    public string TargetReport { get; init; } = string.Empty;
    public Dictionary<string, string> Parameters { get; init; } = new();
    public override string ToSql() => AstSerializer.Format(this);
}

public record NavigatePageAction : VisualAction
{
    public required string TargetPage { get; init; }
    public override string ToSql() => AstSerializer.Format(this);
}

public record RefreshVisualsAction : VisualAction
{
    public required List<string> Targets { get; init; }
    public override string ToSql() => AstSerializer.Format(this);
}

public record SetUiStateAction : VisualAction
{
    public required List<string> Targets { get; init; }
    public required string Key { get; init; }
    public required string Value { get; init; }
    public override string ToSql() => AstSerializer.Format(this);
}

public record ApplyBookmarkAction : VisualAction
{
    public required string BookmarkName { get; init; }
    public override string ToSql() => AstSerializer.Format(this);
}

// ── Bookmark ─────────────────────────────────────────────────────────────

/// <summary>A single presentation-state property a bookmark may set. Constrained to the v1 contract.</summary>
public enum BookmarkStateProperty
{
    Visible,
    Collapsed
}

/// <summary>
/// A typed parameter assignment inside a bookmark's PARAMETERS clause. <see cref="Value"/> retains the
/// parsed expression (typically a typed literal or a variable reference), so numbers, booleans, dates,
/// and null are never flattened to quoted strings during formatting or serialization.
/// </summary>
public record BookmarkParameterAssignment(string ParameterName, Expression Value) : AstNode;

/// <summary>A single STATE entry: <c>ObjectName.PROPERTY = ON|OFF</c>.</summary>
public record BookmarkStateEntry(string ObjectName, BookmarkStateProperty Property, bool On) : AstNode
{
    /// <summary>The dotted key form (<c>ObjectName.VISIBLE</c>) for diagnostics and legacy comparisons.</summary>
    public string ObjectKey => $"{ObjectName}.{Property.ToString().ToUpperInvariant()}";
}

/// <summary>CREATE BOOKMARK Name AS (TITLE = '...', PARAMETERS (...), PAGE = Page, STATE (...), DEFAULT = ON)</summary>
public record CreateBookmarkStatement : Statement
{
    public required string Name { get; init; }
    public Expression? Title { get; init; }
    public string? PageName { get; init; }
    public bool IsDefault { get; init; }
    public IReadOnlyList<BookmarkParameterAssignment> Parameters { get; init; } = [];
    public IReadOnlyList<BookmarkStateEntry> StateEntries { get; init; } = [];
    public override string ToSql() => AstSerializer.Format(this);
}

public record TableSummaryItem(string Aggregate, string Column, string? Alias);

public record TableSummaryOptions : AstNode
{
    public bool GrandTotalRow { get; init; }
    public bool GrandTotalColumn { get; init; }
    public bool SummarizeRow { get; init; }
    public bool SummarizeColumn { get; init; }
    public List<string>? SpecificColumns { get; init; }
}

public record FormattingRule : AstNode
{
    public required Expression Condition { get; init; }
    public required string Color { get; init; }
    public string? FontColor { get; init; }
}

public record TypedSeries : AstNode
{
    public required string SeriesType { get; init; }  // "bar" or "line"
    public new required string Column { get; init; }
}

public record RowDetailBinding(string ParentColumn, string ChildParameter) : AstNode
{
    public override string ToSql() => $"@{ChildParameter} = {ParentColumn}";
}

public record RowDetailDefinition : AstNode
{
    public required string TargetName { get; init; }
    public List<RowDetailBinding> Bindings { get; init; } = new();
    public int? Limit { get; init; }
    public override string ToSql() => AstSerializer.Format(this);
}

/// <summary>CREATE STYLE <name> AS (key = value, ...)</summary>
public record CreateStyleStatement : Statement
{
    public required string Name { get; init; }
    public Dictionary<string, string> Styles { get; init; } = new();
    public string? StyleName { get; init; }
    public ObjectCreationMode Mode { get; init; } = ObjectCreationMode.Create;
    public override string ToSql() => AstSerializer.Format(this);
}

public record CreateButtonStatement : Statement
{
    public required string Name { get; init; }
    public required string ButtonType { get; init; } // BACK, REFRESH, HELP, etc.
    public Expression? Title { get; init; }
    public TooltipDefinition? Tooltip { get; init; }
    public List<VisualOption> Options { get; init; } = new();
    public List<VisualAction> Actions { get; init; } = new();
    public Dictionary<string, string> Styles { get; init; } = new();
    public string? StyleName { get; init; }
    public ObjectCreationMode Mode { get; init; } = ObjectCreationMode.Create;
    public override string ToSql() => AstSerializer.Format(this);
}

public record CreateVisualStatement : Statement
{
    public required string Name { get; init; }
    public required VisualType VisualType { get; init; }
    public Expression? Title { get; init; }
    public bool TitleIsMarkdown { get; init; }
    public Expression? Subtitle { get; init; }
    public bool SubtitleIsMarkdown { get; init; }
    public TooltipDefinition? Tooltip { get; init; }
    public Expression? DefaultValue { get; init; }
    public string? LabelPosition { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }
    public int? Decimals { get; init; }
    public Expression? Placeholder { get; init; }
    public required VisualSourceExpression Source { get; init; }
    public List<VisualMapping> Mappings { get; init; } = new();
    public List<VisualOption> Options { get; init; } = new();
    public List<AxisOptions> AxisOptions { get; init; } = new();
    public List<VisualAction> Actions { get; init; } = new();
    public List<VisualInteraction> Interactions { get; init; } = new();
    public List<TypedSeries> TypedSeries { get; init; } = new();
    public List<FormattingRule> FormattingRules { get; init; } = new();
    public List<VisualOverlay> Overlays { get; init; } = new();
    public List<TableSummaryItem> Summaries { get; init; } = new();
    public TableSummaryOptions? SummaryOptions { get; init; }
    public Dictionary<string, string> Styles { get; init; } = new();
    public VisualFetchMode FetchMode { get; init; } = VisualFetchMode.Auto;
    /// <summary>Name of a CREATE STYLE to inherit. Merged before inline Styles (inline wins).</summary>
    public string? StyleName { get; init; }
    public ObjectCreationMode Mode { get; init; } = ObjectCreationMode.Create;
    public PrintLayoutOverride? PrintLayout { get; init; }
    public RowDetailDefinition? RowDetail { get; init; }
    public CascadeDefinition? Cascade { get; init; }
    public AdvancedChartDefinition? AdvancedChart { get; init; }
    public override string ToSql() => AstSerializer.Format(this);
}

/// ) ;
/// </summary>
public record CreatePageStatement : Statement
{
    public required string Name { get; init; }
    public PageMode PageMode { get; init; } = PageMode.Dashboard;
    public required string Structure { get; init; }
    public Dictionary<string, string> SlotMap { get; init; } = new();
    public Dictionary<string, string> Styles { get; init; } = new();
    public string? StyleName { get; init; }
    public Expression? Title { get; init; }
    public bool TitleIsMarkdown { get; init; }
    public Expression? Subtitle { get; init; }
    public bool SubtitleIsMarkdown { get; init; }
    public TooltipDefinition? Tooltip { get; init; }
    public string? Visibility { get; init; }
    /// <summary>Auto-refresh interval in seconds (0 = disabled).</summary>
    public int RefreshIntervalSeconds { get; init; }
    public ObjectCreationMode Mode { get; init; } = ObjectCreationMode.Create;
    public PageLayoutDefinition? PrintLayout { get; init; }
}

public record PageLayoutDefinition
{
    public string? PageSize { get; init; }
    public decimal? CustomWidth { get; init; }
    public decimal? CustomHeight { get; init; }
    public string? Orientation { get; init; }
    public string? Units { get; init; }
    public decimal? MarginTop { get; init; }
    public decimal? MarginRight { get; init; }
    public decimal? MarginBottom { get; init; }
    public decimal? MarginLeft { get; init; }
    public string? Overflow { get; init; }
}

public record PrintLayoutOverride
{
    public bool? PageBreakBefore { get; init; }
    public bool? PageBreakAfter { get; init; }
    public bool? KeepTogether { get; init; }
    public bool? ExcludeFromPrint { get; init; }
}

/// <summary>
/// CREATE DATASET &name
///     TTL = '&lt;duration&gt;'
///     COMPRESS = ON|OFF
///     ENCRYPT = ON|OFF
///     KEYFILE = '&lt;path&gt;'
/// AS ( SELECT ... );
/// </summary>
/// <summary>SET REPORT TITLE = '...' / SET REPORT DESCRIPTION = '...'</summary>
public record SetReportMetadataStatement : Statement
{
    public required string Key { get; init; }  // "TITLE" or "DESCRIPTION"
    public required string Value { get; init; }
}

public record CreateContainerStatement : Statement
{
    public required string Name { get; init; }
    public required string ContainerType { get; init; }  // BOX, SCROLL, DRAWER, SIDEBAR, TABS, ACCORDION, MODAL, or POPOVER
    public string? Structure { get; init; }
    public Dictionary<string, string> SlotMap { get; init; } = new();
    public Dictionary<string, string> Styles { get; init; } = new();
    public string? StyleName { get; init; }
    public Expression? Title { get; init; }
    public bool TitleIsMarkdown { get; init; }
    public Expression? Subtitle { get; init; }
    public bool SubtitleIsMarkdown { get; init; }
    public TooltipDefinition? Tooltip { get; init; }
    public bool IsCollapsible { get; init; }
    public string? Visibility { get; init; }
    public string? Icon { get; init; }
    public bool IsPinnable { get; init; } = true;
    public ObjectCreationMode Mode { get; init; } = ObjectCreationMode.Create;

}

public enum NavigationType { Tab, Button, Link }
public enum NavigationOrientation { Horizontal, Vertical }

public record CreateNavigationStatement : Statement
{
    public required string Name { get; init; }
    public NavigationType NavType { get; init; }
    public NavigationOrientation Orientation { get; init; }
    public string? DefaultPage { get; init; }
    public List<string> Pages { get; init; } = new();
    public ObjectCreationMode Mode { get; init; } = ObjectCreationMode.Create;
}

public record CreateDatasetStatement : Statement
{
    public required string TempTableName { get; init; }
    public string? RefreshInterval { get; init; }
    public string? Ttl { get; init; }
    public bool Compress { get; init; }
    public DatasetEncryptionMode EncryptionMode { get; init; }
    public string? EncryptionPassword { get; init; }
    public string? KeyFile { get; init; }
    public DatasetAccessLevel AccessLevel { get; init; } = DatasetAccessLevel.Private;
    public required Statement SourceQuery { get; init; }
    public ObjectCreationMode Mode { get; init; } = ObjectCreationMode.Create;
    public override string? GetCreatedTable() => TempTableName;
}

/// <summary>
/// USE DATASET &amp;name — loads a named dataset from the portal registry into the
/// calling script's temp-table namespace. In non-portal mode, verifies the dataset
/// was already created in the current script (no-op if already loaded).
/// </summary>
public record UseDatasetStatement : Statement
{
    public required string DatasetName { get; init; }
}

/// <summary>
/// SHOW DATASETS [INTO #temp] — lists all datasets visible to the calling context.
/// Columns: Name, FolderPath, AccessLevel, RowCount, LastRefresh, IsStale, RefreshInterval, Ttl.
/// </summary>
public record ShowDatasetsStatement : Statement
{
    public string? IntoTable { get; init; }
    public override string? GetCreatedTable() => IntoTable;
}

/// <summary>
/// REFRESH DATASET &amp;name — forces re-execution of the stored source query,
/// re-writes the Parquet file, and updates LastRefresh in the registry.
/// Requires refresh/editor/owner permission in portal context.
/// </summary>
public record RefreshDatasetStatement : Statement
{
    public required string DatasetName { get; init; }
}

/// <summary>
/// EXPORT DATASET &amp;name TO '&lt;file&gt;' ENCRYPT = PASSWORD|KEYFILE [PASSWORD=… | KEYFILE=…] —
/// writes a portable copy of the dataset's Parquet, re-encrypted with a transport credential
/// (supplied here, never persisted), so it can be moved to another machine/portal and PUBLISHed.
/// </summary>
public record ExportDatasetStatement : Statement
{
    public required string DatasetName { get; init; }
    public required string TargetPath { get; init; }
    public DatasetEncryptionMode EncryptionMode { get; init; } = DatasetEncryptionMode.None;
    public string? EncryptionPassword { get; init; }
    public string? KeyFile { get; init; }
}

/// <summary>
/// PUBLISH DATASET &amp;name FROM '&lt;file&gt;' [INTO '&lt;folder&gt;'] [ACCESS PUBLIC|PRIVATE]
/// ENCRYPT = PASSWORD|KEYFILE [PASSWORD=… | KEYFILE=…] — imports a portable EXPORTed file into the
/// portal: decrypts once with the supplied transport credential, then re-encrypts with the portal
/// at-rest key and registers it. The published copy is at-rest-encrypted (not movable); the author
/// keeps the original export file.
/// </summary>
public record PublishDatasetStatement : Statement
{
    public required string SourcePath { get; init; }
    public required string DatasetName { get; init; }
    public string? TargetFolder { get; init; }
    public DatasetAccessLevel AccessLevel { get; init; } = DatasetAccessLevel.Private;
    public DatasetEncryptionMode EncryptionMode { get; init; } = DatasetEncryptionMode.None;
    public string? EncryptionPassword { get; init; }
    public string? KeyFile { get; init; }
    public override string? GetCreatedTable() => DatasetName;
}

/// <summary>
/// DROP CHART|PAGE|CONTAINER|STYLE|NAVIGATION|DATASET <name>
/// </summary>
public record DropReportObjectStatement : Statement
{
    public required ReportObjectType ObjectType { get; init; }
    public required string Name { get; init; }
    public bool IfExists { get; init; }
}

public record AlterReportObjectStatement : Statement
{
    public required ReportObjectType ObjectType { get; init; }
    public required string Name { get; init; }
    public VisualSourceExpression? Source { get; init; }
    public List<VisualMapping>? Mappings { get; init; }
    public List<VisualOption>? Options { get; init; }
    public List<AxisOptions>? AxisOptions { get; init; }
    public List<VisualAction>? Actions { get; init; }
    public Dictionary<string, string>? Styles { get; init; }
    public string? StyleName { get; init; }
    public Expression? Title { get; init; }
    public bool TitleIsMarkdown { get; init; }
    public Expression? Subtitle { get; init; }
    public bool SubtitleIsMarkdown { get; init; }
    public TooltipDefinition? Tooltip { get; init; }
    /// <summary>PAGE and CONTAINER only. Null means the clause was absent, so the value is kept.</summary>
    public string? Visibility { get; init; }
    /// <summary>PAGE only. Null means absent; 0 disables the auto-refresh.</summary>
    public int? RefreshIntervalSeconds { get; init; }
    /// <summary>CONTAINER only.</summary>
    public string? Icon { get; init; }
}

/// <summary>
/// CREATE TEMPLATE <name> AS (<options>)
/// </summary>
public record CreateTemplateStatement : Statement
{
    public required string Name { get; init; }
    public Dictionary<string, string> Options { get; init; } = new();
    public ObjectCreationMode Mode { get; init; } = ObjectCreationMode.Create;
    public override string ToSql() => AstSerializer.Format(this);
}

/// <summary>
/// CREATE THEME <name> AS (<style-key-value pairs>)
/// Theme properties are mapped to an native theme JSON and saved to the themes directory.
/// </summary>
public record CreateThemeStatement : Statement
{
    public required string Name { get; init; }
    public Dictionary<string, string> Properties { get; init; } = new();
    public ObjectCreationMode Mode { get; init; } = ObjectCreationMode.Create;
    public override string ToSql() => AstSerializer.Format(this);
}
