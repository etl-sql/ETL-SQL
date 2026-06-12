using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ETL_SQL.Analysis.Linting.Rules
{
    /// <summary>
    /// Informational hint: USE DATASET &amp;X is unnecessary when CREATE DATASET &amp;X appears
    /// earlier in the same script — the dataset is already in the temp-table namespace.
    /// </summary>
    public class UseDatasetRedundantRule : ILintRule
    {
        public string Name => "UseDatasetRedundant";
        public string Description => "Hints when USE DATASET is redundant because the dataset was already created in the same script.";

        public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();

            var createdNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var stmt in script.Statements)
            {
                if (stmt is CreateDatasetStatement create)
                {
                    createdNames.Add(create.TempTableName);
                }
                else if (stmt is UseDatasetStatement use && createdNames.Contains(use.DatasetName))
                {
                    results.Add(new LintResult
                    {
                        RuleName = Name,
                        Severity = LintSeverity.Info,
                        Message = $"USE DATASET '{use.DatasetName}' is not required here — '{use.DatasetName}' was created earlier in this script and is already available as a temp table.",
                        LineNumber = use.Line,
                        ColumnNumber = use.Column
                    });
                }
            }

            return Task.FromResult<IEnumerable<LintResult>>(results);
        }
    }
}
