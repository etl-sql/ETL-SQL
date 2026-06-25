using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;

namespace ETL_SQL.Analysis.Linting.Rules;
/// <summary>
/// Discourages the use of relative paths in I/O operations.
/// Absolute paths are preferred for portability and security.
/// </summary>
public class AbsolutePathRule : ILintRule
{
    public string Name => "AbsolutePath";
    public string Description => "Encourages the use of absolute paths for file operations to ensure script portability and security.";

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
        if (statement is CreateConnectionStatement conn)
        {
            var typesRequiringAbsolute = new[] { "FILE", "FLATFILE", "EXCEL", "JSON", "XML", "PARQUET", "AVRO", "DIRECTORY" };
            if (conn.ConnectionType != null && typesRequiringAbsolute.Contains(conn.ConnectionType.ToUpperInvariant()))
            {
                CheckPathExpression(conn.TargetExpression, results);
            }
        }
        else if (statement is RunScriptStatement run)
        {
            CheckPathExpression(run.PathExpression, results);
        }
        else if (statement is FileOperationStatement fileOp)
        {
            CheckPathExpression(fileOp.Source, results);
            CheckPathExpression(fileOp.Destination, results);
        }
        else if (statement is DirectoryOperationStatement dirOp)
        {
            CheckPathExpression(dirOp.Path, results);
            CheckPathExpression(dirOp.Destination, results);
        }
        else if (statement is BulkInsertStatement bulk)
        {
            // FilePath is a raw string in BulkInsertStatement node
            CheckPathString(bulk.FilePath, bulk.Line, bulk.Column, results);
        }
        else if (statement is ExportReportStatement exportRpt)
        {
            CheckPathExpression(exportRpt.ReportPath, results);
            CheckPathExpression(exportRpt.OutputPath, results);
        }
        else if (statement is ExportStatement export)
        {
            // TargetPath is a raw string
            CheckPathString(export.TargetPath, export.Line, export.Column, results);
        }
        else if (statement is CreateSshKeyPairStatement ssh)
        {
            CheckPathExpression(ssh.Path, results);
        }
        else if (statement is EmailStatement email)
        {
            if (email.Attachments != null)
            {
                foreach (var attachment in email.Attachments)
                {
                    CheckPathExpression(attachment, results);
                }
            }
        }
        else if (statement is BlockStatement block)
        {
            foreach (var s in block.Statements) AnalyzeStatement(s, results);
        }
        else if (statement is IfStatement ifStmt)
        {
            AnalyzeStatement(ifStmt.IfBody, results);
            if (ifStmt.ElseIfClauses != null)
            {
                foreach (var ei in ifStmt.ElseIfClauses) AnalyzeStatement(ei.Body, results);
            }
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
        else if (statement is ParallelStatement parallel)
        {
            AnalyzeStatement(parallel.Body, results);
        }
        else if (statement is ParallelForStatement parallelFor)
        {
            AnalyzeStatement(parallelFor.Body, results);
        }
        else if (statement is TryCatchStatement tryCatch)
        {
            AnalyzeStatement(tryCatch.TryBody, results);
            AnalyzeStatement(tryCatch.CatchBody, results);
        }
    }

    private void CheckPathExpression(Expression? expr, List<LintResult> results)
    {
        if (expr is LiteralExpression literal && literal.Value is string path)
        {
            CheckPathString(path, literal.Line, literal.Column, results);
        }
    }

    private void CheckPathString(string path, int line, int col, List<LintResult> results)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (path.StartsWith("ENC:", StringComparison.OrdinalIgnoreCase)) return;
        if (path.Contains("://")) return; // Skip URLs (http, s3, etc)

        bool isAbsolute = false;

        // Windows style: C:\ or \\
        if (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
            isAbsolute = true;
        else if (path.StartsWith("\\\\") || path.StartsWith("//"))
            isAbsolute = true;
        // Unix style: /
        else if (path.StartsWith("/"))
            isAbsolute = true;

        if (!isAbsolute)
        {
            results.Add(new LintResult
            {
                RuleName = Name,
                Severity = LintSeverity.Warning,
                Message = $"Relative path detected: '{path}'. Use absolute paths to ensure script portability and avoid ambiguity.",
                LineNumber = line,
                ColumnNumber = col
            });
        }
    }
}
