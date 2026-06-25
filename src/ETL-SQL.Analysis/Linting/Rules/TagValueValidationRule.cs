using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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

    // Boolean tags: a bare @tag is stored as "true" by the parser, so both forms are accepted.
    private static readonly HashSet<string> _boolTags = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "pii", "phi", "pci", "sensitive", "encrypted_at_rest", "nullable"
    };

    // Enum tags → allowed values (lower-cased for case-insensitive comparison).
    private static readonly Dictionary<string, string[]> _enumTags = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["classification"] = new[] { "public", "internal", "confidential", "restricted" },
        ["quality"] = new[] { "gold", "silver", "bronze" },
        ["load_pattern"] = new[] { "full", "incremental", "cdc" },
    };

    // Duration tags: integer followed by s/m/h/d (e.g. 1h, 24h, 7d). Mirrors DatasetRefreshIntervalRule.
    private static readonly HashSet<string> _durationTags = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "freshness"
    };

    private static readonly Regex _duration = new(@"^\d+[smhd]$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
            var key = kvp.Key;
            var value = kvp.Value;

            if (_boolTags.Contains(key))
            {
                if (!value.Equals("true", System.StringComparison.OrdinalIgnoreCase)
                    && !value.Equals("false", System.StringComparison.OrdinalIgnoreCase))
                {
                    Warn(results, line, col,
                        $"Tag '@{key}' expects a boolean (true/false), got '{value}'.");
                }
            }
            else if (_enumTags.TryGetValue(key, out var allowed))
            {
                bool ok = false;
                foreach (var a in allowed)
                    if (value.Equals(a, System.StringComparison.OrdinalIgnoreCase)) { ok = true; break; }
                if (!ok)
                {
                    Warn(results, line, col,
                        $"Tag '@{key}' value '{value}' is not one of: {string.Join(", ", allowed)}.");
                }
            }
            else if (_durationTags.Contains(key))
            {
                if (!_duration.IsMatch(value))
                {
                    Warn(results, line, col,
                        $"Tag '@{key}' value '{value}' is not a duration. Use a number followed by s, m, h, or d (e.g. '1h', '24h', '7d').");
                }
            }
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
