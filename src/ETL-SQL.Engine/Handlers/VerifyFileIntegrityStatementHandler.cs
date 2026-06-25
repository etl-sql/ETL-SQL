using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the VERIFY FILE INTEGRITY statement.
/// </summary>
public class VerifyFileIntegrityStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(VerifyFileIntegrityStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (VerifyFileIntegrityStatement)statement;

        string srcVal = (await context.EvaluateValue(stmt.Source, new Row()))?.ToString() ?? "";
        string source = context.ResolvePath(srcVal);

        // Security check
        context.SecurityService.ValidatePath(source);

        if (!File.Exists(source))
            throw new ExecutionException($"Source file not found for integrity verification: {source}", null, stmt.Line, stmt.Column);

        string algo = "SHA256";
        if (stmt.Algorithm != null)
        {
            var aVal = (await context.EvaluateValue(stmt.Algorithm, new Row()))?.ToString() ?? "";
            if (!string.IsNullOrEmpty(aVal)) algo = aVal.ToUpperInvariant();
        }

        string expectedHash = "";
        if (stmt.ExpectedHash != null)
        {
            var ehVal = (await context.EvaluateValue(stmt.ExpectedHash, new Row()))?.ToString() ?? "";
            expectedHash = ehVal.Trim('\'', '\"').ToLowerInvariant();
        }
        else if (stmt.HashFile != null)
        {
            var hfVal = (await context.EvaluateValue(stmt.HashFile, new Row()))?.ToString() ?? "";
            string hashFile = context.ResolvePath(hfVal);

            context.SecurityService.ValidatePath(hashFile);
            if (!File.Exists(hashFile))
                throw new ExecutionException($"Hash file not found: {hashFile}", null, stmt.Line, stmt.Column);

            context.IncrementOperationCount(OperationType.FileSystem, hashFile, 1);
            string content = await File.ReadAllTextAsync(hashFile);

            // Extract first word (sha256sum outputs 'hash *filename')
            expectedHash = content.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                  .FirstOrDefault()?.Trim('\'', '\"')
                                  .ToLowerInvariant() ?? "";
        }

        if (string.IsNullOrEmpty(expectedHash))
            throw new ExecutionException("No expected hash value could be loaded for validation.", null, stmt.Line, stmt.Column);

        if (context.IsWhatIf)
        {
            context.Log($"WHAT IF: Would verify integrity of '{source}' using {algo} against expected hash '{expectedHash}'", ConsoleColor.Yellow);
            return;
        }

        context.IncrementOperationCount(OperationType.FileSystem, source, 1);

        if (context.IsVerbose)
            context.Log($"[VerifyIntegrity] Computing {algo} hash for '{source}'...");

        using var stream = File.OpenRead(source);
        byte[] hashBytes;
        if (algo == "MD5")
        {
            using var hasher = MD5.Create();
            hashBytes = hasher.ComputeHash(stream);
        }
        else if (algo == "SHA1" || algo == "SHA-1")
        {
            using var hasher = SHA1.Create();
            hashBytes = hasher.ComputeHash(stream);
        }
        else if (algo == "SHA256" || algo == "SHA-256" || algo == "SHA2_256")
        {
            using var hasher = SHA256.Create();
            hashBytes = hasher.ComputeHash(stream);
        }
        else if (algo == "SHA512" || algo == "SHA-512" || algo == "SHA2_512")
        {
            using var hasher = SHA512.Create();
            hashBytes = hasher.ComputeHash(stream);
        }
        else
        {
            throw new ExecutionException($"Unsupported hash algorithm: {algo}. Supported: MD5, SHA1, SHA256, SHA512.", null, stmt.Line, stmt.Column);
        }

        string actualHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        if (actualHash != expectedHash)
        {
            throw new ExecutionException($"File integrity check failed: Expected hash '{expectedHash}' but got '{actualHash}' for file '{source}'.", null, stmt.Line, stmt.Column);
        }

        if (context.IsVerbose || !context.InteractiveMode)
            context.Log($"[VerifyIntegrity] Success: File '{source}' hash matched expected value '{expectedHash}'.", ConsoleColor.Green);
    }
}
