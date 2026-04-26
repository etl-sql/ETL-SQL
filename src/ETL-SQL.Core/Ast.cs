using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Core.Parser;
using System.Text.RegularExpressions;
using ETL_SQL.Core.Formatting;

namespace ETL_SQL.Core
{
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
    }

    public enum ObjectCreationMode { Create, Alter, CreateOrAlter }

    public record Script : AstNode
    {
        public List<Statement> Statements { get; init; } = new();
        public List<ETL_SQL.Core.Common.Diagnostic> Diagnostics { get; init; } = new();
        public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public record NoOpStatement : Statement
    {
    }

    public record CreateConnectionStatement(string name, string? type = null, Expression? target = null, Dictionary<string, Expression>? options = null, ObjectCreationMode mode = ObjectCreationMode.Create) : Statement
    {
        public string ConnectionName { get; } = name;
        public string? ConnectionType { get; } = type; // FILE, DATABASE, EXCEL
        public Expression? TargetExpression { get; } = target; 
        public Dictionary<string, Expression>? Options { get; } = options;
        public ObjectCreationMode Mode { get; } = mode;
    }    public record CreateSshKeyPairStatement(Expression path, Expression? bits = null, Expression? algorithm = null, Expression? passphrase = null, Expression? comment = null) : Statement
    {
        public Expression Path { get; } = path;
        public Expression? Bits { get; } = bits;
        public Expression? Algorithm { get; } = algorithm;
        public Expression? Passphrase { get; } = passphrase;
        public Expression? Comment { get; } = comment;
    }


    public record SelectColumn(Expression expression, string? alias = null, Dictionary<string, string>? metadata = null) : AstNode
    {
        public Expression Expression { get; } = expression;
        public string? Alias { get; } = alias;
        public Dictionary<string, string> Metadata { get; set; } = metadata ?? new(StringComparer.OrdinalIgnoreCase);
        public string? Description => Metadata.TryGetValue("d", out var d) ? d : null;
        public string? DerivedFromDescriptions { get; set; }
    }

    public record TableReference : AstNode
    {
        public string? ConnectionName { get; }
        public string? DatabaseName { get; }
        public string? SchemaName { get; }
        public string TableName { get; }
        public string? Alias { get; }
        public Statement? Subquery { get; }
        public FunctionCallExpression? FunctionCall { get; }
        public List<AstNode> TableOperators { get; } = new();
        public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Expression> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public TableReference(string tableName, string? schemaName = null, string? databaseName = null, string? connectionName = null, string? alias = null, Statement? subquery = null, FunctionCallExpression? functionCall = null)
        {
            TableName = tableName;
            SchemaName = schemaName;
            DatabaseName = databaseName;
            ConnectionName = connectionName;
            Alias = alias;
            Subquery = subquery;
            FunctionCall = functionCall;
        }

        public override string ToSql() => AstSerializer.Format(this);

        public virtual IEnumerable<string> GetSourceTables()
        {
            if (Subquery is SelectStatement sel) return sel.GetSourceTables();
            if (Subquery is SetOperationStatement setOp) return setOp.GetSourceTables();
            if (!string.IsNullOrEmpty(TableName) && TableName != "SUBQUERY" && TableName != "DUAL")
            {
                string fullPath = (ConnectionName != null ? ConnectionName + "." : "") + (DatabaseName != null ? DatabaseName + "." : "") + (SchemaName != null ? SchemaName + "." : "") + TableName;
                return new[] { fullPath };
            }
            return Enumerable.Empty<string>();
        }

        public override string ToString() => ToSql();
    }

    public record PivotClause : AstNode
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

    public record OutputClause : AstNode
    {
        public List<SelectColumn> Columns { get; }
        public TableReference? IntoTable { get; }

        public OutputClause(List<SelectColumn> columns, TableReference? intoTable = null)
        {
            Columns = columns;
            IntoTable = intoTable;
        }
    }

    public record UnpivotClause : AstNode
    {
        public string ValueColumn { get; }
        public string NameColumn { get; }
        public List<string> UnpivotColumns { get; }
        public string? Alias { get; set; }

        public UnpivotClause(string valueColumn, string nameColumn, List<string> unpivotColumns)
        {
            ValueColumn = valueColumn;
            NameColumn = nameColumn;
            UnpivotColumns = unpivotColumns;
        }
    }

    public enum JoinHint { None, Hash, Loop, Merge }
    
    public record JoinClause : AstNode
    {
        public string JoinType { get; }
        public TableReference Table { get; }
        public Expression Condition { get; }
        public JoinHint Hint { get; set; } = JoinHint.None;
        public bool IsApply => JoinType.Contains("APPLY");

        public JoinClause(string joinType, TableReference table, Expression condition, JoinHint hint = JoinHint.None)
        {
            JoinType = joinType;
            Table = table;
            Condition = condition;
            Hint = hint;
        }
    }

    public record OrderByClause : AstNode
    {
        public Expression Expression { get; }
        public bool Descending { get; }
        public OrderByClause(Expression expression, bool descending = false)
        {
            Expression = expression;
            Descending = descending;
        }
    }

    public record CteDefinition : AstNode
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

    public record ForClause : AstNode
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

    public record SelectStatement : Statement
    {
        public List<SelectColumn> Columns { get; }
        public TableReference? IntoTable { get; }
        public TableReference FromTable { get; }
        public List<JoinClause> Joins { get; }
        public Expression? WhereClause { get; }
        public List<Expression>? GroupBy { get; }
        /// <summary>Non-null when GROUP BY uses GROUPING SETS / ROLLUP / CUBE. Null for plain GROUP BY.</summary>
        public GroupingSetClause? GroupingSet { get; set; }
        public Expression? HavingClause { get; set; }
        public List<OrderByClause>? OrderBy { get; set; }
        public bool IsDistinct { get; set; }
        public Expression? TopCount { get; set; }
        public bool IsTopPercent { get; set; }
        public bool WithTies { get; set; }
        public Expression? LimitCount { get; set; }
        public Expression? Offset { get; set; }
        public ForClause? ForClause { get; set; }
        /// <summary>Common Table Expressions (WITH clause) applied to this SELECT statement.</summary>
        public new List<CteDefinition>? Ctes { get; set; }
        public bool IsRecursive { get; set; }

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
    }

    public enum GroupingSetType { None, GroupingSets, Rollup, Cube }

    /// <summary>
    /// Represents GROUP BY GROUPING SETS(...), ROLLUP(...), or CUBE(...).
    /// When Type == None, GroupSets contains exactly one entry (the plain GROUP BY list).
    /// </summary>
    public record GroupingSetClause : AstNode
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

    public enum SetOpType { UNION, UNION_ALL, EXCEPT, INTERSECT }

    public record SetOperationStatement : Statement
    {
        public Statement Left { get; }
        public SetOpType Operation { get; }
        public Statement Right { get; }

        public SetOperationStatement(Statement left, SetOpType op, Statement right)
        {
            Left = left;
            Operation = op;
            Right = right;
        }

        public override IEnumerable<string> GetSourceTables()
        {
            var sources = new List<string>();
            sources.AddRange(Left.GetSourceTables());
            sources.AddRange(Right.GetSourceTables());
            return sources.Distinct(StringComparer.OrdinalIgnoreCase);
        }
    }

    public record ExecStatement : Statement
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

    public record ExecuteRemoteBlockStatement : Statement
    {
        public Expression ConnectionName { get; }
        public BlockStatement Body { get; }

        public ExecuteRemoteBlockStatement(Expression connectionName, BlockStatement body)
        {
            ConnectionName = connectionName;
            Body = body;
        }
    }

    public record ExecutePushdownStatement : Statement
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

    public record InsertStatement : Statement
    {
        public TableReference TargetTable { get; }
        public Statement? SelectQuery { get; }
        public List<string>? Columns { get; }
        public List<List<Expression>>? Values { get; }
        public OutputClause? Output { get; set; }

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

    public record Assignment : AstNode
    {
        public string ColumnName { get; }
        public Expression Value { get; }

        public Assignment(string columnName, Expression value)
        {
            ColumnName = columnName;
            Value = value;
        }
    }

    public record UpdateStatement : Statement
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

    public record DeleteStatement : Statement
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

    public record KillJobStatement(Expression JobIdExpr) : Statement;

    public record TruncateTableStatement : Statement
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
    public record MergeUpdateClause : MergeMatchedClause
    {
        public List<Assignment> Assignments { get; init; }
        public MergeUpdateClause(Expression? condition, List<Assignment> assignments) : base(MergeActionType.UPDATE, condition)
        {
            Assignments = assignments;
            UpdateAssignments = assignments;
        }
    }
    public record MergeDeleteClause(Expression? Condition) : MergeMatchedClause(MergeActionType.DELETE, Condition)
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

    public record MergeInsertClause : MergeNotMatchedClause
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

    public record MergeStatement : Statement
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

    public record ForeignKeyReference : AstNode
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

    public record TablePrimaryKeyConstraint : TableConstraint
    {
        public List<string> Columns { get; }
        public TablePrimaryKeyConstraint(List<string> columns) => Columns = columns;
        public override string ToSql() => AstSerializer.Format(this);
    }

    public record TableUniqueConstraint : TableConstraint
    {
        public List<string> Columns { get; }
        public TableUniqueConstraint(List<string> columns) => Columns = columns;
        public override string ToSql() => AstSerializer.Format(this);
    }

    public record TableForeignKeyConstraint : TableConstraint
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

    public record TableCheckConstraint : TableConstraint
    {
        public Expression Expression { get; }
        public TableCheckConstraint(Expression expression) => Expression = expression;
        public override string ToSql() => AstSerializer.Format(this);
    }

    public record ColumnDefinition : AstNode
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

    public record CreateTableStatement : Statement
    {
        public TableReference TargetTable { get; }
        public bool IfNotExists { get; }
        public List<ColumnDefinition> Columns { get; }
        public List<TableConstraint> TableConstraints { get; } = new();

        public CreateTableStatement(TableReference targetTable, bool ifNotExists, List<ColumnDefinition> columns)
        {
            TargetTable = targetTable;
            IfNotExists = ifNotExists;
            Columns = columns;
        }
    }

    public enum AlterTableActionType { ADD, DROP_COLUMN, RENAME_COLUMN }

    public record AlterTableStatement : Statement
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

    public record DropTableStatement : Statement
    {
        public TableReference TargetTable { get; }
        public bool IfExists { get; }

        public DropTableStatement(TableReference targetTable, bool ifExists)
        {
            TargetTable = targetTable;
            IfExists = ifExists;
        }
    }

    public record DropConnectionStatement : Statement
    {
        public string ConnectionName { get; }
        public bool IfExists { get; }
        public DropConnectionStatement(string name, bool ifExists) { ConnectionName = name; IfExists = ifExists; }
    }

    /// <summary>
    /// ALTER CONNECTION &lt;name&gt; [ON &lt;type&gt;(&lt;target&gt;)] [WITH(&lt;options&gt;)];
    /// Modifies an existing connection. Previous options are preserved unless explicitly overridden.
    /// </summary>
    public record AlterConnectionStatement : Statement
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
            ConnectionName   = name;
            ConnectionType   = type;
            TargetExpression = target;
            Options          = options;
        }
    }

    public enum ClearSessionMode { Current, Single, All, Stale }
    public record ClearSessionStatement(ClearSessionMode Mode = ClearSessionMode.Current, Expression? SessionId = null) : Statement
    {
    }

    public record ShowSessionsStatement(string? IntoTable = null) : Statement
    {
    }

    public record DropProcedureStatement : Statement
    {
        public string ProcedureName { get; }
        public bool IfExists { get; }
        public DropProcedureStatement(string name, bool ifExists) { ProcedureName = name; IfExists = ifExists; }
    }

    public record DropFunctionStatement : Statement
    {
        public string FunctionName { get; }
        public bool IfExists { get; }
        public DropFunctionStatement(string name, bool ifExists) { FunctionName = name; IfExists = ifExists; }
    }

    public record DropIndexStatement : Statement
    {
        public string IndexName { get; }
        public TableReference? Table { get; }
        public bool IfExists { get; }
        public DropIndexStatement(string name, TableReference? table, bool ifExists) { IndexName = name; Table = table; IfExists = ifExists; }
    }

    public record DeclareStatement : Statement
    {
        public string VariableName { get; }
        public string DataType { get; }
        public Expression? InitialValue { get; }
        public bool IsSensitive { get; set; }
        public bool IsInput { get; set; }
        public bool IsOutput { get; set; }
        public Dictionary<string, string> Metadata { get; }
        public string? Description => Metadata.TryGetValue("d", out var d) ? d : null;

        public DeclareStatement(string name, string type, Expression? initialValue = null, Dictionary<string, string>? metadata = null)
        {
            VariableName = name;
            DataType = type;
            InitialValue = initialValue;
            Metadata = metadata ?? new(StringComparer.OrdinalIgnoreCase);
        }

        public DeclareStatement(string name, string type, Expression? initialValue, bool isSensitive, bool isInput, bool isOutput, Dictionary<string, string>? metadata = null)
        {
            VariableName = name;
            DataType = type;
            InitialValue = initialValue;
            IsSensitive = isSensitive;
            IsInput = isInput;
            IsOutput = isOutput;
            Metadata = metadata ?? new(StringComparer.OrdinalIgnoreCase);
        }
    }

    public record DockerStatement : Statement
    {
        public Expression ImageName { get; }
        public string? Alias { get; }
        public DockerStatement(Expression imageName, string? alias = null)
        {
            ImageName = imageName;
            Alias = alias;
        }
    }

    public record RunScriptStatement : Statement
    {
        public Expression PathExpression { get; }
        public Dictionary<string, Expression> Parameters { get; }

        public RunScriptStatement(Expression path, Dictionary<string, Expression> parameters)
        {
            PathExpression = path;
            Parameters = parameters;
        }
    }

    public record SetVariableStatement(Expression Target, Expression Value) : Statement
    {
        public string VariableName => Target switch
        {
            VariableExpression v => v.Name,
            IdentifierExpression i => i.Name,
            MemberAccessExpression m => m.ToSql(), // Handle nested assignments like @json.key
            _ => Target.ToSql()
        };
    }

    public record BlockStatement : Statement
    {
        public List<Statement> Statements { get; }

        public BlockStatement(List<Statement> statements)
        {
            Statements = statements;
        }
    }

    public record WhileStatement : Statement
    {
        public Expression Condition { get; }
        public Statement Body { get; }

        public WhileStatement(Expression condition, Statement body)
        {
            Condition = condition;
            Body = body;
        }
    }

    public record ForStatement : Statement
    {
        public string VariableName { get; }
        public Expression StartValue { get; }
        public Expression EndValue { get; }
        public Expression? StepValue { get; }
        public Statement Body { get; }

        public ForStatement(string variableName, Expression startValue, Expression endValue, Expression? stepValue, Statement body)
        {
            VariableName = variableName;
            StartValue = startValue;
            EndValue = endValue;
            StepValue = stepValue;
            Body = body;
        }
    }

    public record ForeachStatement : Statement
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

    public record ElseIfClause : AstNode
    {
        public Expression Condition { get; }
        public Statement Body { get; }

        public ElseIfClause(Expression condition, Statement body)
        {
            Condition = condition;
            Body = body;
        }
    }

    public record IfStatement : Statement
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

    public record PrintStatement : Statement
    {
        public Expression Message { get; }
        public Expression? ShowTimestamp { get; }
        public Expression? TimestampFormat { get; }

        public PrintStatement(Expression message, Expression? showTimestamp = null, Expression? timestampFormat = null)
        {
            Message = message;
            ShowTimestamp = showTimestamp;
            TimestampFormat = timestampFormat;
        }
    }

    public enum WaitType { Delay, Time, Until }

    /// <summary>WAITFOR DELAY/TIME '...' — pauses execution.</summary>
    public record WaitForStatement(Expression expression, WaitType type = WaitType.Delay) : Statement
    {
        public Expression Expression { get; } = expression;
        public WaitType Type { get; } = type;
    }

    public record RaiseErrorStatement : Statement
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

    public record AssertStatement(Expression Condition, Expression? Message = null) : Statement
    {
    }

    public record ExpectedSchemaColumn
    {
        public required string ColumnName { get; init; }
        public required string DataType   { get; init; }
        public bool NotNull               { get; init; }
    }

    /// <summary>
    /// EXPECT SCHEMA target ( col type [NOT NULL] [, ...] ) [ON DRIFT WARN];
    /// Validates that the actual schema of a #temp table or connection matches the declared columns.
    /// Raises ExecutionException (or logs a warning with ON DRIFT WARN) when drift is detected.
    /// </summary>
    public record ExpectSchemaStatement : Statement
    {
        public required string Target                         { get; init; }
        public required List<ExpectedSchemaColumn> Columns   { get; init; }
        public bool WarnOnDrift                              { get; init; }
    }

    public record ExecuteParameter : AstNode
    {
        public Expression Expression { get; }
        public bool IsOutput { get; }
        public bool IsInput { get; }

        public ExecuteParameter(Expression expression, bool isOutput = false, bool isInput = false)
        {
            Expression = expression;
            IsOutput = isOutput;
            IsInput = isInput;
        }
    }

    public record ExecuteStatement : Statement
    {
        public string ProcedureName { get; }
        public List<ExecuteParameter> Parameters { get; }

        public ExecuteStatement(string procedureName, List<ExecuteParameter> parameters)
        {
            ProcedureName = procedureName;
            Parameters = parameters;
        }
    }

    public record ParallelStatement : Statement
    {
        public BlockStatement Body { get; }
        public int ConcurrencyLimit { get; set; } = 0; // 0 means no limit (all tasks)

        public ParallelStatement(BlockStatement body, int concurrencyLimit = 0)
        {
            Body = body;
            ConcurrencyLimit = concurrencyLimit;
        }
    }

    public record BulkInsertStatement : Statement
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

    public record CreateProcedureStatement : Statement
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

    public record CreateFunctionStatement : Statement
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

    public record ParameterDefinition : AstNode
    {
        public string Name { get; }
        public string DataType { get; }

        public ParameterDefinition(string name, string dataType)
        {
            Name = name;
            DataType = dataType;
        }
    }

    public record BeginTransactionStatement : Statement
    {
        public string? Name { get; }
        public BeginTransactionStatement(string? name = null) => Name = name;
    }

    public record CommitTransactionStatement : Statement
    {
        public string? Name { get; }
        public CommitTransactionStatement(string? name = null) => Name = name;
    }

    public record RollbackTransactionStatement : Statement
    {
        public string? Name { get; }
        public RollbackTransactionStatement(string? name = null) => Name = name;
    }

    public record ContinueStatement : Statement
    {
    }

    public record ThrowStatement : Statement
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

    public record TryCatchStatement : Statement
    {
        public Statement TryBody { get; }
        public Statement CatchBody { get; }

        public TryCatchStatement(Statement tryBody, Statement catchBody)
        {
            TryBody = tryBody;
            CatchBody = catchBody;
        }
    }


    public record ReturnStatement : Statement
    {
        public Expression? ReturnValue { get; }

        public ReturnStatement(Expression? returnValue = null)
        {
            ReturnValue = returnValue;
        }
    }

    public record BreakStatement : Statement 
    {
    }

    /// <summary>Base class for all expressions that return a value.</summary>
    public abstract record Expression : AstNode 
    {
        public virtual IEnumerable<string> GetSourceTables() => Enumerable.Empty<string>();
        public virtual IEnumerable<string> GetSourceColumns() => Enumerable.Empty<string>();
    }

    public record UnaryExpression : Expression
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

    public record BinaryExpression : Expression
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

    public record LiteralExpression : Expression
    {
        public object? Value { get; }
        public TokenType Type { get; }

        public LiteralExpression(object? value, TokenType type)
        {
            Value = value;
            Type = type;
        }
    }

    public record IdentifierExpression : Expression
    {
        public string Name { get; }

        public IdentifierExpression(string name)
        {
            Name = name;
        }

        public override IEnumerable<string> GetSourceColumns() => new[] { Name.Split('.').Last() };
        public override IEnumerable<string> GetSourceTables() => Name.Contains('.') ? new[] { Name.Split('.')[0] } : Enumerable.Empty<string>();
    }

    public record MemberAccessExpression : Expression
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

    public record SubqueryExpression : Expression
    {
        public Statement Query { get; }

        public SubqueryExpression(Statement query)
        {
            Query = query;
        }
    }

    public record VariableExpression(string Name) : Expression
    {
        public string Name { get; } = Name;
    }

    public record ParameterExpression(string Value, int? Index = null) : Expression
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
        public List<OrderByClause>? WithinGroupOrderBy { get; set; }

        public FunctionCallExpression(string functionName, List<Expression> arguments)
        {
            FunctionName = functionName;
            Arguments = arguments;
        }
        public override IEnumerable<string> GetSourceTables() => Arguments.SelectMany(a => a.GetSourceTables()).Distinct(StringComparer.OrdinalIgnoreCase);
        public override IEnumerable<string> GetSourceColumns() => Arguments.SelectMany(a => a.GetSourceColumns()).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public record ListExpression : Expression
    {
        public List<Expression> Items { get; }

        public ListExpression(List<Expression> items)
        {
            Items = items;
        }
    }

    public record IsNullExpression : Expression
    {
        public Expression Expression { get; }
        public bool Not { get; }

        public IsNullExpression(Expression expression, bool isNot)
        {
            Expression = expression;
            Not = isNot;
        }
    }

    /// <summary>EXPORT REPORT 'path.rptsql' FORMAT PDF|CSV|MARKDOWN TO 'output.pdf'</summary>
    public record ExportReportStatement(
        Expression ReportPath,
        string     Format,
        Expression OutputPath) : Statement;

    public record ExportStatement : Statement
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

    public record HelpStatement : Statement
    {
        public string? Topic { get; }
        public string? SubTopic { get; }

        public HelpStatement(string? topic, string? subTopic = null)
        {
            Topic = topic;
            SubTopic = subTopic;
        }
    }

    public record RequireVersionStatement(string Operator, string Version) : Statement
    {
    }

    public record InExpression : Expression
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
    }

    public record LineageStatement : Statement
    {
        public TableReference TargetTable { get; }
        public string? ColumnName { get; }
        public string? ExportPath { get; set; }

        public LineageStatement(TableReference targetTable, string? columnName = null, string? exportPath = null)
        {
            TargetTable = targetTable;
            ColumnName = columnName;
            ExportPath = exportPath;
        }
    }

    public record ShowVariablesStatement : Statement
    {
        public bool IsLocalOnly { get; init; }
        public string? IntoTable { get; init; }

        public ShowVariablesStatement(bool isLocalOnly = false, string? intoTable = null)
        {
            IsLocalOnly = isLocalOnly;
            IntoTable = intoTable;
        }
    }

    public record ShowSafeZonesStatement : Statement
    {
        public string? IntoTable { get; init; }

        public ShowSafeZonesStatement(string? intoTable = null)
        {
            IntoTable = intoTable;
        }
    }

    public record EmailStatement : Statement
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

    public record LikeExpression : Expression
    {
        public Expression Left { get; }
        public Expression Pattern { get; }
        public bool IsNot { get; }
        public Expression? EscapeChar { get; }

        public LikeExpression(Expression left, Expression pattern, bool isNot = false, Expression? escapeChar = null)
        {
            Left = left;
            Pattern = pattern;
            IsNot = isNot;
            EscapeChar = escapeChar;
        }
    }

    public record ExistsExpression : Expression
    {
        public Statement Subquery { get; }
        public bool IsNot { get; }

        public ExistsExpression(Statement subquery, bool isNot = false)
        {
            Subquery = subquery;
            IsNot = isNot;
        }
    }

    public record CaseExpression : Expression
    {
        public List<(Expression Condition, Expression Result)> WhenClauses { get; }
        public Expression? ElseResult { get; }

        public CaseExpression(List<(Expression Condition, Expression Result)> whenClauses, Expression? elseResult)
        {
            WhenClauses = whenClauses;
            ElseResult = elseResult;
        }

        public override IEnumerable<string> GetSourceTables()
        {
            var sources = WhenClauses.SelectMany(c => c.Condition.GetSourceTables().Concat(c.Result.GetSourceTables()));
            if (ElseResult != null) sources = sources.Concat(ElseResult.GetSourceTables());
            return sources.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        public override IEnumerable<string> GetSourceColumns()
        {
            var columns = WhenClauses.SelectMany(c => c.Condition.GetSourceColumns().Concat(c.Result.GetSourceColumns()));
            if (ElseResult != null) columns = columns.Concat(ElseResult.GetSourceColumns());
            return columns.Distinct(StringComparer.OrdinalIgnoreCase);
        }
    }
    public record AtTimeZoneExpression : Expression
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

    public record SubstringExpression : FunctionCallExpression
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

    public record GenerateRule(string ColumnName, string Rule) : AstNode;

    public record GenerateStatement(Expression RowCount, TableReference Target, List<GenerateRule> Rules, Dictionary<string, Expression>? Options = null) : Statement
    {
        public override IEnumerable<string> GetSourceTables() => Enumerable.Empty<string>();
    }

    public record PositionExpression(Expression substring, Expression str) : Expression
    {
        public Expression Substring { get; } = substring;
        public Expression String { get; } = str;

        public override IEnumerable<string> GetSourceTables() => String.GetSourceTables().Concat(Substring.GetSourceTables());
        public override IEnumerable<string> GetSourceColumns() => String.GetSourceColumns().Concat(Substring.GetSourceColumns());
    }

    public record ExtractExpression(string field, Expression source) : Expression
    {
        public string Field { get; } = field;
        public Expression Source { get; } = source;

        public override IEnumerable<string> GetSourceTables() => Source.GetSourceTables();
        public override IEnumerable<string> GetSourceColumns() => Source.GetSourceColumns();
    }

    public record OverlayExpression(Expression str, Expression overlay, Expression start, Expression? length = null) : Expression
    {
        public Expression String { get; } = str;
        public Expression Overlay { get; } = overlay;
        public Expression Start { get; } = start;
        public Expression? Length { get; } = length;

        public override IEnumerable<string> GetSourceTables() => String.GetSourceTables().Concat(Overlay.GetSourceTables());
        public override IEnumerable<string> GetSourceColumns() => String.GetSourceColumns().Concat(Overlay.GetSourceColumns());
    }

    public enum TrimType { BOTH, LEADING, TRAILING }

    public record TrimExpression(TrimType type, Expression? characters, Expression str) : Expression
    {
        public TrimType Type { get; } = type;
        public Expression? Characters { get; } = characters;
        public Expression String { get; } = str;

        public override IEnumerable<string> GetSourceTables() => String.GetSourceTables().Concat(Characters?.GetSourceTables() ?? Enumerable.Empty<string>());
        public override IEnumerable<string> GetSourceColumns() => String.GetSourceColumns().Concat(Characters?.GetSourceColumns() ?? Enumerable.Empty<string>());
    }

    public enum WindowFrameType { ROWS, RANGE }
    public enum WindowFrameBoundType { PRECEDING, FOLLOWING, CURRENT_ROW, UNBOUNDED_PRECEDING, UNBOUNDED_FOLLOWING }

    public record WindowFrame : AstNode
    {
        public WindowFrameType Type { get; }
        public WindowFrameBoundType StartBound { get; }
        public Expression? StartValue { get; }
        public WindowFrameBoundType? EndBound { get; }
        public Expression? EndValue { get; }

        public WindowFrame(WindowFrameType type, WindowFrameBoundType startBound, Expression? startValue = null, WindowFrameBoundType? endBound = null, Expression? endValue = null)
        {
            Type = type;
            StartBound = startBound;
            StartValue = startValue;
            EndBound = endBound;
            EndValue = endValue;
        }
    }


    public record WindowClause : AstNode
    {
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

    public record CreateIndexStatement : Statement
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

    public record ExplainStatement : Statement
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

    public enum FileOpType { Copy, Move, Rename, Delete, Compress, Encrypt, Decrypt }

    public record FileOperationStatement : Statement
    {
        public FileOpType Type { get; }
        public Expression Source { get; }
        public Expression? Destination { get; }
        public Expression? Overwrite { get; set; }
        public Expression? Password { get; set; }

        public FileOperationStatement(FileOpType type, Expression source, Expression? destination = null, Expression? overwrite = null, Expression? password = null)
        {
            Type = type;
            Source = source;
            Destination = destination;
            Overwrite = overwrite;
            Password = password;
        }
    }

    public enum DirectoryOpType { Create, Delete, Rename, Move, Copy, DeleteContents, Compress, Encrypt, Decrypt }

    public record DirectoryOperationStatement : Statement
    {
        public DirectoryOpType Type { get; }
        public Expression Path { get; }
        public Expression? NewNameOrDest { get; }
        public Expression? Overwrite { get; set; }
        public Expression? Recursive { get; set; }
        public Expression? Password { get; set; }

        public DirectoryOperationStatement(DirectoryOpType type, Expression path, Expression? newNameOrDest = null, Expression? overwrite = null, Expression? recursive = null, Expression? password = null)
        {
            Type = type;
            Path = path;
            NewNameOrDest = newNameOrDest;
            Overwrite = overwrite;
            Recursive = recursive;
            Password = password;
        }
    }

    public enum FileTransferType { Send, Receive }

    public record FileTransferStatement : Statement
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

    public record DockerActionStatement(DockerAction action, string? alias = null, DockerTargetMode targetMode = DockerTargetMode.Single) : Statement
    {
        public DockerAction Action { get; } = action;
        public string? Alias { get; } = alias;
        public DockerTargetMode TargetMode { get; } = targetMode;
    }

    public record CreateJobStatement : Statement
    {
        public string JobName { get; }
        public ScheduleInfo Schedule { get; }
        public Statement Script { get; }
        public int MaxRetries { get; }
        public int RetryDelaySeconds { get; }

        public CreateJobStatement(string jobName, ScheduleInfo schedule, Statement script, int maxRetries = 0, int retryDelaySeconds = 30)
        {
            JobName = jobName;
            Schedule = schedule;
            Script = script;
            MaxRetries = maxRetries;
            RetryDelaySeconds = retryDelaySeconds;
        }
    }

    public record ScheduleInfo : AstNode
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

    public record ShowJobHistoryStatement : Statement
    {
        public string? JobName { get; }
        public string? IntoTable { get; set; }
        public ShowJobHistoryStatement(string? jobName = null) { JobName = jobName; }
    }

    public record ShowJobsStatement : Statement
    {
        public string? IntoTable { get; set; }
    }

    public record ShowVersionStatement : Statement
    {
        public string? IntoTable { get; init; }
    }

    public record ShowConnectionsStatement : Statement
    {
        public string? IntoTable { get; set; }
    }

    public record ShowTablesStatement : Statement
    {
        public string? ConnectionName { get; }
        public string? IntoTable { get; set; }
        public ShowTablesStatement(string? connectionName = null) { ConnectionName = connectionName; }
    }

    public record ShowColumnsStatement : Statement
    {
        public TableReference Table { get; }
        public string? IntoTable { get; set; }
        public ShowColumnsStatement(TableReference table) { Table = table; }
    }

    public record ShowTagsStatement : Statement
    {
        public string TableName { get; }
        public string? ColumnName { get; }
        public string? IntoTable { get; set; }
        public ShowTagsStatement(string tableName, string? columnName = null) { TableName = tableName; ColumnName = columnName; }
    }

    public record ShowTagValueStatement : Statement
    {
        public string TableName { get; }
        public string? ColumnName { get; }
        public string TagName { get; }
        public string? IntoTable { get; set; }
        public ShowTagValueStatement(string tableName, string tagName, string? columnName = null) { TableName = tableName; TagName = tagName; ColumnName = columnName; }
    }

    public record LintStatement : Statement
    {
        public string? ScriptPath { get; }

        public LintStatement(string? scriptPath = null)
        {
            ScriptPath = scriptPath;
        }
    }

    /// <summary>A single variable assignment inside a CREATE SETS block.</summary>
    public record SetsAssignment
    {
        public string VariableName { get; }
        public Expression Value { get; }
        public SetsAssignment(string variableName, Expression value) { VariableName = variableName; Value = value; }
    }

    /// <summary>CREATE SETS !&lt;name&gt; BEGIN @var = val, ... [SET WITH_PROMPT ON;] END</summary>
    public record CreateSetsStatement : Statement
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

    /// <summary>DROP SETS [IF EXISTS] !&lt;name&gt;</summary>
    public record DropSetsStatement : Statement
    {
        public string Name { get; }
        public bool IfExists { get; }

        public DropSetsStatement(string name, bool ifExists) { Name = name; IfExists = ifExists; }
    }

    /// <summary>USE SETS !<name></summary>
    public record UseSetsStatement : Statement
    {
        public string Name { get; }
        public UseSetsStatement(string name) { Name = name; }
    }

    /// <summary>USE PASSWORD = 'password'</summary>
    public record UsePasswordStatement : Statement
    {
        public string Password { get; }
        public UsePasswordStatement(string password) { Password = password; }
        
        public string ToSql(bool mask) => $"USE PASSWORD = '{(mask ? "********" : Password.Replace("'", "''"))}';";
        public override string ToSql() => AstSerializer.Format(this); // Always masked in serialization
    }

    /// <summary>SET SHOW_PASSWORD ON/OFF</summary>
    public record SetShowPasswordStatement(bool Enabled) : Statement
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
    public record SetSecurityOverrideStatement(SecurityOverride Override, bool Enabled, Expression? Value = null) : Statement
    {
        public override string ToSql() => AstSerializer.Format(this);
    }

    // ── Portal admin statements (Phase 10) ────────────────────────────────────
    // These are only valid inside an EXECUTE portal BEGIN…END block targeting a
    // REPORTPORTAL connection. The PortalConnector translates them into REST calls.

    public record CreatePortalUserStatement(
        string Username, string Email, Expression Password,
        string Role, string? FirstName, string? LastName) : Statement;

    public record AlterPortalUserStatement(
        string Username,
        string? NewRole,
        string? NewEmail,
        bool? SetActive,        // true = ENABLE, false = DISABLE
        Expression? NewPassword) : Statement;

    public record DropPortalUserStatement(string Username, bool Cascade) : Statement;

    public record CreatePortalGroupStatement(string Name, string? Description) : Statement;

    public record DropPortalGroupStatement(string Name, bool Cascade) : Statement;

    public record AddUserToPortalGroupStatement(string Username, string GroupName) : Statement;

    public record CreatePortalFolderStatement(string Path) : Statement;

    public record DropPortalFolderStatement(string Path, bool Cascade) : Statement;

    public enum PortalFolderPermission { Read, Execute, Manage }

    public record GrantPortalPermissionStatement(
        string FolderPath, string GroupName, PortalFolderPermission Permission) : Statement;

    public record RevokePortalPermissionStatement(
        string FolderPath, string GroupName, PortalFolderPermission Permission) : Statement;

    public record PublishPortalReportStatement(
        string ReportName, string ScriptPath, string FolderPath, string? Description) : Statement;

    public record AlterPortalReportStatement(
        string ReportName, string? NewFolder, string? NewDescription) : Statement;

    public record DropPortalReportStatement(string ReportName, bool Cascade) : Statement;

    public record CreatePortalRefreshJobStatement(
        string ReportName, string Schedule, string OrchestratorAlias) : Statement;

    public record TriggerPortalRefreshStatement(string ReportName) : Statement;

    public record DropPortalRefreshJobStatement(string ReportName) : Statement;

    public record DropPortalSnapshotStatement(string ReportName) : Statement;

    public record RebuildPortalSnapshotStatement(string ReportName) : Statement;

    public enum PortalSubscriptionFormat { Pdf, Csv, Both }

    public record CreatePortalSubscriptionStatement(
        string ReportPath,
        string Recipient,        // username or group name
        bool   IsGroup,
        string? Schedule,
        bool   OnRefresh,
        PortalSubscriptionFormat Format,
        string SmtpAlias) : Statement;

    public record AlterPortalSubscriptionStatement(
        int SubscriptionId,
        string? NewSchedule,
        bool? SetActive) : Statement;

    public record DropPortalSubscriptionStatement(int SubscriptionId) : Statement;

    public record DisconnectPortalUserStatement(string Username) : Statement;

    public record RevokePortalTokensStatement(string Username) : Statement;

    public record RestartPortalStatement : Statement;

    public record ShutdownPortalStatement : Statement;

    public record ShowPortalUsersStatement : Statement;

    public record ShowPortalReportsStatement(string? FolderPath) : Statement;

    public record ShowActivePortalSessionsStatement : Statement;
}
