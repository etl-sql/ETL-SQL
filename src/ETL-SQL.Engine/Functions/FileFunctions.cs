using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;
using ETL_SQL.Engine.Functions;
using ETL_SQL.Engine.Services;

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
            registry.RegisterWithHelp("REMOTE_FILE_LIST", RemoteFileList, "REMOTE_FILE_LIST(conn_str, [path], [recursive]): Returns a table of files from a remote connection (SFTP/FTP/Blob/S3/SharePoint).");
            registry.RegisterWithHelp("REMOTE_FILE_EXISTS", RemoteFileExists, "REMOTE_FILE_EXISTS(connection, path): Returns 1 if the remote file (or directory, for backward compatibility) exists, or 0 otherwise. Prefer REMOTE_DIRECTORY_EXISTS for directories.");
            registry.RegisterWithHelp("REMOTE_DIRECTORY_EXISTS", RemoteDirectoryExists, "REMOTE_DIRECTORY_EXISTS(connection, path): Returns 1 if the remote directory exists, or 0 otherwise.");
            registry.RegisterWithHelp("REMOTE_FILE_SIZE", RemoteFileSize, "REMOTE_FILE_SIZE(connection, path): Returns the size in bytes of a remote file, or NULL if not found.");
            registry.RegisterWithHelp("REMOTE_FILE_MODIFIED", RemoteFileModified, "REMOTE_FILE_MODIFIED(connection, path): Returns the last modified timestamp of a remote file as a DATETIME, or NULL if not found or unavailable.");
            registry.RegisterWithHelp("FILE_LIST", FileList, "FILE_LIST(path, [recursive]): Returns a table of files in a local directory.");
            registry.RegisterWithHelp("ARCHIVE_LIST", ArchiveList, "ARCHIVE_LIST(path): Returns a table of archive entries (Name, FullPath, Size, CompressedSize, LastModified, IsDirectory) for .zip, .tar, .tar.gz, .tgz, or .gz files without extracting to disk.");
            registry.RegisterWithHelp("EXCEL_SHEET_LIST", ExcelSheetList, "EXCEL_SHEET_LIST(path_or_connection): Returns a table of worksheets (SheetIndex, SheetName) in an Excel workbook (.xlsx, .xls, .xlsb) or EXCEL connection.");
            registry.RegisterWithHelp("DIRECTORY", FileList, "DIRECTORY(path, [recursive]): Alias for FILE_LIST.");
            registry.RegisterWithHelp("FILE_EXISTS", FileExists, "FILE_EXISTS(path): Returns true if the file exists.");
            registry.RegisterWithHelp("DIRECTORY_EXISTS", DirectoryExists, "DIRECTORY_EXISTS(path): Returns true if the directory exists.");
            registry.RegisterWithHelp("DIRECTORY_SIZE", DirectorySize, "DIRECTORY_SIZE(path [, recursive]): Returns the total size in bytes of files in a local directory.");
            registry.RegisterWithHelp("DIRECTORY_FILE_COUNT", DirectoryFileCount, "DIRECTORY_FILE_COUNT(path [, recursive]): Returns the number of files in a local directory.");

            registry.RegisterWithHelp("FILE_HASH", FileHash, "FILE_HASH(path [, algorithm]): Computes the cryptographic hash of a file (MD5, SHA1, SHA256, SHA512).");
            registry.RegisterWithHelp("FILE_SIZE", FileSize, "FILE_SIZE(path): Returns the size of a local file in bytes.");
            registry.RegisterWithHelp("FILE_LINE_COUNT", FileLineCount, "FILE_LINE_COUNT(path [, header_rows]): Streams a local file once and returns the number of lines after skipping optional header_rows.");
            registry.RegisterWithHelp("FILE_CREATED", FileCreated, "FILE_CREATED(path): Returns the creation timestamp of a local file as a DATETIME.");
            registry.RegisterWithHelp("FILE_MODIFIED", FileModified, "FILE_MODIFIED(path): Returns the last write timestamp of a file as a DATETIME.");
            registry.RegisterWithHelp("FILE_ACCESSED", FileAccessed, "FILE_ACCESSED(path): Returns the last access timestamp of a local file as a DATETIME.");
            registry.RegisterWithHelp("FILE_READONLY", FileReadOnly, "FILE_READONLY(path): Returns 1 if the local file is read-only, or 0 otherwise.");
            registry.RegisterWithHelp("PATH_COMBINE", PathCombine, "PATH_COMBINE(p1, p2 [, ...]): Combines multiple path segments into a single path.");
            registry.RegisterWithHelp("PATH_FILENAME", PathFilename, "PATH_FILENAME(path): Extracts the filename and extension from a path.");
            registry.RegisterWithHelp("PATH_FILENAME_NO_EXT", PathFilenameNoExt, "PATH_FILENAME_NO_EXT(path): Extracts the filename without its extension from a path.");
            registry.RegisterWithHelp("PATH_EXTENSION", PathExtension, "PATH_EXTENSION(path): Extracts the extension from a path.");
            registry.RegisterWithHelp("PATH_CHANGE_EXTENSION", PathChangeExtension, "PATH_CHANGE_EXTENSION(path, ext): Replaces or removes the extension of a path (accepts '.bak', 'bak', or '' to remove).");
            registry.RegisterWithHelp("PATH_DIRECTORY", PathDirectory, "PATH_DIRECTORY(path): Extracts the directory information from a path.");
            registry.RegisterWithHelp("PATH_ROOT", PathRoot, "PATH_ROOT(path): Extracts the root directory information from a path.");
            registry.RegisterWithHelp("PATH_IS_ABSOLUTE", PathIsAbsolute, "PATH_IS_ABSOLUTE(path): Returns 1 if the path is fully qualified (absolute), or 0 otherwise.");
        }

        /// <summary>
        /// Resolves and authorizes a script-selected local path at the canonical filesystem
        /// policy boundary (safe zones plus enterprise approved roots and policy freshness).
        /// </summary>
        private static string AuthorizeLocalPath(IExecutionContext context, string rawPath, FileSystemAccessKind access) =>
            new FileSystemPolicyAuthorizer(context.SecurityService)
                .Authorize(context, context.ResolvePath(rawPath), access, validateFileType: false)
                .CanonicalPath;

        private static object? FileHash(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 1 || args[0] == null) return null;
            string rawPath = args[0]?.ToString() ?? "";
            string resolvedPath = AuthorizeLocalPath(context, rawPath, FileSystemAccessKind.Read);
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
            string resolvedPath = AuthorizeLocalPath(context, args[0]?.ToString() ?? "", FileSystemAccessKind.Read);
            if (!File.Exists(resolvedPath)) return null;
            return (decimal)new FileInfo(resolvedPath).Length;
        }

        private static object? FileCreated(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 1 || args[0] == null) return null;
            string resolvedPath = AuthorizeLocalPath(context, args[0]?.ToString() ?? "", FileSystemAccessKind.Read);
            if (!File.Exists(resolvedPath)) return null;
            return new FileInfo(resolvedPath).CreationTime;
        }

        private static object? FileModified(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 1 || args[0] == null) return null;
            string resolvedPath = AuthorizeLocalPath(context, args[0]?.ToString() ?? "", FileSystemAccessKind.Read);
            if (!File.Exists(resolvedPath)) return null;
            return new FileInfo(resolvedPath).LastWriteTime;
        }

        private static object? FileAccessed(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 1 || args[0] == null) return null;
            string resolvedPath = AuthorizeLocalPath(context, args[0]?.ToString() ?? "", FileSystemAccessKind.Read);
            if (!File.Exists(resolvedPath)) return null;
            return new FileInfo(resolvedPath).LastAccessTime;
        }

        private static object? FileReadOnly(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 1 || args[0] == null) return null;
            string resolvedPath = AuthorizeLocalPath(context, args[0]?.ToString() ?? "", FileSystemAccessKind.Read);
            if (!File.Exists(resolvedPath)) return null;
            return new FileInfo(resolvedPath).IsReadOnly ? 1m : 0m;
        }

        private static async Task<object?> FileLineCount(List<object?> args, IExecutionContext context)
        {
            if (args.Count > 2)
                throw new ExecutionException("FILE_LINE_COUNT accepts at most 2 arguments: path and optional header_rows.");
            if (args.Count < 1 || args[0] == null) return null;

            long headerRows = 0;
            if (args.Count == 2)
            {
                if (args[1] == null) return null;
                headerRows = ParseHeaderRows(args[1]!);
            }

            string resolvedPath = AuthorizeLocalPath(context, args[0]?.ToString() ?? "", FileSystemAccessKind.Read);
            if (!File.Exists(resolvedPath)) return null;

            long lines = 0;
            bool prevWasCr = false;
            bool hasCharsAfterLastBreak = false;
            var buffer = new char[65536];

            await using var stream = new FileStream(
                resolvedPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                65536,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);

            while (true)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                int read = await reader.ReadAsync(buffer.AsMemory(), context.CancellationToken).ConfigureAwait(false);
                if (read == 0) break;

                for (int i = 0; i < read; i++)
                {
                    char c = buffer[i];
                    if (c == '\r')
                    {
                        lines++;
                        prevWasCr = true;
                        hasCharsAfterLastBreak = false;
                    }
                    else if (c == '\n')
                    {
                        if (prevWasCr)
                        {
                            prevWasCr = false;
                        }
                        else
                        {
                            lines++;
                            hasCharsAfterLastBreak = false;
                        }
                    }
                    else
                    {
                        prevWasCr = false;
                        hasCharsAfterLastBreak = true;
                    }
                }
            }

            if (hasCharsAfterLastBreak)
                lines++;

            return (decimal)Math.Max(0L, lines - headerRows);
        }

        private static long ParseHeaderRows(object value)
        {
            switch (value)
            {
                case int i when i >= 0: return i;
                case long l when l >= 0: return l;
                case short s when s >= 0: return s;
                case byte b: return b;
                case sbyte sb when sb >= 0: return sb;
                case uint ui: return ui;
                case ulong ul when ul <= long.MaxValue: return (long)ul;
                case decimal d when d >= 0 && d == Math.Truncate(d) && d <= long.MaxValue:
                    return (long)d;
                case double dbl when double.IsFinite(dbl) && dbl >= 0 && dbl == Math.Truncate(dbl) && dbl <= long.MaxValue:
                    return (long)dbl;
                case float flt when float.IsFinite(flt) && flt >= 0 && flt == MathF.Truncate(flt) && flt <= long.MaxValue:
                    return (long)flt;
                case string str when long.TryParse(str.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0:
                    return parsed;
                default:
                    throw new ExecutionException($"FILE_LINE_COUNT header_rows must be a non-negative integer, not '{value}'.");
            }
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

        private static object? PathFilenameNoExt(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 1 || args[0] == null) return null;
            return Path.GetFileNameWithoutExtension(args[0]?.ToString() ?? "");
        }

        private static object? PathExtension(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 1 || args[0] == null) return null;
            return Path.GetExtension(args[0]?.ToString() ?? "");
        }

        private static object? PathChangeExtension(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 2)
                throw new ExecutionException("PATH_CHANGE_EXTENSION requires 2 arguments: path and ext.");
            if (args[0] == null || args[1] == null) return null;

            string path = args[0]!.ToString()!;
            string ext = args[1]!.ToString()!;
            return string.IsNullOrEmpty(ext)
                ? Path.ChangeExtension(path, null)
                : Path.ChangeExtension(path, ext);
        }

        private static object? PathDirectory(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 1 || args[0] == null) return null;
            return Path.GetDirectoryName(args[0]?.ToString() ?? "");
        }

        private static object? PathRoot(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 1 || args[0] == null) return null;
            return Path.GetPathRoot(args[0]?.ToString() ?? "") ?? "";
        }

        private static object? PathIsAbsolute(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 1 || args[0] == null) return null;
            return Path.IsPathFullyQualified(args[0]?.ToString() ?? "") ? 1m : 0m;
        }

        private static async Task<object?> RemoteFileList(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 1) throw new ExecutionException("REMOTE_FILE_LIST requires at least a connection name.");
            if (args.Count > 3) throw new ExecutionException("REMOTE_FILE_LIST accepts at most 3 arguments: connection_name, optional path, and optional recursive flag.");
            string connName = args[0]?.ToString() ?? "";
            string path = args.Count > 1 ? args[1]?.ToString() ?? "" : "";
            bool recursive = args.Count > 2 && args[2] != null && ParseRecursiveFlag(args[2]);
            var remoteFs = GetRemoteFileSystem(context, connName);

            var table = new DataTable();
            table.SetColumns(FileMetaData.StandardColumns);
            if (recursive)
            {
                await foreach (var entry in RemoteDirectoryWalker.WalkAsync(remoteFs, path, recursive: true, context, context.CancellationToken))
                {
                    var fileMeta = entry.Meta;
                    await table.AddRowAsync(new Row
                    {
                        ["Name"] = fileMeta.Name,
                        ["FullPath"] = fileMeta.FullPath,
                        ["Size"] = fileMeta.Size,
                        ["LastModified"] = fileMeta.LastModified,
                        ["IsDirectory"] = fileMeta.IsDirectory,
                        ["CreationTime"] = fileMeta.CreationTime,
                        ["Permissions"] = fileMeta.Permissions,
                        ["Owner"] = fileMeta.Owner,
                        ["IsSymlink"] = fileMeta.IsSymlink
                    });
                }
            }
            else
            {
                await foreach (var fileMeta in remoteFs.ListFilesAsync(path))
                {
                    await table.AddRowAsync(new Row
                    {
                        ["Name"] = fileMeta.Name,
                        ["FullPath"] = fileMeta.FullPath,
                        ["Size"] = fileMeta.Size,
                        ["LastModified"] = fileMeta.LastModified,
                        ["IsDirectory"] = fileMeta.IsDirectory,
                        ["CreationTime"] = fileMeta.CreationTime,
                        ["Permissions"] = fileMeta.Permissions,
                        ["Owner"] = fileMeta.Owner,
                        ["IsSymlink"] = fileMeta.IsSymlink
                    });
                }
            }
            return table;
        }

        private static Task<object?> FileExists(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 1 || args[0] == null) return Task.FromResult<object?>(null);
            var res = File.Exists(AuthorizeLocalPath(context, args[0]?.ToString() ?? "", FileSystemAccessKind.Read));
            return Task.FromResult<object?>((object?)(res ? 1m : 0m));
        }

        private static Task<object?> DirectoryExists(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 1 || args[0] == null) return Task.FromResult<object?>(null);
            var res = Directory.Exists(AuthorizeLocalPath(context, args[0]?.ToString() ?? "", FileSystemAccessKind.Read));
            return Task.FromResult<object?>((object?)(res ? 1m : 0m));
        }

        private static object? DirectorySize(List<object?> args, IExecutionContext context)
        {
            var stats = MeasureDirectory(args, context, "DIRECTORY_SIZE");
            return stats.HasValue ? (decimal)stats.Value.TotalBytes : null;
        }

        private static object? DirectoryFileCount(List<object?> args, IExecutionContext context)
        {
            var stats = MeasureDirectory(args, context, "DIRECTORY_FILE_COUNT");
            return stats.HasValue ? (decimal)stats.Value.FileCount : null;
        }

        private static bool ParseRecursiveFlag(object? arg) =>
            arg is bool b
                ? b
                : (arg is string s
                    ? s.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
                        || s.Equals("ON", StringComparison.OrdinalIgnoreCase)
                        || s == "1"
                    : Convert.ToBoolean(arg));

        private static bool IsReparseOrLink(FileSystemInfo entry) =>
            (entry.Attributes & FileAttributes.ReparsePoint) != 0 || entry.LinkTarget != null;

        private static (long FileCount, long TotalBytes)? MeasureDirectory(List<object?> args, IExecutionContext context, string functionName)
        {
            if (args.Count > 2)
                throw new ExecutionException($"{functionName} accepts at most 2 arguments: path and optional recursive flag.");
            if (args.Count < 1 || args[0] == null) return null;

            bool recursive = false;
            if (args.Count == 2)
            {
                if (args[1] == null) return null;
                recursive = ParseRecursiveFlag(args[1]);
            }

            string resolvedPath = AuthorizeLocalPath(context, args[0]?.ToString() ?? "", FileSystemAccessKind.Enumerate);
            if (!Directory.Exists(resolvedPath)) return null;

            long fileCount = 0;
            long totalBytes = 0;
            foreach (var fi in EnumerateAuthorizedDirectoryFiles(resolvedPath, recursive, depth: 0, context))
            {
                fileCount++;
                totalBytes += fi.Length;
            }
            return (fileCount, totalBytes);
        }

        private static IEnumerable<FileInfo> EnumerateAuthorizedDirectoryFiles(
            string currentDir,
            bool recursive,
            int depth,
            IExecutionContext context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var dirInfo = new DirectoryInfo(currentDir);

            foreach (var fi in dirInfo.EnumerateFiles())
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                // Skip symlink/reparse file entries so traversal never escapes the authorized root.
                if (IsReparseOrLink(fi)) continue;
                context.IncrementOperationCount(OperationType.FileSystem, fi.FullName);
                yield return fi;
            }

            if (!recursive) yield break;

            foreach (var subDir in dirInfo.EnumerateDirectories())
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                // Never follow directory junctions or symlinks out of the authorized root.
                if (IsReparseOrLink(subDir)) continue;

                int nextDepth = depth + 1;
                context.SecurityService.CheckRunawayProtection(
                    OperationType.FileSystem,
                    0,
                    nextDepth,
                    allowLargeCount: context.AllowLargeFileOperationCount,
                    allowDeepRecursion: context.AllowDeepRecursion,
                    path: subDir.FullName);
                if (context.ExecutionPolicy?.IsEnrolled ?? EnterprisePolicyRuntime.Current.IsEnrolled)
                {
                    OperationPolicyBoundary.EnforceCeiling(
                        context,
                        "Security:MaxRecursiveNestingDepth",
                        nextDepth,
                        "<recursive-nesting-depth>");
                }

                foreach (var child in EnumerateAuthorizedDirectoryFiles(subDir.FullName, recursive: true, nextDepth, context))
                    yield return child;
            }
        }

        private static async Task<object?> FileList(List<object?> args, IExecutionContext context)
        {
            var table = new DataTable();
            table.SetColumns(new[] { "Name", "Path", "Extension", "Size", "LastModified" });

            if (args.Count < 1 || args[0] == null) return table;
            string path = AuthorizeLocalPath(context, args[0]?.ToString() ?? "", FileSystemAccessKind.Enumerate);
            bool recursive = args.Count >= 2 && args[1] != null && ParseRecursiveFlag(args[1]);

            if (!Directory.Exists(path)) return table;

            foreach (var fi in EnumerateAuthorizedDirectoryFiles(path, recursive, depth: 0, context))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
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

        private static async Task<object?> ArchiveList(List<object?> args, IExecutionContext context)
        {
            var table = new DataTable();
            table.SetColumns(new[] { "Name", "FullPath", "Size", "CompressedSize", "LastModified", "IsDirectory" });

            if (args.Count < 1 || args[0] == null) return table;
            string rawPath = args[0]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(rawPath)) return table;

            string resolvedPath = AuthorizeLocalPath(context, rawPath, FileSystemAccessKind.Read);
            if (!File.Exists(resolvedPath))
            {
                throw new ExecutionException($"Archive file not found: {rawPath}");
            }

            string? formatArg = args.Count >= 2 ? args[1]?.ToString() : null;
            var format = SafeArchiveService.ResolveFormat(formatArg, resolvedPath, defaultToZipIfUnknown: false);
            var entries = SafeArchiveService.ListEntries(resolvedPath, format, context.CancellationToken);

            foreach (var entry in entries)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                await table.AddRowAsync(new Row
                {
                    ["Name"] = entry.Name,
                    ["FullPath"] = entry.FullPath,
                    ["Size"] = entry.Size,
                    ["CompressedSize"] = entry.CompressedSize,
                    ["LastModified"] = entry.LastModified,
                    ["IsDirectory"] = entry.IsDirectory
                });
            }

            return table;
        }

        private static async Task<object?> RemoteFileExists(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 2 || args[0] == null || args[1] == null) return null;
            string connName = args[0]!.ToString()!;
            string path = args[1]!.ToString()!;
            var remoteFs = GetRemoteFileSystem(context, connName);

            try
            {
                var fileExists = await remoteFs.FileExistsAsync(path);
                if (fileExists) return 1m;

                var dirExists = await remoteFs.DirectoryExistsAsync(path);
                return dirExists ? 1m : 0m;
            }
            catch (Exception ex)
            {
                throw new ExecutionException($"REMOTE_FILE_EXISTS failed for '{path}' on '{connName}': {ex.Message}", ex);
            }
        }

        private static async Task<object?> RemoteDirectoryExists(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 2 || args[0] == null || args[1] == null) return null;
            string connName = args[0]!.ToString()!;
            string path = args[1]!.ToString()!;
            var remoteFs = GetRemoteFileSystem(context, connName);

            try
            {
                return await remoteFs.DirectoryExistsAsync(path) ? 1m : 0m;
            }
            catch (Exception ex)
            {
                throw new ExecutionException($"REMOTE_DIRECTORY_EXISTS failed for '{path}' on '{connName}': {ex.Message}", ex);
            }
        }

        private static async Task<object?> RemoteFileSize(List<object?> args, IExecutionContext context)
        {
            var meta = await LookupRemoteFileMetadataAsync(args, context, "REMOTE_FILE_SIZE");
            return meta == null ? null : (decimal)meta.Size;
        }

        private static async Task<object?> RemoteFileModified(List<object?> args, IExecutionContext context)
        {
            var meta = await LookupRemoteFileMetadataAsync(args, context, "REMOTE_FILE_MODIFIED");
            if (meta?.LastModified == null || meta.LastModified.Value == DateTime.MinValue)
                return null;
            return meta.LastModified.Value;
        }

        private static async Task<FileMetaData?> LookupRemoteFileMetadataAsync(
            List<object?> args,
            IExecutionContext context,
            string functionName)
        {
            if (args.Count < 2 || args[0] == null || args[1] == null) return null;
            string connName = args[0]!.ToString()!;
            string path = args[1]!.ToString()!;
            var remoteFs = GetRemoteFileSystem(context, connName);

            try
            {
                if (!await remoteFs.FileExistsAsync(path))
                    return null;

                string normalized = path.Replace('\\', '/');
                int lastSlash = normalized.LastIndexOf('/');
                string remoteDir = lastSlash >= 0 ? normalized[..(lastSlash + 1)] : "";
                string fileName = lastSlash >= 0 ? normalized[(lastSlash + 1)..] : normalized;
                if (string.IsNullOrEmpty(fileName))
                    return null;

                string targetFull = normalized.TrimStart('/');
                FileMetaData? fallback = null;
                await foreach (var item in remoteFs.ListFilesAsync(remoteDir).WithCancellation(context.CancellationToken))
                {
                    if (item.IsDirectory) continue;
                    var itemFull = item.FullPath.Replace('\\', '/').TrimStart('/');
                    if (string.Equals(itemFull, targetFull, StringComparison.Ordinal))
                        return item;

                    if (fallback == null &&
                        string.Equals(item.Name, fileName, StringComparison.Ordinal) &&
                        (string.IsNullOrEmpty(remoteDir) || itemFull.EndsWith("/" + targetFull, StringComparison.Ordinal)))
                    {
                        fallback = item;
                    }
                }

                return fallback;
            }
            catch (ExecutionException) { throw; }
            catch (Exception ex)
            {
                throw new ExecutionException($"{functionName} failed for '{path}' on '{connName}': {ex.Message}", ex);
            }
        }

        private static async Task<object?> ExcelSheetList(List<object?> args, IExecutionContext context)
        {
            if (args.Count < 1 || args[0] == null)
                throw new ExecutionException("EXCEL_SHEET_LIST requires a workbook path or EXCEL connection name.");

            string target = args[0]!.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(target))
                throw new ExecutionException("EXCEL_SHEET_LIST requires a non-empty workbook path or EXCEL connection name.");

            if (context.Connections.TryGetValue(target, out var existing)
                && existing.ConnectorType.Equals("EXCEL", StringComparison.OrdinalIgnoreCase))
            {
                return await ReadSheetListAsync(existing, context);
            }

            string authorizedPath = AuthorizeLocalPath(context, target, FileSystemAccessKind.Read);
            if (!File.Exists(authorizedPath))
                throw new ExecutionException($"EXCEL_SHEET_LIST workbook not found: {target}");

            var connector = ConnectorRegistry.Instance?.GetConnector("EXCEL")
                ?? throw new ExecutionException("EXCEL connector is not registered.");
            await using var ds = connector.CreateDataSource(context, authorizedPath);
            return await ReadSheetListAsync(ds, context);
        }

        // The connection's `_sheets` view lists the sheets and errors on an unreadable workbook, so the
        // function and `SELECT * FROM conn._sheets` share one path.
        private static async Task<DataTable> ReadSheetListAsync(IDataSource workbook, IExecutionContext context)
        {
            var sheetsView = workbook.WithTable("_sheets");
            DataTable? result = null;
            await foreach (var batch in sheetsView.ReadBatches(10000, context.CancellationToken))
            {
                if (result == null) { result = batch; continue; }
                foreach (var row in batch.Rows) await result.AddRowAsync(row);
            }
            if (result == null)
            {
                result = new DataTable();
                result.SetColumns(new[] { "SheetIndex", "SheetName" });
            }
            return result;
        }

        private static IRemoteFileSystem GetRemoteFileSystem(IExecutionContext context, string connName)
        {
            if (!context.Connections.TryGetValue(connName, out var ds) || ds is not IRemoteFileSystem remoteFs)
            {
                throw new ExecutionException($"Connection '{connName}' not found or does not support IRemoteFileSystem.");
            }
            return remoteFs;
        }
    }
}
