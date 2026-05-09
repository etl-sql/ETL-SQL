using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;

namespace ETL_SQL.Analysis.Linting.Rules
{
    /// <summary>
    /// Enforces Zero-Trust security by warning against access to system directories
    /// and direct operations on drive roots.
    /// </summary>
    public class FileSystemSecurityRule : ILintRule
    {
        public string Name => "FileSystemSecurity";
        public string Description => "Enforces security guardrails by discouraging access to system directories and drive roots.";

        private static readonly string[] ForbiddenFolders = {
            "C:\\WINDOWS", "C:\\BIN", "\\ETC", "\\ROOT", ".GIT", ".SSH"
        };

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
                CheckPathExpression(conn.TargetExpression, results);
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
                CheckPathString(bulk.FilePath, bulk.Line, bulk.Column, results);
            }
            else if (statement is ExportReportStatement exportRpt)
            {
                CheckPathExpression(exportRpt.ReportPath, results);
                CheckPathExpression(exportRpt.OutputPath, results);
            }
            else if (statement is ExportStatement export)
            {
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
                    foreach (var attachment in email.Attachments) CheckPathExpression(attachment, results);
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
            if (path.Contains("://")) return; 

            string upperPath = path.ToUpperInvariant().Replace('/', '\\');

            // 1. Root access check (e.g. C:\ or D:\)
            if (upperPath.Length == 3 && char.IsLetter(upperPath[0]) && upperPath.EndsWith(":\\"))
            {
                AddSecurityWarning($"Direct operation on drive root '{path}' is discouraged.", line, col, results);
            }

            // 2. System directory check
            foreach (var forbidden in ForbiddenFolders)
            {
                if (upperPath.StartsWith(forbidden))
                {
                    AddSecurityWarning($"Access to system directory '{path}' is restricted for security reasons.", line, col, results);
                    break;
                }
            }
        }

        private void AddSecurityWarning(string message, int line, int col, List<LintResult> results)
        {
            results.Add(new LintResult
            {
                RuleName = Name,
                Severity = LintSeverity.Warning,
                Message = message,
                LineNumber = line,
                ColumnNumber = col
            });
        }
    }
}
