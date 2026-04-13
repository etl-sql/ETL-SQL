using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Core.Linting.Rules
{
    /// <summary>
    /// Rpt-3: Prevents CREATE VISUAL or CREATE DATASET from using names that conflict with 
    /// engine-reserved keywords or dashboard runtime identifiers (e.g. Manifest, Params, etc.).
    /// </summary>
    public class DashboardKeywordConflictRule : ILintRule
    {
        public string Name => "DashboardKeywordConflict";
        public string Description => "Prevents naming conflicts for visuals and datasets with reserved dashboard keywords.";

        private static readonly string[] ReservedKeywords = 
        { 
            // Engine Reserved
            "ROWCOUNT", "ERROR", "RESULTSETS", "IDENTITY", "FETCH", "TOP", "LIMIT",
            // Dashboard Runtime Reserved
            "Manifest", "Visuals", "Slicers", "Details", "Actions", "Params", "built_at", "Title", "Subtitle", "Description"
        };

        public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();

            foreach (var statement in script.Statements)
            {
                if (statement is CreateVisualStatement visual)
                {
                    CheckConflict(visual.Name, visual, results, "Visual");
                }
                else if (statement is CreateDatasetStatement dataset)
                {
                    CheckConflict(dataset.TempTableName, dataset, results, "Dataset");
                }
            }

            return Task.FromResult<IEnumerable<LintResult>>(results);
        }

        private void CheckConflict(string name, Statement stmt, List<LintResult> results, string type)
        {
            var cleanName = name.TrimStart('@', '#');
            if (ReservedKeywords.Any(k => string.Equals(k, cleanName, StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(new LintResult
                {
                    RuleName = Name,
                    Severity = LintSeverity.Warning,
                    Message = $"{type} name '{name}' is a reserved keyword in the dashboard runtime. This may cause display errors or state management conflicts.",
                    LineNumber = stmt.Line,
                    ColumnNumber = stmt.Column
                });
            }
        }
    }
}
