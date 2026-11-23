using System.Runtime.InteropServices;

namespace ETL_SQL.Orchestrator.Execution;

/// <summary>
/// Host permissions for state a sandbox mounts.
/// </summary>
/// <remarks>
/// A sandbox deliberately runs as an unprivileged uid unrelated to the orchestrator's, so any
/// directory it must write has to admit that uid. The safe shape is to restrict every enclosing
/// directory to the owning account and open only the mounted leaf: a runtime binds the leaf directly
/// rather than walking the path, so opening it grants nothing to other accounts on the host, which
/// cannot traverse the private parents at all. Windows scopes the same state through the per-user
/// profile and inherited ACLs, where Unix modes do not apply.
/// <para>
/// A writable leaf is shared through the orchestrator's own group rather than opened to every
/// account: it is 2770, the sandbox joins that group, and its umask is 0007. The setgid bit makes
/// everything the sandbox creates inherit the group, so the orchestrator can still remove it. With
/// no usable group (Windows, or an orchestrator whose group is root's) the leaf is 0777 and the
/// umask 0000, as before.
/// </para>
/// </remarks>
internal static class SandboxFilePermissions
{
    public static void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(
            path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>
    /// Opens a leaf the sandbox only ever reads — capability material bound read-only. It grants read
    /// and traverse, never write, so the workload can use what it was given and cannot add to it.
    /// </summary>
    public static void AllowUnprivilegedSandboxReads(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    /// <summary>
    /// The group a sandbox may share with this process: its effective group, unless that is gid 0,
    /// which would hand the workload root's group.
    /// </summary>
    public static uint? SharedGroupFor(uint effectiveGroupId) =>
        effectiveGroupId == 0 ? null : effectiveGroupId;

    /// <summary>This process's shareable group, or null on Windows or under root's group.</summary>
    public static uint? CurrentSharedGroupId =>
        OperatingSystem.IsWindows() ? null : SharedGroupFor(getegid());

    /// <summary>The octal umask a sandbox runs with: owner and group only when it shares a group.</summary>
    public static string UmaskFor(uint? sharedGroupId) => sharedGroupId is null ? "0000" : "0007";

    public static void AllowUnprivilegedSandboxWrites(string path) =>
        AllowUnprivilegedSandboxWrites(path, CurrentSharedGroupId);

    public static void AllowUnprivilegedSandboxWrites(string path, uint? sharedGroupId)
    {
        if (OperatingSystem.IsWindows()) return;
        if (sharedGroupId is not null)
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                UnixFileMode.SetGroup);
            return;
        }
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
    }

    [DllImport("libc", EntryPoint = "getegid")]
    private static extern uint getegid();
}
