using Xunit;

namespace ETL_SQL.Tests;

/// <summary>
/// A fact that only means something on Unix (file modes, XDG paths, the non-DPAPI crypto format).
/// It shows as skipped on Windows instead of passing without asserting anything. Engine Coverage
/// runs on Windows, so pair it with <c>[Trait("Category", "Integration")]</c>: the Linux
/// integration lane is the CI run that executes it.
/// </summary>
public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (OperatingSystem.IsWindows())
            Skip = "Unix-only behavior; Windows covers this another way (ACL inheritance, DPAPI, known folders).";
    }
}
