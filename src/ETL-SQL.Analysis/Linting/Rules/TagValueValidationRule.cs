using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.Analysis.Linting.Rules;
/// <summary>
/// Validates the <em>values</em> of standard governance tags against the typed schema in
/// Docs/Reference/Lineage.md. Complements <see cref="UnknownTagLintRule"/>, which validates
/// tag <em>keys</em>. Only typed standard tags are checked; free-form string tags
/// (e.g. @owner, @sla, @d) and unknown tags are ignored here.
/// </summary>
public class TagValueValidationRule : ILintRule
{
    public string Name => "TagValue";
    public string Description => "Warns when a standard governance tag has a value outside its allowed type or enum.";

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
        foreach (var kvp in metadata)
        {
            var validation = StewardshipTagCatalog.Validate(kvp.Key, kvp.Value);
            if (!validation.IsValid && validation.Message is not null)
                Warn(results, line, col, validation.Message);
        }
    }

    private static void Warn(List<LintResult> results, int line, int col, string message)
    {
        results.Add(new LintResult
        {
            RuleName = "TagValue",
            Severity = LintSeverity.Warning,
            Message = message,
            LineNumber = line,
            ColumnNumber = col,
        });
    }
}
