using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.Analysis.Linting.Rules;

public class UnknownTagLintRule : ILintRule
{
    public string Name => "UnknownTag";
    public string Description => "Warns when an inline metadata tag key is not in the standard tag catalog.";

    private static readonly string _standardList =
        string.Join(", ", LanguageMetadata.StandardTags.OrderBy(t => t).Select(t => $"@{t}"));

    public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var results = new List<LintResult>();
        foreach (var stmt in script.Statements)
            AnalyzeStatement(stmt, results);
        return Task.FromResult<IEnumerable<LintResult>>(results);
    }

    private void AnalyzeStatement(Statement? stmt, List<LintResult> results)
    {
        if (stmt is null) return;

        if (stmt is SelectStatement sel)
        {
            foreach (var col in sel.Columns)
                CheckMetadata(col.Metadata, col.Line, col.Column, results);
            CheckTableRef(sel.FromTable, results);
            foreach (var j in sel.Joins) CheckTableRef(j.Table, results);
            if (sel.FromTable?.Subquery != null) AnalyzeStatement(sel.FromTable.Subquery, results);
            foreach (var j in sel.Joins)
                if (j.Table?.Subquery != null) AnalyzeStatement(j.Table.Subquery, results);
        }
        else if (stmt is CreateDatasetStatement ds)
        {
            AnalyzeStatement(ds.SourceQuery, results);
        }
        else if (stmt is CreateVisualStatement vis && vis.Source.IsInlineSelect)
        {
            AnalyzeStatement(vis.Source.InlineSelect, results);
        }
        else if (stmt is InsertStatement ins && ins.SelectQuery != null)
        {
            AnalyzeStatement(ins.SelectQuery, results);
        }
        else if (stmt is SetOperationStatement setOp)
        {
            AnalyzeStatement(setOp.Left, results);
            AnalyzeStatement(setOp.Right, results);
        }

        // Recurse into control-flow containers
        if (stmt is BlockStatement block)
            foreach (var s in block.Statements ?? []) AnalyzeStatement(s, results);
        else if (stmt is IfStatement ifStmt)
        {
            AnalyzeStatement(ifStmt.IfBody, results);
            foreach (var ei in ifStmt.ElseIfClauses ?? []) AnalyzeStatement(ei.Body, results);
            if (ifStmt.ElseBody != null) AnalyzeStatement(ifStmt.ElseBody, results);
        }
        else if (stmt is WhileStatement w) AnalyzeStatement(w.Body, results);
        else if (stmt is ForStatement f) AnalyzeStatement(f.Body, results);
        else if (stmt is ForeachStatement fe) AnalyzeStatement(fe.Body, results);
        else if (stmt is TryCatchStatement tc)
        {
            AnalyzeStatement(tc.TryBody, results);
            AnalyzeStatement(tc.CatchBody, results);
        }
    }

    private static void CheckTableRef(TableReference? tbl, List<LintResult> results)
    {
        if (tbl?.Metadata?.Count > 0)
            CheckMetadata(tbl.Metadata, tbl.Line, tbl.Column, results);
    }

    private static void CheckMetadata(
        IReadOnlyDictionary<string, string>? metadata,
        int line, int col,
        List<LintResult> results)
    {
        if (metadata is null) return;
        foreach (var key in metadata.Keys)
        {
            if (!StewardshipTagCatalog.IsKnownOrCustom(key))
            {
                results.Add(new LintResult
                {
                    RuleName = "UnknownTag",
                    Severity = LintSeverity.Warning,
                    Message = $"Unknown tag key '@{key}'. Standard tags are: {_standardList}.",
                    LineNumber = line,
                    ColumnNumber = col,
                });
            }
        }
    }
}
