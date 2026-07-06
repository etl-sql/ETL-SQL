using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ETL_SQL.Core.Governance;

/// <summary>
/// Resolves the OS-reported final path of an <em>open</em> file handle, so callers can verify
/// that the file they actually opened is the file that was authorized — closing the window in
/// which a path component is swapped for a symbolic link or junction between check and use.
/// Returns <c>null</c> when the platform offers no handle-based resolution (validation is then
/// best-effort by path).
/// </summary>
public static class FileHandleFinalPath
{
    public static string? Resolve(SafeFileHandle handle)
    {
        if (OperatingSystem.IsWindows()) return ResolveWindows(handle);
        if (OperatingSystem.IsLinux()) return ResolveLinux(handle);
        return null;
    }

    /// <summary>
    /// Compares an OS-reported final path with the authorized canonical path, tolerating the
    /// extended-length prefix and trailing separators the OS may add.
    /// </summary>
    public static bool Matches(string finalPath, string canonicalPath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(Normalize(finalPath), Normalize(canonicalPath), comparison);
    }

    private static string Normalize(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            path = @"\\" + path[8..];
        else if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            path = path[4..];
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle hFile, [Out] char[] lpszFilePath, uint cchFilePath, uint dwFlags);

    private static string? ResolveWindows(SafeFileHandle handle)
    {
        var buffer = new char[1024];
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
        if (length == 0) return null;
        if (length > buffer.Length)
        {
            buffer = new char[length];
            length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
            if (length == 0 || length > buffer.Length) return null;
        }
        return new string(buffer, 0, (int)length);
    }

    [DllImport("libc", EntryPoint = "readlink", SetLastError = true)]
    private static extern nint ReadLink(string pathname, byte[] buffer, nuint bufferSize);

    private static string? ResolveLinux(SafeFileHandle handle)
    {
        var fdPath = $"/proc/self/fd/{handle.DangerousGetHandle().ToInt64()}";
        var buffer = new byte[4096];
        var length = ReadLink(fdPath, buffer, (nuint)buffer.Length);
        if (length <= 0 || length >= buffer.Length) return null;
        return Encoding.UTF8.GetString(buffer, 0, (int)length);
    }
}
