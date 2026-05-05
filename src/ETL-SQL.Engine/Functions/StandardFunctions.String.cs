using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Functions
{
    public static partial class StandardFunctions
    {
        private static void RegisterStringFunctions(IFunctionRegistry registry)
        {
            registry.RegisterWithHelp("UPPER", (args, ctx) => args[0]?.ToString()?.ToUpper(), "UPPER(str): Returns the string in all-caps.");
            registry.RegisterWithHelp("LOWER", (args, ctx) => args[0]?.ToString()?.ToLower(), "LOWER(str): Returns the string in all-lowercase.");
            registry.RegisterWithHelp("LEN", Len, "LEN(string): Returns the character count of the string. Returns NULL if input is NULL.");
            registry.RegisterWithHelp("LENGTH", Len, "LENGTH(string|list): Returns the character count of a string or the number of items in a list. Returns NULL if input is NULL.");
            registry.RegisterWithHelp("TRIM", (args, ctx) => args[0] == null ? null : args[0]!.ToString()?.Trim(), "TRIM(str): Removes leading and trailing whitespaces.");
            registry.RegisterWithHelp("LTRIM", (args, ctx) => args[0] == null ? null : args[0]!.ToString()?.TrimStart(), "LTRIM(str): Removes leading whitespaces.");
            registry.RegisterWithHelp("RTRIM", (args, ctx) => args[0] == null ? null : args[0]!.ToString()?.TrimEnd(), "RTRIM(str): Removes trailing whitespaces.");
            registry.RegisterWithHelp("REVERSE", (args, ctx) => args[0] == null ? null : new string((args[0]!.ToString() ?? "").Reverse().ToArray()), "REVERSE(str): Reverses the characters in the string.");
            
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
            registry.RegisterWithHelp("REPLACE", (args, ctx) => args.Count >= 3 ? args[0]?.ToString()?.Replace(args[1]?.ToString() ?? "", args[2]?.ToString() ?? "") : args[0], "REPLACE(str, old, new): Replaces occurrences of a substring.");
            registry.RegisterWithHelp("INITCAP", (args, ctx) => args[0]?.ToString() == null ? null : System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(args[0]!.ToString()!.ToLower()), "INITCAP(str): Capitalizes the first letter of each word.");
            
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

            registry.RegisterWithHelp("CONCAT_WS", ConcatWs, "CONCAT_WS(sep, str1, str2, ...): Concatenates strings with a separator.");
            registry.RegisterWithHelp("SPLIT_PART", SplitPart, "SPLIT_PART(str, sep, part): Returns the nth part of a string after splitting by a separator.");
            registry.RegisterWithHelp("SPACE", (args, ctx) => args[0] == null ? null : new string(' ', Math.Max(0, Convert.ToInt32(args[0]))), "SPACE(n): Returns a string of n spaces.");
            registry.RegisterWithHelp("REGEXP_LIKE", RegexpLike, "REGEXP_LIKE(str, pattern): Returns 1 if the string matches the pattern, 0 otherwise.");
            registry.RegisterWithHelp("REGEXP_SUBSTR", RegexpSubstr, "REGEXP_SUBSTR(str, pattern): Returns the substring that matches the pattern.");
            registry.RegisterWithHelp("REGEXP_REPLACE", RegexpReplace, "REGEXP_REPLACE(str, pat, repl): Replaces matches of a pattern with a replacement string.");
            registry.RegisterWithHelp("REGEXP_INSTR", RegexpInstr, "REGEXP_INSTR(str, pattern): Returns the 1-based position of the first pattern match.");
            registry.RegisterWithHelp("REGEXP_COUNT", RegexpCount, "REGEXP_COUNT(str, pattern): Returns the number of times a pattern occurs in the string.");
        }

        private static object? Len(List<object?> args, IExecutionContext ctx)
        {
            if (args[0].IsNull()) return null;
            return args[0] is System.Collections.ICollection coll ? (decimal)coll.Count : (decimal)(args[0]!.ToString()?.Length ?? 0);
        }

        private static object? Substring(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return args.FirstOrDefault();
            string? s = args[0]?.ToString();
            if (s == null) return null;
            int start = Convert.ToInt32(args[1]);
            int? len = args.Count >= 3 ? Convert.ToInt32(args[2]) : null;

            if (len != null && len <= 0) return "";

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

        private static object? Left(List<object?> args, IExecutionContext ctx)
        {
            string? s = args[0]?.ToString();
            if (s == null) return null;
            int len = Convert.ToInt32(args[1]);
            return len <= 0 ? "" : (len >= s.Length ? s : s.Substring(0, len));
        }

        private static object? Right(List<object?> args, IExecutionContext ctx)
        {
            string? s = args[0]?.ToString();
            if (s == null) return null;
            int len = Convert.ToInt32(args[1]);
            return len <= 0 ? "" : (len >= s.Length ? s : s.Substring(s.Length - len));
        }

        private static object? CharIndex(List<object?> args, IExecutionContext ctx)
        {
            return args.Count >= 2 && args[1]?.ToString() != null ? (decimal)(args[1]!.ToString()!.IndexOf(args[0]?.ToString() ?? "", StringComparison.OrdinalIgnoreCase) + 1) : 0m;
        }

        private static object? InStr(List<object?> args, IExecutionContext ctx)
        {
            return args.Count >= 2 && args[0]?.ToString() != null ? (decimal)(args[0]!.ToString()!.IndexOf(args[1]?.ToString() ?? "", StringComparison.OrdinalIgnoreCase) + 1) : 0m;
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

        private static async Task<object?> StringSplit(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2 || args[0] == null) return new DataTable();
            string s = args[0]!.ToString()!;
            string sep = args[1]?.ToString() ?? ",";
            
            var dt = new DataTable();
            dt.SetColumns(new[] { "Value" });
            
            var parts = s.Split(new[] { sep }, StringSplitOptions.None);
            foreach (var part in parts)
            {
                await dt.AddRowAsync(new Row { ["Value"] = part.Trim() });
            }
            
            return dt;
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

        private static object? ConcatWs(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return null;
            string sep = args[0]?.ToString() ?? "";
            var values = args.Skip(1).Where(a => !a.IsNull()).Select(a => a?.ToString() ?? "");
            return string.Join(sep, values);
        }

        private static object? SplitPart(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 3 || args[0] == null) return null;
            string s = args[0]!.ToString()!;
            string sep = args[1]?.ToString() ?? "";
            int part = Convert.ToInt32(args[2]);
            if (part <= 0) return null;
            var parts = s.Split(new[] { sep }, StringSplitOptions.None);
            return part <= parts.Length ? parts[part - 1] : "";
        }

        private static object? RegexpLike(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2 || args[0] == null || args[1] == null) return 0m;
            return System.Text.RegularExpressions.Regex.IsMatch(args[0]!.ToString()!, args[1]!.ToString()!, System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? 1m : 0m;
        }

        private static object? RegexpSubstr(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2 || args[0] == null || args[1] == null) return null;
            var match = System.Text.RegularExpressions.Regex.Match(args[0]!.ToString()!, args[1]!.ToString()!, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Value : null;
        }

        private static object? RegexpReplace(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 3 || args[0] == null || args[1] == null) return args.FirstOrDefault();
            return System.Text.RegularExpressions.Regex.Replace(args[0]!.ToString()!, args[1]!.ToString()!, args[2]?.ToString() ?? "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private static object? RegexpInstr(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2 || args[0] == null || args[1] == null) return 0m;
            var match = System.Text.RegularExpressions.Regex.Match(args[0]!.ToString()!, args[1]!.ToString()!, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? (decimal)(match.Index + 1) : 0m;
        }

        private static object? RegexpCount(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2 || args[0] == null || args[1] == null) return 0m;
            return (decimal)System.Text.RegularExpressions.Regex.Matches(args[0]!.ToString()!, args[1]!.ToString()!, System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;
        }
    }
}
