using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;
using ETL_SQL.Engine.Services;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles SEND and RECEIVE statements for transferring files between local and remote systems.
/// </summary>
public class FileTransferStatementHandler : IStatementHandler
{
    private readonly ILogger _logger;
    public Type SupportedStatementType => typeof(FileTransferStatement);

    public FileTransferStatementHandler(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>Executes the file transfer, resolving paths and invoking the remote filesystem provider.</summary>
    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (FileTransferStatement)statement;

        string localPathVal = (await context.EvaluateValue(stmt.LocalPath, new Row()))?.ToString() ?? "";
        string localPath = context.ResolvePath(localPathVal);
        string remotePath = (await context.EvaluateValue(stmt.RemotePath, new Row()))?.ToString() ?? "";
        var pathAuthorizer = new FileSystemPolicyAuthorizer(context.SecurityService);

        bool overwrite = true;
        if (stmt.Overwrite != null)
        {
            var ovVal = await context.EvaluateValue(stmt.Overwrite, new Row());
            if (ovVal is bool b) overwrite = b;
            else if (ovVal != null) overwrite = ovVal.ToString()?.ToUpperInvariant() == "ON" || ovVal.ToString()?.ToUpperInvariant() == "TRUE";
        }

        bool recursive = false;
        if (stmt.Recursive != null)
        {
            var recVal = await context.EvaluateValue(stmt.Recursive, new Row());
            if (recVal is bool rb) recursive = rb;
            else if (recVal != null)
            {
                var s = recVal.ToString()?.Trim();
                recursive = string.Equals(s, "ON", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s, "TRUE", StringComparison.OrdinalIgnoreCase)
                    || s == "1";
            }
        }

        bool verify = false;
        if (stmt.Verify != null)
        {
            var verVal = await context.EvaluateValue(stmt.Verify, new Row());
            if (verVal is bool vb) verify = vb;
            else if (verVal != null)
            {
                var s = verVal.ToString()?.Trim();
                verify = string.Equals(s, "ON", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s, "TRUE", StringComparison.OrdinalIgnoreCase)
                    || s == "1";
            }
        }

        if (!context.Connections.TryGetValue(stmt.ConnectionName, out var ds) || ds is not IRemoteFileSystem remoteFs)
        {
            throw new ExecutionException($"Connection '{stmt.ConnectionName}' not found or does not support remote file transfer.");
        }

        if (stmt.Type == FileTransferType.Send)
        {
            bool hasWildcard = localPath.Contains('*') || localPath.Contains('?');

            if (hasWildcard)
            {
                string dir = Path.GetDirectoryName(localPath) ?? Directory.GetCurrentDirectory();
                string pattern = Path.GetFileName(localPath);
                dir = pathAuthorizer.Authorize(context, dir, FileSystemAccessKind.Enumerate,
                    validateFileType: false).CanonicalPath;

                if (!Directory.Exists(dir))
                {
                    throw new ExecutionException($"Local directory not found: {dir}");
                }

                var localFiles = Directory.GetFiles(dir, pattern);
                if (localFiles.Length == 0)
                {
                    _logger.WriteLine($"No files found matching pattern: {localPath}", ConsoleColor.Yellow);
                    return;
                }

                _logger.WriteLine($"SENDING wildcard matches from {localPath} (Count: {localFiles.Length}) to {stmt.ConnectionName}:{remotePath} (OVERWRITE={(overwrite ? "ON" : "OFF")})", ConsoleColor.Cyan);

                foreach (var localFile in localFiles)
                {
                    var authorizedLocalFile = pathAuthorizer.Authorize(context, localFile,
                        FileSystemAccessKind.Read).CanonicalPath;
                    // Security Hardening: Check read/write rules
                    context.SecurityService.ValidateFileType(localFile);

                    string remoteFile = remotePath;
                    if (remotePath.EndsWith("/") || remotePath.EndsWith("\\") || string.IsNullOrEmpty(remotePath))
                    {
                        remoteFile = remotePath + Path.GetFileName(authorizedLocalFile);
                    }
                    else
                    {
                        remoteFile = remotePath + "/" + Path.GetFileName(authorizedLocalFile);
                    }

                    if (context.IsWhatIf)
                    {
                        _logger.WriteLine($"WHAT IF: Would send local file {localFile} to {stmt.ConnectionName}:{remoteFile}", ConsoleColor.Yellow);
                        continue;
                    }

                    _logger.WriteLine($"Sending: {localFile} -> {stmt.ConnectionName}:{remoteFile}", ConsoleColor.Cyan);
                    await remoteFs.UploadFileAsync(authorizedLocalFile, remoteFile, overwrite, context.CancellationToken);
                    if (verify)
                        await VerifyTransferAsync(remoteFs, remoteFile, authorizedLocalFile, deleteLocalOnMismatch: false, context.CancellationToken);
                }
                _logger.WriteLine("Upload complete.", ConsoleColor.Green);
            }
            else
            {
                localPath = pathAuthorizer.Authorize(context, localPath,
                    FileSystemAccessKind.Read).CanonicalPath;
                _logger.WriteLine($"SENDING: {localPath} -> {stmt.ConnectionName}:{remotePath} (OVERWRITE={(overwrite ? "ON" : "OFF")})", ConsoleColor.Cyan);

                context.SecurityService.ValidateFileType(localPath);

                if (context.IsWhatIf)
                {
                    _logger.WriteLine($"WHAT IF: Would send local file {localPath} to {stmt.ConnectionName}:{remotePath}", ConsoleColor.Yellow);
                    return;
                }

                if (!File.Exists(localPath)) throw new ExecutionException($"Local file not found: {localPath}");
                await remoteFs.UploadFileAsync(localPath, remotePath, overwrite, context.CancellationToken);
                if (verify)
                    await VerifyTransferAsync(remoteFs, remotePath, localPath, deleteLocalOnMismatch: false, context.CancellationToken);
                _logger.WriteLine("Upload complete.", ConsoleColor.Green);
            }
        }
        else // Receive
        {
            bool hasWildcard = remotePath.Contains('*') || remotePath.Contains('?');
            localPath = pathAuthorizer.Authorize(context, localPath,
                FileSystemAccessKind.Write,
                validateFileType: !hasWildcard && !recursive).CanonicalPath;

            if (hasWildcard || recursive)
            {
                string remoteDir = remotePath;
                string? remotePattern = null;
                if (hasWildcard)
                {
                    remoteDir = "";
                    remotePattern = remotePath;
                    var lastSlash = remotePath.LastIndexOfAny(new[] { '/', '\\' });
                    if (lastSlash >= 0)
                    {
                        remoteDir = remotePath.Substring(0, lastSlash + 1);
                        remotePattern = remotePath.Substring(lastSlash + 1);
                    }
                }

                _logger.WriteLine($"RECEIVING {(recursive ? "recursive " : "")}matches from {stmt.ConnectionName}:{remotePath} to local directory {localPath} (OVERWRITE={(overwrite ? "ON" : "OFF")})", ConsoleColor.Cyan);

                System.Text.RegularExpressions.Regex? regex = null;
                if (remotePattern != null)
                {
                    string escaped = System.Text.RegularExpressions.Regex.Escape(remotePattern);
                    string regexPattern = "^" + escaped.Replace("\\*", ".*").Replace("\\?", ".") + "$";
                    regex = new System.Text.RegularExpressions.Regex(regexPattern,
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                        context.SecurityService.RegexMatchTimeout);
                }

                int matchCount = 0;

                await foreach (var entry in RemoteDirectoryWalker.WalkAsync(remoteFs, remoteDir, recursive, context, context.CancellationToken))
                {
                    var remoteFile = entry.Meta;
                    if (remoteFile.IsDirectory || remoteFile.IsSymlink == true) continue;

                    string fileNameOnly = Path.GetFileName(remoteFile.FullPath);
                    if (regex != null && !regex.IsMatch(fileNameOnly))
                        continue;

                    matchCount++;
                    string relTarget = recursive
                        ? entry.RelativePath.Replace('/', Path.DirectorySeparatorChar)
                        : fileNameOnly;
                    string localFile = pathAuthorizer.Authorize(context,
                        Path.Combine(localPath, relTarget),
                        FileSystemAccessKind.Write).CanonicalPath;

                    // Security Hardening: Block writing to script files
                    context.SecurityService.ValidateWriteAccess(localFile);
                    context.SecurityService.ValidateFileType(localFile);

                    if (context.IsWhatIf)
                    {
                        _logger.WriteLine($"WHAT IF: Would receive {stmt.ConnectionName}:{remoteFile.FullPath} to local file {localFile}", ConsoleColor.Yellow);
                        continue;
                    }

                    _logger.WriteLine($"Receiving: {stmt.ConnectionName}:{remoteFile.FullPath} -> {localFile}", ConsoleColor.Cyan);
                    var dir = Path.GetDirectoryName(localFile);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    localFile = pathAuthorizer.Authorize(context, localFile,
                        FileSystemAccessKind.Write).CanonicalPath;
                    await remoteFs.DownloadFileAsync(remoteFile.FullPath, localFile, overwrite);
                    if (verify)
                        await VerifyTransferAsync(remoteFs, remoteFile.FullPath, localFile, deleteLocalOnMismatch: true, context.CancellationToken);
                }

                if (matchCount == 0)
                {
                    _logger.WriteLine($"No remote files matched pattern: {remotePath}", ConsoleColor.Yellow);
                }
                else
                {
                    _logger.WriteLine("Download complete.", ConsoleColor.Green);
                }
            }
            else
            {
                _logger.WriteLine($"RECEIVING: {stmt.ConnectionName}:{remotePath} -> {localPath} (OVERWRITE={(overwrite ? "ON" : "OFF")})", ConsoleColor.Cyan);

                // Security Hardening: Block writing to script files and dangerous local file types.
                context.SecurityService.ValidateWriteAccess(localPath);
                context.SecurityService.ValidateFileType(localPath);

                if (context.IsWhatIf)
                {
                    _logger.WriteLine($"WHAT IF: Would receive {stmt.ConnectionName}:{remotePath} to local file {localPath}", ConsoleColor.Yellow);
                    return;
                }

                // Authorized before anything is created: a refused path must not leave a directory behind.
                localPath = pathAuthorizer.Authorize(context, localPath,
                    FileSystemAccessKind.Write).CanonicalPath;
                var dir = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                await remoteFs.DownloadFileAsync(remotePath, localPath, overwrite);
                if (verify)
                    await VerifyTransferAsync(remoteFs, remotePath, localPath, deleteLocalOnMismatch: true, context.CancellationToken);
                _logger.WriteLine("Download complete.", ConsoleColor.Green);
            }
        }
    }

