using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the SPLIT FILE statement.
/// </summary>
public class SplitFileStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(SplitFileStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (SplitFileStatement)statement;

        string srcVal = (await context.EvaluateValue(stmt.Source, new Row()))?.ToString() ?? "";
        string destDirVal = (await context.EvaluateValue(stmt.DestinationDir, new Row()))?.ToString() ?? "";
        string limitType = (await context.EvaluateValue(stmt.LimitType, new Row()))?.ToString() ?? "";
        string limitValStr = (await context.EvaluateValue(stmt.LimitValue, new Row()))?.ToString() ?? "";
        string prefix = stmt.Prefix != null ? (await context.EvaluateValue(stmt.Prefix, new Row()))?.ToString() ?? "" : "part_";

        string source = context.ResolvePath(srcVal);
        string destDir = context.ResolvePath(destDirVal);

        // Security check
        context.SecurityService.ValidatePath(source);
        context.SecurityService.ValidatePath(destDir);
        context.SecurityService.ValidateWriteAccess(destDir);

        // Runaway operation count
        context.IncrementOperationCount(OperationType.FileSystem, source, 1);

        if (context.IsWhatIf)
        {
            context.Log($"WHAT IF: Would split file '{source}' into directory '{destDir}' by {limitType} ({limitValStr})", ConsoleColor.Yellow);
            return;
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

        if (!File.Exists(source))
            throw new ExecutionException($"Source file not found for split: {source}", null, stmt.Line, stmt.Column);

        if (!Directory.Exists(destDir))
            Directory.CreateDirectory(destDir);

        limitType = limitType.Trim().ToUpperInvariant();
        if (limitType != "ROWS" && limitType != "SIZE")
            throw new ExecutionException($"Invalid split LIMIT_TYPE: '{limitType}'. Expected 'ROWS' or 'SIZE'.", null, stmt.Line, stmt.Column);

        long limitValue = 0;
        if (limitType == "SIZE")
        {
            limitValue = ParseSizeLimit(limitValStr);
        }
        else
        {
            if (!long.TryParse(limitValStr, out limitValue) || limitValue <= 0)
                throw new ExecutionException($"Invalid split LIMIT_VALUE for ROWS: '{limitValStr}'. Must be a positive integer.", null, stmt.Line, stmt.Column);
        }

        string extension = Path.GetExtension(source);
        ValidateSafePrefix(prefix, stmt);

        if (context.IsVerbose)
            context.Log($"[SplitFile] Splitting '{source}' into '{destDir}' using prefix '{prefix}' ({limitType} limit: {limitValue})");

        using (var reader = new StreamReader(source, Encoding.UTF8))
        {
            int fileIndex = 1;
            long currentCount = 0;
            StreamWriter? writer = null;
            string? currentDestFile = null;

            try
            {
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();

                    if (writer == null)
                    {
                        currentDestFile = ResolvePartPath(context, destDir, prefix, fileIndex, extension, stmt);
                        if (File.Exists(currentDestFile))
                        {
                            if (overwrite) File.Delete(currentDestFile);
                            else throw new ExecutionException($"Destination file already exists and OVERWRITE is OFF: {currentDestFile}");
                        }

                        writer = new StreamWriter(currentDestFile, false, Encoding.UTF8);
                        currentCount = 0;
                    }

                    await writer.WriteLineAsync(line);

                    if (limitType == "ROWS")
                    {
                        currentCount++;
                        if (currentCount >= limitValue)
                        {
                            writer.Dispose();
                            writer = null;
                            fileIndex++;
                        }
                    }
                    else // SIZE
                    {
                        // Approximate size by character length (roughly 1-2 bytes per char depending on encoding, but close enough)
                        // Or look at base stream length if we want exact bytes, but writer.BaseStream.Length is accurate.
                        await writer.FlushAsync();
                        if (writer.BaseStream.Position >= limitValue)
                        {
                            writer.Dispose();
                            writer = null;
                            fileIndex++;
                        }
                    }
                }
            }
            finally
            {
                writer?.Dispose();
            }
        }

        if (context.IsVerbose)
            context.Log($"[SplitFile] Split operation completed successfully.");
    }

    private static void ValidateSafePrefix(string prefix, SplitFileStatement stmt)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            throw new ExecutionException("SPLIT FILE PREFIX must not be empty.", null, stmt.Line, stmt.Column);

        if (Path.IsPathRooted(prefix)
            || prefix.Contains(Path.DirectorySeparatorChar)
            || prefix.Contains(Path.AltDirectorySeparatorChar)
            || prefix.Contains("..", StringComparison.Ordinal)
            || prefix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ExecutionException("SPLIT FILE PREFIX must be a filename-safe stem and must not contain path separators or parent-directory segments.", null, stmt.Line, stmt.Column);
        }
    }

    private static string ResolvePartPath(IExecutionContext context, string destDir, string prefix, int fileIndex, string extension, SplitFileStatement stmt)
    {
        var partPath = Path.GetFullPath(Path.Combine(destDir, $"{prefix}{fileIndex}{extension}"));
        var destRoot = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!partPath.StartsWith(destRoot, StringComparison.OrdinalIgnoreCase))
            throw new ExecutionException("Generated split file path escaped the destination directory.", null, stmt.Line, stmt.Column);

        context.SecurityService.ValidatePath(partPath);
        context.SecurityService.ValidateWriteAccess(partPath);
        context.SecurityService.ValidateFileType(partPath);
        return partPath;
    }

    private long ParseSizeLimit(string sizeStr)
    {
        sizeStr = sizeStr.Trim().ToUpperInvariant();
        long multiplier = 1;
        if (sizeStr.EndsWith("GB") || sizeStr.EndsWith("G")) { multiplier = 1024L * 1024L * 1024L; sizeStr = sizeStr.TrimEnd('G', 'B'); }
        else if (sizeStr.EndsWith("MB") || sizeStr.EndsWith("M")) { multiplier = 1024L * 1024L; sizeStr = sizeStr.TrimEnd('M', 'B'); }
        else if (sizeStr.EndsWith("KB") || sizeStr.EndsWith("K")) { multiplier = 1024L; sizeStr = sizeStr.TrimEnd('K', 'B'); }

        if (double.TryParse(sizeStr, out var val))
        {
            return (long)(val * multiplier);
        }
        throw new ArgumentException($"Invalid size format: '{sizeStr}'");
    }
}
