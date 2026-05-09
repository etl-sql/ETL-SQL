using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Analysis.Linting.Rules
{
    /// <summary>
    /// Warns when CREATE DATASET uses ENCRYPT = PASSWORD or ENCRYPT = KEYFILE.
    /// MACHINE is the recommended mode for server-managed datasets because it requires
    /// no portable credential and binds the Parquet file to the machine that created it.
    /// PASSWORD and KEYFILE are still accepted for scenarios where the dataset must be
    /// transferred to another machine (closed systems, cross-server sharing).
    /// </summary>
    public class DatasetEncryptionModeRule : ILintRule
    {
        public string Name        => "DatasetEncryptionMode";
        public string Description => "Warns when CREATE DATASET uses ENCRYPT = PASSWORD or ENCRYPT = KEYFILE — MACHINE is recommended for server-managed datasets.";

        public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();

            foreach (var stmt in script.Statements)
            {
                if (stmt is not CreateDatasetStatement ds) continue;

                if (ds.EncryptionMode == DatasetEncryptionMode.Password)
                {
                    results.Add(new LintResult
                    {
                        RuleName     = Name,
                        Severity     = LintSeverity.Warning,
                        Message      = $"Dataset '{ds.TempTableName}': ENCRYPT = PASSWORD produces a portable Parquet file that can be moved to another machine. Use ENCRYPT = MACHINE if the dataset should be server-bound.",
                        LineNumber   = ds.Line,
                        ColumnNumber = ds.Column
                    });
                }
                else if (ds.EncryptionMode == DatasetEncryptionMode.KeyFile)
                {
                    results.Add(new LintResult
                    {
                        RuleName     = Name,
                        Severity     = LintSeverity.Warning,
                        Message      = $"Dataset '{ds.TempTableName}': ENCRYPT = KEYFILE produces a portable Parquet file that can be moved to another machine. Use ENCRYPT = MACHINE if the dataset should be server-bound.",
                        LineNumber   = ds.Line,
                        ColumnNumber = ds.Column
                    });
                }
            }

            return Task.FromResult<IEnumerable<LintResult>>(results);
        }
    }
}
