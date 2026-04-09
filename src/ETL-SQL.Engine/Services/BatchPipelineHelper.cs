using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Data;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace ETL_SQL.Engine.Services
{
    /// <summary>
    /// Provides stateless batch-stream pipeline transformations: progress interception,
    /// column alignment, and FOR JSON / FOR XML serialization.
    /// </summary>
    internal sealed class BatchPipelineHelper
    {
        /// <summary>
        /// Wraps a batch stream to fire a progress callback after each batch is yielded.
        /// </summary>
        public async IAsyncEnumerable<DataTable> InterceptProgress(
            IAsyncEnumerable<DataTable> chunks,
            Action<long>? onBatchProcessed,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var chunk in chunks.WithCancellation(ct))
            {
                onBatchProcessed?.Invoke(chunk.Rows.Count);
                yield return chunk;
            }
        }

        /// <summary>
        /// Re-maps each batch so its columns match <paramref name="targetCols"/> by name,
        /// falling back to positional mapping when a name is not present.
        /// </summary>
        public async IAsyncEnumerable<DataTable> AlignColumns(
            IAsyncEnumerable<DataTable> batches,
            List<string> targetCols,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var batch in batches.WithCancellation(ct))
                yield return await AlignBatch(batch, targetCols);
        }

        /// <summary>
        /// Collects all batches and serializes them as a single FOR JSON or FOR XML result.
        /// </summary>
        public async IAsyncEnumerable<DataTable> EvaluateForClause(
            IAsyncEnumerable<DataTable> batches,
            ForClause forClause,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var allRows = new List<Row>();
            await foreach (var b in batches.WithCancellation(ct))
                allRows.AddRange(b.Rows);

            if (forClause.Type == ForType.JSON)
            {
                var json = ResultFormatter.FormatJson(
                    allRows, forClause.Mode, forClause.RootName,
                    forClause.IncludeNullValues, forClause.WithoutArrayWrapper);
                var result = new DataTable();
                result.SetColumns(new[] { "JSON_F52E2B61" });
                var row = result.NewRow();
                row[0] = json;
                await result.AddRowAsync(row);
                yield return result;
            }
            else if (forClause.Type == ForType.XML)
            {
                var xml = ResultFormatter.FormatXml(
                    allRows, forClause.Mode, forClause.RootName,
                    forClause.IncludeNullValues, forClause.UseElements);
                var result = new DataTable();
                result.SetColumns(new[] { "XML_F52E2B61" });
                var row = result.NewRow();
                row[0] = xml;
                await result.AddRowAsync(row);
                yield return result;
            }
        }

        // ── Private helpers ──────────────────────────────────────────────────

        private static async Task<DataTable> AlignBatch(DataTable batch, List<string> targetCols)
        {
            var newBatch = new DataTable();
            newBatch.SetColumns(targetCols);
            foreach (Row oldRow in batch.Rows)
                await newBatch.AddRowAsync(MapRow(oldRow, batch.ColumnNames, newBatch));
            return newBatch;
        }

        private static Row MapRow(Row oldRow, IReadOnlyList<string> sourceCols, DataTable targetTable)
        {
            var newRow = targetTable.NewRow();
            var targetCols = targetTable.ColumnNames;
            for (int i = 0; i < targetCols.Count; i++)
            {
                var target = targetCols[i];
                if (oldRow.HasColumn(target))
                    newRow[i] = oldRow[target];
                else if (i < sourceCols.Count)
                    newRow[i] = oldRow[sourceCols[i]];
                else
                    newRow[i] = null;
            }
            return newRow;
        }
    }
}
