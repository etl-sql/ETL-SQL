using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;
using ETL_SQL.Engine.Services;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles various file operations including DELETE, COPY, MOVE, RENAME, COMPRESS, DECOMPRESS, ENCRYPT, and DECRYPT.
/// </summary>
public class FileOperationStatementHandler(
    ILogger logger,
    IConnectorRegistry? connectorRegistry = null) : IStatementHandler
{
    private readonly ILogger _logger = logger;
    private readonly IConnectorRegistry? _connectorRegistry = connectorRegistry;
    public Type SupportedStatementType => typeof(FileOperationStatement);

    private static readonly HashSet<string> KnownFileConnectorTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "FLATFILE", "CSV", "JSON", "XML", "EXCEL", "PARQUET", "AVRO", "DIRECTORY", "SQLITE", "FILE", "SFTP", "FTP", "AZUREBLOB", "S3", "SHAREPOINT"
    };

    private readonly record struct ResolvedFileOperand(
        string RawValue,
        string? ConnectionName,
        IDataSource? DataSource,
        bool IsConnection,
        bool IsRemote,
        IRemoteFileSystem? RemoteFs,
        bool IsDirectory,
        string ResolvedPath);

    public FileOperationStatementHandler(ILogger logger) : this(logger, null) { }

    /// <summary>Executes the file operation, resolving paths and performing the requested action.</summary>
    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (FileOperationStatement)statement;

        if (!string.IsNullOrEmpty(stmt.ConnectionName))
        {
            // Remote execution context via explicit AT clause
            if (!context.Connections.TryGetValue(stmt.ConnectionName, out var ds) || ds is not IRemoteFileSystem remoteFs)
            {
                throw new ExecutionException($"Connection '{stmt.ConnectionName}' not found or does not support remote file operations.");
            }

            string remoteSource = (await context.EvaluateValue(stmt.Source, new Row()))?.ToString() ?? "";
            string? remoteDest = stmt.Destination != null ? (await context.EvaluateValue(stmt.Destination, new Row()))?.ToString() : null;
            remoteDest = await ApplyDestinationNamingOptionsAsync(stmt, context, remoteSource, remoteDest, remote: true);
            // Security Hardening: Count this as a file operation for runaway protection
            context.IncrementOperationCount(OperationType.FileSystem, remoteSource, 1);

            if (context.IsWhatIf)
            {
                context.Log($"WHAT IF: Would perform {stmt.Type}_FILE on connection '{stmt.ConnectionName}': {remoteSource}{(remoteDest != null ? " -> " + remoteDest : "")}", ConsoleColor.Yellow);
                return;
            }

            bool remoteOverwrite = true; // Default to true for backward compatibility with underscore functions
            if (stmt.Overwrite != null)
            {
                var ovrVal = await context.EvaluateValue(stmt.Overwrite, new Row());
                if (ovrVal != null)
                {
                    if (ovrVal is bool b) remoteOverwrite = b;
                    else if (string.Equals(ovrVal.ToString(), "ON", StringComparison.OrdinalIgnoreCase)) remoteOverwrite = true;
                    else if (string.Equals(ovrVal.ToString(), "OFF", StringComparison.OrdinalIgnoreCase)) remoteOverwrite = false;
                    else if (string.Equals(ovrVal.ToString(), "TRUE", StringComparison.OrdinalIgnoreCase)) remoteOverwrite = true;
                    else if (string.Equals(ovrVal.ToString(), "FALSE", StringComparison.OrdinalIgnoreCase)) remoteOverwrite = false;
                }
            }

            _logger.Debug("Remote File Operation: {OperationType} on {Connection}:{Source}{Dest}", stmt.Type, stmt.ConnectionName, remoteSource, remoteDest != null ? $" -> {remoteDest}" : "");

            try
            {
                switch (stmt.Type)
                {
                    case FileOpType.Delete:
                        await remoteFs.DeleteFileAsync(remoteSource);
                        context.Log($"Remote file deleted: {stmt.ConnectionName}:{remoteSource}", ConsoleColor.Green);
                        break;
                    case FileOpType.Move:
                    case FileOpType.Rename:
                        if (remoteDest == null)
                            throw new ExecutionException($"{stmt.Type}_FILE requires a destination name.");

                        string finalDest = remoteDest;
                        if (stmt.Type == FileOpType.Rename && !remoteDest.Contains("/") && !remoteDest.Contains("\\"))
                        {
                            // It's just a file name, keep it in the same directory as source
                            var lastSlash = remoteSource.LastIndexOfAny(new[] { '/', '\\' });
                            if (lastSlash >= 0)
                            {
                                finalDest = remoteSource.Substring(0, lastSlash + 1) + remoteDest;
                            }
                        }

                        await remoteFs.RenameFileAsync(remoteSource, finalDest, remoteOverwrite);
                        context.Log($"Remote file {(stmt.Type == FileOpType.Move ? "moved" : "renamed")}: {stmt.ConnectionName}:{remoteSource} -> {finalDest}", ConsoleColor.Green);
                        break;
                    default:
                        throw new ExecutionException($"File operation '{stmt.Type}' is not supported on remote systems.");
                }
            }
            catch (ExecutionException) { throw; }
            catch (Exception ex)
            {
                throw new ExecutionException($"Remote file operation '{stmt.Type}' failed: {ex.Message}", ex, null, stmt.Line, stmt.Column);
            }
            return;
        }

        var sourceOperand = await ResolveOperandAsync(stmt.Source, context);
        var destOperand = stmt.Destination != null ? await ResolveOperandAsync(stmt.Destination, context) : default;

        bool overwrite = true;
        if (stmt.Overwrite != null)
        {
            var ovrVal = await context.EvaluateValue(stmt.Overwrite, new Row());
            if (ovrVal != null)
            {
                if (ovrVal is bool b) overwrite = b;
                else if (string.Equals(ovrVal.ToString(), "ON", StringComparison.OrdinalIgnoreCase)) overwrite = true;
                else if (string.Equals(ovrVal.ToString(), "OFF", StringComparison.OrdinalIgnoreCase)) overwrite = false;
                else if (string.Equals(ovrVal.ToString(), "TRUE", StringComparison.OrdinalIgnoreCase)) overwrite = true;
                else if (string.Equals(ovrVal.ToString(), "FALSE", StringComparison.OrdinalIgnoreCase)) overwrite = false;
            }
        }

        // Bridge Remote <-> Local / Remote <-> Remote connections
        if (sourceOperand.IsRemote || destOperand.IsRemote)
        {
            await ExecuteRemoteBridgedFileOperationAsync(stmt, context, sourceOperand, destOperand, overwrite);
            return;
        }

        string sourceVal = sourceOperand.RawValue;
        string source = sourceOperand.ResolvedPath;
        string? destVal = destOperand.RawValue != null ? destOperand.ResolvedPath : null;
        destVal = await ApplyDestinationNamingOptionsAsync(stmt, context, source, destVal, remote: false, isDestinationDirectory: destOperand.IsDirectory);
        string? dest = destVal != null ? context.ResolvePath(destVal) : null;
        var pathAuthorizer = new FileSystemPolicyAuthorizer(context.SecurityService);
        var sourceAccess = stmt.Type is FileOpType.Delete or FileOpType.Move or FileOpType.Rename
            ? FileSystemAccessKind.Move
            : FileSystemAccessKind.Read;
        source = pathAuthorizer.Authorize(context, source, sourceAccess,
            validateFileType: stmt.Type != FileOpType.Compress || !Directory.Exists(source)).CanonicalPath;
        if (dest != null && stmt.Type != FileOpType.Rename)
        {
            var destinationAccess = stmt.Type == FileOpType.Decompress
                ? FileSystemAccessKind.Extract
                : FileSystemAccessKind.Write;
            dest = pathAuthorizer.Authorize(context, dest, destinationAccess,
                validateFileType: destinationAccess != FileSystemAccessKind.Extract).CanonicalPath;
        }

        // Security Hardening: Count this as a file operation for runaway protection
        context.IncrementOperationCount(OperationType.FileSystem, source, 1);
        if (context.IsWhatIf)
        {
            context.Log($"WHAT IF: Would perform {stmt.Type}_FILE", ConsoleColor.Yellow);
            return;
        }

        _logger.Debug("File Operation: {OperationType} on {Source}{Dest}", stmt.Type, source, dest != null ? $" -> {dest}" : "");

        // Performance / Stability: If the file was JUST written by a preceding INSERT/SELECT INTO, 
        if (stmt.Type != FileOpType.Delete && !File.Exists(source))
        {
            for (int i = 0; i < 3; i++)
            {
                await Task.Delay(100);
                if (File.Exists(source)) break;
            }
        }

        try
        {
            switch (stmt.Type)
            {
                case FileOpType.Delete:
                    var deleteSource = pathAuthorizer.Authorize(context, source, FileSystemAccessKind.Delete);
                    // Security Hardening: Block deleting script files and dangerous file types
                    context.SecurityService.ValidateWriteAccess(deleteSource.CanonicalPath);
                    context.SecurityService.ValidateFileType(deleteSource.CanonicalPath);

                    if (pathAuthorizer.DeleteValidatedFile(context, deleteSource, stmt.IfExists))
                    {
                        source = deleteSource.CanonicalPath;
                        context.Log($"File deleted: {source}", ConsoleColor.Green);
                    }
                    else if (stmt.IfExists)
                    {
                        context.Log($"DELETE FILE IF EXISTS: {sourceVal} not found. Skipping.", ConsoleColor.Gray);
                    }
                    else
                    {
                        _logger.Warning("File not found for deletion: {Source}", source);
                    }
                    break;
                case FileOpType.Copy:
                    if (dest != null)
                    {
                        var copySource = pathAuthorizer.Authorize(context, source, FileSystemAccessKind.Read);
                        var copyDest = pathAuthorizer.Authorize(context, dest, FileSystemAccessKind.Write);
                        // Security Hardening: Block writing to script files and dangerous types
                        context.SecurityService.ValidateWriteAccess(copyDest.CanonicalPath);
                        context.SecurityService.ValidateFileType(copyDest.CanonicalPath);

                        if (!overwrite && File.Exists(copyDest.CanonicalPath))
                            throw new ExecutionException($"Destination file already exists and OVERWRITE is OFF: {copyDest.CanonicalPath}");

                        await using (var sourceStream = pathAuthorizer.OpenValidatedRead(context, copySource))
                        await using (var destStream = pathAuthorizer.OpenValidatedWrite(context, copyDest,
                            truncate: true, failIfExists: !overwrite))
                        {
                            await sourceStream.CopyToAsync(destStream);
                        }
                        dest = copyDest.CanonicalPath;
                    }
                    break;
                case FileOpType.Move:
                    if (dest != null)
                    {
                        var moveSource = pathAuthorizer.Authorize(context, source, FileSystemAccessKind.Move);
                        var moveDest = pathAuthorizer.Authorize(context, dest, FileSystemAccessKind.Move);
                        // Security Hardening: Block writing to script files and dangerous types
                        context.SecurityService.ValidateWriteAccess(moveDest.CanonicalPath);
                        context.SecurityService.ValidateFileType(moveDest.CanonicalPath);

                        pathAuthorizer.MoveValidatedFile(context, moveSource, moveDest, overwrite);
                        source = moveSource.CanonicalPath;
                        dest = moveDest.CanonicalPath;

                        if (sourceOperand.IsConnection && sourceOperand.ConnectionName != null && sourceOperand.DataSource != null)
                        {
                            await UpdateConnectionPathInPlaceAsync(context, sourceOperand.ConnectionName, sourceOperand.DataSource, dest);
                        }
                    }
                    break;
                case FileOpType.Rename:
                    if (destVal != null)
                    {
                        var dir = Path.GetDirectoryName(source) ?? "";
                        var newPath = destVal.Contains('/') || destVal.Contains('\\')
                            ? context.ResolvePath(destVal)
                            : Path.Combine(dir, destVal);
                        var renameSource = pathAuthorizer.Authorize(context, source, FileSystemAccessKind.Move);
                        var renameDest = pathAuthorizer.Authorize(context, newPath, FileSystemAccessKind.Move);

                        // Security Hardening: Validate the constructed rename path
                        context.SecurityService.ValidatePath(renameDest.CanonicalPath);
                        context.SecurityService.ValidateWriteAccess(renameDest.CanonicalPath);
                        context.SecurityService.ValidateFileType(renameDest.CanonicalPath);

                        pathAuthorizer.MoveValidatedFile(context, renameSource, renameDest, overwrite);
                        dest = renameDest.CanonicalPath;

                        if (sourceOperand.IsConnection && sourceOperand.ConnectionName != null && sourceOperand.DataSource != null)
                        {
                            await UpdateConnectionPathInPlaceAsync(context, sourceOperand.ConnectionName, sourceOperand.DataSource, dest);
                        }
                    }
                    else
                    {
                        throw new ExecutionException("RENAME FILE requires a destination name.");
                    }
                    break;
                case FileOpType.Compress:
                    if (dest != null)
                    {
                        var compressDest = pathAuthorizer.Authorize(context, dest, FileSystemAccessKind.Write);
                        // Security Hardening: Block writing to script files and dangerous types
                        context.SecurityService.ValidateWriteAccess(compressDest.CanonicalPath);
                        context.SecurityService.ValidateFileType(compressDest.CanonicalPath);

                        if (File.Exists(compressDest.CanonicalPath))
                        {
                            if (!overwrite)
                                throw new ExecutionException($"Destination file already exists and OVERWRITE is OFF: {compressDest.CanonicalPath}");

                            var deleteDest = pathAuthorizer.Authorize(context, compressDest.CanonicalPath, FileSystemAccessKind.Delete);
                            context.SecurityService.ValidateWriteAccess(deleteDest.CanonicalPath);
                            context.SecurityService.ValidateFileType(deleteDest.CanonicalPath);
                            pathAuthorizer.DeleteValidatedFile(context, deleteDest);
                        }

                        await using var destStream = pathAuthorizer.OpenValidatedWrite(context, compressDest,
                            truncate: true, failIfExists: true);
                        if (Directory.Exists(source))
                        {
                            System.IO.Compression.ZipFile.CreateFromDirectory(source, destStream);
                        }
                        else if (File.Exists(source))
                        {
                            using var archive = new ZipArchive(destStream, ZipArchiveMode.Create);
                            archive.CreateEntryFromFile(source, Path.GetFileName(source));
                        }
                        else
                        {
                            throw new ExecutionException($"Source for COMPRESS FILE does not exist: {source}");
                        }
                    }
                    break;
                case FileOpType.Decompress:
                    if (dest != null)
                    {
                        // Security Hardening: Block writing to script files and dangerous types
                        context.SecurityService.ValidateWriteAccess(dest);
                        context.SecurityService.ValidateFileType(dest);

                        if (File.Exists(source))
                        {
                            SafeZipExtractor.Extract(source, dest, overwrite, context, pathAuthorizer);
                            context.Log($"File decompressed: {source} -> {dest}", ConsoleColor.Green);
                        }
                        else
                        {
                            throw new ExecutionException($"Source for DECOMPRESS FILE does not exist: {source}");
                        }
                    }
                    break;
                case FileOpType.Encrypt:
                    if (dest != null)
                    {
                        // Security Hardening: Block writing to script files and dangerous types
                        context.SecurityService.ValidateWriteAccess(dest);
                        context.SecurityService.ValidateFileType(dest);

                        if (stmt.PgpKey != null)
                        {
                            string pgpKeyPath = context.ResolvePath((await context.EvaluateValue(stmt.PgpKey, new Row()))?.ToString() ?? "");
                            pgpKeyPath = pathAuthorizer.Authorize(context, pgpKeyPath, FileSystemAccessKind.Read).CanonicalPath;
                            await CryptoUtils.EncryptFileWithPgp(source, dest, pgpKeyPath, overwrite);
                        }
                        else if (stmt.KeyFile != null)
                        {
                            string keyFilePath = context.ResolvePath((await context.EvaluateValue(stmt.KeyFile, new Row()))?.ToString() ?? "");
                            keyFilePath = pathAuthorizer.Authorize(context, keyFilePath, FileSystemAccessKind.Read).CanonicalPath;
                            await CryptoUtils.EncryptFileWithSshAsync(source, dest, keyFilePath, overwrite);
                        }
                        else
                        {
                            var pwd = stmt.Password != null ? (await context.EvaluateValue(stmt.Password, new Row(), decryptSensitive: true))?.ToString() : null;
                            pwd ??= context.SecurityService.MasterPassword;
                            if (string.IsNullOrEmpty(pwd))
                                throw new ExecutionException("ENCRYPT FILE requires a PASSWORD, KEYFILE, or PGP_KEY clause, or a configured master password.", null, stmt.Line, stmt.Column);
                            CryptoUtils.EncryptFile(source, dest, pwd, overwrite);
                        }
                    }
                    break;
                case FileOpType.Decrypt:
                    if (dest != null)
                    {
                        // Security Hardening: Block writing to script files and dangerous types
                        context.SecurityService.ValidateWriteAccess(dest);
                        context.SecurityService.ValidateFileType(dest);

                        if (stmt.PgpKey != null)
                        {
                            string pgpKeyPath = context.ResolvePath((await context.EvaluateValue(stmt.PgpKey, new Row()))?.ToString() ?? "");
                            pgpKeyPath = pathAuthorizer.Authorize(context, pgpKeyPath, FileSystemAccessKind.Read).CanonicalPath;
                            var pwd = stmt.Password != null ? (await context.EvaluateValue(stmt.Password, new Row(), decryptSensitive: true))?.ToString() : null;
                            await CryptoUtils.DecryptFileWithPgp(source, dest, pgpKeyPath, pwd, overwrite);
                        }
                        else if (stmt.KeyFile != null)
                        {
                            string keyFilePath = context.ResolvePath((await context.EvaluateValue(stmt.KeyFile, new Row()))?.ToString() ?? "");
                            keyFilePath = pathAuthorizer.Authorize(context, keyFilePath, FileSystemAccessKind.Read).CanonicalPath;
                            var pwd = stmt.Password != null ? (await context.EvaluateValue(stmt.Password, new Row(), decryptSensitive: true))?.ToString() : null;
                            await CryptoUtils.DecryptFileWithSshAsync(source, dest, keyFilePath, overwrite, pwd);
                        }
                        else
                        {
                            var pwd = stmt.Password != null ? (await context.EvaluateValue(stmt.Password, new Row(), decryptSensitive: true))?.ToString() : null;
                            pwd ??= context.SecurityService.MasterPassword;
                            if (string.IsNullOrEmpty(pwd))
                                throw new ExecutionException("DECRYPT FILE requires a PASSWORD, KEYFILE, or PGP_KEY clause, or a configured master password.", null, stmt.Line, stmt.Column);
                            CryptoUtils.DecryptFile(source, dest, pwd, overwrite);
                        }
                    }
                    break;
            }
        }
        catch (ExecutionException) { throw; }
        catch (ETL_SQL.Services.SecurityException) { throw; }
        catch (Exception ex)
        {
            throw new ExecutionException($"File operation '{stmt.Type}' failed: {ex.Message}", ex, null, stmt.Line, stmt.Column);
        }
        await Task.CompletedTask;
    }

    private async Task ExecuteRemoteBridgedFileOperationAsync(
        FileOperationStatement stmt,
        IExecutionContext context,
        ResolvedFileOperand sourceOperand,
        ResolvedFileOperand destOperand,
        bool overwrite)
    {
        var pathAuthorizer = new FileSystemPolicyAuthorizer(context.SecurityService);
        context.IncrementOperationCount(OperationType.FileSystem, sourceOperand.ResolvedPath, 1);

        if (context.IsWhatIf)
        {
            context.Log($"WHAT IF: Would perform {stmt.Type}_FILE {sourceOperand.ResolvedPath} -> {destOperand.ResolvedPath}", ConsoleColor.Yellow);
            return;
        }

        try
        {
            // Case 1: Both source and destination are remote
            if (sourceOperand.IsRemote && destOperand.IsRemote)
            {
                if (sourceOperand.RemoteFs == destOperand.RemoteFs || string.Equals(sourceOperand.ConnectionName, destOperand.ConnectionName, StringComparison.OrdinalIgnoreCase))
                {
                    var remoteFs = sourceOperand.RemoteFs ?? destOperand.RemoteFs!;
                    string destPath = destOperand.ResolvedPath;
                    if (destOperand.IsDirectory)
                        destPath = CombineRemotePath(destPath, GetRemoteFileName(sourceOperand.ResolvedPath));

                    switch (stmt.Type)
                    {
                        case FileOpType.Delete:
                            await remoteFs.DeleteFileAsync(sourceOperand.ResolvedPath);
                            context.Log($"Remote file deleted: {sourceOperand.ConnectionName}:{sourceOperand.ResolvedPath}", ConsoleColor.Green);
                            break;
                        case FileOpType.Move:
                        case FileOpType.Rename:
                            await remoteFs.RenameFileAsync(sourceOperand.ResolvedPath, destPath, overwrite);
                            context.Log($"Remote file moved: {sourceOperand.ConnectionName}:{sourceOperand.ResolvedPath} -> {destPath}", ConsoleColor.Green);
                            if (sourceOperand.IsConnection && sourceOperand.ConnectionName != null && sourceOperand.DataSource != null)
                            {
                                await UpdateConnectionPathInPlaceAsync(context, sourceOperand.ConnectionName, sourceOperand.DataSource, destPath);
                            }
                            break;
                        default:
                            throw new ExecutionException($"File operation '{stmt.Type}' is not supported on remote connection '{sourceOperand.ConnectionName}'.");
                    }
                }
                else
                {
                    // Different remote filesystems: Download to temp local file, upload to target
                    string tempLocal = Path.GetTempFileName();
                    try
                    {
                        await sourceOperand.RemoteFs!.DownloadFileAsync(sourceOperand.ResolvedPath, tempLocal, overwrite: true);
                        string destPath = destOperand.ResolvedPath;
                        if (destOperand.IsDirectory)
                            destPath = CombineRemotePath(destPath, GetRemoteFileName(sourceOperand.ResolvedPath));
                        await destOperand.RemoteFs!.UploadFileAsync(tempLocal, destPath, overwrite, context.CancellationToken);

                        if (stmt.Type == FileOpType.Move)
                        {
                            await sourceOperand.RemoteFs!.DeleteFileAsync(sourceOperand.ResolvedPath);
                            if (sourceOperand.IsConnection && sourceOperand.ConnectionName != null && sourceOperand.DataSource != null)
                            {
                                await UpdateConnectionPathInPlaceAsync(context, sourceOperand.ConnectionName, sourceOperand.DataSource, destPath);
                            }
                        }
                        context.Log($"Remote file {(stmt.Type == FileOpType.Move ? "moved" : "copied")}: {sourceOperand.ConnectionName}:{sourceOperand.ResolvedPath} -> {destOperand.ConnectionName}:{destPath}", ConsoleColor.Green);
                    }
                    finally
                    {
                        if (File.Exists(tempLocal)) File.Delete(tempLocal);
                    }
                }
                return;
            }

            // Case 2: Remote source -> Local destination (Download)
            if (sourceOperand.IsRemote && !destOperand.IsRemote)
            {
                if (stmt.Type == FileOpType.Delete)
                {
                    await sourceOperand.RemoteFs!.DeleteFileAsync(sourceOperand.ResolvedPath);
                    context.Log($"Remote file deleted: {sourceOperand.ConnectionName}:{sourceOperand.ResolvedPath}", ConsoleColor.Green);
                    return;
                }

                if (string.IsNullOrWhiteSpace(destOperand.ResolvedPath))
                    throw new ExecutionException($"{stmt.Type}_FILE requires a destination name.");

                string localDest = destOperand.ResolvedPath;
                localDest = await ApplyDestinationNamingOptionsAsync(stmt, context, sourceOperand.ResolvedPath, localDest, remote: false, isDestinationDirectory: destOperand.IsDirectory) ?? localDest;
                var copyDest = pathAuthorizer.Authorize(context, localDest, FileSystemAccessKind.Write);
                context.SecurityService.ValidateWriteAccess(copyDest.CanonicalPath);
                context.SecurityService.ValidateFileType(copyDest.CanonicalPath);

                await sourceOperand.RemoteFs!.DownloadFileAsync(sourceOperand.ResolvedPath, copyDest.CanonicalPath, overwrite);

                if (stmt.Type == FileOpType.Move)
                {
                    await sourceOperand.RemoteFs!.DeleteFileAsync(sourceOperand.ResolvedPath);
                    if (sourceOperand.IsConnection && sourceOperand.ConnectionName != null && sourceOperand.DataSource != null)
                    {
                        await UpdateConnectionPathInPlaceAsync(context, sourceOperand.ConnectionName, sourceOperand.DataSource, copyDest.CanonicalPath);
                    }
                }
                context.Log($"Remote file {(stmt.Type == FileOpType.Move ? "moved" : "copied")}: {sourceOperand.ConnectionName}:{sourceOperand.ResolvedPath} -> {copyDest.CanonicalPath}", ConsoleColor.Green);
                return;
            }

            // Case 3: Local source -> Remote destination (Upload)
            if (!sourceOperand.IsRemote && destOperand.IsRemote)
            {
                if (string.IsNullOrWhiteSpace(destOperand.ResolvedPath))
                    throw new ExecutionException($"{stmt.Type}_FILE requires a destination name.");

                string localSource = sourceOperand.ResolvedPath;
                var copySource = pathAuthorizer.Authorize(context, localSource, FileSystemAccessKind.Read);

                string remoteDest = destOperand.ResolvedPath;
                if (destOperand.IsDirectory)
                    remoteDest = CombineRemotePath(remoteDest, Path.GetFileName(copySource.CanonicalPath));

                await destOperand.RemoteFs!.UploadFileAsync(copySource.CanonicalPath, remoteDest, overwrite, context.CancellationToken);

                if (stmt.Type == FileOpType.Move)
                {
                    pathAuthorizer.DeleteValidatedFile(context, pathAuthorizer.Authorize(context, copySource.CanonicalPath, FileSystemAccessKind.Delete));
                    if (sourceOperand.IsConnection && sourceOperand.ConnectionName != null && sourceOperand.DataSource != null)
                    {
                        await UpdateConnectionPathInPlaceAsync(context, sourceOperand.ConnectionName, sourceOperand.DataSource, remoteDest);
                    }
                }
                context.Log($"File {(stmt.Type == FileOpType.Move ? "moved" : "copied")}: {copySource.CanonicalPath} -> {destOperand.ConnectionName}:{remoteDest}", ConsoleColor.Green);
            }
        }
        catch (ExecutionException) { throw; }
        catch (Exception ex)
        {
            throw new ExecutionException($"Bridged remote file operation '{stmt.Type}' failed: {ex.Message}", ex, null, stmt.Line, stmt.Column);
        }
    }

    private async Task<ResolvedFileOperand> ResolveOperandAsync(
        Expression? expr,
        IExecutionContext context)
    {
        if (expr == null)
            return default;

        string? connName = null;
        IDataSource? ds = null;

        if (expr is IdentifierExpression id && context.Connections.TryGetValue(id.Name, out var foundDs))
        {
            connName = id.Name;
            ds = foundDs;
        }
        else
        {
            var val = (await context.EvaluateValue(expr, new Row()))?.ToString() ?? "";
            if (!string.IsNullOrEmpty(val) && context.Connections.TryGetValue(val, out var foundDs2))
            {
                connName = val;
                ds = foundDs2;
            }
            else
            {
                bool isLocalDir = !string.IsNullOrEmpty(val) && Directory.Exists(context.ResolvePath(val));
                return new ResolvedFileOperand(
                    RawValue: val,
                    ConnectionName: null,
                    DataSource: null,
                    IsConnection: false,
                    IsRemote: false,
                    RemoteFs: null,
                    IsDirectory: isLocalDir,
                    ResolvedPath: context.ResolvePath(val));
            }
        }

        if (ds == null)
        {
            var rawVal = (await context.EvaluateValue(expr, new Row()))?.ToString() ?? "";
            bool isLocalDir = !string.IsNullOrEmpty(rawVal) && Directory.Exists(context.ResolvePath(rawVal));
            return new ResolvedFileOperand(
                RawValue: rawVal,
                ConnectionName: null,
                DataSource: null,
                IsConnection: false,
                IsRemote: false,
                RemoteFs: null,
                IsDirectory: isLocalDir,
                ResolvedPath: context.ResolvePath(rawVal));
        }

        if (!IsFileOrRemoteConnector(ds))
        {
            throw new ExecutionException($"Connection '{connName}' of type '{ds.ConnectorType}' does not support file operations.");
        }

        var path = ds.Path ?? "";
        if (path.Contains('*') || path.Contains('?'))
        {
            throw new ExecutionException($"Wildcard patterns in connection '{connName}' are not supported for single-file operations. Use a FOREACH loop to process multiple files.");
        }

        bool isRemote = ds is IRemoteFileSystem;
        var remoteFs = ds as IRemoteFileSystem;
        bool isDirectory = ds.ConnectorType.Equals("DIRECTORY", StringComparison.OrdinalIgnoreCase);

        string resolvedPath = isRemote ? path : context.ResolvePath(path);

        return new ResolvedFileOperand(
            RawValue: connName ?? path,
            ConnectionName: connName,
            DataSource: ds,
            IsConnection: true,
            IsRemote: isRemote,
            RemoteFs: remoteFs,
            IsDirectory: isDirectory,
            ResolvedPath: resolvedPath);
    }

    private bool IsFileOrRemoteConnector(IDataSource ds)
    {
        if (ds is IRemoteFileSystem) return true;
        if (KnownFileConnectorTypes.Contains(ds.ConnectorType)) return true;
        var connector = _connectorRegistry?.GetConnector(ds.ConnectorType) ?? ConnectorRegistry.Instance?.GetConnector(ds.ConnectorType);
        return connector?.IsFileBased == true;
    }

    private async Task UpdateConnectionPathInPlaceAsync(
        IExecutionContext context,
        string connectionName,
        IDataSource existingDs,
        string newPath)
    {
        var connectionType = existingDs.ConnectorType;
        var options = new Dictionary<string, string>(
            existingDs.Options ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);

        var connector = _connectorRegistry?.GetConnector(connectionType)
            ?? ConnectorRegistry.Instance?.GetConnector(connectionType);

        if (connector != null)
        {
            var newDs = connector.CreateDataSource(context, newPath, options);
            await existingDs.DisposeAsync();
            context.Connections[connectionName] = newDs;
            _logger.Debug("Updated connection '{ConnectionName}' path to '{NewPath}'", connectionName, newPath);
        }
    }

    private static async Task<string?> ApplyDestinationNamingOptionsAsync(
        FileOperationStatement stmt,
        IExecutionContext context,
        string source,
        string? destination,
        bool remote,
        bool isDestinationDirectory = false)
    {
        bool destIsDir = stmt.DestinationIsDirectory || isDestinationDirectory;
        if (stmt.DateSuffix == null && !destIsDir)
            return destination;

        if (stmt.Type is not (FileOpType.Copy or FileOpType.Move or FileOpType.Rename))
            throw new ExecutionException("DATE_SUFFIX and TO DIRECTORY are only supported for COPY FILE, MOVE FILE, and RENAME FILE.", null, stmt.Line, stmt.Column);

        if (string.IsNullOrWhiteSpace(destination))
            throw new ExecutionException($"{stmt.Type}_FILE requires a destination when using DATE_SUFFIX or TO DIRECTORY.", null, stmt.Line, stmt.Column);

        var finalDestination = destination;
        if (destIsDir)
        {
            var sourceFileName = remote
                ? GetRemoteFileName(source)
                : Path.GetFileName(source);
            if (string.IsNullOrWhiteSpace(sourceFileName))
                throw new ExecutionException($"{stmt.Type}_FILE could not derive a file name from source path '{source}'.", null, stmt.Line, stmt.Column);

            finalDestination = remote
                ? CombineRemotePath(destination, sourceFileName)
                : Path.Combine(destination, sourceFileName);
        }

        if (stmt.DateSuffix == null)
            return finalDestination;

        var format = (await context.EvaluateValue(stmt.DateSuffix, new Row()))?.ToString();
        if (string.IsNullOrWhiteSpace(format))
            throw new ExecutionException("DATE_SUFFIX requires a non-empty date format.", null, stmt.Line, stmt.Column);

        var separator = stmt.SuffixSeparator != null
            ? (await context.EvaluateValue(stmt.SuffixSeparator, new Row()))?.ToString() ?? string.Empty
            : "_";

        var suffix = DateTime.Now.ToString(format, CultureInfo.InvariantCulture);
        return remote
            ? ApplySuffixToRemoteFileName(finalDestination, separator + suffix)
            : ApplySuffixToLocalFileName(finalDestination, separator + suffix);
    }

    private static string ApplySuffixToLocalFileName(string path, string suffix)
    {
        var directory = Path.GetDirectoryName(path);
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var fileName = name + suffix + extension;
        return string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName);
    }

    private static string ApplySuffixToRemoteFileName(string path, string suffix)
    {
        var slash = path.LastIndexOfAny(new[] { '/', '\\' });
        var directory = slash >= 0 ? path[..(slash + 1)] : string.Empty;
        var fileName = slash >= 0 ? path[(slash + 1)..] : path;
        var dot = fileName.LastIndexOf('.');
        return dot > 0
            ? directory + fileName[..dot] + suffix + fileName[dot..]
            : directory + fileName + suffix;
    }

    private static string GetRemoteFileName(string path)
    {
        var slash = path.LastIndexOfAny(new[] { '/', '\\' });
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    private static string CombineRemotePath(string directory, string fileName)
    {
        if (string.IsNullOrEmpty(directory)) return fileName;
        var separator = directory.Contains('\\') && !directory.Contains('/') ? "\\" : "/";
        return directory.EndsWith("/") || directory.EndsWith("\\")
            ? directory + fileName
            : directory + separator + fileName;
    }
}
