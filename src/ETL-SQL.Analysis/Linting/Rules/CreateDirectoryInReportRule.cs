using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ETL_SQL.Analysis.Linting.Rules;
/// <summary>
/// Errors when CREATE DIRECTORY appears in a script that contains Report-SQL statements.
/// In a report context, folder organisation uses CREATE FOLDER — CREATE DIRECTORY is
/// a file-system operation and has no effect on the report catalog.
/// </summary>
public class CreateDirectoryInReportRule : ILintRule
{
    public string Name => "CreateDirectoryInReport";
    public string Description => "Errors when CREATE DIRECTORY is used in a report script. Use CREATE FOLDER to organise reports in the portal.";

    public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var results = new List<LintResult>();

        bool hasReportStatements = script.Statements.Any(s =>
            s is CreateVisualStatement or
                 CreatePageStatement or
                 CreateDatasetStatement or
                 CreateContainerStatement or
                 CreateNavigationStatement or
                 CreateStyleStatement or
                 CreateTemplateStatement or
                 CreateButtonStatement);

        if (!hasReportStatements) return Task.FromResult<IEnumerable<LintResult>>(results);

        foreach (var stmt in script.Statements)
        {
            if (stmt is DirectoryOperationStatement dir &&
                dir.Type == DirectoryOpType.Create)
            {
                results.Add(new LintResult
                {
                    RuleName = Name,
                    Severity = LintSeverity.Error,
                    Message = "CREATE DIRECTORY is a file-system operation and has no effect inside a report script. " +
                                   "To organise reports in the Portal, use CREATE FOLDER instead.",
                    LineNumber = stmt.Line,
                    ColumnNumber = stmt.Column
                });
            }
        }

        return Task.FromResult<IEnumerable<LintResult>>(results);
    }
}
