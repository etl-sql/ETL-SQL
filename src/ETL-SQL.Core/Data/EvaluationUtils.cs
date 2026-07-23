using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;

namespace ETL_SQL.Core.Data;

public static class EvaluationUtils
{
    public static bool IsSoftEqual(object? a, object? b, ILogger? logger = null, bool caseSensitive = false)
    {
        if (a.IsNull() && b.IsNull()) return true;
        if (a.IsNull() || b.IsNull()) return false;

        try
        {
            if (a is Row ra && b is Row rb)
            {
                if (ra.Columns.Count != rb.Columns.Count) return false;
                foreach (var kvp in ra.Columns)
                {
                    if (!rb.Columns.TryGetValue(kvp.Key, out var bVal)) return false;
                    if (!IsSoftEqual(kvp.Value, bVal, logger)) return false;
                }
                return true;
            }

            // SCALAR vs ROW comparison (Phase 9F fix for STRING_SPLIT IN filtering)
            // If one side is a row and the other is a scalar, compare the first column.
            if (a is Row r1 && !(b is Row)) return IsSoftEqual(r1[0], b, logger);
            if (b is Row r2 && !(a is Row)) return IsSoftEqual(a, r2[0], logger);

            if (a is byte[] || b is byte[])
            {
                if (a is byte[] bytesA && b is byte[] bytesB)
                {
                    return bytesA.AsSpan().SequenceEqual(bytesB.AsSpan());
                }
                return false;
            }

            if (a.IsNull() || b.IsNull())
                return a.IsNull() && b.IsNull();

            if (a is decimal da && b is decimal db) return da == db;
            if (a is int ia && b is int ib) return ia == ib;
            if (a is long la && b is long lb) return la == lb;
            if (a is double dbla && b is double dblb) return dbla == dblb;

            if (a is DateTime dta && b is DateTime dtb)
                return dta.Ticks / TimeSpan.TicksPerSecond == dtb.Ticks / TimeSpan.TicksPerSecond;

            // Support ON/OFF boolean literals vs string equivalents
            if (a is bool ba && b is string sb1 && (sb1.Equals("ON", StringComparison.OrdinalIgnoreCase) || sb1.Equals("OFF", StringComparison.OrdinalIgnoreCase)))
                return ba == sb1.Equals("ON", StringComparison.OrdinalIgnoreCase);
            if (b is bool bb && a is string sa1 && (sa1.Equals("ON", StringComparison.OrdinalIgnoreCase) || sa1.Equals("OFF", StringComparison.OrdinalIgnoreCase)))
                return bb == sa1.Equals("ON", StringComparison.OrdinalIgnoreCase);

            if (decimal.TryParse(a?.ToString(), out var m1) && decimal.TryParse(b?.ToString(), out var m2)) return m1 == m2;

            if (DateTime.TryParse(a?.ToString(), out var dt1) && DateTime.TryParse(b?.ToString(), out var dt2)) return dt1.Year == dt2.Year && dt1.Month == dt2.Month && dt1.Day == dt2.Day && dt1.Hour == dt2.Hour && dt1.Minute == dt2.Minute && dt1.Second == dt2.Second;
        }
        catch (Exception ex)
        {
            if (logger != null) logger.Debug($"[EvaluationUtils.IsSoftEqual] Type coercion failed, falling back to string compare: {ex.Message}");
        }

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return a?.ToString()?.Equals(b?.ToString(), comparison) ?? false;
    }

    public static int CompareConstants(object? a, object? b, bool caseSensitive = false)
    {
        if (a.IsNull() && b.IsNull()) return 0;
        if (a.IsNull()) return -1;
        if (b.IsNull()) return 1;

        if (a is byte[] || b is byte[])
        {
            if (a is byte[] ba && b is byte[] bb)
            {
                int minLen = Math.Min(ba.Length, bb.Length);
                for (int i = 0; i < minLen; i++)
                {
                    int cmp = ba[i].CompareTo(bb[i]);
                    if (cmp != 0) return cmp;
                }
                return ba.Length.CompareTo(bb.Length);
            }
            return a is byte[]? 1 : -1;
        }

        // Fast path: same runtime type that compares identically to the decimal round-trip below
        // (integers and decimals — runtime numerics are decimal). The sort/merge and aggregate hot
        // paths call this O(n log n) times, so skipping the two ToString allocations + two
        // decimal.Parse per comparison is significant. double/float and string are intentionally
        // excluded so their existing (string/decimal-parse) semantics are preserved.
        if (a is decimal ma && b is decimal mb) return ma.CompareTo(mb);
        if (a is long la && b is long lb) return la.CompareTo(lb);
        if (a is int ia && b is int ib) return ia.CompareTo(ib);

        // Numeric comparison via decimal (handles mixed numeric types and numeric-looking strings).
        // TryParse instead of Parse-in-try/catch avoids throwing and catching a FormatException on
        // every non-numeric comparison (e.g. a plain string sort) — an expensive per-call cost.
        if (decimal.TryParse(a?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var da)
            && decimal.TryParse(b?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var db))
            return da.CompareTo(db);

        if (TryToDateTime(a, out var dta) && TryToDateTime(b, out var dtb))
            return dta.CompareTo(dtb);

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return string.Compare(a?.ToString() ?? "", b?.ToString() ?? "", comparison);
    }

