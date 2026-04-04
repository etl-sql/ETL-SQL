using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Functions;
using ETL_SQL.Data;
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
            registry.Register("REMOTE_FILE_LIST", RemoteFileList);
            registry.Register("FILE_LIST", FileList);
            registry.Register("DIRECTORY", FileList); // Alias for consistency
            registry.Register("FILE_EXISTS", FileExists);
            registry.Register("DIRECTORY_EXISTS", DirectoryExists);
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

            var files = await remoteFs.ListFilesAsync(path);
            var table = new DataTable();
            table.ColumnNames.AddRange(new[] { "Name", "FullPath", "Size", "LastModified", "IsDirectory" });
            foreach (var fileMeta in files)
            {
                table.AddRow(new Row
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
            var res = args.Count >= 1 && args[0] != null ? File.Exists(context.ResolvePath(args[0]?.ToString() ?? "")) : false;
            return Task.FromResult<object?>(res);
        }

        private static Task<object?> DirectoryExists(List<object?> args, IExecutionContext context)
        {
            var res = args.Count >= 1 && args[0] != null ? Directory.Exists(context.ResolvePath(args[0]?.ToString() ?? "")) : false;
            return Task.FromResult<object?>(res);
        }

        private static Task<object?> FileList(List<object?> args, IExecutionContext context)
        {
            var table = new DataTable();
            table.ColumnNames.AddRange(new[] { "Name", "Path", "Extension", "Size", "LastModified" });

            if (args.Count < 1 || args[0] == null) return Task.FromResult<object?>(table);
            string path = context.ResolvePath(args[0]?.ToString() ?? "");
            bool recursive = args.Count >= 2 && args[1] != null && (args[1] is bool b ? b : (args[1] is string s ? s.Equals("TRUE", StringComparison.OrdinalIgnoreCase) : Convert.ToBoolean(args[1])));
            
            if (!Directory.Exists(path)) return Task.FromResult<object?>(table);
            
            var files = Directory.GetFiles(path, "*", (recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly));
            foreach (var fPath in files)
            {
                var fi = new FileInfo(fPath);
                table.AddRow(new Row
                {
                    ["Name"] = fi.Name,
                    ["Path"] = fi.FullName,
                    ["Extension"] = fi.Extension,
                    ["Size"] = (decimal)fi.Length,
                    ["LastModified"] = fi.LastWriteTime
                });
            }
            return Task.FromResult<object?>(table);
        }
    }
}
