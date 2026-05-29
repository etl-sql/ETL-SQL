using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the SYNC DIRECTORY statement.
    /// </summary>
    public class SyncDirectoryStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(SyncDirectoryStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (SyncDirectoryStatement)statement;

            string srcVal = (await context.EvaluateValue(stmt.Source, new Row()))?.ToString() ?? "";
            string destVal = (await context.EvaluateValue(stmt.Destination, new Row()))?.ToString() ?? "";

            string source = context.ResolvePath(srcVal);
            string dest = context.ResolvePath(destVal);

            // Security checks
            context.SecurityService.ValidatePath(source);
            context.SecurityService.ValidatePath(dest);
            context.SecurityService.ValidateWriteAccess(dest);

            if (context.IsWhatIf)
            {
                context.Log($"WHAT IF: Would sync directory '{source}' to '{dest}'", ConsoleColor.Yellow);
                return;
            }

            bool deleteExtra = false;
            if (stmt.DeleteExtra != null)
            {
                var de = await context.EvaluateValue(stmt.DeleteExtra, new Row());
                if (de != null)
                {
                    if (de is bool b) deleteExtra = b;
                    else if (string.Equals(de.ToString(), "ON", StringComparison.OrdinalIgnoreCase)) deleteExtra = true;
                    else if (string.Equals(de.ToString(), "TRUE", StringComparison.OrdinalIgnoreCase)) deleteExtra = true;
                }
            }

            bool overwrite = true;
            if (stmt.Overwrite != null)
            {
                var ovr = await context.EvaluateValue(stmt.Overwrite, new Row());
                if (ovr != null)
                {
                    if (ovr is bool b) overwrite = b;
                    else if (string.Equals(ovr.ToString(), "OFF", StringComparison.OrdinalIgnoreCase)) overwrite = false;
                    else if (string.Equals(ovr.ToString(), "FALSE", StringComparison.OrdinalIgnoreCase)) overwrite = false;
                }
            }

            bool recursive = false;
            if (stmt.Recursive != null)
            {
                var rec = await context.EvaluateValue(stmt.Recursive, new Row());
                if (rec != null)
                {
                    if (rec is bool b) recursive = b;
                    else if (string.Equals(rec.ToString(), "ON", StringComparison.OrdinalIgnoreCase)) recursive = true;
                    else if (string.Equals(rec.ToString(), "TRUE", StringComparison.OrdinalIgnoreCase)) recursive = true;
                }
            }

            if (!Directory.Exists(source))
                throw new ExecutionException($"Source directory not found: {source}", null, stmt.Line, stmt.Column);

            if (!Directory.Exists(dest))
                Directory.CreateDirectory(dest);

            if (context.IsVerbose)
                context.Log($"[SyncDirectory] Syncing '{source}' -> '{dest}' (Recursive: {recursive}, Overwrite: {overwrite}, DeleteExtra: {deleteExtra})");

            var sourceFiles = Directory.GetFiles(source, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            var destFiles = Directory.GetFiles(dest, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

            var sourceFileMap = sourceFiles.ToDictionary(
                f => Path.GetRelativePath(source, f),
                f => f,
                StringComparer.OrdinalIgnoreCase
            );

            var destFileMap = destFiles.ToDictionary(
                f => Path.GetRelativePath(dest, f),
                f => f,
                StringComparer.OrdinalIgnoreCase
            );

            // 1. Copy new or modified files
            foreach (var kvp in sourceFileMap)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                string relativePath = kvp.Key;
                string sourceFile = kvp.Value;
                string targetFile = Path.Combine(dest, relativePath);

                bool needsCopy = false;
                if (!destFileMap.TryGetValue(relativePath, out var existingDestFile))
                {
                    needsCopy = true;
                }
                else
                {
                    var sourceInfo = new FileInfo(sourceFile);
                    var destInfo = new FileInfo(existingDestFile);
                    if (sourceInfo.Length != destInfo.Length || sourceInfo.LastWriteTime > destInfo.LastWriteTime)
                    {
                        needsCopy = overwrite;
                    }
                }

                if (needsCopy)
                {
                    string? targetDir = Path.GetDirectoryName(targetFile);
                    if (targetDir != null && !Directory.Exists(targetDir))
                        Directory.CreateDirectory(targetDir);

                    context.SecurityService.ValidateWriteAccess(targetFile);
                    context.SecurityService.ValidateFileType(targetFile);

                    context.IncrementOperationCount(OperationType.FileSystem, sourceFile, 1);
                    File.Copy(sourceFile, targetFile, true);
                    
                    if (context.IsVerbose)
                        context.Log($"[SyncDirectory] Copied: {relativePath}");
                }
            }

            // 2. Delete extra files if configured
            if (deleteExtra)
            {
                foreach (var kvp in destFileMap)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    string relativePath = kvp.Key;
                    string destFile = kvp.Value;

                    if (!sourceFileMap.ContainsKey(relativePath))
                    {
                        context.SecurityService.ValidateWriteAccess(destFile);
                        File.Delete(destFile);
                        
                        if (context.IsVerbose)
                            context.Log($"[SyncDirectory] Deleted: {relativePath}");
                    }
                }

                if (recursive)
                {
                    DeleteEmptySubdirectories(dest);
                }
            }
        }

        private void DeleteEmptySubdirectories(string directory)
        {
            foreach (var d in Directory.GetDirectories(directory))
            {
                DeleteEmptySubdirectories(d);
                if (Directory.GetFiles(d).Length == 0 && Directory.GetDirectories(d).Length == 0)
                {
                    Directory.Delete(d, false);
                }
            }
        }
    }
}
