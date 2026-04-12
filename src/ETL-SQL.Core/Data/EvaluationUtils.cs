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
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            if (decimal.TryParse(a.ToString(), out var da) && decimal.TryParse(b.ToString(), out var db)) return da.CompareTo(db);
            if (DateTime.TryParse(a.ToString(), out var dta) && DateTime.TryParse(b.ToString(), out var dta2)) return dta.CompareTo(dta2);

            return string.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
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
            return TypeConverter.Cast(value, type);
        }
    }
}
