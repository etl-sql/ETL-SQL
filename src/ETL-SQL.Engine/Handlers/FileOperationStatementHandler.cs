using System;
using ETL_SQL.Core.Common.Exceptions;
using System.IO;
using System.Threading.Tasks;
using System.IO.Compression;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Handlers
{
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
            
            string sourceVal = (await context.EvaluateValue(stmt.Source, new Row()))?.ToString() ?? "";
            string source = context.ResolvePath(sourceVal); // Resolving path first ensures it's checked against safe zones
            string? dest = stmt.Destination != null ? context.ResolvePath((await context.EvaluateValue(stmt.Destination, new Row()))?.ToString() ?? "") : null;

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
            // it might be locked for a few ms. We'll do a tiny retry if the file is missing but expected.
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
                        // Security Hardening: Block deleting script files and dangerous file types
                        context.SecurityService.ValidateWriteAccess(source);
                        context.SecurityService.ValidateFileType(source);

                        if (File.Exists(source)) 
                        {
                            File.Delete(source);
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
                            // Security Hardening: Block writing to script files and dangerous types
                            context.SecurityService.ValidateWriteAccess(dest);
                            context.SecurityService.ValidateFileType(dest);

                            if (File.Exists(dest))
                            {
                                if (overwrite) File.Delete(dest);
                                else throw new ExecutionException($"Destination file already exists and OVERWRITE is OFF: {dest}");
                            }
                            File.Copy(source, dest, overwrite);
                        }
                        break;
                    case FileOpType.Move:
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
                            File.Move(source, dest);
                        }
                        break;
                    case FileOpType.Rename:
                        if (dest != null)
                        {
                            var fileName = Path.GetFileName(source);
                            var dir = Path.GetDirectoryName(source) ?? "";
                            var newPath = Path.Combine(dir, dest);
                            
                            // Security Hardening: Validate the constructed rename path
                            context.SecurityService.ValidatePath(newPath);
                            context.SecurityService.ValidateWriteAccess(newPath);
                            
                            if (File.Exists(newPath))
                            {
                                if (overwrite) File.Delete(newPath);
                                else throw new ExecutionException($"Destination file already exists and OVERWRITE is OFF: {newPath}");
                            }
                            File.Move(source, newPath);
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
                                ZipFile.ExtractToDirectory(source, dest, overwrite);
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
                                CryptoUtils.EncryptFileWithSsh(source, dest, keyFilePath, overwrite);
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
                                CryptoUtils.DecryptFileWithSsh(source, dest, keyFilePath, overwrite, pwd);
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
}
