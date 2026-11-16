using Xunit;

namespace ETL_SQL.Tests;

/// <summary>
/// A fact that only means something on Windows (NTFS path forms, ACLs, the MSI's Windows PowerShell
/// step). It shows as skipped elsewhere instead of passing without asserting anything. Engine
/// Coverage runs on Windows and executes it. A lane that fails on a skipped result must not select
/// it off Windows: add <c>[Trait("Platform", "Windows")]</c> when such a lane's filter matches.
/// </summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    internal const string Reason = "Windows-only behavior (NTFS path forms, ACLs, Windows PowerShell).";

    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = Reason;
    }
}

/// <summary>The theory form of <see cref="WindowsFactAttribute"/>.</summary>
public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = WindowsFactAttribute.Reason;
    }
}
