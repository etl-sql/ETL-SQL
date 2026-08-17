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
/// </remarks>
internal static class SandboxFilePermissions
{
    public static void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(
            path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public static void AllowUnprivilegedSandboxWrites(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
    }
}
