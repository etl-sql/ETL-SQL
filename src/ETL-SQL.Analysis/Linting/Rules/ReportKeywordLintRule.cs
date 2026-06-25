using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.Analysis.Linting.Rules;
/// <summary>
/// Ensures that Report-SQL object names (Visuals, Pages, Containers, etc.) 
/// do not conflict with reserved engine keywords.
/// </summary>
public class ReportKeywordLintRule : ILintRule
{
    public string Name => "Report-SQL Object Keyword Check";
    public string Description => "Detects visual/page/dataset names that shadow reserved system keywords.";

    public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var results = new List<LintResult>();

        foreach (var stmt in script.Statements)
        {
            if (stmt is CreateVisualStatement cv) Check(cv.Name, cv, results);
            if (stmt is CreatePageStatement cp) Check(cp.Name, cp, results);
            if (stmt is CreateContainerStatement cc) Check(cc.Name, cc, results);
            if (stmt is CreateNavigationStatement cn) Check(cn.Name, cn, results);
            if (stmt is CreateDatasetStatement cd) Check(cd.TempTableName.TrimStart('&'), cd, results);
            if (stmt is CreateStyleStatement cs) Check(cs.Name, cs, results);
            if (stmt is CreateTemplateStatement ct) Check(ct.Name, ct, results);
        }

        return Task.FromResult<IEnumerable<LintResult>>(results);
    }

    private void Check(string name, Statement stmt, List<LintResult> results)
    {
        if (string.IsNullOrEmpty(name)) return;

        if (LanguageMetadata.IsKeyword(name))
        {
            results.Add(new LintResult
            {
                RuleName = Name,
                Severity = LintSeverity.Warning,
                Message = $"Report object name '{name}' is a reserved ETL-SQL keyword. This may cause ambiguity in expressions.",
                LineNumber = stmt.Line,
                ColumnNumber = stmt.Column
            });
        }
    }
}
