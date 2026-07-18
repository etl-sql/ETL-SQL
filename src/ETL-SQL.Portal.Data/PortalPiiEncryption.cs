using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Data;

/// <summary>
/// Context-owned protector for Portal PII columns. Replaces the former process-global static
/// provider: each Portal host owns its own <see cref="PortalPiiProtector"/> (built from that host's
/// Data Protection key ring) and attaches it to the context via <see cref="PortalEncryptionOptions.UsePortalEncryption"/>.
/// Two hosts running sequentially or side-by-side in one process therefore cannot replace or dispose
/// each other's key provider.
/// </summary>
public sealed class PortalPiiProtector
{
    private const string Prefix = "dp:";
    private readonly IDataProtector _protector;

    public PortalPiiProtector(IDataProtector protector) => _protector = protector;

    /// <summary>Creates a protector bound to the Portal PII purpose from the given provider.</summary>
    public static PortalPiiProtector Create(IDataProtectionProvider provider) =>
        new(provider.CreateProtector("ETL-SQL.Portal.PII"));

    public string? Encrypt(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.StartsWith(Prefix, StringComparison.Ordinal)) return value;

        return Prefix + _protector.Protect(value);
    }

    public string? Decrypt(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        try
        {
            if (value.StartsWith(Prefix, StringComparison.Ordinal))
                return _protector.Unprotect(value[Prefix.Length..]);

            return _protector.Unprotect(value);
        }
        catch (CryptographicException)
        {
            if (value.StartsWith(Prefix, StringComparison.Ordinal))
                throw;

            // Legacy unencrypted data remains readable; startup maintenance rewrites it encrypted.
            return value;
        }
    }

    public bool IsEncrypted(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        if (value.StartsWith(Prefix, StringComparison.Ordinal)) return true;

        try
        {
            _protector.Unprotect(value);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}

/// <summary>
/// EF value converter that encrypts a required string column at rest. When no protector is attached
/// (design-time/migrations), values pass through as plaintext — the same behaviour the old
/// uninitialized static provider had.
/// </summary>
public sealed class EncryptedDbConverter : ValueConverter<string, string>
{
    public EncryptedDbConverter(PortalPiiProtector? protector)
        : base(
            v => protector != null ? protector.Encrypt(v)! : v,
            v => protector != null ? protector.Decrypt(v)! : v)
    {
    }
}

/// <summary>EF value converter that encrypts a nullable string column at rest.</summary>
public sealed class EncryptedDbNullableConverter : ValueConverter<string?, string?>
{
    public EncryptedDbNullableConverter(PortalPiiProtector? protector)
        : base(
            v => protector != null ? protector.Encrypt(v) : v,
            v => protector != null ? protector.Decrypt(v) : v)
    {
    }
}

/// <summary>
/// Extension methods for attaching a <see cref="PortalPiiProtector"/> to a
/// <see cref="DbContextOptionsBuilder"/> so PII encryption is owned by the context rather than a
/// process-global static.
/// </summary>
public static class PortalEncryptionOptions
{
    public static DbContextOptionsBuilder UsePortalEncryption(
        this DbContextOptionsBuilder builder, PortalPiiProtector protector)
    {
        ArgumentNullException.ThrowIfNull(protector);
        ((IDbContextOptionsBuilderInfrastructure)builder)
            .AddOrUpdateExtension(new PortalEncryptionOptionsExtension(protector));
        // Isolate the compiled model per protector via the model-cache key (not per internal service
        // provider) so two hosts with different key rings never share converters, while all encryption
        // contexts still share one EF internal service provider.
        builder.ReplaceService<IModelCacheKeyFactory, PortalModelCacheKeyFactory>();
        return builder;
    }

    public static DbContextOptionsBuilder<TContext> UsePortalEncryption<TContext>(
        this DbContextOptionsBuilder<TContext> builder, PortalPiiProtector protector)
        where TContext : DbContext
    {
        UsePortalEncryption((DbContextOptionsBuilder)builder, protector);
        return builder;
    }
}

/// <summary>
/// Model-cache key factory that adds the context's <see cref="PortalPiiProtector"/> identity to the
/// key, so two contexts sharing one EF internal service provider but configured with different
/// protectors compile and cache separate models (each with converters bound to its own protector)
/// rather than one silently reusing the other's.
/// </summary>
public sealed class PortalModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        // The protector is a reference type without value equality, so including the instance keys the
        // model by object identity (no hash-collision aliasing between distinct protectors).
        var protector = context.GetService<IDbContextOptions>()
            .FindExtension<PortalEncryptionOptionsExtension>()?.Protector;
        return (context.GetType(), designTime, protector);
    }
}

/// <summary>
/// Carries the context's <see cref="PortalPiiProtector"/> through the EF options pipeline as data only.
/// It deliberately does not vary the internal service provider (that would build a new provider per
/// protector and exhaust EF's provider cache); per-protector model isolation is handled by
/// <see cref="PortalModelCacheKeyFactory"/> instead.
/// </summary>
public sealed class PortalEncryptionOptionsExtension : IDbContextOptionsExtension
{
    public PortalEncryptionOptionsExtension(PortalPiiProtector protector)
    {
        Protector = protector;
        Info = new ExtensionInfo(this);
    }

    public PortalPiiProtector Protector { get; }

    public DbContextOptionsExtensionInfo Info { get; }

    public void ApplyServices(IServiceCollection services)
    {
        // No EF services to register; the protector is consumed in OnModelCreating and the cache key.
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(PortalEncryptionOptionsExtension extension)
        : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using PortalPiiEncryption ";

        // Constant: all encryption-enabled contexts share one internal service provider regardless of
        // which protector they carry. Protector isolation happens at the model-cache key, not here.
        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["Portal:PiiEncryption"] = "enabled";
    }
}
