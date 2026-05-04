using System.Collections.Generic;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Formatting;

namespace ETL_SQL.Core
{
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
        public string? PlainText     { get; init; }
        public string? ContainerRef  { get; init; }
        public string? InlineMarkdown{ get; init; }
        public List<string>? InlineVisuals { get; init; }

        public static TooltipDefinition Text(string text) =>
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
        Theme
    }

    public enum OverlayType
    {
        Goal, Average, MovingAvg, Linear, Exponential, Logarithmic, Polynomial, Power
    }

    public enum OverlayLineStyle { Solid, Dashed, Dotted }

    public record VisualOverlay : AstNode
    {
        public required OverlayType  OverlayType { get; init; }
        public double?               Parameter   { get; init; }  // GOAL value, MOVING_AVG window, POLYNOMIAL degree
        public OverlayLineStyle      LineStyle   { get; init; } = OverlayLineStyle.Dashed;
        public string?               Color       { get; init; }
        public string?               Label       { get; init; }
    }

    public enum VisualType
    {
        Bar, Line, Scatter, Pie, Table, Card, Slicer,
        Donut, HorizontalBar, BoxPlot, Treemap, HeatMap, Text, Combo,
        DatePicker, Slider, MultiSelect, Search,
        Gauge, Funnel, Waterfall, Image,
        Bubble, Radar, Candlestick
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
        public string? TempTableName  { get; init; }
        public bool IsInlineSelect    => InlineSelect != null;
        public override string ToSql() => AstSerializer.Format(this);
    }

    public record VisualMapping : AstNode
    {
        public required string Role   { get; init; }
        public required new string Column { get; init; }
        public override string ToSql() => AstSerializer.Format(this);
    }

    public record VisualOption : AstNode
    {
        public required string Key   { get; init; }
        public required string Value { get; init; }
        public override string ToSql() => AstSerializer.Format(this);
    }

    public record AxisOptions : AstNode
    {
        public required string Axis             { get; init; }  // "X" or "Y"
        public List<VisualOption> Options       { get; init; } = new();
        public override string ToSql() => AstSerializer.Format(this);
    }

    public abstract record VisualAction : AstNode
    {
        public required string Trigger { get; init; }
        public override string ToSql() => "UNKNOWN ACTION";
    }

    public record SetParameterAction : VisualAction
    {
        public required string ParameterName   { get; init; }
        public required string ValueExpression { get; init; }
        public override string ToSql() => AstSerializer.Format(this);
    }

    public record DrillDownAction : VisualAction
    {
        public required string TargetVisual { get; init; }
        public required string KeyColumn    { get; init; }
        public override string ToSql() => AstSerializer.Format(this);
    }

    public record TableSummaryItem(string Aggregate, string Column, string? Alias);

    public record TableSummaryOptions : AstNode
    {
        public bool GrandTotalRow    { get; init; }
        public bool GrandTotalColumn { get; init; }
        public bool SummarizeRow     { get; init; }
        public bool SummarizeColumn  { get; init; }
        public List<string>? SpecificColumns { get; init; }
    }

    public record FormattingRule : AstNode
    {
        public required Expression Condition { get; init; }
        public required string Color         { get; init; }
    }

    public record TypedSeries : AstNode
    {
        public required string SeriesType { get; init; }  // "bar" or "line"
        public new required string Column { get; init; }
    }

    /// <summary>CREATE STYLE <name> (key = value, ...)</summary>
    public record CreateStyleStatement : Statement
    {
        public required string Name                  { get; init; }
        public Dictionary<string, string> Styles     { get; init; } = new();
        public string? StyleName                     { get; init; }
        public ObjectCreationMode Mode               { get; init; } = ObjectCreationMode.Create;
        public override string ToSql() => AstSerializer.Format(this);
    }

    public record CreateButtonStatement : Statement
    {
        public required string Name                    { get; init; }
        public required string ButtonType              { get; init; } // BACK, REFRESH, HELP, etc.
        public string? Title                          { get; init; }
        public TooltipDefinition? Tooltip             { get; init; }
        public List<VisualOption> Options              { get; init; } = new();
        public List<VisualAction> Actions              { get; init; } = new();
        public Dictionary<string, string> Styles       { get; init; } = new();
        public string? StyleName                       { get; init; }
        public ObjectCreationMode Mode               { get; init; } = ObjectCreationMode.Create;
        public override string ToSql() => AstSerializer.Format(this);
    }

    public record CreateVisualStatement : Statement
    {
        public required string Name                    { get; init; }
        public required VisualType VisualType          { get; init; }
        public string? Title                          { get; init; }
        public bool TitleIsMarkdown                  { get; init; }
        public string? Subtitle                       { get; init; }
        public bool SubtitleIsMarkdown               { get; init; }
        public TooltipDefinition? Tooltip             { get; init; }
        public string? DefaultValue                   { get; init; }
        public required VisualSourceExpression Source  { get; init; }
        public List<VisualMapping> Mappings            { get; init; } = new();
        public List<VisualOption> Options              { get; init; } = new();
        public List<AxisOptions> AxisOptions           { get; init; } = new();
        public List<VisualAction> Actions              { get; init; } = new();
        public List<TypedSeries> TypedSeries           { get; init; } = new();
        public List<FormattingRule> FormattingRules    { get; init; } = new();
        public List<VisualOverlay> Overlays            { get; init; } = new();
        public List<TableSummaryItem> Summaries        { get; init; } = new();
        public TableSummaryOptions? SummaryOptions     { get; init; }
        public Dictionary<string, string> Styles       { get; init; } = new();
        /// <summary>Name of a CREATE STYLE to inherit. Merged before inline Styles (inline wins).</summary>
        public string? StyleName                       { get; init; }
        public ObjectCreationMode Mode                 { get; init; } = ObjectCreationMode.Create;
        public override string ToSql() => AstSerializer.Format(this);
    }

    /// ) ;
    /// </summary>
    public record CreatePageStatement : Statement
    {
        public required string Name                           { get; init; }
        public required string Structure                      { get; init; }
        public Dictionary<string, string> SlotMap             { get; init; } = new();
        public Dictionary<string, string> Styles              { get; init; } = new();
        public string? StyleName                              { get; init; }
        public string? Title                                  { get; init; }
        public bool TitleIsMarkdown                          { get; init; }
        public string? Subtitle                               { get; init; }
        public bool SubtitleIsMarkdown                       { get; init; }
        public TooltipDefinition? Tooltip                     { get; init; }
        public bool IsHidden                                  { get; init; }
        /// <summary>Auto-refresh interval in seconds (0 = disabled).</summary>
        public int RefreshIntervalSeconds                     { get; init; }
        public ObjectCreationMode Mode                         { get; init; } = ObjectCreationMode.Create;
    }

    /// <summary>
    /// CREATE DATASET &name
    ///     REFRESH EVERY '&lt;interval&gt;'
    ///     TTL = '&lt;duration&gt;'
    ///     COMPRESS = ON|OFF
    ///     ENCRYPT = ON|OFF
    ///     KEYFILE = '&lt;path&gt;'
    /// AS ( SELECT ... );
    /// </summary>
    /// <summary>SET REPORT TITLE = '...' / SET REPORT DESCRIPTION = '...'</summary>
    public record SetReportMetadataStatement : Statement
    {
        public required string Key   { get; init; }  // "TITLE" or "DESCRIPTION"
        public required string Value { get; init; }
    }

    public record CreateContainerStatement : Statement
    {
        public required string Name { get; init; }
        public required string ContainerType { get; init; }  // "BOX" or "SCROLL"
        public string? Structure { get; init; }
        public Dictionary<string, string> SlotMap { get; init; } = new();
        public Dictionary<string, string> Styles { get; init; } = new();
        public string? StyleName { get; init; }
        public string? Title { get; init; }
        public bool TitleIsMarkdown { get; init; }
        public string? Subtitle { get; init; }
        public bool SubtitleIsMarkdown { get; init; }
        public TooltipDefinition? Tooltip { get; init; }
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
        public required string TempTableName          { get; init; }
        public string? RefreshInterval                { get; init; }
        public string? Ttl                            { get; init; }
        public bool Compress                          { get; init; }
        public DatasetEncryptionMode EncryptionMode   { get; init; }
        public string? EncryptionPassword             { get; init; }
        public string? KeyFile                        { get; init; }
        public required Statement SourceQuery         { get; init; }
        public ObjectCreationMode Mode                { get; init; } = ObjectCreationMode.Create;
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
        public required ReportObjectType ObjectType    { get; init; }
        public required string Name                   { get; init; }
        public VisualSourceExpression? Source          { get; init; }
        public List<VisualMapping>? Mappings           { get; init; }
        public List<VisualOption>? Options             { get; init; }
        public List<AxisOptions>? AxisOptions          { get; init; }
        public List<VisualAction>? Actions             { get; init; }
        public Dictionary<string, string>? Styles      { get; init; }
        public string? StyleName                       { get; init; }
        public string? Title                           { get; init; }
        public bool TitleIsMarkdown                   { get; init; }
        public string? Subtitle                        { get; init; }
        public bool SubtitleIsMarkdown                { get; init; }
        public TooltipDefinition? Tooltip              { get; init; }
    }

    /// <summary>
    /// CREATE TEMPLATE <name> AS (<options>)
    /// </summary>
    public record CreateTemplateStatement : Statement
    {
        public required string Name                  { get; init; }
        public Dictionary<string, string> Options    { get; init; } = new();
        public ObjectCreationMode Mode               { get; init; } = ObjectCreationMode.Create;
        public override string ToSql() => AstSerializer.Format(this);
    }

    /// <summary>
    /// CREATE THEME <name> AS (<style-key-value pairs>)
    /// Theme properties are mapped to an ECharts theme JSON and saved to the themes directory.
    /// </summary>
    public record CreateThemeStatement : Statement
    {
        public required string Name                       { get; init; }
        public Dictionary<string, string> Properties      { get; init; } = new();
        public ObjectCreationMode Mode                    { get; init; } = ObjectCreationMode.Create;
        public override string ToSql() => AstSerializer.Format(this);
    }
}
