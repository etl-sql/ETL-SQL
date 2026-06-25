using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Analysis.Linting.Rules;
/// <summary>
/// Errors when EXPORT DATASET or PUBLISH DATASET specifies ENCRYPT = PASSWORD without a PASSWORD,
/// or ENCRYPT = KEYFILE without a KEYFILE. The transport credential is required for the portable
/// file (CREATE DATASET encrypts at rest with the portal key and needs no credential).
/// </summary>
public class DatasetEncryptWithoutKeyRule : ILintRule
{
    public string Name => "DatasetEncryptWithoutKey";
    public string Description => "Errors when EXPORT/PUBLISH DATASET is missing the transport credential its ENCRYPT mode requires.";

    public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var results = new List<LintResult>();

        foreach (var stmt in script.Statements)
        {
            (string verb, string name, DatasetEncryptionMode mode, string? password, string? keyFile, int line, int col)? t = stmt switch
            {
                ExportDatasetStatement ex => ("EXPORT", ex.DatasetName, ex.EncryptionMode, ex.EncryptionPassword, ex.KeyFile, ex.Line, ex.Column),
                PublishDatasetStatement pb => ("PUBLISH", pb.DatasetName, pb.EncryptionMode, pb.EncryptionPassword, pb.KeyFile, pb.Line, pb.Column),
                _ => null
            };
            if (t is null) continue;
            var (verb, name, mode, password, keyFile, line, col) = t.Value;

            if (mode == DatasetEncryptionMode.Password && string.IsNullOrWhiteSpace(password))
            {
                results.Add(new LintResult
                {
                    RuleName = Name,
                    Severity = LintSeverity.Error,
                    Message = $"Dataset '{name}': {verb} DATASET with ENCRYPT = PASSWORD requires PASSWORD = '...' to be specified.",
                    LineNumber = line,
                    ColumnNumber = col
                });
            }
            else if (mode == DatasetEncryptionMode.KeyFile && string.IsNullOrWhiteSpace(keyFile))
            {
                results.Add(new LintResult
                {
                    RuleName = Name,
                    Severity = LintSeverity.Error,
                    Message = $"Dataset '{name}': {verb} DATASET with ENCRYPT = KEYFILE requires KEYFILE = '...' to be specified.",
                    LineNumber = line,
                    ColumnNumber = col
                });
            }
        }

        return Task.FromResult<IEnumerable<LintResult>>(results);
    }
}
