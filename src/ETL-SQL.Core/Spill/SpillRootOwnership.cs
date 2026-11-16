using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace ETL_SQL.Core.Spill;

/// <summary>
/// Records which process owns a disposable spill root, so a sweeper can remove the root of a process
/// that crashed instead of waiting for it to age out. The marker sits beside the root
/// (<c>&lt;root&gt;.owner</c>) so the root itself holds only spill extents.
/// </summary>
/// <remarks>
/// The owner keeps the marker open for the life of the root, and the operating system releases that
/// handle only when the owner closes it or exits. A marker that can be opened exclusively therefore
/// has no live owner, and the process it names must also be gone. A marker someone else wrote cannot
/// make a live owner's root look abandoned: the owner's handle still refuses the exclusive open.
/// </remarks>
public static class SpillRootOwnership
{
    public const string MarkerSuffix = ".owner";

    public static string MarkerPath(string root) =>
        root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + MarkerSuffix;

    /// <summary>
    /// Marks <paramref name="root"/> as owned by the current process. Keep the returned handle for
    /// the life of the root and dispose it before <see cref="Release"/>.
    /// </summary>
    public static IDisposable Claim(string root)
    {
        using var self = Process.GetCurrentProcess();
        var content = string.Join('\n',
            Environment.MachineName,
            self.Id.ToString(CultureInfo.InvariantCulture),
            self.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture));
        // Others may read the marker; nobody may open it exclusively while this handle is open.
        var handle = new FileStream(MarkerPath(root), FileMode.Create, FileAccess.Write, FileShare.Read);
        try
        {
            handle.Write(Encoding.UTF8.GetBytes(content));
            handle.Flush();
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>Removes the owner marker once the root itself is gone.</summary>
    public static void Release(string root)
    {
        try { File.Delete(MarkerPath(root)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// True only when no process holds the marker open and the marker names a process on this host
    /// that is no longer running, or whose id now belongs to a process that started at a different
    /// time (a restarted container reuses its process id). A missing, held, unreadable or foreign
    /// marker is never abandoned.
    /// </summary>
    public static bool IsAbandoned(string root)
    {
        string[] fields;
        try
        {
            using var marker = new FileStream(MarkerPath(root), FileMode.Open, FileAccess.Read, FileShare.None);
            using var reader = new StreamReader(marker, Encoding.UTF8);
            fields = reader.ReadToEnd().Split('\n');
        }
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

    /// <summary>
    /// Whether owner markers under <paramref name="spillParent"/> can be trusted. On Unix, a parent
    /// that other users can write to lets them plant or replace markers, so only the age-out applies.
    /// </summary>
    public static bool ParentIsPrivate(string spillParent)
    {
        if (OperatingSystem.IsWindows()) return true;
        var mode = File.GetUnixFileMode(spillParent);
        return (mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) == 0;
    }
}
