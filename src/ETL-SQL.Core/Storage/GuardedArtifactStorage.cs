using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Services;

namespace ETL_SQL.Core.Storage;

/// <summary>
/// Wraps any <see cref="IArtifactStorage"/> and enforces the deployment's security guardrails at the
/// single storage boundary (Practical HA P1.6), so every provider inherits them instead of each call
/// site re-checking. It reuses <see cref="SecurityService"/>'s extension lists as the single source of
/// truth and adds the area-aware policy the artifact areas need:
///
/// <list type="bullet">
///   <item><b>Path traversal</b> — every path is normalized (rejecting <c>..</c> and absolute paths)
///   before it reaches the provider.</item>
///   <item><b>No executables anywhere</b> — a dangerous executable / code-signing file
///   (<c>.exe/.dll/.bat/.pfx/…</c>) can never be written into any area, so a compromised script cannot
///   stage a payload in shared storage.</item>
///   <item><b>Script immutability</b> — application-logic files (<c>.etlsql/.rptsql/.sql/.py/…</c>) may
///   only be written to the <see cref="ArtifactArea.Scripts"/> area (their legitimate home); writing
///   them into snapshots, datasets, maps, or keys is rejected.</item>
/// </list>
///
/// Reads, enumeration, and deletion pass through (with path normalization); only create/replace
/// operations — writes and a move's destination — are policy-checked.
/// </summary>
public sealed class GuardedArtifactStorage(IArtifactStorage inner, SecurityService security) : IArtifactStorage
{
    public Task<bool> ExistsAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        inner.ExistsAsync(area, Norm(path), ct);

    public Task<ArtifactInfo?> GetInfoAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        inner.GetInfoAsync(area, Norm(path), ct);

    public IAsyncEnumerable<ArtifactInfo> EnumerateAsync(
        ArtifactArea area, string? prefix = null, bool recursive = true, CancellationToken ct = default) =>
        inner.EnumerateAsync(area, prefix is null ? null : Norm(prefix), recursive, ct);

    public Task<Stream> OpenReadAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        inner.OpenReadAsync(area, Norm(path), ct);

    public Task<byte[]> ReadAllBytesAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        inner.ReadAllBytesAsync(area, Norm(path), ct);

    public Task<string> ReadAllTextAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        inner.ReadAllTextAsync(area, Norm(path), ct);

    public Task WriteAsync(ArtifactArea area, string path, Stream content, bool overwrite = true, CancellationToken ct = default)
    {
        GuardWrite(area, path);
        return inner.WriteAsync(area, Norm(path), content, overwrite, ct);
    }

    public Task WriteAllBytesAsync(ArtifactArea area, string path, ReadOnlyMemory<byte> content, bool overwrite = true, CancellationToken ct = default)
    {
        GuardWrite(area, path);
        return inner.WriteAllBytesAsync(area, Norm(path), content, overwrite, ct);
    }

    public Task WriteAllTextAsync(ArtifactArea area, string path, string content, bool overwrite = true, CancellationToken ct = default)
    {
        GuardWrite(area, path);
        return inner.WriteAllTextAsync(area, Norm(path), content, overwrite, ct);
    }

    public Task<bool> DeleteAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        inner.DeleteAsync(area, Norm(path), ct);

    public Task MoveAsync(ArtifactArea area, string sourcePath, string destinationPath, bool overwrite = false, CancellationToken ct = default)
    {
        // The destination is a create/replace — guard it; the source is validated for traversal only.
        GuardWrite(area, destinationPath);
        return inner.MoveAsync(area, Norm(sourcePath), Norm(destinationPath), overwrite, ct);
    }

    public Task<IArtifactLease> LeaseLocalCopyAsync(ArtifactArea area, string path, CancellationToken ct = default) =>
        inner.LeaseLocalCopyAsync(area, Norm(path), ct);

    private static string Norm(string path) => ArtifactPath.Normalize(path);

    private void GuardWrite(ArtifactArea area, string path)
    {
        var normalized = Norm(path);

        if (security.IsDangerousExecutable(normalized))
            throw new SecurityException(
                $"Storage guardrail: writing an executable/code-signing file ('{normalized}') to {area} storage is prohibited.");

        if (area != ArtifactArea.Scripts && security.IsApplicationLogicFile(normalized))
            throw new SecurityException(
                $"Script immutability guardrail: application-logic files may only be written to the Scripts area, not {area} ('{normalized}').");
    }
}
