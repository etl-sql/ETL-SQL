using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core.Common;

namespace ETL_SQL.Analysis.Linting.Rules;

public class RunScriptDependencyPreflightRule : ILintRule
{
    public string Name => "RunScriptDependencyPreflight";
    public string Description => "Preflights literal RUN SCRIPT file dependencies for syntax and undeclared variable errors.";

    public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var results = new List<LintResult>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AnalyzeScript(script, context.DocumentUri, visited, results);
        return Task.FromResult<IEnumerable<LintResult>>(results);
    }

    private void AnalyzeScript(
        Script script,
        string documentUri,
        HashSet<string> visited,
        List<LintResult> results)
    {
        foreach (var run in FindRunScripts(script.Statements))
        {
            if (run.PathExpression is not LiteralExpression { Value: string rawPath }
                || string.IsNullOrWhiteSpace(rawPath)
                || rawPath.StartsWith("orch://", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var childPath = ResolveChildPath(rawPath, documentUri);
            if (childPath == null || !File.Exists(childPath) || !visited.Add(childPath))
                continue;

            Script childScript;
            try
            {
                var source = File.ReadAllText(childPath);
                childScript = new Parser(new Lexer(source).Tokenize(), source).Parse();
            }
            catch (Exception ex)
            {
                results.Add(new LintResult
                {
                    RuleName = Name,
                    Severity = LintSeverity.Error,
                    Message = $"RUN SCRIPT dependency '{childPath}' failed to parse: {ex.Message}",
                    LineNumber = run.Line,
                    ColumnNumber = run.Column
                });
                continue;
            }

            foreach (var diagnostic in childScript.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            {
                results.Add(new LintResult
                {
                    RuleName = Name,
                    Severity = LintSeverity.Error,
                    Message = $"RUN SCRIPT dependency '{childPath}' failed to parse: {diagnostic.Message}",
                    LineNumber = diagnostic.Line,
                    ColumnNumber = diagnostic.Column
                });
            }

            var childContext = new DefaultLintContext { DocumentUri = childPath };
            foreach (var finding in new UndeclaredVariableRule().AnalyzeAsync(childScript, childContext).GetAwaiter().GetResult())
            {
                results.Add(new LintResult
                {
                    RuleName = Name,
                    Severity = finding.Severity,
                    Message = $"RUN SCRIPT dependency '{childPath}': {finding.Message}",
                    LineNumber = finding.LineNumber,
                    ColumnNumber = finding.ColumnNumber
                });
            }

            AnalyzeScript(childScript, childPath, visited, results);
        }
    }

    private static string? ResolveChildPath(string rawPath, string documentUri)
    {
        if (Path.IsPathRooted(rawPath))
            return Path.GetFullPath(rawPath);

        var basePath = documentUri;
        if (Uri.TryCreate(documentUri, UriKind.Absolute, out var uri) && uri.IsFile)
            basePath = uri.LocalPath;

        var baseDirectory = string.IsNullOrWhiteSpace(basePath)
            ? Directory.GetCurrentDirectory()
            : Directory.Exists(basePath)
                ? basePath
                : Path.GetDirectoryName(basePath);

        return string.IsNullOrWhiteSpace(baseDirectory)
            ? null
            : Path.GetFullPath(Path.Combine(baseDirectory, rawPath));
    }

    private static IEnumerable<RunScriptStatement> FindRunScripts(IEnumerable<Statement> statements)
    {
        foreach (var statement in statements)
        {
            foreach (var nested in FindRunScripts(statement))
                yield return nested;
        }
    }

    private static IEnumerable<RunScriptStatement> FindRunScripts(Statement statement)
    {
        if (statement is RunScriptStatement run)
            yield return run;

        switch (statement)
        {
            case BlockStatement block:
                foreach (var nested in FindRunScripts(block.Statements)) yield return nested;
                break;
            case IfStatement ifStmt:
                foreach (var nested in FindRunScripts(ifStmt.IfBody)) yield return nested;
                if (ifStmt.ElseIfClauses != null)
                    foreach (var clause in ifStmt.ElseIfClauses)
                        foreach (var nested in FindRunScripts(clause.Body)) yield return nested;
                if (ifStmt.ElseBody != null)
                    foreach (var nested in FindRunScripts(ifStmt.ElseBody)) yield return nested;
                break;
            case WhileStatement whileStmt:
                foreach (var nested in FindRunScripts(whileStmt.Body)) yield return nested;
                break;
            case ForStatement forStmt:
                foreach (var nested in FindRunScripts(forStmt.Body)) yield return nested;
                break;
            case ForeachStatement foreachStmt:
                foreach (var nested in FindRunScripts(foreachStmt.Body)) yield return nested;
                break;
            case TryCatchStatement tryCatch:
                foreach (var nested in FindRunScripts(tryCatch.TryBody)) yield return nested;
                foreach (var nested in FindRunScripts(tryCatch.CatchBody)) yield return nested;
                break;
            case ParallelStatement parallel:
                foreach (var nested in FindRunScripts(parallel.Body)) yield return nested;
                break;
            case ParallelForStatement parallelFor:
                foreach (var nested in FindRunScripts(parallelFor.Body)) yield return nested;
                break;
        }
    }
}
