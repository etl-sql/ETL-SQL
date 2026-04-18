using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;

namespace ETL_SQL.Core.Data
{
    public static class EvaluationUtils
    {
        public static bool IsSoftEqual(object? a, object? b, ILogger? logger = null)
        {
            if ((a == null || a == DBNull.Value) && (b == null || b == DBNull.Value)) return true;
            if (a == null || a == DBNull.Value || b == null || b == DBNull.Value) return false;
            
            try {
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

                if (a == null || a == DBNull.Value || b == null || b == DBNull.Value) 
                    return (a == null || a == DBNull.Value) && (b == null || b == DBNull.Value);

                if (a is decimal da && b is decimal db) return da == db;
                if (a is int ia && b is int ib) return ia == ib;
                if (a is long la && b is long lb) return la == lb;
                if (a is double dbla && b is double dblb) return dbla == dblb;
                
                if (a is DateTime dta && b is DateTime dtb) return dta.Year == dtb.Year && dta.Month == dtb.Month && dta.Day == dtb.Day && dta.Hour == dtb.Hour && dta.Minute == dtb.Minute && dta.Second == dtb.Second;
                
                if (decimal.TryParse(a.ToString(), out var m1) && decimal.TryParse(b.ToString(), out var m2)) return m1 == m2;
                
                if (DateTime.TryParse(a.ToString(), out var dt1) && DateTime.TryParse(b.ToString(), out var dt2)) return dt1.Year == dt2.Year && dt1.Month == dt2.Month && dt1.Day == dt2.Day && dt1.Hour == dt2.Hour && dt1.Minute == dt2.Minute && dt1.Second == dt2.Second;
            }
            catch (Exception ex) 
            { 
                if (logger != null) logger.Debug($"[EvaluationUtils.IsSoftEqual] Type coercion failed, falling back to string compare: {ex.Message}");
            }

            return a.ToString()?.Equals(b.ToString(), StringComparison.OrdinalIgnoreCase) ?? false;
        }

        public static int CompareConstants(object? a, object? b)
        {
            if ((a == null || a == DBNull.Value) && (b == null || b == DBNull.Value)) return 0;
            if (a == null || a == DBNull.Value) return -1;
            if (b == null || b == DBNull.Value) return 1;

            string sa = a.ToString() ?? "";
            string sb = b.ToString() ?? "";

            if (decimal.TryParse(sa, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var da) && 
                decimal.TryParse(sb, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var db)) 
                return da.CompareTo(db);

            if (SafeTryParseDate(sa, out var dta) && SafeTryParseDate(sb, out var dtb)) 
                return dta.CompareTo(dtb);

            return string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);
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
            string[] otherFormats = { "MM/dd/yyyy", "M/d/yyyy", "yyyy/MM/dd", "yyyy-MM-dd HH:mm:ss" };
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

        public static object? CastToType(object? value, string type)
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
}
