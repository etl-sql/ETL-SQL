using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Functions
{
    /// <summary>
    /// Provides a suite of regular expression functions for string processing.
    /// </summary>
    public static class RegexFunctions
    {
        /// <summary>Registers all REGEXP functions into the registry.</summary>
        public static void Register(IFunctionRegistry registry)
        {
            registry.RegisterWithHelp("REGEXP_LIKE", RegexpLike, "REGEXP_LIKE(str, pattern[, flags]): Returns 1 if the string matches the pattern.");
            registry.RegisterWithHelp("REGEXP_SUBSTR", RegexpSubstr, "REGEXP_SUBSTR(str, pattern[, pos[, occ[, flags]]]): Extracts a substring matching the pattern.");
            registry.RegisterWithHelp("REGEXP_REPLACE", RegexpReplace, "REGEXP_REPLACE(str, pattern, new_str[, pos[, occ[, flags]]]): Replaces matching substrings.");
            registry.RegisterWithHelp("REGEXP_INSTR", RegexpInstr, "REGEXP_INSTR(str, pat[, pos[, occ[, option[, flags]]]]): Returns the position of a match.");
            registry.RegisterWithHelp("REGEXP_COUNT", RegexpCount, "REGEXP_COUNT(str, pattern[, pos[, flags]]): Returns the number of matches found.");
            registry.RegisterWithHelp("REGEXP_MATCHES", RegexpMatches, "REGEXP_MATCHES(str, pattern): Returns a table of all matches found.");
            registry.RegisterWithHelp("REGEXP_SPLIT_TO_TABLE", RegexpSplitToTable, "REGEXP_SPLIT_TO_TABLE(str, pattern): Splits a string into a table using regex.");
        }

        private static RegexOptions GetOptions(string? flags)
        {
            var options = RegexOptions.None;
            if (string.IsNullOrEmpty(flags)) return RegexOptions.IgnoreCase; // Default to case-insensitive

            if (flags.Contains("i")) options |= RegexOptions.IgnoreCase;
            if (flags.Contains("m")) options |= RegexOptions.Multiline;
            if (flags.Contains("s")) options |= RegexOptions.Singleline;
            if (flags.Contains("n")) options |= RegexOptions.ExplicitCapture;
            if (flags.Contains("x")) options |= RegexOptions.IgnorePatternWhitespace;

            return options;
        }

        /// <summary>Returns true if the input matches the pattern.</summary>
        private static object? RegexpLike(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return false;
            string input = args[0]?.ToString() ?? "";
            string pattern = args[1]?.ToString() ?? "";
            string? flags = args.Count >= 3 ? args[2]?.ToString() : null;

            try
            {
                return Regex.IsMatch(input, pattern, GetOptions(flags));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Returns the matched substring.</summary>
        private static object? RegexpSubstr(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return null;
            string input = args[0]?.ToString() ?? "";
            string pattern = args[1]?.ToString() ?? "";
            int pos = args.Count >= 3 ? Convert.ToInt32(args[2]) : 1;
            int occ = args.Count >= 4 ? Convert.ToInt32(args[3]) : 1;
            string? flags = args.Count >= 5 ? args[4]?.ToString() : null;

            if (pos < 1) pos = 1;
            if (occ < 1) occ = 1;

            try
            {
                var matches = Regex.Matches(input.Substring(pos - 1), pattern, GetOptions(flags));
                if (matches.Count >= occ)
                {
                    return matches[occ - 1].Value;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Returns the string with matches replaced.</summary>
        private static object? RegexpReplace(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 3) return args.FirstOrDefault();
            string input = args[0]?.ToString() ?? "";
            string pattern = args[1]?.ToString() ?? "";
            string replacement = args[2]?.ToString() ?? "";
            int pos = args.Count >= 4 ? Convert.ToInt32(args[3]) : 1;
            int occ = args.Count >= 5 ? Convert.ToInt32(args[4]) : 0; // 0 means all
            string? flags = args.Count >= 6 ? args[5]?.ToString() : null;

            if (pos < 1) pos = 1;

            try
            {
                var regex = new Regex(pattern, GetOptions(flags));
                string prefix = input.Substring(0, pos - 1);
                string target = input.Substring(pos - 1);

                if (occ == 0)
                {
                    return prefix + regex.Replace(target, replacement);
                }
                else
                {
                    int count = 0;
                    return prefix + regex.Replace(target, m =>
                    {
                        count++;
                        return count == occ ? replacement : m.Value;
                    });
                }
            }
            catch
            {
                return input;
            }
        }

        /// <summary>Returns the 1-based start position of the match.</summary>
        private static object? RegexpInstr(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return 0m;
            string input = args[0]?.ToString() ?? "";
            string pattern = args[1]?.ToString() ?? "";
            int pos = args.Count >= 3 ? Convert.ToInt32(args[2]) : 1;
            int occ = args.Count >= 4 ? Convert.ToInt32(args[3]) : 1;
            string? flags = args.Count >= 5 ? args[4]?.ToString() : null;

            if (pos < 1) pos = 1;
            if (occ < 1) occ = 1;

            try
            {
                var matches = Regex.Matches(input.Substring(pos - 1), pattern, GetOptions(flags));
                if (matches.Count >= occ)
                {
                    return (decimal)(matches[occ - 1].Index + pos);
                }
                return 0m;
            }
            catch
            {
                return 0m;
            }
        }

        /// <summary>Returns the number of matches.</summary>
        private static object? RegexpCount(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return 0m;
            string input = args[0]?.ToString() ?? "";
            string pattern = args[1]?.ToString() ?? "";
            int pos = args.Count >= 3 ? Convert.ToInt32(args[2]) : 1;
            string? flags = args.Count >= 4 ? args[3]?.ToString() : null;

            if (pos < 1) pos = 1;

            try
            {
                return (decimal)Regex.Matches(input.Substring(pos - 1), pattern, GetOptions(flags)).Count;
            }
            catch
            {
                return 0m;
            }
        }

        /// <summary>Returns a list of all matched substrings.</summary>
        private static object? RegexpMatches(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return new List<object?>();
            string input = args[0]?.ToString() ?? "";
            string pattern = args[1]?.ToString() ?? "";
            string? flags = args.Count >= 3 ? args[2]?.ToString() : null;

            try
            {
                return Regex.Matches(input, pattern, GetOptions(flags))
                            .Cast<Match>()
                            .Select(m => (object?)m.Value)
                            .ToList();
            }
            catch
            {
                return new List<object?>();
            }
        }

        /// <summary>Splits the input into a table based on the pattern.</summary>
        private static object? RegexpSplitToTable(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return new DataTable();
            string input = args[0]?.ToString() ?? "";
            string pattern = args[1]?.ToString() ?? "";

            try
            {
                var parts = Regex.Split(input, pattern);
                var dt = new DataTable();
                dt.SetColumns(new[] { "VALUE" });
                foreach (var part in parts)
                {
                    dt.AddRow(new Row { ["VALUE"] = part });
                }
                return dt;
            }
            catch
            {
                return new DataTable();
            }
        }
    }
}
