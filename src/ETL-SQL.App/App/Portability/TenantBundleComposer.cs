using System.Text;
using System.Text.Json;
using ETL_SQL.Core.Portability;
using ETL_SQL.Orchestrator.Storage;

namespace ETL_SQL.App.Portability;

/// <summary>One item the Portal reports as not travelling inside the bootstrap script.</summary>
public sealed record PortalContentManifestItem(string Kind, string Logical, string? Source, string Action);

/// <summary>
/// The reviewed export plan from <c>admin/configuration/export/plan</c>. The skipped list and content
/// manifest matter more than the script: they are the parts that silently do not arrive at the target.
/// </summary>
public sealed record PortalConfigurationPlan(
    string PlanHash,
    IReadOnlyList<string> RequiredSecrets,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<PortalContentManifestItem> ContentManifest,
    string? TenantExportIdentity = null);

/// <summary>
/// The Portal side of composition, behind an interface for two reasons: <c>ETL-SQL.App</c> does not
/// reference the Portal project, so this can only ever be an HTTP call; and composition logic must be
/// testable without standing up a Portal.
/// </summary>
public interface IPortalConfigurationSource
{
    Task<PortalConfigurationPlan> GetPlanAsync(CancellationToken ct);

    /// <summary>
    /// Downloads the bootstrap script, acknowledging the reviewed plan. Implementations must surface
    /// the Portal's stale-plan refusal rather than retrying without the acknowledgement.
    /// </summary>
    Task<string> GetScriptAsync(string acknowledgedPlanHash, CancellationToken ct);
}

/// <summary>Raised when the source configuration moved while the bundle was being assembled.</summary>
public sealed class TenantBundleCompositionException(string message) : Exception(message);

public sealed record TenantBundleCompositionRequest(
    string BundleRoot,
    string BundleId,
    DateTimeOffset CreatedUtc,
    string SourceProductVersion,
    string SourceProfile,
    string TenantExportIdentity,
    string ConsistencyPoint,
    IReadOnlyList<string> ArtifactFiles,
    string? ArtifactRoot = null,
    OrchestratorPromotionPackageService.Package? OrchestratorPackage = null,
    string? RecipientPublicKeyFile = null,
    string? SigningPrivateKeyFile = null,
    string? SigningPassphrase = null);

/// <summary>
/// Assembles the unified tenant bundle from the exports that already exist — the Portal configuration
/// export, the Orchestrator promotion package, and portable source artifacts — per
/// <c>docs/architecture/TenantPortability.md</c> §5. It adds no fourth export format; it is the thing
/// that stops those three being correlated by hand.
/// </summary>
public static class TenantBundleComposer
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static async Task<TenantBundleManifest> ComposeAsync(
        IPortalConfigurationSource portal,
        TenantBundleCompositionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(portal);
        ArgumentNullException.ThrowIfNull(request);

        // Plan first, then download acknowledging it. The Portal refuses a stale acknowledgement, so
        // a configuration change mid-export becomes a failed export rather than a bundle whose
        // contents differ from the plan someone reviewed.
        var plan = await portal.GetPlanAsync(ct).ConfigureAwait(false);
        var tenantExportIdentity = request.TenantExportIdentity;
        if (string.Equals(request.SourceProfile,
                TenantBundle.EncryptionRequiredSourceProfile, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(plan.TenantExportIdentity))
                throw new TenantBundleCompositionException(
                    "A SaaS source Portal did not report its server-owned tenant identity. " +
                    "Refusing an export whose tenant would come only from caller input.");
            var context = ETL_SQL.Core.Multitenancy.TenantContext
                .FromHostConfiguration(plan.TenantExportIdentity);
            context.RequireTenant(request.TenantExportIdentity);
            tenantExportIdentity = context.Tenant.Value;
        }
        string script;
        try
        {
            script = await portal.GetScriptAsync(plan.PlanHash, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not TenantBundleCompositionException)
        {
            throw new TenantBundleCompositionException(
                "The Portal refused the configuration download for the reviewed plan " +
                $"({plan.PlanHash}). The configuration changed while the bundle was being assembled; " +
                $"re-run the export so the bundle matches a plan that was actually reviewed. {ex.Message}");
        }

        var payloads = new List<TenantBundlePayload>
        {
            new("catalog:portal-configuration", "catalog", "application/x-etlsql",
                "catalog/portal-configuration.etlsql", script),
            // The plan travels beside the script because the script alone does not say what was left
            // out of it.
            new("catalog:portal-configuration-plan", "catalog", "application/json",
                "catalog/portal-configuration-plan.json", JsonSerializer.Serialize(plan, Json))
        };

        var bindings = new List<TenantBundleRequiredBinding>();
        var exclusions = new List<TenantBundleExclusion>();

        foreach (var secret in plan.RequiredSecrets)
        {
            bindings.Add(new TenantBundleRequiredBinding($"SECRET:{secret}", "secret",
                "The target must provision this secret; the export carries the reference only."));
        }

        foreach (var skipped in plan.Skipped)
        {
            exclusions.Add(new TenantBundleExclusion(skipped, "portal-configuration",
                "The Portal configuration export cannot represent this resource.",
                "Recreate it at the target, or confirm it is not needed there."));
        }

        foreach (var item in plan.ContentManifest)
        {
            exclusions.Add(new TenantBundleExclusion(item.Logical, item.Kind,
                $"Content is not carried inside the bootstrap script (action: {item.Action}).",
                item.Source is null
                    ? "Transfer this content separately."
                    : $"Transfer this content separately from '{item.Source}'."));
        }

        if (request.OrchestratorPackage is { } package)
        {
            payloads.Add(new TenantBundlePayload("catalog:orchestrator-promotion", "catalog",
                "application/json", "catalog/orchestrator-promotion.json",
                JsonSerializer.Serialize(package, Json)));

            foreach (var secret in package.RequiredSecretReferences)
            {
                var logicalId = $"SECRET:{secret}";
                if (!bindings.Any(b => string.Equals(b.LogicalId, logicalId, StringComparison.OrdinalIgnoreCase)))
                {
                    bindings.Add(new TenantBundleRequiredBinding(logicalId, "secret",
                        "The target must provision this secret; the export carries the reference only."));
                }
            }
        }

        foreach (var file in request.ArtifactFiles)
        {
            if (!File.Exists(file))
            {
                throw new TenantBundleCompositionException(
                    $"Portable artifact '{file}' does not exist. Refusing to write a bundle that " +
                    "silently omits a script the tenant expects to take with them.");
            }

            var relative = request.ArtifactRoot is null
                ? Path.GetFileName(file)
                : Path.GetRelativePath(request.ArtifactRoot, file);
            payloads.Add(new TenantBundlePayload(
                $"artifact:{relative.Replace('\\', '/')}",
                "artifact",
                "text/plain",
                $"artifacts/{relative.Replace('\\', '/')}",
                await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false),
                []));
        }

        return await TenantBundleWriter.WriteAsync(request.BundleRoot, new TenantBundleRequest(
            request.BundleId,
            request.CreatedUtc,
            request.SourceProductVersion,
            request.SourceProfile,
            tenantExportIdentity,
            TenantBundleExportMode.ConfigurationAndArtifacts,
            request.ConsistencyPoint,
            payloads,
            bindings,
            exclusions,
            request.RecipientPublicKeyFile,
            request.SigningPrivateKeyFile,
            request.SigningPassphrase), ct).ConfigureAwait(false);
    }
}
