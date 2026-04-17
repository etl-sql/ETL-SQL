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

    // ── Enumerations ──────────────────────────────────────────────────────────

    public enum VisualType
    {
        Bar, Line, Scatter, Pie, Table, Card, Slicer,
        Donut, HorizontalBar, BoxPlot, Treemap, HeatMap, Text, Combo,
        DatePicker, Slider, MultiSelect, Search,
        Gauge, Funnel, Waterfall
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
    /// Source expression for a visual: either an inline SELECT or a #temp table reference.
    /// Exactly one of InlineSelect / TempTableName is set; the other is null.
    /// </summary>
    public record VisualSourceExpression : AstNode
    {
        public SelectStatement? InlineSelect { get; init; }
        public string? TempTableName        { get; init; }
        public bool IsInlineSelect          => InlineSelect != null;
        public string ToSql() => AstSerializer.Format(this);
    }

    public record VisualMapping : AstNode
    {
        public required string Role   { get; init; }
        public required new string Column { get; init; }
        public string ToSql() => AstSerializer.Format(this);
    }

    public record VisualOption : AstNode
    {
        public required string Key   { get; init; }
        public required string Value { get; init; }
        public string ToSql() => AstSerializer.Format(this);
    }

    public record AxisOptions : AstNode
    {
        public required string Axis             { get; init; }  // "X" or "Y"
        public List<VisualOption> Options       { get; init; } = new();
        public string ToSql() => AstSerializer.Format(this);
    }

    public abstract record VisualAction : AstNode
    {
        public required string Trigger { get; init; }
        public virtual string ToSql() => "UNKNOWN ACTION";
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

    public record PageParameter : AstNode
    {
        public required string Name     { get; init; }
        public string? DataType         { get; init; }
        public string? DefaultValue     { get; init; }
        public string ToSql() => AstSerializer.Format(this);
    }

    public record FormattingRule : AstNode
    {
        public required new string Column { get; init; }  // hides AstNode.Column intentionally
        public required string Operator   { get; init; }  // "<", ">", "<=", ">=", "=", "<>"
        public required string Threshold  { get; init; }
        public required string Color      { get; init; }
    }

    public record TypedSeries : AstNode
    {
        public required string SeriesType { get; init; }  // "bar" or "line"
        public new required string Column { get; init; }
    }

    public record CreateVisualStatement : Statement
    {
        public required string Name                    { get; init; }
        public required VisualType VisualType          { get; init; }
        public string? Title                          { get; init; }
        public string? Subtitle                       { get; init; }
        public required VisualSourceExpression Source  { get; init; }
        public List<VisualMapping> Mappings            { get; init; } = new();
        public List<VisualOption> Options              { get; init; } = new();
        public List<AxisOptions> AxisOptions           { get; init; } = new();
        public List<VisualAction> Actions              { get; init; } = new();
        public List<TypedSeries> TypedSeries           { get; init; } = new();
        public List<FormattingRule> FormattingRules    { get; init; } = new();
        public Dictionary<string, string> Styles       { get; init; } = new();
        public override string ToSql() => AstSerializer.Format(this);
    }

    /// <summary>
    /// CREATE PAGE &lt;name&gt; AS LAYOUT (
    ///     STRUCTURE = '...',
    ///     MAP ( 'A' = VisualName, ... )
    /// ) WITH PARAMETERS ( @name = default, ... );
    /// </summary>
    public record CreatePageStatement : Statement
    {
        public required string Name                           { get; init; }
        public required string Structure                      { get; init; }
        public Dictionary<string, string> SlotMap             { get; init; } = new();
        public List<PageParameter> Parameters                 { get; init; } = new();
        public Dictionary<string, string> Styles              { get; init; } = new();
    }

    /// <summary>
    /// CREATE DATASET #name
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
        public List<string> Visuals { get; init; } = new();
        public Dictionary<string, string> Styles { get; init; } = new();
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
        public required SelectStatement SourceQuery   { get; init; }
    }
}
