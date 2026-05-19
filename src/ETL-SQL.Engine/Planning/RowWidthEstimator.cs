using System;
using System.Collections.Generic;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Planning
{
    /// <summary>
    /// Estimates in-memory byte width of a row or a sample of rows.
    /// Used by the spill decision logic to compare estimated operator working set against
    /// <c>OperatorMemoryGrantMB</c> before falling back to the row-count backstop.
    /// </summary>
    public static class RowWidthEstimator
    {
        /// <summary>Default per-row heuristic when no sample is available.</summary>
        public const int DefaultBytesPerRow = 128;

        /// <summary>Estimates the byte width of a single row by summing value sizes.</summary>
        public static int EstimateRowBytes(Row row)
        {
            int total = 0;
            foreach (var name in row.GetColumnNames())
                total += EstimateValueBytes(row[name]);
            return total == 0 ? DefaultBytesPerRow : total;
        }

        /// <summary>Estimates average row width from a sample (up to 100 rows).</summary>
        public static int EstimateAverageRowBytes(IReadOnlyList<Row> sample)
        {
            if (sample.Count == 0) return DefaultBytesPerRow;
            int limit = Math.Min(sample.Count, 100);
            long total = 0;
            for (int i = 0; i < limit; i++)
                total += EstimateRowBytes(sample[i]);
            return (int)(total / limit);
        }

        /// <summary>Estimates working-set bytes for the given row list.</summary>
        public static long EstimateTotalBytes(IReadOnlyList<Row> rows)
        {
            if (rows.Count == 0) return 0;
            int avgWidth = EstimateAverageRowBytes(rows);
            return (long)rows.Count * avgWidth;
        }

        /// <summary>Returns the estimated heap size in bytes for one value.</summary>
        public static int EstimateValueBytes(object? value) => value switch
        {
            null => 4,
            bool => 1,
            byte or sbyte => 1,
            short or ushort or char => 2,
            int or uint => 4,
            long or ulong => 8,
            float => 4,
            double => 8,
            decimal => 16,
            DateTime => 8,
            DateTimeOffset => 12,
            TimeSpan => 8,
            Guid => 16,
            string s => 24 + s.Length * 2,
            byte[] b => 24 + b.Length,
            _ => DefaultBytesPerRow,
        };
    }
}
