using System;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles directory-related operations such as CREATE, DELETE, MOVE, RENAME, COPY, and DELETE_CONTENTS.
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

            string? dest = stmt.NewNameOrDest != null ? context.ResolvePath((await context.EvaluateValue(stmt.NewNameOrDest, new Row()))?.ToString() ?? "") : null;
            
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

            _logger.Debug($"Directory Operation: {stmt.Type} on {path}{(dest != null ? $" -> {dest}" : "")}");

            if (context.IsWhatIf)
            {
                _logger.WriteLine($"WHAT IF: Would perform {stmt.Type}_DIRECTORY on {path}{(dest != null ? $" to {dest}" : "")}", ConsoleColor.Yellow);
                return;
            }

            // Security Hardening: Count this as a directory operation
            context.IncrementOperationCount(path);

            switch (stmt.Type)
            {
                case DirectoryOpType.Create:
                    Directory.CreateDirectory(path);
                    _logger.WriteLine($"Directory created: {path}", ConsoleColor.Green);
                    break;
                case DirectoryOpType.Delete:
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                        _logger.WriteLine($"Directory deleted: {path}", ConsoleColor.Green);
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
                        CopyDirectory(path, dest, overwrite, context);
                        _logger.WriteLine($"Directory copied: {path} -> {dest}", ConsoleColor.Green);
                    }
                    break;
                case DirectoryOpType.DeleteContents:
                    DeleteDirectoryContents(path, recursive, context);
                    _logger.WriteLine($"Directory contents deleted: {path}", ConsoleColor.Green);
                    break;
                case DirectoryOpType.Compress:
                    if (dest != null)
                    {
                        if (File.Exists(dest))
                        {
                            if (overwrite) File.Delete(dest);
                            else throw new ExecutionException($"Destination file already exists and OVERWRITE is OFF: {dest}");
                        }
                        System.IO.Compression.ZipFile.CreateFromDirectory(path, dest);
                        _logger.WriteLine($"Directory compressed: {path} -> {dest}", ConsoleColor.Green);
                    }
                    break;
                case DirectoryOpType.Encrypt:
                    if (dest != null)
                    {
                        var pwd = context.SecurityService.MasterPassword ?? "DefaultETLPass123!";
                        EncryptDirectory(path, dest, pwd, overwrite, context);
                        _logger.WriteLine($"Directory encrypted: {path} -> {dest}", ConsoleColor.Green);
                    }
                    break;
                case DirectoryOpType.Decrypt:
                    if (dest != null)
                    {
                        var pwd = context.SecurityService.MasterPassword ?? "DefaultETLPass123!";
                        DecryptDirectory(path, dest, pwd, overwrite, context);
                        _logger.WriteLine($"Directory decrypted: {path} -> {dest}", ConsoleColor.Green);
                    }
                    break;
            }
            await Task.CompletedTask;
        }

        private void EncryptDirectory(string sourceDir, string destDir, string password, bool overwrite, IExecutionContext context)
        {
            context.IncrementOperationCount(sourceDir);
            if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                context.IncrementOperationCount(file);
                string destFile = Path.Combine(destDir, Path.GetFileName(file) + ".enc");
                CryptoUtils.EncryptFile(file, destFile, password, overwrite);
            }
            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                context.CurrentRecursiveDepth++;
                EncryptDirectory(subDir, Path.Combine(destDir, Path.GetFileName(subDir)), password, overwrite, context);
                context.CurrentRecursiveDepth--;
            }
        }

        private void DecryptDirectory(string sourceDir, string destDir, string password, bool overwrite, IExecutionContext context)
        {
            context.IncrementOperationCount(sourceDir);
            if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                if (!file.EndsWith(".enc")) continue;
                context.IncrementOperationCount(file);
                string destFile = Path.Combine(destDir, Path.GetFileNameWithoutExtension(file));
                CryptoUtils.DecryptFile(file, destFile, password, overwrite);
            }
            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                context.CurrentRecursiveDepth++;
                DecryptDirectory(subDir, Path.Combine(destDir, Path.GetFileName(subDir)), password, overwrite, context);
                context.CurrentRecursiveDepth--;
            }
        }

        private void CopyDirectory(string sourceDir, string destinationDir, bool overwrite, IExecutionContext context)
        {
            context.IncrementOperationCount(sourceDir);
            var dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists) throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");

            DirectoryInfo[] dirs = dir.GetDirectories();
            Directory.CreateDirectory(destinationDir);

            foreach (FileInfo file in dir.GetFiles())
            {
                context.IncrementOperationCount(file.FullName);
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath, overwrite);
            }

            foreach (DirectoryInfo subDir in dirs)
            {
                context.CurrentRecursiveDepth++;
                string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                CopyDirectory(subDir.FullName, newDestinationDir, overwrite, context);
                context.CurrentRecursiveDepth--;
            }
        }

        private void DeleteDirectoryContents(string path, bool recursive, IExecutionContext context)
        {
            context.IncrementOperationCount(path);
            var dir = new DirectoryInfo(path);
            if (!dir.Exists) return;

            foreach (FileInfo file in dir.GetFiles()) 
            {
                context.IncrementOperationCount(file.FullName);
                file.Delete();
            }
            
            foreach (DirectoryInfo subDir in dir.GetDirectories()) 
            {
                if (recursive)
                {
                    context.CurrentRecursiveDepth++;
                    DeleteDirectoryContents(subDir.FullName, true, context);
                    context.CurrentRecursiveDepth--;
                }
                context.IncrementOperationCount(subDir.FullName);
                subDir.Delete(recursive);
            }
        }
    }
}
