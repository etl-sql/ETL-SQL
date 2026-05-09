using System;
using System.IO;

namespace ETL_SQL.Core;

public static class SafePath
{
    public static string GetFullPath(string path, string root)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(path, GetRootFullPath(root));
    }

    public static bool IsWithinRoot(string root, string candidate)
    {
        var rootFull = GetRootFullPath(root);
        var candidateFull = Path.GetFullPath(candidate);
        var relative = Path.GetRelativePath(rootFull, candidateFull);

        return !Path.IsPathRooted(relative)
            && !relative.Equals("..", StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    public static bool TryResolveWithinRoot(string root, string path, out string resolved)
    {
        resolved = GetFullPath(path, root);
        return IsWithinRoot(root, resolved);
    }

    private static string GetRootFullPath(string root)
    {
        return Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
