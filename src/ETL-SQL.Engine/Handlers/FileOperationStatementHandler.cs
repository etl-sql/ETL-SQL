using System;
using ETL_SQL.Core.Common.Exceptions;
using System.IO;
using System.Threading.Tasks;
using System.IO.Compression;
using ETL_SQL.Data;
using ETL_SQL.Common;

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
            
            
            string source = context.ResolvePath((await context.EvaluateValue(stmt.Source, new Row()))?.ToString() ?? "");
            string? dest = stmt.Destination != null ? context.ResolvePath((await context.EvaluateValue(stmt.Destination, new Row()))?.ToString() ?? "") : null;

            Logger.Verbose($"File Operation: {stmt.Type} on {source}{(dest != null ? $" -> {dest}" : "")}");
            switch (stmt.Type)
            {
                case FileOpType.Delete:
                    File.Delete(source);
                    Logger.WriteLine($"File deleted: {source}", ConsoleColor.Green);
                    break;
                case FileOpType.Copy:
                    if (dest != null) File.Copy(source, dest, true);
                    break;
                case FileOpType.Move:
                    if (dest != null) File.Move(source, dest, true);
                    break;
                case FileOpType.Rename:
                    if (dest != null)
                    {
                        var fileName = Path.GetFileName(source);
                        var dir = Path.GetDirectoryName(source) ?? "";
                        var newPath = Path.Combine(dir, dest);
                        if (File.Exists(newPath)) File.Delete(newPath);
                        File.Move(source, newPath);
                    }
                    else
                    {
                        throw new ExecutionException("RENAME_FILE requires a destination name.");
                    }
                    break;
                case FileOpType.Compress:
                    if (dest != null)
                    {
                        if (File.Exists(dest)) File.Delete(dest);
                        using (var zip = System.IO.Compression.ZipFile.Open(dest, System.IO.Compression.ZipArchiveMode.Create))
                        {
                            zip.CreateEntryFromFile(source, Path.GetFileName(source));
                        }
                    }
                    break;
                case FileOpType.Encrypt:
                    if (dest != null) CryptoUtils.EncryptFile(source, dest, "DefaultETLPass123!");
                    break;
                case FileOpType.Decrypt:
                    if (dest != null) CryptoUtils.DecryptFile(source, dest, "DefaultETLPass123!");
                    break;
            }
            await Task.CompletedTask;
        }
    }
}



