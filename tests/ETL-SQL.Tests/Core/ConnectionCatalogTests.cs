using ETL_SQL.Core.Governance;
using Xunit;

namespace ETL_SQL.Tests.Core;

public class ConnectionCatalogTests
{
    [Fact]
    public async Task LocalProvider_StoreAndResolve_RoundTripsMachineEncrypted()
    {
        var root = TempRoot();
        var serviceAccount = OperatingSystem.IsWindows()
            ? System.Security.Principal.WindowsIdentity.GetCurrent().Name
            : null;
        var provider = new LocalConnectionCatalogProvider(root, LocalVaultProtectionScope.LocalMachine, serviceAccount);
        var definition = new SharedConnectionDefinition(
            "my_sql_server",
            "MSSQL",
            Target: null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SERVER"] = "sql01",
                ["DATABASE"] = "Sales",
                ["PASSWORD"] = "SECRET:sales_db_password"
            },
            Disabled: false);

        try
        {
            await provider.StoreAsync(definition);

            var connectionFile = Path.Combine(root, "my_sql_server.connection");
            var raw = File.ReadAllText(connectionFile);
            var expectedPrefix = OperatingSystem.IsWindows() ? "DPAPI-M:" : "MACHINE:";
            Assert.StartsWith(expectedPrefix, raw);
            Assert.DoesNotContain("sql01", raw);

            var resolved = await provider.ResolveAsync("my_sql_server");
            Assert.Equal("MSSQL", resolved.ConnectorType);
            Assert.Equal("sql01", resolved.Options["SERVER"]);
            Assert.Equal("SECRET:sales_db_password", resolved.Options["PASSWORD"]);
            Assert.False(resolved.Disabled);
        }
        finally
        {
            CleanupWindowsLockedDirectory(root);
        }
    }

    [Fact]
    public async Task LocalProvider_CurrentUserScope_UsesUserDpapiAndRestrictiveAcls()
    {
        var root = TempRoot();
        var provider = new LocalConnectionCatalogProvider(root, LocalVaultProtectionScope.CurrentUser);

        try
        {
            await provider.StoreAsync(new SharedConnectionDefinition(
                "user_pg", "POSTGRES", null,
                new Dictionary<string, string> { ["HOST"] = "pg01" }, false));

            var entryFile = Path.Combine(root, "user_pg.connection");
            var raw = File.ReadAllText(entryFile);
            Assert.StartsWith(OperatingSystem.IsWindows() ? "DPAPI:" : "MACHINE:", raw);
            Assert.Equal("pg01", (await provider.ResolveAsync("user_pg")).Options["HOST"]);

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
                AssertWindowsAclContainsOnly(new FileInfo(entryFile).GetAccessControl(), expectedSids);
            }
            else
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(root));
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(entryFile));
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LocalProvider_LocalMachineScope_AppliesStrictWindowsAcls()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = TempRoot();
        var provider = new LocalConnectionCatalogProvider(root, LocalVaultProtectionScope.LocalMachine);

        try
        {
            await provider.StoreAsync(new SharedConnectionDefinition(
                "machine_pg", "POSTGRES", null,
                new Dictionary<string, string> { ["HOST"] = "pg01" }, false));
            var entryFile = Path.Combine(root, "machine_pg.connection");
            var expectedSids = new[]
            {
                new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.LocalSystemSid, null),
                new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null)
            };

            AssertWindowsAclContainsOnly(new DirectoryInfo(root).GetAccessControl(), expectedSids);
            AssertWindowsAclContainsOnly(new FileInfo(entryFile).GetAccessControl(), expectedSids);
        }
        finally
        {
            CleanupWindowsLockedDirectory(root);
        }
    }

    [Fact]
    public void ConnectionCatalogOptions_FromConfiguration_BindsProtectionScopeAndServiceIdentity()
    {
        var values = new Dictionary<string, string?>
        {
            ["Governance:ConnectionCatalog:Provider"] = "Local",
            ["Governance:ConnectionCatalog:LocalRoot"] = @"C:\connections",
            ["Governance:ConnectionCatalog:ProtectionScope"] = "CurrentUser",
            ["Governance:ConnectionCatalog:ServiceIdentity"] = @"NT AUTHORITY\NETWORK SERVICE"
        };

        var options = ConnectionCatalogOptions.FromConfiguration(k => values.GetValueOrDefault(k));
        Assert.Equal("Local", options.Provider);
        Assert.Equal(LocalVaultProtectionScope.CurrentUser, options.ProtectionScope);
        Assert.Equal(@"NT AUTHORITY\NETWORK SERVICE", options.ServiceIdentity);

        values["Governance:ConnectionCatalog:ProtectionScope"] = "BadScope";
        Assert.Throws<InvalidOperationException>(() =>
            ConnectionCatalogOptions.FromConfiguration(k => values.GetValueOrDefault(k)));
    }

    [Fact]
    public async Task LocalProvider_DisableReenableDeleteLifecycle()
    {
        var provider = new LocalConnectionCatalogProvider(TempRoot(), LocalVaultProtectionScope.CurrentUser);
        var definition = new SharedConnectionDefinition(
            "warehouse", "POSTGRES", null,
            new Dictionary<string, string> { ["HOST"] = "pg01" }, false);

        await provider.StoreAsync(definition);
        Assert.Equal(SecretLifecycleStatus.Active, await provider.GetStatusAsync("warehouse"));
        Assert.Equal(["warehouse"], await provider.ListAsync());

        await provider.DisableAsync("warehouse");
        Assert.Equal(SecretLifecycleStatus.Disabled, await provider.GetStatusAsync("warehouse"));
        var disabledError = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ResolveAsync("warehouse"));
        Assert.Contains("disabled", disabledError.Message);
        var disabledDefinition = await provider.GetDefinitionAsync("warehouse");
        Assert.True(disabledDefinition.Disabled);
        Assert.Equal("POSTGRES", disabledDefinition.ConnectorType);
        Assert.Equal("pg01", disabledDefinition.Options["HOST"]);
        Assert.Equal(SecretLifecycleStatus.Disabled, await provider.GetStatusAsync("warehouse"));

        await provider.StoreAsync(definition);
        Assert.Equal(SecretLifecycleStatus.Active, await provider.GetStatusAsync("warehouse"));

        await provider.DeleteAsync("warehouse");
        Assert.Equal(SecretLifecycleStatus.NotFound, await provider.GetStatusAsync("warehouse"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => provider.ResolveAsync("warehouse"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => provider.DeleteAsync("warehouse"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../evil")]
    [InlineData("name/with/slash")]
    public async Task LocalProvider_RejectsInvalidAliases(string alias)
    {
        var provider = new LocalConnectionCatalogProvider(TempRoot());

        await Assert.ThrowsAnyAsync<ArgumentException>(() => provider.ResolveAsync(alias));
    }

    [Fact]
    public void Factory_ReturnsNullWhenUnconfigured_CreatesLocal_RejectsPortal()
    {
        Assert.Null(ConnectionCatalogProviderFactory.Create(new ConnectionCatalogOptions()));

        var local = ConnectionCatalogProviderFactory.Create(new ConnectionCatalogOptions
        {
            Provider = "Local",
            LocalRoot = TempRoot()
        });
        Assert.Equal("LocalCatalog", local!.ProviderName);

        var portal = Assert.Throws<InvalidOperationException>(() =>
            ConnectionCatalogProviderFactory.Create(new ConnectionCatalogOptions { Provider = "Portal" }));
        Assert.Contains("Portal", portal.Message);
    }

    private static string TempRoot() =>
        Path.Combine(Path.GetTempPath(), $"etl-connection-catalog-{Guid.NewGuid():N}");

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

    [Fact]
    public async Task LocalProvider_EnforcesOwnerPrincipalGroupAndTenantAcls()
    {
        var root = TempRoot();
        var provider = new LocalConnectionCatalogProvider(root, LocalVaultProtectionScope.CurrentUser);

        try
        {
            await provider.StoreAsync(new SharedConnectionDefinition(
                "restricted_dw",
                "MOCKDB",
                Target: null,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Disabled: false,
                OwnerPrincipal: "user:alice",
                AllowedPrincipals: ["user:carol"],
                AllowedGroups: ["Finance"],
                TenantId: "tenant-a",
                RequireAcl: true));

            // Anonymous / null identity is denied.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                provider.ResolveAsync("restricted_dw", identity: null));

            // Same-tenant non-owner without grant is denied.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                provider.ResolveAsync("restricted_dw", new ExecutionIdentity
                {
                    EffectiveUser = "user:bob",
                    RealUser = "user:bob",
                    TenantId = "tenant-a",
                    IsAdmin = false
                }));

            // Cross-tenant caller (even matching owner or admin) is denied.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                provider.ResolveAsync("restricted_dw", new ExecutionIdentity
                {
                    EffectiveUser = "user:alice",
                    RealUser = "user:alice",
                    TenantId = "tenant-b",
                    IsAdmin = true
                }));

            // Owner, explicitly allowed principal, group member, and same-tenant admin are allowed.
            Assert.Equal("MOCKDB", (await provider.ResolveAsync("restricted_dw", new ExecutionIdentity
            {
                EffectiveUser = "user:alice",
                RealUser = "user:alice",
                TenantId = "tenant-a",
                IsAdmin = false
            })).ConnectorType);

            Assert.Equal("MOCKDB", (await provider.ResolveAsync("restricted_dw", new ExecutionIdentity
            {
                EffectiveUser = "user:carol",
                RealUser = "user:carol",
                TenantId = "tenant-a",
                IsAdmin = false
            })).ConnectorType);

            Assert.Equal("MOCKDB", (await provider.ResolveAsync("restricted_dw", new ExecutionIdentity
            {
                EffectiveUser = "user:dave",
                RealUser = "user:dave",
                TenantId = "tenant-a",
                IsAdmin = false,
                Groups = ["Finance"]
            })).ConnectorType);

            Assert.Equal("MOCKDB", (await provider.ResolveAsync("restricted_dw", new ExecutionIdentity
            {
                EffectiveUser = "user:admin",
                RealUser = "user:admin",
                TenantId = "tenant-a",
                IsAdmin = true
            })).ConnectorType);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SharedConnectionReferenceScan_ExtractsAliasesAndFailsClosedOnInvalidScripts()
    {
        var scan = SharedConnectionReferenceScan.ExtractAliases(@"
            CREATE CONNECTION a AS MSSQL('SHARED:finance_dw');
            CREATE CONNECTION b AS POSTGRES(TARGET = 'SHARED:ops_pg', USER = 'u');
            DECLARE @c VARCHAR = 'SHARED:var_db';
            -- CREATE CONNECTION ignored AS MSSQL('SHARED:commented_out');
        ");
        Assert.True(scan.IsResolved);
        Assert.Equal(["finance_dw", "ops_pg", "var_db"], scan.Aliases);

        var invalid = SharedConnectionReferenceScan.ExtractAliases("CREATE CONNECTION broken AS (;");
        Assert.False(invalid.IsResolved);

        var dynamicConn = SharedConnectionReferenceScan.ExtractAliases("CREATE CONNECTION dyn AS MSSQL(@conn);");
        Assert.False(dynamicConn.IsResolved);
    }
}
