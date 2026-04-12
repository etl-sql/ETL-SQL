using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Linting.Rules
{
    /// <summary>
    /// Warns when a CREATE DATASET REFRESH EVERY interval string is not a recognisable
    /// duration (e.g. '1h', '30m', '1d', '15s').
    /// </summary>
    public class DatasetRefreshIntervalRule : ILintRule
    {
        public string Name        => "DatasetRefreshInterval";
        public string Description => "Warns when a DATASET REFRESH EVERY interval is not a valid duration string.";

        // Accepted format: integer followed by s/m/h/d (seconds/minutes/hours/days)
        private static readonly Regex _valid = new(@"^\d+[smhd]$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();

            foreach (var stmt in script.Statements)
            {
                if (stmt is not CreateDatasetStatement ds) continue;
                if (string.IsNullOrWhiteSpace(ds.RefreshInterval)) continue;

                if (!_valid.IsMatch(ds.RefreshInterval.Trim()))
                {
                    results.Add(new LintResult
                    {
                        RuleName     = Name,
                        Severity     = LintSeverity.Warning,
                        Message      = $"Dataset '{ds.TempTableName}': REFRESH EVERY interval '{ds.RefreshInterval}' is not a recognised duration. Use a number followed by s, m, h, or d (e.g. '30m', '1h', '7d').",
                        LineNumber   = ds.Line,
                        ColumnNumber = ds.Column
                    });
                }
            }

            return Task.FromResult<IEnumerable<LintResult>>(results);
        }
    }
}
