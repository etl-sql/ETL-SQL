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

        public SetOperationEngine(IExecutionContext context)
        {
            _context = context;
        }

        /// <summary>Executes a set operation by buffering both sides and performing set logic.</summary>
        public async IAsyncEnumerable<DataTable> ApplySetOperation(SetOperationStatement setOp)
        {
            var leftRows = await BufferAllRows(setOp.Left);
            var rightRows = await BufferAllRows(setOp.Right);

            if (leftRows.Count == 0 && rightRows.Count == 0) yield break;

            var resultBatch = new DataTable();
            var targetColumns = leftRows.Count > 0 ? leftRows[0].Columns.Keys.ToList() : rightRows[0].Columns.Keys.ToList();
            resultBatch.SetColumns(targetColumns);

            switch (setOp.Operation)
            {
                case SetOpType.UNION_ALL:
                    foreach (var r in leftRows) resultBatch.AddRow(Normalize(r, targetColumns));
                    foreach (var r in rightRows) resultBatch.AddRow(Normalize(r, targetColumns));
                    yield return resultBatch;
                    break;

                case SetOpType.UNION:
                case SetOpType.EXCEPT:
                case SetOpType.INTERSECT:
                    var normalizedLeft = leftRows.Select(r => Normalize(r, targetColumns)).ToList();
                    var normalizedRight = rightRows.Select(r => Normalize(r, targetColumns)).ToList();
                    
                    var leftHashes = BuildHashSet(normalizedLeft);
                    var rightHashes = BuildHashSet(normalizedRight);

                    if (setOp.Operation == SetOpType.UNION)
                    {
                        foreach (var r in normalizedLeft) resultBatch.AddRow(r);
                        foreach (var r in normalizedRight)
                        {
                            var hash = GetRowHash(r);
                            if (!leftHashes.Contains(hash)) resultBatch.AddRow(r);
                        }
                    }
                    else if (setOp.Operation == SetOpType.EXCEPT)
                    {
                        foreach (var r in normalizedLeft)
                        {
                            var hash = GetRowHash(r);
                            if (!rightHashes.Contains(hash)) resultBatch.AddRow(r);
                        }
                    }
                    else if (setOp.Operation == SetOpType.INTERSECT)
                    {
                        foreach (var r in normalizedLeft)
                        {
                            var hash = GetRowHash(r);
                            if (rightHashes.Contains(hash)) resultBatch.AddRow(r);
                        }
                    }

                    // Apply DISTINCT behavior for UNION/EXCEPT/INTERSECT results
                    if (resultBatch.Rows.Count > 0)
                    {
                        var finalRows = new List<Row>();
                        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var r in resultBatch.Rows)
                        {
                            var h = GetRowHash(r);
                            if (seen.Add(h)) finalRows.Add(r);
                        }
                        resultBatch.Rows.Clear();
                        foreach (var r in finalRows) resultBatch.AddRow(r);
                    }
                    
                    yield return resultBatch;
                    break;
            }
        }

        private Row Normalize(Row row, List<string> targetColumns)
        {
            var newRow = new Row();
            var sourceKeys = row.Columns.Keys.ToList();
            for (int i = 0; i < targetColumns.Count; i++)
            {
                if (i < sourceKeys.Count)
                {
                    newRow[targetColumns[i]] = row[sourceKeys[i]];
                }
                else
                {
                    newRow[targetColumns[i]] = null;
                }
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

        private HashSet<string> BuildHashSet(List<Row> rows)
        {
            var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows) hashes.Add(GetRowHash(r));
            return hashes;
        }

        private string GetRowHash(Row row)
        {
            // Simple robust hashing based on stringified values
            return string.Join("|", row.Columns.OrderBy(k => k.Key).Select(kv => kv.Value?.ToString() ?? "NULL"));
        }
    }
}

