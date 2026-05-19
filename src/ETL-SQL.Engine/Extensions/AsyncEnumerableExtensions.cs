using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Extensions
{
    /// <summary>
    /// Streaming pipeline utilities for the Volcano/Iterator execution model.
    /// </summary>
    public static class AsyncEnumerableExtensions
    {
        /// <summary>
        /// Returns the top <paramref name="n"/> rows from <paramref name="source"/> according to
        /// <paramref name="comparer"/> using an O(n log N) min-heap, plus an optional leading
        /// <paramref name="offset"/> skip. This avoids a full sort + Take when N &lt;&lt; total rows.
        /// </summary>
        /// <param name="source">Unsorted row stream.</param>
        /// <param name="n">Maximum rows to return after the offset is skipped.</param>
        /// <param name="offset">Number of top-ranked rows to skip before returning.</param>
        /// <param name="comparer">Comparer that defines the ordering (ascending = smallest first).</param>
        public static async IAsyncEnumerable<Row> TopNAsync(
            this IAsyncEnumerable<Row> source,
            int n,
            int offset,
            IComparer<Row> comparer)
        {
            if (n <= 0) yield break;

            // Keep the top (offset + n) rows so we can skip offset at the end.
            int keep = checked(offset + n);

            // Min-heap (smallest element at top). We keep the heap at size <= keep.
            // When full, discard any new element that is smaller than the heap minimum
            // (i.e., worse-ranked than the current worst in our kept set).
            var heap = new PriorityQueue<Row, Row>(keep, comparer);

            await foreach (var row in source)
            {
                if (heap.Count < keep)
                {
                    heap.Enqueue(row, row);
                }
                else if (comparer.Compare(row, heap.Peek()) > 0)
                {
                    // New row is better-ranked (larger in the comparison that the caller
                    // inverts for DESC). Replace the worst kept row.
                    heap.DequeueEnqueue(row, row);
                }
            }

            // Drain into a sorted list (ascending). The heap pops smallest first.
            var sorted = new List<Row>(heap.Count);
            while (heap.Count > 0) sorted.Add(heap.Dequeue());

            // Skip offset, yield remaining up to n.
            int start = Math.Min(offset, sorted.Count);
            int end = Math.Min(start + n, sorted.Count);
            for (int i = start; i < end; i++)
                yield return sorted[i];
        }
    }
}
