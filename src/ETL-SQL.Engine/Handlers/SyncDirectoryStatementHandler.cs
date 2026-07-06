using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;
using ETL_SQL.Services;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the SYNC DIRECTORY statement.
/// </summary>
public class SyncDirectoryStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(SyncDirectoryStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (SyncDirectoryStatement)statement;

        string srcVal = (await context.EvaluateValue(stmt.Source, new Row()))?.ToString() ?? "";
        string destVal = (await context.EvaluateValue(stmt.Destination, new Row()))?.ToString() ?? "";

        string source = context.ResolvePath(srcVal);
        string dest = context.ResolvePath(destVal);
        var pathAuthorizer = new FileSystemPolicyAuthorizer(context.SecurityService);
        source = pathAuthorizer.Authorize(context, source, FileSystemAccessKind.Enumerate,
            validateFileType: false).CanonicalPath;
        dest = pathAuthorizer.Authorize(context, dest, FileSystemAccessKind.Write,
            validateFileType: false).CanonicalPath;

        // Security checks
        context.SecurityService.ValidatePath(source);
        context.SecurityService.ValidatePath(dest);
        context.SecurityService.ValidateWriteAccess(dest);

        if (context.IsWhatIf)
        {
            context.Log($"WHAT IF: Would sync directory '{source}' to '{dest}'", ConsoleColor.Yellow);
            return;
        }

        bool deleteExtra = false;
        if (stmt.DeleteExtra != null)
        {
            var de = await context.EvaluateValue(stmt.DeleteExtra, new Row());
            if (de != null)
            {
                if (de is bool b) deleteExtra = b;
                else if (string.Equals(de.ToString(), "ON", StringComparison.OrdinalIgnoreCase)) deleteExtra = true;
                else if (string.Equals(de.ToString(), "TRUE", StringComparison.OrdinalIgnoreCase)) deleteExtra = true;
            }
        }

        bool overwrite = true;
        if (stmt.Overwrite != null)
        {
            var ovr = await context.EvaluateValue(stmt.Overwrite, new Row());
            if (ovr != null)
            {
                if (ovr is bool b) overwrite = b;
                else if (string.Equals(ovr.ToString(), "OFF", StringComparison.OrdinalIgnoreCase)) overwrite = false;
                else if (string.Equals(ovr.ToString(), "FALSE", StringComparison.OrdinalIgnoreCase)) overwrite = false;
            }
        }

        bool recursive = false;
        if (stmt.Recursive != null)
        {
            var rec = await context.EvaluateValue(stmt.Recursive, new Row());
            if (rec != null)
            {
                if (rec is bool b) recursive = b;
                else if (string.Equals(rec.ToString(), "ON", StringComparison.OrdinalIgnoreCase)) recursive = true;
                else if (string.Equals(rec.ToString(), "TRUE", StringComparison.OrdinalIgnoreCase)) recursive = true;
            }
        }

        if (!Directory.Exists(source))
            throw new ExecutionException($"Source directory not found: {source}", null, stmt.Line, stmt.Column);

        if (!Directory.Exists(dest))
            Directory.CreateDirectory(dest);

        if (context.IsVerbose)
            context.Log($"[SyncDirectory] Syncing '{source}' -> '{dest}' (Recursive: {recursive}, Overwrite: {overwrite}, DeleteExtra: {deleteExtra})");

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var destFileMap = EnumerateFiles(context, pathAuthorizer, dest, recursive, searchOption).ToDictionary(
            f => Path.GetRelativePath(dest, f),
            f => f,
            StringComparer.OrdinalIgnoreCase
        );
        var sourceSeen = deleteExtra ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : null;

        // 1. Copy new or modified files
        foreach (var sourceFile in EnumerateFiles(context, pathAuthorizer, source, recursive, searchOption))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            string relativePath = Path.GetRelativePath(source, sourceFile);
            sourceSeen?.Add(relativePath);
            string targetFile = pathAuthorizer.Authorize(context, Path.Combine(dest, relativePath),
                FileSystemAccessKind.Write).CanonicalPath;

            bool needsCopy = false;
            if (!destFileMap.TryGetValue(relativePath, out var existingDestFile))
            {
                needsCopy = true;
            }
            else
            {
                var sourceInfo = new FileInfo(sourceFile);
                var destInfo = new FileInfo(existingDestFile);
                if (sourceInfo.Length != destInfo.Length || sourceInfo.LastWriteTime > destInfo.LastWriteTime)
                {
                    needsCopy = overwrite;
                }
            }

            if (needsCopy)
            {
                string? targetDir = Path.GetDirectoryName(targetFile);
                if (targetDir != null && !Directory.Exists(targetDir))
                    Directory.CreateDirectory(targetDir);

                context.SecurityService.ValidateWriteAccess(targetFile);
                context.SecurityService.ValidateFileType(targetFile);

                context.IncrementOperationCount(OperationType.FileSystem, sourceFile, 1);
                targetFile = pathAuthorizer.Authorize(context, targetFile,
                    FileSystemAccessKind.Write).CanonicalPath;
                File.Copy(sourceFile, targetFile, true);

                if (context.IsVerbose)
                    context.Log($"[SyncDirectory] Copied: {relativePath}");
            }
        }

        // 2. Delete extra files if configured
        if (deleteExtra)
        {
            foreach (var kvp in destFileMap)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                string relativePath = kvp.Key;
                string destFile = kvp.Value;

                if (sourceSeen == null || !sourceSeen.Contains(relativePath))
                {
                    destFile = pathAuthorizer.Authorize(context, destFile,
                        FileSystemAccessKind.Delete).CanonicalPath;
                    context.SecurityService.ValidateWriteAccess(destFile);
                    context.SecurityService.ValidateFileType(destFile);
                    context.IncrementOperationCount(OperationType.FileSystem, destFile, 1);
                    File.Delete(destFile);

                    if (context.IsVerbose)
                        context.Log($"[SyncDirectory] Deleted: {relativePath}");
                }
            }

            if (recursive)
            {
                DeleteEmptySubdirectories(context, pathAuthorizer, dest);
            }
        }
    }

    private static IEnumerable<string> EnumerateFiles(IExecutionContext context,
        FileSystemPolicyAuthorizer authorizer, string root, bool recursive, SearchOption searchOption)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", searchOption))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (recursive) ValidateRecursiveDepth(context, root, file);
            yield return authorizer.Authorize(context, file, FileSystemAccessKind.Read).CanonicalPath;
        }
    }

    private static void ValidateRecursiveDepth(IExecutionContext context, string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        var depth = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries).Length - 1;
        if (depth > context.MaxRecursiveDepth && !context.AllowDeepRecursion)
            throw new SecurityException($"Runaway protection: Recursive operation depth ({depth}) exceeds the safety limit of {context.MaxRecursiveDepth}. Use 'SET ALLOW_RECURSIVE_LAYERS = n;' override if allowed.");
    }

    private static void DeleteEmptySubdirectories(IExecutionContext context,
        FileSystemPolicyAuthorizer authorizer, string directory)
    {
        foreach (var d in Directory.GetDirectories(directory))
        {
            var authorizedDirectory = authorizer.Authorize(context, d, FileSystemAccessKind.Enumerate,
                validateFileType: false).CanonicalPath;
            DeleteEmptySubdirectories(context, authorizer, authorizedDirectory);
            if (Directory.GetFiles(authorizedDirectory).Length == 0
                && Directory.GetDirectories(authorizedDirectory).Length == 0)
            {
                var deleteTarget = authorizer.Authorize(context, authorizedDirectory,
                    FileSystemAccessKind.Delete, validateFileType: false).CanonicalPath;
                context.SecurityService.ValidateWriteAccess(deleteTarget);
                context.IncrementOperationCount(OperationType.FileSystem, deleteTarget, 1);
                Directory.Delete(deleteTarget, false);
            }
        }
    }
}
