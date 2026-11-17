using System.Net;
using ETL_SQL.Common;
using ETL_SQL.Core.Governance;
using Xunit;

namespace ETL_SQL.Tests.Core;

public class SecretProviderTests
{
    [Fact]
    public async Task EnvironmentSecretProvider_ResolvesPrefixedSecret()
    {
        var provider = new EnvironmentSecretProvider(
            "ETLSQL_",
            key => key == "ETLSQL_SALES_DB_PASSWORD" ? "env-secret" : null);

        var result = await provider.ResolveAsync("sales-db-password");

        Assert.Equal("Environment", result.Provider);
        Assert.Equal("sales-db-password", result.Name);
        Assert.Equal("env-secret", result.Value);
    }

    [Fact]
    public async Task OsSecretStoreProvider_StoresAndResolvesProtectedSecret()
    {
        var root = Path.Combine(Path.GetTempPath(), $"etl-secret-store-{Guid.NewGuid():N}");
        var provider = new OsSecretStoreProvider(root, LocalVaultProtectionScope.CurrentUser);

        try
        {
            await provider.StoreAsync("sales_db_password", "stored-secret");
            var result = await provider.ResolveAsync("sales_db_password");

            Assert.Equal("OsSecretStore", result.Provider);
            Assert.Equal("stored-secret", result.Value);
            Assert.DoesNotContain("stored-secret", File.ReadAllText(Path.Combine(root, "sales_db_password.secret")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("CompatBreak", "0.23")]
    public async Task OsSecretStoreProvider_MachineWritesRequireAnAdministratorOnWindows()
    {
        var root = Path.Combine(Path.GetTempPath(), $"etl-secret-store-{Guid.NewGuid():N}");
        var serviceAccount = OperatingSystem.IsWindows()
            ? System.Security.Principal.WindowsIdentity.GetCurrent().Name
            : null;
        var provider = new OsSecretStoreProvider(root, LocalVaultProtectionScope.LocalMachine, serviceAccount);

        try
        {
            if (!LocalStoreProtectionOrderingTests.CanWriteMachineStore)
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.StoreAsync("sales_db_password", "stored-secret"));
                Assert.False(File.Exists(Path.Combine(root, "sales_db_password.secret")));
                return;
            }
            await provider.StoreAsync("sales_db_password", "stored-secret");

            var secretFile = Path.Combine(root, "sales_db_password.secret");
            var payload = File.ReadAllText(secretFile);
            var expectedPrefix = OperatingSystem.IsWindows() ? "DPAPI-M:" : "MACHINE:";
            Assert.StartsWith(expectedPrefix, payload);
        }
        finally
        {
            CleanupWindowsLockedDirectory(root);
        }
    }

    [Fact]
    public async Task OsSecretStoreProvider_CurrentUserScope_UsesUserDpapiAndRestrictiveAcls()
    {
        var root = Path.Combine(Path.GetTempPath(), $"etl-secret-store-user-{Guid.NewGuid():N}");
        var provider = new OsSecretStoreProvider(root, LocalVaultProtectionScope.CurrentUser);

        try
        {
            await provider.StoreAsync("api_token", "user-secret");

            var secretFile = Path.Combine(root, "api_token.secret");
            var raw = File.ReadAllText(secretFile);
            Assert.StartsWith(OperatingSystem.IsWindows() ? "DPAPI:" : "MACHINE:", raw);
            Assert.Equal("user-secret", (await provider.ResolveAsync("api_token")).Value);

            if (OperatingSystem.IsWindows())
            {
                using var current = System.Security.Principal.WindowsIdentity.GetCurrent();
                var expectedSids = new[]
                {
                    new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.LocalSystemSid, null),
                    new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null),
                    current.User!
                };
                AssertWindowsAclContainsOnly(new DirectoryInfo(root).GetAccessControl(), expectedSids);
                AssertWindowsAclContainsOnly(new FileInfo(secretFile).GetAccessControl(), expectedSids);
            }
            else
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(root));
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(secretFile));
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OsSecretStoreProvider_LocalMachineScope_AppliesStrictWindowsAcls()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = Path.Combine(Path.GetTempPath(), $"etl-secret-store-machine-{Guid.NewGuid():N}");
        var provider = new OsSecretStoreProvider(root, LocalVaultProtectionScope.LocalMachine);

        try
        {
            if (LocalStoreProtectionOrderingTests.CanWriteMachineStore)
                await provider.StoreAsync("machine_token", "machine-secret");
            else
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.StoreAsync("machine_token", "machine-secret"));
            var secretFile = Path.Combine(root, "machine_token.secret");
            var expectedSids = new[]
            {
                new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.LocalSystemSid, null),
                new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null)
            };

            AssertWindowsAclContainsOnly(new DirectoryInfo(root).GetAccessControl(), expectedSids);
            if (LocalStoreProtectionOrderingTests.CanWriteMachineStore)
                AssertWindowsAclContainsOnly(new FileInfo(secretFile).GetAccessControl(), expectedSids);
            else
                Assert.False(File.Exists(secretFile));
        }
        finally
        {
            CleanupWindowsLockedDirectory(root);
        }
    }

    [Fact]
    public void SecretProviderOptions_FromConfiguration_BindsProtectionScopeAndServiceIdentity()
    {
        var values = new Dictionary<string, string?>
        {
            ["Governance:Secrets:Provider"] = "OsSecretStore",
            ["Governance:Secrets:OsStoreRoot"] = @"C:\vault",
            ["Governance:Secrets:ProtectionScope"] = "CurrentUser",
            ["Governance:Secrets:ServiceIdentity"] = @"NT AUTHORITY\NETWORK SERVICE"
        };

        var options = SecretProviderOptions.FromConfiguration(k => values.GetValueOrDefault(k));
        Assert.Equal("OsSecretStore", options.Provider);
        Assert.Equal(LocalVaultProtectionScope.CurrentUser, options.ProtectionScope);
        Assert.Equal(@"NT AUTHORITY\NETWORK SERVICE", options.ServiceIdentity);

        values["Governance:Secrets:ProtectionScope"] = "InvalidScope";
        Assert.Throws<InvalidOperationException>(() =>
            SecretProviderOptions.FromConfiguration(k => values.GetValueOrDefault(k)));
    }

    [Fact]
    public async Task OsSecretStoreProvider_ResolvesLegacyUserScopedPayload()
    {
        var root = Path.Combine(Path.GetTempPath(), $"etl-secret-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "legacy_secret.secret"),
            CryptoUtils.Protect("legacy-value", "legacy_secret"));
        var provider = new OsSecretStoreProvider(root);

        var result = await provider.ResolveAsync("legacy_secret");

        Assert.Equal("legacy-value", result.Value);
    }

    [Fact]
    public async Task OsSecretStoreProvider_RejectsUnprotectedPayload()
    {
        var root = Path.Combine(Path.GetTempPath(), $"etl-secret-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "tampered.secret"), "plaintext-value");
        var provider = new OsSecretStoreProvider(root);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ResolveAsync("tampered"));

        Assert.DoesNotContain("plaintext-value", ex.Message);
    }

    [Fact]
    public void ProtectMachine_RoundTripsAndUsesMachinePrefix()
    {
        var protectedValue = CryptoUtils.ProtectMachine("machine-secret", "entropy-1");

        var expectedPrefix = OperatingSystem.IsWindows() ? "DPAPI-M:" : "MACHINE:";
        Assert.StartsWith(expectedPrefix, protectedValue);
        Assert.Equal("machine-secret", CryptoUtils.Unprotect(protectedValue, "entropy-1"));
    }

    [Fact]
    public async Task HttpsVaultSecretProvider_ResolvesJsonValue()
    {
        using var http = new HttpClient(new StubHttpHandler(HttpStatusCode.OK, """{"value":"vault-secret"}"""));
        var provider = new HttpsVaultSecretProvider(new Uri("https://vault.example.test/secrets"), http, "token-1");

        var result = await provider.ResolveAsync("sales_db_password");

        Assert.Equal("HttpsVault", result.Provider);
        Assert.Equal("vault-secret", result.Value);
    }

    [Fact]
    public async Task HttpsVaultSecretProvider_RejectsHttpEndpoint()
    {
        using var http = new HttpClient(new StubHttpHandler(HttpStatusCode.OK, "secret"));
        var provider = new HttpsVaultSecretProvider(new Uri("http://vault.example.test/secrets"), http);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ResolveAsync("sales_db_password"));

        Assert.Contains("HTTPS", ex.Message);
    }

    [Fact]
    public void SecretProviderFactory_CreatesConfiguredProvider()
    {
        using var http = new HttpClient(new StubHttpHandler(HttpStatusCode.OK, "secret"));
        var factory = new SecretProviderFactory(http);

        var provider = factory.Create(new SecretProviderOptions
        {
            Provider = "HttpsVault",
            VaultEndpoint = "https://vault.example.test/secrets"
        });

        Assert.Equal("HttpsVault", provider.ProviderName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../secret")]
    [InlineData("secret/name")]
    public async Task Providers_RejectInvalidSecretNames(string name)
    {
        var provider = new EnvironmentSecretProvider(getEnvironmentVariable: _ => "secret");

        await Assert.ThrowsAnyAsync<ArgumentException>(() => provider.ResolveAsync(name));
    }

    private sealed class StubHttpHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content)
            });
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void AssertWindowsAclContainsOnly(
        System.Security.AccessControl.FileSystemSecurity security,
        params System.Security.Principal.SecurityIdentifier[] expectedSids)
    {
        Assert.True(security.AreAccessRulesProtected);
        var rules = security
            .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(System.Security.Principal.SecurityIdentifier))
            .Cast<System.Security.AccessControl.FileSystemAccessRule>()
            .ToList();
        Assert.All(rules, rule => Assert.False(rule.IsInherited));
        Assert.Equal(expectedSids.ToHashSet(), rules.Select(r => (System.Security.Principal.SecurityIdentifier)r.IdentityReference).ToHashSet());
    }

    private static void CleanupWindowsLockedDirectory(string root)
    {
        if (!Directory.Exists(root))
            return;

        if (OperatingSystem.IsWindows())
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            if (identity.User is not null)
            {
                var dirSec = new System.Security.AccessControl.DirectorySecurity();
                dirSec.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                    identity.User,
                    System.Security.AccessControl.FileSystemRights.FullControl,
                    System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                    System.Security.AccessControl.PropagationFlags.None,
                    System.Security.AccessControl.AccessControlType.Allow));
                new DirectoryInfo(root).SetAccessControl(dirSec);

                foreach (var file in Directory.GetFiles(root))
                {
                    var fileSec = new System.Security.AccessControl.FileSecurity();
                    fileSec.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                        identity.User,
                        System.Security.AccessControl.FileSystemRights.FullControl,
                        System.Security.AccessControl.AccessControlType.Allow));
                    new FileInfo(file).SetAccessControl(fileSec);
                }
            }
        }

        Directory.Delete(root, recursive: true);
    }
}
