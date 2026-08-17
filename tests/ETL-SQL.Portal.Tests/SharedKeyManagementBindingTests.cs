using ETL_SQL.Core.Security;
using ETL_SQL.Portal.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Smoke.Security")]
public sealed class SharedKeyManagementBindingTests
{
    [Fact]
    public void SharedBindingsRequireValidatedExplicitScopesWhileDedicatedRejectsRelabeling()
    {
        var shared = new PortalConfig
        {
            SharedTenancy = new SharedTenancyConfig { Enabled = true }
        };
        Assert.Throws<InvalidOperationException>(() =>
            KeyManagementBindingScope.Resolve(shared, new KeyManagementBindingConfig()));
        Assert.Equal("tenant-alpha", KeyManagementBindingScope.Resolve(
            shared, new KeyManagementBindingConfig { Scope = "tenant-alpha" }));

        var dedicated = new PortalConfig { TenantId = "tenant-alpha" };
        Assert.Equal("tenant-alpha", KeyManagementBindingScope.Resolve(
            dedicated, new KeyManagementBindingConfig()));
        Assert.Throws<InvalidOperationException>(() => KeyManagementBindingScope.Resolve(
            dedicated, new KeyManagementBindingConfig { Scope = "tenant-beta" }));
    }

    [Fact]
    public async Task StartupValidationChecksEveryConfiguredSharedTenantScope()
    {
        var config = SharedConfig();
        var provider = Provider(includeBetaCheckpoint: false);
        var lifetime = new CapturingLifetime();
        var service = new DatasetAtRestKeyValidationService(
            config,
            lifetime,
            NullLogger<DatasetAtRestKeyValidationService>.Instance,
            provider);

        await service.StartAsync(CancellationToken.None);

        Assert.True(lifetime.StopRequested);
        Assert.Equal(["tenant-alpha", "tenant-beta"],
            KeyManagementBindingScope.ConfiguredScopes(config));
    }

    [Fact]
    public async Task EqualVersionsResolveToDifferentMaterialInEachSharedTenantNamespace()
    {
        var provider = Provider(includeBetaCheckpoint: true);
        using var alpha = await provider.ResolveAsync(
            new KeyMaterialRequest("tenant-alpha", KeyPurpose.Dataset, "v1"));
        using var beta = await provider.ResolveAsync(
            new KeyMaterialRequest("tenant-beta", KeyPurpose.Dataset, "v1"));

        Assert.NotEqual(alpha.Bytes.ToArray(), beta.Bytes.ToArray());
        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await provider.ResolveAsync(
                new KeyMaterialRequest("tenant-gamma", KeyPurpose.Dataset, "v1")));
    }

    private static PortalConfig SharedConfig()
    {
        var config = new PortalConfig
        {
            SharedTenancy = new SharedTenancyConfig { Enabled = true },
            KeyManagement = new KeyManagementConfig { Enabled = true }
        };
        foreach (var tenant in new[] { "tenant-alpha", "tenant-beta" })
            foreach (var purpose in Enum.GetNames<KeyPurpose>())
            {
                config.KeyManagement.Bindings.Add(new KeyManagementBindingConfig
                {
                    Scope = tenant,
                    Purpose = purpose,
                    Version = "v1",
                    KeyId = $"{tenant}-{purpose.ToLowerInvariant()}",
                    EnvironmentVariable = $"KEY_{tenant}_{purpose}",
                    IsCurrent = true
                });
            }
        return config;
    }

    private static ResolvedKeyMaterialProvider Provider(bool includeBetaCheckpoint)
    {
        var entries = new List<(KeyMaterialDescriptor Descriptor, byte[] Bytes)>();
        byte marker = 1;
        foreach (var tenant in new[] { "tenant-alpha", "tenant-beta" })
            foreach (var purpose in Enum.GetValues<KeyPurpose>())
            {
                if (!includeBetaCheckpoint && tenant == "tenant-beta" && purpose == KeyPurpose.Checkpoint)
                    continue;
                entries.Add((
                    new KeyMaterialDescriptor(
                        "test-vault", $"{tenant}-{purpose}", tenant, purpose, "v1"),
                    Enumerable.Repeat(marker++, 32).ToArray()));
            }
        return new ResolvedKeyMaterialProvider("test-vault", entries);
    }

    private sealed class CapturingLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();
        public bool StopRequested => _stopping.IsCancellationRequested;
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() => _stopping.Cancel();
    }
}
