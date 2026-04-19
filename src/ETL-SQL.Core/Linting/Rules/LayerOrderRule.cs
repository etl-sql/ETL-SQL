using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Linting.Rules
{
    /// <summary>
    /// Warns when Report-SQL statements appear in the wrong order:
    ///   1. CREATE DATASET / temp tables  (data layer)
    ///   2. CREATE VISUAL                 (visual layer)
    ///   3. CREATE PAGE                   (layout layer)
    ///
    /// A visual defined after a page that references it, or a page defined before
    /// all its visuals, indicates a likely authoring mistake.
    /// </summary>
    public class LayerOrderRule : ILintRule
    {
        public string Name        => "LayerOrder";
        public string Description => "Warns when CREATE VISUAL or CREATE PAGE statements appear before their dependencies.";

        public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results      = new List<LintResult>();
            var definedDashboardObjects = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var definedDatasets = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var stmt in script.Statements)
            {
                switch (stmt)
                {
                    case CreateDatasetStatement ds:
                        definedDatasets.Add(StripSigil(ds.TempTableName));
                        break;

                    case SelectStatement sel when sel.IntoTable != null:
                        definedDatasets.Add(StripSigil(sel.IntoTable.TableName));
                        break;

                    case CreateVisualStatement visual:
                        // Check that a temp-table source was defined earlier
                        if (!visual.Source.IsInlineSelect && visual.Source.TempTableName != null)
                        {
                            var refName = visual.Source.TempTableName;
                            if (!definedDatasets.Contains(StripSigil(refName)))
                            {
                                results.Add(new LintResult
                                {
                                    RuleName     = Name,
                                    Severity     = LintSeverity.Warning,
                                    Message      = $"Visual '{visual.Name}' references '{refName}' before it is defined. Move CREATE DATASET / SELECT INTO above this CREATE VISUAL.",
                                    LineNumber   = visual.Line,
                                    ColumnNumber = visual.Column
                                });
                            }
                        }
                        definedDashboardObjects.Add(visual.Name);
                        break;

                    case CreateContainerStatement container:
                        definedDashboardObjects.Add(container.Name);
                        break;

                    case CreateNavigationStatement nav:
                        definedDashboardObjects.Add(nav.Name);
                        break;

                    case CreatePageStatement page:
                        // All slot objects should be defined before the page
                        foreach (var (slot, objectName) in page.SlotMap)
                        {
                            if (!definedDashboardObjects.Contains(objectName))
                            {
                                results.Add(new LintResult
                                {
                                    RuleName     = Name,
                                    Severity     = LintSeverity.Warning,
                                    Message      = $"Page '{page.Name}': slot '{slot}' references dashboard object '{objectName}' before it is defined. Move its CREATE statement above this CREATE PAGE.",
                                    LineNumber   = page.Line,
                                    ColumnNumber = page.Column
                                });
                            }
                        }
                        break;
                }
            }

            return Task.FromResult<IEnumerable<LintResult>>(results);
        }

        private static string StripSigil(string name) =>
            name.Length > 0 && (name[0] == '#' || name[0] == '&') ? name[1..] : name;
    }
}
