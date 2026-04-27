using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Linting.Rules
{
    /// <summary>
    /// Warns when a CREATE VISUAL MAPPINGS section references a column
    /// that is not present in the inline SELECT column list.
    /// Only runs when the source is an inline SELECT (not a #temp table reference,
    /// because the temp table's columns are not statically known at lint time).
    /// </summary>
    public class VisualMappingColumnExistsRule : ILintRule
    {
        public string Name        => "VisualMappingColumnExists";
        public string Description => "Warns when a MAPPINGS column is not present in the visual's inline SELECT.";

        public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();

            foreach (var stmt in script.Statements)
            {
                if (stmt is not CreateVisualStatement visual) continue;
                if (!visual.Source.IsInlineSelect) continue;

                // Only validate plain SELECT; UNION ALL / set-operations are not statically enumerable.
                if (visual.Source.InlineSelect is not SelectStatement select) continue;

                // Collect the alias / column names produced by the SELECT
                var selectColumns = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var col in select.Columns)
                {
                    if (col.Alias != null)
                        selectColumns.Add(col.Alias);
                    else if (col.Expression is IdentifierExpression id)
                        selectColumns.Add(id.Name.Split('.').Last());
                }

                // Skip validation if SELECT * is used (columns are not statically enumerable)
                if (select.Columns.Any(c => c.Expression is IdentifierExpression id2 && id2.Name == "*"))
                    continue;

                foreach (var mapping in visual.Mappings)
                {
                    if (!selectColumns.Contains(mapping.Column))
                    {
                        results.Add(new LintResult
                        {
                            RuleName     = Name,
                            Severity     = LintSeverity.Warning,
                            Message      = $"Visual '{visual.Name}': mapping role '{mapping.Role}' references column '{mapping.Column}' which is not in the inline SELECT column list.",
                            LineNumber   = visual.Line,
                            ColumnNumber = visual.Column
                        });
                    }
                }
            }

            return Task.FromResult<IEnumerable<LintResult>>(results);
        }
    }
}
