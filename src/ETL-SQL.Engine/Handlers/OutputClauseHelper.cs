using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    public static class OutputClauseHelper
    {
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

                var outputRow = new Row();
                foreach (var outCol in output.Columns)
                {
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
                    var targetCols = (await intoDest.GetColumnsAsync()).ToList();
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
}
