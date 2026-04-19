using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Engine.Functions;

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
            registry.RegisterWithHelp("UPPER", (args, ctx) => args[0]?.ToString()?.ToUpper(), "UPPER(str): Returns the string in all-caps.");
            registry.RegisterWithHelp("LOWER", (args, ctx) => args[0]?.ToString()?.ToLower(), "LOWER(str): Returns the string in all-lowercase.");
            registry.RegisterWithHelp("LEN", Len, "LEN(string): Returns the character count of the string. Returns NULL if input is NULL.");
            registry.RegisterWithHelp("LENGTH", Len, "LENGTH(string|list): Returns the character count of a string or the number of items in a list. Returns NULL if input is NULL.");
            registry.RegisterWithHelp("APPEND_TO_LIST", AddToList, "APPEND_TO_LIST(@list, value): Adds an item to a list variable. Returns the new list.");
            registry.RegisterWithHelp("ADD_TO_LIST", AddToList, "ADD_TO_LIST(@list, value): Alias for APPEND_TO_LIST.");
            registry.RegisterWithHelp("REMOVE_FROM_LIST", RemoveFromList, "REMOVE_FROM_LIST(@list, value): Removes all occurrences of a value from a list variable.");
            registry.RegisterWithHelp("SORT_LIST", SortList, "SORT_LIST(list[, 'ASC'|'DESC']): Returns a sorted version of the list.");
            registry.RegisterWithHelp("TRIM", (args, ctx) => args[0] == null ? null : args[0].ToString()?.Trim(), "TRIM(str): Removes leading and trailing whitespaces.");
            registry.RegisterWithHelp("LTRIM", (args, ctx) => args[0] == null ? null : args[0].ToString()?.TrimStart(), "LTRIM(str): Removes leading whitespaces.");
            registry.RegisterWithHelp("RTRIM", (args, ctx) => args[0] == null ? null : args[0].ToString()?.TrimEnd(), "RTRIM(str): Removes trailing whitespaces.");
            registry.RegisterWithHelp("REVERSE", (args, ctx) => args[0] == null ? null : new string((args[0].ToString() ?? "").Reverse().ToArray()), "REVERSE(str): Reverses the characters in the string.");
            registry.RegisterWithHelp("ABS", (args, ctx) => {
                if (args[0].IsNull()) return null;
                if (!decimal.TryParse(args[0]?.ToString(), out var n)) return null;
                return Math.Abs(n);
            }, "ABS(n): Returns the absolute value of a number. Returns NULL on non-numeric input.");
            registry.RegisterWithHelp("ROUND", Round, "ROUND(numeric, decimals): Rounds a numeric value to a specified number of decimal places.");
            registry.RegisterWithHelp("CEILING", (args, ctx) => args[0] == null ? null : (decimal.TryParse(args[0]?.ToString(), out var n) ? Math.Ceiling(n) : null), "CEILING(n): Returns the smallest integer greater than or equal to the number.");
            registry.RegisterWithHelp("FLOOR", (args, ctx) => args[0] == null ? null : (decimal.TryParse(args[0]?.ToString(), out var n) ? Math.Floor(n) : null), "FLOOR(n): Returns the largest integer less than or equal to the number.");
            registry.RegisterWithHelp("SQRT", (args, ctx) => {
                if (args[0] == null) return null;
                if (!double.TryParse(args[0]?.ToString(), out var d)) return null;
                if (d < 0) return null; // Defensive return NULL for negative SQRT
                return (decimal)Math.Sqrt(d);
            }, "SQRT(n): Returns the square root of a number. Returns NULL for negative inputs.");
            registry.RegisterWithHelp("CONCAT", (args, ctx) => {
                long totalLength = args.Sum(a => (long)(a?.ToString()?.Length ?? 0));
                ctx.SecurityService.ValidateStringSize(totalLength, ctx.MaxStringResultSize, ctx.AllowLargeStringResults, ctx.CurrentScriptPath);
                return string.Join("", args.Select(a => a?.ToString() ?? ""));
            }, "CONCAT(str1, str2, ...): Concatenates multiple strings into one.");
            registry.RegisterWithHelp("SUBSTRING", Substring, "SUBSTRING(str, start, length): Extracts a substring using 1-based indexing.");
            registry.RegisterWithHelp("SUBSTR", Substring, "SUBSTR(str, start[, length]): Extracts a substring (Oracle-style).");
            registry.RegisterWithHelp("LEFT", Left, "LEFT(str, n): Extracts n characters from the left side of the string.");
            registry.RegisterWithHelp("RIGHT", Right, "RIGHT(str, n): Extracts n characters from the right side of the string.");
            registry.RegisterWithHelp("CHARINDEX", CharIndex, "CHARINDEX(sub, str): Returns the 1-based index of a substring within a string.");
            registry.RegisterWithHelp("INSTR", InStr, "INSTR(str, sub): Returns the 1-based index of a substring within a string.");
            registry.RegisterWithHelp("POWER", (args, ctx) => {
                if (args.Count < 2 || args[0] == null || args[1] == null) return null;
                if (!double.TryParse(args[0]?.ToString(), out var b)) return null;
                if (!double.TryParse(args[1]?.ToString(), out var p)) return null;
                if (b == 0 && p < 0) return null; // Defensive return NULL for divide-by-zero
                return (decimal)Math.Pow(b, p);
            }, "POWER(base, exp): Returns the result of a base raised to an exponent.");
            registry.RegisterWithHelp("DATENAME", DateName, "DATENAME(datepart, date): Returns a string representing the specified date part (e.g. 'January').");
            registry.RegisterWithHelp("DATEPART", DatePart, "DATEPART(datepart, date): Returns an integer representing the specified date part.");
            registry.RegisterWithHelp("DATEDIFF", DateDiff, "DATEDIFF(datepart, start, end): Returns the count of specified datepart boundaries crossed between two dates.");
            registry.RegisterWithHelp("ISDATE", (args, ctx) => EvaluationUtils.SafeTryParseDate(args[0]?.ToString() ?? "", out _) ? 1 : 0, "ISDATE(expr): Returns 1 if the expression is a valid date, 0 otherwise.");
            registry.RegisterWithHelp("EOMONTH", EoMonth, "EOMONTH(date[, months_to_add]): Returns the last day of the month containing the date.");
            registry.RegisterWithHelp("REPLACE", (args, ctx) => args.Count >= 3 ? args[0]?.ToString()?.Replace(args[1]?.ToString() ?? "", args[2]?.ToString() ?? "") : args[0], "REPLACE(str, old, new): Replaces occurrences of a substring.");
            registry.RegisterWithHelp("INITCAP", (args, ctx) => args[0]?.ToString() == null ? null : System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(args[0]!.ToString()!.ToLower()), "INITCAP(str): Capitalizes the first letter of each word.");
            registry.RegisterWithHelp("MOD", (args, ctx) => args.Count >= 2 && args[0] != null && args[1] != null ? (decimal.TryParse(args[0]?.ToString(), out var n1) && decimal.TryParse(args[1]?.ToString(), out var n2) && n2 != 0 ? n1 % n2 : null) : null, "MOD(n, d): Returns the remainder of a division.");
            registry.RegisterWithHelp("YEAR", (args, ctx) => args[0] == null ? null : (EvaluationUtils.SafeTryParseDate(args[0]!.ToString()!, out var dt) ? (decimal)dt.Year : null), "YEAR(date): Returns the year part of a date.");
            registry.RegisterWithHelp("MONTH", (args, ctx) => args[0] == null ? null : (EvaluationUtils.SafeTryParseDate(args[0]!.ToString()!, out var dt) ? (decimal)dt.Month : null), "MONTH(date): Returns the month part of a date.");
            registry.RegisterWithHelp("DAY", (args, ctx) => args[0] == null ? null : (EvaluationUtils.SafeTryParseDate(args[0]!.ToString()!, out var dt) ? (decimal)dt.Day : null), "DAY(date): Returns the day part of a date.");
            registry.RegisterWithHelp("COALESCE", (args, ctx) => args.FirstOrDefault(a => !a.IsNull()), "COALESCE(v1, v2, ...): Returns the first non-null value.");
            registry.RegisterWithHelp("ISNULL", IsNull, "ISNULL(v1, v2): Returns v2 if v1 is null.");
            registry.RegisterWithHelp("NVL", IsNull, "NVL(v1, v2): Alias for ISNULL.");
            registry.RegisterWithHelp("NULLIF", (args, ctx) => EvaluationUtils.IsSoftEqual(args.ElementAtOrDefault(0), args.ElementAtOrDefault(1)) ? null : args.ElementAtOrDefault(0), "NULLIF(v1, v2): Returns NULL if v1 equals v2, else v1.");
            registry.RegisterWithHelp("GETDATE", (args, ctx) => DateTime.Now, "GETDATE(): Returns the current system date and time.");
            registry.RegisterWithHelp("NOW", (args, ctx) => DateTime.Now, "NOW(): Alias for GETDATE.");
            registry.RegisterWithHelp("CAST", (args, ctx) => args.Count >= 2 ? EvaluationUtils.CastToType(args[0], args[1]?.ToString() ?? "STRING") : args[0], "CAST(expr AS type): Converts an expression to a target data type.");
            registry.RegisterWithHelp("COUNT", Count, "COUNT(col): Returns the number of items in a collection.");
            registry.RegisterWithHelp("IS_NULL", (args, ctx) => args[0].IsNull(), "IS_NULL(expr): Returns TRUE if the expression is null.");
            registry.RegisterWithHelp("IS_NOT_NULL", (args, ctx) => !args[0].IsNull(), "IS_NOT_NULL(expr): Returns TRUE if the expression is NOT null.");
            registry.RegisterWithHelp("IIF", (args, ctx) => args.Count >= 3 ? (Convert.ToBoolean(args[0]) ? args[1] : args[2]) : args.FirstOrDefault(), "IIF(cond, true_val, false_val): Returns one of two values depending on a condition.");
            registry.RegisterWithHelp("IFNULL", IsNull, "IFNULL(v1, v2): Alias for ISNULL.");
            registry.RegisterWithHelp("GREATEST", (args, ctx) => args.Where(a => !a.IsNull()).OrderByDescending(a => a).FirstOrDefault(), "GREATEST(v1, v2, ...): Returns the largest value in the list.");
            registry.RegisterWithHelp("LEAST", (args, ctx) => args.Where(a => !a.IsNull()).OrderBy(a => a).FirstOrDefault(), "LEAST(v1, v2, ...): Returns the smallest value in the list.");
            registry.RegisterWithHelp("GENERATE_SERIES", GenerateSeries, "GENERATE_SERIES(start, stop[, step]): Generates a series of numbers.");
            registry.RegisterWithHelp("FILE_EXISTS", (args, ctx) => args.Count >= 1 && args[0] != null ? System.IO.File.Exists(ctx.ResolvePath(args[0]?.ToString() ?? "")) : false, "FILE_EXISTS(path): Returns TRUE if the file exists.");
            registry.RegisterWithHelp("DIRECTORY_EXISTS", (args, ctx) => args.Count >= 1 && args[0] != null ? System.IO.Directory.Exists(ctx.ResolvePath(args[0]?.ToString() ?? "")) : false, "DIRECTORY_EXISTS(path): Returns TRUE if the directory exists.");
            registry.RegisterWithHelp("DATETIMEFROMPARTS", DateTimeFromParts, "DATETIMEFROMPARTS(y, m, d, h, mi, s, ms): Constructs a DATETIME from parts.");
            registry.RegisterWithHelp("TIMEFROMPARTS", TimeFromParts, "TIMEFROMPARTS(h, mi, s, frac, prec): Constructs a TIME from parts.");
            registry.RegisterWithHelp("DATETIMEOFFSETSFROMPARTS", DateTimeOffsetsFromParts, "DATETIMEOFFSETSFROMPARTS(...): Constructs a DATETIMEOFFSET from parts.");
            registry.RegisterWithHelp("HASHBYTES", HashBytes, "HASHBYTES('algo', val): Returns a cryptographic hash (MD5, SHA1, SHA256, SHA512).");
            registry.RegisterWithHelp("NEWID", (args, ctx) => NewUuidV7(), "NEWID(): Returns a new unique identifier (UUID v7).");
            registry.RegisterWithHelp("NEWSEQUENTIALID", (args, ctx) => NewUuidV7(), "NEWSEQUENTIALID(): Returns a new sequential unique identifier.");
            registry.RegisterWithHelp("CHECKSUM", Checksum, "CHECKSUM(v1, v2, ...): Returns a hash of the input values.");
            registry.RegisterWithHelp("BINARY_CHECKSUM", Checksum, "BINARY_CHECKSUM(v1, v2, ...): Returns a binary-compatible hash.");
            
            // Item 11 - String Extension Suite
            registry.RegisterWithHelp("STUFF", Stuff, "STUFF(str, start, len, new_str): Replaces a portion of a string with another string.");
            registry.RegisterWithHelp("STRING_ESCAPE", StringEscape, "STRING_ESCAPE(text, type): Escapes special characters (e.g. 'json').");
            registry.RegisterWithHelp("STRING_SPLIT", StringSplit, "STRING_SPLIT(str, sep): Splits a string into a list of substrings.");
            registry.RegisterWithHelp("ASCII", (args, ctx) => args[0]?.ToString() == null || args[0]!.ToString()!.Length == 0 ? null : (decimal)args[0]!.ToString()![0], "ASCII(str): Returns the ASCII code of the first character.");
            registry.RegisterWithHelp("CHAR", (args, ctx) => args.Count >= 1 && args[0] != null ? ((char)Convert.ToInt32(args[0])).ToString() : null, "CHAR(n): Converts an ASCII code to a character.");
            registry.RegisterWithHelp("FORMAT", Format, "FORMAT(val, fmt): Formats a value based on a .NET format string.");
            registry.RegisterWithHelp("PATINDEX", PatIndex, "PATINDEX(pat, str): Returns the 1-based start position of a pattern in a string.");
            registry.RegisterWithHelp("STR", Str, "STR(f[, len[, dec]]): Returns character data converted from numeric data.");
            registry.RegisterWithHelp("QUOTENAME", QuoteName, "QUOTENAME(str[, char]): Returns a delimited identifier (default []).");
            registry.RegisterWithHelp("TRANSLATE", Translate, "TRANSLATE(str, from, to): Replaces characters specified in 'from' with 'to'.");
            registry.RegisterWithHelp("UNICODE", (args, ctx) => args[0]?.ToString() == null || args[0]!.ToString()!.Length == 0 ? null : (decimal)args[0]!.ToString()![0], "UNICODE(str): Returns the Unicode point of the first character.");
            registry.RegisterWithHelp("DATALENGTH", DataLength, "DATALENGTH(val): Returns the number of bytes used to represent any expression.");
            registry.RegisterWithHelp("TO_STR", (args, ctx) => args[0]?.ToString(), "TO_STR(val): Converts a value to a string.");
            registry.RegisterWithHelp("REPLICATE", (args, ctx) => {
                if (args.Count < 2 || args[0] == null) return null;
                string s = args[0]!.ToString()!;
                int n = Math.Max(0, Convert.ToInt32(args[1]));
                long totalLength = (long)s.Length * n;
                ctx.SecurityService.ValidateStringSize(totalLength, ctx.MaxStringResultSize, ctx.AllowLargeStringResults, ctx.CurrentScriptPath);
                return string.Concat(Enumerable.Repeat(s, n));
            }, "REPLICATE(str, n): Repeats a string n times.");
            registry.RegisterWithHelp("TRY_CAST", TryCast, "TRY_CAST(expr AS type): Converts to type or returns NULL on failure.");

            // Item 13 - Math Extension Suite
            registry.RegisterWithHelp("SIN", (args, ctx) => args[0] == null ? null : (decimal)Math.Sin(Convert.ToDouble(args[0])), "SIN(f): Sine (input in radians).");
            registry.RegisterWithHelp("COS", (args, ctx) => args[0] == null ? null : (decimal)Math.Cos(Convert.ToDouble(args[0])), "COS(f): Cosine (input in radians).");
            registry.RegisterWithHelp("TAN", (args, ctx) => args[0] == null ? null : (decimal)Math.Tan(Convert.ToDouble(args[0])), "TAN(f): Tangent (input in radians).");
            registry.RegisterWithHelp("ASIN", (args, ctx) => args[0] == null ? null : (decimal)Math.Asin(Convert.ToDouble(args[0])), "ASIN(f): Inverse Sine (returns radians).");
            registry.RegisterWithHelp("ACOS", (args, ctx) => args[0] == null ? null : (decimal)Math.Acos(Convert.ToDouble(args[0])), "ACOS(f): Inverse Cosine (returns radians).");
            registry.RegisterWithHelp("ATAN", (args, ctx) => args[0] == null ? null : (decimal)Math.Atan(Convert.ToDouble(args[0])), "ATAN(f): Inverse Tangent (returns radians).");
            registry.RegisterWithHelp("ATAN2", (args, ctx) => args.Count >= 2 ? (decimal)Math.Atan2(Convert.ToDouble(args[0]), Convert.ToDouble(args[1])) : null, "ATAN2(y, x): Returns the angle in radians between the x-axis and (x, y).");
            registry.RegisterWithHelp("SIGN", (args, ctx) => args[0] == null ? null : (decimal)Math.Sign(Convert.ToDecimal(args[0])), "SIGN(n): Returns the sign of a number (1, -1, or 0).");
            
            // FW-7 & FW-9
            registry.RegisterWithHelp("DATEADD", DateAdd, "DATEADD(datepart, number, date): Adds a value to a date.");
            registry.RegisterWithHelp("SUM", Sum, "SUM(expression): Returns the sum of values in a collection.");
            registry.RegisterWithHelp("AVG", Avg, "AVG(expression): Returns the average of values in a collection.");
            registry.RegisterWithHelp("MIN", Min, "MIN(expression): Returns the minimum value in a collection.");
            registry.RegisterWithHelp("MAX", Max, "MAX(expression): Returns the maximum value in a collection.");
            registry.RegisterWithHelp("STDDEV", StdDev, "STDDEV(expression): Returns the statistical standard deviation.");
            registry.RegisterWithHelp("VAR", Variance, "VAR(expression): Returns the statistical variance.");

            // ENG-5 - Error Functions
            registry.RegisterWithHelp("ERROR_NUMBER", (args, ctx) => (ctx.ActiveException ?? ctx.LastError)?.Number ?? 0, "ERROR_NUMBER(): Returns the error number of the error that caused the CATCH block to run.");
            registry.RegisterWithHelp("ERROR_MESSAGE", (args, ctx) => (ctx.ActiveException ?? ctx.LastError)?.Message, "ERROR_MESSAGE(): Returns the message text of the error that caused the CATCH block to run.");
            registry.RegisterWithHelp("ERROR_SEVERITY", (args, ctx) => (ctx.ActiveException ?? ctx.LastError)?.Severity ?? 0, "ERROR_SEVERITY(): Returns the severity of the error that caused the CATCH block to run.");
            registry.RegisterWithHelp("ERROR_STATE", (args, ctx) => (ctx.ActiveException ?? ctx.LastError)?.State ?? 0, "ERROR_STATE(): Returns the state number of the error that caused the CATCH block to run.");
            registry.RegisterWithHelp("ERROR_LINE", (args, ctx) => (ctx.ActiveException ?? ctx.LastError)?.Line ?? 0, "ERROR_LINE(): Returns the line number where the error occurred.");

            // ENG-6 - Env Var Expansion
            registry.RegisterWithHelp("ENV", (args, ctx) => {
                string? name = args.FirstOrDefault()?.ToString();
                if (string.IsNullOrEmpty(name)) return null;
                ctx.SecurityService.ValidateEnvVar(name);
                return Environment.GetEnvironmentVariable(name);
            }, "ENV('VAR_NAME'): Returns the value of a host environment variable (subject to security allow-list).");
        }

        /// <summary>Calculates the length of a string or collection.</summary>
        private static object? Len(List<object?> args, IExecutionContext ctx)
        {
            if (args[0].IsNull()) return null;
            return args[0] is System.Collections.ICollection coll ? (decimal)coll.Count : (decimal)(args[0].ToString()?.Length ?? 0);
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

        /// <summary>Rounds a numeric value to a specified number decimal places.</summary>
        private static object? Round(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 1 || args[0] == null) return null;
            if (!decimal.TryParse(args[0]?.ToString(), out var n)) return null;
            int decimals = args.Count >= 2 && int.TryParse(args[1]?.ToString(), out var d) ? d : 0;
            return Math.Round(n, decimals);
        }

        /// <summary>Extracts a substring from a given string based on a 1-based start index and length.</summary>
        private static object? Substring(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return args.FirstOrDefault();
            string? s = args[0]?.ToString();
            if (s == null) return null;
            int start = Convert.ToInt32(args[1]);
            int? len = args.Count >= 3 ? Convert.ToInt32(args[2]) : null;

            if (len != null && len <= 0) return "";

            ctx.Logger.Error($"[SUBSTRING] start={start}, len={len}");
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                int pos = i + 1;
                if (pos >= start && (len == null || pos < start + len))
                {
                    sb.Append(s[i]);
                }
            }
            return sb.ToString();
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
            if (args.Count < 2 || args[1] == null) return null;
            string part = args[0]?.ToString()?.ToUpperInvariant() ?? "";
            if (!DateTime.TryParse(args[1]?.ToString(), out var dt)) throw new ExecutionException($"Invalid date format for DATENAME: {args[1]}");
            return part switch {
                "MONTH" or "MM" or "M" => dt.ToString("MMMM"),
                "WEEKDAY" or "DW" or "W" => dt.ToString("dddd"),
                "YEAR" or "YY" or "YYYY" => dt.Year.ToString(),
                "QUARTER" or "QQ" or "Q" => "Q" + ((dt.Month - 1) / 3 + 1),
                _ => dt.ToString()
            };
        }

        /// <summary>Returns an integer representing a specific date part (e.g., year, hour).</summary>
        private static object? DatePart(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2 || args[1] == null) return null;
            string part = args[0]?.ToString()?.ToUpperInvariant() ?? "";
            if (!DateTime.TryParse(args[1]?.ToString(), out var dt)) throw new ExecutionException($"Invalid date format for DATEPART: {args[1]}");
            return part switch {
                "YEAR" or "YY" or "YYYY" => (decimal)dt.Year,
                "QUARTER" or "QQ" or "Q" => (decimal)((dt.Month - 1) / 3 + 1),
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
            if (args.Count < 3 || args[1] == null || args[2] == null) return null;
            string part = args[0]?.ToString()?.ToUpperInvariant() ?? "";
            if (!EvaluationUtils.SafeTryParseDate(args[1]?.ToString() ?? "", out var dt1)) return null;
            if (!EvaluationUtils.SafeTryParseDate(args[2]?.ToString() ?? "", out var dt2)) return null;
            
            var diff = dt2 - dt1;
            return part switch {
                "YEAR" or "YY" or "YYYY" => (decimal)(dt2.Year - dt1.Year),
                "QUARTER" or "QQ" or "Q" => (decimal)((dt2.Year - dt1.Year) * 4 + ((dt2.Month - 1) / 3) - ((dt1.Month - 1) / 3)),
                "MONTH" or "MM" or "M" => (decimal)((dt2.Year - dt1.Year) * 12 + dt2.Month - dt1.Month),
                "WEEK" or "WK" or "WW" => (decimal)Math.Truncate((dt2.Date - dt1.Date).TotalDays / 7),
                "DAY" or "DD" or "D" => (decimal)(dt2.Date - dt1.Date).TotalDays,
                "HOUR" or "HH" => (decimal)Math.Truncate(diff.TotalHours),
                "MINUTE" or "MI" or "N" => (decimal)Math.Truncate(diff.TotalMinutes),
                "SECOND" or "SS" or "S" => (decimal)Math.Truncate(diff.TotalSeconds),
                "MILLISECOND" or "MS" => (decimal)Math.Truncate(diff.TotalMilliseconds),
                _ => (decimal)0
            };
        }

        /// <summary>Returns the last day of the month that contains the specified date.</summary>
        private static object? EoMonth(List<object?> args, IExecutionContext ctx)
        {
            if (args[0] == null) return null;
            if (!EvaluationUtils.SafeTryParseDate(args[0]?.ToString() ?? "", out var dt)) return null;
            var monthsToAdd = args.Count >= 2 && int.TryParse(args[1]?.ToString(), out var m) ? m : 0;
            var target = dt.AddMonths(monthsToAdd);
            var firstOfNextMonth = new DateTime(target.Year, target.Month, 1).AddMonths(1);
            return firstOfNextMonth.AddDays(-1);
        }

        /// <summary>Returns the second argument if the first is null (COALESCE/ISNULL style).</summary>
        private static object? IsNull(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return args.FirstOrDefault();
            var first = args[0];
            return (first.IsNull()) ? args[1] : first;
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

        private static object? DateAdd(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 3 || args[2] == null) return null;
            string part = args[0]?.ToString()?.ToUpperInvariant() ?? "";
            if (!double.TryParse(args[1]?.ToString(), out var val)) return null;
            if (!EvaluationUtils.SafeTryParseDate(args[2]?.ToString() ?? "", out var dt)) return null;
            
            return part switch {
                "YEAR" or "YY" or "YYYY" => dt.AddYears((int)val),
                "QUARTER" or "QQ" or "Q" => dt.AddMonths((int)val * 3),
                "MONTH" or "MM" or "M" => dt.AddMonths((int)val),
                "WEEK" or "WK" or "WW" => dt.AddDays(val * 7),
                "DAY" or "DD" or "D" => dt.AddDays(val),
                "HOUR" or "HH" => dt.AddHours(val),
                "MINUTE" or "MI" or "N" => dt.AddMinutes(val),
                "SECOND" or "SS" or "S" => dt.AddSeconds(val),
                "MILLISECOND" or "MS" => dt.AddMilliseconds(val),
                _ => dt
            };
        }

        private static IEnumerable<decimal> GetNumbers(object? arg)
        {
            if (arg is IEnumerable<object?> enumerable)
                return enumerable.Where(x => x != null).Select(x => Convert.ToDecimal(x));
            if (arg != null)
                return new[] { Convert.ToDecimal(arg) };
            return Enumerable.Empty<decimal>();
        }

        private static object? Sum(List<object?> args, IExecutionContext ctx)
        {
            var nums = GetNumbers(args.FirstOrDefault());
            return nums.Any() ? nums.Sum() : (decimal)0;
        }

        private static object? Avg(List<object?> args, IExecutionContext ctx)
        {
            var nums = GetNumbers(args.FirstOrDefault());
            return nums.Any() ? nums.Average() : (decimal)0;
        }

        private static object? Min(List<object?> args, IExecutionContext ctx)
        {
            var nums = GetNumbers(args.FirstOrDefault());
            return nums.Any() ? nums.Min() : null;
        }

        private static object? Max(List<object?> args, IExecutionContext ctx)
        {
            var nums = GetNumbers(args.FirstOrDefault());
            return nums.Any() ? nums.Max() : null;
        }

        private static object? StdDev(List<object?> args, IExecutionContext ctx)
        {
            var nums = GetNumbers(args.FirstOrDefault()).ToList();
            if (nums.Count < 2) return (decimal)0;
            double avg = (double)nums.Average();
            double sum = nums.Sum(d => Math.Pow((double)d - avg, 2));
            return (decimal)Math.Sqrt(sum / (nums.Count - 1));
        }

        private static object? Variance(List<object?> args, IExecutionContext ctx)
        {
            var nums = GetNumbers(args.FirstOrDefault()).ToList();
            if (nums.Count < 2) return (decimal)0;
            double avg = (double)nums.Average();
            double sum = nums.Sum(d => Math.Pow((double)d - avg, 2));
            return (decimal)(sum / (nums.Count - 1));
        }
    }
}
