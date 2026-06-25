using System;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

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

        bool overwrite = true;
        if (stmt.Overwrite != null)
        {
            var ovVal = await context.EvaluateValue(stmt.Overwrite, new Row());
            if (ovVal is bool b) overwrite = b;
            else if (ovVal != null) overwrite = ovVal.ToString()?.ToUpperInvariant() == "ON" || ovVal.ToString()?.ToUpperInvariant() == "TRUE";
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
                    // Security Hardening: Check read/write rules
                    context.SecurityService.ValidateFileType(localFile);

                    string remoteFile = remotePath;
                    if (remotePath.EndsWith("/") || remotePath.EndsWith("\\") || string.IsNullOrEmpty(remotePath))
                    {
                        remoteFile = remotePath + Path.GetFileName(localFile);
                    }
                    else
                    {
                        remoteFile = remotePath + "/" + Path.GetFileName(localFile);
                    }

                    if (context.IsWhatIf)
                    {
                        _logger.WriteLine($"WHAT IF: Would send local file {localFile} to {stmt.ConnectionName}:{remoteFile}", ConsoleColor.Yellow);
                        continue;
                    }

                    _logger.WriteLine($"Sending: {localFile} -> {stmt.ConnectionName}:{remoteFile}", ConsoleColor.Cyan);
                    await remoteFs.UploadFileAsync(localFile, remoteFile, overwrite);
                }
                _logger.WriteLine("Upload complete.", ConsoleColor.Green);
            }
            else
            {
                _logger.WriteLine($"SENDING: {localPath} -> {stmt.ConnectionName}:{remotePath} (OVERWRITE={(overwrite ? "ON" : "OFF")})", ConsoleColor.Cyan);

                context.SecurityService.ValidateFileType(localPath);

                if (context.IsWhatIf)
                {
                    _logger.WriteLine($"WHAT IF: Would send local file {localPath} to {stmt.ConnectionName}:{remotePath}", ConsoleColor.Yellow);
                    return;
                }

                if (!File.Exists(localPath)) throw new ExecutionException($"Local file not found: {localPath}");
                await remoteFs.UploadFileAsync(localPath, remotePath, overwrite);
                _logger.WriteLine("Upload complete.", ConsoleColor.Green);
            }
        }
        else // Receive
        {
            bool hasWildcard = remotePath.Contains('*') || remotePath.Contains('?');

            if (hasWildcard)
            {
                string remoteDir = "";
                string remotePattern = remotePath;
                var lastSlash = remotePath.LastIndexOfAny(new[] { '/', '\\' });
                if (lastSlash >= 0)
                {
                    remoteDir = remotePath.Substring(0, lastSlash + 1);
                    remotePattern = remotePath.Substring(lastSlash + 1);
                }

                _logger.WriteLine($"RECEIVING wildcard matches from {stmt.ConnectionName}:{remotePath} to local directory {localPath} (OVERWRITE={(overwrite ? "ON" : "OFF")})", ConsoleColor.Cyan);

                // Compile regex for wildcard pattern
                string escaped = System.Text.RegularExpressions.Regex.Escape(remotePattern);
                string regexPattern = "^" + escaped.Replace("\\*", ".*").Replace("\\?", ".") + "$";
                var regex = new System.Text.RegularExpressions.Regex(regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                var remoteFilesList = remoteFs.ListFilesAsync(remoteDir);
                int matchCount = 0;

                await foreach (var remoteFile in remoteFilesList)
                {
                    if (remoteFile.IsDirectory) continue;

                    string fileNameOnly = Path.GetFileName(remoteFile.FullPath);
                    if (regex.IsMatch(fileNameOnly))
                    {
                        matchCount++;
                        string localFile = Path.Combine(localPath, fileNameOnly);

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
                        await remoteFs.DownloadFileAsync(remoteFile.FullPath, localFile, overwrite);
                    }
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

                var dir = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                await remoteFs.DownloadFileAsync(remotePath, localPath, overwrite);
                _logger.WriteLine("Download complete.", ConsoleColor.Green);
            }
        }
    }
}
