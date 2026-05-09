using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ETL_SQL.Analysis.Linting.Rules
{
    /// <summary>
    /// Warns when a CREATE VISUAL references a #temp table that is not defined
    /// earlier in the same script via CREATE DATASET or SELECT INTO.
    /// </summary>
    public class VisualSourceExistsRule : ILintRule
    {
        public string Name        => "VisualSourceExists";
        public string Description => "Warns when CREATE VISUAL SOURCE = &dataset (or #table) references a source not defined in the script.";

        public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results   = new List<LintResult>();
            var tempNames = CollectTempTableNames(script);

            foreach (var stmt in script.Statements)
            {
                if (stmt is not CreateVisualStatement visual) continue;
                if (visual.Source.IsInlineSelect) continue;

                var refName = visual.Source.TempTableName;
                if (refName == null) continue;

                var bare = StripSigil(refName);
                if (!tempNames.Contains(bare))
                {
                    results.Add(new LintResult
                    {
                        RuleName     = Name,
                        Severity     = LintSeverity.Warning,
                        Message      = $"Visual '{visual.Name}' references source '{refName}' which is not defined in this script. Ensure it is created before this visual.",
                        LineNumber   = visual.Line,
                        ColumnNumber = visual.Column
                    });
                }
            }

            return Task.FromResult<IEnumerable<LintResult>>(results);
        }

        private static string StripSigil(string name) =>
            name.Length > 0 && (name[0] == '#' || name[0] == '&') ? name[1..] : name;

        private static HashSet<string> CollectTempTableNames(Script script)
        {
            var names = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var stmt in script.Statements)
            {
                if (stmt is CreateDatasetStatement ds)
                    names.Add(StripSigil(ds.TempTableName));
                else if (stmt is SelectStatement sel && sel.IntoTable != null)
                    names.Add(StripSigil(sel.IntoTable.TableName));
            }
            return names;
        }
    }
}