    public static bool TryToDateTime(object? val, out DateTime dt)
    {
        if (val is DateTime d) { dt = d; return true; }
        if (val is DateTimeOffset dto) { dt = dto.DateTime; return true; }
        if (SafeTryParseDateTimeOffset(val?.ToString() ?? "", out var dto2))
        {
            dt = dto2.DateTime;
            return true;
        }
        return SafeTryParseDate(val?.ToString() ?? "", out dt);
    }

    public static bool TryToDateTimeOffset(object? val, out DateTimeOffset dto)
    {
        if (val is DateTimeOffset dtoBe) { dto = dtoBe; return true; }
        if (val is DateTime dt) { dto = new DateTimeOffset(dt); return true; }
        return SafeTryParseDateTimeOffset(val?.ToString() ?? "", out dto);
    }

    public static bool SafeTryParseDateTimeOffset(string s, out DateTimeOffset dto)
    {
        s = s?.Trim() ?? "";
        if (string.IsNullOrEmpty(s)) { dto = default; return false; }

        string[] formats = {
            "yyyy-MM-dd HH:mm:ss.ffffff zzz",
            "yyyy-MM-dd HH:mm:ss.fff zzz",
            "yyyy-MM-dd HH:mm:ss zzz",
            "yyyy-MM-ddTHH:mm:ss.ffffffzzz",
            "yyyy-MM-ddTHH:mm:ss.fffzzz",
            "yyyy-MM-ddTHH:mm:sszzz",
            "yyyy-MM-dd",
            "yyyyMMdd",
            "yyyy-MM-dd HH:mm:ss"
        };

        if (DateTimeOffset.TryParseExact(s, formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dto))
        {
            return true;
        }

        if (DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dto))
        {
            return true;
        }

        return false;
    }

    public static bool SafeTryParseDate(string s, out DateTime dt)
    {
        s = s?.Trim() ?? "";
        if (string.IsNullOrEmpty(s)) { dt = default; return false; }

        // 1. Try ISO first
        if (DateTime.TryParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt)) return true;

        // 2. Try dd/MM/yyyy specifically (PRIORITY)
        if (DateTime.TryParseExact(s, "dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt)) return true;
        if (DateTime.TryParseExact(s, "d/M/yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt)) return true;

        // 3. Manual fallback for dd/MM/yyyy
        if (s.Contains("/"))
        {
            var parts = s.Split('/');
            if (parts.Length == 3 && int.TryParse(parts[0], out int d) && int.TryParse(parts[1], out int m) && int.TryParse(parts[2], out int y))
            {
                if (y < 100) y += 2000;
                if (m >= 1 && m <= 12 && d >= 1 && d <= DateTime.DaysInMonth(y, m))
                {
                    dt = new DateTime(y, m, d);
                    return true;
                }
            }
        }

        // 4. Fallback to others
        string[] otherFormats = { "yyyyMMdd", "MM/dd/yyyy", "M/d/yyyy", "yyyy/MM/dd", "yyyy-MM-dd HH:mm:ss" };
        if (DateTime.TryParseExact(s, otherFormats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AllowWhiteSpaces, out dt)) return true;

        if (DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AllowWhiteSpaces, out dt)) return true;
        return false;
    }

    public static object? MathOp(object? a, object? b, string op)
    {
        TokenType? tokenType = op switch { "+" => TokenType.PLUS, "-" => TokenType.MINUS, "*" => TokenType.STAR, "/" => TokenType.SLASH, "%" => TokenType.MODULO, _ => null };
        if (tokenType == null) return null;
        return BinaryOperatorFactory.Execute(tokenType.Value, a, b);
    }

    public static bool EvaluateLike(object? input, object? pattern, string? escapeChar = null)
    {
        if (input == null || pattern == null) return false;
        string s = input.ToString() ?? "";
        string p = pattern.ToString() ?? "";

        string regexPattern;
        if (!string.IsNullOrEmpty(escapeChar) && escapeChar.Length == 1)
        {
            char esc = escapeChar[0];
            // Manually build regex for explicitly escaped sequences vs non-escaped wildcards
            var sb = new System.Text.StringBuilder("^");
            bool escaped = false;
            for (int i = 0; i < p.Length; i++)
            {
                char c = p[i];
                if (escaped)
                {
                    sb.Append(Regex.Escape(c.ToString()));
                    escaped = false;
                }
                else if (c == esc)
                {
                    escaped = true;
                }
                else if (c == '%')
                {
                    sb.Append(".*");
                }
                else if (c == '_')
                {
                    sb.Append(".");
                }
                else
                {
                    sb.Append(Regex.Escape(c.ToString()));
                }
            }
            if (escaped)
            {
                // hanging escape character
                sb.Append(Regex.Escape(esc.ToString()));
            }
            sb.Append("$");
            regexPattern = sb.ToString();
        }
        else
        {
            regexPattern = "^" + Regex.Escape(p).Replace("%", ".*").Replace("_", ".") + "$";
        }

        return Regex.IsMatch(s, regexPattern, RegexOptions.IgnoreCase);
    }

    public static object? CastToType(object? value, string? type)
    {
        if (value == null) return null;
        try
        {
            return TypeConverter.Cast(value, type);
        }
        catch (Exception ex)
        {
            throw new ExecutionException($"Failed to cast value '{value}' to type '{type}': {ex.Message}", ex);
        }
    }
}
