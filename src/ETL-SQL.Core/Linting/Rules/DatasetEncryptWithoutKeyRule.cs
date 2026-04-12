using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Linting.Rules
{
    /// <summary>
    /// Errors when ENCRYPT = ON is specified on a CREATE DATASET without a KEYFILE.
    /// </summary>
    public class DatasetEncryptWithoutKeyRule : ILintRule
    {
        public string Name        => "DatasetEncryptWithoutKey";
        public string Description => "Errors when CREATE DATASET uses ENCRYPT = ON without specifying a KEYFILE.";

        public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();

            foreach (var stmt in script.Statements)
            {
                if (stmt is not CreateDatasetStatement ds) continue;
                if (!ds.Encrypt) continue;
                if (!string.IsNullOrWhiteSpace(ds.KeyFile)) continue;

                results.Add(new LintResult
                {
                    RuleName     = Name,
                    Severity     = LintSeverity.Error,
                    Message      = $"Dataset '{ds.TempTableName}': ENCRYPT = ON requires a KEYFILE to be specified.",
                    LineNumber   = ds.Line,
                    ColumnNumber = ds.Column
                });
            }

            return Task.FromResult<IEnumerable<LintResult>>(results);
        }
    }
}
