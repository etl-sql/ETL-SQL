using System;
using System.Collections.Generic;
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
/// Handles directory-related operations such as CREATE, DELETE, MOVE, RENAME, COPY, DELETE_CONTENTS, COMPRESS, and DECOMPRESS.
/// </summary>
public class DirectoryOperationStatementHandler(
    ILogger logger,
    IConnectorRegistry? connectorRegistry = null) : IStatementHandler
{
    private readonly ILogger _logger = logger;
    private readonly IConnectorRegistry? _connectorRegistry = connectorRegistry;
    public Type SupportedStatementType => typeof(DirectoryOperationStatement);

    public DirectoryOperationStatementHandler(ILogger logger) : this(logger, null) { }

    /// <summary>Executes the directory operation, resolving paths and performing filesystem actions.</summary>
    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (DirectoryOperationStatement)statement;

        if (!string.IsNullOrEmpty(stmt.ConnectionName))
        {
            // Remote execution context
            if (!context.Connections.TryGetValue(stmt.ConnectionName, out var ds) || ds is not IRemoteFileSystem remoteFs)
            {
                throw new ExecutionException($"Connection '{stmt.ConnectionName}' not found or does not support remote directory operations.");
            }

            string remotePath = (await context.EvaluateValue(stmt.Path, new Row()))?.ToString() ?? "";

            // Security Hardening: Count this as a directory operation
            context.IncrementOperationCount(OperationType.FileSystem, remotePath);

            if (context.IsWhatIf)
            {
                _logger.WriteLine($"WHAT IF: Would perform {stmt.Type}_DIRECTORY on connection '{stmt.ConnectionName}': {remotePath}", ConsoleColor.Yellow);
                return;
            }

            try
            {
                switch (stmt.Type)
                {
                    case DirectoryOpType.Create:
                        await remoteFs.CreateDirectoryAsync(remotePath);
                        _logger.WriteLine($"Remote directory created: {stmt.ConnectionName}:{remotePath}", ConsoleColor.Green);
                        break;
                    case DirectoryOpType.Delete:
                        await remoteFs.DeleteDirectoryAsync(remotePath);
                        _logger.WriteLine($"Remote directory deleted: {stmt.ConnectionName}:{remotePath}", ConsoleColor.Green);
                        break;
                    default:
                        throw new ExecutionException($"Directory operation '{stmt.Type}' is not supported on remote systems.");
                }
            }
            catch (ExecutionException) { throw; }
            catch (Exception ex)
            {
                throw new ExecutionException($"Remote directory operation '{stmt.Type}' failed: {ex.Message}", ex, null, stmt.Line, stmt.Column);
            }
            return;
        }

        string? sourceConnectionName = null;
        IDataSource? sourceDs = null;
        if (stmt.Path is IdentifierExpression pathId && context.Connections.TryGetValue(pathId.Name, out var foundPathDs))
        {
            sourceConnectionName = pathId.Name;
            sourceDs = foundPathDs;
        }
        else
        {
            var pVal = (await context.EvaluateValue(stmt.Path, new Row()))?.ToString() ?? "";
            if (!string.IsNullOrEmpty(pVal) && context.Connections.TryGetValue(pVal, out var foundPathDs2))
            {
                sourceConnectionName = pVal;
                sourceDs = foundPathDs2;
            }
        }

        string pathVal = sourceDs != null ? sourceDs.Path : ((await context.EvaluateValue(stmt.Path, new Row()))?.ToString() ?? "");
        string path = context.ResolvePath(pathVal);

        string? destVal = null;
        if (stmt.Destination != null)
        {
            if (stmt.Destination is IdentifierExpression destId && context.Connections.TryGetValue(destId.Name, out var foundDestDs))
            {
                destVal = foundDestDs.Path;
            }
            else
            {
                var dVal = (await context.EvaluateValue(stmt.Destination, new Row()))?.ToString();
                if (!string.IsNullOrEmpty(dVal) && context.Connections.TryGetValue(dVal, out var foundDestDs2))
                    destVal = foundDestDs2.Path;
                else
                    destVal = dVal ?? "";
            }
        }
        string? dest = destVal != null ? context.ResolvePath(destVal) : null;
        var pathAuthorizer = new FileSystemPolicyAuthorizer(context.SecurityService);
        var sourceAccess = stmt.Type is DirectoryOpType.Delete or DirectoryOpType.DeleteContents
            or DirectoryOpType.Move or DirectoryOpType.Rename
            ? FileSystemAccessKind.Move
            : FileSystemAccessKind.Read;
        path = pathAuthorizer.Authorize(context, path, sourceAccess,
            validateFileType: stmt.Type == DirectoryOpType.Decompress).CanonicalPath;
        if (dest != null && stmt.Type != DirectoryOpType.Rename)
        {
            var destinationAccess = stmt.Type == DirectoryOpType.Decompress
                ? FileSystemAccessKind.Extract
                : FileSystemAccessKind.Write;
            dest = pathAuthorizer.Authorize(context, dest, destinationAccess,
                validateFileType: stmt.Type == DirectoryOpType.Compress).CanonicalPath;
        }

        bool overwrite = true; // Default to true for backward compatibility
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

        bool recursive = true; // Default to true for backward compatibility
        if (stmt.Recursive != null)
        {
            var recVal = await context.EvaluateValue(stmt.Recursive, new Row());
            if (recVal != null)
            {
                if (recVal is bool b) recursive = b;
                else if (string.Equals(recVal.ToString(), "ON", StringComparison.OrdinalIgnoreCase)) recursive = true;
                else if (string.Equals(recVal.ToString(), "OFF", StringComparison.OrdinalIgnoreCase)) recursive = false;
                else if (string.Equals(recVal.ToString(), "TRUE", StringComparison.OrdinalIgnoreCase)) recursive = true;
                else if (string.Equals(recVal.ToString(), "FALSE", StringComparison.OrdinalIgnoreCase)) recursive = false;
            }
        }

        _logger.Debug("Directory Operation: {OperationType} on {Path}{Dest}", stmt.Type, path, dest != null ? $" -> {dest}" : "");

        if (context.IsWhatIf)
        {
            _logger.WriteLine($"WHAT IF: Would perform {stmt.Type}_DIRECTORY on {path}{(dest != null ? $" to {dest}" : "")}", ConsoleColor.Yellow);
            return;
        }

        // Security Hardening: Count this as a directory operation
        context.IncrementOperationCount(OperationType.FileSystem, path);

        var fsService = new FileSystemService(_logger);

        try
        {
            switch (stmt.Type)
            {
                case DirectoryOpType.Create:
                    path = pathAuthorizer.Authorize(context, path, FileSystemAccessKind.Write,
                        validateFileType: false).CanonicalPath;
                    Directory.CreateDirectory(path);
                    _logger.WriteLine($"Directory created: {path}", ConsoleColor.Green);
                    break;
                case DirectoryOpType.Delete:
                    var deletePath = pathAuthorizer.Authorize(context, path, FileSystemAccessKind.Delete,
                        validateFileType: false);
                    // Security Hardening: Block deleting directories containing scripts (or with script extensions)
                    context.SecurityService.ValidateWriteAccess(deletePath.CanonicalPath);

                    if (pathAuthorizer.DeleteValidatedDirectory(context, deletePath, recursive: true, stmt.IfExists))
                    {
                        path = deletePath.CanonicalPath;
                        _logger.WriteLine($"Directory deleted: {path}", ConsoleColor.Green);
                    }
                    else if (stmt.IfExists)
                    {
                        _logger.WriteLine($"DELETE DIRECTORY IF EXISTS: {pathVal} not found. Skipping.", ConsoleColor.Gray);
                    }
                    break;
                case DirectoryOpType.Rename:
                case DirectoryOpType.Move:
                    if (dest != null)
                    {
                        var target = dest;
                        if (stmt.Type == DirectoryOpType.Rename && destVal != null)
                        {
                            var parent = System.IO.Path.GetDirectoryName(path.TrimEnd('/', '\\')) ?? "";
                            target = destVal.Contains('/') || destVal.Contains('\\')
                                ? context.ResolvePath(destVal)
                                : System.IO.Path.Combine(parent, destVal);
                        }
                        var moveSource = pathAuthorizer.Authorize(context, path, FileSystemAccessKind.Move,
                            validateFileType: false);
                        var moveDest = pathAuthorizer.Authorize(context, target, FileSystemAccessKind.Move,
                            validateFileType: false);

                        // Security Hardening: Validate the target path
                        context.SecurityService.ValidatePath(moveDest.CanonicalPath);
                        context.SecurityService.ValidateWriteAccess(moveDest.CanonicalPath);

                        pathAuthorizer.MoveValidatedDirectory(context, moveSource, moveDest, overwrite);
                        path = moveSource.CanonicalPath;
                        dest = moveDest.CanonicalPath;

                        if (sourceConnectionName != null && sourceDs != null && dest != null)
                        {
                            await UpdateConnectionPathInPlaceAsync(context, sourceConnectionName, sourceDs, dest);
                        }
                    }
                    break;
                case DirectoryOpType.Copy:
                    if (dest != null)
                    {
                        // Security Hardening: Block copying into sensitive script locations
                        context.SecurityService.ValidateWriteAccess(dest);

                        await fsService.CopyDirectory(path, dest, overwrite, context);
                        _logger.WriteLine($"Directory copied: {path} -> {dest}", ConsoleColor.Green);
                    }
                    break;
                case DirectoryOpType.DeleteContents:
                    // Security Hardening: Block deleting contents of directories containing scripts
                    context.SecurityService.ValidateWriteAccess(path);

                    await fsService.DeleteDirectoryContents(path, recursive, context);
                    _logger.WriteLine($"Directory contents deleted: {path}", ConsoleColor.Green);
                    break;
                case DirectoryOpType.Compress:
                    if (dest != null)
                    {
                        var compressDest = pathAuthorizer.Authorize(context, dest, FileSystemAccessKind.Write,
                            validateFileType: true);
                        // Security Hardening: Block writing to script files
                        context.SecurityService.ValidateWriteAccess(compressDest.CanonicalPath);

                        if (File.Exists(compressDest.CanonicalPath))
                        {
                            if (!overwrite)
                                throw new ExecutionException($"Destination file already exists and OVERWRITE is OFF: {compressDest.CanonicalPath}");

                            var deleteDest = pathAuthorizer.Authorize(context, compressDest.CanonicalPath,
                                FileSystemAccessKind.Delete, validateFileType: true);
                            context.SecurityService.ValidateWriteAccess(deleteDest.CanonicalPath);
                            pathAuthorizer.DeleteValidatedFile(context, deleteDest);
                        }
                        await using var destStream = pathAuthorizer.OpenValidatedWrite(context, compressDest,
                            truncate: true, failIfExists: true);
                        System.IO.Compression.ZipFile.CreateFromDirectory(path, destStream);
                        _logger.WriteLine($"Directory compressed: {path} -> {dest}", ConsoleColor.Green);
                    }
                    break;
                case DirectoryOpType.Decompress:
                    if (dest != null)
                    {
                        // Security Hardening: Block writing to script files
                        context.SecurityService.ValidateWriteAccess(dest);

                        if (File.Exists(path))
                        {
                            SafeZipExtractor.Extract(path, dest, overwrite, context, pathAuthorizer);
                            _logger.WriteLine($"Directory decompressed: {path} -> {dest}", ConsoleColor.Green);
                        }
                        else
                        {
                            throw new ExecutionException($"Source for DECOMPRESS_DIRECTORY does not exist: {path}");
                        }
                    }
                    break;
                case DirectoryOpType.Encrypt:
                    if (dest != null)
                    {
                        // Security Hardening: Block writing to script files
                        context.SecurityService.ValidateWriteAccess(dest);

                        var pwd = stmt.Password != null ? (await context.EvaluateValue(stmt.Password, new Row(), decryptSensitive: true))?.ToString() : null;
                        pwd ??= context.SecurityService.MasterPassword;
                        if (pwd == null)
                            throw new ExecutionException("ENCRYPT_DIRECTORY requires a PASSWORD clause or a configured master password.", null, stmt.Line, stmt.Column);
                        await fsService.EncryptDirectory(path, dest, pwd, overwrite, context);
                        _logger.WriteLine($"Directory encrypted: {path} -> {dest}", ConsoleColor.Green);
                    }
                    break;
                case DirectoryOpType.Decrypt:
                    if (dest != null)
                    {
                        // Security Hardening: Block writing to script files
                        context.SecurityService.ValidateWriteAccess(dest);

                        var pwd = stmt.Password != null ? (await context.EvaluateValue(stmt.Password, new Row(), decryptSensitive: true))?.ToString() : null;
                        pwd ??= context.SecurityService.MasterPassword;
                        if (pwd == null)
                            throw new ExecutionException("DECRYPT_DIRECTORY requires a PASSWORD clause or a configured master password.", null, stmt.Line, stmt.Column);
                        await fsService.DecryptDirectory(path, dest, pwd, overwrite, context);
                        _logger.WriteLine($"Directory decrypted: {path} -> {dest}", ConsoleColor.Green);
                    }
                    break;
            }
        }
        catch (ExecutionException) { throw; }
        catch (ETL_SQL.Services.SecurityException) { throw; }
        catch (Exception ex)
        {
            throw new ExecutionException($"Directory operation '{stmt.Type}' failed: {ex.Message}", ex, null, stmt.Line, stmt.Column);
        }
        await Task.CompletedTask;
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
            _logger.Debug("Updated directory connection '{ConnectionName}' path to '{NewPath}'", connectionName, newPath);
        }
    }
}
