using Xunit;

namespace ETL_SQL.Tests;

/// <summary>
/// A fact that needs to create a directory symlink. Windows allows that only to administrators or
/// in Developer Mode, so on a machine that cannot, the test shows as skipped instead of returning
/// early and reporting a pass it never earned.
/// </summary>
public sealed class SymlinkFactAttribute : FactAttribute
{
    private static readonly Lazy<string?> Unavailable = new(Probe);

    public SymlinkFactAttribute()
    {
        if (Unavailable.Value is { } reason)
            Skip = reason;
    }

    private static string? Probe()
    {
        var root = Path.Combine(Path.GetTempPath(), "etlsql-symlink-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateSymbolicLink(Path.Combine(root, "link"), root);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"This machine cannot create symlinks: {ex.Message}";
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
