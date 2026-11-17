using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Engine.Services;

public enum ArchiveFormat
{
    Zip,
    Gzip,
    Tar,
    TarGz
}

public sealed record ArchiveExtractionLimits(
    int MaxEntries = 100_000,
    long MaxEntryBytes = 4L * 1024 * 1024 * 1024,
    long MaxTotalBytes = 10L * 1024 * 1024 * 1024,
    double MaxCompressionRatio = 100.0,
    long RatioCheckMinBytes = 16L * 1024 * 1024)
{
    public static ArchiveExtractionLimits Default { get; } = new();
}

public sealed record ArchiveEntryInfo(
    string Name,
    string FullPath,
    decimal? Size,
    decimal? CompressedSize,
    DateTime LastModified,
    bool IsDirectory);

/// <summary>
/// Safe archive creation, inspection, and extraction across ZIP, GZIP, TAR, and TAR.GZ (.tgz).
/// </summary>
public static class SafeArchiveService
{
    public static ArchiveFormat ResolveFormat(
        string? explicitFormat,
        string? archivePath,
        bool defaultToZipIfUnknown)
    {
        if (!string.IsNullOrWhiteSpace(explicitFormat))
        {
            return explicitFormat.Trim().ToUpperInvariant() switch
            {
                "ZIP" => ArchiveFormat.Zip,
                "GZIP" => ArchiveFormat.Gzip,
                "TAR" => ArchiveFormat.Tar,
                "TAR_GZ" => ArchiveFormat.TarGz,
                _ => throw new ExecutionException(
                    $"Unsupported archive FORMAT '{explicitFormat}'. Supported formats: ZIP, GZIP, TAR, TAR_GZ.")
            };
        }

        if (!string.IsNullOrWhiteSpace(archivePath))
        {
            if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
                archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
            {
                return ArchiveFormat.TarGz;
            }
            if (archivePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                return ArchiveFormat.Gzip;
            }
            if (archivePath.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
            {
                return ArchiveFormat.Tar;
            }
            if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return ArchiveFormat.Zip;
            }
        }

        if (defaultToZipIfUnknown)
        {
            return ArchiveFormat.Zip;
        }

        throw new ExecutionException(
            $"Cannot determine archive format from '{archivePath}'. Use a .zip, .gz, .tar, .tar.gz, or .tgz extension, or specify WITH (FORMAT = ...).");
    }

    public static string DeriveDefaultCompressDestination(string sourcePath, ArchiveFormat format, bool isDirectory)
    {
        string basePath = isDirectory
            ? sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : sourcePath;
        if (string.IsNullOrEmpty(basePath))
        {
            basePath = sourcePath;
        }

        return format switch
        {
            ArchiveFormat.Zip => basePath + ".zip",
            ArchiveFormat.Gzip => basePath + ".gz",
            ArchiveFormat.Tar => basePath + ".tar",
            ArchiveFormat.TarGz => basePath + ".tar.gz",
            _ => basePath + ".zip"
        };
    }

    public static string DeriveDefaultDecompressDestination(string sourcePath, string sourceDisplay)
    {
        string? stripped = StripKnownArchiveSuffix(sourcePath);
        if (string.IsNullOrEmpty(stripped) || string.IsNullOrEmpty(Path.GetFileName(stripped)))
        {
            throw new ExecutionException(
                $"DECOMPRESS without TO requires a '.zip', '.gz', '.tar', '.tar.gz', or '.tgz' file extension on '{sourceDisplay}', or specify TO '<destination>'.");
        }
        return stripped;
    }

