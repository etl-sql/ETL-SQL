using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ETL_SQL.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Formatting;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Quality;
using ETL_SQL.Data;

namespace ETL_SQL.Core;
/// <summary>Base class for all Abstract Syntax Tree nodes, tracking source locations.</summary>
public abstract record AstNode
{
    /// <summary>Starting line number in the source script.</summary>
    public int Line { get; init; }
    /// <summary>Starting column position in the source script.</summary>
    public int Column { get; init; }
    /// <summary>Ending line number in the source script.</summary>
    public int EndLine { get; init; }
    /// <summary>Ending column position in the source script.</summary>
    public int EndColumn { get; init; }

    /// <summary>Converts the node back to its SQL representation using the central serializer.</summary>
    public virtual string ToSql() => AstSerializer.Format(this);
}

/// <summary>Base class for all executable SQL statements.</summary>
public abstract record Statement : AstNode
{
    /// <summary>Common Table Expressions (WITH clause) applied to this statement.</summary>
    public List<CteDefinition>? Ctes { get; init; }
    /// <summary>Identifies all tables referenced as data sources in this statement.</summary>
    public virtual IEnumerable<string> GetSourceTables() => Enumerable.Empty<string>();
    /// <summary>Identifies the table created or populated by this statement (e.g. INTO #temp).</summary>
    public virtual string? GetCreatedTable() => null;
}

public enum ObjectCreationMode { Create, Alter, CreateOrAlter, CreateOrReplace }

public sealed record Script : AstNode
{
    public List<Statement> Statements { get; init; } = new();
    public List<ETL_SQL.Core.Common.Diagnostic> Diagnostics { get; init; } = new();
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record NoOpStatement : Statement
{
}

public sealed record CreateConnectionStatement(string name, string? type = null, Expression? target = null, Dictionary<string, Expression>? options = null, ObjectCreationMode mode = ObjectCreationMode.Create) : Statement
{
    public string ConnectionName { get; } = name;
    public string? ConnectionType { get; } = type; // FILE, DATABASE, EXCEL
    public Expression? TargetExpression { get; } = target;
    public Dictionary<string, Expression>? Options { get; } = options;
    public ObjectCreationMode Mode { get; } = mode;
}



public sealed record CreateBindingStatement(string Name, string Type, Dictionary<string, Expression>? Options = null, ObjectCreationMode Mode = ObjectCreationMode.Create) : Statement;

public sealed record GrantBindingStatement(string Permission, string BindingName, string PrincipalKind, string PrincipalName) : Statement;

public sealed record RevokeBindingStatement(string Permission, string BindingName, string PrincipalKind, string PrincipalName) : Statement;
public sealed record CreateSshKeyPairStatement(Expression path, Expression? bits = null, Expression? algorithm = null, Expression? passphrase = null, Expression? comment = null) : Statement
{
    public Expression Path { get; } = path;
    public Expression? Bits { get; } = bits;
    public Expression? Algorithm { get; } = algorithm;
    public Expression? Passphrase { get; } = passphrase;
    public Expression? Comment { get; } = comment;
}

public sealed record CreatePgpKeyPairStatement(Expression path, Expression? bits = null, Expression? identity = null, Expression? passphrase = null) : Statement
{
    public Expression Path { get; } = path;
    public Expression? Bits { get; } = bits;
    public Expression? Identity { get; } = identity;
    public Expression? Passphrase { get; } = passphrase;
}


public sealed record SelectColumn(Expression expression, string? alias = null, Dictionary<string, string>? metadata = null) : AstNode
{
    public Expression Expression { get; } = expression;
    public string? Alias { get; } = alias;
    // Lazily allocated: most select columns carry no metadata, so the dictionary is created on
    // first use. Description reads the backing field directly to avoid allocating just to look up.
    private Dictionary<string, string>? _metadata = metadata;
    public Dictionary<string, string> Metadata
    {
        get => _metadata ??= new(StringComparer.OrdinalIgnoreCase);
        set => _metadata = value;
    }
    public string? Description => _metadata != null && _metadata.TryGetValue("d", out var d) ? d : null;
    public string? DerivedFromDescriptions { get; set; }
}

public sealed record TableReference : AstNode
{
    public string? ConnectionName { get; }
    public string? DatabaseName { get; }
    public string? SchemaName { get; }
    public string TableName { get; }
    public string? Alias { get; }
    public Statement? Subquery { get; }
    public FunctionCallExpression? FunctionCall { get; }
    public List<List<Expression>>? ValuesRows { get; }
    public List<string>? ColumnAliases { get; }
    // Lazily allocated: PIVOT/UNPIVOT operators, table-hint options, and metadata are absent on
    // the vast majority of table references, so the backing collections are created on first use
    // rather than per-instance.
    private List<AstNode>? _tableOperators;
    private Dictionary<string, string>? _metadata;
    private Dictionary<string, Expression>? _options;
    public List<AstNode> TableOperators => _tableOperators ??= new();
    public Dictionary<string, string> Metadata
    {
        get => _metadata ??= new(StringComparer.OrdinalIgnoreCase);
        set => _metadata = value;
    }
    public Dictionary<string, Expression> Options
    {
        get => _options ??= new(StringComparer.OrdinalIgnoreCase);
        set => _options = value;
    }

    public TableReference(string tableName, string? schemaName = null, string? databaseName = null, string? connectionName = null, string? alias = null, Statement? subquery = null, FunctionCallExpression? functionCall = null, List<List<Expression>>? valuesRows = null, List<string>? columnAliases = null)
    {
        TableName = tableName;
        SchemaName = schemaName;
        DatabaseName = databaseName;
        ConnectionName = connectionName;
        Alias = alias;
        Subquery = subquery;
        FunctionCall = functionCall;
        ValuesRows = valuesRows;
        ColumnAliases = columnAliases;
    }

    public string FullyQualifiedName => (ConnectionName != null ? ConnectionName + "." : "") + (DatabaseName != null ? DatabaseName + "." : "") + (SchemaName != null ? SchemaName + "." : "") + TableName;

    public override string ToSql() => AstSerializer.Format(this);

    public IEnumerable<string> GetSourceTables()
    {
        if (Subquery is SelectStatement sel) return sel.GetSourceTables();
        if (Subquery is SetOperationStatement setOp) return setOp.GetSourceTables();
        if (ValuesRows != null) return Enumerable.Empty<string>();
        if (!string.IsNullOrEmpty(TableName) && TableName != "SUBQUERY" && TableName != "DUAL")
        {
            string fullPath = (ConnectionName != null ? ConnectionName + "." : "") + (DatabaseName != null ? DatabaseName + "." : "") + (SchemaName != null ? SchemaName + "." : "") + TableName;
            return new[] { fullPath };
        }
        return Enumerable.Empty<string>();
    }

    public override string ToString() => ToSql();
}

public sealed record PivotClause : AstNode
{
    public string AggregateFunction { get; }
    public string AggregateColumn { get; }
    public string PivotColumn { get; }
    public List<Expression> PivotValues { get; }
    public string? Alias { get; set; }

    public PivotClause(string aggregateFunction, string aggregateColumn, string pivotColumn, List<Expression> pivotValues)
    {
        AggregateFunction = aggregateFunction;
        AggregateColumn = aggregateColumn;
        PivotColumn = pivotColumn;
        PivotValues = pivotValues;
    }
}

public sealed record OutputClause : AstNode
{
    public List<SelectColumn> Columns { get; }
    public TableReference? IntoTable { get; }

    public OutputClause(List<SelectColumn> columns, TableReference? intoTable = null)
    {
        Columns = columns;
        IntoTable = intoTable;
    }
}

public sealed record UnpivotClause : AstNode
{
    public string ValueColumn { get; }
    public string NameColumn { get; }
    public List<string> UnpivotColumns { get; }
    public string? Alias { get; set; }
    /// <summary>DuckDB <c>ON COLUMNS(* EXCLUDE (...))</c>: unpivot every source column except those in
    /// <see cref="ExcludeColumns"/> (and the name/value columns), resolved at runtime. When true,
    /// <see cref="UnpivotColumns"/> is ignored.</summary>
    public bool AllColumnsExcept { get; init; }
    public List<string>? ExcludeColumns { get; init; }

    public UnpivotClause(string valueColumn, string nameColumn, List<string> unpivotColumns)
    {
        ValueColumn = valueColumn;
        NameColumn = nameColumn;
        UnpivotColumns = unpivotColumns;
    }
}

/// <summary>A single aggregate in a DuckDB-style PIVOT <c>USING</c> list, e.g. <c>SUM(amount) AS total</c>.</summary>
public sealed record PivotAggregate
{
    public string Function { get; }
    /// <summary>Aggregate argument column, or null/"*" for a no-argument aggregate such as <c>COUNT(*)</c>.</summary>
    public string? Column { get; }
    public string? Alias { get; }

    public PivotAggregate(string function, string? column, string? alias)
    {
        Function = function;
        Column = column;
        Alias = alias;
    }
}

/// <summary>
/// DuckDB-style PIVOT table operator: <c>PIVOT src ON &lt;cols&gt; [IN (&lt;values&gt;)] USING &lt;aggs&gt; [GROUP BY &lt;cols&gt;]</c>.
/// Distinct from the SQL-standard <see cref="PivotClause"/>; supports multiple pivot columns, multiple
/// aggregates, dynamic value discovery (when <see cref="InValues"/> is null), and an explicit row grouping.
/// </summary>
public sealed record DuckPivotClause : AstNode
{
    public List<string> OnColumns { get; }
    /// <summary>Explicit pivot values (single ON column only); null requests runtime discovery of distinct combinations.</summary>
    public List<Expression>? InValues { get; }
    public List<PivotAggregate> Aggregates { get; }
    /// <summary>Explicit grouping (row) columns; null groups by all columns not consumed by ON or the aggregates.</summary>
    public List<string>? GroupByColumns { get; }
    public string? Alias { get; set; }

    public DuckPivotClause(List<string> onColumns, List<Expression>? inValues, List<PivotAggregate> aggregates, List<string>? groupByColumns)
    {
        OnColumns = onColumns;
        InValues = inValues;
        Aggregates = aggregates;
        GroupByColumns = groupByColumns;
    }
}

public sealed record MatchRecognizeClause : AstNode
{
    public List<Expression> PartitionBy { get; } = new();
    public List<OrderByClause> OrderBy { get; } = new();
    public List<SelectColumn> Measures { get; } = new();
    public string Pattern { get; set; } = "";
    public Dictionary<string, Expression> Definitions { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool AllRowsPerMatch { get; set; }
    public string? Alias { get; set; }
}

public enum JoinHint { None, Hash, Loop, Merge }

public sealed record JoinClause : AstNode
{
    public string JoinType { get; }
    public TableReference Table { get; }
    public Expression Condition { get; }
    public JoinHint Hint { get; set; } = JoinHint.None;
    public int? KeepBest { get; init; }  // FUZZY JOIN: KEEP BEST n; null = keep all above threshold
    public bool IsApply => JoinType.Contains("APPLY");
    public bool IsFuzzy => JoinType.Contains("FUZZY", StringComparison.OrdinalIgnoreCase);
    public bool IsAsof => JoinType.StartsWith("ASOF", StringComparison.OrdinalIgnoreCase);

    public JoinClause(string joinType, TableReference table, Expression condition, JoinHint hint = JoinHint.None, int? keepBest = null)
    {
        JoinType = joinType;
        Table = table;
        Condition = condition;
        Hint = hint;
        KeepBest = keepBest;
    }
}

public sealed record OrderByClause : AstNode
{
    public Expression Expression { get; }
    public bool Descending { get; }
    public OrderByClause(Expression expression, bool descending = false)
    {
        Expression = expression;
        Descending = descending;
    }
}

public sealed record CteDefinition : AstNode
{
    public string Name { get; }
    public List<string>? ColumnNames { get; }
    public Statement Query { get; }
    public CteDefinition(string name, Statement query, List<string>? columnNames = null)
    {
        Name = name;
        Query = query;
        ColumnNames = columnNames;
    }
}

public enum ForType { JSON, XML }
public enum ForMode { PATH, AUTO, RAW, EXPLICIT }

public sealed record ForClause : AstNode
{
    public ForType Type { get; }
    public ForMode Mode { get; }
    public string? RootName { get; }
    public bool IncludeNullValues { get; set; }
    public bool WithoutArrayWrapper { get; set; }
    public bool UseElements { get; set; }

