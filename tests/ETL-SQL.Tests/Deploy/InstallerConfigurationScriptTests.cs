using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json.Nodes;

namespace ETL_SQL.Tests.Deploy;

/// <summary>
/// The MSI's deferred configuration step (configure-portal-jwt.ps1), run as the installer runs it:
/// Windows PowerShell beside an installed appsettings.json. A fresh install must start the Portal
/// and Orchestrator with no operator configuration; an upgrade must keep what the installed file held.
/// </summary>
public sealed class InstallerConfigurationScriptTests : IDisposable
{
    private readonly string _install = Directory.CreateTempSubdirectory("etlsql-msi-config-").FullName;

    public void Dispose() => Directory.Delete(_install, recursive: true);

    [Fact]
    public void FreshInstall_GeneratesEveryStartupSecretAndAOneTimeSignIn()
    {
        if (!OperatingSystem.IsWindows()) return;
        Stage();

        Run(upgradeCode: "");

        var config = ReadConfig();
        Assert.False(string.IsNullOrWhiteSpace((string?)config["Portal"]!["Jwt"]!["Secret"]));
        Assert.False(string.IsNullOrWhiteSpace((string?)config["Portal"]!["Dataset"]!["AtRestKey"]));
        var signing = (string?)config["Orchestrator"]!["IdentitySigningSecret"];
        Assert.False(string.IsNullOrWhiteSpace(signing));
        Assert.Equal(signing, (string?)config["Portal"]!["Orchestrator"]!["IdentitySigningSecret"]);
        Assert.Equal((string?)config["Orchestrator"]!["ApiKey"], (string?)config["Portal"]!["Orchestrator"]!["ApiKey"]);
        Assert.Contains(_install, config["Security"]!["ApprovedSafeZones"]!.AsArray().Select(zone => (string?)zone));

        // SYSTEM and Administrators only; nothing inherited from Program Files' Users read.
        var signIn = Path.Combine(_install, "first-run-password.txt");
        Assert.Equal(new HashSet<string> { "S-1-5-18", "S-1-5-32-544" }, Grantees(signIn));
        // A non-elevated test process is neither; as the file's owner it may grant itself read.
        GrantCurrentUserRead(signIn);
        var lines = File.ReadAllLines(signIn);
        Assert.Equal("admin", lines[0]);
        Assert.Equal((string?)config["Portal"]!["FirstRun"]!["AdminPassword"], lines[1]);
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

    [Fact]
    public void Upgrade_KeepsTheInstalledValuesAndAddsKeysNewToTheRelease()
    {
        if (!OperatingSystem.IsWindows()) return;
        Stage();
        // The configuration an earlier release left installed: its own secrets, an operator setting
        // and safe zone, and no key the new release added.
        var installed = ReadConfig();
        installed["Portal"]!["Jwt"]!["Secret"] = "installed-jwt-secret-0000000000000000000000";
        installed["Portal"]!["Dataset"]!["AtRestKey"] = "installed-at-rest-key-000000000000000000000";
        installed["Orchestrator"]!["ApiKey"] = "installed-api-key";
        installed["Portal"]!["Orchestrator"]!["ApiKey"] = "installed-api-key";
        installed["Security"]!["ApprovedSafeZones"] = new JsonArray(@"D:\operator\data");
        installed["UpgradeSentinel"] = new JsonObject { ["Value"] = "kept" };
        installed["Portal"]!.AsObject().Remove("Studio");
        File.WriteAllText(Path.Combine(_install, "appsettings.pre-upgrade.json"), installed.ToJsonString());

        Run(upgradeCode: "{00000000-0000-0000-0000-000000000001}");

        var config = ReadConfig();
        Assert.Equal("installed-jwt-secret-0000000000000000000000", (string?)config["Portal"]!["Jwt"]!["Secret"]);
        Assert.Equal("installed-at-rest-key-000000000000000000000", (string?)config["Portal"]!["Dataset"]!["AtRestKey"]);
        Assert.Equal("installed-api-key", (string?)config["Orchestrator"]!["ApiKey"]);
        Assert.Equal("kept", (string?)config["UpgradeSentinel"]!["Value"]);
        Assert.NotNull(config["Portal"]!["Studio"]);
        Assert.Equal(new[] { @"D:\operator\data", _install },
            config["Security"]!["ApprovedSafeZones"]!.AsArray().Select(zone => (string?)zone).ToArray());
        Assert.False(File.Exists(Path.Combine(_install, "appsettings.pre-upgrade.json")));
        Assert.False(File.Exists(Path.Combine(_install, "first-run-password.txt")));
        Assert.True(string.IsNullOrWhiteSpace((string?)config["Portal"]!["FirstRun"]!["AdminPassword"]));
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

    private static string FindRepoRoot([CallerFilePath] string sourceFilePath = "")
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath)!);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "ETL-SQL.slnx")))
            current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
