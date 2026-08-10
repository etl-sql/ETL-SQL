using System.Security.Cryptography;

namespace ETL_SQL.Core.Security;

public sealed record KeyMaterialContractValidation(
    bool IsValid,
    IReadOnlyList<KeyMaterialDescriptor> Descriptors,
    IReadOnlyList<string> Errors);

/// <summary>
/// Validates the complete at-rest key contract without retaining resolved bytes. A provider is not
/// ready for Enterprise use unless every purpose resolves and no two authority domains reuse key
/// material, even when their safe key ids or version labels differ.
/// </summary>
public static class KeyMaterialContractValidator
{
    private static readonly KeyPurpose[] RequiredPurposes = Enum.GetValues<KeyPurpose>();

    public static async Task<KeyMaterialContractValidation> ValidateAsync(
        IKeyMaterialProvider provider,
        string serverDerivedScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var descriptors = new List<KeyMaterialDescriptor>();
        var errors = new List<string>();
        var fingerprints = new Dictionary<string, KeyMaterialDescriptor>(StringComparer.Ordinal);

        foreach (var purpose in RequiredPurposes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var lease = await provider.ResolveAsync(
                    new KeyMaterialRequest(serverDerivedScope, purpose), cancellationToken);
                descriptors.Add(lease.Descriptor);
                var fingerprint = Convert.ToHexString(SHA256.HashData(lease.Bytes.Span));
                if (fingerprints.TryGetValue(fingerprint, out var reused))
                {
                    errors.Add(
                        $"Key material is reused by '{reused.Purpose}' and '{purpose}' in scope " +
                        $"'{serverDerivedScope}'. Each purpose requires an independent key binding.");
                }
                else
                {
                    fingerprints.Add(fingerprint, lease.Descriptor);
                }
            }
            catch (Exception ex) when (ex is KeyNotFoundException or ArgumentException
                                       or InvalidOperationException)
            {
                errors.Add($"{purpose}: {ex.Message}");
            }
        }

        return new KeyMaterialContractValidation(errors.Count == 0, descriptors, errors);
    }

    /// <summary>
    /// Provisioning-time validation across tenant namespaces. Besides validating every purpose in
    /// each scope, it rejects identical resolved material in different tenants—even when key ids,
    /// versions, or provider namespaces differ.
    /// </summary>
    public static async Task<KeyMaterialContractValidation> ValidateTenantNamespacesAsync(
        IKeyMaterialProvider provider,
        IEnumerable<string> serverDerivedScopes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(serverDerivedScopes);
        var scopes = serverDerivedScopes
            .Select(scope => new KeyMaterialRequest(scope, KeyPurpose.Dataset).Normalize().Scope)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var descriptors = new List<KeyMaterialDescriptor>();
        var errors = new List<string>();
        var fingerprints = new Dictionary<string, KeyMaterialDescriptor>(StringComparer.Ordinal);

        foreach (var scope in scopes)
        {
            foreach (var purpose in RequiredPurposes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var lease = await provider.ResolveAsync(
                        new KeyMaterialRequest(scope, purpose), cancellationToken);
                    descriptors.Add(lease.Descriptor);
                    var fingerprint = Convert.ToHexString(SHA256.HashData(lease.Bytes.Span));
                    if (fingerprints.TryGetValue(fingerprint, out var reused))
                    {
                        errors.Add(
                            $"Key material for '{scope}/{purpose}' is reused by " +
                            $"'{reused.Scope}/{reused.Purpose}'. Tenant and purpose namespaces " +
                            "must be cryptographically disjoint.");
                    }
                    else
                    {
                        fingerprints.Add(fingerprint, lease.Descriptor);
                    }
                }
                catch (Exception ex) when (ex is KeyNotFoundException or ArgumentException
                                           or InvalidOperationException)
                {
                    errors.Add($"{scope}/{purpose}: {ex.Message}");
                }
            }
        }

        return new KeyMaterialContractValidation(errors.Count == 0, descriptors, errors);
    }
}