    public ForClause(ForType type, ForMode mode, string? rootName = null)
    {
        Type = type;
        Mode = mode;
        RootName = rootName;
    }
}

public sealed record SelectStatement : Statement
{
    public List<SelectColumn> Columns { get; init; }
    public TableReference? IntoTable { get; init; }
    public TableReference FromTable { get; init; }
    public List<JoinClause> Joins { get; init; }
    public Expression? WhereClause { get; init; }
    public List<Expression>? GroupBy { get; init; }
    /// <summary>Non-null when GROUP BY uses GROUPING SETS / ROLLUP / CUBE. Null for plain GROUP BY.</summary>
    public GroupingSetClause? GroupingSet { get; init; }
    public Expression? HavingClause { get; init; }
    public List<OrderByClause>? OrderBy { get; init; }
    public bool IsDistinct { get; init; }
    public Expression? TopCount { get; init; }
    public bool IsTopPercent { get; init; }
    public bool WithTies { get; init; }
    public Expression? LimitCount { get; init; }
    public Expression? Offset { get; init; }
    public ForClause? ForClause { get; init; }
    public Expression? QualifyClause { get; init; }
    public List<NamedWindowDefinition>? WindowDefinitions { get; init; }
    public bool IsRecursive { get; init; }
    /// <summary>True for <c>GROUP BY ALL</c>. The engine expands this to every non-aggregate, non-window
    /// SELECT expression before execution, after which <see cref="GroupBy"/> holds the concrete list.</summary>
    public bool GroupByAll { get; init; }
    /// <summary>True for <c>ORDER BY ALL</c>; the engine expands it to every output column once they are known.</summary>
    public bool OrderByAll { get; init; }
    public bool OrderByAllDescending { get; init; }
    /// <summary><c>USING SAMPLE</c> clause; null when absent.</summary>
    public SampleClause? Sample { get; init; }
    /// <summary>
    /// Trailing <c>ON FAILURE &lt;ACTION&gt; [TO &lt;table&gt;] [WITH (RETENTION = '…')]</c> blocks
    /// routing <c>@fail</c> data-quality actions (at most one clause per action). Null when absent.
    /// </summary>
    public IReadOnlyList<FailureActionClause>? OnFailureActions { get; init; }

    public SelectStatement(List<SelectColumn> columns, TableReference? intoTable, TableReference fromTable, List<JoinClause> joins, Expression? whereClause, List<Expression>? groupBy = null, Expression? havingClause = null, List<OrderByClause>? orderBy = null)
    {
        Columns = columns;
        IntoTable = intoTable;
        FromTable = fromTable;
        Joins = joins;
        WhereClause = whereClause;
        GroupBy = groupBy;
        HavingClause = havingClause;
        OrderBy = orderBy;
    }

    public override IEnumerable<string> GetSourceTables()
    {
        var sources = new List<string>();
        if (FromTable != null) sources.AddRange(FromTable.GetSourceTables());
        foreach (var join in Joins)
        {
            sources.AddRange(join.Table.GetSourceTables());
        }
        return sources.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public override string? GetCreatedTable() => IntoTable?.TableName;
}

public enum GroupingSetType { None, GroupingSets, Rollup, Cube }

public sealed record NamedWindowDefinition(string Name, WindowClause Clause) : AstNode;

/// <summary>
/// Represents GROUP BY GROUPING SETS(...), ROLLUP(...), or CUBE(...).
/// When Type == None, GroupSets contains exactly one entry (the plain GROUP BY list).
/// </summary>
public sealed record GroupingSetClause : AstNode
{
    public GroupingSetType Type { get; }
    /// <summary>
    /// For GROUPING SETS: each inner list is one grouping set (empty list = grand total row).
    /// For ROLLUP/CUBE: exactly one inner list — the column list; expansion is done in the engine.
    /// </summary>
    public List<List<Expression>> GroupSets { get; }

    public GroupingSetClause(GroupingSetType type, List<List<Expression>> groupSets)
    {
        Type = type;
        GroupSets = groupSets;
    }
}

/// <summary><c>USING SAMPLE n PERCENT|ROWS [REPEATABLE (seed)]</c> — random row sampling.</summary>
public sealed record SampleClause(decimal Count, bool IsPercent, int? Seed) : AstNode;

/// <summary>
/// One trailing <c>ON FAILURE &lt;ACTION&gt; [TO &lt;table&gt;] [WITH (RETENTION = '…')]</c> block on a
/// SELECT carrying <c>@expect</c>/<c>@fail</c> rules. <c>QUARANTINE</c> requires a <see cref="Target"/>;
/// <c>WARN</c> optionally takes one (none = diagnostic-only); <c>THROW</c> never does. Validation is
/// symmetric (design decision 5): a <c>@fail</c> action without its clause and a clause without any
/// matching <c>@fail</c> rule are both hard errors.
/// </summary>
/// <param name="Handling">
/// QUARANTINE only: who owns the diverted rows. Defaults to <see cref="QuarantineHandling.Steward"/>,
/// the durable-evidence behavior; <see cref="QuarantineHandling.Script"/> marks rows the running
/// script will handle itself.
/// </param>
public sealed record FailureActionClause(
    FailAction Action,
    string? Target,
    RetentionInterval? Retention,
    QuarantineHandling Handling = QuarantineHandling.Steward) : AstNode;

public enum SetOpType { UNION, UNION_ALL, EXCEPT, INTERSECT }

public sealed record SetOperationStatement : Statement
{
    public Statement Left { get; }
    public SetOpType Operation { get; }
    public Statement Right { get; }
    /// <summary>True for <c>UNION [ALL] BY NAME</c>: align inputs by column name rather than position,
    /// filling columns missing from either side with NULL.</summary>
    public bool ByName { get; init; }

    public SetOperationStatement(Statement left, SetOpType op, Statement right)
    {
        Left = left;
        Operation = op;
        Right = right;
    }

    public override string? GetCreatedTable() => Left.GetCreatedTable() ?? Right.GetCreatedTable();

    public override IEnumerable<string> GetSourceTables()
    {
        var sources = new List<string>();
        sources.AddRange(Left.GetSourceTables());
        sources.AddRange(Right.GetSourceTables());
        return sources.Distinct(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record ExecStatement : Statement
{
    public Expression SqlExpression { get; }
    public Expression? ConnectionName { get; set; }
    public TableReference? IntoTable { get; set; }
    public List<Expression> Parameters { get; } = new();

    public ExecStatement(Expression sqlExpression, Expression? connectionName = null, TableReference? intoTable = null, List<Expression>? parameters = null)
    {
        SqlExpression = sqlExpression;
        ConnectionName = connectionName;
        IntoTable = intoTable;
        if (parameters != null) Parameters.AddRange(parameters);
    }
}

public sealed record ExecuteRemoteBlockStatement : Statement
{
    public Expression ConnectionName { get; }
    public BlockStatement Body { get; }
    public TableReference? IntoTable { get; }

    public ExecuteRemoteBlockStatement(Expression connectionName, BlockStatement body, TableReference? intoTable = null)
    {
        ConnectionName = connectionName;
        Body = body;
        IntoTable = intoTable;
    }
}

public sealed record ExecuteToolStatement(string ToolAlias, TableReference? SourceTable = null, TableReference? TargetTable = null, Dictionary<string, Expression>? Parameters = null, List<ExpectedSchemaColumn>? ExpectedSchema = null) : Statement
{
    public string ToolAlias { get; } = ToolAlias;
    public TableReference? SourceTable { get; } = SourceTable;
    public TableReference? TargetTable { get; } = TargetTable;
    public Dictionary<string, Expression>? Parameters { get; } = Parameters;
    public List<ExpectedSchemaColumn>? ExpectedSchema { get; } = ExpectedSchema;
}

public sealed record ExecutePushdownStatement : Statement
{
    public Expression ConnectionName { get; }
    public string SqlText { get; }
    public TableReference? IntoTable { get; }
    public List<Expression> Parameters { get; } = new();
    public bool HasUnbalancedBlocks { get; set; }

    public ExecutePushdownStatement(Expression connectionName, string sqlText, TableReference? intoTable = null, List<Expression>? parameters = null)
    {
        ConnectionName = connectionName;
        SqlText = sqlText;
        IntoTable = intoTable;
        if (parameters != null) Parameters.AddRange(parameters);
    }

    public override IEnumerable<string> GetSourceTables()
    {
        if (string.IsNullOrEmpty(SqlText)) return Enumerable.Empty<string>();

        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tokens = TokenizeSql(SqlText);

        // 1. Identify CTE names to exclude them from sources
        for (int i = 0; i < tokens.Count - 2; i++)
        {
            if (tokens[i].Equals("WITH", StringComparison.OrdinalIgnoreCase))
            {
                int j = i + 1;
                while (j < tokens.Count - 2)
                {
                    var cteName = tokens[j];
                    if (tokens[j + 1].Equals("AS", StringComparison.OrdinalIgnoreCase) && tokens[j + 2] == "(")
                    {
                        cteNames.Add(cteName.Replace("[", "").Replace("]", "").Replace("\"", ""));
                        // Skip the entire CTE definition body
                        j += 2;
                        int depth = 0;
                        while (j < tokens.Count)
                        {
                            if (tokens[j] == "(") depth++;
                            else if (tokens[j] == ")") depth--;
                            if (depth == 0) break;
                            j++;
                        }
                    }
                    if (j + 1 < tokens.Count && tokens[j + 1] == ",") j += 2;
                    else break;
                }
            }
        }

        // 2. Extract tables from FROM, JOIN, and APPLY clauses
        var connPrefix = ConnectionName.ToSql().Trim('\'', '(', ')');

        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i].ToUpperInvariant();
            if (t == "FROM" || t == "JOIN" || t == "APPLY")
            {
                int j = i + 1;
                while (j < tokens.Count)
                {
                    var tblToken = tokens[j];

                    // Skip common JOIN hints
                    if (tblToken.ToUpperInvariant() == "HASH" || tblToken.ToUpperInvariant() == "LOOP" || tblToken.ToUpperInvariant() == "MERGE")
                    {
                        j++; continue;
                    }

                    // Skip subqueries correctly
                    if (tblToken == "(" || tblToken.ToUpperInvariant() == "SELECT")
                    {
                        if (tblToken == "(")
                        {
                            int depth = 1;
                            j++;
                            while (j < tokens.Count && depth > 0)
                            {
                                if (tokens[j] == "(") depth++;
                                else if (tokens[j] == ")") depth--;
                                j++;
                            }
                        }
                        else j++;
                        break;
                    }

                    var cleanTbl = tblToken.Replace("[", "").Replace("]", "").Replace("\"", "");

                    // Avoid system keywords and variables, and ensure it's not a CTE
                    if (!cteNames.Contains(cleanTbl) && !cleanTbl.StartsWith("@") && !cleanTbl.Equals("DUAL", StringComparison.OrdinalIgnoreCase))
                    {
                        if (cleanTbl.Contains(".") && cleanTbl.StartsWith(connPrefix + ".", StringComparison.OrdinalIgnoreCase))
                            sources.Add(cleanTbl); // already connection-qualified
                        else
                            sources.Add($"{connPrefix}.{cleanTbl}"); // plain or schema-qualified — prepend connection
                    }

                    // Support comma-separated tables: FROM T1, T2
                    if (j + 1 < tokens.Count && tokens[j + 1] == ",")
                    {
                        j += 2;
                        continue;
                    }

                    break;
                }
            }
        }

        return sources.Count == 0 ? new[] { $"Native SQL on {ConnectionName.ToSql()}" } : sources;
    }

    private List<string> TokenizeSql(string sql)
    {
        var tokens = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inString = false;
        char? stringChar = null;
        bool inBracket = false;

        for (int i = 0; i < sql.Length; i++)
        {
            char c = sql[i];

            if (inString)
            {
                if (c == stringChar && (i + 1 >= sql.Length || sql[i + 1] != stringChar))
                {
                    inString = false;
                    stringChar = null;
                }
                else if (c == stringChar) { i++; } // Skip escaped char
                continue;
            }

            if (inBracket)
            {
                sb.Append(c);
                if (c == ']') inBracket = false;
                continue;
            }

            if (c == '\'' || c == '"')
            {
                inString = true;
                stringChar = c;
                if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
                continue;
            }

            if (c == '[')
            {
                inBracket = true;
                if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
                sb.Append(c);
                continue;
            }

            if (char.IsWhiteSpace(c) || c == ',' || c == '(' || c == ')' || c == ';' || c == '=')
            {
                if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
                if (!char.IsWhiteSpace(c)) tokens.Add(c.ToString());
            }
            else
            {
                sb.Append(c);
            }
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());
        return tokens;
    }
}

public sealed record InsertStatement : Statement
{
    public TableReference TargetTable { get; }
    public Statement? SelectQuery { get; }
    public List<string>? Columns { get; }
    public List<List<Expression>>? Values { get; }
    public OutputClause? Output { get; set; }
    public bool IsReplace { get; set; } = false;

    public InsertStatement(TableReference targetTable, Statement query)
    {
        TargetTable = targetTable;
        SelectQuery = query;
    }

    public InsertStatement(TableReference targetTable, List<string>? columns, Statement query)
    {
        TargetTable = targetTable;
        Columns = columns;
        SelectQuery = query;
    }

    public InsertStatement(TableReference targetTable, List<string>? columns, List<List<Expression>> values)
    {
        TargetTable = targetTable;
        Columns = columns;
        Values = values;
    }

    public override IEnumerable<string> GetSourceTables()
    {
        if (SelectQuery != null) return SelectQuery.GetSourceTables();
        return Enumerable.Empty<string>();
    }
}

public sealed record Assignment : AstNode
{
    public string ColumnName { get; }
    public Expression Value { get; }

