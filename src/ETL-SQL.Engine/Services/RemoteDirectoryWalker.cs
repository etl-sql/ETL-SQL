using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using ETL_SQL.Common;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Services;

internal readonly record struct RemoteWalkEntry(
    FileMetaData Meta,
    string RelativePath,
    int Depth);

/// <summary>
/// Walks a remote filesystem tree with runaway-recursion depth enforcement
/// (<c>SET ALLOW_RECURSIVE_LAYERS</c>, default 5) and symlink-loop protection.
/// Symlinked folders are never descended into.
/// </summary>
internal static class RemoteDirectoryWalker
{
    internal static string NormalizeRemoteDir(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var normalized = path.Replace('\\', '/');
        return normalized.Length > 1 ? normalized.TrimEnd('/') : normalized;
    }

    internal static string CombineRemotePath(string parent, string child)
    {
        if (string.IsNullOrEmpty(parent)) return child.TrimStart('/');
        if (parent == "/") return "/" + child.TrimStart('/');
        return parent.TrimEnd('/') + "/" + child.TrimStart('/');
    }

    internal static async IAsyncEnumerable<RemoteWalkEntry> WalkAsync(
        IRemoteFileSystem remoteFs,
        string rootPath,
        bool recursive,
        IExecutionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var normalizedRoot = NormalizeRemoteDir(rootPath);
        var visitedDirs = new HashSet<string>(StringComparer.Ordinal)
        {
            normalizedRoot
        };

        await foreach (var entry in WalkDirectoryCoreAsync(
            remoteFs,
            currentDir: rootPath,
            relativePrefix: "",
            depth: 0,
            recursive: recursive,
            visitedDirs: visitedDirs,
            context: context,
            cancellationToken: cancellationToken))
        {
            yield return entry;
        }
    }

    private static async IAsyncEnumerable<RemoteWalkEntry> WalkDirectoryCoreAsync(
        IRemoteFileSystem remoteFs,
        string currentDir,
        string relativePrefix,
        int depth,
        bool recursive,
        HashSet<string> visitedDirs,
        IExecutionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entries = new List<FileMetaData>();
        await foreach (var item in remoteFs.ListFilesAsync(currentDir).WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Name is "." or "..") continue;
            entries.Add(item);
        }

        foreach (var item in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string relPath;
            if (string.IsNullOrEmpty(relativePrefix))
            {
                relPath = item.Name;
            }
            else
            {
                relPath = relativePrefix + "/" + item.Name;
            }

            // Object stores (S3 / Azure Blob) return flat keys without a leading '/' containing
            // nested '/' segments even at depth 0. Enforce the recursive depth bound against those
            // nested segments without misclassifying absolute POSIX SFTP/FTP paths.
            if (recursive && depth == 0 && !item.IsDirectory
                && !string.IsNullOrEmpty(item.FullPath)
                && !item.FullPath.StartsWith('/')
                && !item.FullPath.StartsWith('\\'))
            {
                var normalizedRoot = NormalizeRemoteDir(currentDir).TrimStart('/');
                var normalizedFull = item.FullPath.Replace('\\', '/');
                string keyRel = !string.IsNullOrEmpty(normalizedRoot) && normalizedFull.StartsWith(normalizedRoot + "/", StringComparison.Ordinal)
                    ? normalizedFull[(normalizedRoot.Length + 1)..]
                    : normalizedFull;

                int slashDepth = keyRel.Count(c => c == '/');
                if (slashDepth > 0)
                {
                    EnforceDepthLimit(context, slashDepth, item.FullPath);
                    relPath = keyRel;
                }
            }

            yield return new RemoteWalkEntry(item, relPath, depth);

            if (!recursive || !item.IsDirectory)
                continue;

            // Never follow symlinked folders (prevents loops and escapes).
            if (item.IsSymlink == true)
                continue;

            string childPath = string.IsNullOrEmpty(item.FullPath)
                ? CombineRemotePath(currentDir, item.Name)
                : item.FullPath;
            string normalizedChild = NormalizeRemoteDir(childPath);

            // Cycle defense even when a server does not flag IsSymlink.
            if (!visitedDirs.Add(normalizedChild))
                continue;

            int nextDepth = depth + 1;
            EnforceDepthLimit(context, nextDepth, childPath);

            await foreach (var child in WalkDirectoryCoreAsync(
                remoteFs,
                currentDir: childPath,
                relativePrefix: relPath,
                depth: nextDepth,
                recursive: true,
                visitedDirs: visitedDirs,
                context: context,
                cancellationToken: cancellationToken))
            {
                yield return child;
            }
        }
    }

    private static void EnforceDepthLimit(IExecutionContext context, int depth, string remotePath)
    {
        context.SecurityService.CheckRunawayProtection(
            OperationType.FileSystem,
            0,
            depth,
            allowLargeCount: context.AllowLargeFileOperationCount,
            allowDeepRecursion: context.AllowDeepRecursion,
            path: remotePath);

        if (context.ExecutionPolicy?.IsEnrolled ?? EnterprisePolicyRuntime.Current.IsEnrolled)
        {
            OperationPolicyBoundary.EnforceCeiling(
                context,
                "Security:MaxRecursiveNestingDepth",
                depth,
                "<recursive-nesting-depth>");
        }
    }
}
