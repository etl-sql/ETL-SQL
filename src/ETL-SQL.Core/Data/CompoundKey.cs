using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ETL_SQL.Data
{
    /// <summary>
    /// Represents a multi-column key for efficient hashing and comparison in Joins and Aggregations.
    /// Avoids expensive string concatenation by storing raw objects and computing a stable hash.
    /// </summary>
    public readonly struct CompoundKey : IEquatable<CompoundKey>, IComparable<CompoundKey>
    {
        private readonly object?[] _values;
        private readonly int _hashCode;

        public CompoundKey(params object?[] values)
        {
            _values = values ?? Array.Empty<object?>();
            
            // Pre-calculate hash code for performance in dictionary lookups
            var hash = new HashCode();
            foreach (var val in _values)
            {
                hash.Add(NormalizeValue(val));
            }
            _hashCode = hash.ToHashCode();
        }

        public bool Equals(CompoundKey other)
        {
            if (_values.Length != other._values.Length) return false;
            for (int i = 0; i < _values.Length; i++)
            {
                if (!ValuesEqual(_values[i], other._values[i])) return false;
            }
            return true;
        }

        public override bool Equals(object? obj) => obj is CompoundKey other && Equals(other);

        public override int GetHashCode() => _hashCode;

        public static bool operator ==(CompoundKey left, CompoundKey right) => left.Equals(right);
        public static bool operator !=(CompoundKey left, CompoundKey right) => !left.Equals(right);

        private static object? NormalizeValue(object? val)
        {
            if (val == null || val == DBNull.Value) return null;

            // Unwrap JsonElement that arrives after JSON deserialization (spill-to-disk paths)
            if (val is JsonElement je)
            {
                val = je.ValueKind switch
                {
                    JsonValueKind.Number when je.TryGetDecimal(out var dv) => dv,
                    JsonValueKind.Number => je.GetDouble(),
                    JsonValueKind.True  => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String => je.GetString(),
                    JsonValueKind.Null  => null,
                    _                   => (object?)je.ToString()
                };
                if (val == null) return null;
            }
            
            // Ensure numeric types hash consistently. 
            // IMPORTANT: In .NET, different scales of decimal (e.g. 1.0m vs 1m) have different hash codes!
            // We normalize all decimals by dividing by 1.000... which strips trailing zeros.
            if (val is int i)    return (decimal)i / 1.00000000000000000000000000000m;
            if (val is long l)   return (decimal)l / 1.00000000000000000000000000000m;
            if (val is double d) return (decimal)d / 1.00000000000000000000000000000m;
            if (val is float f)  return (decimal)f / 1.00000000000000000000000000000m;
            if (val is decimal dec) return dec / 1.00000000000000000000000000000m;
            
            if (val is DateTime dt) return dt;
            if (val is DateTimeOffset dto) return dto.DateTime;

            // Ensure dates and numbers in strings are normalized
            if (val is string s)
            {
                if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var dec2)) 
                    return dec2 / 1.00000000000000000000000000000m;
                
                if (DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt2))
                    return dt2;

                return s.Trim(); 
            }

            return val;
        }

        private static bool ValuesEqual(object? a, object? b)
        {
            var normA = NormalizeValue(a);
            var normB = NormalizeValue(b);

            if (normA == null) return normB == null;
            if (normB == null) return false;

            return normA.Equals(normB);
        }

        public int CompareTo(CompoundKey other)
        {
            int len = Math.Min(_values.Length, other._values.Length);
            for (int i = 0; i < len; i++)
            {
                int cmp = CompareValues(_values[i], other._values[i]);
                if (cmp != 0) return cmp;
            }
            return _values.Length.CompareTo(other._values.Length);
        }

        private static int CompareValues(object? a, object? b)
        {
            var normA = NormalizeValue(a);
            var normB = NormalizeValue(b);

            if (normA == null) return normB == null ? 0 : -1;
            if (normB == null) return 1;

            if (normA is IComparable comp)
            {
                try
                {
                    // Handle numeric comparisons across types if Normalize didn't quite catch everything
                    if (normA is decimal d1 && normB is decimal d2) return d1.CompareTo(d2);
                    return comp.CompareTo(normB);
                }
                catch
                {
                    return string.Compare(normA.ToString(), normB.ToString(), StringComparison.OrdinalIgnoreCase);
                }
            }
            return string.Compare(normA.ToString(), normB.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public override string ToString() => string.Join("|", _values.Select(v => v?.ToString() ?? "NULL"));
    }
}
