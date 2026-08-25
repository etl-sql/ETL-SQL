using ETL_SQL.Core.Portability;

namespace ETL_SQL.App.Portability;

/// <summary>
/// Distinct exit codes per failure kind, following the admin identity CLI precedent: a runbook must
/// be able to tell "this bundle is not authentic" from "this bundle needs bindings you have not
/// supplied", because the first is a stop and the second is a to-do list.
/// </summary>
public enum TenantPortabilityExitCode
{
    Ok = 0,
    BundleInvalid = 4,
    SignatureUnverified = 5,
    BindingsRequired = 6,
    NotFound = 7
}

public sealed record TenantPortabilityPreflight(
    TenantPortabilityExitCode ExitCode,
    TenantBundleManifest? Manifest,
    IReadOnlyList<TenantBundleFinding> Findings,
    IReadOnlyList<TenantBundleRequiredBinding> RequiredBindings,
    IReadOnlyList<TenantBundleExclusion> Exclusions)
{
    public bool CanProceed => ExitCode == TenantPortabilityExitCode.Ok;
}

/// <summary>
/// Non-mutating bundle inspection for the <c>admin tenant validate</c> and
/// <c>admin tenant preflight</c> verbs (tenant-portability.md §10, §16).
/// </summary>
/// <remarks>
/// Preflight answers a different question from validation. Validation asks "is this bundle intact and
/// authentic?"; preflight asks "can this target accept it, and what must the target supply first?".
/// A bundle can be perfectly valid and still not importable because the environment owes it bindings,
/// so the two get separate exit codes rather than one boolean.
/// </remarks>
public static class TenantPortabilityInspector
{
    public static async Task<TenantPortabilityPreflight> PreflightAsync(
        string bundleRoot,
        string? operatorPublicKeyFile = null,
        bool requireSignature = false,
        IReadOnlyCollection<string>? bindingsSuppliedByTarget = null,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(bundleRoot))
        {
            return new TenantPortabilityPreflight(
                TenantPortabilityExitCode.NotFound, null,
                [new TenantBundleFinding("bundle.root.missing", "Error", bundleRoot,
                    $"No bundle directory at '{bundleRoot}'.")],
                [], []);
        }

        var result = await TenantBundleValidator.ValidateAsync(bundleRoot,
            new TenantBundleValidator.Options(operatorPublicKeyFile, requireSignature), ct)
            .ConfigureAwait(false);

        if (!result.IsValid)
        {
            // Signature failures are called out separately: "someone altered this" and "this file is
            // corrupt" are different conversations with whoever sent the bundle.
            var signatureFailed = result.Findings.Any(f =>
                f.Code.StartsWith("bundle.signature", StringComparison.Ordinal));
            return new TenantPortabilityPreflight(
                signatureFailed
                    ? TenantPortabilityExitCode.SignatureUnverified
                    : TenantPortabilityExitCode.BundleInvalid,
                result.Manifest, result.Findings, [], []);
        }

        var manifest = result.Manifest!;
        var supplied = bindingsSuppliedByTarget is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(bindingsSuppliedByTarget, StringComparer.OrdinalIgnoreCase);
        var outstanding = manifest.RequiredBindings
            .Where(b => !supplied.Contains(b.LogicalId))
            .ToArray();

        return new TenantPortabilityPreflight(
            outstanding.Length == 0
                ? TenantPortabilityExitCode.Ok
                : TenantPortabilityExitCode.BindingsRequired,
            manifest,
            result.Findings,
            outstanding,
            manifest.Exclusions);
    }
}
