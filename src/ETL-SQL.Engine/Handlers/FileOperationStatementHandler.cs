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
    /// Handles various file operations including DELETE, COPY, MOVE, RENAME, COMPRESS, ENCRYPT, and DECRYPT.
    /// </summary>
    public class FileOperationStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(FileOperationStatement);
        /// <summary>Executes the file operation, resolving paths and performing the requested action.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (FileOperationStatement)statement;
            
            string sourceVal = (await context.EvaluateValue(stmt.Source, new Row()))?.ToString() ?? "";
            string source = context.ResolvePath(sourceVal);
            string? dest = stmt.Destination != null ? context.ResolvePath((await context.EvaluateValue(stmt.Destination, new Row()))?.ToString() ?? "") : null;
            
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

            Logger.Verbose($"File Operation: {stmt.Type} on {source}{(dest != null ? $" -> {dest}" : "")}");

            if (context.IsWhatIf)
            {
                Logger.WriteLine($"WHAT IF: Would perform {stmt.Type}_FILE on {source}{(dest != null ? $" to {dest}" : "")}", ConsoleColor.Yellow);
                return;
            }

            switch (stmt.Type)
            {
                case FileOpType.Delete:
                    if (File.Exists(source)) 
                    {
                        File.Delete(source);
                        Logger.WriteLine($"File deleted: {source}", ConsoleColor.Green);
                    }
                    break;
                case FileOpType.Copy:
                    if (dest != null)
                    {
                        if (File.Exists(dest) && !overwrite)
                             throw new ExecutionException($"Destination file already exists and OVERWRITE is OFF: {dest}");
                        File.Copy(source, dest, overwrite);
                    }
                    break;
                case FileOpType.Move:
                    if (dest != null)
                    {
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
                case FileOpType.Encrypt:
                    if (dest != null) CryptoUtils.EncryptFile(source, dest, "DefaultETLPass123!", overwrite);
                    break;
                case FileOpType.Decrypt:
                    if (dest != null) CryptoUtils.DecryptFile(source, dest, "DefaultETLPass123!", overwrite);
                    break;
            }
            await Task.CompletedTask;
        }
    }
}
