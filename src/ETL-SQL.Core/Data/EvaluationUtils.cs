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

            // Full precision, as CompareConstants orders. Equality used to drop the fraction of a
            // second, so two times in one second were both equal and ordered: a = b and a < b.
            if (a is DateTime dta && b is DateTime dtb)
                return dta.Ticks == dtb.Ticks;

            // Support ON/OFF boolean literals vs string equivalents
            if (a is bool ba && b is string sb1 && (sb1.Equals("ON", StringComparison.OrdinalIgnoreCase) || sb1.Equals("OFF", StringComparison.OrdinalIgnoreCase)))
                return ba == sb1.Equals("ON", StringComparison.OrdinalIgnoreCase);
            if (b is bool bb && a is string sa1 && (sa1.Equals("ON", StringComparison.OrdinalIgnoreCase) || sa1.Equals("OFF", StringComparison.OrdinalIgnoreCase)))
                return bb == sa1.Equals("ON", StringComparison.OrdinalIgnoreCase);

            if (decimal.TryParse(Convert.ToString(a, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var m1) && decimal.TryParse(Convert.ToString(b, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var m2)) return m1 == m2;

            if (TryToDateTime(a, out var dt1) && TryToDateTime(b, out var dt2)) return dt1.Ticks == dt2.Ticks;
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
        //
        // Values order by kind first: numbers, then dates, then other text. A number against text
        // that is not a number used to compare as text, which gave no consistent order
        // (9 < 10, 10 < '5x' and '5x' < 9 all held), so a sort had no right answer.
        bool aNumber = TryOrderAsNumber(a, out var da);
        bool bNumber = TryOrderAsNumber(b, out var db);
        if (aNumber && bNumber) return da.CompareTo(db);
        if (aNumber != bNumber) return aNumber ? -1 : 1;

        bool aDate = TryOrderAsDate(a, out var dta);
        bool bDate = TryOrderAsDate(b, out var dtb);
        if (aDate && bDate) return dta.CompareTo(dtb);
        if (aDate != bDate) return aDate ? -1 : 1;

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return string.Compare(a?.ToString() ?? "", b?.ToString() ?? "", comparison);
    }

    private static bool TryOrderAsNumber(object? value, out decimal number)
    {
        number = 0;
        if (value is string text && !HasDigit(text)) return false;
        return decimal.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out number);
    }

    private static bool TryOrderAsDate(object? value, out DateTime date)
    {
        date = default;
        if (value is string text && !HasDigit(text)) return false;
        return TryToDateTime(value, out date);
    }

    // Text with no digit is neither a number nor a date. Checking first keeps the parsers off the
    // plain-text sort path.
    private static bool HasDigit(string text)
    {
        foreach (var c in text)
            if (c is >= '0' and <= '9') return true;
        return false;
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

        // Slash dates follow the session date order (SET DATEFORMAT, default MDY), as CAST AS DATE
        // reads them; the invariant parse below would always read them month first.
        if (s.Contains('/') && SafeTryParseDate(s, out var slashDate))
        {
            dto = new DateTimeOffset(slashDate);
            return true;
        }

        if (DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dto))
        {
            return true;
        }

        return false;
    }

    private static readonly string[] MonthFirstSlashFormats =
    [
        "M/d/yyyy", "M/d/yyyy H:mm", "M/d/yyyy H:mm:ss", "M/d/yyyy H:mm:ss.FFFFFFF",
        "M/d/yyyy h:mm tt", "M/d/yyyy h:mm:ss tt"
    ];

    private static readonly string[] DayFirstSlashFormats =
    [
        "d/M/yyyy", "d/M/yyyy H:mm", "d/M/yyyy H:mm:ss", "d/M/yyyy H:mm:ss.FFFFFFF",
        "d/M/yyyy h:mm tt", "d/M/yyyy h:mm:ss tt"
    ];

    /// <summary>
    /// Text read as a date, the same on every host: ISO first, then slash dates in the session date
    /// order (<see cref="DateTextOrder"/>: SET DATEFORMAT, default MDY, so <c>08/01/2024</c> is
    /// 1 August as in SQL Server and PostgreSQL), then the other order when the first reading is
    /// impossible (<c>25/01/2024</c>), then a few unambiguous forms and the invariant culture.
    /// CAST AS DATE and every engine path that reads text as a date use this.
    /// </summary>
    public static bool SafeTryParseDate(string s, out DateTime dt) =>
        SafeTryParseDate(s, DateTextOrder.Current, out dt);

    /// <inheritdoc cref="SafeTryParseDate(string, out DateTime)"/>
    public static bool SafeTryParseDate(string s, DateOrder order, out DateTime dt)
    {
        s = s?.Trim() ?? "";
        if (string.IsNullOrEmpty(s)) { dt = default; return false; }
        var invariant = System.Globalization.CultureInfo.InvariantCulture;

        if (DateTime.TryParseExact(s, "yyyy-MM-dd", invariant, System.Globalization.DateTimeStyles.None, out dt)) return true;

        if (s.Contains('/'))
        {
            var (preferred, other) = order == DateOrder.DayFirst
                ? (DayFirstSlashFormats, MonthFirstSlashFormats)
                : (MonthFirstSlashFormats, DayFirstSlashFormats);
            if (DateTime.TryParseExact(s, preferred, invariant, System.Globalization.DateTimeStyles.None, out dt)) return true;
            if (DateTime.TryParseExact(s, other, invariant, System.Globalization.DateTimeStyles.None, out dt)) return true;

            // Two-digit years (8/1/24) are 20xx.
            var parts = s.Split('/');
            if (parts.Length == 3 && int.TryParse(parts[0], out int a) && int.TryParse(parts[1], out int b) && int.TryParse(parts[2], out int y) && y < 100)
            {
                y += 2000;
                var (m, d) = order == DateOrder.DayFirst ? (b, a) : (a, b);
                if (m > 12) (m, d) = (d, m);
                if (m >= 1 && m <= 12 && d >= 1 && d <= DateTime.DaysInMonth(y, m))
                {
                    dt = new DateTime(y, m, d);
                    return true;
                }
            }
        }

        string[] otherFormats = { "yyyyMMdd", "yyyy/MM/dd", "yyyy-MM-dd HH:mm:ss" };
        if (DateTime.TryParseExact(s, otherFormats, invariant, System.Globalization.DateTimeStyles.AllowWhiteSpaces, out dt)) return true;

        if (DateTime.TryParse(s, invariant, System.Globalization.DateTimeStyles.AllowWhiteSpaces, out dt)) return true;
        return false;
    }

    public static object? MathOp(object? a, object? b, string op)
    {
        TokenType? tokenType = op switch { "+" => TokenType.PLUS, "-" => TokenType.MINUS, "*" => TokenType.STAR, "/" => TokenType.SLASH, "%" => TokenType.MODULO, _ => null };
        if (tokenType == null) return null;
        return BinaryOperatorFactory.Execute(tokenType.Value, a, b);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex> _likeRegexCache = new();
    private const int MaxLikeCacheSize = 1000;

    public static bool EvaluateLike(object? input, object? pattern, string? escapeChar = null)
    {
        if (input == null || pattern == null || input is DBNull || pattern is DBNull) return false;
        string s = input.ToString() ?? "";
        string p = pattern.ToString() ?? "";

        // Fast path for simple non-escaped patterns
        if (string.IsNullOrEmpty(escapeChar))
        {
            if (p == "%") return true;
            if (!p.Contains('%') && !p.Contains('_') && !p.Contains('[') && !p.Contains(']'))
                return string.Equals(s, p, StringComparison.OrdinalIgnoreCase);

            if (p.StartsWith('%') && p.EndsWith('%') && p.Length >= 2)
            {
                var middle = p.Substring(1, p.Length - 2);
                if (!middle.Contains('%') && !middle.Contains('_') && !middle.Contains('[') && !middle.Contains(']'))
                    return s.Contains(middle, StringComparison.OrdinalIgnoreCase);
            }
            else if (p.StartsWith('%') && p.Length >= 1)
            {
                var suffix = p.Substring(1);
                if (!suffix.Contains('%') && !suffix.Contains('_') && !suffix.Contains('[') && !suffix.Contains(']'))
                    return s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
            }
            else if (p.EndsWith('%') && p.Length >= 1)
            {
                var prefix = p.Substring(0, p.Length - 1);
                if (!prefix.Contains('%') && !prefix.Contains('_') && !prefix.Contains('[') && !prefix.Contains(']'))
                    return s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }
        }

        string cacheKey = (escapeChar ?? "") + ":" + p;
        if (!_likeRegexCache.TryGetValue(cacheKey, out var regex))
        {
            string regexPattern;
            if (!string.IsNullOrEmpty(escapeChar) && escapeChar.Length == 1)
            {
                char esc = escapeChar[0];
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
                    sb.Append(Regex.Escape(esc.ToString()));
                }
                sb.Append("$");
                regexPattern = sb.ToString();
            }
            else
            {
                regexPattern = "^" + Regex.Escape(p).Replace("%", ".*").Replace("_", ".") + "$";
            }

            regex = new Regex(regexPattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            if (_likeRegexCache.Count < MaxLikeCacheSize)
            {
                _likeRegexCache.TryAdd(cacheKey, regex);
            }
        }

        try
        {
            return regex.IsMatch(s);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// The error for a name nothing defines. It read as NULL when a row was in scope and as its own
    /// name when none was, so a misspelt column gave a column of NULLs and the script succeeded.
    /// </summary>
    public static ExecutionException UnknownName(string name, Row? row)
    {
        var known = row?.GetColumnNames().Where(n => !n.Contains('.')).Take(30).ToList();
        return new ExecutionException(known is { Count: > 0 }
            ? $"Unknown column '{name}'. Columns here: {string.Join(", ", known)}."
            : $"Unknown identifier '{name}'. It is not a column, a variable, a connection or a function.");
    }

    /// <summary>
    /// The error for a variable nothing declared. It read as NULL, so a misspelt variable in a
    /// filter matched no rows and the script succeeded.
    /// </summary>
    public static ExecutionException UnknownVariable(string name) =>
        new($"Unknown variable '{name}'. Declare it with DECLARE before it is read.");

    public static object? CastToType(object? value, string? type)
    {
        if (value is null or DBNull) return null;
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
