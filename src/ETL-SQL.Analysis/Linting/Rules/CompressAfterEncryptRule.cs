using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Analysis.Linting.Rules;
/// <summary>
/// Warns when a COMPRESS FILE operation follows an ENCRYPT FILE operation
/// on the same file path. Compressing already-encrypted data is ineffective
/// because encryption maximises entropy, leaving no redundancy for
/// compression to exploit.  The correct order is: compress first, then encrypt.
/// </summary>
public class CompressAfterEncryptRule : ILintRule
{
    public string Name => "CompressAfterEncrypt";
    public string Description => "Warns when COMPRESS FILE follows ENCRYPT FILE on the same target, because compression is ineffective on encrypted data.";

    public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var results = new List<LintResult>();
        var encryptedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ProcessStatements(script.Statements, encryptedPaths, results);

        return Task.FromResult<IEnumerable<LintResult>>(results);
    }

    private void ProcessStatements(IEnumerable<Statement> statements, HashSet<string> encryptedPaths, List<LintResult> results)
    {
        foreach (var stmt in statements)
        {
            ProcessStatement(stmt, encryptedPaths, results);
        }
    }

    private void ProcessStatement(Statement stmt, HashSet<string> encryptedPaths, List<LintResult> results)
    {
        if (stmt is FileOperationStatement fileOp)
        {
            CheckFileOperation(fileOp, encryptedPaths, results);
            return;
        }

        // Recurse into blocks and control flow
        switch (stmt)
        {
            case BlockStatement block:
                ProcessStatements(block.Statements, encryptedPaths, results);
                break;
            case TryCatchStatement tc:
                ProcessStatement(tc.TryBody, encryptedPaths, results);
                ProcessStatement(tc.CatchBody, encryptedPaths, results);
                break;
            case IfStatement ifs:
                ProcessStatement(ifs.IfBody, encryptedPaths, results);
                if (ifs.ElseIfClauses != null)
                    foreach (var c in ifs.ElseIfClauses) ProcessStatement(c.Body, encryptedPaths, results);
                if (ifs.ElseBody != null) ProcessStatement(ifs.ElseBody, encryptedPaths, results);
                break;
            case WhileStatement ws:
                ProcessStatement(ws.Body, encryptedPaths, results);
                break;
            case ForStatement fs:
                ProcessStatement(fs.Body, encryptedPaths, results);
                break;
            case ForeachStatement fes:
                ProcessStatement(fes.Body, encryptedPaths, results);
                break;
            case ParallelStatement ps:
                ProcessStatement(ps.Body, encryptedPaths, results);
                break;
            case ParallelForStatement pfs:
                ProcessStatement(pfs.Body, encryptedPaths, results);
                break;
        }
    }

    private void CheckFileOperation(FileOperationStatement fileOp, HashSet<string> encryptedPaths, List<LintResult> results)
    {
        // Track which files have been encrypted
        if (fileOp.Type == FileOpType.Encrypt)
        {
            var path = GetLiteralPath(fileOp.Source);
            if (path != null) encryptedPaths.Add(path);

            // If a destination is specified, the encrypted output goes there
            var destPath = GetLiteralPath(fileOp.Destination);
            if (destPath != null) encryptedPaths.Add(destPath);
        }

        // If we see a COMPRESS FILE on a path that was previously encrypted, warn
        if (fileOp.Type == FileOpType.Compress)
        {
            var path = GetLiteralPath(fileOp.Source);
            if (path != null && encryptedPaths.Contains(path))
            {
                results.Add(new LintResult
                {
                    RuleName = Name,
                    Code = "PERF-COMPRESS-AFTER-ENCRYPT",
                    Severity = LintSeverity.Warning,
                    Message = $"COMPRESS FILE on '{path}' follows an ENCRYPT FILE on the same path. " +
                              "Compression is ineffective on encrypted data because encryption maximises entropy. " +
                              "Reverse the order: compress first, then encrypt.",
                    LineNumber = fileOp.Line,
                    ColumnNumber = fileOp.Column
                });
            }
        }
    }

    private static string? GetLiteralPath(Expression? expr)
    {
        return expr is LiteralExpression lit && lit.Value is string path
            ? path
            : null;
    }
}
