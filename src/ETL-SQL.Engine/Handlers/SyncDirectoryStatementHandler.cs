using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;
using ETL_SQL.Engine.Services;
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

        if (!string.IsNullOrEmpty(stmt.SourceConnectionName) || !string.IsNullOrEmpty(stmt.DestinationConnectionName))
        {
            await ExecuteRemoteSyncAsync(stmt, context, srcVal, destVal, deleteExtra, overwrite, recursive);
            return;
        }

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

        if (!Directory.Exists(source))
            throw new ExecutionException($"Source directory not found: {source}", null, stmt.Line, stmt.Column);

        if (!Directory.Exists(dest))
            Directory.CreateDirectory(dest);

        if (context.IsVerbose)
            context.Log($"[SyncDirectory] Syncing '{source}' -> '{dest}' (Recursive: {recursive}, Overwrite: {overwrite}, DeleteExtra: {deleteExtra})");

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var destFileMap = EnumerateFiles(context, pathAuthorizer, dest, recursive, searchOption).ToDictionary(
            f => f.RelativePath,
            f => f.CanonicalPath,
            StringComparer.OrdinalIgnoreCase
        );
        var sourceSeen = deleteExtra ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : null;

        // 1. Copy new or modified files
        foreach (var (relativePath, sourceFile) in EnumerateFiles(context, pathAuthorizer, source, recursive, searchOption))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            sourceSeen?.Add(relativePath);
            string targetFile = pathAuthorizer.Authorize(context, Path.Combine(dest, relativePath),
                FileSystemAccessKind.Write).CanonicalPath;
            RequireWithinDestination(dest, targetFile);

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
                var sourceAuth = pathAuthorizer.Authorize(context, sourceFile,
                    FileSystemAccessKind.Read);
                var targetAuth = pathAuthorizer.Authorize(context, targetFile,
                    FileSystemAccessKind.Write);
                if (File.Exists(targetAuth.CanonicalPath))
                {
                    var deleteTarget = pathAuthorizer.Authorize(context, targetAuth.CanonicalPath,
                        FileSystemAccessKind.Delete);
                    context.SecurityService.ValidateWriteAccess(deleteTarget.CanonicalPath);
                    context.SecurityService.ValidateFileType(deleteTarget.CanonicalPath);
                    pathAuthorizer.DeleteValidatedFile(context, deleteTarget);
                }

                await using (var sourceStream = pathAuthorizer.OpenValidatedRead(context, sourceAuth))
                await using (var targetStream = pathAuthorizer.OpenValidatedWrite(context, targetAuth,
                    failIfExists: true))
                {
                    await sourceStream.CopyToAsync(targetStream, context.CancellationToken);
                }

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
                    RequireWithinDestination(dest, destFile);
                    context.SecurityService.ValidateWriteAccess(destFile);
                    context.SecurityService.ValidateFileType(destFile);
                    context.IncrementOperationCount(OperationType.FileSystem, destFile, 1);
                    pathAuthorizer.DeleteValidatedFile(context, pathAuthorizer.Authorize(context, destFile,
                        FileSystemAccessKind.Delete));

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

    private static readonly TimeSpan TimestampTolerance = TimeSpan.FromSeconds(2);

    private static bool IsSourceNewerOrDifferentSize(long srcSize, DateTime? srcModUtc, long dstSize, DateTime? dstModUtc)
    {
        if (srcSize != dstSize) return true;
        if (srcModUtc.HasValue && dstModUtc.HasValue)
            return srcModUtc.Value > dstModUtc.Value + TimestampTolerance;
        return false;
    }

    private async Task ExecuteRemoteSyncAsync(
        SyncDirectoryStatement stmt,
        IExecutionContext context,
        string srcVal,
        string destVal,
        bool deleteExtra,
        bool overwrite,
        bool recursive)
    {
        bool srcRemote = !string.IsNullOrEmpty(stmt.SourceConnectionName);
        bool dstRemote = !string.IsNullOrEmpty(stmt.DestinationConnectionName);
        if (srcRemote && dstRemote)
            throw new ExecutionException("Remote-to-remote SYNC DIRECTORY is not supported; sync local-to-remote or remote-to-local.", null, stmt.Line, stmt.Column);

        var connName = srcRemote ? stmt.SourceConnectionName! : stmt.DestinationConnectionName!;
        if (!context.Connections.TryGetValue(connName, out var ds) || ds is not IRemoteFileSystem remoteFs)
            throw new ExecutionException($"Connection '{connName}' not found or does not support remote directory sync.", null, stmt.Line, stmt.Column);

        var pathAuthorizer = new FileSystemPolicyAuthorizer(context.SecurityService);

        if (dstRemote)
        {
            // Local -> Remote
            string localSource = pathAuthorizer.Authorize(context, context.ResolvePath(srcVal),
                FileSystemAccessKind.Enumerate, validateFileType: false).CanonicalPath;
            if (!Directory.Exists(localSource))
                throw new ExecutionException($"Source directory not found: {localSource}", null, stmt.Line, stmt.Column);

            var remoteFiles = new Dictionary<string, FileMetaData>(StringComparer.OrdinalIgnoreCase);
            bool remoteDirExists = await remoteFs.DirectoryExistsAsync(destVal);
            if (remoteDirExists)
            {
                await foreach (var entry in RemoteDirectoryWalker.WalkAsync(remoteFs, destVal, recursive, context, context.CancellationToken))
                {
                    if (entry.Meta.IsDirectory || entry.Meta.IsSymlink == true) continue;
                    remoteFiles[entry.RelativePath.Replace('\\', '/')] = entry.Meta;
                }
            }

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var localFiles = EnumerateFiles(context, pathAuthorizer, localSource, recursive, searchOption)
                .Select(f => (Rel: f.RelativePath.Replace('\\', '/'), LocalPath: f.CanonicalPath))
                .ToList();

            var toCopy = new List<(string LocalPath, string RemotePath, string Rel)>();
            var sourceSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (rel, localFile) in localFiles)
            {
                sourceSeen.Add(rel);
                var fi = new FileInfo(localFile);
                bool needsCopy;
                if (!remoteFiles.TryGetValue(rel, out var existing))
                {
                    needsCopy = true;
                }
                else
                {
                    var remoteModUtc = existing.LastModified?.ToUniversalTime();
                    needsCopy = overwrite && IsSourceNewerOrDifferentSize(fi.Length, fi.LastWriteTimeUtc, existing.Size, remoteModUtc);
                }

                if (needsCopy)
                    toCopy.Add((localFile, RemoteDirectoryWalker.CombineRemotePath(destVal, rel), rel));
            }

            var toDelete = new List<string>();
            if (deleteExtra)
            {
                foreach (var kvp in remoteFiles)
                {
                    if (!sourceSeen.Contains(kvp.Key))
                        toDelete.Add(kvp.Value.FullPath);
                }
            }

            if (context.IsWhatIf)
            {
                foreach (var item in toCopy)
                    context.Log($"WHAT IF: Would copy '{item.LocalPath}' -> '{connName}:{item.RemotePath}'", ConsoleColor.Yellow);
                foreach (var del in toDelete)
                    context.Log($"WHAT IF: Would delete extra '{connName}:{del}'", ConsoleColor.Yellow);
                context.Log($"WHAT IF: SYNC DIRECTORY plan ({localSource} -> {connName}:{destVal}): {toCopy.Count} to copy, {toDelete.Count} to delete.", ConsoleColor.Yellow);
                return;
            }

            if (!remoteDirExists)
                await remoteFs.CreateDirectoryAsync(destVal);

            var createdRemoteDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in toCopy)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var authLocal = pathAuthorizer.Authorize(context, item.LocalPath, FileSystemAccessKind.Read).CanonicalPath;
                context.SecurityService.ValidateFileType(authLocal);
                context.IncrementOperationCount(OperationType.FileSystem, authLocal, 1);

                var slashIdx = item.Rel.LastIndexOf('/');
                if (slashIdx > 0)
                {
                    var parts = item.Rel[..slashIdx].Split('/');
                    var curRemote = destVal;
                    foreach (var p in parts)
                    {
                        curRemote = RemoteDirectoryWalker.CombineRemotePath(curRemote, p);
                        if (createdRemoteDirs.Add(curRemote) && !await remoteFs.DirectoryExistsAsync(curRemote))
                            await remoteFs.CreateDirectoryAsync(curRemote);
                    }
                }

                await remoteFs.UploadFileAsync(authLocal, item.RemotePath, overwrite: true, context.CancellationToken);
            }

            foreach (var del in toDelete)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                context.IncrementOperationCount(OperationType.FileSystem, del, 1);
                await remoteFs.DeleteFileAsync(del);
            }

            context.Log($"Synced local '{localSource}' -> '{connName}:{destVal}': {toCopy.Count} copied, {toDelete.Count} deleted.", ConsoleColor.Green);
        }
        else
        {
            // Remote -> Local
            string localDest = pathAuthorizer.Authorize(context, context.ResolvePath(destVal),
                FileSystemAccessKind.Write, validateFileType: false).CanonicalPath;
            context.SecurityService.ValidateWriteAccess(localDest);

            // A missing or mistyped remote source lists nothing; with DELETE_EXTRA = ON that would
            // empty the local destination. Fail instead, as the local-source branch does.
            if (!await remoteFs.DirectoryExistsAsync(srcVal))
                throw new ExecutionException($"Source directory not found: {connName}:{srcVal}", null, stmt.Line, stmt.Column);

            var remoteFiles = new List<(string Rel, FileMetaData Meta)>();
            await foreach (var entry in RemoteDirectoryWalker.WalkAsync(remoteFs, srcVal, recursive, context, context.CancellationToken))
            {
                if (entry.Meta.IsDirectory || entry.Meta.IsSymlink == true) continue;
                remoteFiles.Add((entry.RelativePath.Replace('\\', '/'), entry.Meta));
            }

            var localFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(localDest))
            {
                var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                foreach (var (rel, canonical) in EnumerateFiles(context, pathAuthorizer, localDest, recursive, searchOption))
                {
                    localFiles[rel.Replace('\\', '/')] = canonical;
                }
            }

            var toCopy = new List<(string RemotePath, string LocalPath)>();
            var sourceSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (rel, meta) in remoteFiles)
            {
                sourceSeen.Add(rel);
                string targetFile = pathAuthorizer.Authorize(context,
                    Path.Combine(localDest, rel.Replace('/', Path.DirectorySeparatorChar)),
                    FileSystemAccessKind.Write).CanonicalPath;
                RequireWithinDestination(localDest, targetFile);
                context.SecurityService.ValidateWriteAccess(targetFile);
                context.SecurityService.ValidateFileType(targetFile);

                bool needsCopy;
                if (!localFiles.TryGetValue(rel, out var existingLocal))
                {
                    needsCopy = true;
                }
                else
                {
                    var fi = new FileInfo(existingLocal);
                    var remoteModUtc = meta.LastModified?.ToUniversalTime();
                    needsCopy = overwrite && IsSourceNewerOrDifferentSize(meta.Size, remoteModUtc, fi.Length, fi.LastWriteTimeUtc);
                }

                if (needsCopy)
                    toCopy.Add((meta.FullPath, targetFile));
            }

            var toDelete = new List<string>();
            if (deleteExtra)
            {
                foreach (var kvp in localFiles)
                {
                    if (!sourceSeen.Contains(kvp.Key))
                    {
                        var delAuth = pathAuthorizer.Authorize(context, kvp.Value, FileSystemAccessKind.Delete);
                        RequireWithinDestination(localDest, delAuth.CanonicalPath);
                        context.SecurityService.ValidateWriteAccess(delAuth.CanonicalPath);
                        context.SecurityService.ValidateFileType(delAuth.CanonicalPath);
                        toDelete.Add(delAuth.CanonicalPath);
                    }
                }
            }

            if (context.IsWhatIf)
            {
                foreach (var item in toCopy)
                    context.Log($"WHAT IF: Would copy '{connName}:{item.RemotePath}' -> '{item.LocalPath}'", ConsoleColor.Yellow);
                foreach (var del in toDelete)
                    context.Log($"WHAT IF: Would delete extra '{del}'", ConsoleColor.Yellow);
                context.Log($"WHAT IF: SYNC DIRECTORY plan ({connName}:{srcVal} -> {localDest}): {toCopy.Count} to copy, {toDelete.Count} to delete.", ConsoleColor.Yellow);
                return;
            }

            if (!Directory.Exists(localDest))
                Directory.CreateDirectory(localDest);

            foreach (var item in toCopy)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                context.IncrementOperationCount(OperationType.FileSystem, item.LocalPath, 1);
                var parent = Path.GetDirectoryName(item.LocalPath);
                if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                    Directory.CreateDirectory(parent);
                await remoteFs.DownloadFileAsync(item.RemotePath, item.LocalPath, overwrite: true);
            }

            foreach (var del in toDelete)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                context.IncrementOperationCount(OperationType.FileSystem, del, 1);
                pathAuthorizer.DeleteValidatedFile(context, pathAuthorizer.Authorize(context, del, FileSystemAccessKind.Delete));
            }

            context.Log($"Synced '{connName}:{srcVal}' -> local '{localDest}': {toCopy.Count} copied, {toDelete.Count} deleted.", ConsoleColor.Green);
        }
    }

    /// <summary>
    /// Enumerates files under <paramref name="root"/>. The relative path is taken from the
    /// enumerated (lexical) path, never from the canonical one: a symlink or junction inside the
    /// tree canonicalizes outside it, and a relative path computed from that target would start
    /// with <c>..</c> and escape the other side of the sync.
    /// </summary>
    private static IEnumerable<(string RelativePath, string CanonicalPath)> EnumerateFiles(
        IExecutionContext context, FileSystemPolicyAuthorizer authorizer, string root, bool recursive,
        SearchOption searchOption)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", searchOption))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (recursive) ValidateRecursiveDepth(context, root, file);
            var canonical = authorizer.Authorize(context, file, FileSystemAccessKind.Read).CanonicalPath;
            yield return (Path.GetRelativePath(root, file), canonical);
        }
    }

    /// <summary>Refuses a write or delete whose canonical target is outside the destination.</summary>
    private static void RequireWithinDestination(string dest, string canonicalTarget)
    {
        if (!SafePath.IsWithinRoot(dest, canonicalTarget))
            throw new SecurityException(
                $"SYNC DIRECTORY refused '{canonicalTarget}': it resolves outside the destination directory '{dest}'.");
    }

    private static void ValidateRecursiveDepth(IExecutionContext context, string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        var depth = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries).Length - 1;
        // Directory depth is nesting, governed by Security:MaxRecursiveNestingDepth and lifted only
        // by ALLOW_RECURSIVE_LAYERS inside an approved safe zone; not the CTE iteration limit.
        context.SecurityService.CheckRunawayProtection(
            OperationType.FileSystem, 0, depth, allowDeepRecursion: context.AllowDeepRecursion, path: file);
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
                    FileSystemAccessKind.Delete, validateFileType: false);
                context.SecurityService.ValidateWriteAccess(deleteTarget.CanonicalPath);
                context.IncrementOperationCount(OperationType.FileSystem, deleteTarget.CanonicalPath, 1);
                authorizer.DeleteValidatedDirectory(context, deleteTarget, recursive: false);
            }
        }
    }
}