    public static string? StripKnownArchiveSuffix(string path)
    {
        string[] suffixes = [".tar.gz", ".tgz", ".zip", ".tar", ".gz"];
        foreach (var suffix in suffixes)
        {
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return path[..^suffix.Length];
            }
        }
        return null;
    }

    public static async Task CompressFileAsync(
        string sourceFile,
        Stream destStream,
        ArchiveFormat format,
        CancellationToken cancellationToken)
    {
        switch (format)
        {
            case ArchiveFormat.Zip:
                using (var archive = new ZipArchive(destStream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    archive.CreateEntryFromFile(sourceFile, Path.GetFileName(sourceFile));
                }
                break;

            case ArchiveFormat.Gzip:
                await using (var gz = new GZipStream(destStream, CompressionMode.Compress, leaveOpen: true))
                await using (var src = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await src.CopyToAsync(gz, cancellationToken).ConfigureAwait(false);
                }
                break;

            case ArchiveFormat.Tar:
                await using (var writer = new TarWriter(destStream, TarEntryFormat.Pax, leaveOpen: true))
                {
                    await writer.WriteEntryAsync(sourceFile, Path.GetFileName(sourceFile), cancellationToken).ConfigureAwait(false);
                }
                break;

            case ArchiveFormat.TarGz:
                await using (var gz = new GZipStream(destStream, CompressionMode.Compress, leaveOpen: true))
                await using (var writer = new TarWriter(gz, TarEntryFormat.Pax, leaveOpen: true))
                {
                    await writer.WriteEntryAsync(sourceFile, Path.GetFileName(sourceFile), cancellationToken).ConfigureAwait(false);
                }
                break;

            default:
                throw new ExecutionException($"Unsupported archive format: {format}");
        }
    }

    public static async Task CompressDirectoryAsync(
        string sourceDirectory,
        Stream destStream,
        ArchiveFormat format,
        CancellationToken cancellationToken)
    {
        switch (format)
        {
            case ArchiveFormat.Zip:
                ZipFile.CreateFromDirectory(sourceDirectory, destStream);
                break;

            case ArchiveFormat.Gzip:
                throw new ExecutionException(
                    "GZIP format compresses a single file only. Use ZIP, TAR, or TAR_GZ to compress a directory.");

            case ArchiveFormat.Tar:
                await TarFile.CreateFromDirectoryAsync(sourceDirectory, destStream, includeBaseDirectory: false, cancellationToken).ConfigureAwait(false);
                break;

            case ArchiveFormat.TarGz:
                await using (var gz = new GZipStream(destStream, CompressionMode.Compress, leaveOpen: true))
                {
                    await TarFile.CreateFromDirectoryAsync(sourceDirectory, gz, includeBaseDirectory: false, cancellationToken).ConfigureAwait(false);
                }
                break;

            default:
                throw new ExecutionException($"Unsupported archive format: {format}");
        }
    }

    public static List<ArchiveEntryInfo> ListEntries(
        string archivePath,
        ArchiveFormat format,
        CancellationToken cancellationToken,
        ArchiveExtractionLimits? limits = null)
    {
        limits ??= ArchiveExtractionLimits.Default;
        var entries = new List<ArchiveEntryInfo>();

        switch (format)
        {
            case ArchiveFormat.Zip:
                using (var archive = ZipFile.OpenRead(archivePath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (entries.Count >= limits.MaxEntries)
                        {
                            throw new InvalidDataException($"Archive exceeds maximum entry count ({limits.MaxEntries:N0}).");
                        }

                        var fullPath = NormalizeArchivePath(entry.FullName);
                        bool isDir = entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                                     entry.FullName.EndsWith("\\", StringComparison.Ordinal);
                        var name = GetEntryName(fullPath);
                        entries.Add(new ArchiveEntryInfo(
                            name,
                            fullPath,
                            (decimal)entry.Length,
                            (decimal)entry.CompressedLength,
                            entry.LastWriteTime.LocalDateTime,
                            isDir));
                    }
                }
                break;

            case ArchiveFormat.Tar:
                using (var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new TarReader(fs))
                {
                    ReadTarEntryInfos(reader, entries, limits, fs.Length, compressedSizeKnown: true, cancellationToken);
                }
                break;

            case ArchiveFormat.TarGz:
                using (var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                using (var reader = new TarReader(gz))
                {
                    ReadTarEntryInfos(reader, entries, limits, fs.Length, compressedSizeKnown: false, cancellationToken);
                }
                break;

            case ArchiveFormat.Gzip:
                var fi = new FileInfo(archivePath);
                var stripped = StripKnownArchiveSuffix(fi.Name) ?? fi.Name;
                entries.Add(new ArchiveEntryInfo(
                    stripped,
                    stripped,
                    null,
                    (decimal)fi.Length,
                    fi.LastWriteTime,
                    false));
                break;

            default:
                throw new ExecutionException($"Unsupported archive format: {format}");
        }

        return entries;
    }

    private static void ReadTarEntryInfos(
        TarReader reader,
        List<ArchiveEntryInfo> entries,
        ArchiveExtractionLimits limits,
        long archiveFileLength,
        bool compressedSizeKnown,
        CancellationToken cancellationToken)
    {
        long totalBytes = 0;
        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entries.Count >= limits.MaxEntries)
            {
                throw new InvalidDataException($"Archive exceeds maximum entry count ({limits.MaxEntries:N0}).");
            }

            totalBytes = ValidateTarEntrySize(entry.Length, totalBytes, archiveFileLength, limits);
            var fullPath = NormalizeArchivePath(entry.Name);
            bool isDir = entry.EntryType == TarEntryType.Directory;
            var name = GetEntryName(fullPath);
            decimal size = (decimal)entry.Length;
            entries.Add(new ArchiveEntryInfo(
                name,
                fullPath,
                size,
                compressedSizeKnown ? size : null,
                entry.ModificationTime.LocalDateTime,
                isDir));
        }
    }

    public static void ExtractArchive(
        string archivePath,
        string destination,
        ArchiveFormat format,
        bool overwrite,
        IExecutionContext context,
        FileSystemPolicyAuthorizer authorizer,
        string? pattern = null,
        bool destinationIsFileForGzip = false,
        ArchiveExtractionLimits? limits = null)
    {
        limits ??= ArchiveExtractionLimits.Default;

        switch (format)
        {
            case ArchiveFormat.Zip:
                ExtractZip(archivePath, destination, overwrite, context, authorizer, pattern, limits);
                break;

            case ArchiveFormat.Tar:
                using (var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    ExtractTarStream(fs, new FileInfo(archivePath).Length, destination, overwrite, context, authorizer, pattern, limits);
                }
                break;

            case ArchiveFormat.TarGz:
                using (var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                {
                    ExtractTarStream(gz, new FileInfo(archivePath).Length, destination, overwrite, context, authorizer, pattern, limits);
                }
                break;

            case ArchiveFormat.Gzip:
                ExtractGzip(archivePath, destination, overwrite, context, authorizer, pattern, destinationIsFileForGzip, limits);
                break;

            default:
                throw new ExecutionException($"Unsupported archive format: {format}");
        }
    }

    public static void ExtractZip(
        string archivePath,
        string destinationDirectory,
        bool overwrite,
        IExecutionContext context,
        FileSystemPolicyAuthorizer authorizer,
        string? pattern = null,
        ArchiveExtractionLimits? limits = null)
    {
        limits ??= ArchiveExtractionLimits.Default;
        var destination = authorizer.Authorize(context, destinationDirectory,
            FileSystemAccessKind.Extract, validateFileType: false).CanonicalPath;
        Directory.CreateDirectory(destination);

        long archiveFileLength = new FileInfo(archivePath).Length;
        long totalExtractedBytes = 0;
        int entryCount = 0;

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            entryCount++;
            if (entryCount > limits.MaxEntries)
            {
                throw new InvalidDataException($"ZIP archive exceeds maximum entry count ({limits.MaxEntries:N0}).");
            }

            context.IncrementOperationCount(OperationType.FileSystem, destination);
            RejectUnsafeEntryName(entry.FullName, "ZIP");
            if (IsZipSymbolicLink(entry))
            {
                throw new InvalidDataException("ZIP archive contains an unsafe entry name or link.");
            }

            var relative = entry.FullName
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(destination, relative));
            if (!SafePath.TryResolveWithinRoot(destination, target, out target))
            {
                throw Denied(context, entry.FullName, "Archive entry escapes the extraction root.");
            }

            var isDirectory = entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                              entry.FullName.EndsWith('\\');
            var authorized = authorizer.Authorize(context, target, FileSystemAccessKind.Extract,
                validateFileType: !isDirectory).CanonicalPath;

            if (!string.IsNullOrEmpty(pattern))
            {
                if (isDirectory || !MatchesPattern(entry.FullName, pattern))
                {
                    continue;
                }
            }

            if (isDirectory)
            {
                Directory.CreateDirectory(authorized);
                continue;
            }

            var parent = Path.GetDirectoryName(authorized);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            // Re-check immediately before mutation to narrow the check/use window.
            authorized = authorizer.Authorize(context, authorized, FileSystemAccessKind.Extract).CanonicalPath;
            context.SecurityService.ValidateWriteAccess(authorized);

            using var entryStream = entry.Open();
            totalExtractedBytes = WriteBoundedEntryFile(
                entryStream,
                authorized,
                overwrite,
                entry.FullName,
                entry.CompressedLength,
                archiveFileLength,
                totalExtractedBytes,
                limits,
                context.CancellationToken);
        }
    }

    private static void ExtractTarStream(
        Stream tarStream,
        long archiveFileLength,
        string destinationDirectory,
        bool overwrite,
        IExecutionContext context,
        FileSystemPolicyAuthorizer authorizer,
        string? pattern,
        ArchiveExtractionLimits limits)
    {
        var destination = authorizer.Authorize(context, destinationDirectory,
            FileSystemAccessKind.Extract, validateFileType: false).CanonicalPath;
        Directory.CreateDirectory(destination);

        long totalExtractedBytes = 0;
        long totalDeclaredBytes = 0;
        int entryCount = 0;

        using var reader = new TarReader(tarStream);
        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) != null)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            // TarReader consumes skipped bodies on the next read, including PATTERN exclusions.
            // Admit their sizes before that read so selective extraction cannot bypass the bounds.
            totalDeclaredBytes = ValidateTarEntrySize(entry.Length, totalDeclaredBytes, archiveFileLength, limits);
            entryCount++;
            if (entryCount > limits.MaxEntries)
            {
                throw new InvalidDataException($"TAR archive exceeds maximum entry count ({limits.MaxEntries:N0}).");
            }

            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.Directory))
            {
                throw new InvalidDataException(
                    $"TAR archive contains an unsupported or unsafe entry type '{entry.EntryType}' for '{entry.Name}'.");
            }

            string normalizedName = NormalizeArchivePath(entry.Name);
            if (normalizedName == "." || string.IsNullOrEmpty(normalizedName))
            {
                continue;
            }

            context.IncrementOperationCount(OperationType.FileSystem, destination);
            RejectUnsafeEntryName(entry.Name, "TAR");

            var relative = entry.Name
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(destination, relative));
            if (!SafePath.TryResolveWithinRoot(destination, target, out target))
            {
                throw Denied(context, entry.Name, "Archive entry escapes the extraction root.");
            }

            bool isDirectory = entry.EntryType == TarEntryType.Directory;
            var authorized = authorizer.Authorize(context, target, FileSystemAccessKind.Extract,
                validateFileType: !isDirectory).CanonicalPath;

            if (!string.IsNullOrEmpty(pattern))
            {
                if (isDirectory || !MatchesPattern(entry.Name, pattern))
                {
                    continue;
                }
            }

            if (isDirectory)
            {
                Directory.CreateDirectory(authorized);
                continue;
            }

            var parent = Path.GetDirectoryName(authorized);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            authorized = authorizer.Authorize(context, authorized, FileSystemAccessKind.Extract).CanonicalPath;
            context.SecurityService.ValidateWriteAccess(authorized);

            if (entry.DataStream == null)
            {
                if (!overwrite && File.Exists(authorized))
                {
                    throw new IOException($"Destination file already exists and OVERWRITE is OFF: {authorized}");
                }
                using var _ = new FileStream(authorized, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None);
                continue;
            }

            totalExtractedBytes = WriteBoundedEntryFile(
                entry.DataStream,
                authorized,
                overwrite,
                entry.Name,
                compressedEntryBytes: -1,
                archiveFileLength,
                totalExtractedBytes,
                limits,
                context.CancellationToken);
        }
    }

    private static void ExtractGzip(
        string archivePath,
        string destination,
        bool overwrite,
        IExecutionContext context,
        FileSystemPolicyAuthorizer authorizer,
        string? pattern,
        bool destinationIsFile,
        ArchiveExtractionLimits limits)
    {
        var archiveFileInfo = new FileInfo(archivePath);
        string strippedName = StripKnownArchiveSuffix(archiveFileInfo.Name) ?? (archiveFileInfo.Name + ".out");

        string targetFile;
        if (destinationIsFile)
        {
            targetFile = destination;
        }
        else
        {
            var destDir = authorizer.Authorize(context, destination,
                FileSystemAccessKind.Extract, validateFileType: false).CanonicalPath;
            Directory.CreateDirectory(destDir);
            targetFile = Path.Combine(destDir, strippedName);
        }

        if (!string.IsNullOrEmpty(pattern) && !MatchesPattern(Path.GetFileName(targetFile), pattern))
        {
            return;
        }

        context.IncrementOperationCount(OperationType.FileSystem, targetFile);
        var authorized = authorizer.Authorize(context, targetFile, FileSystemAccessKind.Extract, validateFileType: true).CanonicalPath;
        context.SecurityService.ValidateWriteAccess(authorized);
        context.SecurityService.ValidateFileType(authorized);

        var parent = Path.GetDirectoryName(authorized);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

        using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        WriteBoundedEntryFile(
            gz,
            authorized,
            overwrite,
            archiveFileInfo.Name,
            compressedEntryBytes: archiveFileInfo.Length,
            archiveFileInfo.Length,
            totalExtractedBytesBefore: 0,
            limits,
            context.CancellationToken);
    }

    private static long ValidateTarEntrySize(long entryBytes, long totalBefore, long archiveFileLength, ArchiveExtractionLimits limits)
    {
        if (entryBytes > limits.MaxEntryBytes)
            throw new InvalidDataException("TAR entry exceeds maximum allowed uncompressed size.");
        if (entryBytes > limits.MaxTotalBytes - totalBefore)
            throw new InvalidDataException("TAR archive exceeds maximum allowed total uncompressed size.");
        long total = totalBefore + entryBytes;
        if (total > limits.RatioCheckMinBytes && archiveFileLength > 0 &&
            total > archiveFileLength * limits.MaxCompressionRatio)
            throw new InvalidDataException("TAR archive exceeds maximum allowed decompression ratio.");
        return total;
    }

    private static long WriteBoundedEntryFile(
        Stream inputStream,
        string outputPath,
        bool overwrite,
        string entryName,
        long compressedEntryBytes,
        long archiveFileLength,
        long totalExtractedBytesBefore,
        ArchiveExtractionLimits limits,
        CancellationToken cancellationToken)
    {
        if (!overwrite && File.Exists(outputPath))
        {
            throw new IOException($"Destination file already exists and OVERWRITE is OFF: {outputPath}");
        }

        long entryBytes = 0;
        long totalBytes = totalExtractedBytesBefore;
        bool deletePartial = false;

        try
        {
            using var outStream = new FileStream(
                outputPath,
                overwrite ? FileMode.Create : FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);

            var buffer = new byte[81920];
            int read;
            while ((read = inputStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                entryBytes += read;
                totalBytes += read;

                if (entryBytes > limits.MaxEntryBytes)
                {
                    deletePartial = true;
                    throw new InvalidDataException(
                        $"Archive entry '{entryName}' exceeds maximum allowed uncompressed size ({limits.MaxEntryBytes:N0} bytes).");
                }
                if (totalBytes > limits.MaxTotalBytes)
                {
                    deletePartial = true;
                    throw new InvalidDataException(
                        $"Archive exceeds maximum allowed total uncompressed size ({limits.MaxTotalBytes:N0} bytes).");
                }
                if (entryBytes > limits.RatioCheckMinBytes &&
                    compressedEntryBytes > 0 &&
                    entryBytes > compressedEntryBytes * limits.MaxCompressionRatio)
                {
                    deletePartial = true;
                    throw new InvalidDataException(
                        $"Archive entry '{entryName}' exceeds maximum allowed decompression ratio ({limits.MaxCompressionRatio}:1).");
                }
                if (totalBytes > limits.RatioCheckMinBytes &&
                    archiveFileLength > 0 &&
                    totalBytes > archiveFileLength * limits.MaxCompressionRatio)
                {
                    deletePartial = true;
                    throw new InvalidDataException(
                        $"Archive exceeds maximum allowed decompression ratio ({limits.MaxCompressionRatio}:1).");
                }

                outStream.Write(buffer, 0, read);
            }
        }
        catch
        {
            if (deletePartial && File.Exists(outputPath))
            {
                try { File.Delete(outputPath); } catch { }
            }
            throw;
        }

        return totalBytes;
    }

    public static bool MatchesPattern(string entryPath, string pattern)
    {
        string normalizedEntry = NormalizeArchivePath(entryPath).TrimEnd('/');
        string normalizedPattern = NormalizeArchivePath(pattern);

        bool hasSeparator = normalizedPattern.Contains('/');
        string candidate = hasSeparator ? normalizedEntry : Path.GetFileName(normalizedEntry);
        var regex = BuildGlobRegex(normalizedPattern, matchFullPath: hasSeparator);
        return regex.IsMatch(candidate);
    }

    private static Regex BuildGlobRegex(string pattern, bool matchFullPath)
    {
        var sb = new StringBuilder("^");
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '*')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                {
                    if (i + 2 < pattern.Length && pattern[i + 2] == '/')
                    {
                        sb.Append("(?:.*/)?");
                        i += 2;
                    }
                    else
                    {
                        sb.Append(".*");
                        i++;
                    }
                }
                else
                {
                    sb.Append(matchFullPath ? "[^/]*" : ".*");
                }
            }
            else if (c == '?')
            {
                sb.Append(matchFullPath ? "[^/]" : ".");
            }
            else
            {
                sb.Append(Regex.Escape(c.ToString()));
            }
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string NormalizeArchivePath(string rawPath)
    {
        string normalized = rawPath.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }
        return normalized;
    }

    private static string GetEntryName(string normalizedFullPath)
    {
        string trimmed = normalizedFullPath.TrimEnd('/');
        int slash = trimmed.LastIndexOf('/');
        return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
    }

    private static void RejectUnsafeEntryName(string entryName, string formatLabel)
    {
        if (string.IsNullOrWhiteSpace(entryName)
            || Path.IsPathRooted(entryName)
            || entryName.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{formatLabel} archive contains an unsafe entry name or link.");
        }
    }

    private static bool IsZipSymbolicLink(ZipArchiveEntry entry) =>
        ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;

    private static FileSystemPolicyDeniedException Denied(
        IExecutionContext context,
        string entry,
        string reason)
    {
        var snapshot = context.ExecutionPolicy ?? ExecutionPolicySnapshot.Capture(
            EnterprisePolicyRuntime.Current, ETL_SQL.Core.Common.ProcessActor.Current,
            ScriptExecutionMode.Batch, "unknown");
        return new FileSystemPolicyDeniedException(OperationPolicyDecision.Deny(snapshot,
            "Filesystem:ArchiveExtraction", $"<archive-entry>/{Path.GetFileName(entry)}",
            "entry must remain inside authorized extraction root", reason));
    }
}
