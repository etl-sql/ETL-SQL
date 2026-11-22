using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json.Nodes;
using ETL_SQL.Common;
using ETL_SQL.Tests.Core;
using ConfigurationExtensions = ETL_SQL.Common.ConfigurationExtensions;

namespace ETL_SQL.Tests.Deploy;

/// <summary>
/// The Orchestrator and Portal prepare an MSI install's configuration on first start (MI01): they
/// generate the missing secrets into the file only SYSTEM and Administrators can read, move any
/// secret an operator wrote into the shared appsettings.json, and publish the one-time sign-in.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class InstalledConfigurationBootstrapTests : IDisposable
{
    private const string Planted = "PLANTED-SECRET-VALUE-7f3a";
    private readonly string _install = Directory.CreateTempSubdirectory("etlsql-bootstrap-").FullName;
    private readonly string _mutex = @"Local\ETL-SQL-Bootstrap-Test-" + Guid.NewGuid().ToString("N");

    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(_install)) GrantCurrentUser(file);
        Directory.Delete(_install, recursive: true);
    }

    [WindowsFact]
    public void NotInstalledHere_TouchesNothing()
    {
        WriteOperator("{}");

        Assert.Equal(InstalledBootstrapResult.Skipped, Run(registered: null));
        Assert.Equal(InstalledBootstrapResult.Skipped, Run(registered: Path.Combine(_install, "other")));

        Assert.Equal(["appsettings.json"], Directory.GetFiles(_install).Select(Path.GetFileName));
    }

    [WindowsFact]
    public void FreshInstall_GeneratesEverySecretIntoTheProtectedFile()
    {
        WriteOperator("{}");

        var result = Run();

        Assert.True(result.HasFlag(InstalledBootstrapResult.Ran));
        Assert.True(result.HasFlag(InstalledBootstrapResult.SecretsGenerated));
        var secretsPath = Path.Combine(_install, ConfigurationExtensions.InstalledSecretsFileName);
        Assert.Equal(new HashSet<string> { "S-1-5-18", "S-1-5-32-544" }, Grantees(secretsPath));
        Assert.True(new FileInfo(secretsPath).GetAccessControl().AreAccessRulesProtected);
        var secrets = ReadSecrets();
        foreach (var path in new[] { "Portal:Jwt:Secret", "Portal:Dataset:AtRestKey", "Orchestrator:ApiKey", "Orchestrator:IdentitySigningSecret" })
            Assert.Equal(32, Convert.FromBase64String(Get(secrets, path)!).Length);
        Assert.Equal(Get(secrets, "Orchestrator:ApiKey"), Get(secrets, "Portal:Orchestrator:ApiKey"));
        Assert.Equal(Get(secrets, "Orchestrator:IdentitySigningSecret"), Get(secrets, "Portal:Orchestrator:IdentitySigningSecret"));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(_install, "appsettings.json")));
    }

    [WindowsFact]
    public void FreshInstall_WritesOneTimeSignIn()
    {
        WriteOperator("{}");

        Assert.True(Run().HasFlag(InstalledBootstrapResult.FirstRunWritten));

        var signIn = Path.Combine(_install, "first-run-password.txt");
        Assert.Equal(new HashSet<string> { "S-1-5-18", "S-1-5-32-544" }, Grantees(signIn));
        GrantCurrentUser(signIn);
        var bytes = File.ReadAllBytes(signIn);
        Assert.NotEqual(0xEF, bytes[0]); // no BOM
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        var password = Get(ReadSecrets(), "Portal:FirstRun:AdminPassword")!;
        Assert.Equal($"admin\r\n{password}\r\n", text);
        Assert.Matches("^[A-Za-z0-9xy]{24}9!$", password);
    }

    [WindowsFact]
    public void ExistingPortalDb_WritesNoSignIn()
    {
        WriteOperator("{}");
        File.WriteAllText(Path.Combine(_install, "portal.db"), "db");

        Assert.False(Run().HasFlag(InstalledBootstrapResult.FirstRunWritten));

        Assert.False(File.Exists(Path.Combine(_install, "first-run-password.txt")));
    }

    [WindowsFact]
    public void RetryReusesSavedPassword()
    {
        WriteOperator("{}");
        Run();
        var signIn = Path.Combine(_install, "first-run-password.txt");
        GrantCurrentUser(signIn);
        var first = File.ReadAllText(signIn);
        File.Delete(signIn);
        GrantCurrentUser(Path.Combine(_install, ConfigurationExtensions.InstalledSecretsFileName)); // as SYSTEM reads it

        Run();

        GrantCurrentUser(signIn);
        Assert.Equal(first, File.ReadAllText(signIn));
    }

    [WindowsFact]
    public void SecretsInAppSettings_MoveAndLeaveEmptyValues()
    {
        WriteOperator($$"""
            {
              "Portal": { "Jwt": { "Secret": "{{Planted}}", "PreviousSecrets": [ "old-1" ], "ExpiryMinutes": 30 },
                          "Orchestrator": { "ApiKey": "operator-key" } },
              "Orchestrator": { "ApiKey": "operator-key" },
              "UpgradeSentinel": { "Value": "kept" }
            }
            """);
        WriteSecrets("""{ "Portal": { "Jwt": { "Secret": "protected-wins" } } }""");

        Assert.True(Run().HasFlag(InstalledBootstrapResult.SecretsMoved));

        var secrets = ReadSecrets();
        Assert.Equal("protected-wins", Get(secrets, "Portal:Jwt:Secret"));
        Assert.Equal("old-1", (string?)secrets["Portal"]!["Jwt"]!["PreviousSecrets"]![0]);
        Assert.Equal("operator-key", Get(secrets, "Orchestrator:ApiKey"));
        Assert.Equal("operator-key", Get(secrets, "Portal:Orchestrator:ApiKey"));
        var operatorText = File.ReadAllText(Path.Combine(_install, "appsettings.json"));
        Assert.DoesNotContain(Planted, operatorText);
        Assert.DoesNotContain("operator-key", operatorText);
        var operatorFile = JsonNode.Parse(operatorText)!;
        Assert.Equal("", Get(operatorFile, "Portal:Jwt:Secret"));
        Assert.Empty(operatorFile["Portal"]!["Jwt"]!["PreviousSecrets"]!.AsArray());
        Assert.Equal(30, (int)operatorFile["Portal"]!["Jwt"]!["ExpiryMinutes"]!);
        Assert.Equal("kept", Get(operatorFile, "UpgradeSentinel:Value"));
    }

    [WindowsFact]
    public void SecondRun_RegeneratesNothing()
    {
        WriteOperator("{}");
        Run();
        var path = Path.Combine(_install, ConfigurationExtensions.InstalledSecretsFileName);
        GrantCurrentUser(path);
        var before = File.ReadAllBytes(path);

        var result = Run();

        Assert.False(result.HasFlag(InstalledBootstrapResult.SecretsGenerated));
        GrantCurrentUser(path);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [WindowsFact]
    public void TwoConcurrentRuns_AgreeOnSharedSecrets()
    {
        WriteOperator("{}");
        using var start = new Barrier(2);
        var runs = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            start.SignalAndWait();
            // The second run must read the first one's protected file, as SYSTEM would.
            for (var attempt = 0; ; attempt++)
            {
                try { return Run(); }
                catch (InvalidOperationException) when (attempt < 20) { GrantAll(); Thread.Sleep(50); }
            }
        })).ToArray();
        Task.WaitAll(runs);

        var secrets = ReadSecrets();
        Assert.Equal(Get(secrets, "Orchestrator:ApiKey"), Get(secrets, "Portal:Orchestrator:ApiKey"));
        Assert.Single(runs, r => r.Result.HasFlag(InstalledBootstrapResult.SecretsGenerated));
    }

    [WindowsFact]
    public void OperatorFileWithCommentsAndTrailingCommas_IsAccepted()
    {
        WriteOperator($$"""
            {
              // operator note
              "Orchestrator": { "ApiKey": "{{Planted}}", },
            }
            """);

        Assert.True(Run().HasFlag(InstalledBootstrapResult.SecretsMoved));

        Assert.Equal(Planted, Get(ReadSecrets(), "Orchestrator:ApiKey"));
    }

    [WindowsTheory]
    [InlineData("[]")]
    [InlineData("\"x\"")]
    [InlineData("null")]
    [InlineData("{")]
    public void NonObjectRoot_FailsWithoutChangingAnything(string document)
    {
        WriteOperator(document);

        var ex = Assert.Throws<InvalidOperationException>(() => Run());

        Assert.Equal("ETL-SQL installed configuration could not be prepared.", ex.Message);
        Assert.Equal(["appsettings.json"], Directory.GetFiles(_install).Select(Path.GetFileName));
        Assert.Equal(document, File.ReadAllText(Path.Combine(_install, "appsettings.json")));
    }

    [WindowsFact]
    public void LockedSecretsFile_FailsKeepsKeysAndPublishesNoSignIn()
    {
        WriteOperator($$"""{ "Portal": { "Jwt": { "Secret": "{{Planted}}" } } }""");
        WriteSecrets("""{ "Orchestrator": { "ApiKey": "kept-key" } }""");
        var path = Path.Combine(_install, ConfigurationExtensions.InstalledSecretsFileName);

        InvalidOperationException ex;
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            ex = Assert.Throws<InvalidOperationException>(() => Run());

        GrantCurrentUser(path); // the failed write still re-protects the existing file
        Assert.Equal("""{ "Orchestrator": { "ApiKey": "kept-key" } }""", File.ReadAllText(path));
        Assert.False(File.Exists(Path.Combine(_install, "first-run-password.txt")));
        Assert.Empty(Directory.GetFiles(_install, "*.tmp"));
        for (Exception? e = ex; e is not null; e = e.InnerException)
            Assert.DoesNotContain(Planted, e.Message);

        Run();
        Assert.Equal("kept-key", Get(ReadSecrets(), "Orchestrator:ApiKey"));
        Assert.Equal(Planted, Get(ReadSecrets(), "Portal:Jwt:Secret"));
    }

    private InstalledBootstrapResult Run(string? registered = "") =>
        InstalledConfigurationBootstrap.Ensure(new InstalledBootstrapOptions(
            _install, registered == "" ? _install : registered, _mutex));

    private void WriteOperator(string json) => File.WriteAllText(Path.Combine(_install, "appsettings.json"), json);

    private void WriteSecrets(string json) =>
        File.WriteAllText(Path.Combine(_install, ConfigurationExtensions.InstalledSecretsFileName), json);

    private JsonNode ReadSecrets()
    {
        var path = Path.Combine(_install, ConfigurationExtensions.InstalledSecretsFileName);
        GrantCurrentUser(path);
        return JsonNode.Parse(File.ReadAllText(path))!;
    }

    private static string? Get(JsonNode node, string path)
    {
        JsonNode? current = node;
        foreach (var part in path.Split(':')) current = current?[part];
        return (string?)current;
    }

    private void GrantAll()
    {
        foreach (var file in Directory.GetFiles(_install))
        {
            try { GrantCurrentUser(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static HashSet<string> Grantees(string path) =>
        new FileInfo(path).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().Select(rule => ((SecurityIdentifier)rule.IdentityReference).Value).ToHashSet();

    private static void GrantCurrentUser(string path)
    {
        var file = new FileInfo(path);
        var security = file.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, AccessControlType.Allow));
        file.SetAccessControl(security);
    }
}
