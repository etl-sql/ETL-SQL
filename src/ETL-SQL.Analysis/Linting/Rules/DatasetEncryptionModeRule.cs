using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Analysis.Linting.Rules
{
    /// <summary>
    /// Warns when CREATE DATASET specifies ENCRYPT = PASSWORD or ENCRYPT = KEYFILE. In a portal the
    /// at-rest cache is always encrypted with the portal-managed key, so an ENCRYPT clause here is a
    /// transport credential that is ignored at rest — supply it on EXPORT DATASET instead to produce a
    /// movable file.
    /// </summary>
    public class DatasetEncryptionModeRule : ILintRule
    {
        public string Name        => "DatasetEncryptionMode";
        public string Description => "Warns when CREATE DATASET uses ENCRYPT = PASSWORD or ENCRYPT = KEYFILE — at rest the portal key is used; the transport credential belongs on EXPORT DATASET.";

        public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();

            foreach (var stmt in script.Statements)
            {
                if (stmt is not CreateDatasetStatement ds) continue;

                if (ds.EncryptionMode is DatasetEncryptionMode.Password or DatasetEncryptionMode.KeyFile)
                {
                    var mode = ds.EncryptionMode == DatasetEncryptionMode.Password ? "PASSWORD" : "KEYFILE";
                    results.Add(new LintResult
                    {
                        RuleName     = Name,
                        Severity     = LintSeverity.Warning,
                        Message      = $"Dataset '{ds.TempTableName}': ENCRYPT = {mode} on CREATE DATASET is a transport credential that is ignored at rest in a portal (the portal at-rest key is used). Supply the credential on EXPORT DATASET to produce a movable file.",
                        LineNumber   = ds.Line,
                        ColumnNumber = ds.Column
                    });
                }
            }

            return Task.FromResult<IEnumerable<LintResult>>(results);
        }
    }
}
