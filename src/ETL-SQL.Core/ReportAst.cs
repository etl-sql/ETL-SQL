using System.Collections.Generic;
using ETL_SQL.Core.Parser;

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
        Bar, Line, Scatter, Pie, Table, Card, Slicer
    }

    // ── Sub-nodes (all must be records since AstNode is a record) ────────────

    /// <summary>
    /// Source expression for a visual: either an inline SELECT or a #temp table reference.
    /// Exactly one of InlineSelect / TempTableName is set; the other is null.
    /// </summary>
    public record VisualSourceExpression : AstNode
    {
        /// <summary>Set when SOURCE = ( SELECT ... ) — the inline query AST.</summary>
        public SelectStatement? InlineSelect { get; init; }
        /// <summary>Set when SOURCE = #tableName — the temp table name (with #).</summary>
        public string? TempTableName        { get; init; }
        public bool IsInlineSelect          => InlineSelect != null;
    }

    /// <summary>Role-to-column mapping inside MAPPINGS ( role = Column, ... ).</summary>
    public record VisualMapping : AstNode
    {
        public required string Role   { get; init; }
        public required new string Column { get; init; }
    }

    /// <summary>Flat key-value option inside OPTIONS ( key = value, ... ).</summary>
    public record VisualOption : AstNode
    {
        public required string Key   { get; init; }
        public required string Value { get; init; }
    }

    /// <summary>Nested axis options: X_AXIS ( scale = ..., format = ... ) or Y_AXIS (...).</summary>
    public record AxisOptions : AstNode
    {
        public required string Axis             { get; init; }  // "X" or "Y"
        public List<VisualOption> Options       { get; init; } = new();
    }

    /// <summary>Base record for action definitions inside ACTIONS ( ... ).</summary>
    public abstract record VisualAction : AstNode
    {
        /// <summary>"ON_CLICK" or "ON_CHANGE"</summary>
        public required string Trigger { get; init; }
    }

    /// <summary>SET_PARAMETER(@paramName, columnRef) action.</summary>
    public record SetParameterAction : VisualAction
    {
        public required string ParameterName   { get; init; }
        public required string ValueExpression { get; init; }
    }

    /// <summary>DRILL_DOWN(Target = VisualName, Key = ColumnName) action.</summary>
    public record DrillDownAction : VisualAction
    {
        public required string TargetVisual { get; init; }
        public required string KeyColumn    { get; init; }
    }

    /// <summary>A parameter default declared in WITH PARAMETERS ( @name = value, ... ).</summary>
    public record PageParameter : AstNode
    {
        public required string Name     { get; init; }
        public string? DefaultValue     { get; init; }
    }

    // ── Top-level statements ──────────────────────────────────────────────────

    /// <summary>
    /// CREATE VISUAL &lt;name&gt; AS &lt;type&gt; (
    ///     SOURCE = ( ... ) | #table,
    ///     MAPPINGS ( ... ),
    ///     OPTIONS ( ... ),
    ///     ACTIONS ( ... )
    /// );
    /// </summary>
    public record CreateVisualStatement : Statement
    {
        public required string Name                    { get; init; }
        public required VisualType VisualType          { get; init; }
        public required VisualSourceExpression Source  { get; init; }
        public List<VisualMapping> Mappings            { get; init; } = new();
        public List<VisualOption> Options              { get; init; } = new();
        public List<AxisOptions> AxisOptions           { get; init; } = new();
        public List<VisualAction> Actions              { get; init; } = new();
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
    public record CreateDatasetStatement : Statement
    {
        public required string TempTableName         { get; init; }
        public string? RefreshInterval               { get; init; }
        public string? Ttl                           { get; init; }
        public bool Compress                         { get; init; }
        public bool Encrypt                          { get; init; }
        public string? KeyFile                       { get; init; }
        public required SelectStatement SourceQuery  { get; init; }
    }
}
