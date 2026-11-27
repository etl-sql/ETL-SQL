using System;
using System.Collections.Generic;
using System.IO;

namespace ETL_SQL.Common;

/// <summary>
/// Reads the values the Windows installer records under <c>HKLM\Software\ETL-SQL\Install</c>: the
/// install folder and the feature choices made at install time. The MSI writes them with native
/// registry rows, so no installer script has to put them into a settings file.
/// </summary>
public interface IInstallerRegistry
{
    /// <summary>The recorded values by name, or null when nothing is recorded (or not Windows).</summary>
    IReadOnlyDictionary<string, string>? ReadInstallValues();
}

public static class InstallerRegistry
{
    public const string KeyPath = @"Software\ETL-SQL\Install";
    public const string InstallFolder = "InstallFolder";
    public const string OrchSurface = "OrchSurface";
    public const string StewardSurface = "StewardSurface";
    public const string ReportPortal = "ReportPortal";
    public const string Reporting = "Reporting";
    public const string Designer = "Designer";
    public const string Scheduling = "Scheduling";
    public const string Operations = "Operations";
    public const string OrchestratorDatabasePath = "OrchestratorDatabasePath";

    /// <summary>
    /// The recorded install folder when it is <paramref name="baseDirectory"/>, else null. A developer
    /// build or a copied folder on a machine that also has the MSI installed must not pick up the
    /// installed host's choices.
    /// </summary>
    public static string? MatchingInstallFolder(IReadOnlyDictionary<string, string>? values, string baseDirectory)
    {
        if (values is null || !values.TryGetValue(InstallFolder, out var folder) || string.IsNullOrWhiteSpace(folder))
            return null;
        var recorded = Normalize(folder);
        return string.Equals(recorded, Normalize(baseDirectory), StringComparison.OrdinalIgnoreCase) ? recorded : null;
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

/// <summary>The real registry, 64-bit view (the MSI's components are Win64).</summary>
public sealed class WindowsInstallerRegistry : IInstallerRegistry
{
    public static readonly WindowsInstallerRegistry Instance = new();

    private WindowsInstallerRegistry() { }

    public IReadOnlyDictionary<string, string>? ReadInstallValues()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var root = Microsoft.Win32.RegistryKey.OpenBaseKey(
                Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
            using var key = root.OpenSubKey(InstallerRegistry.KeyPath);
            if (key is null) return null;
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in key.GetValueNames())
            {
                if (key.GetValue(name) is { } value) values[name] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            }
            return values;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
