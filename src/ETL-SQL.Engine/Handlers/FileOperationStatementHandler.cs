using System;
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
public class FileOperationStatementHandler : IStatementHandler
{
    private readonly ILogger _logger;
    public Type SupportedStatementType => typeof(FileOperationStatement);

    public FileOperationStatementHandler(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>Executes the file operation, resolving paths and performing the requested action.</summary>
    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (FileOperationStatement)statement;

        if (!string.IsNullOrEmpty(stmt.ConnectionName))
        {
            // Remote execution context
            if (!context.Connections.TryGetValue(stmt.ConnectionName, out var ds) || ds is not IRemoteFileSystem remoteFs)
            {
                throw new ExecutionException($"Connection '{stmt.ConnectionName}' not found or does not support remote file operations.");
            }

            string remoteSource = (await context.EvaluateValue(stmt.Source, new Row()))?.ToString() ?? "";
            string? remoteDest = stmt.Destination != null ? (await context.EvaluateValue(stmt.Destination, new Row()))?.ToString() : null;
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

        string sourceVal = (await context.EvaluateValue(stmt.Source, new Row()))?.ToString() ?? "";
        string source = context.ResolvePath(sourceVal); // Resolving path first ensures it's checked against safe zones
        string? destVal = stmt.Destination != null ? (await context.EvaluateValue(stmt.Destination, new Row()))?.ToString() ?? "" : null;
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

        bool overwrite = true; // Default to true for backward compatibility with underscore functions
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
                        // If not if_exists, the engine usually continues but logs a warning or throws depending on strictness
                        // Standards say: Check existence first to avoid silent no-ops or errors (Rule 9)
                        _logger.Warning("File not found for deletion: {Source}", source);
                    }
                    break;
                case FileOpType.Copy:
                    if (dest != null)
                    {
                        // Re-authorize source (read) and destination (write) immediately before the
                        // copy and stream the bytes through handle-validated opens, so a link swapped
                        // in after the path check cannot redirect the read or the write to an
                        // unauthorized target (TOCTOU / link-race hardening — the write handle is
                        // non-destructive until its final path is verified). Consistent with the
                        // recursive directory-copy path.
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
                    }
                    else
                    {
                        throw new ExecutionException("RENAME FILE requires a destination name.");
                    }
                    break;
                case FileOpType.Compress:
                    if (dest != null)
                    {
                        // Security Hardening: Block writing to script files and dangerous types
                        context.SecurityService.ValidateWriteAccess(dest);
                        context.SecurityService.ValidateFileType(dest);

                        if (File.Exists(dest))
                        {
                            if (overwrite) File.Delete(dest);
                            else throw new ExecutionException($"Destination file already exists and OVERWRITE is OFF: {dest}");
                        }

                        if (Directory.Exists(source))
                        {
                            System.IO.Compression.ZipFile.CreateFromDirectory(source, dest);
                        }
                        else if (File.Exists(source))
                        {
                            using var archive = ZipFile.Open(dest, ZipArchiveMode.Create);
                            archive.CreateEntryFromFile(source, Path.GetFileName(source));
                        }
                        else
                        {
                            throw new ExecutionException($"Source for COMPRESS_FILE does not exist: {source}");
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
                            throw new ExecutionException($"Source for DECOMPRESS_FILE does not exist: {source}");
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
                            await CryptoUtils.EncryptFileWithPgp(source, dest, pgpKeyPath, overwrite);
                        }
                        else if (stmt.KeyFile != null)
                        {
                            string keyFilePath = context.ResolvePath((await context.EvaluateValue(stmt.KeyFile, new Row()))?.ToString() ?? "");
                            await CryptoUtils.EncryptFileWithSshAsync(source, dest, keyFilePath, overwrite);
                        }
                        else
                        {
                            var pwd = stmt.Password != null ? (await context.EvaluateValue(stmt.Password, new Row(), decryptSensitive: true))?.ToString() : null;
                            pwd ??= context.SecurityService.MasterPassword;
                            if (string.IsNullOrEmpty(pwd))
                                throw new ExecutionException("ENCRYPT_FILE requires a PASSWORD, KEYFILE, or PGP_KEY clause, or a configured master password.", null, stmt.Line, stmt.Column);
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
                            var pwd = stmt.Password != null ? (await context.EvaluateValue(stmt.Password, new Row(), decryptSensitive: true))?.ToString() : null;
                            await CryptoUtils.DecryptFileWithPgp(source, dest, pgpKeyPath, pwd, overwrite);
                        }
                        else if (stmt.KeyFile != null)
                        {
                            string keyFilePath = context.ResolvePath((await context.EvaluateValue(stmt.KeyFile, new Row()))?.ToString() ?? "");
                            var pwd = stmt.Password != null ? (await context.EvaluateValue(stmt.Password, new Row(), decryptSensitive: true))?.ToString() : null;
                            await CryptoUtils.DecryptFileWithSshAsync(source, dest, keyFilePath, overwrite, pwd);
                        }
                        else
                        {
                            var pwd = stmt.Password != null ? (await context.EvaluateValue(stmt.Password, new Row(), decryptSensitive: true))?.ToString() : null;
                            pwd ??= context.SecurityService.MasterPassword;
                            if (string.IsNullOrEmpty(pwd))
                                throw new ExecutionException("DECRYPT_FILE requires a PASSWORD, KEYFILE, or PGP_KEY clause, or a configured master password.", null, stmt.Line, stmt.Column);
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
}
