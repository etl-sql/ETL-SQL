using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace ETL_SQL.Core.Spill;

/// <summary>
/// Records which process owns a disposable spill root, so a sweeper can remove the root of a process
/// that crashed instead of waiting for it to age out. The marker sits beside the root
/// (<c>&lt;root&gt;.owner</c>) so the root itself holds only spill extents.
/// </summary>
public static class SpillRootOwnership
{
    public const string MarkerSuffix = ".owner";

    public static string MarkerPath(string root) =>
        root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + MarkerSuffix;

    /// <summary>Marks <paramref name="root"/> as owned by the current process.</summary>
    public static void Claim(string root)
    {
        using var self = Process.GetCurrentProcess();
        File.WriteAllText(MarkerPath(root), string.Join('\n',
            Environment.MachineName,
            self.Id.ToString(CultureInfo.InvariantCulture),
            self.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>Removes the owner marker once the root itself is gone.</summary>
    public static void Release(string root)
    {
        try { File.Delete(MarkerPath(root)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// True only when the marker names a process on this host that is no longer running, or whose
    /// id now belongs to a process that started at a different time (a restarted container reuses
    /// its process id). A missing, unreadable or foreign marker is never abandoned.
    /// </summary>
    public static bool IsAbandoned(string root)
    {
        string[] fields;
        try { fields = File.ReadAllText(MarkerPath(root)).Split('\n'); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        if (fields.Length != 3
            || !string.Equals(fields[0], Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
            || !long.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var startedTicks))
            return false;

        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited) return true;
            return Math.Abs(process.StartTime.ToUniversalTime().Ticks - startedTicks) > TimeSpan.TicksPerSecond;
        }
        catch (ArgumentException) { return true; }            // no process holds that id
        catch (InvalidOperationException) { return true; }    // it exited while being inspected
        catch (Win32Exception) { return false; }              // not inspectable; keep the root
        catch (NotSupportedException) { return false; }
    }
}
