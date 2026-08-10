using System.Text.Json;
using ETL_SQL.Core.Security;

namespace ETL_SQL.Tests.Security;

public sealed class KeyMaterialContractTests
{
    [Fact]
    public async Task EqualVersionsAcrossPurposeAndScopeNeverResolveTheSameBinding()
    {
        var provider = Provider();

        using var alphaDataset = await provider.ResolveAsync(new("tenant-alpha", KeyPurpose.Dataset));
        using var alphaArtifact = await provider.ResolveAsync(new("tenant-alpha", KeyPurpose.Artifact));
        using var betaDataset = await provider.ResolveAsync(new("tenant-beta", KeyPurpose.Dataset));

        Assert.NotEqual(alphaDataset.Bytes.ToArray(), alphaArtifact.Bytes.ToArray());
        Assert.NotEqual(alphaDataset.Bytes.ToArray(), betaDataset.Bytes.ToArray());
        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await provider.ResolveAsync(new("tenant-beta", KeyPurpose.Credential)));
    }

    [Fact]
    public async Task VersionLookupIsExactAndCallerCannotWidenScope()
    {
        var provider = Provider();
        using var current = await provider.ResolveAsync(new("tenant-alpha", KeyPurpose.Dataset));
        using var exact = await provider.ResolveAsync(new("tenant-alpha", KeyPurpose.Dataset, "v1"));

        Assert.Equal(current.Bytes.ToArray(), exact.Bytes.ToArray());
        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await provider.ResolveAsync(new("tenant-alpha", KeyPurpose.Dataset, "v2")));
        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await provider.ResolveAsync(new("tenant-alpha/../tenant-beta", KeyPurpose.Dataset, "v1")));
    }

    [Fact]
    public async Task CurrentAndPreviousVersionsResolveWithoutAmbiguity()
    {
        var current = new KeyMaterialDescriptor(
            "vault", "dataset-current", "tenant-alpha", KeyPurpose.Dataset, "v2");
        var previous = new KeyMaterialDescriptor(
            "vault", "dataset-previous", "tenant-alpha", KeyPurpose.Dataset, "v1", IsCurrent: false);
        var provider = new ResolvedKeyMaterialProvider("vault",
        [
            (current, Enumerable.Repeat((byte)2, 32).ToArray()),
            (previous, Enumerable.Repeat((byte)1, 32).ToArray())
        ]);

        using var currentLease = await provider.ResolveAsync(new("tenant-alpha", KeyPurpose.Dataset));
        using var previousLease = await provider.ResolveAsync(new("tenant-alpha", KeyPurpose.Dataset, "v1"));

        Assert.Equal("v2", currentLease.Descriptor.Version);
        Assert.Equal("v1", previousLease.Descriptor.Version);
        Assert.Throws<ArgumentException>(() => new ResolvedKeyMaterialProvider("vault",
        [
            (current, Enumerable.Repeat((byte)2, 32).ToArray()),
            (previous with { IsCurrent = true }, Enumerable.Repeat((byte)1, 32).ToArray())
        ]));
    }

    [Fact]
    public async Task ResolvedBytesNeverEnterSerializationOrStringFormAndLeaseExpiresOnDispose()
    {
        var provider = Provider();
        var lease = await provider.ResolveAsync(new("tenant-alpha", KeyPurpose.Credential));
        var secret = Convert.ToBase64String(lease.Bytes.Span);

        var json = JsonSerializer.Serialize(lease);
        Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, lease.ToString(), StringComparison.Ordinal);
        Assert.Contains("Credential", json, StringComparison.Ordinal);

        lease.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _ = lease.Bytes);
    }

    [Fact]
    public async Task EnvironmentAdapterExportsOnlyBindingMetadataAndResolvesExactPurpose()
    {
        var encoded = Convert.ToBase64String(Enumerable.Repeat((byte)77, 32).ToArray());
        var binding = new EnvironmentKeyMaterialBinding(
            "ETLSQL_TEST_DATASET_KEY",
            new("environment", "dataset-key", "tenant-alpha", KeyPurpose.Dataset, "v3"));
        var provider = new EnvironmentKeyMaterialProvider(
            [binding],
            name => name == binding.EnvironmentVariable ? encoded : null);

        var exportedBinding = JsonSerializer.Serialize(binding);
        Assert.DoesNotContain(encoded, exportedBinding, StringComparison.Ordinal);
        Assert.Contains("ETLSQL_TEST_DATASET_KEY", exportedBinding, StringComparison.Ordinal);

        using var lease = await provider.ResolveAsync(new("tenant-alpha", KeyPurpose.Dataset));
        Assert.Equal("v3", lease.Descriptor.Version);
        Assert.Equal(Enumerable.Repeat((byte)77, 32), lease.Bytes.ToArray());
        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await provider.ResolveAsync(new("tenant-alpha", KeyPurpose.Artifact)));
    }

    [Fact]
    public async Task TenantNamespaceValidationRejectsCrossTenantKeyReuse()
    {
        static (KeyMaterialDescriptor, byte[]) Entry(
            string tenant, KeyPurpose purpose, byte marker) =>
            (new("provisioning-vault", $"{tenant}-{purpose}", tenant, purpose, "v1"),
                Enumerable.Repeat(marker, 32).ToArray());

        var provider = new ResolvedKeyMaterialProvider("provisioning-vault",
        [
            Entry("tenant-alpha", KeyPurpose.Dataset, 1),
            Entry("tenant-alpha", KeyPurpose.Credential, 2),
            Entry("tenant-alpha", KeyPurpose.Artifact, 3),
            Entry("tenant-alpha", KeyPurpose.Checkpoint, 4),
            Entry("tenant-beta", KeyPurpose.Dataset, 5),
            Entry("tenant-beta", KeyPurpose.Credential, 6),
            Entry("tenant-beta", KeyPurpose.Artifact, 3), // forbidden reuse from alpha
            Entry("tenant-beta", KeyPurpose.Checkpoint, 8)
        ]);

        var result = await KeyMaterialContractValidator.ValidateTenantNamespacesAsync(
            provider, ["tenant-alpha", "tenant-beta"]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
            error.Contains("tenant-beta/Artifact", StringComparison.Ordinal)
            && error.Contains("tenant-alpha/Artifact", StringComparison.Ordinal));
        Assert.All(result.Descriptors, descriptor =>
            Assert.DoesNotContain("AQEBAQ", JsonSerializer.Serialize(descriptor), StringComparison.Ordinal));
    }

    private static ResolvedKeyMaterialProvider Provider()
    {
        static (KeyMaterialDescriptor, byte[]) Entry(string scope, KeyPurpose purpose, byte marker) =>
            (new("test-vault", $"{scope}-{purpose}", scope, purpose, "v1"),
                Enumerable.Repeat(marker, 32).ToArray());

        return new ResolvedKeyMaterialProvider("test-vault",
        [
            Entry("tenant-alpha", KeyPurpose.Dataset, 1),
            Entry("tenant-alpha", KeyPurpose.Credential, 2),
            Entry("tenant-alpha", KeyPurpose.Artifact, 3),
            Entry("tenant-alpha", KeyPurpose.Checkpoint, 4),
            Entry("tenant-beta", KeyPurpose.Dataset, 5)
        ]);
    }
}
