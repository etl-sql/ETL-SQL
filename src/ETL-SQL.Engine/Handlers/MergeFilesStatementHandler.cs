using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Core.Common.Exceptions;
using System.Text;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the MERGE FILES statement.
    /// </summary>
    public class MergeFilesStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(MergeFilesStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (MergeFilesStatement)statement;

            var srcVal = await context.EvaluateValue(stmt.Source, new Row());
            string destVal = (await context.EvaluateValue(stmt.Destination, new Row()))?.ToString() ?? "";
            string dest = context.ResolvePath(destVal);

            // Security check
            context.SecurityService.ValidatePath(dest);
            context.SecurityService.ValidateWriteAccess(dest);
            context.SecurityService.ValidateFileType(dest);

            if (context.IsWhatIf)
            {
                context.Log($"WHAT IF: Would merge source files into '{dest}'", ConsoleColor.Yellow);
                return;
            }

            bool header = true;
            if (stmt.Header != null)
            {
                var hVal = await context.EvaluateValue(stmt.Header, new Row());
                if (hVal != null)
                {
                    if (hVal is bool b) header = b;
                    else if (string.Equals(hVal.ToString(), "OFF", StringComparison.OrdinalIgnoreCase)) header = false;
                    else if (string.Equals(hVal.ToString(), "FALSE", StringComparison.OrdinalIgnoreCase)) header = false;
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

            if (File.Exists(dest))
            {
                if (overwrite) File.Delete(dest);
                else throw new ExecutionException($"Destination file already exists and OVERWRITE is OFF: {dest}", null, stmt.Line, stmt.Column);
            }

            // Resolve list of source files
            var files = new List<string>();
            if (srcVal is string srcStr)
            {
                string resolvedSrc = context.ResolvePath(srcStr);
                if (resolvedSrc.Contains("*") || resolvedSrc.Contains("?"))
                {
                    string dir = Path.GetDirectoryName(resolvedSrc) ?? "";
                    if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
                    string pattern = Path.GetFileName(resolvedSrc);
                    
                    context.SecurityService.ValidatePath(dir);
                    if (Directory.Exists(dir))
                    {
                        var matched = Directory.GetFiles(dir, pattern);
                        Array.Sort(matched, StringComparer.OrdinalIgnoreCase);
                        files.AddRange(matched);
                    }
                }
                else
                {
                    context.SecurityService.ValidatePath(resolvedSrc);
                    if (File.Exists(resolvedSrc)) files.Add(resolvedSrc);
                }
            }
            else if (srcVal is System.Collections.IEnumerable list)
            {
                foreach (var item in list)
                {
                    if (item == null) continue;
                    string path = "";
                    if (item is Row r)
                    {
                        if (r.HasColumn("Path")) path = r["Path"]?.ToString() ?? "";
                        else if (r.HasColumn("FullPath")) path = r["FullPath"]?.ToString() ?? "";
                        else if (r.Columns.Count > 0) path = r[r.Columns.Keys.First()]?.ToString() ?? "";
                    }
                    else
                    {
                        path = item.ToString() ?? "";
                    }

                    if (!string.IsNullOrEmpty(path))
                    {
                        string resolved = context.ResolvePath(path);
                        context.SecurityService.ValidatePath(resolved);
                        if (File.Exists(resolved)) files.Add(resolved);
                    }
                }
            }

            if (files.Count == 0)
            {
                if (context.IsVerbose) context.Log("[MergeFiles] No source files found to merge.");
                using (File.Create(dest)) { }
                return;
            }

            if (context.IsVerbose)
                context.Log($"[MergeFiles] Merging {files.Count} files into '{dest}' (Header strip: {header})");

            using (var writer = new StreamWriter(dest, false, Encoding.UTF8))
            {
                bool isFirstFile = true;
                foreach (var file in files)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    context.IncrementOperationCount(OperationType.FileSystem, file, 1);

                    using (var reader = new StreamReader(file, Encoding.UTF8))
                    {
                        if (header && !isFirstFile)
                        {
                            await reader.ReadLineAsync(); // Skip header
                        }

                        string? line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            await writer.WriteLineAsync(line);
                        }
                    }
                    isFirstFile = false;
                }
            }

            if (context.IsVerbose)
                context.Log("[MergeFiles] Merge completed successfully.");
        }
    }
}
