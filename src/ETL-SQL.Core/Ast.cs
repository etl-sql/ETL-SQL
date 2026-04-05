using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Core.Parser;
using System.Text.RegularExpressions;

namespace ETL_SQL.Core
{
    /// <summary>Base class for all Abstract Syntax Tree nodes, tracking source locations.</summary>
    public abstract class AstNode 
    { 
        /// <summary>Starting line number in the source script.</summary>
        public int Line { get; set; }
        /// <summary>Starting column position in the source script.</summary>
        public int Column { get; set; }
        /// <summary>Ending line number in the source script.</summary>
        public int EndLine { get; set; }
        /// <summary>Ending column position in the source script.</summary>
        public int EndColumn { get; set; }
    }

    /// <summary>Base class for all executable SQL statements.</summary>
    public abstract class Statement : AstNode 
    {
        /// <summary>Common Table Expressions (WITH clause) applied to this statement.</summary>
        public List<CteDefinition>? Ctes { get; set; }
        /// <summary>Converts the statement back to its SQL representation.</summary>
        public virtual string ToSql() => "UNKNOWN STATEMENT";
        /// <summary>Identifies all tables referenced as data sources in this statement.</summary>
        public virtual IEnumerable<string> GetSourceTables() => Enumerable.Empty<string>();
    }

    public enum ObjectCreationMode { Create, Alter, CreateOrAlter }

    public class Script : AstNode
    {
        public List<Statement> Statements { get; } = new List<Statement>();
        public List<ETL_SQL.Core.Common.Diagnostic> Diagnostics { get; } = new List<ETL_SQL.Core.Common.Diagnostic>();
    }

    public class NoOpStatement : Statement
    {
        public override string ToSql() => ";";
    }

    public class CreateConnectionStatement(string name, string type, Expression target, Dictionary<string, string>? options = null, ObjectCreationMode mode = ObjectCreationMode.Create) : Statement
    {
        public string ConnectionName { get; } = name;
        public string ConnectionType { get; } = type; // FILE, DATABASE, EXCEL
        public Expression TargetExpression { get; } = target; 
        public Dictionary<string, string>? Options { get; } = options;
        public ObjectCreationMode Mode { get; } = mode;

        public override string ToSql()
        {
            var modeStr = Mode switch {
                ObjectCreationMode.Alter => "ALTER",
                ObjectCreationMode.CreateOrAlter => "CREATE OR ALTER",
                _ => "CREATE"
            };
            var optionsStr = "";
            if (Options != null && Options.Count > 0)
            {
                optionsStr = " WITH (" + string.Join(", ", Options.Select(o => $"{o.Key}='{o.Value}'")) + ")";
            }
            return $"{modeStr} CONNECTION {ConnectionName} ON {ConnectionType}({TargetExpression.ToSql()}){optionsStr};";
        }
    }

    public class SelectColumn(Expression expression, string? alias = null, Dictionary<string, string>? metadata = null) : AstNode
    {
        public Expression Expression { get; } = expression;
        public string? Alias { get; } = alias;
        public Dictionary<string, string> Metadata { get; set; } = metadata ?? new(StringComparer.OrdinalIgnoreCase);
        public string? Description => Metadata.TryGetValue("d", out var d) ? d : null;
        public string? DerivedFromDescriptions { get; set; }

        public string ToSql() => Alias != null ? $"{Expression.ToSql()} AS {Alias}" : Expression.ToSql();
    }

    public class TableReference : AstNode
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

        public string ToSql()
        {
            var sql = "";
            if (Subquery != null)
            {
                sql = $"({Subquery.ToSql().TrimEnd(';')})";
            }
            else if (FunctionCall != null)
            {
                sql = FunctionCall.ToSql();
            }
            else
            {
                var parts = new List<string>();
                if (ConnectionName != null) parts.Add(ConnectionName);
                if (DatabaseName != null) parts.Add(DatabaseName);
                if (SchemaName != null) parts.Add(SchemaName);
                parts.Add(TableName);
                sql = string.Join(".", parts);
            }
            if (Alias != null) sql += " AS " + Alias;
            foreach (var op in TableOperators)
            {
                if (op is PivotClause p) sql += " " + p.ToSql();
                else if (op is UnpivotClause u) sql += " " + u.ToSql();
            }
            return sql;
        }

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

    public class PivotClause : AstNode
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

