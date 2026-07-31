using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Analysis.Linting.Rules;
/// <summary>
/// Flags usage of deprecated connection syntax and emits migration diagnostics.
/// </summary>
public class DeprecatedConnectionSyntaxRule : ILintRule
{
    public const string FileConnectorDiagnosticCode = "ETLSQL-MIG001";

    public string Name => "Deprecated Connection Syntax";
    public string Description => "Detects deprecated connection syntax and reports canonical replacements.";

    public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var results = new List<LintResult>();

        foreach (var stmt in script.Statements)
        {
            if (stmt is CreateConnectionStatement cc)
            {
                Check(cc.ConnectionType, cc, results);
            }
            else if (stmt is AlterConnectionStatement ac)
            {
                Check(ac.ConnectionType, ac, results);
            }
        }

        return Task.FromResult<IEnumerable<LintResult>>(results);
    }

    private void Check(string? type, Statement stmt, List<LintResult> results)
    {
        if (string.Equals(type, "FILE", StringComparison.OrdinalIgnoreCase))
        {
            results.Add(new LintResult
            {
                RuleName = Name,
                Code = FileConnectorDiagnosticCode,
                Severity = LintSeverity.Warning,
                Message = "Connection type 'FILE' is deprecated and will be removed in v0.19.0. Use 'FLATFILE' instead.",
                LineNumber = stmt.Line,
                ColumnNumber = stmt.Column
            });
        }
    }
}
