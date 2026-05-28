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
            
            registry.RegisterWithHelp("FILE_HASH", FileHash, "FILE_HASH(path [, algorithm]): Computes the cryptographic hash of a file (MD5, SHA1, SHA256, SHA512).");
            registry.RegisterWithHelp("FILE_SIZE", FileSize, "FILE_SIZE(path): Returns the size of a local file in bytes.");
            registry.RegisterWithHelp("FILE_MODIFIED", FileModified, "FILE_MODIFIED(path): Returns the last write timestamp of a file as a DATETIME.");
            registry.RegisterWithHelp("PATH_COMBINE", PathCombine, "PATH_COMBINE(p1, p2 [, ...]): Combines multiple path segments into a single path.");
            registry.RegisterWithHelp("PATH_FILENAME", PathFilename, "PATH_FILENAME(path): Extracts the filename and extension from a path.");
            registry.RegisterWithHelp("PATH_EXTENSION", PathExtension, "PATH_EXTENSION(path): Extracts the extension from a path.");
            registry.RegisterWithHelp("PATH_DIRECTORY", PathDirectory, "PATH_DIRECTORY(path): Extracts the directory information from a path.");
        }

        private static object? FileHash(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 1 || args[0] == null) return null;
            string rawPath = args[0]?.ToString() ?? "";
            string resolvedPath = context.ResolvePath(rawPath);
            if (!File.Exists(resolvedPath)) return null;

            string algo = "SHA256";
            if (args.Count >= 2 && args[1] != null)
            {
                algo = args[1]!.ToString()!.ToUpperInvariant();
            }

            using var stream = File.OpenRead(resolvedPath);
            byte[] hashBytes;
            if (algo == "MD5")
            {
                using var hasher = System.Security.Cryptography.MD5.Create();
                hashBytes = hasher.ComputeHash(stream);
            }
            else if (algo == "SHA1" || algo == "SHA-1")
            {
                using var hasher = System.Security.Cryptography.SHA1.Create();
                hashBytes = hasher.ComputeHash(stream);
            }
            else if (algo == "SHA256" || algo == "SHA-256")
            {
                using var hasher = System.Security.Cryptography.SHA256.Create();
                hashBytes = hasher.ComputeHash(stream);
            }
            else if (algo == "SHA512" || algo == "SHA-512")
            {
                using var hasher = System.Security.Cryptography.SHA512.Create();
                hashBytes = hasher.ComputeHash(stream);
            }
            else
            {
                throw new ExecutionException($"Unsupported hash algorithm: {algo}. Supported algorithms: MD5, SHA1, SHA256, SHA512.");
            }

            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        private static object? FileSize(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 1 || args[0] == null) return null;
            string resolvedPath = context.ResolvePath(args[0]?.ToString() ?? "");
            if (!File.Exists(resolvedPath)) return null;
            return (decimal)new FileInfo(resolvedPath).Length;
        }

        private static object? FileModified(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 1 || args[0] == null) return null;
            string resolvedPath = context.ResolvePath(args[0]?.ToString() ?? "");
            if (!File.Exists(resolvedPath)) return null;
            return new FileInfo(resolvedPath).LastWriteTime;
        }

        private static object? PathCombine(List<object?> args, IExecutionContext context)
        {
            if (args.Count == 0) return null;
            var segments = args.Where(a => a != null).Select(a => a!.ToString()!).ToArray();
            if (segments.Length == 0) return null;
            return Path.Combine(segments);
        }

        private static object? PathFilename(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 1 || args[0] == null) return null;
            return Path.GetFileName(args[0]?.ToString() ?? "");
        }

        private static object? PathExtension(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 1 || args[0] == null) return null;
            return Path.GetExtension(args[0]?.ToString() ?? "");
        }

        private static object? PathDirectory(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 1 || args[0] == null) return null;
            return Path.GetDirectoryName(args[0]?.ToString() ?? "");
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
