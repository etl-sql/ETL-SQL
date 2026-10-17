using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ETL_SQL.Common;
using ETL_SQL.Core.Common;

namespace ETL_SQL.Tests.Deploy;

/// <summary>
/// The MSI's deferred configuration step (configure-portal-jwt.ps1), run as the installer runs it:
/// Windows PowerShell beside an installed appsettings.json. A fresh install must start the Portal
/// and Orchestrator with no operator configuration; an upgrade must keep what was installed; and the
/// generated secrets must end up in appsettings.secrets.json, which only Administrators and SYSTEM
/// can read, never in the appsettings.json every local user can read.
/// </summary>
public sealed class InstallerConfigurationScriptTests : IDisposable
{
    private readonly string _install = Directory.CreateTempSubdirectory("etlsql-msi-config-").FullName;

    public void Dispose() => Directory.Delete(_install, recursive: true);

    [Fact]
    public void FreshInstall_GeneratesEveryStartupSecretIntoTheProtectedFile()
    {
        if (!OperatingSystem.IsWindows()) return;
        Stage();

        Run(upgradeCode: "");

        var config = ReadConfig();
        var secrets = ReadSecrets();
        Assert.False(string.IsNullOrWhiteSpace((string?)secrets["Portal"]!["Jwt"]!["Secret"]));
        Assert.False(string.IsNullOrWhiteSpace((string?)secrets["Portal"]!["Dataset"]!["AtRestKey"]));
        var signing = (string?)secrets["Orchestrator"]!["IdentitySigningSecret"];
        Assert.False(string.IsNullOrWhiteSpace(signing));
        Assert.Equal(signing, (string?)secrets["Portal"]!["Orchestrator"]!["IdentitySigningSecret"]);
        Assert.Equal((string?)secrets["Orchestrator"]!["ApiKey"], (string?)secrets["Portal"]!["Orchestrator"]!["ApiKey"]);
        AssertNoSecretsIn(config);
        Assert.Contains(_install, config["Security"]!["ApprovedSafeZones"]!.AsArray().Select(zone => (string?)zone));

        var signIn = Path.Combine(_install, "first-run-password.txt");
        // SYSTEM and Administrators only; nothing inherited from Program Files' Users read.
        Assert.Equal(new HashSet<string> { "S-1-5-18", "S-1-5-32-544" }, Grantees(signIn));
        GrantCurrentUserRead(signIn);
        var lines = File.ReadAllLines(signIn);
        Assert.Equal("admin", lines[0]);
        Assert.Equal((string?)secrets["Portal"]!["FirstRun"]!["AdminPassword"], lines[1]);
    }

    [Fact]
    public void UpgradeFromAnInstallWithSecretsInAppSettings_MovesThemAndKeepsEveryValue()
    {
        if (!OperatingSystem.IsWindows()) return;
        Stage();
        // The configuration an earlier release left installed: its own secrets in appsettings.json,
        // an operator setting and safe zone, and no key the new release added.
        var installed = ReadConfig();
        installed["Portal"]!["Jwt"]!["Secret"] = "installed-jwt-secret-0000000000000000000000";
        installed["Portal"]!["Jwt"]!["PreviousSecrets"] = new JsonArray("previous-jwt-secret-00000000000000000000");
        installed["Portal"]!["Dataset"]!["AtRestKey"] = "installed-at-rest-key-000000000000000000000";
        installed["Orchestrator"]!["ApiKey"] = "installed-api-key";
        installed["Portal"]!["Orchestrator"]!["ApiKey"] = "installed-api-key";
        installed["Security"]!["ApprovedSafeZones"] = new JsonArray(@"D:\operator\data");
        installed["UpgradeSentinel"] = new JsonObject { ["Value"] = "kept" };
        installed["Portal"]!.AsObject().Remove("Studio");
        File.WriteAllText(Path.Combine(_install, "appsettings.pre-upgrade.json"), installed.ToJsonString());

        Run(upgradeCode: "{00000000-0000-0000-0000-000000000001}");

        var config = ReadConfig();
        var secrets = ReadSecrets();
        Assert.Equal("installed-jwt-secret-0000000000000000000000", (string?)secrets["Portal"]!["Jwt"]!["Secret"]);
        Assert.Equal("previous-jwt-secret-00000000000000000000", (string?)secrets["Portal"]!["Jwt"]!["PreviousSecrets"]![0]);
        Assert.Equal("installed-at-rest-key-000000000000000000000", (string?)secrets["Portal"]!["Dataset"]!["AtRestKey"]);
        Assert.Equal("installed-api-key", (string?)secrets["Orchestrator"]!["ApiKey"]);
        AssertNoSecretsIn(config);
        Assert.Empty(config["Portal"]!["Jwt"]!["PreviousSecrets"]!.AsArray());
        Assert.Equal("kept", (string?)config["UpgradeSentinel"]!["Value"]);
        Assert.NotNull(config["Portal"]!["Studio"]);
        Assert.Equal(new[] { @"D:\operator\data", _install },
            config["Security"]!["ApprovedSafeZones"]!.AsArray().Select(zone => (string?)zone).ToArray());
        Assert.False(File.Exists(Path.Combine(_install, "appsettings.pre-upgrade.json")));
        Assert.False(File.Exists(Path.Combine(_install, "first-run-password.txt")));
    }