    internal static async Task VerifyTransferAsync(
        IRemoteFileSystem remoteFs,
        string remotePath,
        string localPath,
        bool deleteLocalOnMismatch,
        CancellationToken cancellationToken)
    {
        RemoteFileVerificationInfo? remoteInfo;
        try
        {
            remoteInfo = await remoteFs.GetFileVerificationInfoAsync(remotePath, cancellationToken);
        }
        catch (NotSupportedException)
        {
            // Fallback for connectors that do not implement GetFileVerificationInfoAsync:
            // look up the entry in its parent directory listing.
            remoteInfo = await LookupSizeFromListingAsync(remoteFs, remotePath, cancellationToken);
        }

        if (remoteInfo == null)
        {
            DeletePartialLocalFileIfNeeded(localPath, deleteLocalOnMismatch);
            throw new ExecutionException($"Transfer verification failed for '{remotePath}': remote file metadata could not be retrieved.");
        }

        long localSize = File.Exists(localPath) ? new FileInfo(localPath).Length : -1;
        if (localSize != remoteInfo.Size)
        {
            DeletePartialLocalFileIfNeeded(localPath, deleteLocalOnMismatch);
            throw new ExecutionException(
                $"Transfer verification failed for '{remotePath}': size mismatch (remote={remoteInfo.Size} bytes, local={localSize} bytes).");
        }

        if (!string.IsNullOrEmpty(remoteInfo.Md5Hex))
        {
            string localMd5;
            await using (var stream = File.OpenRead(localPath))
            {
                var hash = await MD5.HashDataAsync(stream, cancellationToken);
                localMd5 = Convert.ToHexString(hash).ToLowerInvariant();
            }

            if (!string.Equals(localMd5, remoteInfo.Md5Hex, StringComparison.OrdinalIgnoreCase))
            {
                DeletePartialLocalFileIfNeeded(localPath, deleteLocalOnMismatch);
                throw new ExecutionException(
                    $"Transfer verification failed for '{remotePath}': MD5 checksum mismatch (remote={remoteInfo.Md5Hex}, local={localMd5}).");
            }
        }
    }

    private static async Task<RemoteFileVerificationInfo?> LookupSizeFromListingAsync(
        IRemoteFileSystem remoteFs,
        string remotePath,
        CancellationToken cancellationToken)
    {
        var normalized = remotePath.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        var parentDir = slash < 0 ? "" : normalized[..slash];
        var fileName = slash < 0 ? normalized : normalized[(slash + 1)..];

        await foreach (var entry in remoteFs.ListFilesAsync(parentDir).WithCancellation(cancellationToken))
        {
            if (!entry.IsDirectory && string.Equals(entry.Name, fileName, StringComparison.Ordinal))
                return new RemoteFileVerificationInfo(entry.Size, Md5Hex: null);
        }
        return null;
    }

    private static void DeletePartialLocalFileIfNeeded(string localPath, bool deleteLocalOnMismatch)
    {
        if (!deleteLocalOnMismatch) return;
        try
        {
            if (File.Exists(localPath))
                File.Delete(localPath);
        }
        catch
        {
            // Best-effort removal of partial local file.
        }
    }
}
