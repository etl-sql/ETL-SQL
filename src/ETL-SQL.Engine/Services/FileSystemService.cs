using System;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Engine.Services;
/// <summary>
/// Centralized service for secure file and directory operations.
/// Integrates security guardrails, operation counting, and recursion depth checks.
/// </summary>
public class FileSystemService(ILogger logger)
{
    private readonly ILogger _logger = logger;

    public async Task CopyDirectory(string sourceDir, string destinationDir, bool overwrite, IExecutionContext context)
    {
        var authorizer = new FileSystemPolicyAuthorizer(context.SecurityService);
        sourceDir = authorizer.Authorize(context, sourceDir, FileSystemAccessKind.Enumerate,
            validateFileType: false).CanonicalPath;
        destinationDir = authorizer.Authorize(context, destinationDir, FileSystemAccessKind.Write,
            validateFileType: false).CanonicalPath;
        context.IncrementOperationCount(OperationType.FileSystem, sourceDir);
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");

        Directory.CreateDirectory(destinationDir);

        foreach (FileInfo file in dir.GetFiles())
        {
            context.IncrementOperationCount(OperationType.FileSystem, file.FullName);
            var sourceFile = authorizer.Authorize(context, file.FullName,
                FileSystemAccessKind.Read).CanonicalPath;
            string targetFilePath = authorizer.Authorize(context,
                Path.Combine(destinationDir, file.Name), FileSystemAccessKind.Write).CanonicalPath;
            await using (var sourceStream = File.OpenRead(sourceFile))
            await using (var destStream = new FileStream(targetFilePath, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await sourceStream.CopyToAsync(destStream);
            }
        }

        foreach (DirectoryInfo subDir in dir.GetDirectories())
        {
            using (context.EnterRecursiveScope())
            {
                string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                await CopyDirectory(subDir.FullName, newDestinationDir, overwrite, context);
            }
        }
    }

    public async Task DeleteDirectoryContents(string path, bool recursive, IExecutionContext context)
    {
        var authorizer = new FileSystemPolicyAuthorizer(context.SecurityService);
        path = authorizer.Authorize(context, path, FileSystemAccessKind.Enumerate,
            validateFileType: false).CanonicalPath;
        context.IncrementOperationCount(OperationType.FileSystem, path);
        var dir = new DirectoryInfo(path);
        if (!dir.Exists) return;

        foreach (FileInfo file in dir.GetFiles())
        {
            context.IncrementOperationCount(OperationType.FileSystem, file.FullName);
            var authorized = authorizer.Authorize(context, file.FullName,
                FileSystemAccessKind.Delete).CanonicalPath;
            await Task.Run(() => File.Delete(authorized));
        }

        foreach (DirectoryInfo subDir in dir.GetDirectories())
        {
            if (recursive)
            {
                using (context.EnterRecursiveScope())
                {
                    await DeleteDirectoryContents(subDir.FullName, true, context);
                }
            }
            context.IncrementOperationCount(OperationType.FileSystem, subDir.FullName);
            var authorized = authorizer.Authorize(context, subDir.FullName,
                FileSystemAccessKind.Delete, validateFileType: false).CanonicalPath;
            await Task.Run(() => Directory.Delete(authorized, recursive));
        }
    }

    public async Task EncryptDirectory(string sourceDir, string destDir, string password, bool overwrite, IExecutionContext context)
    {
        var authorizer = new FileSystemPolicyAuthorizer(context.SecurityService);
        sourceDir = authorizer.Authorize(context, sourceDir, FileSystemAccessKind.Enumerate,
            validateFileType: false).CanonicalPath;
        destDir = authorizer.Authorize(context, destDir, FileSystemAccessKind.Write,
            validateFileType: false).CanonicalPath;
        context.IncrementOperationCount(OperationType.FileSystem, sourceDir);
        if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            context.IncrementOperationCount(OperationType.FileSystem, file);
            var sourceFile = authorizer.Authorize(context, file, FileSystemAccessKind.Read).CanonicalPath;
            string destFile = authorizer.Authorize(context,
                Path.Combine(destDir, Path.GetFileName(file) + ".enc"),
                FileSystemAccessKind.Write).CanonicalPath;
            await Task.Run(() => CryptoUtils.EncryptFile(sourceFile, destFile, password, overwrite));
        }
        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            using (context.EnterRecursiveScope())
            {
                await EncryptDirectory(subDir, Path.Combine(destDir, Path.GetFileName(subDir)), password, overwrite, context);
            }
        }
    }

    public async Task DecryptDirectory(string sourceDir, string destDir, string password, bool overwrite, IExecutionContext context)
    {
        var authorizer = new FileSystemPolicyAuthorizer(context.SecurityService);
        sourceDir = authorizer.Authorize(context, sourceDir, FileSystemAccessKind.Enumerate,
            validateFileType: false).CanonicalPath;
        destDir = authorizer.Authorize(context, destDir, FileSystemAccessKind.Write,
            validateFileType: false).CanonicalPath;
        context.IncrementOperationCount(OperationType.FileSystem, sourceDir);
        if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            if (!file.EndsWith(".enc")) continue;
            context.IncrementOperationCount(OperationType.FileSystem, file);
            var sourceFile = authorizer.Authorize(context, file, FileSystemAccessKind.Read).CanonicalPath;
            string destFile = authorizer.Authorize(context,
                Path.Combine(destDir, Path.GetFileNameWithoutExtension(file)),
                FileSystemAccessKind.Write).CanonicalPath;
            await Task.Run(() => CryptoUtils.DecryptFile(sourceFile, destFile, password, overwrite));
        }
        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            using (context.EnterRecursiveScope())
            {
                await DecryptDirectory(subDir, Path.Combine(destDir, Path.GetFileName(subDir)), password, overwrite, context);
            }
        }
    }
}
