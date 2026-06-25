using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles CREATE TAG FOR TABLE &lt;table&gt; [COLUMN &lt;col&gt;] (key = expr, ...). Evaluates the
/// table/column names and tag values at runtime (so they may be variables, e.g. @r.tbl inside a
/// FOR loop) and seeds the lineage tracker's metadata so the tags inherit onto derived columns
/// in any subsequent SELECT ... INTO. Last-writer-wins: a later CREATE TAG or inherited value
/// overrides an earlier one for the same table/column key.
/// </summary>
public class CreateTagStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(CreateTagStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (CreateTagStatement)statement;
        var row = new Row();

        var table = Stringify(await context.EvaluateValue(stmt.TableName, row));
        if (string.IsNullOrWhiteSpace(table))
            throw new ExecutionException("CREATE TAG: the target table name evaluated to null or empty.");

        string? column = stmt.ColumnName == null
            ? null
            : Stringify(await context.EvaluateValue(stmt.ColumnName, row));
        if (column != null && string.IsNullOrWhiteSpace(column)) column = null;

        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in stmt.Tags)
        {
            var val = Stringify(await context.EvaluateValue(kv.Value, row));
            if (val != null) tags[kv.Key] = val;
        }

        if (tags.Count == 0) return;

        context.LineageTracker.ApplyTags(table, column, tags);
        context.Log($"Tagged {table}{(column != null ? "." + column : "")} with {tags.Count} tag(s).");
    }

    /// <summary>Converts an evaluated value to its tag-string form, formatting numbers invariantly.</summary>
    private static string? Stringify(object? v) => v switch
    {
        null => null,
        string s => s,
        decimal d => d.ToString("0.############", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString()
    };
}
