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
/// its table reference declares (<see cref="DeclaredColumnsAsync"/>). When neither is known, the
/// row is marked <see cref="Row.MayLackColumns"/> and an unknown name on it reads NULL.
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

    /// <summary>True once the right side's column names are known.</summary>
    public bool RightKnown => _right != null;

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
    /// </summary>
    public static async Task<List<string>?> DeclaredColumnsAsync(IEnumerable<TableReference> tables, IExecutionContext context)
    {
        var names = new List<string>();
        foreach (var table in tables)
        {
            var columns = await DeclaredColumnsAsync(table, context, depth: 0);
            if (columns == null) return null;
            names.AddRange(Qualified(columns, table.Alias ?? table.TableName));
        }
        return names;
    }

    private static async Task<List<string>?> DeclaredColumnsAsync(TableReference table, IExecutionContext context, int depth)
    {
        if (depth > 8 || table.TableOperators.Count > 0 || table.FunctionCall != null || table.ValuesRows != null) return null;
        if (table.ColumnAliases is { Count: > 0 }) return table.ColumnAliases;

        if (table.Subquery != null)
        {
            if (table.Subquery is not SelectStatement select || select.Columns == null) return null;
            var names = new List<string>();
            foreach (var column in select.Columns)
            {
                if (!string.IsNullOrEmpty(column.Alias)) { names.Add(column.Alias!); continue; }
                if (column.Expression is not IdentifierExpression id) return null;
                if (id.Name == "*")
                {
                    // `SELECT * FROM one_table`: the names are that table's.
                    if (select.FromTable == null || select.Joins is { Count: > 0 }) return null;
                    var inner = await DeclaredColumnsAsync(select.FromTable, context, depth + 1);
                    if (inner == null) return null;
                    names.AddRange(inner);
                    continue;
                }
                if (id.Name.EndsWith('*')) return null;
                names.Add(id.Name[(id.Name.LastIndexOf('.') + 1)..]);
            }
            return names;
        }

        // A CTE or a temp table: held in memory, so asking for its columns reads nothing.
        if (table.ConnectionName != null || table.DatabaseName != null || table.SchemaName != null
            || string.IsNullOrEmpty(table.TableName)
            || !(context.LocalSources.TryGetValue(table.TableName, out var source)
                 || context.Connections.TryGetValue(table.TableName, out source))
            || source is not InMemoryDataSource memory)
            return null;

        var declared = (await ((IDataSource)memory).GetColumnsAsync(context.CancellationToken)).ToList();
        return declared.Count == 0 ? null : declared;
    }
}
