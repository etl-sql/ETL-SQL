using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;

/// <summary>
/// Rejects a column name a SELECT cannot resolve, before the statement runs. The row evaluator
/// reads an unknown name as NULL, so without this a misspelt column returned NULLs and the script
/// succeeded.
/// <para>
/// The check is deliberately narrow: it only runs when every source of the statement is a temp
/// table that already exists with known columns, because only then is the full set of names known
/// before execution. Anything else (files, connections, subqueries, CTEs, APPLY, PIVOT) is left
/// to the row evaluator, which fails on an unknown name when it reads a row.
/// </para>
/// </summary>
internal static class SelectColumnBinder
{
    // Bare words some functions take as their first argument: date parts and type names.
    private static readonly HashSet<string> FunctionKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "year", "yy", "yyyy", "quarter", "qq", "q", "month", "mm", "m", "dayofyear", "dy", "y",
        "day", "dd", "d", "week", "wk", "ww", "weekday", "dw", "w", "hour", "hh", "minute", "mi", "n",
        "second", "ss", "s", "millisecond", "ms", "microsecond", "mcs", "nanosecond", "ns",
        "iso_week", "isowk", "isoww", "tzoffset", "tz", "epoch", "dow", "doy", "isoyear", "decade",
        "century", "millennium",
        "int", "integer", "bigint", "smallint", "tinyint", "bit", "decimal", "numeric", "money",
        "float", "real", "double", "varchar", "nvarchar", "char", "nchar", "text", "ntext", "string",
        "date", "datetime", "datetime2", "smalldatetime", "time", "datetimeoffset", "boolean", "bool",
        "uniqueidentifier", "varbinary", "binary", "json", "xml",
    };

    public static async Task ValidateAsync(SelectStatement stmt, IExecutionContext context)
    {
        if (stmt.Ctes is { Count: > 0 } || stmt.FromTable == null) return;

        var sources = new List<(TableReference Table, HashSet<string> Columns)>();
        foreach (var table in new[] { stmt.FromTable }.Concat(stmt.Joins?.Select(j => j.Table) ?? Enumerable.Empty<TableReference>()))
        {
            var columns = await KnownColumnsAsync(table, context);
            if (columns == null) return;
            sources.Add((table, columns));
        }
        if (stmt.Joins?.Any(j => j.IsApply || j.IsFuzzy || j.IsAsof) == true) return;

        var aliases = new HashSet<string>(
            stmt.Columns.Where(c => !string.IsNullOrEmpty(c.Alias)).Select(c => c.Alias!),
            StringComparer.OrdinalIgnoreCase);

        var roots = new List<AstNode?>();
        roots.AddRange(stmt.Columns.Select(c => (AstNode?)c.Expression));
        roots.Add(stmt.WhereClause);
        if (stmt.GroupBy != null) roots.AddRange(stmt.GroupBy);
        roots.Add(stmt.HavingClause);
        if (stmt.OrderBy != null) roots.AddRange(stmt.OrderBy);
        roots.Add(stmt.QualifyClause);
        if (stmt.Joins != null) roots.AddRange(stmt.Joins.Select(j => (AstNode?)j.Condition));

        foreach (var root in roots)
            Walk(root, functionArgument: false, sources, aliases, context);
    }

    private static async Task<HashSet<string>?> KnownColumnsAsync(TableReference table, IExecutionContext context)
    {
        if (table.Subquery != null || table.FunctionCall != null || table.ValuesRows != null
            || table.ConnectionName != null || table.DatabaseName != null || table.SchemaName != null
            || table.TableOperators.Count > 0
            || !(table.TableName.StartsWith('#') || table.TableName.StartsWith('&'))
            || context.LocalSources.ContainsKey(table.TableName)
            || !context.Connections.TryGetValue(table.TableName, out var source)
            || source is not InMemoryDataSource memory)
            return null;

        var columns = new HashSet<string>(await ((IDataSource)memory).GetColumnsAsync(context.CancellationToken), StringComparer.OrdinalIgnoreCase);
        return columns.Count == 0 ? null : columns;
    }

    private static void Walk(object? node, bool functionArgument, List<(TableReference Table, HashSet<string> Columns)> sources,
        HashSet<string> aliases, IExecutionContext context)
    {
        switch (node)
        {
            case null:
            case Statement:
            case TableReference:
            case SubqueryExpression:
                // A subquery binds its own names and may refer to this statement's columns.
                return;
            case IdentifierExpression id:
                Check(id.Name, functionArgument, sources, aliases, context);
                return;
            case FunctionCallExpression call:
                foreach (var argument in call.Arguments)
                    Walk(argument, functionArgument: true, sources, aliases, context);
                Walk(call.Window, false, sources, aliases, context);
                Walk(call.Filter, false, sources, aliases, context);
                if (call.WithinGroupOrderBy != null)
                    foreach (var order in call.WithinGroupOrderBy) Walk(order, false, sources, aliases, context);
                return;
            case AstNode ast:
                foreach (var property in ast.GetType().GetProperties())
                {
                    if (property.GetIndexParameters().Length > 0 || property.PropertyType == typeof(string)) continue;
                    var value = property.GetValue(ast);
                    if (value is AstNode child) Walk(child, false, sources, aliases, context);
                    else if (value is IEnumerable items and not string)
                        foreach (var item in items)
                            if (item is AstNode element) Walk(element, false, sources, aliases, context);
                }
                return;
        }
    }

    private static void Check(string name, bool functionArgument, List<(TableReference Table, HashSet<string> Columns)> sources,
        HashSet<string> aliases, IExecutionContext context)
    {
        // `t.*` reaches here as an identifier; star expansion reports its own unknown qualifier.
        if (string.IsNullOrEmpty(name) || name == "*" || name.EndsWith(".*", StringComparison.Ordinal) || name.StartsWith('@')) return;

        var dot = name.IndexOf('.');
        if (dot < 0)
        {
            if (sources.Any(s => s.Columns.Contains(name)) || aliases.Contains(name)) return;
            if (functionArgument && FunctionKeywords.Contains(name)) return;
            if (IsNonColumnName(name, context)) return;
            throw Unknown(name, sources);
        }

        var qualifier = name[..dot];
        var rest = name[(dot + 1)..];
        var column = rest.Split('.')[0];
        var owners = sources.Where(s => Names(s.Table, qualifier)).ToList();
        if (owners.Count > 0)
        {
            if (owners.Any(s => s.Columns.Contains(column) || s.Columns.Contains(name))) return;
            throw Unknown(name, owners);
        }

        // `col.field` reads a member of a column value.
        if (sources.Any(s => s.Columns.Contains(qualifier) || s.Columns.Contains(name))) return;
        if (IsNonColumnName(name, context)) return;
        throw Unknown(name, sources);
    }

    private static bool Names(TableReference table, string qualifier) =>
        string.Equals(table.Alias, qualifier, StringComparison.OrdinalIgnoreCase)
        || string.Equals(table.TableName, qualifier, StringComparison.OrdinalIgnoreCase);

    // Names the evaluator resolves without a row: connections, no-argument functions such as
    // CURRENT_TIMESTAMP, and Docker connection strings.
    private static bool IsNonColumnName(string name, IExecutionContext context) =>
        context.Connections.ContainsKey(name)
        || context.FunctionRegistry.IsRegistered(name)
        || name.Contains(".CONNECTION_STRING", StringComparison.OrdinalIgnoreCase);

    private static ExecutionException Unknown(string name, IEnumerable<(TableReference Table, HashSet<string> Columns)> sources)
    {
        var known = string.Join("; ", sources.Select(s => $"{s.Table.Alias ?? s.Table.TableName}: {string.Join(", ", s.Columns)}"));
        return new ExecutionException($"Unknown column '{name}' in SELECT. Known columns: {known}.");
    }
}
