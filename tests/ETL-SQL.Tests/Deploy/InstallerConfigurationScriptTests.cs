using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ETL_SQL.Core.Common;

namespace ETL_SQL.Tests.Deploy;

/// <summary>
/// The installed-secret contract shared by the DEB package's install-time configuration and the
/// services' first-start bootstrap (the MSI runs no configuration script since MI01; see
/// InstalledConfigurationBootstrapTests): the same settings move to appsettings.secrets.json, and
/// every one of them is a shipped secret setting.
/// </summary>
public sealed class InstallerConfigurationScriptTests
{
    [Fact]
    public void BothInstallersAndRestore_MoveTheSameSecretsAndEveryOneIsAShippedSecretSetting()
    {
        var root = FindRepoRoot();
        var deb = Paths(File.ReadAllText(Path.Combine(root, "scripts", "build-linux-packages.sh")), @"SECRET_PATHS = \[(?<list>[^\]]*)\]");
        Assert.NotEmpty(deb);
        // The MSI bootstrap and restore use this list.
        Assert.Equal(deb, ETL_SQL.Common.ConfigurationExtensions.InstalledSecretPaths);

        var shipped = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "src", "appsettings.json")))!;
        foreach (var dotted in deb)
        {
            var parts = dotted.Split(':');
            var node = shipped;
            foreach (var part in parts[..^1]) node = node?[part];
            // The section ships; a key such as FirstRun:AdminPassword is added by the installer.
            Assert.True(node is JsonObject, $"{dotted} is not in a shipped settings section.");
            Assert.True(SupportBundleRedactor.IsSecretKey(parts[^1]), $"{dotted} is not classified as a secret.");
        }
    }

    [Fact]
    public void DebPackage_RecordsTheJobHistoryFileTheServiceUserWrites()
    {
        // .NET resolves LocalApplicationData to $HOME/.local/share on Linux, and both units set HOME.
        var root = FindRepoRoot();
        var linux = Path.Combine(root, "src", "ETL-SQL.Installer", "linux");
        foreach (var unit in new[] { "etl-sql-orchestrator.service", "etl-sql-portal.service" })
            Assert.Contains("Environment=HOME=/var/lib/etl-sql", File.ReadAllText(Path.Combine(linux, unit)));
        Assert.Contains("\"/var/lib/etl-sql/.local/share/etlsql-data/etlsql.db\"",
            File.ReadAllText(Path.Combine(root, "scripts", "build-linux-packages.sh")));
    }

    [Fact]
    public void DebPackage_KeepsShippedFilesOutOfTheServiceAccountsHands()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "scripts", "build-linux-packages.sh")).Replace("\r\n", "\n");

        // The service account must not own the install tree: it could rewrite its own binaries.
        Assert.DoesNotContain("chown -R etlsql:etlsql /usr/lib/etl-sql", script);
        // The bin folder stays writable for runtime state, with the sticky bit protecting root's files.
        Assert.Contains("chmod 1775 \"$BIN\"", script);
        Assert.Contains("dpkg-deb --root-owner-group --build", script);
    }

    private static string[] Paths(string text, string pattern) =>
        Regex.Matches(Regex.Match(text, pattern).Groups["list"].Value, "[\"']([^\"']+)[\"']")
            .Select(match => match.Groups[1].Value).ToArray();

    private static string FindRepoRoot([CallerFilePath] string sourceFilePath = "")
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath)!);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "ETL-SQL.slnx")))
            current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