    public Assignment(string columnName, Expression value)
    {
        ColumnName = columnName;
        Value = value;
    }
}

public sealed record UpdateStatement : Statement
{
    public TableReference TargetTable { get; }
    public List<Assignment> Assignments { get; }
    public Expression? WhereClause { get; }
    public OutputClause? Output { get; set; }

    public TableReference? FromTable { get; set; }
    public List<JoinClause>? Joins { get; set; }

    public UpdateStatement(TableReference targetTable, List<Assignment> assignments, Expression? whereClause)
    {
        TargetTable = targetTable;
        Assignments = assignments;
        WhereClause = whereClause;
    }
}

public sealed record DeleteStatement : Statement
{
    public TableReference TargetTable { get; }
    public Expression? WhereClause { get; }
    public OutputClause? Output { get; set; }

    public DeleteStatement(TableReference targetTable, Expression? whereClause)
    {
        TargetTable = targetTable;
        WhereClause = whereClause;
    }
}

public sealed record ReplayQuarantineStatement(TableReference QuarantineTable) : Statement;

public sealed record KillJobStatement(Expression JobIdExpr) : Statement;

public sealed record DropJobStatement(string Name, bool IfExists) : Statement;

// ── Schedules and notifications ───────────────────────────────────────────────
// Peer entities to JOB, owned by the Orchestrator. Design and rationale in
// docs/architecture/decisions/job_schedule_notification.md.

/// <summary>
/// Presentation and classification metadata shared by every catalog object. None of it is ever
/// referenced by a script, which is what lets the object's <c>Name</c> stay a stable identity while
/// what an operator reads stays freely editable.
/// </summary>
public sealed record CatalogObjectOptions
{
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    /// <summary>Free-form classification metadata. Never read by the scheduler.</summary>
    public Dictionary<string, string>? Options { get; init; }
}

/// <summary>
/// <c>CREATE [OR ALTER|OR REPLACE] SCHEDULE &lt;name&gt; ON '&lt;cron&gt;' [AT TIME ZONE '&lt;tz&gt;']
/// [WITH (...)]</c>
/// </summary>
public sealed record CreateScheduleStatement : Statement
{
    public required string Name { get; init; }
    public required string Cron { get; init; }
    /// <summary>Null resolves to the configured default at execution, and is then stored.</summary>
    public string? TimeZone { get; init; }
    public CatalogObjectOptions Metadata { get; init; } = new();
    public ObjectCreationMode Mode { get; init; } = ObjectCreationMode.Create;
    public override string ToSql() => AstSerializer.Format(this);
}

/// <summary>
/// <c>CREATE [OR ALTER|OR REPLACE] NOTIFICATION &lt;name&gt; USING &lt;connection&gt;
/// [TO '&lt;recipient&gt;'] [WITH (...)]</c>
/// </summary>
public sealed record CreateNotificationStatement : Statement
{
    public required string Name { get; init; }
    /// <summary>Connection alias. Resolved where dispatch happens; never a credential.</summary>
    public required string ConnectionName { get; init; }
    public string? Recipient { get; init; }
    public CatalogObjectOptions Metadata { get; init; } = new();
    public ObjectCreationMode Mode { get; init; } = ObjectCreationMode.Create;
    public override string ToSql() => AstSerializer.Format(this);
}

/// <summary>Which catalog object an <c>ALTER</c>/<c>DROP</c>/<c>ENABLE</c> names.</summary>
public enum CatalogObjectKind
{
    Schedule,
    Notification
}

/// <summary>
/// <c>ALTER SCHEDULE &lt;name&gt; SET CRON = '…' | SET TIME ZONE '…' | SET (…)</c> and
/// <c>ALTER NOTIFICATION &lt;name&gt; SET TO '…' | SET USING &lt;connection&gt; | SET (…)</c>.
/// A null property is absent from the statement and keeps its stored value.
/// </summary>
public sealed record AlterCatalogObjectStatement : Statement
{
    public required CatalogObjectKind Kind { get; init; }
    public required string Name { get; init; }
    public string? Cron { get; init; }
    public string? TimeZone { get; init; }
    public string? ConnectionName { get; init; }
    public string? Recipient { get; init; }
    public CatalogObjectOptions Metadata { get; init; } = new();
    public override string ToSql() => AstSerializer.Format(this);
}

/// <summary><c>DROP SCHEDULE|NOTIFICATION [IF EXISTS] &lt;name&gt;</c></summary>
public sealed record DropCatalogObjectStatement : Statement
{
    public required CatalogObjectKind Kind { get; init; }
    public required string Name { get; init; }
    public bool IfExists { get; init; }
    public override string ToSql() => AstSerializer.Format(this);
}

/// <summary><c>ENABLE|DISABLE SCHEDULE|NOTIFICATION &lt;name&gt;</c></summary>
public sealed record SetCatalogObjectEnabledStatement : Statement
{
    public required CatalogObjectKind Kind { get; init; }
    public required string Name { get; init; }
    public required bool IsEnabled { get; init; }
    public override string ToSql() => AstSerializer.Format(this);
}

/// <summary>Whether an <c>ALTER JOB</c> attachment adds or removes.</summary>
public enum JobAttachmentAction
{
    Add,
    Remove
}

/// <summary>
/// <c>ALTER JOB &lt;job&gt; ADD|REMOVE SCHEDULE &lt;name&gt;</c> and
/// <c>ALTER JOB &lt;job&gt; ADD|REMOVE NOTIFICATION &lt;name&gt; ON SUCCESS|FAILURE|COMPLETION</c>.
/// </summary>
/// <remarks>
/// Both directions are idempotent by contract: adding an attachment that exists and removing one
/// that does not are no-ops. An exported configuration script re-issues every attachment on replay,
/// so anything else would fail the import on its second run.
/// </remarks>
public sealed record AlterJobAttachmentStatement : Statement
{
    public required JobAttachmentAction Action { get; init; }
    public required CatalogObjectKind Kind { get; init; }
    public required string JobName { get; init; }
    public required string TargetName { get; init; }
    /// <summary>Required for a notification, meaningless for a schedule.</summary>
    public string? Trigger { get; init; }
    public override string ToSql() => AstSerializer.Format(this);
}

/// <summary>ENABLE JOB 'name'; — enables a disabled job in the scheduler.</summary>
public sealed record EnableJobStatement(string Name) : Statement
{
    public string? At { get; set; }
}

/// <summary>DISABLE JOB 'name'; — disables a job without deleting it.</summary>
public sealed record DisableJobStatement(string Name) : Statement
{
    public string? At { get; set; }
}

/// <summary>TRIGGER JOB 'name'; — immediately queues a one-off run of the job.</summary>
public sealed record TriggerJobStatement(string Name) : Statement
{
    public string? At { get; set; }
}

public sealed record TruncateTableStatement : Statement
{
    public TableReference TargetTable { get; }

    public TruncateTableStatement(TableReference targetTable)
    {
        TargetTable = targetTable;
    }
}

public enum MergeActionType
{
    UPDATE,
    INSERT,
    DELETE
}

public enum MergeSourceOrTarget { Target, Source }

public record MergeActionClause : AstNode
{
    public MergeActionType ActionType { get; init; }
    public Expression? Condition { get; init; }
    public List<Assignment>? UpdateAssignments { get; init; }
    public List<string>? InsertColumns { get; init; }
    public List<Expression>? InsertValues { get; init; }

    public MergeActionClause(MergeActionType actionType, Expression? condition)
    {
        ActionType = actionType;
        Condition = condition;
    }
}

public record MergeMatchedClause(MergeActionType ActionType, Expression? Condition) : MergeActionClause(ActionType, Condition);
public sealed record MergeUpdateClause : MergeMatchedClause
{
    public List<Assignment> Assignments { get; init; }
    public MergeUpdateClause(Expression? condition, List<Assignment> assignments) : base(MergeActionType.UPDATE, condition)
    {
        Assignments = assignments;
        UpdateAssignments = assignments;
    }
}
public sealed record MergeDeleteClause(Expression? Condition) : MergeMatchedClause(MergeActionType.DELETE, Condition)
{
}

public record MergeNotMatchedClause : MergeActionClause
{
    public MergeSourceOrTarget Option { get; init; }
    public MergeNotMatchedClause(Expression? condition, MergeSourceOrTarget option = MergeSourceOrTarget.Target)
        : base(MergeActionType.INSERT, condition)
    {
        Option = option;
    }
}

public sealed record MergeInsertClause : MergeNotMatchedClause
{
    public List<string>? Columns { get; init; }
    public List<Expression> Values { get; init; }

    public MergeInsertClause(Expression? condition, List<string>? columns, List<Expression> values, MergeSourceOrTarget option = MergeSourceOrTarget.Target)
        : base(condition, option)
    {
        Columns = columns;
        Values = values;

        // Initialize base class properties for engine compatibility
        InsertColumns = columns;
        InsertValues = values;
    }
}

public sealed record MergeStatement : Statement
{
    public TableReference TargetTable { get; init; }
    public string? TargetAlias { get; init; }
    public TableReference SourceTable { get; init; }
    public string? SourceAlias { get; init; }
    public Expression OnCondition { get; init; }
    public List<MergeMatchedClause> MatchedClauses { get; init; }
    public List<MergeNotMatchedClause> NotMatchedClauses { get; init; }
    public OutputClause? Output { get; set; }

    public MergeStatement(
        TableReference targetTable,
        string? targetAlias,
        TableReference sourceTable,
        string? sourceAlias,
        Expression onCondition,
        List<MergeMatchedClause> matchedClauses,
        List<MergeNotMatchedClause> notMatchedClauses,
        OutputClause? output = null)
    {
        TargetTable = targetTable;
        TargetAlias = targetAlias;
        SourceTable = sourceTable;
        SourceAlias = sourceAlias;
        OnCondition = onCondition;
        MatchedClauses = matchedClauses;
        NotMatchedClauses = notMatchedClauses;
        Output = output;
    }

    public override IEnumerable<string> GetSourceTables()
    {
        return SourceTable.GetSourceTables();
    }
}

public sealed record ForeignKeyReference : AstNode
{
    public TableReference Table { get; }
    public List<string> Columns { get; }
    public ForeignKeyReference(TableReference table, List<string> columns)
    {
        Table = table;
        Columns = columns;
    }
}

public abstract record TableConstraint : AstNode
{
    public string? ConstraintName { get; set; }
    public override abstract string ToSql();
}

public sealed record TablePrimaryKeyConstraint : TableConstraint
{
    public List<string> Columns { get; }
    public TablePrimaryKeyConstraint(List<string> columns) => Columns = columns;
    public override string ToSql() => AstSerializer.Format(this);
}

public sealed record TableUniqueConstraint : TableConstraint
{
    public List<string> Columns { get; }
    public TableUniqueConstraint(List<string> columns) => Columns = columns;
    public override string ToSql() => AstSerializer.Format(this);
}

public sealed record TableForeignKeyConstraint : TableConstraint
{
    public List<string> Columns { get; }
    public ForeignKeyReference Reference { get; }
    public TableForeignKeyConstraint(List<string> columns, ForeignKeyReference reference)
    {
        Columns = columns;
        Reference = reference;
    }
    public override string ToSql() => AstSerializer.Format(this);
}

public sealed record TableCheckConstraint : TableConstraint
{
    public Expression Expression { get; }
    public TableCheckConstraint(Expression expression) => Expression = expression;
    public override string ToSql() => AstSerializer.Format(this);
}

public sealed record ColumnDefinition : AstNode
{
    public string ColumnName { get; }
    public string DataType { get; }
    public bool IsIdentity { get; }
    public Expression? DefaultExpression { get; set; }
    public Dictionary<string, string> Metadata { get; }
    public string? Description => Metadata.TryGetValue("d", out var d) ? d : null;
    public bool IsPrimaryKey { get; set; }
    public bool IsUnique { get; set; }
    public bool IsNullable { get; set; } = true;
    public Expression? CheckConstraint { get; set; }
    public ForeignKeyReference? ForeignKey { get; set; }

