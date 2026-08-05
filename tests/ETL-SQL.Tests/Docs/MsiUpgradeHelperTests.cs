using System.Diagnostics;

namespace ETL_SQL.Tests.Docs;

/// <summary>
/// The non-elevated half of the MSI upgrade gate, tested without installing anything.
///
/// <para>The gate's first real execution failed on a pure-logic bug: an unsuppressed COM call made
/// <c>Get-MsiProperty</c> return <c>Object[]</c> instead of a string, and PowerShell's <c>-ne</c>
/// against an array is a filter rather than a comparison — so the UpgradeCode check reported
/// "UpgradeCode changed" while printing the same GUID twice, and the gate could never have passed.
/// Finding that cost a 26-minute job that had to download a previous release and build an
/// installer first.</para>
///
/// <para>Nothing about that bug needed an MSI, elevation, or an install. These tests exercise the
/// guard that now catches its whole class, in milliseconds, on any machine — which matters
/// particularly here, because the elevated half has no local path at all on Windows Home.</para>
/// </summary>
public sealed class MsiUpgradeHelperTests
{
    /// <summary>A single value is the contract, and it comes back trimmed.</summary>
    [Fact]
    public void ConvertToSingleMsiValue_ReturnsTheValue_WhenExactlyOne()
    {
        var result = RunHelper("ConvertTo-SingleMsiValue -Value '  {GUID}  ' -Description 'probe'");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal("{GUID}", result.Output.Trim());
    }

    /// <summary>
    /// The exact shape the leak produced: <c>('', '{GUID}', '')</c>. It must throw rather than
    /// return something a comparison will silently mis-handle.
    /// </summary>
    [Fact]
    public void ConvertToSingleMsiValue_Throws_OnTheMultiValueLeakThatBrokeTheGate()
    {
        var result = RunHelper(
            "ConvertTo-SingleMsiValue -Value @('', '{GUID}', '') -Description 'UpgradeCode'");

        Assert.False(result.ExitCode == 0,
            "A three-element result was accepted. That is precisely the value that turned the "
            + "UpgradeCode comparison into an array filter and made the gate unpassable.");
        Assert.Contains("resolved to 3 values", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConvertToSingleMsiValue_Throws_OnEmpty()
    {
        var result = RunHelper("ConvertTo-SingleMsiValue -Value @() -Description 'UpgradeCode'");

        Assert.False(result.ExitCode == 0, result.Output);
        Assert.Contains("resolved to 0 values", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The helpers file must stay side-effect free on load, or dot-sourcing it — which both the
    /// certification script and these tests do — would start running the certification.
    /// </summary>
    [Fact]
    public void HelpersFile_DefinesFunctionsWithoutDoingAnything()
    {
        var result = RunHelper("Write-Output 'loaded'");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal("loaded", result.Output.Trim());
    }

    /// <summary>Dot-sources the helpers, then runs one expression against them.</summary>
    private static (int ExitCode, string Output) RunHelper(string expression)
    {
        var helpers = Path.Combine(RepoRoot(), "scripts", "MsiUpgrade.Helpers.ps1");
        Assert.True(File.Exists(helpers), $"Helpers not found: {helpers}");

        var script = $". '{helpers}'; {expression}";
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "pwsh",
            ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", script },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });

        Assert.NotNull(process);
        var output = process!.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(60_000);
        return (process.ExitCode, output);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ETL-SQL.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
