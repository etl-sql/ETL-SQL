using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json.Nodes;
using ETL_SQL.App;
using ETL_SQL.Common;
using Microsoft.Extensions.Configuration;
using ConfigurationExtensions = ETL_SQL.Common.ConfigurationExtensions;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// appsettings.secrets.json holds the installed service secrets so that appsettings.json, which every
/// local user can read, holds none. The hosts must read it in the right place in the precedence
/// order, and a user who cannot read it must still be able to run the CLI.
/// </summary>
public sealed class InstalledSecretsConfigurationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("etlsql-secrets-").FullName;

    public void Dispose()
    {
        if (OperatingSystem.IsWindows()) RestoreAccess(Path.Combine(_dir, ConfigurationExtensions.InstalledSecretsFileName));
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Secrets_OverrideAppSettings_AndLaterSourcesOverrideSecrets()
    {
        Write("appsettings.json", """{ "Portal": { "Jwt": { "Secret": "", "ExpiryMinutes": 60 } }, "Orchestrator": { "ApiKey": "" } }""");
        Write(ConfigurationExtensions.InstalledSecretsFileName, """{ "Portal": { "Jwt": { "Secret": "from-secrets" } }, "Orchestrator": { "ApiKey": "from-secrets" } }""");

        // Added after an environment-like source, as the Orchestrator does: it still lands below it.
        var config = new ConfigurationBuilder()
            .SetBasePath(_dir)
            .AddJsonFile("appsettings.json")
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Orchestrator:ApiKey"] = "from-environment" })
            .AddInstalledSecrets(_dir)
            .Build();

        Assert.Equal("from-secrets", config["Portal:Jwt:Secret"]);
        Assert.Equal("60", config["Portal:Jwt:ExpiryMinutes"]);
        Assert.Equal("from-environment", config["Orchestrator:ApiKey"]);
    }

    [Fact]
    public void MissingFile_AddsNoSource()
    {
        Write("appsettings.json", "{}");
        var builder = new ConfigurationBuilder().SetBasePath(_dir).AddJsonFile("appsettings.json");

        builder.AddInstalledSecrets(_dir);

        Assert.Single(builder.Sources);
    }

    [Fact]
    public void UnreadableFile_IsSkipped_SoAnOrdinaryUserCanStillRunTheCli()
    {
        if (!OperatingSystem.IsWindows()) return; // On Linux the test user may be root, who reads anything.
        Write("appsettings.json", """{ "Portal": { "Jwt": { "Secret": "" } } }""");
        var secrets = Write(ConfigurationExtensions.InstalledSecretsFileName, """{ "Portal": { "Jwt": { "Secret": "hidden" } } }""");
        DenyCurrentUserRead(secrets);

        var builder = new ConfigurationBuilder().SetBasePath(_dir).AddJsonFile("appsettings.json");
        var config = builder.AddInstalledSecrets(_dir).Build();

        Assert.Single(builder.Sources);
        Assert.Equal("", config["Portal:Jwt:Secret"]);
    }

    [Fact]
    public async Task Backup_CollectsEveryInstalledSecretByPath()
    {
        var secrets = Write(ConfigurationExtensions.InstalledSecretsFileName, """
            { "Portal": { "Jwt": { "Secret": "jwt", "PreviousSecrets": ["old"] }, "Dataset": { "AtRestKey": "", "PreviousAtRestKeys": { "v0": "older" } } },
              "Orchestrator": { "ApiKey": "api", "PreviousApiKeys": [] } }
            """);
        var sink = new Dictionary<string, JsonNode?>();

        await BackupRestoreService.AddInstalledSecretsAsync(secrets, sink);

        Assert.Equal(new[] { "Orchestrator.ApiKey", "Portal.Dataset.PreviousAtRestKeys.v0", "Portal.Jwt.PreviousSecrets", "Portal.Jwt.Secret" },
            sink.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("old", (string?)sink["Portal.Jwt.PreviousSecrets"]![0]);
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void DenyCurrentUserRead(string path)
    {
        var file = new FileInfo(path);
        var security = file.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ReadData, AccessControlType.Deny));
        file.SetAccessControl(security);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void RestoreAccess(string path)
    {
        if (!File.Exists(path)) return;
        var file = new FileInfo(path);
        var security = file.GetAccessControl();
        security.RemoveAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ReadData, AccessControlType.Deny));
        file.SetAccessControl(security);
    }
}
