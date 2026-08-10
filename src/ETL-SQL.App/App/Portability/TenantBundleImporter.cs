using System.Text;
using System.Text.Json;
using ETL_SQL.Core.Portability;
using ETL_SQL.Orchestrator.Storage;

namespace ETL_SQL.App.Portability;

/// <summary>One planned change at the target, in the Portal's existing detection vocabulary.</summary>
public sealed record PortalPlanEntry(string Kind, string Name, string Action);

/// <summary>
/// What happens when the target already has an object the bundle also carries.
/// </summary>
/// <remarks>
/// Only two values, and that is a consequence of how the Portal half is applied rather than an
/// oversight. The bundle's Portal payload is a declarative script executed by the engine as a whole,
/// so there is no seam at which one colliding object could be skipped or renamed while the rest of
/// the script proceeds. §5.2's <c>skip</c> and <c>rename</c> need per-object application — a
/// server-side import endpoint — and offering them here would be a flag that cannot keep its promise.
/// </remarks>
public enum TenantImportCollisionPolicy
{
    /// <summary>Refuse the import if the target already holds anything the bundle carries.</summary>
    Fail,

    /// <summary>Run the script anyway and let its own statement semantics decide.</summary>
    Proceed
}

/// <summary>Applies the Portal half of a bundle. Implemented by executing the script via the engine.</summary>
public interface IPortalConfigurationTarget
{
    Task<IReadOnlyList<PortalPlanEntry>> PlanAsync(
        string script, IReadOnlyDictionary<string, string> bindings, CancellationToken ct);

    Task ApplyAsync(
        string script, IReadOnlyDictionary<string, string> bindings, CancellationToken ct);
}

/// <summary>Applies the Orchestrator half. Wraps <c>OrchestratorPromotionPackageService.ImportAsync</c>.</summary>
public interface IOrchestratorPackageTarget
{
    Task<int> ImportAsync(
        OrchestratorPromotionPackageService.Package package, bool leaveDisabled, CancellationToken ct);
}

public sealed record TenantImportOptions(
    IReadOnlyDictionary<string, string> Bindings,
    string? OperatorPublicKeyFile = null,
    bool RequireSignature = false,
    string? RecipientPrivateKeyFile = null,
    string? RecipientPassphrase = null,
    TenantImportCollisionPolicy CollisionPolicy = TenantImportCollisionPolicy.Fail,
    bool DryRun = false);

public sealed record TenantImportResult(
    TenantPortabilityExitCode ExitCode,
    bool Applied,
    IReadOnlyList<PortalPlanEntry> Plan,
    IReadOnlyList<TenantBundleFinding> Findings,
    int OrchestratorObjects,
    string? RefusalReason = null);

