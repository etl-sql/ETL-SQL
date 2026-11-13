using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Engines;

/// <summary>
/// The column names on each side of one join step. An outer join returns a row with no match
/// carrying its own side only; this fills in the other side's names as NULL, so the row has the
/// same columns as a matched row and a name neither side defines is still an unknown column.
/// <para>
/// A side's names come from the first row seen on it. A side with no rows falls back to the names
/// its table reference declares (<see cref="DeclaredColumnsAsync"/>): a select list, a temp table,
/// a <c>PIVOT</c> list, the names a table function returned when it ran, or the source's own
/// header or catalog, a <c>PIVOT</c> or <c>MATCH_RECOGNIZE</c> worked out from its clause, or the
/// names any table function or table operator returned when this statement read it (a database
/// connection's table function, a dynamic pivot). When none is known, the row is marked
/// <see cref="Row.MayLackColumns"/> and an unknown name on it reads NULL.
/// </para>
/// </summary>
internal sealed class OuterJoinShape
{
    private List<string>? _left;
    private List<string>? _right;
    private bool _leftSeen;
    private bool _rightSeen;

    public void DeclareLeft(List<string>? names) { if (!_leftSeen && names != null) _left = names; }
    public void DeclareRight(List<string>? names) { if (!_rightSeen && names != null) _right = names; }

    /// <summary>Each name, bare and qualified by <paramref name="qualifier"/>.</summary>
    public static List<string> Qualified(IEnumerable<string> columns, string? qualifier)
    {
        var names = new List<string>();
        foreach (var column in columns)
        {
            names.Add(column);
            if (!string.IsNullOrEmpty(qualifier) && !column.Contains('.')) names.Add($"{qualifier}.{column}");
        }
        return names;
    }

    /// <summary>
    /// Each name, bare and qualified by every name the side answers to: its alias (or table name)
    /// and the alias of each table operator on it (<c>PIVOT (...) AS p</c>, <c>MATCH_RECOGNIZE (...)
    /// AS m</c>), which the parser keeps on the operator, not on the table reference.
    /// </summary>
    public static List<string> Qualified(IEnumerable<string> columns, TableReference table)
    {
        var list = columns as IReadOnlyCollection<string> ?? columns.ToList();
        var names = Qualified(list, table.Alias ?? table.TableName);
        foreach (var alias in table.TableOperators.Select(OperatorAlias))
        {
            if (string.IsNullOrEmpty(alias) || alias.Equals(table.Alias ?? table.TableName, StringComparison.OrdinalIgnoreCase)) continue;
            names.AddRange(list.Where(c => !c.Contains('.')).Select(c => $"{alias}.{c}"));
        }
        return names;
    }

    private static string? OperatorAlias(AstNode tableOperator) => tableOperator switch
    {
        PivotClause pivot => pivot.Alias,
        UnpivotClause unpivot => unpivot.Alias,
        DuckPivotClause duck => duck.Alias,
        MatchRecognizeClause matchRecognize => matchRecognize.Alias,
        _ => null,
    };

    /// <summary>True once the right side's column names are known.</summary>
    public bool RightKnown => _right != null;

    /// <summary>True once the left side's column names are known.</summary>
    public bool LeftKnown => _left != null;

    public void SeeLeft(Row row)
    {
        if (_leftSeen) return;
        _left = NamesOf(row);
        _leftSeen = true;
    }

    public void SeeRight(Row row)
    {
        if (_rightSeen) return;
        _right = NamesOf(row);
        _rightSeen = true;
    }

    /// <summary>A copy of <paramref name="row"/> with every column of both sides it lacks set to NULL.</summary>
    public Row Unmatched(Row row)
    {
        var copy = row.Clone();
        Complete(copy);
        return copy;
    }

    /// <summary>Sets every column of both sides that <paramref name="row"/> lacks to NULL, in place.</summary>
    public void Complete(Row row)
    {
        Pad(row, _left);
        Pad(row, _right);
        if (_left == null || _right == null) row.MayLackColumns = true;
    }

    private static void Pad(Row row, List<string>? names)
    {
        if (names == null) return;
        foreach (var name in names)
            if (!row.HasColumn(name)) row[name] = null;
    }

    private static List<string> NamesOf(Row row)
    {
        var names = new List<string>();
        row.ForEachColumn((name, _) => names.Add(name));
        if (row.Schema != null)
        {
            for (var i = 0; i < row.Schema.ColumnCount; i++)
                names.AddRange(row.Schema.EnumerateAliasesOf(row.Schema.GetName(i)));
        }
        return names;
    }

