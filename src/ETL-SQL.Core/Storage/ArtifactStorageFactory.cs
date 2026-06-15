using System;
using System.Collections.Generic;

namespace ETL_SQL.Core.Storage;

/// <summary>
/// Builds the configured <see cref="IArtifactStorage"/> provider from a provider name and the per-area
/// roots. Mirrors the database provider-selection seam (P1.1): "local" (default) and "smb"/"unc".
/// </summary>
public static class ArtifactStorageFactory
{
    /// <param name="provider">"local" (default/null) or "smb"/"unc". Case-insensitive.</param>
    /// <param name="roots">Per-area root directory (local paths for Local, UNC paths for SMB).</param>
    /// <param name="verifyReachable">For SMB, fail fast at construction if a share is unreachable.</param>
    public static IArtifactStorage Create(
        string? provider,
        IReadOnlyDictionary<ArtifactArea, string> roots,
        bool verifyReachable = true)
    {
        ArgumentNullException.ThrowIfNull(roots);
        return (provider ?? "local").Trim().ToLowerInvariant() switch
        {
            "" or "local" => new LocalArtifactStorage(roots),
            "smb" or "unc" => new SmbArtifactStorage(roots, verifyReachable),
            var other => throw new ArgumentException(
                $"Unknown artifact storage provider '{other}'. Supported: 'local', 'smb'.", nameof(provider)),
        };
    }
}
