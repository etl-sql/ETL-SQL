using ETL_SQL.Core.Governance;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Core;

public class LocalStoreProtectionOrderingTests
{
    internal static bool CanWriteMachineStore
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return true;
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedFileProtectionLeavesExistingContentsUntouched(bool connection)
    {
        var root = Path.Combine(Path.GetTempPath(), $"etl-store-order-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, connection ? "test.connection" : "test.secret");
        await File.WriteAllTextAsync(path, "original");
        var protector = new Mock<ILocalStoreProtector>();
        protector.Setup(p => p.ProtectFile(path, LocalVaultProtectionScope.CurrentUser, null))
            .Throws(new UnauthorizedAccessException("Cannot protect file."));
        try
        {
            if (connection)
            {
                var provider = new LocalConnectionCatalogProvider(root, LocalVaultProtectionScope.CurrentUser, protector: protector.Object);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.StoreAsync(
                    new SharedConnectionDefinition("test", "MOCKDB", null, new Dictionary<string, string>(), false)));
            }
            else
            {
                var provider = new OsSecretStoreProvider(root, LocalVaultProtectionScope.CurrentUser, protector: protector.Object);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.StoreAsync("test", "test-value"));
            }
            Assert.Equal("original", await File.ReadAllTextAsync(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedDirectoryProtectionNeverWritesAProtectedValue(bool connection)
    {
        var root = Path.Combine(Path.GetTempPath(), $"etl-store-order-{Guid.NewGuid():N}");
        var protector = new Mock<ILocalStoreProtector>();
        protector.Setup(p => p.ProtectDirectory(root, LocalVaultProtectionScope.CurrentUser, null))
            .Throws(new UnauthorizedAccessException("Cannot protect directory."));
        try
        {
            if (connection)
            {
                var provider = new LocalConnectionCatalogProvider(root, LocalVaultProtectionScope.CurrentUser, protector: protector.Object);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.StoreAsync(
                    new SharedConnectionDefinition("test", "MOCKDB", null, new Dictionary<string, string>(), false)));
            }
            else
            {
                var provider = new OsSecretStoreProvider(root, LocalVaultProtectionScope.CurrentUser, protector: protector.Object);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.StoreAsync("test", "test-value"));
            }
            Assert.Empty(Directory.EnumerateFiles(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
