using System;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles directory-related operations such as CREATE, DELETE, MOVE, RENAME, COPY, and DELETE_CONTENTS.
    /// </summary>
    public class DirectoryOperationStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(DirectoryOperationStatement);
        /// <summary>Executes the directory operation, resolving paths and performing filesystem actions.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (DirectoryOperationStatement)statement;
            
            string pathVal = (await context.EvaluateValue(stmt.Path, new Row()))?.ToString() ?? "";
            string path = context.ResolvePath(pathVal);

            string? dest = stmt.NewNameOrDest != null ? context.ResolvePath((await context.EvaluateValue(stmt.NewNameOrDest, new Row()))?.ToString() ?? "") : null;
            
            Logger.Verbose($"Directory Operation: {stmt.Type} on {path}{(dest != null ? $" -> {dest}" : "")}");

            if (context.IsWhatIf)
            {
                Logger.WriteLine($"WHAT IF: Would perform {stmt.Type}_DIRECTORY on {path}{(dest != null ? $" to {dest}" : "")}", ConsoleColor.Yellow);
                return;
            }

            switch (stmt.Type)
            {
                case DirectoryOpType.Create:
                    Directory.CreateDirectory(path);
                    Logger.WriteLine($"Directory created: {path}", ConsoleColor.Green);
                    break;
                case DirectoryOpType.Delete:
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                        Logger.WriteLine($"Directory deleted: {path}", ConsoleColor.Green);
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
                        if (Directory.Exists(target)) Directory.Delete(target, true);
                        Directory.Move(path, target);
                    }
                    break;
                case DirectoryOpType.Copy:
                    if (dest != null)
                    {
                        CopyDirectory(path, dest, true);
                        Logger.WriteLine($"Directory copied: {path} -> {dest}", ConsoleColor.Green);
                    }
                    break;
                case DirectoryOpType.DeleteContents:
                    DeleteDirectoryContents(path, true);
                    Logger.WriteLine($"Directory contents deleted: {path}", ConsoleColor.Green);
                    break;
            }
            await Task.CompletedTask;
        }

        private void CopyDirectory(string sourceDir, string destinationDir, bool recursive)
        {
            var dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists) throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");

            DirectoryInfo[] dirs = dir.GetDirectories();
            Directory.CreateDirectory(destinationDir);

            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath, true);
            }

            if (recursive)
            {
                foreach (DirectoryInfo subDir in dirs)
                {
                    string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                    CopyDirectory(subDir.FullName, newDestinationDir, true);
                }
            }
        }

        private void DeleteDirectoryContents(string path, bool recursive)
        {
            var dir = new DirectoryInfo(path);
            if (!dir.Exists) return;

            foreach (FileInfo file in dir.GetFiles()) file.Delete();
            foreach (DirectoryInfo subDir in dir.GetDirectories()) subDir.Delete(recursive);
        }
    }
}
