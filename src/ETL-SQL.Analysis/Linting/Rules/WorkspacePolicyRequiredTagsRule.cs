using System.Text.RegularExpressions;
using ETL_SQL.Core;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Analysis.Linting.Rules;

/// <summary>
/// Makes a checked-in workspace policy enforceable in every local lint/run surface. Required SCRIPT
/// and COLUMN tags are errors, rather than advisory stewardship-score gaps.
/// </summary>
public sealed class WorkspacePolicyRequiredTagsRule : ILintRule
{
    public const string MissingRequiredTagCode = "ETLSQL-WORKSPACE-POLICY-REQUIRED-TAG";

    public string Name => "WorkspacePolicyRequiredTags";
    public string Description => "Enforces required metadata tags from etlsql-policy.json.";

    public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var policy = LoadPolicy(context.DocumentUri);
        if (policy is null) return Task.FromResult<IEnumerable<LintResult>>([]);

        var results = new List<LintResult>();
        Audit("SCRIPT", Path.GetFileName(context.DocumentUri), script.Metadata, script.Line, script.Column);
        foreach (var statement in script.Statements) AuditStatement(statement);
        return Task.FromResult<IEnumerable<LintResult>>(results);

        void AuditStatement(Statement? statement)
        {
            if (statement is null) return;
            switch (statement)
            {
                case SelectStatement select:
                    if (select.IntoTable is null)
                    {
                        AuditStatement(select.FromTable?.Subquery);
                        foreach (var join in select.Joins) AuditStatement(join.Table.Subquery);
                        break;
                    }
                    var table = select.IntoTable?.TableName ?? select.FromTable?.TableName ?? "query";
                    foreach (var column in select.Columns)
                    {
                        var name = column.Alias ?? column.Expression.ToSql();
                        Audit("COLUMN", $"{table}.{name}", column.Metadata, column.Line, column.Column);
                    }
                    AuditStatement(select.FromTable?.Subquery);
                    foreach (var join in select.Joins) AuditStatement(join.Table.Subquery);
                    break;
                case InsertStatement { SelectQuery: not null } insert:
                    AuditStatement(insert.SelectQuery);
                    break;
                case CreateDatasetStatement dataset:
                    AuditStatement(dataset.SourceQuery);
                    break;
                case SetOperationStatement setOperation:
                    AuditStatement(setOperation.Left);
                    AuditStatement(setOperation.Right);
                    break;
                case BlockStatement block:
                    foreach (var nested in block.Statements ?? []) AuditStatement(nested);
                    break;
                case IfStatement conditional:
                    AuditStatement(conditional.IfBody);
                    foreach (var branch in conditional.ElseIfClauses ?? []) AuditStatement(branch.Body);
                    AuditStatement(conditional.ElseBody);
                    break;
                case WhileStatement loop:
                    AuditStatement(loop.Body);
                    break;
                case ForStatement loop:
                    AuditStatement(loop.Body);
                    break;
                case ForeachStatement loop:
                    AuditStatement(loop.Body);
                    break;
                case TryCatchStatement guarded:
                    AuditStatement(guarded.TryBody);
                    AuditStatement(guarded.CatchBody);
                    break;
            }
        }

        void Audit(string scope, string name, IReadOnlyDictionary<string, string> tags, int line, int column)
        {
            foreach (var requirement in policy.RequiredTags.Where(rule =>
                rule.Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase)
                && !rule.Exclude.Any(pattern => WildcardMatches(name, pattern))))
            {
                var tag = requirement.Tag.TrimStart('@');
                if (tags.TryGetValue(tag, out var value) && !string.IsNullOrWhiteSpace(value)) continue;
                results.Add(new LintResult
                {
                    RuleName = Name,
                    Code = MissingRequiredTagCode,
                    Severity = LintSeverity.Error,
                    Message = $"Workspace policy requires {requirement.Tag} on {scope.ToLowerInvariant()} '{name}'.",
                    LineNumber = line,
                    ColumnNumber = column
                });
            }
        }
    }

    private static WorkspacePolicyDocument? LoadPolicy(string documentUri)
    {
        if (string.IsNullOrWhiteSpace(documentUri)) return null;
        string path;
        try
        {
            path = Uri.TryCreate(documentUri, UriKind.Absolute, out var uri) && uri.IsFile
                ? uri.LocalPath
                : Path.GetFullPath(documentUri);
        }
        catch (Exception) when (documentUri.Contains("://", StringComparison.Ordinal))
        {
            return null;
        }

        var directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory)) return null;
        var policyPath = WorkspacePolicyLoader.Find(directory);
        if (policyPath is null) return null;
        var loaded = WorkspacePolicyLoader.Load(policyPath);
        return loaded.IsValid ? loaded.Policy : null;
    }

    private static bool WildcardMatches(string value, string pattern)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(250));
    }
}
