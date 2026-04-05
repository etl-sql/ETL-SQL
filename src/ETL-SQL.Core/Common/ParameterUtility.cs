using System;
using System.Text.RegularExpressions;

namespace ETL_SQL.Core.Common
{
    /// <summary>
    /// Utility for processing SQL parameters, supporting standard '?' and indexed '?n' placeholders.
    /// </summary>
    public static class ParameterUtility
    {
        private static readonly Regex ParameterRegex = new Regex(@"\?(?<index>[0-9]+)?", RegexOptions.Compiled);

        /// <summary>
        /// Processes the SQL text, replacing '?' and '?n' (1-indexed) with numbered parameter tokens (e.g., '@p0').
        /// </summary>
        /// <param name="sqlText">The raw SQL text to process.</param>
        /// <param name="parameterPrefix">The prefix to use for generated parameters (default is '@').</param>
        /// <returns>The processed SQL with normalized parameter tokens.</returns>
        public static string ProcessParameters(string sqlText, string parameterPrefix = "@")
        {
            if (string.IsNullOrWhiteSpace(sqlText)) return sqlText;

            int sequentialIndex = 0;
            return ParameterRegex.Replace(sqlText, match =>
            {
                if (match.Groups["index"].Success)
                {
                    // Indexed parameter ?n (1-indexed)
                    int index = int.Parse(match.Groups["index"].Value) - 1;
                    if (index < 0) throw new ArgumentException("Parameter index must be greater than 0.");
                    return $"{parameterPrefix}p{index}";
                }
                else
                {
                    // Sequential parameter ?
                    return $"{parameterPrefix}p{sequentialIndex++}";
                }
            });
        }
    }
}
