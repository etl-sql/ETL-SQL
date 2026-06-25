using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Analysis.Linting.Rules;
/// <summary>
/// Warns when USE DATASET &amp;X appears before CREATE DATASET &amp;X in the same script.
/// Within a single execution, CREATE must precede USE — the dataset does not exist yet
/// when the USE statement runs.
/// </summary>
public class UseBeforeCreateRule : ILintRule
{
    public string Name => "UseBeforeCreate";
    public string Description => "Warns when USE DATASET appears before CREATE DATASET for the same dataset name in the same script.";

    public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var results = new List<LintResult>();
        var usedNames = new Dictionary<string, (int Line, int Column)>(System.StringComparer.OrdinalIgnoreCase);
        var createdNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var stmt in script.Statements)
        {
            if (stmt is UseDatasetStatement use && !createdNames.Contains(use.DatasetName))
            {
                // Record the first USE for this name (before any matching CREATE)
                if (!usedNames.ContainsKey(use.DatasetName))
                    usedNames[use.DatasetName] = (use.Line, use.Column);
            }
            else if (stmt is CreateDatasetStatement create)
            {
                if (usedNames.TryGetValue(create.TempTableName, out var useLocation))
                {
                    results.Add(new LintResult
                    {
                        RuleName = Name,
                        Severity = LintSeverity.Warning,
                        Message = $"USE DATASET '{create.TempTableName}' (line {useLocation.Line}) appears before CREATE DATASET '{create.TempTableName}' (line {create.Line}). " +
                                       "CREATE must precede USE within the same script.",
                        LineNumber = useLocation.Line,
                        ColumnNumber = useLocation.Column
                    });
                }
                createdNames.Add(create.TempTableName);
                usedNames.Remove(create.TempTableName);
            }
        }

        return Task.FromResult<IEnumerable<LintResult>>(results);
    }
}
