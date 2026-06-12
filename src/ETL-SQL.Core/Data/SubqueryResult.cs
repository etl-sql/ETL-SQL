using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Data;

namespace ETL_SQL.Core.Data
{
    /// <summary>
    /// Represents the cached result of a subquery. 
    /// Supports both scalar values and large row-streams (with optional spill-to-disk).
    /// </summary>
    public sealed class SubqueryResult : IAsyncDisposable
    {
        /// <summary>The scalar value of the subquery (for SELECT list or WHERE comparison).</summary>
        public object? ScalarValue { get; }

        /// <summary>A hash set of values for optimized IN-clause lookups.</summary>
        public HashSet<object?>? InSet { get; }

        /// <summary>The full row-set of the subquery (for complex stream processing).</summary>
        public InMemoryDataSource? StreamData { get; }

        /// <summary>True if this result represents a single scalar value.</summary>
        public bool IsScalar => StreamData == null && InSet == null;

        public long MemoryUsageBytes
        {
            get
            {
                if (ScalarValue != null) return 128; // fixed overhead
                if (InSet != null) return InSet.Count * 64L; // rough estimate per entry
                if (StreamData != null) return StreamData.MemoryUsageBytes;
                return 0;
            }
        }

        public SubqueryResult(object? scalarValue)
        {
            ScalarValue = scalarValue;
        }

        public SubqueryResult(HashSet<object?> inSet)
        {
            InSet = inSet;
        }

        public SubqueryResult(InMemoryDataSource streamData)
        {
            StreamData = streamData;
        }

        public async ValueTask DisposeAsync()
        {
            if (StreamData != null)
            {
                await StreamData.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Comparer for subquery result sets that follows "soft equality" rules 
    /// (e.g., 1 == 1.0 == "1") for stable hashing in cached sets.
    /// </summary>
    public class CanonicalEqualityComparer : IEqualityComparer<object?>
    {
        public static readonly CanonicalEqualityComparer Instance = new();

        public new bool Equals(object? x, object? y)
        {
            return EvaluationUtils.IsSoftEqual(x, y);
        }

        public int GetHashCode(object? obj)
        {
            if (obj == null || obj == DBNull.Value) return 0;

            // Normalize to a canonical form for stable hashing
            if (obj is decimal d) return d.GetHashCode();
            if (obj is int i) return ((decimal)i).GetHashCode();
            if (obj is long l) return ((decimal)l).GetHashCode();
            if (obj is double dbl) return ((decimal)dbl).GetHashCode();
            if (obj is float f) return ((decimal)f).GetHashCode();

            if (decimal.TryParse(obj.ToString(), out var m)) return m.GetHashCode();

            if (obj is DateTime dt) return dt.GetHashCode();
            if (DateTime.TryParse(obj.ToString(), out var dt2)) return dt2.GetHashCode();

            if (obj is bool b) return b.GetHashCode();
            if (obj.ToString()?.Equals("ON", StringComparison.OrdinalIgnoreCase) == true) return true.GetHashCode();
            if (obj.ToString()?.Equals("OFF", StringComparison.OrdinalIgnoreCase) == true) return false.GetHashCode();

            return obj.ToString()?.ToLowerInvariant().GetHashCode() ?? 0;
        }
    }
}
