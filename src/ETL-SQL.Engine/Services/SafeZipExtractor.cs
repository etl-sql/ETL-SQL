using System.IO.Compression;
using ETL_SQL.Core;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Engine.Services;

/// <summary>Extracts ZIP entries only after each final target passes the shared path authorizer.</summary>
public static class SafeZipExtractor
{
    public static void Extract(
        string archivePath,
        string destinationDirectory,
        bool overwrite,
        IExecutionContext context,
        FileSystemPolicyAuthorizer authorizer)
    {
        var destination = authorizer.Authorize(context, destinationDirectory,
            FileSystemAccessKind.Extract, validateFileType: false).CanonicalPath;
        Directory.CreateDirectory(destination);

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            context.IncrementOperationCount(OperationType.FileSystem, destination);
            RejectUnsafeEntry(entry);

            var relative = entry.FullName
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(destination, relative));
            if (!SafePath.TryResolveWithinRoot(destination, target, out target))
                throw Denied(context, entry.FullName, "Archive entry escapes the extraction root.");

            var isDirectory = entry.FullName.EndsWith("/", StringComparison.Ordinal)
                || entry.FullName.EndsWith('\\');
            var authorized = authorizer.Authorize(context, target, FileSystemAccessKind.Extract,
                validateFileType: !isDirectory).CanonicalPath;
            if (isDirectory)
            {
                Directory.CreateDirectory(authorized);
                continue;
            }

            var parent = Path.GetDirectoryName(authorized);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            // Re-check immediately before mutation to narrow the check/use window.
            authorized = authorizer.Authorize(context, authorized, FileSystemAccessKind.Extract).CanonicalPath;
            entry.ExtractToFile(authorized, overwrite);
        }
    }

    private static void RejectUnsafeEntry(ZipArchiveEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.FullName)
            || Path.IsPathRooted(entry.FullName)
            || entry.FullName.Contains(':', StringComparison.Ordinal)
            || IsSymbolicLink(entry))
        {
            throw new InvalidDataException("ZIP archive contains an unsafe entry name or link.");
        }
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry) =>
        ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;

    private static FileSystemPolicyDeniedException Denied(
        IExecutionContext context,
        string entry,
        string reason)
    {
        var snapshot = context.ExecutionPolicy ?? ExecutionPolicySnapshot.Capture(
            EnterprisePolicyRuntime.Current, ETL_SQL.Core.Common.ProcessActor.Current,
            ScriptExecutionMode.Batch, "unknown");
        return new FileSystemPolicyDeniedException(OperationPolicyDecision.Deny(snapshot,
            "Filesystem:ArchiveExtraction", $"<archive-entry>/{Path.GetFileName(entry)}",
            "entry must remain inside authorized extraction root", reason));
    }
}
