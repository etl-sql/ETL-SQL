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
///   <item>an optional startup check that the <b>share</b> (<c>\\server\share</c>) is reachable so a
///   node <b>fails fast</b> on an offline/unauthorized share rather than serving an empty store. The
///   per-area subdirectories are created on demand by the first write (exactly like the local provider),
///   so a missing area folder is not a startup error — only an unreachable share is.</item>
/// </list>
/// </summary>
public sealed class SmbArtifactStorage : FileSystemArtifactStorage
{
    /// <param name="roots">UNC root (<c>\\server\share\…</c>) for each area this provider serves.</param>
    /// <param name="verifyReachable">When true, each distinct share must be reachable at construction.</param>
    public SmbArtifactStorage(IReadOnlyDictionary<ArtifactArea, string> roots, bool verifyReachable = true)
        : base(Validate(roots))
    {
        if (!verifyReachable) return;

        // Probe each distinct SHARE (the \\server\share component), not the area subdirectory: a missing
        // area folder is created on the first write, but a wrong/offline server or share is a fatal
        // misconfiguration we must catch at startup, not on the first request.
        foreach (var share in roots.Values.Select(r => Path.GetPathRoot(r) ?? r)
                                          .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (string.IsNullOrEmpty(share) || !Directory.Exists(share))
                    throw new IOException($"SMB share is not reachable: {share}");
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"SMB artifact storage cannot reach share '{share}'. Verify the share is online and the " +
                    "service account has access (mapped credentials / cmdkey). Area subdirectories under the " +
                    "share are created on demand.", ex);
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