/// <summary>
/// Imports a tenant bundle into a target deployment (TenantPortability.md §11).
/// </summary>
/// <remarks>
/// Two rules shape the whole flow. Nothing mutates until preflight passes, so a bundle that is
/// inauthentic, tampered with, or missing target bindings cannot half-apply. And everything that
/// executes work — jobs, schedules, subscriptions, service accounts — arrives disabled, because an
/// import that starts running the tenant's pipelines against a half-bound environment is worse than
/// one that requires a deliberate activation.
/// </remarks>
public static class TenantBundleImporter
{
    public static async Task<TenantImportResult> ImportAsync(
        string bundleRoot,
        TenantImportOptions options,
        IPortalConfigurationTarget portal,
        IOrchestratorPackageTarget? orchestrator = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(portal);

        var preflight = await TenantPortabilityInspector.PreflightAsync(
            bundleRoot, options.OperatorPublicKeyFile, options.RequireSignature,
            [.. options.Bindings.Keys], ct).ConfigureAwait(false);

        if (!preflight.CanProceed)
        {
            return new TenantImportResult(preflight.ExitCode, Applied: false, [], preflight.Findings, 0,
                preflight.ExitCode == TenantPortabilityExitCode.BindingsRequired
                    ? "The target has not supplied every binding the bundle requires: " +
                      string.Join(", ", preflight.RequiredBindings.Select(b => b.LogicalId))
                    : "The bundle did not pass validation; nothing was applied.");
        }

        var manifest = preflight.Manifest!;
        var script = Encoding.UTF8.GetString(await ReadComponentAsync(
            bundleRoot, manifest, "catalog:portal-configuration", options, ct).ConfigureAwait(false));

        var plan = await portal.PlanAsync(script, options.Bindings, ct).ConfigureAwait(false);
        var collisions = plan
            .Where(p => string.Equals(p.Action, "Collision", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (collisions.Length > 0 && options.CollisionPolicy == TenantImportCollisionPolicy.Fail)
        {
            return new TenantImportResult(TenantPortabilityExitCode.BundleInvalid, Applied: false,
                plan, preflight.Findings, 0,
                $"The target already holds {collisions.Length} object(s) this bundle carries: " +
                string.Join(", ", collisions.Select(c => $"{c.Kind} '{c.Name}'")) +
                ". Re-run with an explicit collision policy, or resolve them at the target first.");
        }

        if (options.DryRun)
        {
            return new TenantImportResult(TenantPortabilityExitCode.Ok, Applied: false, plan,
                preflight.Findings, 0, "Dry run: the plan above was computed and nothing was applied.");
        }

        await portal.ApplyAsync(script, options.Bindings, ct).ConfigureAwait(false);

        var orchestratorObjects = 0;
        var packageComponent = manifest.Components
            .FirstOrDefault(c => c.LogicalId == "catalog:orchestrator-promotion");
        if (packageComponent is not null && orchestrator is not null)
        {
            var json = Encoding.UTF8.GetString(await ReadComponentAsync(
                bundleRoot, manifest, packageComponent.LogicalId, options, ct).ConfigureAwait(false));
            var package = JsonSerializer.Deserialize<OrchestratorPromotionPackageService.Package>(json)
                ?? throw new TenantBundleCompositionException(
                    "The bundle's Orchestrator promotion payload did not deserialize.");

            // leaveDisabled is not configurable. An import that starts executing the tenant's
            // pipelines against a freshly bound environment is the failure mode this guards.
            orchestratorObjects = await orchestrator
                .ImportAsync(package, leaveDisabled: true, ct).ConfigureAwait(false);
        }

        return new TenantImportResult(TenantPortabilityExitCode.Ok, Applied: true, plan,
            preflight.Findings, orchestratorObjects);
    }

    /// <summary>
    /// Reads a component, decrypting when the bundle is encrypted and verifying the plaintext against
    /// the hash the manifest recorded before encryption. The stored-bytes hash was already checked by
    /// validation; this is the other half, and it is what catches a payload that decrypts to
    /// something other than what was exported.
    /// </summary>
    private static async Task<byte[]> ReadComponentAsync(
        string bundleRoot, TenantBundleManifest manifest, string logicalId,
        TenantImportOptions options, CancellationToken ct)
    {
        var component = manifest.Components.FirstOrDefault(c => c.LogicalId == logicalId)
            ?? throw new TenantBundleCompositionException(
                $"The bundle does not contain '{logicalId}'.");

        var stored = await File.ReadAllBytesAsync(
            Path.Combine(bundleRoot, component.Path), ct).ConfigureAwait(false);

        if (manifest.Encryption?.Encrypted != true) return stored;

        if (string.IsNullOrWhiteSpace(options.RecipientPrivateKeyFile))
        {
            throw new TenantBundleCompositionException(
                "This bundle is encrypted to a tenant recipient key, but no private key was supplied. " +
                "Only the tenant can decrypt it — by design.");
        }

        var plaintext = await TenantBundleCrypto.DecryptAsync(
            stored, options.RecipientPrivateKeyFile!, options.RecipientPassphrase, ct).ConfigureAwait(false);

        if (component.PlaintextSha256 is { } expected)
        {
            var actual = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(plaintext)).ToLowerInvariant();
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new TenantBundleCompositionException(
                    $"'{logicalId}' decrypted to content that does not match the plaintext hash " +
                    "recorded at export. Refusing to import it.");
            }
        }

        return plaintext;
    }
}
