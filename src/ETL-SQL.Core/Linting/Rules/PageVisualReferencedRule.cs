using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Linting.Rules
{
    /// <summary>
    /// Warns when a CREATE PAGE MAP slot references a visual name that is not
    /// defined earlier in the same script.
    /// </summary>
    public class PageVisualReferencedRule : ILintRule
    {
        public string Name        => "PageVisualReferenced";
        public string Description => "Warns when a CREATE PAGE MAP slot references a visual that is not defined in the script.";

        public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results      = new List<LintResult>();
            var visualNames  = new HashSet<string>(
                script.Statements.OfType<CreateVisualStatement>().Select(v => v.Name),
                System.StringComparer.OrdinalIgnoreCase);

            foreach (var stmt in script.Statements)
            {
                if (stmt is not CreatePageStatement page) continue;

                foreach (var (slot, visualName) in page.SlotMap)
                {
                    if (!visualNames.Contains(visualName))
                    {
                        results.Add(new LintResult
                        {
                            RuleName     = Name,
                            Severity     = LintSeverity.Warning,
                            Message      = $"Page '{page.Name}': slot '{slot}' references visual '{visualName}' which is not defined in this script.",
                            LineNumber   = page.Line,
                            ColumnNumber = page.Column
                        });
                    }
                }
            }

            return Task.FromResult<IEnumerable<LintResult>>(results);
        }
    }
}
