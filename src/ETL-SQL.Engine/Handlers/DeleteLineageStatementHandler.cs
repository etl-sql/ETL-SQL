using System;
using System.Globalization;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;

/// <summary>
/// Handles DELETE LINEAGE FOR TABLE &lt;table&gt;. Only imported lineage is mutable;
/// auto-captured lineage entries are preserved.
/// </summary>
public class DeleteLineageStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(DeleteLineageStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (DeleteLineageStatement)statement;
        var table = Stringify(await context.EvaluateValue(stmt.TableName, new Row()));
        if (string.IsNullOrWhiteSpace(table))
            throw new ExecutionException("DELETE LINEAGE: the target table name evaluated to null or empty.");

        var removed = context.LineageTracker.RemoveImportedLineage(table);
        context.Log($"Deleted {removed} imported lineage entr{(removed == 1 ? "y" : "ies")} for {table}.");
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
