using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Functions
{
    /// <summary>
    /// Provides a comprehensive suite of standard SQL functions (String, Math, Date, List processing).
    /// </summary>
    public static class StandardFunctions
    {
        /// <summary>Registers all standard SQL-compatible functions into the registry.</summary>
        public static void Register(IFunctionRegistry registry)
        {
            registry.Register("UPPER", (args, ctx) => args[0]?.ToString()?.ToUpper());
            registry.Register("LOWER", (args, ctx) => args[0]?.ToString()?.ToLower());
            registry.Register("LEN", Len);
            registry.Register("LENGTH", Len);
            registry.Register("APPEND_TO_LIST", AddToList);
            registry.Register("ADD_TO_LIST", AddToList);
            registry.Register("REMOVE_FROM_LIST", RemoveFromList);
            registry.Register("SORT_LIST", SortList);
            registry.Register("TRIM", (args, ctx) => args[0]?.ToString()?.Trim());
            registry.Register("LTRIM", (args, ctx) => args[0]?.ToString()?.TrimStart());
            registry.Register("RTRIM", (args, ctx) => args[0]?.ToString()?.TrimEnd());
            registry.Register("REVERSE", (args, ctx) => new string((args[0]?.ToString() ?? "").Reverse().ToArray()));
            registry.Register("ABS", (args, ctx) => args[0] == null ? null : Math.Abs(Convert.ToDecimal(args[0])));
            registry.Register("ROUND", Round);
            registry.Register("CEILING", (args, ctx) => args[0] == null ? null : Math.Ceiling(Convert.ToDecimal(args[0])));
            registry.Register("FLOOR", (args, ctx) => args[0] == null ? null : Math.Floor(Convert.ToDecimal(args[0])));
            registry.Register("SQRT", (args, ctx) => args[0] == null ? null : (decimal)Math.Sqrt(Convert.ToDouble(args[0])));
            registry.Register("CONCAT", (args, ctx) => string.Join("", args.Select(a => a?.ToString() ?? "")));
            registry.Register("SUBSTRING", Substring);
            registry.Register("SUBSTR", Substring);
            registry.Register("LEFT", Left);
            registry.Register("RIGHT", Right);
            registry.Register("CHARINDEX", CharIndex);
            registry.Register("INSTR", InStr);
            registry.Register("POWER", (args, ctx) => args.Count >= 2 ? (decimal)Math.Pow(Convert.ToDouble(args[0]), Convert.ToDouble(args[1])) : args.FirstOrDefault());
            registry.Register("DATENAME", DateName);
            registry.Register("DATEPART", DatePart);
            registry.Register("DATEDIFF", DateDiff);
            registry.Register("ISDATE", (args, ctx) => DateTime.TryParse(args[0]?.ToString(), out _) ? 1 : 0);
            registry.Register("EOMONTH", EoMonth);
            registry.Register("REPLACE", (args, ctx) => args.Count >= 3 ? args[0]?.ToString()?.Replace(args[1]?.ToString() ?? "", args[2]?.ToString() ?? "") : args[0]);
            registry.Register("INITCAP", (args, ctx) => args[0]?.ToString() == null ? null : System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(args[0]!.ToString()!.ToLower()));
            registry.Register("MOD", (args, ctx) => args.Count >= 2 && args[0] != null && args[1] != null ? Convert.ToDecimal(args[0]) % Convert.ToDecimal(args[1]) : args.FirstOrDefault());
            registry.Register("YEAR", (args, ctx) => args[0]?.ToString() == null ? null : (decimal)DateTime.Parse(args[0]!.ToString()!).Year);
            registry.Register("MONTH", (args, ctx) => args[0]?.ToString() == null ? null : (decimal)DateTime.Parse(args[0]!.ToString()!).Month);
            registry.Register("DAY", (args, ctx) => args[0]?.ToString() == null ? null : (decimal)DateTime.Parse(args[0]!.ToString()!).Day);
            registry.Register("COALESCE", (args, ctx) => args.FirstOrDefault(a => a != null));
            registry.Register("ISNULL", IsNull);
            registry.Register("NVL", IsNull);
            registry.Register("NULLIF", (args, ctx) => EvaluationUtils.IsSoftEqual(args.ElementAtOrDefault(0), args.ElementAtOrDefault(1)) ? null : args.ElementAtOrDefault(0));
            registry.Register("GETDATE", (args, ctx) => DateTime.Now);
            registry.Register("NOW", (args, ctx) => DateTime.Now);
            registry.Register("SYSDATE", (args, ctx) => DateTime.Now);
            registry.Register("SYSDATETIME", (args, ctx) => DateTime.Now);
            registry.Register("CAST", (args, ctx) => args.Count >= 2 ? EvaluationUtils.CastToType(args[0], args[1]?.ToString() ?? "STRING") : args[0]);
            registry.Register("COUNT", Count);
            registry.Register("IS_NULL", (args, ctx) => args[0] == null || args[0] == DBNull.Value);
            registry.Register("IS_NOT_NULL", (args, ctx) => args[0] != null && args[0] != DBNull.Value);
            registry.Register("IIF", (args, ctx) => args.Count >= 3 ? (Convert.ToBoolean(args[0]) ? args[1] : args[2]) : args.FirstOrDefault());
            registry.Register("IFNULL", IsNull);
            registry.Register("GREATEST", (args, ctx) => args.Where(a => a != null).OrderByDescending(a => a).FirstOrDefault());
            registry.Register("LEAST", (args, ctx) => args.Where(a => a != null).OrderBy(a => a).FirstOrDefault());
            registry.Register("GENERATE_SERIES", GenerateSeries);
            registry.Register("FILE_EXISTS", (args, ctx) => args.Count >= 1 && args[0] != null ? System.IO.File.Exists(ctx.ResolvePath(args[0]?.ToString() ?? "")) : false);
            registry.Register("DIRECTORY_EXISTS", (args, ctx) => args.Count >= 1 && args[0] != null ? System.IO.Directory.Exists(ctx.ResolvePath(args[0]?.ToString() ?? "")) : false);
            registry.Register("DATETIMEFROMPARTS", DateTimeFromParts);
            registry.Register("TIMEFROMPARTS", TimeFromParts);
            registry.Register("DATETIMEOFFSETSFROMPARTS", DateTimeOffsetsFromParts);
            registry.Register("HASHBYTES", HashBytes);
            registry.Register("NEWID", (args, ctx) => NewUuidV7());
            registry.Register("NEWSEQUENTIALID", (args, ctx) => NewUuidV7());
            registry.Register("CHECKSUM", Checksum);
            registry.Register("BINARY_CHECKSUM", Checksum);
            
            // Item 11 - String Extension Suite
            registry.Register("STUFF", Stuff);
            registry.Register("STRING_ESCAPE", StringEscape);
            registry.Register("STRING_SPLIT", StringSplit);
            registry.Register("ASCII", (args, ctx) => args[0]?.ToString() == null || args[0]!.ToString()!.Length == 0 ? null : (decimal)args[0]!.ToString()![0]);
            registry.Register("CHAR", (args, ctx) => args.Count >= 1 && args[0] != null ? ((char)Convert.ToInt32(args[0])).ToString() : null);
            registry.Register("FORMAT", Format);
            registry.Register("PATINDEX", PatIndex);
            registry.Register("STR", Str);
            registry.Register("QUOTENAME", QuoteName);
            registry.Register("TRANSLATE", Translate);
            registry.Register("UNICODE", (args, ctx) => args[0]?.ToString() == null || args[0]!.ToString()!.Length == 0 ? null : (decimal)args[0]!.ToString()![0]);
            registry.Register("DATALENGTH", DataLength);
            registry.Register("TO_STR", (args, ctx) => args[0]?.ToString());
            registry.Register("REPLICATE", (args, ctx) => args.Count >= 2 && args[0] != null ? string.Concat(Enumerable.Repeat(args[0]!.ToString(), Math.Max(0, Convert.ToInt32(args[1])))) : null);
            registry.Register("TRY_CAST", TryCast);

            // Item 13 - Math Extension Suite
            registry.Register("SIN", (args, ctx) => args[0] == null ? null : (decimal)Math.Sin(Convert.ToDouble(args[0])));
            registry.Register("COS", (args, ctx) => args[0] == null ? null : (decimal)Math.Cos(Convert.ToDouble(args[0])));
            registry.Register("TAN", (args, ctx) => args[0] == null ? null : (decimal)Math.Tan(Convert.ToDouble(args[0])));
            registry.Register("ASIN", (args, ctx) => args[0] == null ? null : (decimal)Math.Asin(Convert.ToDouble(args[0])));
            registry.Register("ACOS", (args, ctx) => args[0] == null ? null : (decimal)Math.Acos(Convert.ToDouble(args[0])));
            registry.Register("ATAN", (args, ctx) => args[0] == null ? null : (decimal)Math.Atan(Convert.ToDouble(args[0])));
            registry.Register("ATAN2", (args, ctx) => args.Count >= 2 ? (decimal)Math.Atan2(Convert.ToDouble(args[0]), Convert.ToDouble(args[1])) : null);
            registry.Register("SIGN", (args, ctx) => args[0] == null ? null : (decimal)Math.Sign(Convert.ToDecimal(args[0])));
        }

        /// <summary>Calculates the length of a string or collection.</summary>
        private static object? Len(List<object?> args, IExecutionContext ctx)
        {
            return args[0] is System.Collections.ICollection coll ? (decimal)coll.Count : (args[0] == null ? 0m : (decimal)(args[0]?.ToString()?.Length ?? 0));
        }

        /// <summary>Adds an item to a list.</summary>
        private static object? AddToList(List<object?> args, IExecutionContext ctx)
        {
            return args.Count >= 2 && args[0] is List<object?> alp ? alp.Concat(new[] { args[1] }).ToList() : args.FirstOrDefault();
        }

        /// <summary>Removes all occurrences of a value from a list.</summary>
        private static object? RemoveFromList(List<object?> args, IExecutionContext ctx)
        {
            return args.Count >= 2 && args[0] is List<object?> rfl ? rfl.Where(x => !EvaluationUtils.IsSoftEqual(x, args[1])).ToList() : args.FirstOrDefault();
        }

        /// <summary>Sorts a list in ascending order.</summary>
        private static object? SortList(List<object?> args, IExecutionContext ctx)
        {
            return args.Count >= 1 && args[0] is List<object?> sl ? sl.OrderBy(x => x).ToList() : args.FirstOrDefault();
        }

        /// <summary>Rounds a numeric value to a specified number of decimal places.</summary>
        private static object? Round(List<object?> args, IExecutionContext ctx)
        {
            return args.Count >= 2 ? Math.Round(Convert.ToDecimal(args[0]), Convert.ToInt32(args[1])) : (args.Count == 1 ? Math.Round(Convert.ToDecimal(args[0])) : null);
        }

        /// <summary>Extracts a substring from a given string based on a 1-based start index and length.</summary>
        private static object? Substring(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return args.FirstOrDefault();
            string? s = args[0]?.ToString();
            if (s == null) return null;
            int start = Convert.ToInt32(args[1]);
            int? len = args.Count >= 3 ? Convert.ToInt32(args[2]) : null;

            // Simple heuristic to detect if we called it as SUBSTR (1-based, potential negative start)
            // Note: ExpressionEvaluator had some 'fn' checks here, but we can't easily see 'fn' unless we pass it.
            // Let's assume SUBSTRING/SUBSTR both follow SQL convention (1-based).
            
            int csharpStart = start - 1;
            if (csharpStart < 0) csharpStart = 0; // Negative handling usually specific to SUBSTR in some dialects
            
            int csharpLen = len ?? (s.Length - csharpStart);

            if (csharpStart >= s.Length || csharpLen <= 0) return "";
            if (csharpStart + csharpLen > s.Length) csharpLen = s.Length - csharpStart;

            return s.Substring(csharpStart, csharpLen);
        }

        /// <summary>Extracts a specified number of characters from the left side of a string.</summary>
        private static object? Left(List<object?> args, IExecutionContext ctx)
        {
            string? s = args[0]?.ToString();
            if (s == null) return null;
            int len = Convert.ToInt32(args[1]);
            return len <= 0 ? "" : (len >= s.Length ? s : s.Substring(0, len));
        }

        /// <summary>Extracts a specified number of characters from the right side of a string.</summary>
        private static object? Right(List<object?> args, IExecutionContext ctx)
        {
            string? s = args[0]?.ToString();
            if (s == null) return null;
            int len = Convert.ToInt32(args[1]);
            return len <= 0 ? "" : (len >= s.Length ? s : s.Substring(s.Length - len));
        }

        /// <summary>Returns the 1-based index of a substring within a string (SQL Server style).</summary>
        private static object? CharIndex(List<object?> args, IExecutionContext ctx)
        {
            return args.Count >= 2 && args[1]?.ToString() != null ? (decimal)(args[1]!.ToString()!.IndexOf(args[0]?.ToString() ?? "", StringComparison.OrdinalIgnoreCase) + 1) : 0m;
        }

        /// <summary>Returns the 1-based index of a substring within a string (MySQL/PostgreSQL style).</summary>
        private static object? InStr(List<object?> args, IExecutionContext ctx)
        {
            return args.Count >= 2 && args[0]?.ToString() != null ? (decimal)(args[0]!.ToString()!.IndexOf(args[1]?.ToString() ?? "", StringComparison.OrdinalIgnoreCase) + 1) : 0m;
        }

        /// <summary>Returns a string representation of a specific date part (e.g., month name).</summary>
        private static object? DateName(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return null;
            string part = args[0]?.ToString()?.ToUpperInvariant() ?? "";
            if (!DateTime.TryParse(args[1]?.ToString(), out var dt)) return null;
            return part switch {
                "MONTH" or "MM" or "M" => dt.ToString("MMMM"),
                "WEEKDAY" or "DW" or "W" => dt.ToString("dddd"),
                "YEAR" or "YY" or "YYYY" => dt.Year.ToString(),
                _ => dt.ToString()
            };
        }

        /// <summary>Returns an integer representing a specific date part (e.g., year, hour).</summary>
        private static object? DatePart(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return null;
            string part = args[0]?.ToString()?.ToUpperInvariant() ?? "";
            if (!DateTime.TryParse(args[1]?.ToString(), out var dt)) return null;
            return part switch {
                "YEAR" or "YY" or "YYYY" => (decimal)dt.Year,
                "MONTH" or "MM" or "M" => (decimal)dt.Month,
                "DAY" or "DD" or "D" => (decimal)dt.Day,
                "HOUR" or "HH" => (decimal)dt.Hour,
                "MINUTE" or "MI" or "N" => (decimal)dt.Minute,
                "SECOND" or "SS" or "S" => (decimal)dt.Second,
                _ => (decimal)0
            };
        }

        /// <summary>Returns the difference between two dates in the specified units.</summary>
        private static object? DateDiff(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 3) return null;
            string part = args[0]?.ToString()?.ToUpperInvariant() ?? "";
            if (!DateTime.TryParse(args[1]?.ToString(), out var dt1)) return null;
            if (!DateTime.TryParse(args[2]?.ToString(), out var dt2)) return null;
            var diff = dt2 - dt1;
            return part switch {
                "YEAR" or "YY" or "YYYY" => (decimal)(dt2.Year - dt1.Year),
                "MONTH" or "MM" or "M" => (decimal)((dt2.Year - dt1.Year) * 12 + dt2.Month - dt1.Month),
                "DAY" or "DD" or "D" => (decimal)diff.TotalDays,
                "HOUR" or "HH" => (decimal)diff.TotalHours,
                "MINUTE" or "MI" or "N" => (decimal)diff.TotalMinutes,
                "SECOND" or "SS" or "S" => (decimal)diff.TotalSeconds,
                _ => (decimal)0
            };
        }

        /// <summary>Returns the last day of the month that contains the specified date.</summary>
        private static object? EoMonth(List<object?> args, IExecutionContext ctx)
        {
            if (args[0] == null || !DateTime.TryParse(args[0]?.ToString(), out var dt)) return null;
            var firstOfNextMonth = new DateTime(dt.Year, dt.Month, 1).AddMonths(1);
            return firstOfNextMonth.AddDays(-1);
        }

        /// <summary>Returns the second argument if the first is null (COALESCE/ISNULL style).</summary>
        private static object? IsNull(List<object?> args, IExecutionContext ctx)
        {
            return args.Count >= 2 ? (args[0] ?? args[1]) : args.FirstOrDefault();
        }

        /// <summary>Returns the number of elements in a collection or 1 if it's a non-null scalar.</summary>
        private static object? Count(List<object?> args, IExecutionContext ctx)
        {
            return (args[0] is System.Collections.ICollection ic) ? (decimal)ic.Count : (args[0] is System.Collections.IEnumerable ie && args[0] is not string ? (decimal)Enumerable.Count(ie.Cast<object>()) : (args[0] == null ? 0m : 1m));
        }

        /// <summary>Generates a numeric sequence based on start, stop, and step.</summary>
        private static object? GenerateSeries(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return null;
            long start = Convert.ToInt64(args[0]);
            long stop = Convert.ToInt64(args[1]);
            long step = args.Count >= 3 ? Convert.ToInt64(args[2]) : 1;
            
            var list = new List<object?>();
            for (long i = start; (step > 0 ? i <= stop : i >= stop); i += step)
            {
                list.Add(i);
                if (list.Count > 1000000) break; // Safety cap for function-returned lists
            }
            return list;
        }

        /// <summary>Constructs a DateTime value from year, month, day, and time components.</summary>
        private static object? DateTimeFromParts(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 7) throw new ExecutionException("DATETIMEFROMPARTS requires 7 arguments");
            return new DateTime(Convert.ToInt32(args[0]), Convert.ToInt32(args[1]), Convert.ToInt32(args[2]), Convert.ToInt32(args[3]), Convert.ToInt32(args[4]), Convert.ToInt32(args[5]), Convert.ToInt32(args[6]));
        }

        /// <summary>Constructs a TimeSpan value from hour, minute, second, and fractional components.</summary>
        private static object? TimeFromParts(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 5) throw new ExecutionException("TIMEFROMPARTS requires 5 arguments");
            return new TimeSpan(0, Convert.ToInt32(args[0]), Convert.ToInt32(args[1]), Convert.ToInt32(args[2]), Convert.ToInt32(args[3]));
        }

        /// <summary>Constructs a DateTimeOffset value from components including time zone offset.</summary>
        private static object? DateTimeOffsetsFromParts(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 10) throw new ExecutionException("DATETIMEOFFSETSFROMPARTS requires 10 arguments");
            return new DateTimeOffset(Convert.ToInt32(args[0]), Convert.ToInt32(args[1]), Convert.ToInt32(args[2]), Convert.ToInt32(args[3]), Convert.ToInt32(args[4]), Convert.ToInt32(args[5]), Convert.ToInt32(args[6]), new TimeSpan(Convert.ToInt32(args[7]), Convert.ToInt32(args[8]), 0));
        }

        /// <summary>Computes a cryptographic hash of a string using the specified algorithm (MD5, SHA1, SHA256, SHA512).</summary>
        private static object? HashBytes(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) throw new ExecutionException("HASHBYTES requires 2 arguments");
            string algo = args[0]?.ToString()?.ToUpperInvariant() ?? "MD5";
            byte[] data = System.Text.Encoding.UTF8.GetBytes(args[1]?.ToString() ?? "");
            using (System.Security.Cryptography.HashAlgorithm hash = algo switch
            {
                "MD5" => System.Security.Cryptography.MD5.Create(),
                "SHA1" => System.Security.Cryptography.SHA1.Create(),
                "SHA2_256" or "SHA256" => System.Security.Cryptography.SHA256.Create(),
                "SHA2_512" or "SHA512" => System.Security.Cryptography.SHA512.Create(),
                _ => throw new ExecutionException($"Unsupported hash algorithm: {algo}")
            })
            {
                return hash.ComputeHash(data);
            }
        }

        /// <summary>Generates a UUID v7 (RFC 9562): time-ordered, random GUID.</summary>
        private static Guid NewUuidV7() => Guid.CreateVersion7();

        /// <summary>Computes a 64-bit checksum / hash of the input values.</summary>
        private static object? Checksum(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 1) return 0L;
            // Use a more robust 64-bit hash for "strictly unique" requirement
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] h = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("|", args.Select(a => a?.ToString() ?? "NULL"))));
                return BitConverter.ToInt64(h, 0); // Return 64-bit long
            }
        }

        private static object? Stuff(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 4) return args.FirstOrDefault();
            string s = args[0]?.ToString() ?? "";
            int start = Convert.ToInt32(args[1]);
            int length = Convert.ToInt32(args[2]);
            string newS = args[3]?.ToString() ?? "";
            
            if (start < 1) start = 1;
            if (start > s.Length + 1) return s + newS;
            
            var sb = new System.Text.StringBuilder(s);
            if (start <= s.Length)
            {
                int removeLen = Math.Min(length, s.Length - start + 1);
                sb.Remove(start - 1, removeLen);
            }
            sb.Insert(start - 1, newS);
            return sb.ToString();
        }

        private static object? StringEscape(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return args.FirstOrDefault();
            string s = args[0]?.ToString() ?? "";
            string type = args[1]?.ToString()?.ToLowerInvariant() ?? "";
            
            if (type == "json") return System.Text.Json.JsonSerializer.Serialize(s).Trim('"');
            return s;
        }

        private static object? StringSplit(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return new List<object?>();
            string s = args[0]?.ToString() ?? "";
            string sep = args[1]?.ToString() ?? "";
            return s.Split(new[] { sep }, StringSplitOptions.None).Cast<object?>().ToList();
        }

        private static object? Format(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return args.FirstOrDefault();
            object? val = args[0];
            string fmt = args[1]?.ToString() ?? "";
            
            if (val is IFormattable formattable) return formattable.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
            return val?.ToString();
        }

        private static object? PatIndex(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return 0m;
            string pat = args[0]?.ToString() ?? "";
            string s = args[1]?.ToString() ?? "";
            
            // SQL PATINDEX uses % for wildcards. Convert to Regex.
            string regexPat = "^" + System.Text.RegularExpressions.Regex.Escape(pat).Replace("%", ".*").Replace("_", ".") + "$";
            var match = System.Text.RegularExpressions.Regex.Match(s, regexPat, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? (decimal)(match.Index + 1) : 0m;
        }

        private static object? Str(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 1) return null;
            double val = Convert.ToDouble(args[0]);
            int length = args.Count >= 2 ? Convert.ToInt32(args[1]) : 10;
            int decimals = args.Count >= 3 ? Convert.ToInt32(args[2]) : 0;
            
            string fmt = "F" + decimals;
            string s = val.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
            return s.Length > length ? new string('*', length) : s.PadLeft(length);
        }

        private static object? QuoteName(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 1) return null;
            string s = args[0]?.ToString() ?? "";
            char quote = args.Count >= 2 ? (args[1]?.ToString()?.FirstOrDefault() ?? '[') : '[';
            
            return quote switch {
                '[' => "[" + s.Replace("]", "]]") + "]",
                '\'' => "'" + s.Replace("'", "''") + "'",
                '"' => "\"" + s.Replace("\"", "\"\"") + "\"",
                _ => "[" + s.Replace("]", "]]") + "]"
            };
        }

        private static object? Translate(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 3) return args.FirstOrDefault();
            string s = args[0]?.ToString() ?? "";
            string from = args[1]?.ToString() ?? "";
            string to = args[2]?.ToString() ?? "";
            
            var map = new Dictionary<char, char>();
            for (int i = 0; i < Math.Min(from.Length, to.Length); i++) map[from[i]] = to[i];
            
            var sb = new System.Text.StringBuilder();
            foreach (char c in s) sb.Append(map.TryGetValue(c, out var r) ? r : c);
            return sb.ToString();
        }

        private static object? DataLength(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 1 || args[0] == null) return null;
            if (args[0] is byte[] b) return (decimal)b.Length;
            if (args[0] is string s) return (decimal)(s.Length * 2); // Assume UTF-16
            return (decimal)System.Runtime.InteropServices.Marshal.SizeOf(args[0]!);
        }

        private static object? TryCast(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return null;
            try {
                return EvaluationUtils.CastToType(args[0], args[1]?.ToString() ?? "STRING");
            } catch {
                return null;
            }
        }
    }
}