        public string ToSql() => $"PIVOT ({AggregateFunction}({AggregateColumn}) FOR {PivotColumn} IN ({string.Join(", ", PivotValues.Select(v => v.ToSql()))}))" + (Alias != null ? $" AS {Alias}" : "");
    }

    public class OutputClause : AstNode
    {
        public List<SelectColumn> Columns { get; }
        public TableReference? IntoTable { get; }

        public OutputClause(List<SelectColumn> columns, TableReference? intoTable = null)
        {
            Columns = columns;
            IntoTable = intoTable;
        }

        public string ToSql()
        {
            var cols = string.Join(", ", Columns.Select(c => c.ToSql()));
            var into = IntoTable != null ? $" INTO {IntoTable.ToSql()}" : "";
            return $"OUTPUT {cols}{into}";
        }
    }

    public class UnpivotClause : AstNode
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

        public string ToSql() => $"UNPIVOT ({ValueColumn} FOR {NameColumn} IN ({string.Join(", ", UnpivotColumns.Select(c => c))}))" + (Alias != null ? $" AS {Alias}" : "");
    }

    public enum JoinHint { None, Hash, Loop, Merge }
    
    public class JoinClause
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

        public string ToSql() 
        {
            var hintStr = Hint switch {
                JoinHint.Hash => "HASH ",
                JoinHint.Loop => "LOOP ",
                JoinHint.Merge => "MERGE ",
                _ => ""
            };
            var typeParts = JoinType.Split(' ');
            var typeWithHint = typeParts.Length > 1 ? $"{typeParts[0]} {hintStr}{string.Join(" ", typeParts.Skip(1))}" : $"{hintStr}{JoinType}";
            
            // Fix for cases like INNER JOIN -> INNER HASH JOIN
            if (JoinType == "INNER" && Hint != JoinHint.None) return $"INNER {hintStr}JOIN {Table.ToSql()} ON {Condition.ToSql()}";
            if (JoinType == "LEFT" && Hint != JoinHint.None) return $"LEFT {hintStr}OUTER JOIN {Table.ToSql()} ON {Condition.ToSql()}";
            if (JoinType == "RIGHT" && Hint != JoinHint.None) return $"RIGHT {hintStr}OUTER JOIN {Table.ToSql()} ON {Condition.ToSql()}";
            if (JoinType == "FULL" && Hint != JoinHint.None) return $"FULL {hintStr}OUTER JOIN {Table.ToSql()} ON {Condition.ToSql()}";

            return IsApply ? $"{JoinType} {Table.ToSql()}" : $"{JoinType} JOIN {Table.ToSql()} ON {Condition.ToSql()}";
        }
    }

    public class OrderByClause
    {
        public Expression Expression { get; }
        public bool Descending { get; }
        public OrderByClause(Expression expression, bool descending = false)
        {
            Expression = expression;
            Descending = descending;
        }

        public string ToSql() => Expression.ToSql() + (Descending ? " DESC" : " ASC");
    }

    public class CteDefinition : AstNode
    {
        public string Name { get; }
        public Statement Query { get; }
        public CteDefinition(string name, Statement query)
        {
            Name = name;
            Query = query;
        }
    }

    public enum ForType { JSON, XML }
    public enum ForMode { PATH, AUTO, RAW, EXPLICIT }

    public class ForClause : AstNode
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

        public string ToSql()
        {
            var options = new List<string>();
            if (RootName != null) options.Add($"ROOT('{RootName}')");
            if (IncludeNullValues) options.Add("INCLUDE_NULL_VALUES");
            if (WithoutArrayWrapper) options.Add("WITHOUT_ARRAY_WRAPPER");
            
            var optStr = options.Count > 0 ? (Type == ForType.JSON ? ", " : " ") + string.Join(", ", options) : "";
            return $"FOR {Type} {Mode}{optStr}";
        }
    }

    public class SelectStatement : Statement
    {
        public List<SelectColumn> Columns { get; }
        public TableReference? IntoTable { get; }
        public TableReference FromTable { get; }
        public List<JoinClause> Joins { get; }
        public Expression? WhereClause { get; }
        public List<Expression>? GroupBy { get; }
        public Expression? HavingClause { get; }
        public List<OrderByClause>? OrderBy { get; }
        public bool IsDistinct { get; set; }
        public Expression? TopCount { get; set; }
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

        public override string ToSql()
        {
            var recursive = IsRecursive ? "RECURSIVE " : "";
            var with = (Ctes != null && Ctes.Count > 0) ? $"WITH {recursive}" + string.Join(", ", Ctes.Select(c => $"{c.Name} AS ({c.Query.ToSql().TrimEnd(';')})")) + " " : "";
            var distinct = IsDistinct ? "DISTINCT " : "";
            var top = TopCount != null ? $"TOP ({TopCount.ToSql()}) " : "";
            var cols = string.Join(", ", Columns.Select(c => c.ToSql()));
            var into = IntoTable != null ? $" INTO {IntoTable.ToSql()}" : "";
            var from = $" FROM {FromTable.ToSql()}";
            var joins = Joins.Count > 0 ? " " + string.Join(" ", Joins.Select(j => j.ToSql())) : "";
            var where = WhereClause != null ? $" WHERE {WhereClause.ToSql()}" : "";
            var group = GroupBy != null && GroupBy.Count > 0 ? $" GROUP BY {string.Join(", ", GroupBy.Select(g => g.ToSql()))}" : "";
            var having = HavingClause != null ? $" HAVING {HavingClause.ToSql()}" : "";
            var order = OrderBy != null && OrderBy.Count > 0 ? $" ORDER BY {string.Join(", ", OrderBy.Select(o => o.ToSql()))}" : "";
            var limit = LimitCount != null ? $" LIMIT {LimitCount.ToSql()}" : "";
            var offset = Offset != null ? $" OFFSET {Offset.ToSql()} ROWS" : "";
            var forClause = ForClause != null ? $" {ForClause.ToSql()}" : "";

            return $"{with}SELECT {distinct}{top}{cols}{into}{from}{joins}{where}{group}{having}{order}{limit}{offset}{forClause};";
        }
    }

    public enum SetOpType { UNION, UNION_ALL, EXCEPT, INTERSECT }

    public class SetOperationStatement : Statement
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

        public override string ToSql()
        {
            string op = Operation switch {
                SetOpType.UNION => "UNION",
                SetOpType.UNION_ALL => "UNION ALL",
                SetOpType.EXCEPT => "EXCEPT",
                SetOpType.INTERSECT => "INTERSECT",
                _ => "UNION"
            };
            return $"({Left.ToSql().TrimEnd(';')}) {op} ({Right.ToSql().TrimEnd(';')});";
        }
    }

    public class ExecStatement : Statement
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
        
        public override string ToSql() 
        {
            var sql = $"EXEC ({SqlExpression.ToSql()})";
            if (ConnectionName != null) sql += $" AT {ConnectionName.ToSql()}";
            if (IntoTable != null) sql += $" INTO {IntoTable.ToSql()}";
            if (Parameters.Count > 0) sql += $" WITH (" + string.Join(", ", Parameters.Select(p => p.ToSql())) + ")";
            return sql + ";";
        }
    }

    public class ExecuteRemoteBlockStatement : Statement
    {
        public Expression ConnectionName { get; }
        public BlockStatement Body { get; }

        public ExecuteRemoteBlockStatement(Expression connectionName, BlockStatement body)
        {
            ConnectionName = connectionName;
            Body = body;
        }

        public override string ToSql() => $"EXECUTE ({ConnectionName.ToSql()}) BEGIN ... END";
    }

    public class ExecutePushdownStatement : Statement
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

        public override string ToSql() 
        {
            var into = IntoTable != null ? $" INTO {IntoTable.ToSql()}" : "";
            var parameters = Parameters.Count > 0 ? " WITH (" + string.Join(", ", Parameters.Select(p => p.ToSql())) + ")" : "";
            return $"EXECUTE {ConnectionName.ToSql()}{into}{parameters} BEGIN\n{SqlText}\nEND;";
        }

        public override IEnumerable<string> GetSourceTables()
        {
            if (string.IsNullOrEmpty(SqlText)) return Enumerable.Empty<string>();

            var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            // Refined regex for identifying potential table names in FROM and JOIN clauses.
            // Handles multi-part identifiers like [db].[schema].[table] or schema.table
            var tableRegex = new Regex(@"(?i)\b(?:FROM|JOIN)\s+([\[\]\w\.-]+)", RegexOptions.Compiled);
            
            var matches = tableRegex.Matches(SqlText);
            foreach (Match m in matches)
            {
                var tbl = m.Groups[1].Value;
                if (!string.IsNullOrEmpty(tbl))
                {
                    // Clean up brackets for consistent lineage tracking
                    tbl = tbl.Replace("[", "").Replace("]", "");
                    
                    // Prefix with connection name if it's not already qualified by this connection
                    var connPrefix = ConnectionName.ToSql().Trim('\'', '(', ')');
                    if (tbl.StartsWith(connPrefix + ".", StringComparison.OrdinalIgnoreCase))
                    {
                        sources.Add(tbl);
                    }
                    else
                    {
                        sources.Add($"{connPrefix}.{tbl}");
                    }
                }
            }

            if (sources.Count == 0)
            {
                return new[] { $"Native SQL on {ConnectionName.ToSql()}" };
            }

            return sources;
        }
    }

    public class InsertStatement : Statement
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

        public override string ToSql()
        {
            var with = (Ctes != null && Ctes.Count > 0) ? "WITH " + string.Join(", ", Ctes.Select(c => $"{c.Name} AS ({c.Query.ToSql().TrimEnd(';')})")) + " " : "";
            var cols = Columns != null && Columns.Count > 0 ? "(" + string.Join(", ", Columns) + ") " : "";
            var output = Output != null ? " " + Output.ToSql() : "";
            if (SelectQuery != null)
            {
                return $"{with}INSERT INTO {TargetTable.ToSql()} {cols}{output}{SelectQuery.ToSql()}";
            }
            else
            {
                var vals = Values != null ? string.Join(", ", Values.Select(row => "(" + string.Join(", ", row.Select(v => v.ToSql())) + ")")) : "";
                return $"{with}INSERT INTO {TargetTable.ToSql()} {cols}{output}VALUES {vals};";
            }
        }
    }

    public class Assignment : AstNode
    {
        public string ColumnName { get; }
        public Expression Value { get; }

        public Assignment(string columnName, Expression value)
        {
            ColumnName = columnName;
            Value = value;
        }

        public string ToSql() => $"{ColumnName} = {Value.ToSql()}";
    }

    public class UpdateStatement : Statement
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

        public override string ToSql()
        {
            var with = (Ctes != null && Ctes.Count > 0) ? "WITH " + string.Join(", ", Ctes.Select(c => $"{c.Name} AS ({c.Query.ToSql().TrimEnd(';')})")) + " " : "";
            var sets = string.Join(", ", Assignments.Select(a => a.ToSql()));
            var from = FromTable != null ? $" FROM {FromTable.ToSql()}" : "";
            var joins = Joins != null && Joins.Count > 0 ? " " + string.Join(" ", Joins.Select(j => j.ToSql())) : "";
            var output = Output != null ? " " + Output.ToSql() : "";
            var where = WhereClause != null ? $" WHERE {WhereClause.ToSql()}" : "";
            return $"{with}UPDATE {TargetTable.ToSql()} SET {sets}{from}{joins}{output}{where};";
        }
    }

    public class DeleteStatement : Statement
    {
        public TableReference TargetTable { get; }
        public Expression? WhereClause { get; }
        public OutputClause? Output { get; set; }

        public DeleteStatement(TableReference targetTable, Expression? whereClause)
        {
            TargetTable = targetTable;
            WhereClause = whereClause;
        }

        public override string ToSql()
        {
            var with = (Ctes != null && Ctes.Count > 0) ? "WITH " + string.Join(", ", Ctes.Select(c => $"{c.Name} AS ({c.Query.ToSql().TrimEnd(';')})")) + " " : "";
            var output = Output != null ? " " + Output.ToSql() : "";
            var where = WhereClause != null ? $" WHERE {WhereClause.ToSql()}" : "";
            return $"{with}DELETE FROM {TargetTable.ToSql()}{output}{where};";
        }
    }

    public class TruncateTableStatement : Statement
    {
        public TableReference TargetTable { get; }

        public TruncateTableStatement(TableReference targetTable)
        {
            TargetTable = targetTable;
        }

        public override string ToSql() => $"TRUNCATE TABLE {TargetTable.ToSql()};";
    }

    public enum MergeActionType
    {
        UPDATE,
        INSERT,
        DELETE
    }

    public class MergeActionClause : AstNode
    {
        public MergeActionType ActionType { get; }
        public Expression? Condition { get; }
        public List<Assignment>? UpdateAssignments { get; }
        public List<string>? InsertColumns { get; }
        public List<Expression>? InsertValues { get; }

        public MergeActionClause(MergeActionType actionType, Expression? condition, 
            List<Assignment>? updateAssignments = null, 
            List<string>? insertColumns = null, 
            List<Expression>? insertValues = null)
        {
            ActionType = actionType;
            Condition = condition;
            UpdateAssignments = updateAssignments;
            InsertColumns = insertColumns;
            InsertValues = insertValues;
        }

        public string ToSql()
        {
            var cond = Condition != null ? $" AND {Condition.ToSql()}" : "";
            switch (ActionType)
            {
                case MergeActionType.UPDATE:
                    return $"THEN UPDATE SET {string.Join(", ", UpdateAssignments!.Select(a => a.ToSql()))}";
                case MergeActionType.INSERT:
                    var cols = InsertColumns != null && InsertColumns.Count > 0 ? "(" + string.Join(", ", InsertColumns) + ") " : "";
                    var vals = string.Join(", ", InsertValues!.Select(v => v.ToSql()));
                    return $"THEN INSERT {cols}VALUES ({vals})";
                case MergeActionType.DELETE:
                    return "THEN DELETE";
                default: return "";
            }
        }
    }

    public class MergeStatement : Statement
    {
        public TableReference TargetTable { get; }
        public TableReference SourceTable { get; } 
        public Expression OnCondition { get; }
        public List<MergeActionClause> MatchedClauses { get; } = new();
        public List<MergeActionClause> NotMatchedByTargetClauses { get; } = new();
        public List<MergeActionClause> NotMatchedBySourceClauses { get; } = new();

        public MergeStatement(TableReference targetTable, TableReference sourceTable, Expression onCondition)
        {
            TargetTable = targetTable;
            SourceTable = sourceTable;
            OnCondition = onCondition;
        }

        public override IEnumerable<string> GetSourceTables()
        {
            return SourceTable.GetSourceTables();
        }

        public override string ToSql()
        {
            var with = (Ctes != null && Ctes.Count > 0) ? "WITH " + string.Join(", ", Ctes.Select(c => $"{c.Name} AS ({c.Query.ToSql().TrimEnd(';')})")) + " " : "";
            var sb = new System.Text.StringBuilder();
            sb.Append(with);
            sb.AppendLine($"MERGE INTO {TargetTable.ToSql()}");
            sb.AppendLine($"USING {SourceTable.ToSql()}");
            sb.AppendLine($"ON {OnCondition.ToSql()}");
            foreach (var c in MatchedClauses) sb.AppendLine($"WHEN MATCHED {c.ToSql()}");
            foreach (var c in NotMatchedByTargetClauses) sb.AppendLine($"WHEN NOT MATCHED {c.ToSql()}");
            foreach (var c in NotMatchedBySourceClauses) sb.AppendLine($"WHEN NOT MATCHED BY SOURCE {c.ToSql()}");
            sb.Append(";");
            return sb.ToString();
        }
    }

    public class ForeignKeyReference : AstNode
    {
        public TableReference Table { get; }
        public List<string> Columns { get; }
        public ForeignKeyReference(TableReference table, List<string> columns)
        {
            Table = table;
            Columns = columns;
        }
        public string ToSql() => $"REFERENCES {Table.ToSql()}({string.Join(", ", Columns)})";
    }

    public abstract class TableConstraint : AstNode
    {
        public string? ConstraintName { get; set; }
        public abstract string ToSql();
    }

    public class TablePrimaryKeyConstraint : TableConstraint
    {
        public List<string> Columns { get; }
        public TablePrimaryKeyConstraint(List<string> columns) => Columns = columns;
        public override string ToSql() => $"{(ConstraintName != null ? $"CONSTRAINT {ConstraintName} " : "")}PRIMARY KEY ({string.Join(", ", Columns)})";
    }

    public class TableUniqueConstraint : TableConstraint
    {
        public List<string> Columns { get; }
        public TableUniqueConstraint(List<string> columns) => Columns = columns;
        public override string ToSql() => $"{(ConstraintName != null ? $"CONSTRAINT {ConstraintName} " : "")}UNIQUE ({string.Join(", ", Columns)})";
    }

    public class TableForeignKeyConstraint : TableConstraint
    {
        public List<string> Columns { get; }
        public ForeignKeyReference Reference { get; }
        public TableForeignKeyConstraint(List<string> columns, ForeignKeyReference reference)
        {
            Columns = columns;
            Reference = reference;
        }
        public override string ToSql() => $"{(ConstraintName != null ? $"CONSTRAINT {ConstraintName} " : "")}FOREIGN KEY ({string.Join(", ", Columns)}) {Reference.ToSql()}";
    }

    public class TableCheckConstraint : TableConstraint
    {
        public Expression Expression { get; }
        public TableCheckConstraint(Expression expression) => Expression = expression;
        public override string ToSql() => $"{(ConstraintName != null ? $"CONSTRAINT {ConstraintName} " : "")}CHECK ({Expression.ToSql()})";
    }

    public class ColumnDefinition
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

        public string ToSql()
        {
            var pk = IsPrimaryKey ? " PRIMARY KEY" : "";
            var unq = IsUnique ? " UNIQUE" : "";
            var nullable = !IsNullable ? " NOT NULL" : "";
            var identity = IsIdentity ? " IDENTITY" : "";
            var def = DefaultExpression != null ? $" DEFAULT {DefaultExpression.ToSql()}" : "";
            var check = CheckConstraint != null ? $" CHECK ({CheckConstraint.ToSql()})" : "";
            var fk = ForeignKey != null ? $" {ForeignKey.ToSql()}" : "";
            
            var tags = "";
            if (Metadata.Count > 0)
            {
                tags = " /* " + string.Join(" ", Metadata.Select(kv => $"@{kv.Key}: {kv.Value}")) + " */";
            }
            
            return $"{ColumnName} {DataType}{pk}{unq}{nullable}{identity}{def}{check}{fk}{tags}";
        }
    }

    public class CreateTableStatement : Statement
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

        public override string ToSql()
        {
            var ifNot = IfNotExists ? "IF NOT EXISTS " : "";
            var items = new List<string>(Columns.Select(c => c.ToSql()));
            items.AddRange(TableConstraints.Select(tc => tc.ToSql()));
            
            return $"{ifNot}CREATE TABLE {TargetTable.ToSql()} ({string.Join(", ", items)});";
        }
    }

    public enum AlterTableActionType { ADD, DROP_COLUMN, RENAME_COLUMN }

    public class AlterTableStatement : Statement
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

        public override string ToSql()
        {
            var sql = $"ALTER TABLE {TargetTable.ToSql()} ";
            switch (Action)
            {
                case AlterTableActionType.ADD:
                    sql += $"ADD {NewColumn!.ToSql()}";
                    break;
                case AlterTableActionType.DROP_COLUMN:
                    sql += $"DROP COLUMN {ColumnToDelete}";
                    break;
                case AlterTableActionType.RENAME_COLUMN:
                    sql += $"RENAME COLUMN {OldColumnName} TO {NewColumnName}";
                    break;
            }
            return sql + ";";
        }
    }

    public class DropTableStatement : Statement
    {
        public TableReference TargetTable { get; }
        public bool IfExists { get; }

        public DropTableStatement(TableReference targetTable, bool ifExists)
        {
            TargetTable = targetTable;
            IfExists = ifExists;
        }

        public override string ToSql() => $"DROP TABLE {(IfExists ? "IF EXISTS " : "")}{TargetTable.ToSql()};";
    }

    public class DropConnectionStatement : Statement
    {
        public string ConnectionName { get; }
        public bool IfExists { get; }
        public DropConnectionStatement(string name, bool ifExists) { ConnectionName = name; IfExists = ifExists; }
        public override string ToSql() => $"DROP CONNECTION {(IfExists ? "IF EXISTS " : "")}{ConnectionName};";
    }

    public class ClearSessionStatement : Statement
    {
        public override string ToSql() => "CLEAR SESSION;";
    }

    public class DropProcedureStatement : Statement
    {
        public string ProcedureName { get; }
        public bool IfExists { get; }
        public DropProcedureStatement(string name, bool ifExists) { ProcedureName = name; IfExists = ifExists; }
        public override string ToSql() => $"DROP PROCEDURE {(IfExists ? "IF EXISTS " : "")}{ProcedureName};";
    }

    public class DropFunctionStatement : Statement
    {
        public string FunctionName { get; }
        public bool IfExists { get; }
        public DropFunctionStatement(string name, bool ifExists) { FunctionName = name; IfExists = ifExists; }
        public override string ToSql() => $"DROP FUNCTION {(IfExists ? "IF EXISTS " : "")}{FunctionName};";
    }

    public class DropIndexStatement : Statement
    {
        public string IndexName { get; }
        public TableReference? Table { get; }
        public bool IfExists { get; }
        public DropIndexStatement(string name, TableReference? table, bool ifExists) { IndexName = name; Table = table; IfExists = ifExists; }
        public override string ToSql() => $"DROP INDEX {(IfExists ? "IF EXISTS " : "")}{IndexName}{(Table != null ? " ON " + Table.ToSql() : "")};";
    }

    public class DeclareStatement : Statement
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

        public override string ToSql()
        {
            var init = InitialValue != null ? $" = {InitialValue.ToSql()}" : "";
            var pass = IsSensitive ? " PASSWORD" : "";
            var input = IsInput ? " INPUT" : "";
            var output = IsOutput ? " OUTPUT" : "";
            return $"DECLARE {VariableName} {DataType}{init}{pass}{input}{output};";
        }
    }

    public class DockerStatement : Statement
    {
        public Expression ImageName { get; }
        public string? Alias { get; }
        public DockerStatement(Expression imageName, string? alias = null)
        {
            ImageName = imageName;
            Alias = alias;
        }
        public override string ToSql() => Alias != null ? $"USE DOCKER({ImageName.ToSql()}) AS {Alias};" : $"USE DOCKER({ImageName.ToSql()});";
    }

    public class RunScriptStatement : Statement
    {
        public Expression PathExpression { get; }
        public Dictionary<string, Expression> Parameters { get; }

        public RunScriptStatement(Expression path, Dictionary<string, Expression> parameters)
        {
            PathExpression = path;
            Parameters = parameters;
        }

        public override string ToSql()
        {
            var paramsStr = Parameters.Count > 0 ? " WITH (" + string.Join(", ", Parameters.Select(p => $"{p.Key} = {p.Value.ToSql()}")) + ")" : "";
            return $"RUN SCRIPT {PathExpression.ToSql()}{paramsStr};";
        }
    }

    public class SetVariableStatement : Statement
    {
        public string VariableName { get; }
        public Expression Value { get; }

        public SetVariableStatement(string variableName, Expression value)
        {
            VariableName = variableName;
            Value = value;
        }

        public override string ToSql() => $"SET {VariableName} = {Value.ToSql()};";
    }

    public class BlockStatement : Statement
    {
        public List<Statement> Statements { get; }

        public BlockStatement(List<Statement> statements)
        {
            Statements = statements;
        }

        public override string ToSql() => "BEGIN ... END";
    }

    public class WhileStatement : Statement
    {
        public Expression Condition { get; }
        public Statement Body { get; }

        public WhileStatement(Expression condition, Statement body)
        {
            Condition = condition;
            Body = body;
        }

        public override string ToSql() => $"WHILE {Condition.ToSql()} BEGIN ... END";
    }

    public class ForStatement : Statement
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

        public override string ToSql() => $"FOR {VariableName} = {StartValue.ToSql()} TO {EndValue.ToSql()} BEGIN ... END";
    }

    public class ForeachStatement : Statement
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

        public override string ToSql() => $"FOREACH {VariableName} IN {ListExpression.ToSql()} BEGIN ... END";
    }

    public class ElseIfClause : AstNode
    {
        public Expression Condition { get; }
        public Statement Body { get; }

        public ElseIfClause(Expression condition, Statement body)
        {
            Condition = condition;
            Body = body;
        }

        public string ToSql() => $"ELSE IF {Condition.ToSql()} BEGIN ... END";
    }

    public class IfStatement : Statement
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

        public override string ToSql()
        {
            var sql = $"IF {Condition.ToSql()} BEGIN ... END";
            if (ElseIfClauses != null)
            {
                foreach (var ei in ElseIfClauses) sql += " " + ei.ToSql();
            }
            if (ElseBody != null)
            {
                sql += " ELSE BEGIN ... END";
            }
            return sql;
        }
    }

    public class PrintStatement : Statement
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

        public override string ToSql()
        {
            var ts = ShowTimestamp != null ? $", {ShowTimestamp.ToSql()}" : "";
            var fmt = TimestampFormat != null ? $", {TimestampFormat.ToSql()}" : "";
            return $"PRINT({Message.ToSql()}{ts}{fmt});";
        }
    }

    /// <summary>WAITFOR DELAY '00:00:05' — pauses execution for the specified interval.</summary>
    public class WaitForStatement : Statement
    {
        /// <summary>The delay expression — a string literal in 'hh:mm:ss[.ms]' format or a variable.</summary>
        public Expression DelayExpression { get; }

        public WaitForStatement(Expression delayExpression)
        {
            DelayExpression = delayExpression;
        }

        public override string ToSql() => $"WAITFOR DELAY {DelayExpression.ToSql()};";
    }

    public class RaiseErrorStatement : Statement
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

        public override string ToSql()
        {
            var loc = CodeLocation != null ? $", {CodeLocation.ToSql()}" : "";
            var paramsStr = Parameters.Count > 0 ? ", " + string.Join(", ", Parameters.Select(p => p.ToSql())) : "";
            return $"RAISEERROR({Message.ToSql()}, {Severity.ToSql()}{loc}{paramsStr});";
        }
    }

    public class ExecuteParameter
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

        public string ToSql() => Expression.ToSql() + (IsOutput ? " OUTPUT" : "") + (IsInput ? " INPUT" : "");
    }

    public class ExecuteStatement : Statement
    {
        public string ProcedureName { get; }
        public List<ExecuteParameter> Parameters { get; }

        public ExecuteStatement(string procedureName, List<ExecuteParameter> parameters)
        {
            ProcedureName = procedureName;
            Parameters = parameters;
        }

        public override string ToSql()
        {
            var paramsStr = Parameters.Count > 0 ? " " + string.Join(", ", Parameters.Select(p => p.ToSql())) : "";
            return $"EXECUTE {ProcedureName}{paramsStr};";
        }
    }

    public class ParallelStatement : Statement
    {
        public BlockStatement Body { get; }
        public int ConcurrencyLimit { get; set; } = 0; // 0 means no limit (all tasks)

        public ParallelStatement(BlockStatement body, int concurrencyLimit = 0)
        {
            Body = body;
            ConcurrencyLimit = concurrencyLimit;
        }

        public override string ToSql() => "PARALLEL " + (ConcurrencyLimit > 0 ? $"({ConcurrencyLimit}) " : "") + Body.ToSql();
    }

    public class BulkInsertStatement : Statement
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

        public override string ToSql()
        {
            var cols = Columns != null && Columns.Count > 0 ? "(" + string.Join(", ", Columns) + ") " : "";
            var optionsStr = Options.Count > 0 
                ? $" WITH ({string.Join(", ", Options.Select(o => $"{o.Key} = {o.Value.ToSql()}"))})" 
                : "";
            return $"BULK INSERT {TargetTable.ToSql()} {cols}FROM '{FilePath}'{optionsStr};";
        }
    }

    public class CreateProcedureStatement : Statement
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

        public override string ToSql()
        {
            var modeStr = Mode switch {
                ObjectCreationMode.Alter => "ALTER",
                ObjectCreationMode.CreateOrAlter => "CREATE OR ALTER",
                _ => "CREATE"
            };
            var paramsStr = string.Join(", ", Parameters.Select(p => p.ToSql()));
            return $"{modeStr} PROCEDURE {ProcedureName} ({paramsStr}) AS BEGIN ... END;";
        }
    }

    public class CreateFunctionStatement : Statement
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

        public override string ToSql()
        {
            var modeStr = Mode switch {
                ObjectCreationMode.Alter => "ALTER",
                ObjectCreationMode.CreateOrAlter => "CREATE OR ALTER",
                _ => "CREATE"
            };
            var paramsStr = string.Join(", ", Parameters.Select(p => p.ToSql()));
            return $"{modeStr} FUNCTION {FunctionName} ({paramsStr}) RETURNS {ReturnType} AS BEGIN ... END;";
        }
    }

    public class ParameterDefinition : AstNode
    {
        public string Name { get; }
        public string DataType { get; }

        public ParameterDefinition(string name, string dataType)
        {
            Name = name;
            DataType = dataType;
        }

        public string ToSql() => $"{Name} {DataType}";
    }

    public class BeginTransactionStatement : Statement
    {
        public string? Name { get; }
        public BeginTransactionStatement(string? name = null) => Name = name;
        public override string ToSql() => Name != null ? $"BEGIN TRANSACTION {Name};" : "BEGIN TRANSACTION;";
    }

    public class CommitTransactionStatement : Statement
    {
        public string? Name { get; }
        public CommitTransactionStatement(string? name = null) => Name = name;
        public override string ToSql() => Name != null ? $"COMMIT TRANSACTION {Name};" : "COMMIT TRANSACTION;";
    }

    public class RollbackTransactionStatement : Statement
    {
        public string? Name { get; }
        public RollbackTransactionStatement(string? name = null) => Name = name;
        public override string ToSql() => Name != null ? $"ROLLBACK TRANSACTION {Name};" : "ROLLBACK TRANSACTION;";
    }

    public class ContinueStatement : Statement
    {
        public override string ToSql() => "CONTINUE;";
    }

    public class ThrowStatement : Statement
    {
        public Expression? Message { get; }
        public ThrowStatement(Expression? message = null) => Message = message;
        public override string ToSql() => Message != null ? $"THROW {Message.ToSql()};" : "THROW;";
    }

    public class TryCatchStatement : Statement
    {
        public Statement TryBody { get; }
        public Statement CatchBody { get; }

        public TryCatchStatement(Statement tryBody, Statement catchBody)
        {
            TryBody = tryBody;
            CatchBody = catchBody;
        }

        public override string ToSql() => "TRY ... CATCH ... END";
    }


    public class ReturnStatement : Statement
    {
        public Expression? ReturnValue { get; }

        public ReturnStatement(Expression? returnValue = null)
        {
            ReturnValue = returnValue;
        }

        public override string ToSql() => ReturnValue != null ? $"RETURN {ReturnValue.ToSql()};" : "RETURN;";
    }

    public class BreakStatement : Statement 
    {
        public override string ToSql() => "BREAK;";
    }

    public abstract class Expression : AstNode 
    { 
        public virtual string ToSql() => "UNKNOWN EXPRESSION";
        public virtual IEnumerable<string> GetSourceTables() => Enumerable.Empty<string>();
        public virtual IEnumerable<string> GetSourceColumns() => Enumerable.Empty<string>();
    }

    public class UnaryExpression : Expression
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

        public override string ToSql()
        {
            string op = Operator switch {
                TokenType.NOT => "NOT ",
                TokenType.MINUS => "-",
                TokenType.PLUS => "+",
                _ => Operator.ToString()
            };
            return $"{op}{Expression.ToSql()}";
        }
    }

    public class BinaryExpression : Expression
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

        public override string ToSql()
        {
            string op = Operator switch
            {
                TokenType.PLUS => "+",
                TokenType.MINUS => "-",
                TokenType.STAR => "*",
                TokenType.SLASH => "/",
                TokenType.MODULO => "%",
                TokenType.EQUALS => "=",
                TokenType.NOT_EQUALS => "<>",
                TokenType.LESS_THAN => "<",
                TokenType.LESS_EQUALS => "<=",
                TokenType.GREATER_THAN => ">",
                TokenType.GREATER_EQUALS => ">=",
                TokenType.AND => "AND",
                TokenType.OR => "OR",
                _ => Operator.ToString()
            };
            return $"({Left.ToSql()} {op} {Right.ToSql()})";
        }
    }

    public class LiteralExpression : Expression
    {
        public object? Value { get; }
        public TokenType Type { get; }

        public LiteralExpression(object? value, TokenType type)
        {
            Value = value;
            Type = type;
        }

        public override string ToSql()
        {
            if (Value == null) return "NULL";
            string valStr = Value.ToString() ?? "";
            if (Type == TokenType.TRUE) return "TRUE";
            if (Type == TokenType.FALSE) return "FALSE";
            if (Type == TokenType.STRING) return $"'{valStr.Replace("'", "''")}'";
            return valStr;
        }
    }

    public class IdentifierExpression : Expression
    {
        public string Name { get; }

        public IdentifierExpression(string name)
        {
            Name = name;
        }

        public override string ToSql() => Name;
        public override IEnumerable<string> GetSourceColumns() => new[] { Name.Split('.').Last() };
        public override IEnumerable<string> GetSourceTables() => Name.Contains('.') ? new[] { Name.Split('.')[0] } : Enumerable.Empty<string>();
    }

    public class MemberAccessExpression : Expression
    {
        public Expression Expression { get; }
        public string MemberName { get; }

        public MemberAccessExpression(Expression expression, string memberName)
        {
            Expression = expression;
            MemberName = memberName;
        }

        public override string ToSql() => $"{Expression.ToSql()}.{MemberName}";
        public override IEnumerable<string> GetSourceTables() => Expression.GetSourceTables();
        public override IEnumerable<string> GetSourceColumns() => new[] { MemberName };
    }

    public class SubqueryExpression : Expression
    {
        public Statement Query { get; }

        public SubqueryExpression(Statement query)
        {
            Query = query;
        }

        public override string ToSql() => $"({Query.ToSql().TrimEnd(';')})";
    }

    public class VariableExpression : Expression
    {
        public string Name { get; }

        public VariableExpression(string name)
        {
            Name = name;
        }

        public override string ToSql() => Name;
    }

    public class FunctionCallExpression : Expression
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

        public override string ToSql()
        {
            var distinct = IsDistinct ? "DISTINCT " : "";
            var args = string.Join(", ", Arguments.Select(a => a.ToSql()));
            var sql = $"{FunctionName}({distinct}{args})";
            if (WithinGroupOrderBy != null)
            {
                sql += $" WITHIN GROUP (ORDER BY {string.Join(", ", WithinGroupOrderBy.Select(o => o.ToSql()))})";
            }
            if (Window != null)
            {
                sql += $" OVER ({Window.ToSql()})";
            }
            return sql;
        }
    }

    public class ListExpression : Expression
    {
        public List<Expression> Items { get; }

        public ListExpression(List<Expression> items)
        {
            Items = items;
        }

        public override string ToSql() => "(" + string.Join(", ", Items.Select(i => i.ToSql())) + ")";
    }

    public class IsNullExpression : Expression
    {
        public Expression Expression { get; }
        public bool Not { get; }

        public IsNullExpression(Expression expression, bool isNot)
        {
            Expression = expression;
            Not = isNot;
        }

        public override string ToSql() => $"{Expression.ToSql()} IS {(Not ? "NOT " : "")}NULL";
    }

    public class ExportStatement : Statement
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

        public override string ToSql() => $"EXPORT {Source.ToSql()} TO '{TargetPath}'" + (Options != null ? " WITH (...)" : "");
    }

    public class HelpStatement : Statement
    {
        public string? Topic { get; }
        public string? SubTopic { get; }

        public HelpStatement(string? topic, string? subTopic = null)
        {
            Topic = topic;
            SubTopic = subTopic;
        }

        public override string ToSql() => $"HELP {(Topic != null ? Topic + (SubTopic != null ? " " + SubTopic : "") : "")}";
    }

    public class InExpression : Expression
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

        public override string ToSql() => $"{Left.ToSql()} {(IsNot ? "NOT " : "")}IN {Right.ToSql()}";
    }

    public class LineageStatement : Statement
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

        public override string ToSql() 
        {
            var sql = $"LINEAGE {TargetTable.ToSql()}";
            if (ColumnName != null) sql += $", {ColumnName}";
            if (ExportPath != null) sql += $" TO '{ExportPath}'";
            return sql;
        }
    }

    public class EmailStatement : Statement
    {
        public Expression To { get; }
        public Expression Subject { get; }
        public Expression Body { get; }
        public Expression? ConnectionName { get; set; }
        public List<Expression>? Attachments { get; set; }
        public List<Expression>? Cc { get; set; }
        public List<Expression>? Bcc { get; set; }

        public EmailStatement(Expression to, Expression subject, Expression body, Expression? connectionName = null)
        {
            To = to;
            Subject = subject;
            Body = body;
            ConnectionName = connectionName;
        }

        public override string ToSql()
        {
            var sql = $"SEND_EMAIL TO {To.ToSql()} SUBJECT {Subject.ToSql()} BODY {Body.ToSql()}";
            if (Cc != null && Cc.Count > 0) sql += " CC " + string.Join(", ", Cc.Select(e => e.ToSql()));
            if (Bcc != null && Bcc.Count > 0) sql += " BCC " + string.Join(", ", Bcc.Select(e => e.ToSql()));
            if (Attachments != null && Attachments.Count > 0) sql += " ATTACH " + string.Join(", ", Attachments.Select(e => e.ToSql()));
            if (ConnectionName != null) sql += $" AT {ConnectionName.ToSql()}";
            return sql + ";";
        }
    }

    public class LikeExpression : Expression
    {
        public Expression Left { get; }
        public Expression Pattern { get; }
        public bool IsNot { get; }

        public LikeExpression(Expression left, Expression pattern, bool isNot = false)
        {
            Left = left;
            Pattern = pattern;
            IsNot = isNot;
        }

        public override string ToSql() => $"{Left.ToSql()} {(IsNot ? "NOT " : "")}LIKE {Pattern.ToSql()}";
    }

    public class ExistsExpression : Expression
    {
        public Statement Subquery { get; }
        public bool IsNot { get; }

        public ExistsExpression(Statement subquery, bool isNot = false)
        {
            Subquery = subquery;
            IsNot = isNot;
        }

        public override string ToSql() => $"{(IsNot ? "NOT " : "")}EXISTS ({Subquery.ToSql()})";
    }

    public class CaseExpression : Expression
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

        public override string ToSql()
        {
            var sql = "CASE ";
            foreach (var clause in WhenClauses)
            {
                sql += $"WHEN {clause.Condition.ToSql()} THEN {clause.Result.ToSql()} ";
            }
            if (ElseResult != null)
            {
                sql += $"ELSE {ElseResult.ToSql()} ";
            }
            sql += "END";
            return sql;
        }
    }
    public class AtTimeZoneExpression : Expression
    {
        public Expression Left { get; }
        public Expression TimeZone { get; }

        public AtTimeZoneExpression(Expression left, Expression timeZone)
        {
            Left = left;
            TimeZone = timeZone;
        }

        public override string ToSql() => $"{Left.ToSql()} AT TIME ZONE {TimeZone.ToSql()}";

        public override IEnumerable<string> GetSourceTables() => Left.GetSourceTables();
        public override IEnumerable<string> GetSourceColumns() => Left.GetSourceColumns();
    }

    public enum WindowFrameType { ROWS, RANGE }
    public enum WindowFrameBoundType { PRECEDING, FOLLOWING, CURRENT_ROW, UNBOUNDED_PRECEDING, UNBOUNDED_FOLLOWING }

    public class WindowFrame : AstNode
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

        public string ToSql()
        {
            string start = BoundToSql(StartBound, StartValue);
            if (EndBound == null) return $"{Type} {start}";
            return $"{Type} BETWEEN {start} AND {BoundToSql(EndBound.Value, EndValue)}";
        }

        private string BoundToSql(WindowFrameBoundType bound, Expression? value)
        {
            return bound switch
            {
                WindowFrameBoundType.PRECEDING => $"{value?.ToSql()} PRECEDING",
                WindowFrameBoundType.FOLLOWING => $"{value?.ToSql()} FOLLOWING",
                WindowFrameBoundType.CURRENT_ROW => "CURRENT ROW",
                WindowFrameBoundType.UNBOUNDED_PRECEDING => "UNBOUNDED PRECEDING",
                WindowFrameBoundType.UNBOUNDED_FOLLOWING => "UNBOUNDED FOLLOWING",
                _ => ""
            };
        }
    }


    public class WindowClause : AstNode
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

        public string ToSql()
        {
            var parts = new List<string>();
            if (PartitionBy.Count > 0)
            {
                parts.Add("PARTITION BY " + string.Join(", ", PartitionBy.Select(p => p.ToSql())));
            }
            if (OrderBy.Count > 0)
            {
                parts.Add("ORDER BY " + string.Join(", ", OrderBy.Select(o => o.ToSql())));
            }
            if (Frame != null)
            {
                parts.Add(Frame.ToSql());
            }
            return string.Join(" ", parts);
        }
    }

    public class CreateIndexStatement : Statement
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

        public override string ToSql()
        {
            var unique = IsUnique ? "UNIQUE " : "";
            return $"CREATE {unique}INDEX {IndexName} ON {TargetTable.ToSql()} ({string.Join(", ", Columns)});";
        }
    }

    public class ExplainStatement : Statement
    {
        public Statement Query { get; }

        public ExplainStatement(Statement query)
        {
            Query = query;
        }

        public override string ToSql()
        {
            return $"EXPLAIN {Query.ToSql()}";
        }
    }

    public enum FileOpType { Copy, Move, Rename, Delete, Compress, Encrypt, Decrypt }

    public class FileOperationStatement : Statement
    {
        public FileOpType Type { get; }
        public Expression Source { get; }
        public Expression? Destination { get; }

        public FileOperationStatement(FileOpType type, Expression source, Expression? destination = null)
        {
            Type = type;
            Source = source;
            Destination = destination;
        }

        public override string ToSql()
        {
            var op = Type.ToString().ToUpper() + "_FILE";
            var dest = Destination != null ? ", " + Destination.ToSql() : "";
            return $"{op}({Source.ToSql()}{dest});";
        }
    }

    public enum DirectoryOpType { Create, Delete, Rename, Move, Copy, DeleteContents }

    public class DirectoryOperationStatement : Statement
    {
        public DirectoryOpType Type { get; }
        public Expression Path { get; }
        public Expression? NewNameOrDest { get; }

        public DirectoryOperationStatement(DirectoryOpType type, Expression path, Expression? newNameOrDest = null)
        {
            Type = type;
            Path = path;
            NewNameOrDest = newNameOrDest;
        }

        public override string ToSql()
        {
            var op = Type.ToString().ToUpper() + "_DIRECTORY";
            var extra = NewNameOrDest != null ? ", " + NewNameOrDest.ToSql() : "";
            return $"{op}({Path.ToSql()}{extra});";
        }
    }

    public enum FileTransferType { Send, Receive }

    public class FileTransferStatement : Statement
    {
        public FileTransferType Type { get; set; }
        public Expression LocalPath { get; set; } = null!;
        public string ConnectionName { get; set; } = "";
        public Expression RemotePath { get; set; } = null!;

        public override string ToSql()
        {
            var op = Type == FileTransferType.Send ? "SEND_FILE" : "RECEIVE_FILE";
            if (Type == FileTransferType.Send)
                return $"{op} {LocalPath.ToSql()}, {ConnectionName}, {RemotePath.ToSql()};";
            else
                return $"{op} {ConnectionName}, {RemotePath.ToSql()}, {LocalPath.ToSql()};";
        }
    }

    public enum DockerAction { Start, Stop, Pause, Resume, Close }

    public class DockerActionStatement : Statement
    {
        public string Alias { get; }
        public DockerAction Action { get; }
        public DockerActionStatement(string alias, DockerAction action) { Alias = alias; Action = action; }
        public override string ToSql() => $"{Action.ToString().ToUpper()}_DOCKER {Alias};";
    }

    public class DockerCloseStatement : Statement
    {
        public Expression? ImageName { get; }
        public string? Alias { get; }
        public DockerCloseStatement(Expression? imageName = null, string? alias = null) 
        { 
            ImageName = imageName; 
            Alias = alias;
        }
        public override string ToSql() 
        {
            if (Alias != null) return $"CLOSE_DOCKER {Alias};";
            return ImageName != null ? $"CLOSE_DOCKER {ImageName.ToSql()};" : "CLOSE_DOCKER;";
        }
    }

    public class CreateJobStatement : Statement
    {
        public string JobName { get; }
        public ScheduleInfo Schedule { get; }
        public Statement Script { get; }

        public CreateJobStatement(string jobName, ScheduleInfo schedule, Statement script)
        {
            JobName = jobName;
            Schedule = schedule;
            Script = script;
        }

        public override string ToSql()
        {
            return $"CREATE JOB {JobName} ON SCHEDULE {Schedule.ToSql()} AS {Script.ToSql()}";
        }
    }

    public class ScheduleInfo : AstNode
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

        public string ToSql()
        {
            var at = AtTime != null ? $" AT '{AtTime}'" : "";
            return $"EVERY {Interval} {Unit}{at}";
        }
    }

    public class ShowJobHistoryStatement : Statement
    {
        public string? JobName { get; }
        public ShowJobHistoryStatement(string? jobName = null) { JobName = jobName; }
        public override string ToSql() => JobName != null ? $"SHOW JOB HISTORY {JobName};" : "SHOW JOB HISTORY;";
    }

    public class ShowJobsStatement : Statement
    {
        public override string ToSql() => "SHOW JOBS;";
    }

    public class LintStatement : Statement
    {
        public string? ScriptPath { get; }

        public LintStatement(string? scriptPath = null)
        {
            ScriptPath = scriptPath;
        }

        public override string ToSql() => ScriptPath != null ? $"LINT '{ScriptPath}';" : "LINT;";
    }

    /// <summary>A single variable assignment inside a CREATE SETS block.</summary>
    public class SetsAssignment
    {
        public string VariableName { get; }
        public Expression Value { get; }
        public SetsAssignment(string variableName, Expression value) { VariableName = variableName; Value = value; }
    }

    /// <summary>CREATE SETS !&lt;name&gt; BEGIN @var = val, ... [SET WITH_PROMPT ON;] END</summary>
    public class CreateSetsStatement : Statement
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

        public override string ToSql()
        {
            var assignments = string.Join(",\n    ", Assignments.Select(a => $"@{a.VariableName} = {a.Value.ToSql()}"));
            var prompt = WithPrompt ? "\n    SET WITH_PROMPT ON;" : "";
            return $"CREATE SETS !{Name}\nBEGIN\n    {assignments};{prompt}\nEND";
        }
    }

    /// <summary>DROP SETS [IF EXISTS] !&lt;name&gt;</summary>
    public class DropSetsStatement : Statement
    {
        public string Name { get; }
        public bool IfExists { get; }

        public DropSetsStatement(string name, bool ifExists) { Name = name; IfExists = ifExists; }

        public override string ToSql() => IfExists ? $"DROP SETS IF EXISTS !{Name};" : $"DROP SETS !{Name};";
    }

    /// <summary>USE SETS !<name></summary>
    public class UseSetsStatement : Statement
    {
        public string Name { get; }
        public UseSetsStatement(string name) { Name = name; }
        public override string ToSql() => $"USE SETS !{Name};";
    }

    /// <summary>USE PASSWORD = 'password'</summary>
    public class UsePasswordStatement : Statement
    {
        public string Password { get; }
        public UsePasswordStatement(string password) { Password = password; }
        
        /// <summary>
        /// Converts the statement to its SQL string, optionally masking the password for security.
        /// When masked, results in: USE PASSWORD = '********';
        /// </summary>
        public string ToSql(bool mask) => $"USE PASSWORD = '{(mask ? "********" : Password.Replace("'", "''"))}';";
        public override string ToSql() => ToSql(true); // Default to masked for safety in serialization
    }

    /// <summary>SET SHOW_PASSWORD ON/OFF</summary>
    public class SetShowPasswordStatement : Statement
    {
        public bool Enabled { get; }
        public SetShowPasswordStatement(bool enabled) { Enabled = enabled; }
        public override string ToSql() => $"SET SHOW_PASSWORD {(Enabled ? "ON" : "OFF")};";
    }
}

