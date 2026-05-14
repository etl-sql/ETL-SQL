using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Functions;
using ETL_SQL.Data;
using ETL_SQL.Engine.Functions;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Functions
{
    /// <summary>
    /// Provides built-in functions for file system metadata and existence checks.
    /// Includes FILE_LIST, FILE_EXISTS, and REMOTE_FILE_LIST support.
    /// </summary>
    public static class FileFunctions
    {
        /// <summary>Registers file-related functions into the global function registry.</summary>
        public static void Register(IFunctionRegistry registry)
        {
            registry.RegisterWithHelp("REMOTE_FILE_LIST", RemoteFileList, "REMOTE_FILE_LIST(conn_str, [path]): Returns a table of files from a remote connection (SFTP/FTP/Blob).");
            registry.RegisterWithHelp("FILE_LIST", FileList, "FILE_LIST(path, [recursive]): Returns a table of files in a local directory.");
            registry.RegisterWithHelp("DIRECTORY", FileList, "DIRECTORY(path, [recursive]): Alias for FILE_LIST.");
            registry.RegisterWithHelp("FILE_EXISTS", FileExists, "FILE_EXISTS(path): Returns true if the file exists.");
            registry.RegisterWithHelp("DIRECTORY_EXISTS", DirectoryExists, "DIRECTORY_EXISTS(path): Returns true if the directory exists.");
        }

        private static async Task<object?> RemoteFileList(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 1) throw new ExecutionException("REMOTE_FILE_LIST requires at least a connection name.");
            string connName = args[0]?.ToString() ?? "";
            string path = args.Count > 1 ? args[1]?.ToString() ?? "" : "";
            
            if (!context.Connections.TryGetValue(connName, out var ds) || ds is not IRemoteFileSystem remoteFs)
            {
                throw new ExecutionException($"Connection '{connName}' not found or does not support IRemoteFileSystem.");
            }

            var table = new DataTable();
            table.SetColumns(new[] { "Name", "FullPath", "Size", "LastModified", "IsDirectory" });
            await foreach (var fileMeta in remoteFs.ListFilesAsync(path))
            {
                await table.AddRowAsync(new Row
                {
                    ["Name"] = fileMeta.Name,
                    ["FullPath"] = fileMeta.FullPath,
                    ["Size"] = fileMeta.Size,
                    ["LastModified"] = fileMeta.LastModified,
                    ["IsDirectory"] = fileMeta.IsDirectory
                });
            }
            return table;
        }

        private static Task<object?> FileExists(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 1 || args[0] == null) return Task.FromResult<object?>(null);
            var res = File.Exists(context.ResolvePath(args[0]?.ToString() ?? ""));
            return Task.FromResult<object?>((object?)(res ? 1m : 0m));
        }

        private static Task<object?> DirectoryExists(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 1 || args[0] == null) return Task.FromResult<object?>(null);
            var res = Directory.Exists(context.ResolvePath(args[0]?.ToString() ?? ""));
            return Task.FromResult<object?>((object?)(res ? 1m : 0m));
        }

        private static async Task<object?> FileList(List<object?> args, IExecutionContext context)
        {
            var table = new DataTable();
            table.SetColumns(new[] { "Name", "Path", "Extension", "Size", "LastModified" });

            if (args.Count < 1 || args[0] == null) return table;
            string path = context.ResolvePath(args[0]?.ToString() ?? "");
            bool recursive = args.Count >= 2 && args[1] != null && (args[1] is bool b ? b : (args[1] is string s ? s.Equals("TRUE", StringComparison.OrdinalIgnoreCase) : Convert.ToBoolean(args[1])));
            
            if (!Directory.Exists(path)) return table;
            
            var files = Directory.GetFiles(path, "*", (recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly));
            foreach (var fPath in files)
            {
                var fi = new FileInfo(fPath);
                await table.AddRowAsync(new Row
                {
                    ["Name"] = fi.Name,
                    ["Path"] = fi.FullName,
                    ["Extension"] = fi.Extension,
                    ["Size"] = (decimal)fi.Length,
                    ["LastModified"] = fi.LastWriteTime
                });
            }
            return table;
        }
    }
}
