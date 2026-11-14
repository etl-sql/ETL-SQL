using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Data;
/// <summary>
/// Represents a multi-column key for efficient hashing and comparison in Joins and Aggregations.
/// Avoids expensive string concatenation by storing raw objects and computing a stable hash.
/// </summary>
public readonly struct CompoundKey : IEquatable<CompoundKey>, IComparable<CompoundKey>
{
    private readonly object?[] _values;
    private readonly int _setIndex;
    private readonly int _hashCode;
    private readonly bool _caseInsensitive;
    private readonly bool _isJoinKey;
    // The text a group key was built from, where normalizing changed it. Not part of equality.
    private readonly object?[]? _originals;

    public int Length => _values.Length;
    public object? this[int index] => _values[index];

    public CompoundKey(object? val1)
    {
        _setIndex = 0;
        var n1 = NormalizeValue(val1);
        _values = new[] { n1 };
        _hashCode = HashCode.Combine(_setIndex, n1);
    }

    public CompoundKey(object? val1, object? val2)
    {
        _setIndex = 0;
        var n1 = NormalizeValue(val1);
        var n2 = NormalizeValue(val2);
        _values = new[] { n1, n2 };
        _hashCode = HashCode.Combine(_setIndex, n1, n2);
    }

    public CompoundKey(object? val1, object? val2, object? val3)
    {
        _setIndex = 0;
        var n1 = NormalizeValue(val1);
        var n2 = NormalizeValue(val2);
        var n3 = NormalizeValue(val3);
        _values = new[] { n1, n2, n3 };
        _hashCode = HashCode.Combine(_setIndex, n1, n2, n3);
    }

    public CompoundKey(params object?[] values) : this(0, values) { }

    public CompoundKey(int setIndex, params object?[] values) : this(setIndex, true, false, values) { }

    /// <summary>Creates a join key using the active string comparison setting.</summary>
    public static CompoundKey CreateJoinKey(bool caseSensitive, object?[] values, int setIndex = 0) =>
        new(setIndex, caseSensitive, true, values);

    /// <summary>
    /// Creates a group key that remembers the text it was built from. <c>'123'</c> and <c>123</c>
    /// still fall in one group, and <see cref="Original"/> returns the first value the group saw.
    /// </summary>
    public static CompoundKey ForGroup(object?[] values) => new(0, true, false, values, keepOriginals: true);

    /// <summary>The value to return for a grouping column: text stays text.</summary>
    public object? Original(int index) => _originals != null ? _originals[index] : _values[index];

    private CompoundKey(int setIndex, bool caseSensitive, bool isJoinKey, object?[] values, bool keepOriginals = false)
    {
        _setIndex = setIndex;
        _caseInsensitive = !caseSensitive;
        _isJoinKey = isJoinKey;
        if (values == null || values.Length == 0)
        {
            _values = Array.Empty<object?>();
            _hashCode = HashCode.Combine(_setIndex);
            return;
        }

        _values = new object?[values.Length];
        var hash = new HashCode();
        hash.Add(_setIndex);
        for (int i = 0; i < values.Length; i++)
        {
            var normalized = NormalizeValue(values[i]);
            // COMPAT_BREAK: 0.20.0 — join equality and hashing honor case-insensitive comparison.
            _values[i] = normalized;
            if (_caseInsensitive && normalized is string text)
                hash.Add(text, StringComparer.OrdinalIgnoreCase);
            else
                hash.Add(normalized);
        }
        _hashCode = hash.ToHashCode();

        if (!keepOriginals) return;
        for (int i = 0; i < values.Length; i++)
        {
            var original = values[i];
            if (original is null || (original is string s && ReferenceEquals(s, _values[i])) || (original is not string && original is not DateOnly))
                continue;
            _originals ??= (object?[])_values.Clone();
            _originals[i] = original;
        }
    }

    public bool Equals(CompoundKey other)
    {
        if (_hashCode != other._hashCode) return false;
        if (_setIndex != other._setIndex) return false;
        if (_caseInsensitive != other._caseInsensitive) return false;
        if (_values.Length != other._values.Length) return false;
        for (int i = 0; i < _values.Length; i++)
        {
            if (_caseInsensitive && _values[i] is string left && other._values[i] is string right)
            {
                if (!StringComparer.OrdinalIgnoreCase.Equals(left, right)) return false;
            }
            else if (!object.Equals(_values[i], other._values[i])) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is CompoundKey other && Equals(other);

    public override int GetHashCode() => _hashCode;

    /// <summary>Computes the same salted single-value hash used by a one-column compound key.</summary>
    public static int GetNormalizedHashCode(int setIndex, object? normalizedValue)
    {
        var hash = new HashCode();
        hash.Add(setIndex);
        hash.Add(normalizedValue);
        return hash.ToHashCode();
    }

    public static bool operator ==(CompoundKey left, CompoundKey right) => left.Equals(right);
    public static bool operator !=(CompoundKey left, CompoundKey right) => !left.Equals(right);

    public static object? NormalizeValue(object? val)
    {
        if (val == null || val == DBNull.Value) return null;

        // Unwrap JsonElement that arrives after JSON deserialization (spill-to-disk paths)
        if (val is JsonElement je)
        {
            val = je.ValueKind switch
            {
                JsonValueKind.Number when je.TryGetDecimal(out var dv) => dv,
                JsonValueKind.Number => je.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Null => null,
                _ => (object?)je.ToString()
            };
            if (val == null) return null;
        }

        // Ensure numeric types hash consistently. 
        // IMPORTANT: In .NET, different scales of decimal (e.g. 1.0m vs 1m) have different hash codes!
        // We normalize all decimals by dividing by 1.000... which strips trailing zeros.
        if (val is int i) return (decimal)i / 1.00000000000000000000000000000m;
        if (val is long l) return (decimal)l / 1.00000000000000000000000000000m;
        if (val is double d) return (decimal)d / 1.00000000000000000000000000000m;
        if (val is float f) return (decimal)f / 1.00000000000000000000000000000m;
        if (val is decimal dec) return dec / 1.00000000000000000000000000000m;

        if (val is DateTime dt) return dt;
        if (val is DateOnly dateOnly) return dateOnly.ToDateTime(TimeOnly.MinValue);
        if (val is DateTimeOffset dto) return dto.DateTime;

        // Ensure dates and numbers in strings are normalized
        if (val is string s)
        {
            // Only plain number text ('123', '-4.50') keys as a number, so '1' still joins to 1.
            // Codes such as '00123', '1,000' or '(5)' stay text: parsing them merged distinct
            // values into one group, and the group came back as the number.
            if (IsPlainNumber(s.Trim())
                && decimal.TryParse(s.Trim(), System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var dec2))
                return dec2 / 1.00000000000000000000000000000m;

            if (EvaluationUtils.SafeTryParseDate(s, out var dt2))
                return dt2;

            return s.Trim();
        }

        return val;
    }

    // -?(0|[1-9][0-9]*)(\.[0-9]+)?
    private static bool IsPlainNumber(string s)
    {
        int i = 0;
        if (i < s.Length && s[i] == '-') i++;
        int start = i;
        while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
        int digits = i - start;
        if (digits == 0 || (digits > 1 && s[start] == '0')) return false;
        if (i == s.Length) return true;
        if (s[i] != '.') return false;
        int fraction = ++i;
        while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
        return i > fraction && i == s.Length;
    }

    public int CompareTo(CompoundKey other)
    {
        int setCmp = _setIndex.CompareTo(other._setIndex);
        if (setCmp != 0) return setCmp;

        int len = Math.Min(_values.Length, other._values.Length);
        for (int i = 0; i < len; i++)
        {
            // COMPAT_BREAK: 0.20.0 — merge ordering must use the same comparison as hash equality.
            int cmp = _isJoinKey && other._isJoinKey && _values[i] is string left && other._values[i] is string right
                ? string.Compare(left, right, _caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                : CompareValues(_values[i], other._values[i]);
            if (cmp != 0) return cmp;
        }
        return _values.Length.CompareTo(other._values.Length);
    }

    private static int CompareValues(object? normA, object? normB)
    {

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
