using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ETL_SQL.Analysis.Linting.Rules;
/// <summary>
/// Warns when a CREATE VISUAL references a #temp table that is not defined
/// earlier in the same script via CREATE DATASET or SELECT INTO.
/// </summary>
public class VisualSourceExistsRule : ILintRule
{
    public string Name => "VisualSourceExists";
    public string Description => "Warns when CREATE VISUAL SOURCE = &dataset (or #table) references a source not defined in the script.";

    public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var results = new List<LintResult>();
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
                    RuleName = Name,
                    Severity = LintSeverity.Warning,
                    Message = $"Visual '{visual.Name}' references source '{refName}' which is not defined in this script. Ensure it is created before this visual.",
                    LineNumber = visual.Line,
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
        CollectFromStatements(script.Statements, names);
        return names;
    }

    private static void CollectFromStatements(IEnumerable<Statement> statements, HashSet<string> names)
    {
        foreach (var stmt in statements)
        {
            var created = stmt.GetCreatedTable();
            if (created != null)
            {
                names.Add(StripSigil(created));
            }

            // Recurse into blocks to find nested definitions (e.g. inside BEGIN TRY)
            if (stmt is BlockStatement block)
            {
                CollectFromStatements(block.Statements, names);
            }
            else if (stmt is TryCatchStatement tc)
            {
                CollectFromStatements(new[] { tc.TryBody, tc.CatchBody }, names);
            }
            else if (stmt is IfStatement ifs)
            {
                var branchStmts = new List<Statement> { ifs.IfBody };
                if (ifs.ElseIfClauses != null) branchStmts.AddRange(ifs.ElseIfClauses.Select(c => c.Body));
                if (ifs.ElseBody != null) branchStmts.Add(ifs.ElseBody);
                CollectFromStatements(branchStmts, names);
            }
            else if (stmt is WhileStatement ws)
            {
                CollectFromStatements(new[] { ws.Body }, names);
            }
            else if (stmt is ForStatement fs)
            {
                CollectFromStatements(new[] { fs.Body }, names);
            }
            else if (stmt is ForeachStatement fes)
            {
                CollectFromStatements(new[] { fes.Body }, names);
            }
            else if (stmt is ParallelStatement ps)
            {
                CollectFromStatements(new[] { ps.Body }, names);
            }
            else if (stmt is ParallelForStatement pfs)
            {
                CollectFromStatements(new[] { pfs.Body }, names);
            }
        }
    }
}
