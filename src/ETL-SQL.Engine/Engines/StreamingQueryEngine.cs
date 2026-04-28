using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Engines
{
    /// <summary>
    /// Handles the streaming execution of simple SELECT statements.
    /// This engine is used when no heavy operations (joins, aggregates, window functions) are required.
    /// </summary>
    public class StreamingQueryEngine(IExecutionContext context, ILogger logger)
    {
        private readonly IExecutionContext _context = context;
        private readonly ILogger _logger = logger;

        public async IAsyncEnumerable<DataTable> ExecuteStreamingSelect(
            SelectStatement stmt, 
            IAsyncEnumerable<DataTable> batches, 
            List<SelectColumn> finalColumns, 
            List<string> colNames)
        {
            var resultBatch = new DataTable();
            resultBatch.SetColumns(colNames);

            bool yielded = false;
            int rowsYielded = 0;
            int rowsSkipped = 0;
            int offset = 0;
            
            if (stmt.Offset != null)
            {
                var offVal = await _context.EvaluateValue(stmt.Offset, new Row());
                offset = Convert.ToInt32(offVal);
            }
            int? limit = null;
            if (stmt.LimitCount != null)
            {
                var limVal = await _context.EvaluateValue(stmt.LimitCount, new Row());
                limit = Convert.ToInt32(limVal);
            }

            string fromName = stmt.FromTable.Alias ?? stmt.FromTable.TableName;
            await foreach (var batch in batches)
            {
                foreach (var row in batch.Rows)
                {
                    // Qualify row for evaluation context (especially for correlated subqueries)
                    var evalRow = row;
                    if (!string.IsNullOrEmpty(fromName))
                    {
                        evalRow = row.Clone();
                        foreach (var kv in row.Columns)
                        {
                            if (!kv.Key.Contains(".")) evalRow[$"{fromName}.{kv.Key}"] = kv.Value;
                        }
                    }

                    if (stmt.WhereClause != null && !await _context.EvaluateCondition(stmt.WhereClause, evalRow)) continue;
                    
                    if (rowsSkipped < offset)
                    {
                        rowsSkipped++;
                        continue;
                    }

                    if (limit.HasValue && rowsYielded >= limit.Value) goto done;

                    var resRow = resultBatch.NewRow();
                    for (int i = 0; i < finalColumns.Count; i++)
                        resRow[i] = await _context.EvaluateValue(finalColumns[i].Expression, evalRow);
                    
                    await resultBatch.AddRowAsync(resRow);
                    rowsYielded++;

                    if (resultBatch.Rows.Count >= _context.BatchSize)
                    {
                        yield return resultBatch;
                        yielded = true;
                        resultBatch = new DataTable();
                        resultBatch.SetColumns(colNames);
                    }
                }
                if (limit.HasValue && rowsYielded >= limit.Value) break;
            }
            done:
            if (resultBatch.Rows.Count > 0 || !yielded) yield return resultBatch;
        }

        public async IAsyncEnumerable<DataTable> ReplayBatches(DataTable? first, IAsyncEnumerator<DataTable> e)
        {
            try
            {
                if (first != null) yield return first;
                while (await e.MoveNextAsync()) yield return e.Current;
            }
            finally { await e.DisposeAsync(); }
        }
    }
}
