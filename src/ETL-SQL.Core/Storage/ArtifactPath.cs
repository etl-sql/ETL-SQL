using System;
using System.IO;
using System.Linq;

namespace ETL_SQL.Core.Storage;

/// <summary>
/// Normalizes provider-agnostic artifact paths and rejects anything that would escape an area root.
/// This is the first line of the path-traversal guardrail; the filesystem providers additionally
/// re-check the resolved absolute path against the root (P1.6, via <see cref="SafePath"/>).
/// </summary>
public static class ArtifactPath
{
    /// <summary>
    /// Returns the canonical relative path (forward slashes, no <c>.</c>/<c>..</c> segments), or throws
    /// <see cref="ArgumentException"/> if the input is absolute, empty, or escapes the area root.
    /// </summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Artifact path must not be empty.", nameof(path));

        if (Path.IsPathRooted(path) || path.Contains(':'))
            throw new ArgumentException($"Artifact path must be relative, not '{path}'.", nameof(path));

        var segments = path
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        var resolved = segments.Where(s => s != ".").ToList();
        if (resolved.Count == 0 || resolved.Any(s => s == ".."))
            throw new ArgumentException($"Artifact path '{path}' is invalid or escapes its area root.", nameof(path));

        return string.Join('/', resolved);
    }
}
