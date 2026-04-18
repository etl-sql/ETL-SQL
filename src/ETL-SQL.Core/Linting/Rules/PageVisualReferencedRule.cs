using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Linting.Rules
{
    /// <summary>
    /// Validates CREATE PAGE slot consistency (Rpt-3 / Rpt-4):
    ///   - Every MAP slot must reference a visual defined in the script.
    ///   - Every letter in STRUCTURE must have a MAP entry.
    ///   - Every MAP key must appear in STRUCTURE.
    /// </summary>
    public class PageVisualReferencedRule : ILintRule
    {
        public string Name        => "PageVisualReferenced";
        public string Description => "Warns when CREATE PAGE MAP slots and STRUCTURE letters are inconsistent, or a referenced visual is not defined.";

        private static readonly Regex _slotLetters = new(@"[A-Za-z]", RegexOptions.Compiled);

        public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results     = new List<LintResult>();
            var objectNames = new HashSet<string>(
                script.Statements.OfType<CreateVisualStatement>().Select(v => v.Name)
                .Concat(script.Statements.OfType<CreateContainerStatement>().Select(c => c.Name))
                .Concat(script.Statements.OfType<CreateNavigationStatement>().Select(n => n.Name)),
                System.StringComparer.OrdinalIgnoreCase);

            foreach (var stmt in script.Statements)
            {
                if (stmt is not CreatePageStatement page) continue;

                // Rpt-3: MAP references must exist in the script
                foreach (var (slot, objectName) in page.SlotMap)
                {
                    if (!objectNames.Contains(objectName))
                        results.Add(new LintResult
                        {
                            RuleName     = Name,
                            Severity     = LintSeverity.Warning,
                            Message      = $"Page '{page.Name}': slot '{slot}' references object '{objectName}' which is not defined in this script.",
                            LineNumber   = page.Line,
                            ColumnNumber = page.Column
                        });
                }

                // Rpt-4: STRUCTURE letters and MAP keys must match exactly
                var structureSlots = new HashSet<string>(
                    _slotLetters.Matches(page.Structure).Select(m => m.Value.ToUpperInvariant()));

                var mapKeys = new HashSet<string>(
                    page.SlotMap.Keys.Select(k => k.ToUpperInvariant()));

                foreach (var letter in structureSlots.Where(s => !mapKeys.Contains(s)))
                    results.Add(new LintResult
                    {
                        RuleName     = Name,
                        Severity     = LintSeverity.Warning,
                        Message      = $"Page '{page.Name}': STRUCTURE slot '{letter}' has no entry in MAP.",
                        LineNumber   = page.Line,
                        ColumnNumber = page.Column
                    });

                foreach (var key in mapKeys.Where(k => !structureSlots.Contains(k)))
                    results.Add(new LintResult
                    {
                        RuleName     = Name,
                        Severity     = LintSeverity.Warning,
                        Message      = $"Page '{page.Name}': MAP key '{key}' does not appear in STRUCTURE.",
                        LineNumber   = page.Line,
                        ColumnNumber = page.Column
                    });
            }

            return Task.FromResult<IEnumerable<LintResult>>(results);
        }
    }
}
