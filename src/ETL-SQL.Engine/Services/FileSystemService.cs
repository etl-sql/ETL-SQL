using System;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Services
{
    /// <summary>
    /// Centralized service for secure file and directory operations.
    /// Integrates security guardrails, operation counting, and recursion depth checks.
    /// </summary>
    public class FileSystemService(ILogger logger)
    {
        private readonly ILogger _logger = logger;

        public async Task CopyDirectory(string sourceDir, string destinationDir, bool overwrite, IExecutionContext context)
        {
            context.IncrementOperationCount(OperationType.FileSystem, sourceDir);
            var dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists) throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");

            Directory.CreateDirectory(destinationDir);

            foreach (FileInfo file in dir.GetFiles())
            {
                context.IncrementOperationCount(OperationType.FileSystem, file.FullName);
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath, overwrite);
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

        public void DeleteDirectoryContents(string path, bool recursive, IExecutionContext context)
        {
            context.IncrementOperationCount(OperationType.FileSystem, path);
            var dir = new DirectoryInfo(path);
            if (!dir.Exists) return;

            foreach (FileInfo file in dir.GetFiles()) 
            {
                context.IncrementOperationCount(OperationType.FileSystem, file.FullName);
                file.Delete();
            }
            
            foreach (DirectoryInfo subDir in dir.GetDirectories()) 
            {
                if (recursive)
                {
                    using (context.EnterRecursiveScope())
                    {
                        DeleteDirectoryContents(subDir.FullName, true, context);
                    }
                }
                context.IncrementOperationCount(OperationType.FileSystem, subDir.FullName);
                subDir.Delete(recursive);
            }
        }

        public async Task EncryptDirectory(string sourceDir, string destDir, string password, bool overwrite, IExecutionContext context)
        {
            context.IncrementOperationCount(OperationType.FileSystem, sourceDir);
            if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                context.IncrementOperationCount(OperationType.FileSystem, file);
                string destFile = Path.Combine(destDir, Path.GetFileName(file) + ".enc");
                CryptoUtils.EncryptFile(file, destFile, password, overwrite);
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
            context.IncrementOperationCount(OperationType.FileSystem, sourceDir);
            if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                if (!file.EndsWith(".enc")) continue;
                context.IncrementOperationCount(OperationType.FileSystem, file);
                string destFile = Path.Combine(destDir, Path.GetFileNameWithoutExtension(file));
                CryptoUtils.DecryptFile(file, destFile, password, overwrite);
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
}
