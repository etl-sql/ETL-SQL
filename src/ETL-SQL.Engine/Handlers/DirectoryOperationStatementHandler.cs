using System;
using System.IO;
using System.Threading.Tasks;
using System.IO.Compression;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Engine.Services;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles directory-related operations such as CREATE, DELETE, MOVE, RENAME, COPY, DELETE_CONTENTS, COMPRESS, and DECOMPRESS.
    /// </summary>
    public class DirectoryOperationStatementHandler : IStatementHandler
    {
        private readonly ILogger _logger;
        public Type SupportedStatementType => typeof(DirectoryOperationStatement);

        public DirectoryOperationStatementHandler(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>Executes the directory operation, resolving paths and performing filesystem actions.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (DirectoryOperationStatement)statement;
            
            string pathVal = (await context.EvaluateValue(stmt.Path, new Row()))?.ToString() ?? "";
            string path = context.ResolvePath(pathVal);

            string? dest = stmt.Destination != null ? context.ResolvePath((await context.EvaluateValue(stmt.Destination, new Row()))?.ToString() ?? "") : null;
            
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
                        Directory.CreateDirectory(path);
                        _logger.WriteLine($"Directory created: {path}", ConsoleColor.Green);
                        break;
                    case DirectoryOpType.Delete:
                        // Security Hardening: Block deleting directories containing scripts (or with script extensions)
                        context.SecurityService.ValidateWriteAccess(path);

                        if (Directory.Exists(path))
                        {
                            Directory.Delete(path, true);
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
                            if (stmt.Type == DirectoryOpType.Rename)
                            {
                                var parent = System.IO.Path.GetDirectoryName(path.TrimEnd('/', '\\')) ?? "";
                                target = System.IO.Path.Combine(parent, dest);
                            }
                            
                            // Security Hardening: Validate the target path
                            context.SecurityService.ValidatePath(target);
                            context.SecurityService.ValidateWriteAccess(target);
                            
                            if (Directory.Exists(target))
                            {
                                if (overwrite) Directory.Delete(target, true);
                                else throw new ExecutionException($"Destination directory already exists and OVERWRITE is OFF: {target}");
                            }
                            Directory.Move(path, target);
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

                        fsService.DeleteDirectoryContents(path, recursive, context);
                        _logger.WriteLine($"Directory contents deleted: {path}", ConsoleColor.Green);
                        break;
                    case DirectoryOpType.Compress:
                        if (dest != null)
                        {
                            // Security Hardening: Block writing to script files
                            context.SecurityService.ValidateWriteAccess(dest);

                            if (File.Exists(dest))
                            {
                                 if (overwrite) File.Delete(dest);
                                 else throw new ExecutionException($"Destination file already exists and OVERWRITE is OFF: {dest}");
                            }
                            System.IO.Compression.ZipFile.CreateFromDirectory(path, dest);
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
                                ZipFile.ExtractToDirectory(path, dest, overwrite);
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
    }
}
