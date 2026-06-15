using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Storage;

/// <summary>
/// Filesystem-backed <see cref="IArtifactStorage"/>: the shared I/O engine for the local-disk and
/// SMB/UNC providers (P1.5). Each <see cref="ArtifactArea"/> maps to a configured root directory; the
/// same <c>System.IO</c> APIs serve both a local path and a UNC share, so this class holds all the
/// behavior and the concrete providers differ only in how they validate their roots.
///
/// <para>Writes are atomic: content is written to a temporary file in the destination directory (same
/// volume/share) and renamed into place, so a concurrent reader never sees a partial artifact. The
/// <see cref="ArtifactArea.Keys"/> area is written owner-only and refuses local-copy leasing.</para>
/// </summary>
public class FileSystemArtifactStorage : IArtifactStorage
{
    private readonly IReadOnlyDictionary<ArtifactArea, string> _roots;

    /// <param name="roots">Absolute root directory for each area this provider serves.</param>
    public FileSystemArtifactStorage(IReadOnlyDictionary<ArtifactArea, string> roots)
    {
        var resolved = new Dictionary<ArtifactArea, string>();
        foreach (var (area, root) in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new ArgumentException($"Root for area '{area}' must not be empty.", nameof(roots));
            resolved[area] = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        }
        _roots = resolved;
    }

    public Task<bool> ExistsAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        Task.FromResult(File.Exists(Resolve(area, path)));

    public Task<ArtifactInfo?> GetInfoAsync(ArtifactArea area, string path, CancellationToken ct = default)
    {
        var full = Resolve(area, path);
        if (!File.Exists(full))
            return Task.FromResult<ArtifactInfo?>(null);
        var fi = new FileInfo(full);
        return Task.FromResult<ArtifactInfo?>(
            new ArtifactInfo(ArtifactPath.Normalize(path), fi.Length, new DateTimeOffset(fi.LastWriteTimeUtc)));
    }

    public async IAsyncEnumerable<ArtifactInfo> EnumerateAsync(
        ArtifactArea area, string? prefix = null, bool recursive = true,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        var root = Root(area);
        var searchRoot = string.IsNullOrWhiteSpace(prefix)
            ? root
            : Resolve(area, prefix); // a prefix is a sub-directory within the area
        if (!Directory.Exists(searchRoot))
            yield break;

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var file in Directory.EnumerateFiles(searchRoot, "*", option))
        {
            ct.ThrowIfCancellationRequested();
            var fi = new FileInfo(file);
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            yield return new ArtifactInfo(rel, fi.Length, new DateTimeOffset(fi.LastWriteTimeUtc));
        }
    }

    public Task<Stream> OpenReadAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        Task.FromResult<Stream>(new FileStream(
            Resolve(area, path), FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true));

    public Task<byte[]> ReadAllBytesAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        File.ReadAllBytesAsync(Resolve(area, path), ct);

    public Task<string> ReadAllTextAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        File.ReadAllTextAsync(Resolve(area, path), ct);

    public async Task WriteAsync(
        ArtifactArea area, string path, Stream content, bool overwrite = true, CancellationToken ct = default)
    {
        var dest = Resolve(area, path);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var temp = dest + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                await content.CopyToAsync(fs, ct);
            Commit(temp, dest, overwrite, area);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    public async Task WriteAllBytesAsync(
        ArtifactArea area, string path, ReadOnlyMemory<byte> content, bool overwrite = true, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await ms.WriteAsync(content, ct);
        ms.Position = 0;
        await WriteAsync(area, path, ms, overwrite, ct);
    }

    public Task WriteAllTextAsync(
        ArtifactArea area, string path, string content, bool overwrite = true, CancellationToken ct = default) =>
        WriteAllBytesAsync(area, path, System.Text.Encoding.UTF8.GetBytes(content), overwrite, ct);

    public Task<bool> DeleteAsync(ArtifactArea area, string path, CancellationToken ct = default)
    {
        var full = Resolve(area, path);
        if (!File.Exists(full))
            return Task.FromResult(false);
        File.Delete(full);
        return Task.FromResult(true);
    }

    public Task MoveAsync(
        ArtifactArea area, string sourcePath, string destinationPath, bool overwrite = false, CancellationToken ct = default)
    {
        var src = Resolve(area, sourcePath);
        var dst = Resolve(area, destinationPath);
        if (!File.Exists(src))
            throw new FileNotFoundException($"Artifact not found: {ArtifactPath.Normalize(sourcePath)}", src);
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.Move(src, dst, overwrite); // throws IOException if dst exists and overwrite is false
        if (area == ArtifactArea.Keys)
            RestrictToOwner(dst);
        return Task.CompletedTask;
    }

    public Task<IArtifactLease> LeaseLocalCopyAsync(ArtifactArea area, string path, CancellationToken ct = default)
    {
        if (area == ArtifactArea.Keys)
            throw new InvalidOperationException("Key material cannot be leased to a local file.");
        var full = Resolve(area, path);
        if (!File.Exists(full))
            throw new FileNotFoundException($"Artifact not found: {ArtifactPath.Normalize(path)}", full);
        // The artifact already lives on a filesystem the caller can read directly — hand back its real
        // path with a no-op release (no temporary copy to clean up).
        return Task.FromResult<IArtifactLease>(new NoOpLease(full));
    }

    // ── Internals ────────────────────────────────────────────────────────────────

    /// <summary>Resolves an area-relative path to an absolute path, re-checked to be within the root.</summary>
    protected string Resolve(ArtifactArea area, string path)
    {
        var root = Root(area);
        var normalized = ArtifactPath.Normalize(path);
        var full = Path.GetFullPath(Path.Combine(root, normalized));
        // Defense in depth: ArtifactPath already rejected traversal, but re-verify the resolved path so
        // a symlink or odd normalization can never land outside the area root.
        if (!SafePath.IsWithinRoot(root, full))
            throw new ArgumentException($"Artifact path '{path}' escapes the '{area}' root.", nameof(path));
        return full;
    }

    private string Root(ArtifactArea area) =>
        _roots.TryGetValue(area, out var root)
            ? root
            : throw new InvalidOperationException($"No storage root is configured for the '{area}' area.");

    private static void Commit(string temp, string dest, bool overwrite, ArtifactArea area)
    {
        if (!overwrite && File.Exists(dest))
            throw new IOException($"Artifact already exists: {dest}");
        File.Move(temp, dest, overwrite);
        if (area == ArtifactArea.Keys)
            RestrictToOwner(dest);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
    }

    /// <summary>Owner-only (0600) on Unix; no-op on Windows (user-scoped ACLs already apply).</summary>
    private static void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
        catch { /* best-effort hardening; never fail the write over a chmod */ }
    }

    private sealed class NoOpLease(string localPath) : IArtifactLease
    {
        public string LocalPath { get; } = localPath;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