    /// <summary>
    /// The column names the given table references declare, bare and qualified by alias, or null
    /// when any of them cannot be known without reading rows.
    /// <para>
    /// With <paramref name="askSources"/> a file is asked for its header and a database for its
    /// catalog, and a <c>PIVOT</c> or <c>UNPIVOT</c> is worked out from its source. A join asks
    /// this way only once a side has returned no rows, so a join with rows pays nothing for it.
    /// </para>
    /// </summary>
    public static async Task<List<string>?> DeclaredColumnsAsync(
        IEnumerable<TableReference> tables, IExecutionContext context, bool askSources = false)
    {
        var names = new List<string>();
        foreach (var table in tables)
        {
            var columns = await DeclaredColumnsAsync(table, context, depth: 0, askSources);
            if (columns == null) return null;
            names.AddRange(Qualified(columns, table));
        }
        return names;
    }

    private static async Task<List<string>?> DeclaredColumnsAsync(TableReference table, IExecutionContext context, int depth, bool askSources)
    {
        if (depth > 8) return null;
        if (table.TableOperators.Count == 0) return await SourceColumnsAsync(table, context, depth, askSources);
        if (!askSources) return null;

        // What this reference returned when the statement read it: exact, even for a dynamic pivot.
        if (context.ReturnedColumns(table) is { Count: > 0 } returned) return returned.ToList();

        // PIVOT, UNPIVOT and MATCH_RECOGNIZE: the names follow from the source's names and the clause.
        var columns = await SourceColumnsAsync(table, context, depth, askSources);
        foreach (var tableOperator in table.TableOperators)
        {
            if (columns == null) return null;
            columns = await PivotEngine.DeclaredColumnsAsync(columns, tableOperator, context);
        }
        return columns;
    }

    /// <summary>The columns of the table reference itself, before any <c>PIVOT</c> or <c>UNPIVOT</c>.</summary>
    private static async Task<List<string>?> SourceColumnsAsync(TableReference table, IExecutionContext context, int depth, bool askSources)
    {
        // A table function has to run to say what it returns. It is not run again here: the names
        // are the ones it returned when this statement read it (with no operators after it).
        if (table.FunctionCall != null)
            return askSources && table.TableOperators.Count == 0 && context.ReturnedColumns(table) is { Count: > 0 } returned
                ? returned.ToList()
                : null;
        if (table.ValuesRows != null) return null;
        if (table.ColumnAliases is { Count: > 0 }) return table.ColumnAliases;

        if (table.Subquery != null) return await SelectListAsync(table.Subquery, context, depth, askSources);

        if (string.IsNullOrEmpty(table.TableName)) return null;

        // A CTE or a temp table: held in memory, so asking for its columns reads nothing.
        var plainName = table.ConnectionName == null && table.DatabaseName == null && table.SchemaName == null;
        IDataSource? source = null;
        if (plainName
            && (context.LocalSources.TryGetValue(table.TableName, out var local)
                || context.Connections.TryGetValue(table.TableName, out local))
            && local is InMemoryDataSource)
            source = local;
        else if (askSources)
            source = await context.ResolveReadSourceAsync(table);

        if (source == null) return null;
        var declared = (await source.GetColumnsAsync(context.CancellationToken)).ToList();
        return declared.Count == 0 ? null : declared;
    }

    private static async Task<List<string>?> SelectListAsync(Statement query, IExecutionContext context, int depth, bool askSources)
    {
        // A UNION takes its names from its first query.
        while (query is SetOperationStatement set) query = set.Left;
        if (query is not SelectStatement select || select.Columns == null) return null;

        var names = new List<string>();
        foreach (var column in select.Columns)
        {
            if (!string.IsNullOrEmpty(column.Alias)) { names.Add(column.Alias!); continue; }
            if (column.Expression is not IdentifierExpression id) return null;
            if (id.Name == "*" || id.Name.EndsWith(".*", StringComparison.Ordinal))
            {
                // `SELECT *`: the names of every table the query reads. `t.*`: the names of t.
                var only = id.Name == "*" ? null : id.Name[..^2];
                var found = false;
                foreach (var inner in StarTables(select))
                {
                    if (only != null && !only.Equals(inner.Alias ?? inner.TableName, StringComparison.OrdinalIgnoreCase)) continue;
                    var innerNames = await DeclaredColumnsAsync(inner, context, depth + 1, askSources);
                    if (innerNames == null) return null;
                    names.AddRange(innerNames.Where(name => !names.Contains(name, StringComparer.OrdinalIgnoreCase)));
                    found = true;
                }
                if (!found) return null;
                continue;
            }
            names.Add(id.Name[(id.Name.LastIndexOf('.') + 1)..]);
        }
        return names;
    }

    /// <summary>The tables whose columns <c>SELECT *</c> returns: a SEMI or ANTI join adds none.</summary>
    private static IEnumerable<TableReference> StarTables(SelectStatement select)
    {
        if (select.FromTable != null) yield return select.FromTable;
        if (select.Joins == null) yield break;
        foreach (var join in select.Joins)
        {
            if (join.JoinType.Contains("SEMI", StringComparison.OrdinalIgnoreCase)
                || join.JoinType.Contains("ANTI", StringComparison.OrdinalIgnoreCase))
                continue;
            yield return join.Table;
        }
    }
}
