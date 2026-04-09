using System;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles SEND and RECEIVE statements for transferring files between local and remote systems.
    /// </summary>
    public class FileTransferStatementHandler : IStatementHandler
    {
        private readonly ILogger _logger;
        public Type SupportedStatementType => typeof(FileTransferStatement);

        public FileTransferStatementHandler(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>Executes the file transfer, resolving paths and invoking the remote filesystem provider.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (FileTransferStatement)statement;
            
            string localPathVal = (await context.EvaluateValue(stmt.LocalPath, new Row()))?.ToString() ?? "";
            string localPath = context.ResolvePath(localPathVal);
            string remotePath = (await context.EvaluateValue(stmt.RemotePath, new Row()))?.ToString() ?? "";
            
            bool overwrite = true;
            if (stmt.Overwrite != null)
            {
                var ovVal = await context.EvaluateValue(stmt.Overwrite, new Row());
                if (ovVal is bool b) overwrite = b;
                else if (ovVal != null) overwrite = ovVal.ToString()?.ToUpperInvariant() == "ON" || ovVal.ToString()?.ToUpperInvariant() == "TRUE";
            }

            if (!context.Connections.TryGetValue(stmt.ConnectionName, out var ds) || ds is not IRemoteFileSystem remoteFs)
            {
                throw new ExecutionException($"Connection '{stmt.ConnectionName}' not found or does not support remote file transfer.");
            }

            if (stmt.Type == FileTransferType.Send)
            {
                _logger.WriteLine($"SENDING: {localPath} -> {stmt.ConnectionName}:{remotePath} (OVERWRITE={(overwrite ? "ON" : "OFF")})", ConsoleColor.Cyan);
                
                if (context.IsWhatIf)
                {
                    _logger.WriteLine($"WHAT IF: Would send local file {localPath} to {stmt.ConnectionName}:{remotePath}", ConsoleColor.Yellow);
                    return;
                }

                if (!File.Exists(localPath)) throw new ExecutionException($"Local file not found: {localPath}");
                await remoteFs.UploadFileAsync(localPath, remotePath, overwrite);
                _logger.WriteLine("Upload complete.", ConsoleColor.Green);
            }
            else // Receive
            {
                _logger.WriteLine($"RECEIVING: {stmt.ConnectionName}:{remotePath} -> {localPath} (OVERWRITE={(overwrite ? "ON" : "OFF")})", ConsoleColor.Cyan);
                
                if (context.IsWhatIf)
                {
                    _logger.WriteLine($"WHAT IF: Would receive {stmt.ConnectionName}:{remotePath} to local file {localPath}", ConsoleColor.Yellow);
                    return;
                }

                var dir = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                await remoteFs.DownloadFileAsync(remotePath, localPath, overwrite);
                _logger.WriteLine("Download complete.", ConsoleColor.Green);
            }
        }
    }
}
