using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Linting.Rules
{
    /// <summary>
    /// Errors when ENCRYPT = PASSWORD is missing a PASSWORD, or ENCRYPT = KEYFILE is missing a KEYFILE.
    /// </summary>
    public class DatasetEncryptWithoutKeyRule : ILintRule
    {
        public string Name        => "DatasetEncryptWithoutKey";
        public string Description => "Errors when CREATE DATASET encryption mode is missing its required credential.";

        public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();

            foreach (var stmt in script.Statements)
            {
                if (stmt is not CreateDatasetStatement ds) continue;

                if (ds.EncryptionMode == DatasetEncryptionMode.Password &&
                    string.IsNullOrWhiteSpace(ds.EncryptionPassword))
                {
                    results.Add(new LintResult
                    {
                        RuleName     = Name,
                        Severity     = LintSeverity.Error,
                        Message      = $"Dataset '{ds.TempTableName}': ENCRYPT = PASSWORD requires PASSWORD = '...' to be specified.",
                        LineNumber   = ds.Line,
                        ColumnNumber = ds.Column
                    });
                }
                else if (ds.EncryptionMode == DatasetEncryptionMode.KeyFile &&
                         string.IsNullOrWhiteSpace(ds.KeyFile))
                {
                    results.Add(new LintResult
                    {
                        RuleName     = Name,
                        Severity     = LintSeverity.Error,
                        Message      = $"Dataset '{ds.TempTableName}': ENCRYPT = KEYFILE requires KEYFILE = '...' to be specified.",
                        LineNumber   = ds.Line,
                        ColumnNumber = ds.Column
                    });
                }
            }

            return Task.FromResult<IEnumerable<LintResult>>(results);
        }
    }
}
