using System;
using ETL_SQL.Portal;
using ETL_SQL.Portal.Services;
using Xunit;
using Sev = ETL_SQL.Portal.Services.DatasetAtRestKeyValidator.Severity;
using ETL_SQL.Core.Security;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// 2i: the portal must fail closed when the at-rest key is missing/weak, unless an operator opts into
/// the dev/standalone MACHINE fallback. These cover the pure validation rules.
/// </summary>
public class DatasetAtRestKeyValidatorTests
{
    [Fact]
    public async Task ProviderContractRequiresAllPurposesAndRejectsCrossPurposeKeyReuse()
    {
        static KeyMaterialDescriptor Descriptor(KeyPurpose purpose) =>
            new("test-vault", purpose.ToString(), "tenant-alpha", purpose, "v1");
        static byte[] Key(byte marker) => Enumerable.Repeat(marker, 32).ToArray();

        var complete = new ResolvedKeyMaterialProvider("test-vault",
        [
            (Descriptor(KeyPurpose.Dataset), Key(1)),
            (Descriptor(KeyPurpose.Credential), Key(2)),
            (Descriptor(KeyPurpose.Artifact), Key(3)),
            (Descriptor(KeyPurpose.Checkpoint), Key(4))
        ]);
        var valid = await DatasetAtRestKeyValidator.ValidateProviderAsync(complete, "tenant-alpha");
        Assert.True(valid.IsValid);
        Assert.Equal(4, valid.Descriptors.Count);
        Assert.Empty(valid.Errors);

        var reused = new ResolvedKeyMaterialProvider("test-vault",
        [
            (Descriptor(KeyPurpose.Dataset), Key(1)),
            (Descriptor(KeyPurpose.Credential), Key(1)),
            (Descriptor(KeyPurpose.Artifact), Key(3)),
            (Descriptor(KeyPurpose.Checkpoint), Key(4))
        ]);
        var invalid = await DatasetAtRestKeyValidator.ValidateProviderAsync(reused, "tenant-alpha");
        Assert.False(invalid.IsValid);
        Assert.Contains(invalid.Errors, error => error.Contains("reused", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProviderValidationReturnsDescriptorsWithoutResolvedMaterial()
    {
        static (KeyMaterialDescriptor, byte[]) Entry(KeyPurpose purpose, byte marker) =>
            (new("vault", purpose.ToString(), "tenant-alpha", purpose, "2026-08"),
                Enumerable.Repeat(marker, 32).ToArray());
        var provider = new ResolvedKeyMaterialProvider("vault",
        [
            Entry(KeyPurpose.Dataset, 1), Entry(KeyPurpose.Credential, 2),
            Entry(KeyPurpose.Artifact, 3), Entry(KeyPurpose.Checkpoint, 4)
        ]);

        var result = await DatasetAtRestKeyValidator.ValidateProviderAsync(provider, "tenant-alpha");
        var json = System.Text.Json.JsonSerializer.Serialize(result);

        Assert.True(result.IsValid);
        Assert.DoesNotContain(Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray()), json);
        Assert.Contains("2026-08", json, StringComparison.Ordinal);
    }
    private static Sev Validate(string? key, bool allowFallback) =>
        DatasetAtRestKeyValidator.Validate(new DatasetConfig { AtRestKey = key, AllowMachineFallback = allowFallback }).Severity;

    [Fact]
    public void ValidBase64Key_AtLeast32Bytes_IsOk()
    {
        var key = Convert.ToBase64String(new byte[32]);   // 256-bit
        Assert.Equal(Sev.Ok, Validate(key, allowFallback: false));
    }

    [Fact]
    public void EmptyKey_WithFallback_IsWarn()
    {
        Assert.Equal(Sev.Warn, Validate("", allowFallback: true));
        Assert.Equal(Sev.Warn, Validate(null, allowFallback: true));
    }

    [Fact]
    public void EmptyKey_WithoutFallback_IsFatal()
    {
        Assert.Equal(Sev.Fatal, Validate("", allowFallback: false));
        Assert.Equal(Sev.Fatal, Validate(null, allowFallback: false));
    }

    [Fact]
    public void NonBase64Key_IsFatal()
    {
        Assert.Equal(Sev.Fatal, Validate("not valid base64 !!!", allowFallback: false));
    }

    [Fact]
    public void Base64Key_TooShort_IsFatal()
    {
        var shortKey = Convert.ToBase64String(new byte[16]);   // only 128-bit
        Assert.Equal(Sev.Fatal, Validate(shortKey, allowFallback: false));
    }

    [Fact]
    public void PreviousKey_MustBeStrongAndUseDifferentVersion()
    {
        var key = Convert.ToBase64String(new byte[32]);
        var duplicate = new DatasetConfig
        {
            AtRestKey = key,
            AtRestKeyVersion = "v2",
            PreviousAtRestKeys = new() { ["v2"] = key }
        };
        Assert.Equal(Sev.Fatal, DatasetAtRestKeyValidator.Validate(duplicate).Severity);

        var weak = new DatasetConfig
        {
            AtRestKey = key,
            AtRestKeyVersion = "v2",
            PreviousAtRestKeys = new() { ["v1"] = Convert.ToBase64String(new byte[16]) }
        };
        Assert.Equal(Sev.Fatal, DatasetAtRestKeyValidator.Validate(weak).Severity);
    }

    [Fact]
    public void LegacyVersion_MustResolveToConfiguredKey()
    {
        var key = Convert.ToBase64String(new byte[32]);
        var config = new DatasetConfig
        {
            AtRestKey = key,
            AtRestKeyVersion = "v2",
            LegacyAtRestKeyVersion = "v1"
        };

        Assert.Equal(Sev.Fatal, DatasetAtRestKeyValidator.Validate(config).Severity);
        config.PreviousAtRestKeys["v1"] = Convert.ToBase64String(new byte[32]);
        Assert.Equal(Sev.Ok, DatasetAtRestKeyValidator.Validate(config).Severity);
    }
}
