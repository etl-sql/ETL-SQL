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
            if (args.Count < 2 || args[0] == null || args[1] == null) return null;
            string input = args[0]?.ToString() ?? "";
            string pattern = args[1]?.ToString() ?? "";
            string? flags = args.Count >= 3 ? args[2]?.ToString() : null;

            try
            {
                var timeout = TimeSpan.FromMilliseconds(ctx.RegexMatchTimeoutMs);
                return Regex.IsMatch(input, pattern, GetOptions(flags), timeout);
            }
            catch (RegexMatchTimeoutException)
            {
                ctx.Logger.Warning("Regex timeout exceeded in REGEXP_LIKE.");
                return null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Returns the matched substring.</summary>
        private static object? RegexpSubstr(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2 || args[0] == null || args[1] == null) return null;
            string input = args[0]?.ToString() ?? "";
            string pattern = args[1]?.ToString() ?? "";
            
            if (args.Count >= 3 && args[2] == null) return null;
            if (args.Count >= 4 && args[3] == null) return null;

            int pos = 1;
            if (args.Count >= 3 && !int.TryParse(args[2]?.ToString(), out pos)) pos = 1;
            int occ = 1;
            if (args.Count >= 4 && !int.TryParse(args[3]?.ToString(), out occ)) occ = 1;
            string? flags = args.Count >= 5 ? args[4]?.ToString() : null;

            if (pos < 1) pos = 1;
            if (occ < 1) occ = 1;
            if (pos > input.Length) return null;

            try
            {
                var timeout = TimeSpan.FromMilliseconds(ctx.RegexMatchTimeoutMs);
                var matches = Regex.Matches(input.Substring(pos - 1), pattern, GetOptions(flags), timeout);
                if (matches.Count >= occ)
                {
                    return matches[occ - 1].Value;
                }
                return null;
            }
            catch (RegexMatchTimeoutException)
            {
                ctx.Logger.Warning("Regex timeout exceeded in REGEXP_SUBSTR.");
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
            if (args.Count < 3 || args[0] == null || args[1] == null || args[2] == null) return null;
            string input = args[0]?.ToString() ?? "";
            string pattern = args[1]?.ToString() ?? "";
            string replacement = args[2]?.ToString() ?? "";
            
            if (args.Count >= 4 && args[3] == null) return null;
            if (args.Count >= 5 && args[4] == null) return null;

            int pos = 1;
            if (args.Count >= 4 && !int.TryParse(args[3]?.ToString(), out pos)) pos = 1;
            int occ = 0; // 0 means all
            if (args.Count >= 5 && !int.TryParse(args[4]?.ToString(), out occ)) occ = 0;
            string? flags = args.Count >= 6 ? args[5]?.ToString() : null;

            if (pos < 1) pos = 1;
            if (pos > input.Length + 1) return input;

            try
            {
                var timeout = TimeSpan.FromMilliseconds(ctx.RegexMatchTimeoutMs);
                var regex = new Regex(pattern, GetOptions(flags), timeout);
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
            catch (RegexMatchTimeoutException)
            {
                ctx.Logger.Warning("Regex timeout exceeded in REGEXP_REPLACE.");
                return input;
            }
            catch
            {
                return input;
            }
        }

        /// <summary>Returns the 1-based start position of the match.</summary>
        private static object? RegexpInstr(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2 || args[0] == null || args[1] == null) return null;
            string input = args[0]?.ToString() ?? "";
            string pattern = args[1]?.ToString() ?? "";
            
            if (args.Count >= 3 && args[2] == null) return null;
            if (args.Count >= 4 && args[3] == null) return null;

            int pos = 1;
            if (args.Count >= 3 && !int.TryParse(args[2]?.ToString(), out pos)) pos = 1;
            int occ = 1;
            if (args.Count >= 4 && !int.TryParse(args[3]?.ToString(), out occ)) occ = 1;
            string? flags = args.Count >= 5 ? args[4]?.ToString() : null;

            if (pos < 1) pos = 1;
            if (occ < 1) occ = 1;
            if (pos > input.Length) return 0m;

            try
            {
                var timeout = TimeSpan.FromMilliseconds(ctx.RegexMatchTimeoutMs);
                var matches = Regex.Matches(input.Substring(pos - 1), pattern, GetOptions(flags), timeout);
                if (matches.Count >= occ)
                {
                    return (decimal)(matches[occ - 1].Index + pos);
                }
                return 0m;
            }
            catch (RegexMatchTimeoutException)
            {
                ctx.Logger.Warning("Regex timeout exceeded in REGEXP_INSTR.");
                return null;
            }
            catch
            {
                return 0m;
            }
        }

        /// <summary>Returns the number of matches.</summary>
        private static object? RegexpCount(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2 || args[0] == null || args[1] == null) return null;
            string input = args[0]?.ToString() ?? "";
            string pattern = args[1]?.ToString() ?? "";
            
            if (args.Count >= 3 && args[2] == null) return null;

            int pos = 1;
            if (args.Count >= 3 && !int.TryParse(args[2]?.ToString(), out pos)) pos = 1;
            string? flags = args.Count >= 4 ? args[3]?.ToString() : null;

            if (pos < 1) pos = 1;
            if (pos > input.Length) return 0m;

            try
            {
                var timeout = TimeSpan.FromMilliseconds(ctx.RegexMatchTimeoutMs);
                return (decimal)Regex.Matches(input.Substring(pos - 1), pattern, GetOptions(flags), timeout).Count;
            }
            catch (RegexMatchTimeoutException)
            {
                ctx.Logger.Warning("Regex timeout exceeded in REGEXP_COUNT.");
                return null;
            }
            catch
            {
                return 0m;
            }
        }

        /// <summary>Returns a list of all matched substrings.</summary>
        private static object? RegexpMatches(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2 || args[0] == null || args[1] == null) return null;
            string input = args[0]?.ToString() ?? "";
            string pattern = args[1]?.ToString() ?? "";
            string? flags = args.Count >= 3 ? args[2]?.ToString() : null;

            try
            {
                var timeout = TimeSpan.FromMilliseconds(ctx.RegexMatchTimeoutMs);
                return Regex.Matches(input, pattern, GetOptions(flags), timeout)
                            .Cast<Match>()
                            .Select(m => (object?)m.Value)
                            .ToList();
            }
            catch (RegexMatchTimeoutException)
            {
                ctx.Logger.Warning("Regex timeout exceeded in REGEXP_MATCHES.");
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Splits the input into a table based on the pattern.</summary>
        private static async Task<object?> RegexpSplitToTable(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2 || args[0] == null || args[1] == null) return null;
            string input = args[0]?.ToString() ?? "";
            string pattern = args[1]?.ToString() ?? "";

            try
            {
                var timeout = TimeSpan.FromMilliseconds(ctx.RegexMatchTimeoutMs);
                var parts = Regex.Split(input, pattern, GetOptions(null), timeout);
                var dt = new DataTable();
                dt.SetColumns(new[] { "VALUE" });
                foreach (var part in parts)
                {
                    await dt.AddRowAsync(new Row { ["VALUE"] = part });
                }
                return dt;
            }
            catch (RegexMatchTimeoutException)
            {
                ctx.Logger.Warning("Regex timeout exceeded in REGEXP_SPLIT_TO_TABLE.");
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