    [Fact]
    public void LaterUpgrade_KeepsTheProtectedSecretsAndRegeneratesNothing()
    {
        if (!OperatingSystem.IsWindows()) return;
        Stage();
        Run(upgradeCode: "");
        var first = ReadSecrets().ToJsonString();
        // The next major upgrade lays down defaults again; the installed (secret-free) file is saved.
        File.Copy(Path.Combine(_install, "appsettings.json"), Path.Combine(_install, "appsettings.pre-upgrade.json"));
        File.Copy(Path.Combine(FindRepoRoot(), "src", "appsettings.json"), Path.Combine(_install, "appsettings.json"), overwrite: true);

        Run(upgradeCode: "{00000000-0000-0000-0000-000000000002}");

        Assert.Equal(first, ReadSecrets().ToJsonString());
        AssertNoSecretsIn(ReadConfig());
    }

    [Fact]
    public void BothInstallers_MoveTheSameSecretsAndEveryOneIsAShippedSecretSetting()
    {
        var root = FindRepoRoot();
        var deb = Paths(File.ReadAllText(Path.Combine(root, "scripts", "build-linux-packages.sh")), @"SECRET_PATHS = \[(?<list>[^\]]*)\]");
        var msi = Paths(File.ReadAllText(Path.Combine(root, "src", "ETL-SQL.Installer", "configure-portal-jwt.ps1")), @"\$secretPaths = @\((?<list>[^)]*)\)");
        Assert.NotEmpty(deb);
        Assert.Equal(deb, msi);

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

    private static string[] Paths(string text, string pattern) =>
        Regex.Matches(Regex.Match(text, pattern).Groups["list"].Value, "[\"']([^\"']+)[\"']")
            .Select(match => match.Groups[1].Value).ToArray();

    private void AssertNoSecretsIn(JsonNode config)
    {
        foreach (var dotted in Paths(File.ReadAllText(Path.Combine(FindRepoRoot(), "scripts", "build-linux-packages.sh")), @"SECRET_PATHS = \[(?<list>[^\]]*)\]"))
        {
            var node = config;
            foreach (var part in dotted.Split(':')) node = node?[part];
            var empty = node is null
                || node is JsonArray { Count: 0 }
                || node is JsonObject { Count: 0 }
                || (node is JsonValue value && string.IsNullOrEmpty(value.ToString()));
            Assert.True(empty, $"appsettings.json still holds {dotted}.");
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static HashSet<string> Grantees(string path) =>
        new FileInfo(path).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().Select(rule => ((SecurityIdentifier)rule.IdentityReference).Value).ToHashSet();

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void GrantCurrentUserRead(string path)
    {
        var file = new FileInfo(path);
        var security = file.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, AccessControlType.Allow));
        file.SetAccessControl(security);
    }

    private void Stage()
    {
        var root = FindRepoRoot();
        File.Copy(Path.Combine(root, "src", "appsettings.json"), Path.Combine(_install, "appsettings.json"));
        File.Copy(Path.Combine(root, "src", "ETL-SQL.Installer", "configure-portal-jwt.ps1"),
            Path.Combine(_install, "configure-portal-jwt.ps1"));
    }

    private void Run(string upgradeCode)
    {
        // The MSI passes [INSTALLFOLDER] (with its trailing separator) first, then key=value pairs.
        var customData = $@"{_install}\|UPGRADE={upgradeCode}|INSTALL_PORTAL=1|REPORT_PORTAL=1";
        var start = new ProcessStartInfo("powershell.exe")
        {
            ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                "-File", Path.Combine(_install, "configure-portal-jwt.ps1"), "-CustomData", customData },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
    }

    private JsonNode ReadConfig() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(_install, "appsettings.json")))!;

    private JsonNode ReadSecrets()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var path = Path.Combine(_install, ConfigurationExtensions.InstalledSecretsFileName);
        // Only Administrators and SYSTEM may read it; the test, as its owner, grants itself read.
        Assert.Equal(new HashSet<string> { "S-1-5-18", "S-1-5-32-544" }, Grantees(path));
        GrantCurrentUserRead(path);
        return JsonNode.Parse(File.ReadAllText(path))!;
    }

    private static string FindRepoRoot([CallerFilePath] string sourceFilePath = "")
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath)!);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "ETL-SQL.slnx")))
            current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
