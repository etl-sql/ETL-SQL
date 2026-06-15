using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ETL_SQL.Core.Storage;

/// <summary>
/// SMB/UNC shared-storage provider for Practical HA: every node points at the same network share
/// (<c>\\server\share\…</c>) so artifacts written by one node are immediately visible to the others.
/// The I/O is identical to <see cref="LocalArtifactStorage"/> (System.IO serves UNC paths
/// transparently); this provider adds the two share-specific safeguards:
/// <list type="bullet">
///   <item>roots must be UNC paths — a local path here is almost always a misconfiguration that would
///   silently de-share the deployment;</item>
///   <item>an optional startup reachability check so a node <b>fails fast</b> if the share is offline
///   or its credentials are wrong, rather than serving an empty store.</item>
/// </list>
/// </summary>
public sealed class SmbArtifactStorage : FileSystemArtifactStorage
{
    /// <param name="roots">UNC root (<c>\\server\share\…</c>) for each area this provider serves.</param>
    /// <param name="verifyReachable">When true, each distinct share root must be reachable at construction.</param>
    public SmbArtifactStorage(IReadOnlyDictionary<ArtifactArea, string> roots, bool verifyReachable = true)
        : base(Validate(roots))
    {
        if (!verifyReachable) return;

        // Probe each distinct share so an offline/unauthorized share is caught at startup, not on the
        // first request. We check the configured root's existence (creating it would mask a typo).
        foreach (var root in roots.Values.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(root) && !Directory.Exists(Path.GetPathRoot(root)!))
                    throw new IOException($"SMB share is not reachable: {root}");
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"SMB artifact storage cannot reach '{root}'. Verify the share is online and the " +
                    "service account has access (mapped credentials / cmdkey).", ex);
            }
        }
    }

    private static IReadOnlyDictionary<ArtifactArea, string> Validate(IReadOnlyDictionary<ArtifactArea, string> roots)
    {
        foreach (var (area, root) in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !IsUnc(root))
                throw new ArgumentException(
                    $"SMB storage root for area '{area}' must be a UNC path (\\\\server\\share\\…), not '{root}'. " +
                    "Use the Local provider for node-local directories.", nameof(roots));
        }
        return roots;
    }

    private static bool IsUnc(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal);
}
