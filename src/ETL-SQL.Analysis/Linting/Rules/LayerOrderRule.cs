using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Analysis.Linting.Rules
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
            var results = new List<LintResult>();
            var definedDashboardObjects = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var definedDatasets = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            ProcessStatements(script.Statements, definedDatasets, definedDashboardObjects, results);

            return Task.FromResult<IEnumerable<LintResult>>(results);
        }

        private void ProcessStatements(IEnumerable<Statement> statements, HashSet<string> definedDatasets, HashSet<string> definedDashboardObjects, List<LintResult> results)
        {
            foreach (var stmt in statements)
            {
                ProcessStatement(stmt, definedDatasets, definedDashboardObjects, results);
            }
        }

        private void ProcessStatement(Statement stmt, HashSet<string> definedDatasets, HashSet<string> definedDashboardObjects, List<LintResult> results)
        {
            var created = stmt.GetCreatedTable();
            if (created != null)
            {
                definedDatasets.Add(StripSigil(created));
            }

            switch (stmt)
            {
                case CreateVisualStatement visual:
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

                case CreateButtonStatement btn:
                    definedDashboardObjects.Add(btn.Name);
                    break;

                case CreateNavigationStatement nav:
                    definedDashboardObjects.Add(nav.Name);
                    break;

                case CreatePageStatement page:
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
                
                // Recurse into blocks and control flow
                case BlockStatement block:
                    ProcessStatements(block.Statements, definedDatasets, definedDashboardObjects, results);
                    break;
                case TryCatchStatement tc:
                    ProcessStatement(tc.TryBody, definedDatasets, definedDashboardObjects, results);
                    ProcessStatement(tc.CatchBody, definedDatasets, definedDashboardObjects, results);
                    break;
                case IfStatement ifs:
                    ProcessStatement(ifs.IfBody, definedDatasets, definedDashboardObjects, results);
                    if (ifs.ElseIfClauses != null) foreach (var c in ifs.ElseIfClauses) ProcessStatement(c.Body, definedDatasets, definedDashboardObjects, results);
                    if (ifs.ElseBody != null) ProcessStatement(ifs.ElseBody, definedDatasets, definedDashboardObjects, results);
                    break;
                case WhileStatement ws:
                    ProcessStatement(ws.Body, definedDatasets, definedDashboardObjects, results);
                    break;
                case ForStatement fs:
                    ProcessStatement(fs.Body, definedDatasets, definedDashboardObjects, results);
                    break;
                case ForeachStatement fes:
                    ProcessStatement(fes.Body, definedDatasets, definedDashboardObjects, results);
                    break;
                case ParallelStatement ps:
                    ProcessStatement(ps.Body, definedDatasets, definedDashboardObjects, results);
                    break;
                case ParallelForStatement pfs:
                    ProcessStatement(pfs.Body, definedDatasets, definedDashboardObjects, results);
                    break;
            }
        }

        private static string StripSigil(string name) =>
            name.Length > 0 && (name[0] == '#' || name[0] == '&') ? name[1..] : name;
    }
}