    public ColumnDefinition(string columnName, string dataType, bool isIdentity, Expression? defaultExpression = null, Dictionary<string, string>? metadata = null)
    {
        ColumnName = columnName;
        DataType = dataType;
        IsIdentity = isIdentity;
        DefaultExpression = defaultExpression;
        Metadata = metadata ?? new(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record CreateTableStatement : Statement
{
    public TableReference TargetTable { get; }
    public bool IfNotExists { get; }
    public List<ColumnDefinition> Columns { get; }
    public List<TableConstraint> TableConstraints { get; } = new();
    /// <summary>True for <c>CREATE OR REPLACE TABLE</c>: drop any existing table first, then create.</summary>
    public bool OrReplace { get; init; }

    public CreateTableStatement(TableReference targetTable, bool ifNotExists, List<ColumnDefinition> columns)
    {
        TargetTable = targetTable;
        IfNotExists = ifNotExists;
        Columns = columns;
    }
}

public enum AlterTableActionType { ADD, DROP_COLUMN, RENAME_COLUMN }

public sealed record AlterTableStatement : Statement
{
    public TableReference TargetTable { get; }
    public AlterTableActionType Action { get; }
    public ColumnDefinition? NewColumn { get; }
    public string? ColumnToDelete { get; }
    public string? OldColumnName { get; }
    public string? NewColumnName { get; }

    public AlterTableStatement(TableReference targetTable, AlterTableActionType action, ColumnDefinition? newColumn = null, string? columnToDelete = null, string? oldColumnName = null, string? newColumnName = null)
    {
        TargetTable = targetTable;
        Action = action;
        NewColumn = newColumn;
        ColumnToDelete = columnToDelete;
        OldColumnName = oldColumnName;
        NewColumnName = newColumnName;
    }
}

public sealed record DropTableStatement : Statement
{
    public TableReference TargetTable { get; }
    public bool IfExists { get; }

    public DropTableStatement(TableReference targetTable, bool ifExists)
    {
        TargetTable = targetTable;
        IfExists = ifExists;
    }
}

public sealed record DropConnectionStatement : Statement
{
    public string ConnectionName { get; }
    public bool IfExists { get; }
    public DropConnectionStatement(string name, bool ifExists) { ConnectionName = name; IfExists = ifExists; }
}

/// <summary>
/// ALTER CONNECTION &lt;name&gt; [ON &lt;type&gt;(&lt;target&gt;)] [WITH(&lt;options&gt;)];
/// Modifies an existing connection. Previous options are preserved unless explicitly overridden.
/// </summary>
public sealed record AlterConnectionStatement : Statement
{
    public string ConnectionName { get; }
    /// <summary>New connector type — null means keep the existing type.</summary>
    public string? ConnectionType { get; }
    /// <summary>New target/connection-string expression — null means keep the existing one.</summary>
    public Expression? TargetExpression { get; }
    /// <summary>Options to merge into the existing connection's option set.</summary>
    public Dictionary<string, Expression>? Options { get; }

    public AlterConnectionStatement(string name, string? type, Expression? target, Dictionary<string, Expression>? options)
    {
        ConnectionName = name;
        ConnectionType = type;
        TargetExpression = target;
        Options = options;
    }
}

public enum ClearSessionMode { Current, Single, All, Stale }
public sealed record ClearSessionStatement(ClearSessionMode Mode = ClearSessionMode.Current, Expression? SessionId = null) : Statement
{
}

public sealed record ShowSessionsStatement(string? IntoTable = null) : Statement
{
}

public sealed record ShowLocksStatement(string? IntoTable = null) : Statement
{
}

public sealed record DropProcedureStatement : Statement
{
    public string ProcedureName { get; }
    public bool IfExists { get; }
    public DropProcedureStatement(string name, bool ifExists) { ProcedureName = name; IfExists = ifExists; }
}

public sealed record DropFunctionStatement : Statement
{
    public string FunctionName { get; }
    public bool IfExists { get; }
    public DropFunctionStatement(string name, bool ifExists) { FunctionName = name; IfExists = ifExists; }
}

public sealed record DropViewStatement : Statement
{
    public string ViewName { get; }
    public bool IfExists { get; }
    public DropViewStatement(string name, bool ifExists) { ViewName = name; IfExists = ifExists; }
}

public sealed record DropIndexStatement : Statement
{
    public string IndexName { get; }
    public TableReference? Table { get; }
    public bool IfExists { get; }
    public DropIndexStatement(string name, TableReference? table, bool ifExists) { IndexName = name; Table = table; IfExists = ifExists; }
}

public sealed record DeclareStatement : Statement
{
    public string VariableName { get; }
    public string DataType { get; }
    public Expression? InitialValue { get; }
    public bool IsSensitive { get; set; }
    public bool IsSecret { get; set; }
    public bool IsInput { get; set; }
    public bool IsOutput { get; set; }
    public bool IsRequired { get; set; }
    public Dictionary<string, string> Metadata { get; }
    public string? Description => Metadata.TryGetValue("d", out var d) ? d : null;

    public DeclareStatement(string name, string type, Expression? initialValue = null, Dictionary<string, string>? metadata = null)
    {
        VariableName = name;
        DataType = type;
        InitialValue = initialValue;
        Metadata = metadata ?? new(StringComparer.OrdinalIgnoreCase);
    }

    public DeclareStatement(string name, string type, Expression? initialValue, bool isSensitive, bool isInput, bool isOutput, bool isRequired = false, Dictionary<string, string>? metadata = null)
    {
        VariableName = name;
        DataType = type;
        InitialValue = initialValue;
        IsSensitive = isSensitive;
        IsInput = isInput;
        IsOutput = isOutput;
        IsRequired = isRequired;
        Metadata = metadata ?? new(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record DockerStatement : Statement
{
    public Expression ImageName { get; }
    public string? Alias { get; }
    public DockerStatement(Expression imageName, string? alias = null)
    {
        ImageName = imageName;
        Alias = alias;
    }
}

public sealed record RunScriptParameter(string Name, Expression Value, bool IsOutput);

public sealed record RunScriptStatement : Statement
{
    public Expression PathExpression { get; }
    public List<RunScriptParameter> Parameters { get; }

    public RunScriptStatement(Expression path, List<RunScriptParameter> parameters)
    {
        PathExpression = path;
        Parameters = parameters;
    }
}

public enum BundleSecretMode { None, Prompt, Literal }

public sealed record PublishBundleStatement(
    string BundleName,
    Expression SourcePath,
    string EntryPath,
    BundleSecretMode PasswordMode = BundleSecretMode.None,
    string? Password = null,
    string EncryptionMode = "MACHINE",
    string? KeyFile = null,
    string? Description = null) : Statement;

public sealed record ValidateBundleStatement(
    string BundleName,
    Expression SourcePath,
    string EntryPath,
    BundleSecretMode PasswordMode = BundleSecretMode.None,
    string? Password = null) : Statement;

public sealed record ExportScriptStatement(Expression SourcePath, Expression TargetPath) : Statement;

public sealed record SetVariableStatement(Expression Target, Expression Value) : Statement
{
    public string VariableName => Target switch
    {
        VariableExpression v => v.Name,
        IdentifierExpression i => i.Name,
        MemberAccessExpression m => m.ToSql(), // Handle nested assignments like @json.key
        _ => Target.ToSql()
    };
}

public sealed record BlockStatement : Statement
{
    public List<Statement> Statements { get; }

    public BlockStatement(List<Statement> statements)
    {
        Statements = statements;
    }
}

public sealed record WhileStatement : Statement
{
    public Expression Condition { get; }
    public Statement Body { get; }

    public WhileStatement(Expression condition, Statement body)
    {
        Condition = condition;
        Body = body;
    }
}

public sealed record ForStatement : Statement
{
    public string VariableName { get; }
    public Expression StartValue { get; }
    public Expression EndValue { get; }
    public Expression? StepValue { get; }
    public Statement Body { get; }
    public bool IsStartImplicit { get; init; }

    public ForStatement(string variableName, Expression startValue, Expression endValue, Expression? stepValue, Statement body)
    {
        VariableName = variableName;
        StartValue = startValue;
        EndValue = endValue;
        StepValue = stepValue;
        Body = body;
    }
}

public sealed record ForeachStatement : Statement
{
    public string VariableName { get; }
    public Expression ListExpression { get; }
    public Statement Body { get; }

    public ForeachStatement(string variableName, Expression listExpression, Statement body)
    {
        VariableName = variableName;
        ListExpression = listExpression;
        Body = body;
    }
}

public sealed record ElseIfClause : AstNode
{
    public Expression Condition { get; }
    public Statement Body { get; }

    public ElseIfClause(Expression condition, Statement body)
    {
        Condition = condition;
        Body = body;
    }
}

public sealed record IfStatement : Statement
{
    public Expression Condition { get; }
    public Statement IfBody { get; }
    public List<ElseIfClause>? ElseIfClauses { get; }
    public Statement? ElseBody { get; }

    public IfStatement(Expression condition, Statement ifBody, List<ElseIfClause>? elseIfClauses, Statement? elseBody)
    {
        Condition = condition;
        IfBody = ifBody;
        ElseIfClauses = elseIfClauses;
        ElseBody = elseBody;
    }
}

public sealed record PrintStatement(List<Expression> Arguments, Expression? ShowTimestamp = null, Expression? TimestampFormat = null) : Statement;

public sealed record FileOperationStatement : Statement
{
    public FileOpType Type { get; }
    public Expression Source { get; }
    public Expression? Destination { get; }
    public Expression? Overwrite { get; }
    public Expression? Password { get; }
    public Expression? KeyFile { get; }
    public Expression? PgpKey { get; }
    public Expression? DateSuffix { get; }
    public Expression? SuffixSeparator { get; }
    public bool DestinationIsDirectory { get; }
    public bool IfExists { get; set; }
    public string? ConnectionName { get; }

    public FileOperationStatement(FileOpType type, Expression source, Expression? destination = null, Expression? overwrite = null, Expression? password = null, Expression? keyFile = null, Expression? pgpKey = null, bool ifExists = false, string? connectionName = null, Expression? dateSuffix = null, Expression? suffixSeparator = null, bool destinationIsDirectory = false)
    {
        Type = type;
        Source = source;
        Destination = destination;
        Overwrite = overwrite;
        Password = password;
        KeyFile = keyFile;
        PgpKey = pgpKey;
        DateSuffix = dateSuffix;
        SuffixSeparator = suffixSeparator;
        DestinationIsDirectory = destinationIsDirectory;
        IfExists = ifExists;
        ConnectionName = connectionName;
    }
}

public sealed record DirectoryOperationStatement : Statement
{
    public DirectoryOpType Type { get; }
    public Expression Path { get; }
    public Expression? Destination { get; }
    public Expression? Overwrite { get; }
    public Expression? Recursive { get; }
    public Expression? Password { get; }
    public Expression? KeyFile { get; }
    public Expression? PgpKey { get; }
    public bool IfExists { get; set; }
    public string? ConnectionName { get; }

    public DirectoryOperationStatement(DirectoryOpType type, Expression path, Expression? destination = null, Expression? overwrite = null, Expression? recursive = null, Expression? password = null, Expression? keyFile = null, Expression? pgpKey = null, bool ifExists = false, string? connectionName = null)
    {
        Type = type;
        Path = path;
        Destination = destination;
        Overwrite = overwrite;
        Recursive = recursive;
        Password = password;
        KeyFile = keyFile;
        PgpKey = pgpKey;
        IfExists = ifExists;
        ConnectionName = connectionName;
    }
}


public sealed record WaitForFileStatement(Expression Path, Expression? Timeout = null, Expression? PollInterval = null) : Statement;

public sealed record ConvertFileEncodingStatement(Expression Source, Expression Destination, Expression FromEncoding, Expression ToEncoding, Expression? Overwrite = null) : Statement;

public sealed record SplitFileStatement(Expression Source, Expression DestinationDir, Expression LimitType, Expression LimitValue, Expression? Prefix = null, Expression? Overwrite = null) : Statement;

public sealed record MergeFilesStatement(Expression Source, Expression Destination, Expression? Header = null, Expression? Overwrite = null) : Statement;

public sealed record SyncDirectoryStatement(Expression Source, Expression Destination, Expression? DeleteExtra = null, Expression? Overwrite = null, Expression? Recursive = null) : Statement;

public sealed record VerifyFileIntegrityStatement(Expression Source, Expression? HashFile = null, Expression? ExpectedHash = null, Expression? Algorithm = null) : Statement;




public enum WaitType { Delay, Time, Until }

/// <summary>WAITFOR DELAY/TIME '...' — pauses execution.</summary>
public sealed record WaitForStatement(Expression expression, WaitType type = WaitType.Delay) : Statement
{
    public Expression Expression { get; } = expression;
    public WaitType Type { get; } = type;
}

public sealed record RaiseErrorStatement : Statement
{
    public Expression Message { get; }
    public Expression Severity { get; }
    public Expression? CodeLocation { get; }
    public List<Expression> Parameters { get; }

    public RaiseErrorStatement(Expression message, Expression severity, Expression? codeLocation = null, List<Expression>? parameters = null)
    {
        Message = message;
        Severity = severity;
        CodeLocation = codeLocation;
        Parameters = parameters ?? new List<Expression>();
    }
}

public sealed record AssertStatement(Expression Condition, Expression? Message = null) : Statement
{
}

/// <summary>
/// <c>ASSERT TABLE &lt;actual&gt; MATCHES &lt;expected&gt; [WITH (IGNORE_ORDER = TRUE, TOLERANCE = 0.001, IGNORE_COLUMNS = 'col1,col2', MESSAGE = '...')];</c>
/// Asserts that two tables (e.g. #temp tables produced during a test pipeline) have matching schema and data.
/// </summary>
public sealed record AssertTableStatement(
    string ActualTable,
    string ExpectedTable,
    bool IgnoreOrder = false,
    decimal? Tolerance = null,
    IReadOnlyList<string>? IgnoreColumns = null,
    Expression? Message = null,
    IReadOnlyDictionary<string, Expression>? Options = null) : Statement;

/// <summary>The run metric an <c>ASSERT JOB</c> predicate is measured against.</summary>
public enum JobMetricKind
{
    /// <summary>Rows processed by the run.</summary>
    RowCount,
    /// <summary>Fraction of NULLs in a named column (0..1), collected in-stream.</summary>
    NullPercent,
    /// <summary>Age of the newest observed value in a named timestamp column.</summary>
    Freshness,
    /// <summary>Fraction of validated rows removed by QUARANTINE actions (0..1).</summary>
    QuarantinePercent,
    /// <summary>Fraction of validated rows that failed a WARN rule (0..1).</summary>
    WarnPercent
}

/// <summary>
/// One <c>ASSERT JOB</c> predicate. Either a direct comparison against a literal
/// (<c>NULL_PERCENT(Email) &lt; 0.02</c>) or a tolerance band around the historical baseline
/// (<c>ROW_COUNT WITHIN 0.2 OF HISTORICAL</c>), in which case <see cref="Tolerance"/> is set and
/// <see cref="Op"/> is unused.
/// </summary>
public sealed record JobMetricPredicate(
    JobMetricKind Metric,
    string? ColumnName,
    CompareOp? Op,
    decimal? Bound,
    decimal? Tolerance,
    string? TargetName = null,
    RetentionInterval? IntervalBound = null,
    bool UsesSigma = false) : AstNode
{
    /// <summary>True for the <c>WITHIN &lt;frac&gt; OF HISTORICAL</c> form.</summary>
    public bool IsHistorical => Tolerance.HasValue;

    /// <summary>Renders the predicate as written, for diagnostics and alert payloads.</summary>
    public string Describe()
    {
        var metric = Metric switch
        {
            JobMetricKind.RowCount => "ROW_COUNT",
            JobMetricKind.NullPercent => $"NULL_PERCENT({FormatQualifiedColumn()})",
            JobMetricKind.Freshness => $"FRESHNESS({FormatQualifiedColumn()})",
            JobMetricKind.QuarantinePercent => "QUARANTINE_PERCENT",
            _ => "WARN_PERCENT"
        };
        if (IsHistorical) return UsesSigma
            ? $"{metric} WITHIN {Tolerance} SIGMA OF HISTORICAL"
            : $"{metric} WITHIN {Tolerance} OF HISTORICAL";
        var op = Op switch
        {
            CompareOp.GreaterOrEqual => ">=",
            CompareOp.LessOrEqual => "<=",
            CompareOp.Greater => ">",
            CompareOp.Less => "<",
            _ => "="
        };
        return IntervalBound != null
            ? $"{metric} {op} '{IntervalBound}'"
            : $"{metric} {op} {Bound}";
    }

    private string FormatQualifiedColumn() =>
        TargetName == null ? ColumnName ?? "" : $"{TargetName}.{ColumnName}";
}

/// <summary>
/// <c>ASSERT JOB &lt;name&gt; (&lt;predicates&gt;) [ON FAILURE NOTIFY &lt;notification&gt;]
/// [ON CRITICAL_FAILURE THROW]</c> — asserts on the run's own metrics, collected in-stream during
/// execution rather than by a post-run re-scan.
/// </summary>
public sealed record AssertJobStatement(
    string JobName,
    IReadOnlyList<JobMetricPredicate> Predicates,
    string? FailureNotification = null,
    bool ThrowOnCritical = false,
    /// <summary>Fails the run with a non-zero exit when any row triggered a WARN quality action.</summary>
    bool FailOnWarn = false) : Statement;

public sealed record ExpectedSchemaColumn
{
    public required string ColumnName { get; init; }
    public required string DataType { get; init; }
    public bool NotNull { get; init; }
}

/// <summary>
/// EXPECT SCHEMA target ( col type [NOT NULL] [, ...] ) [ON DRIFT WARN];
/// Validates that the actual schema of a #temp table or connection matches the declared columns.
/// Raises ExecutionException (or logs a warning with ON DRIFT WARN) when drift is detected.
/// </summary>
public sealed record ExpectSchemaStatement : Statement
{
    public required string Target { get; init; }
    public List<ExpectedSchemaColumn>? Columns { get; init; }
    public string? SchemaPath { get; init; }
    public bool WarnOnDrift { get; init; }
}

public sealed record ExecuteParameter(Expression Expression, string? Name = null, bool IsOutput = false, bool IsInput = false) : AstNode;

public sealed record ExecuteStatement : Statement
{
    public string ProcedureName { get; }
    public List<ExecuteParameter> Parameters { get; }

    public ExecuteStatement(string procedureName, List<ExecuteParameter> parameters)
    {
        ProcedureName = procedureName;
        Parameters = parameters;
    }
}

public sealed record ParallelStatement : Statement
{
    public BlockStatement Body { get; }
    public int ConcurrencyLimit { get; set; } = 0; // 0 means no limit (all tasks)

    public ParallelStatement(BlockStatement body, int concurrencyLimit = 0)
    {
        Body = body;
        ConcurrencyLimit = concurrencyLimit;
    }
}

public sealed record ParallelForStatement : Statement
{
    public string VariableName { get; }
    public Expression StartValue { get; }
    public Expression EndValue { get; }
    public Expression? StepValue { get; }
    public Statement Body { get; }
    public int ConcurrencyLimit { get; }
    public bool IsStartImplicit { get; init; }

    public ParallelForStatement(string variableName, Expression startValue, Expression endValue, Expression? stepValue, Statement body, int concurrencyLimit = 0)
    {
        VariableName = variableName;
        StartValue = startValue;
        EndValue = endValue;
        StepValue = stepValue;
        Body = body;
        ConcurrencyLimit = concurrencyLimit;
    }
}

public sealed record BulkInsertStatement : Statement
{
    public TableReference TargetTable { get; }
    public List<string>? Columns { get; }
    public string FilePath { get; }
    public Dictionary<string, Expression> Options { get; }

    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? DerivedFromDescriptions { get; set; }

    public BulkInsertStatement(TableReference targetTable, string filePath, Dictionary<string, Expression> options, List<string>? columns = null)
    {
        TargetTable = targetTable;
        FilePath = filePath;
        Options = options;
        Columns = columns;
    }

    public override IEnumerable<string> GetSourceTables()
    {
        return new[] { FilePath };
    }
}

public sealed record CreateProcedureStatement : Statement
{
    public string ProcedureName { get; }
    public List<ParameterDefinition> Parameters { get; }
    public Statement Body { get; }
    public ObjectCreationMode Mode { get; }

    public CreateProcedureStatement(string name, List<ParameterDefinition> parameters, Statement body, ObjectCreationMode mode = ObjectCreationMode.Create)
    {
        ProcedureName = name;
        Parameters = parameters;
        Body = body;
        Mode = mode;
    }
}

public sealed record CreateFunctionStatement : Statement
{
    public string FunctionName { get; }
    public List<ParameterDefinition> Parameters { get; }
    public string ReturnType { get; }
    public Statement Body { get; }
    public ObjectCreationMode Mode { get; }

    public CreateFunctionStatement(string name, List<ParameterDefinition> parameters, string returnType, Statement body, ObjectCreationMode mode = ObjectCreationMode.Create)
    {
        FunctionName = name;
        Parameters = parameters;
        ReturnType = returnType;
        Body = body;
        Mode = mode;
    }
}

public sealed record CreateViewStatement : Statement
{
    public string ViewName { get; }
    public Statement Query { get; }
    public ObjectCreationMode Mode { get; }

    public CreateViewStatement(string name, Statement query, ObjectCreationMode mode = ObjectCreationMode.Create)
    {
        ViewName = name;
        Query = query;
        Mode = mode;
    }
}

public sealed record ParameterDefinition : AstNode
{
    public string Name { get; }
    public string DataType { get; }

    public ParameterDefinition(string name, string dataType)
    {
        Name = name;
        DataType = dataType;
    }
}

public sealed record BeginTransactionStatement : Statement
{
    public string? Name { get; }
    public BeginTransactionStatement(string? name = null) => Name = name;
}

public sealed record CommitTransactionStatement : Statement
{
    public string? Name { get; }
    public CommitTransactionStatement(string? name = null) => Name = name;
}

public sealed record RollbackTransactionStatement : Statement
{
    public string? Name { get; }
    public RollbackTransactionStatement(string? name = null) => Name = name;
}

public sealed record ContinueStatement : Statement
{
}

public sealed record ThrowStatement : Statement
{
    public Expression? ErrorNumber { get; }
    public Expression? Message { get; }
    public Expression? State { get; }

    public ThrowStatement(Expression? errorNumber = null, Expression? message = null, Expression? state = null)
    {
        ErrorNumber = errorNumber;
        Message = message;
        State = state;
    }
}

public sealed record TryCatchStatement : Statement
{
    public Statement TryBody { get; }
    public Statement CatchBody { get; }

    public TryCatchStatement(Statement tryBody, Statement catchBody)
    {
        TryBody = tryBody;
        CatchBody = catchBody;
    }
}


public sealed record ReturnStatement : Statement
{
    public Expression? ReturnValue { get; }

    public ReturnStatement(Expression? returnValue = null)
    {
        ReturnValue = returnValue;
    }
}

public sealed record BreakStatement : Statement
{
}

/// <summary>Marks a batch boundary. The Evaluator runs each batch independently;
/// if one batch fails the next batch still executes.</summary>
public sealed record GoStatement(int Count = 1) : Statement;

public sealed record SectionLabelStatement(string LabelName) : Statement
{
    public bool IsTopLevel { get; set; }
}

public sealed record GotoStatement(string LabelName) : Statement;

/// <summary>Generates a 32-bit cryptographically secure JWT secret, encrypts it, 
/// and optionally updates the appsettings.json file.</summary>
public sealed record GenerateJwtSecretStatement : Statement
{
}


/// <summary>Base class for all expressions that return a value.</summary>
public abstract record Expression : AstNode
{
    public virtual IEnumerable<string> GetSourceTables() => Enumerable.Empty<string>();
    public virtual IEnumerable<string> GetSourceColumns() => Enumerable.Empty<string>();
}

public sealed record UnaryExpression : Expression
{
    public TokenType Operator { get; }
    public Expression Expression { get; }

    public UnaryExpression(TokenType op, Expression expr)
    {
        Operator = op;
        Expression = expr;
    }
    public override IEnumerable<string> GetSourceTables() => Expression.GetSourceTables();
    public override IEnumerable<string> GetSourceColumns() => Expression.GetSourceColumns();
}

public sealed record BinaryExpression : Expression
{
    public Expression Left { get; }
    public TokenType Operator { get; }
    public Expression Right { get; }

    public BinaryExpression(Expression left, TokenType op, Expression right)
    {
        Left = left;
        Operator = op;
        Right = right;
    }
    public override IEnumerable<string> GetSourceTables() => Left.GetSourceTables().Concat(Right.GetSourceTables()).Distinct(StringComparer.OrdinalIgnoreCase);
    public override IEnumerable<string> GetSourceColumns() => Left.GetSourceColumns().Concat(Right.GetSourceColumns()).Distinct(StringComparer.OrdinalIgnoreCase);
}

public sealed record LiteralExpression : Expression
{
    public object? Value { get; }
    public TokenType Type { get; }

    public LiteralExpression(object? value, TokenType type)
    {
        Value = value;
        Type = type;
    }
}

public sealed record IdentifierExpression : Expression
{
    public string Name { get; }

    public IdentifierExpression(string name)
    {
        Name = name;
    }

    public override IEnumerable<string> GetSourceColumns() => new[] { Name.Split('.').Last() };
    public override IEnumerable<string> GetSourceTables() => Name.Contains('.') ? new[] { Name.Split('.')[0] } : Enumerable.Empty<string>();
}

public sealed record MemberAccessExpression : Expression
{
    public Expression Expression { get; }
    public string MemberName { get; }

    public MemberAccessExpression(Expression expression, string memberName)
    {
        Expression = expression;
        MemberName = memberName;
    }

    public override IEnumerable<string> GetSourceTables() => Expression.GetSourceTables();
    public override IEnumerable<string> GetSourceColumns() => new[] { MemberName };
}

public sealed record SubqueryExpression : Expression
{
    public Statement Query { get; }

    public SubqueryExpression(Statement query)
    {
        Query = query;
    }
}

public sealed record VariableExpression(string Name) : Expression
{
    public string Name { get; } = Name;
}

public sealed record ParameterExpression(string Value, int? Index = null) : Expression
{
    public string Value { get; } = Value;
    public int? Index { get; } = Index;
}

public record FunctionCallExpression : Expression
{
    public string FunctionName { get; }
    public List<Expression> Arguments { get; }
    public bool IsDistinct { get; set; }
    public WindowClause? Window { get; set; }
    public string? WindowName { get; set; }
    public List<OrderByClause>? WithinGroupOrderBy { get; set; }
    public Expression? Filter { get; set; }
    public JsonTableSpec? JsonTable { get; set; }

    public FunctionCallExpression(string functionName, List<Expression> arguments)
    {
        FunctionName = functionName;
        Arguments = arguments;
    }
    public override IEnumerable<string> GetSourceTables() => Arguments.SelectMany(a => a.GetSourceTables()).Distinct(StringComparer.OrdinalIgnoreCase);
    public override IEnumerable<string> GetSourceColumns() => Arguments.SelectMany(a => a.GetSourceColumns()).Distinct(StringComparer.OrdinalIgnoreCase);
}

public sealed record JsonTableSpec(List<JsonTableColumnSpec> Columns);

public sealed record JsonTableColumnSpec(
    string Name,
    string? TypeName,
    Expression? Path,
    bool ForOrdinality = false,
    bool Exists = false,
    Expression? DefaultOnEmpty = null,
    Expression? DefaultOnError = null);

public sealed record ListExpression : Expression
{
    public List<Expression> Items { get; }

    public ListExpression(List<Expression> items)
    {
        Items = items;
    }
}

/// <summary>
/// A <c>*</c> projection carrying DuckDB/Snowflake star modifiers: <c>* EXCLUDE (cols)</c>,
/// <c>* REPLACE (expr AS col)</c>, and <c>* RENAME (col AS new)</c>. Expanded against the source
/// columns during column expansion. <see cref="Qualifier"/> is set for a qualified <c>t.*</c>.
/// </summary>
public sealed record StarExpression : Expression
{
    public string? Qualifier { get; }
    public List<string> Exclude { get; }
    public List<(string Column, Expression Value)> Replace { get; }
    public List<(string Column, string NewName)> Rename { get; }
    /// <summary>When set (from <c>COLUMNS('regex')</c>), expand to source columns whose name matches this regex.</summary>
    public string? Pattern { get; init; }

    public StarExpression(string? qualifier, List<string> exclude, List<(string, Expression)> replace, List<(string, string)> rename)
    {
        Qualifier = qualifier;
        Exclude = exclude;
        Replace = replace;
        Rename = rename;
    }
}

public sealed record IsNullExpression : Expression
{
    public Expression Expression { get; }
    public bool Not { get; }

    public IsNullExpression(Expression expression, bool isNot)
    {
        Expression = expression;
        Not = isNot;
    }
}

/// <summary>
/// Null-safe comparison. <c>a IS DISTINCT FROM b</c> treats NULL as an ordinary comparable value:
/// it is true when the operands differ (including exactly one being NULL) and false when they are
/// equal or both NULL. When <see cref="Not"/> is true the expression is <c>a IS NOT DISTINCT FROM b</c>
/// (null-safe equality), the logical negation. Never yields NULL.
/// </summary>
public sealed record IsDistinctFromExpression : Expression
{
    public Expression Left { get; }
    public Expression Right { get; }
    /// <summary>True for <c>IS NOT DISTINCT FROM</c> (null-safe equals); false for <c>IS DISTINCT FROM</c> (null-safe not-equals).</summary>
    public bool Not { get; }

    public IsDistinctFromExpression(Expression left, Expression right, bool not)
    {
        Left = left;
        Right = right;
        Not = not;
    }
}

/// <summary>EXPORT REPORT 'path.rptsql' FORMAT PDF|CSV|MARKDOWN TO 'output.pdf' [WITH (...)]</summary>
public sealed record ExportReportStatement(
    Expression ReportPath,
    string Format,
    Expression OutputPath,
    string? PdfMode = null,
    Expression? Host = null,
    Expression? BrowserPath = null) : Statement;

public sealed record ExportStatement : Statement
{
    public Expression Source { get; }
    public string TargetPath { get; }
    public Dictionary<string, string>? Options { get; }

    public ExportStatement(Expression source, string targetPath, Dictionary<string, string>? options)
    {
        Source = source;
        TargetPath = targetPath;
        Options = options;
    }
}

public sealed record HelpStatement : Statement
{
    public string? Topic { get; }
    public string? SubTopic { get; }

    public HelpStatement(string? topic, string? subTopic = null)
    {
        Topic = topic;
        SubTopic = subTopic;
    }
}

public sealed record RequireVersionStatement(string Operator, string Version) : Statement
{
}

public sealed record InExpression : Expression
{
    public Expression Left { get; }
    public Expression Right { get; }
    public bool IsNot { get; }
    public Statement? Subquery { get; }

    public InExpression(Expression left, Expression right, bool isNot, Statement? subquery = null)
    {
        Left = left;
        Right = right;
        IsNot = isNot;
        Subquery = subquery;
    }

    // Subquery RHS has its own scope; only Left's columns belong to the outer query.
    public override IEnumerable<string> GetSourceColumns() => Left.GetSourceColumns();
    public override IEnumerable<string> GetSourceTables() => Left.GetSourceTables();
}

public sealed record BetweenExpression : Expression
{
    public Expression Left { get; }
    public Expression Start { get; }
    public Expression End { get; }
    public bool IsNot { get; }

    public BetweenExpression(Expression left, Expression start, Expression end, bool isNot = false)
    {
        Left = left;
        Start = start;
        End = end;
        IsNot = isNot;
    }

    public override IEnumerable<string> GetSourceTables() => Left.GetSourceTables().Concat(Start.GetSourceTables()).Concat(End.GetSourceTables()).Distinct(StringComparer.OrdinalIgnoreCase);
    public override IEnumerable<string> GetSourceColumns() => Left.GetSourceColumns().Concat(Start.GetSourceColumns()).Concat(End.GetSourceColumns()).Distinct(StringComparer.OrdinalIgnoreCase);
}

public sealed record LineageStatement : Statement
{
    public TableReference? TargetTable { get; }
    public string? ColumnName { get; }
    public string? ExportPath { get; set; }
    public bool ExportAsOpenLineage { get; init; }
    public string? IntoTable { get; init; }

    public LineageStatement(TableReference? targetTable = null, string? columnName = null, string? exportPath = null, bool exportAsOpenLineage = false, string? intoTable = null)
    {
        TargetTable = targetTable;
        ColumnName = columnName;
        ExportPath = exportPath;
        ExportAsOpenLineage = exportAsOpenLineage;
        IntoTable = intoTable;
    }
}

/// <summary>
/// <c>SHOW DATA QUALITY RULES [FOR [TABLE] &lt;table&gt;] [COLUMN &lt;col&gt;] [INTO #t]</c> — lists the
/// <c>@expect</c>/<c>@fail</c> rules protecting each column, so a steward can answer "is this column
/// protected, and by what?" without reading the load script. Rules are steward-facing governance
/// metadata; this is the surface that makes them visible.
/// </summary>
public sealed record ShowDataQualityRulesStatement(
    TableReference? TargetTable = null,
    string? ColumnName = null,
    string? IntoTable = null) : Statement;

public sealed record ShowLineageHistoryForTableStatement : Statement
{
    public string TableName { get; init; } = string.Empty;
    public int? Limit { get; init; }
    public string? IntoTable { get; init; }
    public string? At { get; set; }
}

public sealed record ShowLineageHistoryForTagStatement : Statement
{
    public string TagKey { get; init; } = string.Empty;
    public string? TagValue { get; init; }
    public int? Limit { get; init; }
    public string? IntoTable { get; init; }
    public string? At { get; set; }
}

public sealed record ShowLineageHistoryForMissingTagsStatement : Statement
{
    public int? Limit { get; init; }
    public string? IntoTable { get; init; }
    public string? At { get; set; }
}

public sealed record ShowLineageHistoryForJobStatement : Statement
{
    public string JobName { get; init; } = string.Empty;
    public int? Limit { get; init; }
    public string? IntoTable { get; init; }
    public string? At { get; set; }
}

public sealed record ShowProtectedDataStatement : Statement
{
    public int? Limit { get; init; }
    public string? IntoTable { get; init; }
    public string? At { get; set; }
    public bool Suggestions { get; init; }
}

public sealed record ShowVariablesStatement : Statement
{
    public bool IsLocalOnly { get; init; }
    public string? IntoTable { get; init; }

    public ShowVariablesStatement(bool isLocalOnly = false, string? intoTable = null)
    {
        IsLocalOnly = isLocalOnly;
        IntoTable = intoTable;
    }
}

public sealed record ShowSafeZonesStatement : Statement
{
    public string? IntoTable { get; init; }

    public ShowSafeZonesStatement(string? intoTable = null)
    {
        IntoTable = intoTable;
    }
}

public sealed record EmailStatement : Statement
{
    public Expression To { get; }
    public Expression From { get; }
    public Expression Subject { get; }
    public Expression Body { get; }
    public Expression? ConnectionName { get; set; }
    public List<Expression>? Attachments { get; set; }
    public List<Expression>? Cc { get; set; }
    public List<Expression>? Bcc { get; set; }
    public bool IsSqlStyle { get; set; }

    public EmailStatement(Expression to, Expression from, Expression subject, Expression body, Expression? connectionName = null)
    {
        To = to;
        From = from;
        Subject = subject;
        Body = body;
        ConnectionName = connectionName;
    }
}

public sealed record LikeExpression : Expression
{
    public Expression Left { get; }
    public Expression Pattern { get; }
    public bool IsNot { get; }
    public Expression? EscapeChar { get; }
    public bool IsCaseInsensitive { get; }

    public LikeExpression(Expression left, Expression pattern, bool isNot = false, Expression? escapeChar = null, bool isCaseInsensitive = false)
    {
        Left = left;
        Pattern = pattern;
        IsNot = isNot;
        EscapeChar = escapeChar;
        IsCaseInsensitive = isCaseInsensitive;
    }
}

public sealed record ExistsExpression : Expression
{
    public Statement Subquery { get; }
    public bool IsNot { get; }

    public ExistsExpression(Statement subquery, bool isNot = false)
    {
        Subquery = subquery;
        IsNot = isNot;
    }
}

public sealed record CaseExpression : Expression
{
    public Expression? InputExpression { get; }
    public List<(Expression Condition, Expression Result)> WhenClauses { get; }
    public Expression? ElseResult { get; }

    public CaseExpression(List<(Expression Condition, Expression Result)> whenClauses, Expression? elseResult, Expression? inputExpression = null)
    {
        InputExpression = inputExpression;
        WhenClauses = whenClauses;
        ElseResult = elseResult;
    }

    public override IEnumerable<string> GetSourceTables()
    {
        var sources = WhenClauses.SelectMany(c => c.Condition.GetSourceTables().Concat(c.Result.GetSourceTables()));
        if (InputExpression != null) sources = sources.Concat(InputExpression.GetSourceTables());
        if (ElseResult != null) sources = sources.Concat(ElseResult.GetSourceTables());
        return sources.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public override IEnumerable<string> GetSourceColumns()
    {
        var columns = WhenClauses.SelectMany(c => c.Condition.GetSourceColumns().Concat(c.Result.GetSourceColumns()));
        if (InputExpression != null) columns = columns.Concat(InputExpression.GetSourceColumns());
        if (ElseResult != null) columns = columns.Concat(ElseResult.GetSourceColumns());
        return columns.Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
public sealed record AtTimeZoneExpression : Expression
{
    public Expression Left { get; }
    public Expression TimeZone { get; }

    public AtTimeZoneExpression(Expression left, Expression timeZone)
    {
        Left = left;
        TimeZone = timeZone;
    }

    public override IEnumerable<string> GetSourceTables() => Left.GetSourceTables();
    public override IEnumerable<string> GetSourceColumns() => Left.GetSourceColumns();
}

public sealed record SubstringExpression : FunctionCallExpression
{
    public Expression String { get; }
    public Expression Start { get; }
    public Expression? Length { get; }

    public SubstringExpression(Expression str, Expression start, Expression? length = null)
        : base("SUBSTRING", new List<Expression> { str, start, length ?? new LiteralExpression(null, TokenType.NULL) })
    {
        String = str;
        Start = start;
        Length = length;
    }

    public override IEnumerable<string> GetSourceTables() => String.GetSourceTables();
    public override IEnumerable<string> GetSourceColumns() => String.GetSourceColumns();
}

public sealed record GenerateRule(string ColumnName, string Rule) : AstNode;

public sealed record GenerateStatement(Expression RowCount, TableReference Target, List<GenerateRule> Rules, Dictionary<string, Expression>? Options = null) : Statement
{
    public override IEnumerable<string> GetSourceTables() => Enumerable.Empty<string>();
}

public sealed record GenerateCalendarStatement(Expression StartDate, Expression EndDate, TableReference Target) : Statement
{
    public override IEnumerable<string> GetSourceTables() => Enumerable.Empty<string>();
}

public sealed record CompareDatasetsStatement(TableReference SourceTable, TableReference BaselineTable, List<string> KeyColumns, List<string>? ExcludeColumns, TableReference TargetTable) : Statement
{
    public override IEnumerable<string> GetSourceTables() => new[] { SourceTable.TableName, BaselineTable.TableName };
}

public sealed record TransformStatement(TableReference TargetTable, TableReference? SourceTable, string Algorithm, Dictionary<string, Expression> Options) : Statement
{
    public override IEnumerable<string> GetSourceTables() => SourceTable != null ? new[] { SourceTable.TableName } : Enumerable.Empty<string>();
    public override string? GetCreatedTable() => TargetTable.TableName;
}



public sealed record PositionExpression(Expression substring, Expression str) : FunctionCallExpression("POSITION", new List<Expression> { substring, str })
{
    public Expression Substring { get; } = substring;
    public Expression String { get; } = str;

    public override IEnumerable<string> GetSourceTables() => String.GetSourceTables().Concat(Substring.GetSourceTables());
    public override IEnumerable<string> GetSourceColumns() => String.GetSourceColumns().Concat(Substring.GetSourceColumns());
}

public sealed record ExtractExpression(string field, Expression source) : FunctionCallExpression("EXTRACT", new List<Expression> { new LiteralExpression(field, TokenType.IDENTIFIER), source })
{
    public string Field { get; } = field;
    public Expression Source { get; } = source;

    public override IEnumerable<string> GetSourceTables() => Source.GetSourceTables();
    public override IEnumerable<string> GetSourceColumns() => Source.GetSourceColumns();
}

public sealed record OverlayExpression(Expression str, Expression overlay, Expression start, Expression? length = null) : FunctionCallExpression("OVERLAY", new List<Expression> { str, overlay, start, length ?? new LiteralExpression(null, TokenType.NULL) })
{
    public Expression String { get; } = str;
    public Expression Overlay { get; } = overlay;
    public Expression Start { get; } = start;
    public Expression? Length { get; } = length;

    public override IEnumerable<string> GetSourceTables() => String.GetSourceTables().Concat(Overlay.GetSourceTables());
    public override IEnumerable<string> GetSourceColumns() => String.GetSourceColumns().Concat(Overlay.GetSourceColumns());
}

public enum TrimType { BOTH, LEADING, TRAILING }

public sealed record TrimExpression(TrimType type, Expression? characters, Expression str) : FunctionCallExpression("TRIM", new List<Expression> { new LiteralExpression(type.ToString(), TokenType.IDENTIFIER), characters ?? new LiteralExpression(null, TokenType.NULL), str })
{
    public TrimType Type { get; } = type;
    public Expression? Characters { get; } = characters;
    public Expression String { get; } = str;

    public override IEnumerable<string> GetSourceTables() => String.GetSourceTables().Concat(Characters?.GetSourceTables() ?? Enumerable.Empty<string>());
    public override IEnumerable<string> GetSourceColumns() => String.GetSourceColumns().Concat(Characters?.GetSourceColumns() ?? Enumerable.Empty<string>());
}

public enum WindowFrameType { ROWS, RANGE, GROUPS }
public enum WindowFrameBoundType { PRECEDING, FOLLOWING, CURRENT_ROW, UNBOUNDED_PRECEDING, UNBOUNDED_FOLLOWING }
public enum WindowFrameExclusion { NoOthers, CurrentRow, Group, Ties }

public sealed record WindowFrame : AstNode
{
    public WindowFrameType Type { get; }
    public WindowFrameBoundType StartBound { get; }
    public Expression? StartValue { get; }
    public WindowFrameBoundType? EndBound { get; }
    public Expression? EndValue { get; }
    public WindowFrameExclusion Exclusion { get; init; }

    public WindowFrame(WindowFrameType type, WindowFrameBoundType startBound, Expression? startValue = null, WindowFrameBoundType? endBound = null, Expression? endValue = null, WindowFrameExclusion exclusion = WindowFrameExclusion.NoOthers)
    {
        Type = type;
        StartBound = startBound;
        StartValue = startValue;
        EndBound = endBound;
        EndValue = endValue;
        Exclusion = exclusion;
    }
}


public sealed record WindowClause : AstNode
{
    public string? BaseName { get; init; }
    public List<Expression> PartitionBy { get; }
    public List<OrderByClause> OrderBy { get; }
    public WindowFrame? Frame { get; set; }

    public WindowClause(List<Expression> partitionBy, List<OrderByClause> orderBy, WindowFrame? frame = null)
    {
        PartitionBy = partitionBy;
        OrderBy = orderBy;
        Frame = frame;
    }
}

public sealed record CreateIndexStatement : Statement
{
    public string IndexName { get; }
    public TableReference TargetTable { get; }
    public List<string> Columns { get; }
    public bool IsUnique { get; }

    public CreateIndexStatement(string indexName, TableReference targetTable, List<string> columns, bool isUnique = false)
    {
        IndexName = indexName;
        TargetTable = targetTable;
        Columns = columns;
        IsUnique = isUnique;
    }
}

public sealed record ExplainStatement : Statement
{
    public Statement Query { get; }
    public bool IsAnalyze { get; init; }
    public TableReference? IntoTable { get; init; }

    public ExplainStatement(Statement query, bool isAnalyze = false, TableReference? intoTable = null)
    {
        Query = query;
        IsAnalyze = isAnalyze;
        IntoTable = intoTable;
    }
}

public enum FileOpType { Copy, Move, Rename, Delete, Compress, Decompress, Encrypt, Decrypt }
public enum DirectoryOpType { Create, Delete, Rename, Move, Copy, DeleteContents, Compress, Decompress, Encrypt, Decrypt }

public enum FileTransferType { Send, Receive }

public sealed record FileTransferStatement : Statement
{
    public FileTransferType Type { get; set; }
    public Expression LocalPath { get; set; } = null!;
    public string ConnectionName { get; set; } = "";
    public Expression RemotePath { get; set; } = null!;
    public Expression? Overwrite { get; set; }
    public bool IsSqlStyle { get; set; }
}

public enum DockerAction { Start, Stop, Pause, Resume, Close }

public enum DockerTargetMode { Single, LastStarted, All }

public sealed record DockerActionStatement(DockerAction action, string? alias = null, DockerTargetMode targetMode = DockerTargetMode.Single) : Statement
{
    public DockerAction Action { get; } = action;
    public string? Alias { get; } = alias;
    public DockerTargetMode TargetMode { get; } = targetMode;
}

public sealed record CreateJobStatement : Statement
{
    public string JobName { get; }
    public JobTargetKind TargetKind { get; }
    public string TargetPath { get; }
    /// <summary>Null means the option was omitted. CREATE applies the catalog default; OR ALTER preserves it.</summary>
    public int? MaxRetries { get; }
    /// <summary>Null means the option was omitted. CREATE applies the catalog default; OR ALTER preserves it.</summary>
    public int? RetryDelaySeconds { get; }
    public CatalogObjectOptions Metadata { get; }
    public ObjectCreationMode Mode { get; }

    public CreateJobStatement(
        string jobName,
        JobTargetKind targetKind,
        string targetPath,
        int? maxRetries = null,
        int? retryDelaySeconds = null,
        CatalogObjectOptions? metadata = null,
        ObjectCreationMode mode = ObjectCreationMode.Create)
    {
        JobName = jobName;
        TargetKind = targetKind;
        TargetPath = targetPath;
        MaxRetries = maxRetries;
        RetryDelaySeconds = retryDelaySeconds;
        Metadata = metadata ?? new CatalogObjectOptions();
        Mode = mode;
    }

    public override string ToSql() => AstSerializer.Format(this);
}

/// <summary>ALTER JOB &lt;name&gt; SET TARGET = '…' | SET (job options).</summary>
public sealed record AlterJobStatement : Statement
{
    public string JobName { get; }
    public string? TargetPath { get; }
    public int? MaxRetries { get; }
    public int? RetryDelaySeconds { get; }
    public CatalogObjectOptions Metadata { get; }

    public AlterJobStatement(
        string jobName,
        string? targetPath,
        int? maxRetries,
        int? retryDelaySeconds,
        CatalogObjectOptions? metadata = null)
    {
        JobName = jobName;
        TargetPath = targetPath;
        MaxRetries = maxRetries;
        RetryDelaySeconds = retryDelaySeconds;
        Metadata = metadata ?? new CatalogObjectOptions();
    }

    public override string ToSql() => AstSerializer.Format(this);
}

public sealed record ScheduleInfo : AstNode
{
    public int Interval { get; }
    public string Unit { get; } // SECOND, MINUTE, HOUR, DAY
    public string? AtTime { get; }

    public ScheduleInfo(int interval, string unit, string? atTime = null)
    {
        Interval = interval;
        Unit = unit;
        AtTime = atTime;
    }
}

public sealed record ShowJobHistoryStatement : Statement
{
    public string? JobName { get; }
    public string? IntoTable { get; set; }
    public string? At { get; set; }
    public ShowJobHistoryStatement(string? jobName = null) { JobName = jobName; }
}

public sealed record ShowHostMetricsStatement : Statement
{
    public string? NodeId { get; }
    public string? IntoTable { get; set; }
    public ShowHostMetricsStatement(string? nodeId = null) { NodeId = nodeId; }
}

public sealed record ShowJobStateStatement : Statement
{
    public string? JobName { get; }
    public string? IntoTable { get; set; }
    public ShowJobStateStatement(string? jobName = null) { JobName = jobName; }
}

public sealed record ShowJobsStatement : Statement
{
    public string? IntoTable { get; set; }
    public string? At { get; set; }
}

public sealed record ShowVersionStatement : Statement
{
    public string? IntoTable { get; init; }
}

public sealed record ShowConnectionsStatement : Statement
{
    public string? IntoTable { get; set; }
}

public sealed record ShowConnectionConfigStatement : Statement
{
    public string ConnectionName { get; }
    public string? IntoTable { get; set; }
    public ShowConnectionConfigStatement(string connectionName) { ConnectionName = connectionName; }
}

/// <summary>
/// TEST CONNECTION &lt;alias&gt; — actively runs a governed, layered diagnostic (DNS → TCP → TLS)
/// against a catalog connection and reports a plain-English troubleshooting result.
/// </summary>
public sealed record TestConnectionStatement(string ConnectionName, string? IntoTable = null) : Statement;

public sealed record ShowTablesStatement : Statement
{
    public string? ConnectionName { get; }
    public string? IntoTable { get; set; }
    public ShowTablesStatement(string? connectionName = null) { ConnectionName = connectionName; }
}

public sealed record ShowViewsStatement : Statement
{
    public string? IntoTable { get; init; }
}

public sealed record LintStatement : Statement
{
    public string? ScriptPath { get; }

    public LintStatement(string? scriptPath = null)
    {
        ScriptPath = scriptPath;
    }
}

/// <summary>A single variable assignment inside a CREATE SETS block.</summary>
public sealed record SetsAssignment
{
    public string VariableName { get; }
    public Expression Value { get; }
    public SetsAssignment(string variableName, Expression value) { VariableName = variableName; Value = value; }
}

/// <summary>CREATE SETS !&lt;name&gt; BEGIN @var = val, ... [SET WITH_PROMPT ON;] END</summary>
public sealed record CreateSetsStatement : Statement
{
    public string Name { get; }
    public List<SetsAssignment> Assignments { get; }
    public bool WithPrompt { get; }

    public CreateSetsStatement(string name, List<SetsAssignment> assignments, bool withPrompt)
    {
        Name = name;
        Assignments = assignments;
        WithPrompt = withPrompt;
    }
}

/// <summary>
/// INSERT/UPDATE TAG FOR TABLE &lt;table&gt; [COLUMN &lt;col&gt;] (key = expr, ...) explicitly seeds
/// table-/column-level metadata (tags) into the lineage tracker. Table/column names are
/// expressions so they may be variables (e.g. @r.tbl in a FOR loop) or static identifiers.
/// </summary>
public sealed record CreateTagStatement : Statement
{
    public Expression TableName { get; }
    public Expression? ColumnName { get; }
    public Dictionary<string, Expression> Tags { get; }

    public CreateTagStatement(Expression tableName, Expression? columnName, Dictionary<string, Expression> tags)
    {
        TableName = tableName;
        ColumnName = columnName;
        Tags = tags;
    }
}

/// <summary>
/// DELETE TAG FOR TABLE &lt;table&gt; [COLUMN &lt;col&gt;] (key, ...) removes explicit
/// table-/column-level metadata from the lineage tracker.
/// </summary>
public sealed record DeleteTagStatement : Statement
{
    public Expression TableName { get; }
    public Expression? ColumnName { get; }
    public IReadOnlyList<string> TagNames { get; }

    public DeleteTagStatement(Expression tableName, Expression? columnName, IReadOnlyList<string> tagNames)
    {
        TableName = tableName;
        ColumnName = columnName;
        TagNames = tagNames;
    }
}

/// <summary>
/// INSERT LINEAGE FOR TABLE &lt;table&gt; FROM &lt;source&gt; imports lineage from an OpenLineage
/// JSON document (file path or inline JSON string), mirroring EXPORT LINEAGE AS OPENLINEAGE.
/// </summary>
public sealed record CreateLineageStatement : Statement
{
    public Expression TableName { get; }
    public Expression Source { get; }

    public CreateLineageStatement(Expression tableName, Expression source)
    {
        TableName = tableName;
        Source = source;
    }
}

/// <summary>
/// DELETE LINEAGE FOR TABLE &lt;table&gt; removes imported lineage records for the target table.
/// Auto-captured lineage remains immutable.
/// </summary>
public sealed record DeleteLineageStatement : Statement
{
    public Expression TableName { get; }

    public DeleteLineageStatement(Expression tableName)
    {
        TableName = tableName;
    }
}

/// <summary>DROP SETS [IF EXISTS] !&lt;name&gt;</summary>
public sealed record DropSetsStatement : Statement
{
    public string Name { get; }
    public bool IfExists { get; }

    public DropSetsStatement(string name, bool ifExists) { Name = name; IfExists = ifExists; }
}

/// <summary>USE SETS !<name></summary>
public sealed record UseSetsStatement : Statement
{
    public string Name { get; }
    public UseSetsStatement(string name) { Name = name; }
}

/// <summary>USE PASSWORD = 'password' or USE PASSWORD PROMPT</summary>
public sealed record UsePasswordStatement : Statement
{
    public string? Password { get; }
    public bool Prompt { get; }
    public UsePasswordStatement(string password) { Password = password; }
    public UsePasswordStatement(bool prompt) { Prompt = prompt; }

    public string ToSql(bool mask) => Prompt
        ? "USE PASSWORD PROMPT;"
        : $"USE PASSWORD = '{(mask ? "********" : (Password ?? "").Replace("'", "''"))}';";
    public override string ToSql() => AstSerializer.Format(this); // Always masked in serialization
}

public sealed record ShowPublishedBundlesStatement : Statement
{
    public bool IsAlias { get; set; } = false;
    public string? IntoTable { get; set; }
    public string? At { get; set; }
}

public sealed record ShowBundleVersionsStatement(string BundleName) : Statement
{
    public string? IntoTable { get; set; }
    public string? At { get; set; }
}

public sealed record ShowBundleFilesStatement(string BundleName, int Version) : Statement
{
    public string? IntoTable { get; set; }
    public string? At { get; set; }
}

public sealed record ShowBundleDependenciesStatement(string BundleName, int Version) : Statement
{
    public string? IntoTable { get; set; }
    public string? At { get; set; }
}

/// <summary>SET SHOW_SECRETS ON/OFF (alias: SET SHOW_PASSWORD)</summary>
public sealed record SetShowPasswordStatement(bool Enabled) : Statement
{
}

/// <summary>SET ALLOW_PLAINTEXT_SECRETS ON/OFF</summary>
public sealed record SetAllowPlaintextSecretsStatement(bool Enabled) : Statement
{
}

/// <summary>SET NO_SAVE_SENSITIVE ON/OFF</summary>
public sealed record SetNoSaveSensitiveStatement(bool Enabled) : Statement
{
}

/// <summary>SET NO_SAVE_CONNECTION ON/OFF</summary>
public sealed record SetNoSaveConnectionStatement(bool Enabled) : Statement
{
}

/// <summary>SET CONNECTION_ENCRYPTION ON/OFF</summary>
public sealed record SetConnectionEncryptionStatement(bool Enabled) : Statement
{
}

/// <summary>SET WEEK_START_DAY = 'Monday' — configures the start-of-week day for RELDATE W/WS/WE anchors.</summary>
public sealed record SetWeekStartDayStatement(string DayName) : Statement
{
}

/// <summary>SET SCRIPT_HASH_POLICY = 'Warn'|'Block' — controls behaviour when a script's hash differs from the pinned value.</summary>
public sealed record SetScriptHashPolicyStatement(string Policy) : Statement
{
}

public enum SecurityOverride
{
    FileTypeAccess,
    LargeFileCount,
    DeepRecursion,
    LargeStringResults,
    FileTypeExtension
}

/// <summary>SET ALLOW_... ON/OFF or SET ALLOW_... = value</summary>
public sealed record SetSecurityOverrideStatement(SecurityOverride Override, bool Enabled, Expression? Value = null) : Statement
{
    public override string ToSql() => AstSerializer.Format(this);
}

// ── Portal admin statements (Phase 10) ────────────────────────────────────
// These are only valid inside an EXECUTE portal BEGIN…END block targeting a
public sealed record CreatePortalToolStatement(string ToolName, string ToolType, Dictionary<string, Expression>? Options = null, ObjectCreationMode Mode = ObjectCreationMode.Create) : Statement;
public sealed record DropPortalToolStatement(string ToolName, bool IfExists = false) : Statement;
// PORTAL connection. The PortalConnector translates them into REST calls.

public sealed record CreatePortalUserStatement(
    string Username, string Email, Expression? Password,
    string Role, string? FirstName, string? LastName, string? Provider = null) : Statement;

public sealed record AlterPortalUserStatement(
    string Username,
    string? NewRole,
    string? NewEmail,
    bool? SetActive,        // true = ENABLE, false = DISABLE
    Expression? NewPassword) : Statement;

public sealed record DropPortalUserStatement(string Username, bool Cascade) : Statement;

public sealed record CreatePortalGroupStatement(string Name, string? Description, string? Provider = null, string? AdGroup = null) : Statement;

public sealed record DropPortalGroupStatement(string Name, bool Cascade) : Statement;

public sealed record AddUserToPortalGroupStatement(string Username, string GroupName) : Statement;

// CreatePortalSmtpConnectionStatement and DropPortalSmtpConnectionStatement were removed: SMTP is
// an ordinary connector, so it uses CreateConnectionStatement/DropConnectionStatement like every
// other type. An EXECUTE <portal> BEGIN ... END block routes those to the governed catalog.

public sealed record CreatePortalFolderStatement(string Path, string? CatalogOwner = null) : Statement;

public sealed record AlterPortalFolderStatement(string Path, string? NewName, string? NewParentPath) : Statement;

public sealed record DropPortalFolderStatement(string Path, bool Cascade) : Statement;

public enum PortalFolderPermission { Read, Execute, Manage }

public sealed record GrantPortalPermissionStatement(
    string FolderPath, string GroupName, PortalFolderPermission Permission) : Statement;

public sealed record RevokePortalPermissionStatement(
    string FolderPath, string GroupName, PortalFolderPermission Permission) : Statement;

public enum PortalDatasetPermission { Viewer, Refresh, Editor, Owner }

public sealed record AlterPortalDatasetStatement(
    string DatasetName, string FolderPath, string? AccessLevel, string? Ttl) : Statement;

public sealed record RefreshPortalDatasetStatement(string DatasetName, string FolderPath) : Statement;

public sealed record DropPortalDatasetStatement(string DatasetName, string FolderPath) : Statement;

public sealed record GrantPortalDatasetPermissionStatement(
    string DatasetName, string FolderPath, string GroupName, PortalDatasetPermission Permission) : Statement;

public sealed record RevokePortalDatasetPermissionStatement(
    string DatasetName, string FolderPath, string GroupName, PortalDatasetPermission Permission) : Statement;

public sealed record PublishPortalReportStatement(
    string ReportName, string ScriptPath, string FolderPath, string? Description, string? CatalogOwner = null) : Statement;

public sealed record AlterPortalReportStatement(
    string ReportName, string? NewFolder, string? NewDescription) : Statement;

public sealed record DropPortalReportStatement(string ReportName, bool Cascade) : Statement;

public sealed record FavoritePortalReportStatement(string ReportName, string? Username) : Statement;

public sealed record UnfavoritePortalReportStatement(string ReportName, string? Username) : Statement;

public sealed record CreatePortalShareLinkStatement(string Name, string ReportName, string? ExpiresAt, string? IntoTable = null) : Statement;

public sealed record RevokePortalShareLinkStatement(string Name, string? ReportName = null) : Statement;

public sealed record CreatePortalEmbedTokenStatement(string Name, string ReportName, string? ExpiresAt, string? IntoTable = null) : Statement;

public sealed record RevokePortalEmbedTokenStatement(string Name, string? ReportName = null) : Statement;

public sealed record CreatePortalSavedViewStatement(string ReportName, string Name, IReadOnlyList<SubscriptionParameter> Parameters, bool IsDefault, string? IntoTable = null) : Statement;

public sealed record DropPortalSavedViewStatement(string ReportName, string Name) : Statement;

public sealed record CreatePortalAlertStatement(
    string Name,
    string ReportName,
    string VisualName,
    string Operator,
    decimal Threshold,
    CatalogObjectOptions Metadata,
    ObjectCreationMode Mode = ObjectCreationMode.Create) : Statement;

public enum PortalAlertAttachmentAction
{
    Add,
    Remove
}

public sealed record PortalAlertNotificationReference(string OrchestratorAlias, string NotificationName)
{
    public override string ToString() => $"{OrchestratorAlias}.{NotificationName}";
}

public sealed record AlterPortalAlertNotificationStatement(
    string AlertName,
    PortalAlertAttachmentAction Action,
    PortalAlertNotificationReference Notification) : Statement;

public sealed record AlterPortalAlertStatement(
    string Name,
    CatalogObjectOptions Metadata) : Statement;

public sealed record DropPortalAlertStatement(string Name, bool IfExists = false) : Statement;

public sealed record SetPortalAlertEnabledStatement(string Name, bool IsEnabled) : Statement;

public sealed record CreatePortalRefreshJobStatement(
    string ReportName, string Schedule, string OrchestratorAlias) : Statement;

public sealed record RefreshPortalReportStatement(string ReportName) : Statement;

public sealed record DropPortalRefreshJobStatement(string ReportName) : Statement;

public sealed record DropPortalSnapshotStatement(string ReportName) : Statement;

public sealed record RebuildPortalSnapshotStatement(string ReportName) : Statement;

public enum PortalSubscriptionFormat { Pdf, Csv, Both }

/// <summary>A named parameter binding passed to a subscription's report script.</summary>
public sealed record SubscriptionParameter(string Name, string Value);

public sealed record CreatePortalSubscriptionStatement(
    string ReportPath,
    string Recipient,        // username or group name
    bool IsGroup,
    string? Schedule,
    bool OnRefresh,
    PortalSubscriptionFormat Format,
    string SmtpAlias,
    string? Name,
    IReadOnlyList<SubscriptionParameter> Parameters,
    bool IsActive = true) : Statement;

/// <summary>
/// ALTER SUBSCRIPTION &lt;id&gt; SET ...
/// Parameters: null = leave unchanged; empty list = clear all parameters.
/// </summary>
public sealed record AlterPortalSubscriptionStatement(
    int SubscriptionId,
    string? NewSchedule,
    bool? SetActive,
    PortalSubscriptionFormat? NewFormat,
    string? NewSmtpAlias,
    IReadOnlyList<SubscriptionParameter>? Parameters) : Statement;

public sealed record DropPortalSubscriptionStatement(int SubscriptionId) : Statement;

public sealed record DisconnectPortalUserStatement(string Username) : Statement;

public sealed record RevokePortalTokensStatement(string Username) : Statement;

public sealed record RestartPortalStatement : Statement;

/// <summary>EXPORT PORTAL CONFIGURATION TO '&lt;file&gt;' — admin-only: writes the portal's
/// declarative configuration as a replayable bootstrap script (secrets excluded; the portal
/// emits placeholders and an export summary).</summary>
public sealed record ExportPortalConfigurationStatement(string TargetPath) : Statement;

public sealed record ShutdownPortalStatement : Statement;

public sealed record ShowPortalUsersStatement : Statement;

public sealed record ShowPortalReportsStatement(string? FolderPath, string? IntoTable = null) : Statement;

public sealed record ShowPortalReportStatement(string ReportName, string? IntoTable = null) : Statement;

public sealed record ShowPortalReportHistoryStatement(string ReportName, string? IntoTable = null) : Statement;

public sealed record ShowPortalReportDependenciesStatement(string ReportName, string? IntoTable = null) : Statement;

public sealed record ShowPortalShareLinksStatement(string ReportName, string? IntoTable = null) : Statement;

public sealed record ShowPortalEmbedTokensStatement(string ReportName, string? IntoTable = null) : Statement;

public sealed record ShowPortalSavedViewsStatement(string ReportName, string? IntoTable = null) : Statement;

public sealed record ShowPortalAlertsStatement(string ReportName, string? IntoTable = null) : Statement;

public sealed record ShowPortalFavoritesStatement(string? Username, int? Limit, string? IntoTable = null) : Statement;

public sealed record ShowPortalRecentReportsStatement(int? Limit, string? IntoTable = null) : Statement;

public sealed record SearchPortalCatalogStatement(string Query, int? Limit, string? IntoTable = null) : Statement;

public sealed record ShowEffectivePortalPermissionsStatement(string TargetType, string Target, string? IntoTable = null) : Statement;

public sealed record ShowPortalUsageMetricsStatement(int? Days, string? IntoTable = null) : Statement;

public sealed record ShowPortalOperationalMetricsStatement(string? IntoTable = null) : Statement;

public sealed record ShowPortalAuditStatement(string? Action, int? Limit, string? IntoTable = null) : Statement;

public sealed record ShowActivePortalSessionsStatement(string? IntoTable = null) : Statement;

public sealed record ValidatePortalReportStatement(string ScriptPath, string? IntoTable = null) : Statement;
