using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Analysis.Linting.Rules;
/// <summary>
/// SEC-5: Flags SECRET: references on connection fields where the engine does not resolve them
/// (anything outside <see cref="SecretResolvableFields.CredentialKeys"/>). At execution time
/// these fail connection creation, so surfacing them at lint time saves a failed run.
/// </summary>
public partial class SecretReferenceUsageRule : ILintRule
{
    public string Name => "SecretReferenceUsage";
    public string Description =>
        "Detects SECRET: references on connection fields that are not secret-resolvable. " +
        "Secrets are only resolved for credential fields (PASSWORD, TOKEN, ACCESS_KEY, ...).";

    public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var results = new List<LintResult>();

        foreach (var statement in script.Statements)
        {
            AnalyzeStatement(statement, results);
        }

        return Task.FromResult<IEnumerable<LintResult>>(results);
    }

    private void AnalyzeStatement(Statement statement, List<LintResult> results)
    {
        if (statement is CreateConnectionStatement create)
        {
            CheckOptions(create.ConnectionName, create.Options, create, results);
            CheckTarget(create.ConnectionName, create.TargetExpression, create, results);
        }
        else if (statement is AlterConnectionStatement alter)
        {
            CheckOptions(alter.ConnectionName, alter.Options, alter, results);
            CheckTarget(alter.ConnectionName, alter.TargetExpression, alter, results);
        }
        else if (statement is BlockStatement block)
        {
            foreach (var s in block.Statements) AnalyzeStatement(s, results);
        }
        else if (statement is IfStatement ifStmt)
        {
            AnalyzeStatement(ifStmt.IfBody, results);
            if (ifStmt.ElseIfClauses != null)
                foreach (var ei in ifStmt.ElseIfClauses) AnalyzeStatement(ei.Body, results);
            if (ifStmt.ElseBody != null) AnalyzeStatement(ifStmt.ElseBody, results);
        }
        else if (statement is WhileStatement whileStmt)
        {
            AnalyzeStatement(whileStmt.Body, results);
        }
        else if (statement is ForStatement forStmt)
        {
            AnalyzeStatement(forStmt.Body, results);
        }
        else if (statement is ForeachStatement foreachStmt)
        {
            AnalyzeStatement(foreachStmt.Body, results);
        }
        else if (statement is TryCatchStatement tryCatch)
        {
            AnalyzeStatement(tryCatch.TryBody, results);
            AnalyzeStatement(tryCatch.CatchBody, results);
        }
    }

    private void CheckOptions(string connectionName, Dictionary<string, Expression>? options, AstNode node, List<LintResult> results)
    {
        if (options == null) return;

        foreach (var (key, expr) in options)
        {
            if (expr is LiteralExpression { Value: string s }
                && s.TrimStart().StartsWith("SECRET:", StringComparison.OrdinalIgnoreCase)
                && !SecretResolvableFields.IsResolvable(key))
            {
                Report(connectionName, key, node, results);
            }
        }
    }

    private void CheckTarget(string connectionName, Expression? target, AstNode node, List<LintResult> results)
    {
        if (target is not LiteralExpression { Value: string s }) return;

        foreach (Match match in ConnectionStringSecretFieldRegex().Matches(s))
        {
            var key = match.Groups["key"].Value;
            if (!SecretResolvableFields.IsResolvable(key))
                Report(connectionName, key, node, results);
        }
    }

    private void Report(string connectionName, string key, AstNode node, List<LintResult> results)
    {
        results.Add(new LintResult
        {
            RuleName = Name,
            Severity = LintSeverity.Error,
            Message = $"Connection '{connectionName}': field '{key}' uses a SECRET: reference, but secrets are only " +
                      "resolved for credential fields (PASSWORD, TOKEN, ACCESS_KEY, SECRET_KEY, ...) and fields " +
                      "listed in Governance:Secrets:SensitiveConnectionFields. This fails at execution time unless " +
                      "the field is designated sensitive in that setting.",
            LineNumber = node.Line,
            ColumnNumber = node.Column
        });
    }

    // Mirrors the engine's ConnectionSecretResolver connection-string field matching.
    [GeneratedRegex(@"(?i)(?:^|;)\s*(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<quote>['""]?)SECRET:[^;'""]+\k<quote>")]
    private static partial Regex ConnectionStringSecretFieldRegex();
}
