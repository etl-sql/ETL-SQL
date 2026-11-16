using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;

public static class OutputClauseHelper
{
    private static bool TryGetStarSide(string name, out bool deleted)
    {
        deleted = name.Equals("DELETED.*", StringComparison.OrdinalIgnoreCase);
        return deleted || name.Equals("INSERTED.*", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Fails on a name the OUTPUT clause cannot resolve, before the statement changes any row. The
    /// clause was evaluated only after the mutation, so <c>DELETE ... OUTPUT DELETED.nmae</c> failed
    /// with the rows already deleted. Each expression is evaluated once against a row of the
    /// target's columns, all NULL; only an unknown-name error is raised, since another failure on
    /// NULL input says nothing about the real rows.
    /// </summary>
    public static async Task ValidateNamesAsync(OutputClause? output, IExecutionContext context, IEnumerable<string> targetColumns)
    {
        if (output == null) return;

        var probe = new Row { ["$action"] = null };
        foreach (var col in targetColumns)
        {
            probe[col] = null;
            probe[$"DELETED.{col}"] = null;
            probe[$"INSERTED.{col}"] = null;
        }

        foreach (var outCol in output.Columns)
        {
            if (outCol.Expression is IdentifierExpression { Name: var starName } && TryGetStarSide(starName, out _)) continue;
            try
            {
                await context.EvaluateValue(outCol.Expression, probe);
            }
            catch (Exception ex) when (!IsUnknownName(ex))
            {
                // Not a naming error; the real evaluation after the mutation decides.
            }
        }
    }

    private static bool IsUnknownName(Exception ex) =>
        ex.Message.StartsWith("Unknown column", StringComparison.OrdinalIgnoreCase)
        || ex.Message.StartsWith("Unknown identifier", StringComparison.OrdinalIgnoreCase)
        || ex.Message.StartsWith("Unknown variable", StringComparison.OrdinalIgnoreCase);

    public static async Task ProcessAsync(
        OutputClause output,
        IExecutionContext context,
        IEnumerable<(Row? Before, Row? After, string? Action)> rowInfos)
    {
        if (output == null) return;

        var outputRows = new List<Row>();
        foreach (var (before, after, action) in rowInfos)
        {
            var contextRow = new Row();
            if (action != null) contextRow["$action"] = action;

            // Populate DELETED.* and INSERTED.* namespaces
            if (before != null)
            {
                contextRow["DELETED"] = before;
                foreach (var col in before.Columns)
                {
                    contextRow[$"DELETED.{col.Key}"] = col.Value;
                }
            }

            if (after != null)
            {
                contextRow["INSERTED"] = after;
                foreach (var col in after.Columns)
                {
                    contextRow[$"INSERTED.{col.Key}"] = col.Value;
                    // For INSERT/UPDATE, bare column names refer to the NEW values (INSERTED)
                    if (!contextRow.HasColumn(col.Key)) contextRow[col.Key] = col.Value;
                }
            }
            else if (before != null)
            {
                // For DELETE, bare column names refer to the OLD values (DELETED)
                foreach (var col in before.Columns)
                {
                    if (!contextRow.HasColumn(col.Key)) contextRow[col.Key] = col.Value;
                }
            }

            // An inserted row has no DELETED values and a deleted row no INSERTED values. Both
            // namespaces still exist, so the missing one reads as NULL instead of an unknown column.
            foreach (var col in (after ?? before)?.Columns.Keys ?? Enumerable.Empty<string>())
            {
                if (before == null) contextRow[$"DELETED.{col}"] = null;
                if (after == null) contextRow[$"INSERTED.{col}"] = null;
            }

            var outputRow = new Row();
            foreach (var outCol in output.Columns)
            {
                // INSERTED.* and DELETED.* expand to every column of that row, named by the bare
                // column name as SQL Server does. They were evaluated as one column named "DELETED.*"
                // and failed. A side the statement has no row for reads NULL in every column.
                if (outCol.Expression is IdentifierExpression { Name: var starName }
                    && TryGetStarSide(starName, out var deletedSide))
                {
                    var source = deletedSide ? before : after;
                    foreach (var col in (after ?? before)?.Columns.Keys ?? Enumerable.Empty<string>())
                        outputRow[col] = source?[col];
                    continue;
                }

                var val = await context.EvaluateValue(outCol.Expression, contextRow);
                outputRow[outCol.Alias ?? outCol.ToSql()] = val;
            }
            outputRows.Add(outputRow);
        }

        if (outputRows.Count > 0)
        {
            var outputTable = new DataTable();
            outputTable.SetColumns(outputRows[0].Columns.Keys);
            foreach (var r in outputRows) await outputTable.AddRowAsync(r);

            if (output.IntoTable != null)
            {
                var intoDest = await context.ResolveDataSourceAsync(output.IntoTable);
                var targetCols = (await intoDest.GetColumnsAsync(context.CancellationToken)).ToList();
                IAsyncEnumerable<DataTable> alignedBatches = new[] { outputTable }.ToAsyncEnumerable();
                if (targetCols.Count > 0)
                {
                    alignedBatches = context.AlignColumns(alignedBatches, targetCols);
                }
                await intoDest.WriteBatches(alignedBatches);
            }
            else
            {
                // Append to LastResult or replace? T-SQL standard is to return it.
                // In ETL-SQL engine, LastResult is the primary mechanism.
                context.LastResult = outputTable;
            }
        }
    }
}
