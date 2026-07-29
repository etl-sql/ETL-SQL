using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;

/// <summary>
/// Handles DELETE TAG FOR TABLE &lt;table&gt; [COLUMN &lt;col&gt;] (key, ...).
/// </summary>
public class DeleteTagStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(DeleteTagStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (DeleteTagStatement)statement;
        var row = new Row();

        var table = Stringify(await context.EvaluateValue(stmt.TableName, row));
        if (string.IsNullOrWhiteSpace(table))
            throw new ExecutionException("DELETE TAG: the target table name evaluated to null or empty.");

        string? column = stmt.ColumnName == null
            ? null
            : Stringify(await context.EvaluateValue(stmt.ColumnName, row));
        if (column != null && string.IsNullOrWhiteSpace(column)) column = null;

        var tagNames = stmt.TagNames
            .Where(tagName => !string.IsNullOrWhiteSpace(tagName))
            .ToArray();
        if (tagNames.Length == 0) return;

        context.LineageTracker.RemoveTags(table, column, tagNames);
        context.Log($"Deleted {tagNames.Length} tag(s) from {table}{(column != null ? "." + column : "")}.");
    }

    private static string? Stringify(object? v) => v switch
    {
        null => null,
        string s => s,
        decimal d => d.ToString("0.############", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString()
    };
}
