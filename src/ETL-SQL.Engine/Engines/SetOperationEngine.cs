using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Engines
{
    /// <summary>
    /// Handles set operations (UNION, UNION ALL, EXCEPT, INTERSECT) between query result sets.
    /// </summary>
    public class SetOperationEngine
    {
        private readonly IExecutionContext _context;
        private readonly ILogger _logger;

        public SetOperationEngine(IExecutionContext context, ILogger logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>Executes a set operation by buffering both sides and performing set logic.</summary>
        public async IAsyncEnumerable<DataTable> ApplySetOperation(SetOperationStatement setOp)
        {
            if (setOp.Operation == SetOpType.UNION_ALL)
            {
                _logger.Debug("[SET_OP] Executing streaming UNION ALL (no buffering)");
                List<string>? leftColumns = null;
                await foreach (var batch in _context.ExecuteQuery(setOp.Left))
                {
                    if (leftColumns == null) leftColumns = batch.ColumnNames.ToList();
                    _logger.Debug($"[UNION ALL] Yielding Left batch: {batch.Rows.Count} rows");
                    yield return batch;
                }

                var rightBatches = _context.ExecuteQuery(setOp.Right);
                if (leftColumns != null)
                {
                    rightBatches = _context.AlignColumns(rightBatches, leftColumns);
                }
                await foreach (var batch in rightBatches)
                {
                    _logger.Debug($"[UNION ALL] Yielding Right batch: {batch.Rows.Count} rows");
                    yield return batch;
                }
                yield break;
            }

            var leftRows = await BufferAllRows(setOp.Left);
            var rightRows = await BufferAllRows(setOp.Right);

            if (leftRows.Count == 0 && rightRows.Count == 0) yield break;

            var resultBatch = new DataTable();
            var targetColumns = leftRows.Count > 0
                ? leftRows[0].Schema?.ColumnNames.ToList() ?? leftRows[0].Columns.Keys.ToList()
                : rightRows[0].Schema?.ColumnNames.ToList() ?? rightRows[0].Columns.Keys.ToList();
            resultBatch.SetColumns(targetColumns);

            // case SetOpType.UNION:
            // case SetOpType.EXCEPT:
            // case SetOpType.INTERSECT:

            var normalizedLeft = leftRows.Select(r => Normalize(r, targetColumns)).ToList();
            var normalizedRight = rightRows.Select(r => Normalize(r, targetColumns)).ToList();

            var leftKeys = BuildKeySet(normalizedLeft, targetColumns);
            var rightKeys = BuildKeySet(normalizedRight, targetColumns);

            if (setOp.Operation == SetOpType.UNION)
            {
                _logger.Debug("[SET_OP] Executing DISTINCT UNION");
                var seen = new HashSet<CompoundKey>();
                foreach (var r in normalizedLeft)
                {
                    if (seen.Add(ToKey(r, targetColumns))) await resultBatch.AddRowAsync(r);
                }
                foreach (var r in normalizedRight)
                {
                    if (seen.Add(ToKey(r, targetColumns))) await resultBatch.AddRowAsync(r);
                }
            }
            else if (setOp.Operation == SetOpType.EXCEPT)
            {
                _logger.Debug("[SET_OP] Executing EXCEPT");
                foreach (var r in normalizedLeft)
                {
                    var key = ToKey(r, targetColumns);
                    if (!rightKeys.Contains(key)) await resultBatch.AddRowAsync(r);
                }
            }
            else if (setOp.Operation == SetOpType.INTERSECT)
            {
                _logger.Debug("[SET_OP] Executing INTERSECT");
                foreach (var r in normalizedLeft)
                {
                    var key = ToKey(r, targetColumns);
                    if (rightKeys.Contains(key)) await resultBatch.AddRowAsync(r);
                }
            }

            // Apply DISTINCT behavior for EXCEPT/INTERSECT results (UNION already handled above)
            if (setOp.Operation != SetOpType.UNION && resultBatch.Rows.Count > 0)
            {
                var finalRows = new List<Row>();
                var seen = new HashSet<CompoundKey>();
                foreach (var r in resultBatch.Rows)
                {
                    if (seen.Add(ToKey(r, targetColumns))) finalRows.Add(r);
                }
                resultBatch.Rows.Clear();
                foreach (var r in finalRows) await resultBatch.AddRowAsync(r);
            }

            yield return resultBatch;
        }

        private Row Normalize(Row row, List<string> targetColumns)
        {
            var newRow = new Row();
            // IMPORTANT: SQL Set operations are positional.
            for (int i = 0; i < targetColumns.Count; i++)
            {
                newRow[targetColumns[i]] = row[i];
            }
            return newRow;
        }

        private async Task<List<Row>> BufferAllRows(Statement query)
        {
            var rows = new List<Row>();
            await foreach (var batch in _context.ExecuteQuery(query))
            {
                rows.AddRange(batch.Rows);
            }
            return rows;
        }

        private HashSet<CompoundKey> BuildKeySet(List<Row> rows, List<string> columns)
        {
            var keys = new HashSet<CompoundKey>();
            foreach (var r in rows) keys.Add(ToKey(r, columns));
            return keys;
        }

        private CompoundKey ToKey(Row row, List<string> columns)
        {
            var vals = new object?[columns.Count];
            for (int i = 0; i < columns.Count; i++) vals[i] = row[columns[i]];
            return new CompoundKey(vals);
        }
    }
}

