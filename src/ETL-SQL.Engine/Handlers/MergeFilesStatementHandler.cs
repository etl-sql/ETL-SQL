using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;
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
        var pathAuthorizer = new FileSystemPolicyAuthorizer(context.SecurityService);
        var destAuth = pathAuthorizer.Authorize(context, dest, FileSystemAccessKind.Write);
        dest = destAuth.CanonicalPath;

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

        // Resolve list of source files
        var files = new List<AuthorizedFileSystemPath>();
        if (srcVal is string srcStr)
        {
            string resolvedSrc = context.ResolvePath(srcStr);
            if (resolvedSrc.Contains("*") || resolvedSrc.Contains("?"))
            {
                string dir = Path.GetDirectoryName(resolvedSrc) ?? "";
                if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
                string pattern = Path.GetFileName(resolvedSrc);

                dir = pathAuthorizer.Authorize(context, dir, FileSystemAccessKind.Enumerate,
                    validateFileType: false).CanonicalPath;
                if (Directory.Exists(dir))
                {
                    var matched = Directory.GetFiles(dir, pattern);
                    Array.Sort(matched, StringComparer.OrdinalIgnoreCase);
                    files.AddRange(matched.Select(file => pathAuthorizer.Authorize(
                        context, file, FileSystemAccessKind.Read, validateFileType: false)));
                }
            }
            else
            {
                var sourceAuth = pathAuthorizer.Authorize(context, resolvedSrc, FileSystemAccessKind.Read,
                    validateFileType: false);
                if (File.Exists(sourceAuth.CanonicalPath)) files.Add(sourceAuth);
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
                    var sourceAuth = pathAuthorizer.Authorize(context, resolved, FileSystemAccessKind.Read,
                        validateFileType: false);
                    if (File.Exists(sourceAuth.CanonicalPath)) files.Add(sourceAuth);
                }
            }
        }

        if (files.Count == 0)
        {
            throw new ExecutionException("MERGE FILES found no source files to merge. Destination was left unchanged.", null, stmt.Line, stmt.Column);
        }

        if (File.Exists(dest))
        {
            if (!overwrite)
                throw new ExecutionException($"Destination file already exists and OVERWRITE is OFF: {dest}", null, stmt.Line, stmt.Column);

            var deleteDest = pathAuthorizer.Authorize(context, dest, FileSystemAccessKind.Delete);
            context.SecurityService.ValidateWriteAccess(deleteDest.CanonicalPath);
            context.SecurityService.ValidateFileType(deleteDest.CanonicalPath);
            pathAuthorizer.DeleteValidatedFile(context, deleteDest);
        }

        if (context.IsVerbose)
            context.Log($"[MergeFiles] Merging {files.Count} files into '{dest}' (Header strip: {header})");

        using (var writer = new StreamWriter(pathAuthorizer.OpenValidatedWrite(context, destAuth,
                   failIfExists: !overwrite), Encoding.UTF8))
        {
            bool isFirstFile = true;
            foreach (var file in files)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                context.IncrementOperationCount(OperationType.FileSystem, file.CanonicalPath, 1);

                using (var reader = new StreamReader(pathAuthorizer.OpenValidatedRead(context, file), Encoding.UTF8))
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
