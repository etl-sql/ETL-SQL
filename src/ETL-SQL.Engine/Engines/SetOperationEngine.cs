using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
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
            
            var leftHashes = BuildHashSet(normalizedLeft);
            var rightHashes = BuildHashSet(normalizedRight);

            if (setOp.Operation == SetOpType.UNION)
            {
                _logger.Debug("[SET_OP] Executing DISTINCT UNION");
                var seen = new HashSet<long>();
                foreach (var r in normalizedLeft)
                {
                    if (seen.Add(GetRowHash(r))) await resultBatch.AddRowAsync(r);
                }
                foreach (var r in normalizedRight)
                {
                    if (seen.Add(GetRowHash(r))) await resultBatch.AddRowAsync(r);
                }
            }
            else if (setOp.Operation == SetOpType.EXCEPT)
            {
                _logger.Debug("[SET_OP] Executing EXCEPT");
                foreach (var r in normalizedLeft)
                {
                    var hash = GetRowHash(r);
                    if (!rightHashes.Contains(hash)) await resultBatch.AddRowAsync(r);
                }
            }
            else if (setOp.Operation == SetOpType.INTERSECT)
            {
                _logger.Debug("[SET_OP] Executing INTERSECT");
                foreach (var r in normalizedLeft)
                {
                    var hash = GetRowHash(r);
                    if (rightHashes.Contains(hash)) await resultBatch.AddRowAsync(r);
                }
            }

            // Apply DISTINCT behavior for EXCEPT/INTERSECT results (UNION already handled above)
            if (setOp.Operation != SetOpType.UNION && resultBatch.Rows.Count > 0)
            {
                var finalRows = new List<Row>();
                var seen = new HashSet<long>();
                foreach (var r in resultBatch.Rows)
                {
                    if (seen.Add(GetRowHash(r))) finalRows.Add(r);
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

        private HashSet<long> BuildHashSet(List<Row> rows)
        {
            var hashes = new HashSet<long>();
            foreach (var r in rows) hashes.Add(GetRowHash(r));
            return hashes;
        }

        private long GetRowHash(Row row)
        {
            // SEC-9: Robust FNV-1a 64-bit hash for row equality comparison
            const long fnvOffsetBasis = unchecked((long)14695981039346656037UL);
            const long fnvPrime = 1099511628211;

            long hash = fnvOffsetBasis;
            foreach (var kv in row.Columns.OrderBy(k => k.Key))
            {
                var valStr = kv.Value?.ToString() ?? "NULL";
                foreach (char c in valStr)
                {
                    hash ^= (byte)c;
                    hash *= fnvPrime;
                    hash ^= (byte)(c >> 8);
                    hash *= fnvPrime;
                }
                hash ^= 0x7C; // Pipe separator
                hash *= fnvPrime;
            }
            return hash;
        }
    }
}

