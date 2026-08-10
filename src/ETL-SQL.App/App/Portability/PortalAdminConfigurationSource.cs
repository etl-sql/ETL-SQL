using System.Text.Json.Nodes;
using ETL_SQL.App.Admin;

namespace ETL_SQL.App.Portability;

/// <summary>
/// The production <see cref="IPortalConfigurationSource"/>: the Portal configuration export read over
/// HTTP through <see cref="PortalAdminClient"/>.
/// </summary>
/// <remarks>
/// HTTP is not an implementation preference here — <c>ETL-SQL.App</c> deliberately does not reference
/// the Portal project, so the CLI cannot touch <c>PortalDbContext</c> and an architecture test keeps
/// it that way. The same constraint produced <see cref="PortalAdminClient"/> for the identity verbs.
/// </remarks>
public sealed class PortalAdminConfigurationSource(
    PortalAdminClient client, string? orchestratorAlias = null) : IPortalConfigurationSource
{
    private const string BasePath = "/api/admin/configuration/export";

    public async Task<PortalConfigurationPlan> GetPlanAsync(CancellationToken ct)
    {
        var query = orchestratorAlias is null ? string.Empty : $"?orchestratorAlias={Uri.EscapeDataString(orchestratorAlias)}";
        var payload = await client.GetAsync($"{BasePath}/plan{query}", ct).ConfigureAwait(false)
            ?? throw new TenantBundleCompositionException(
                "The Portal returned an empty configuration export plan.");

        var planHash = payload["planHash"]?.GetValue<string>()
            ?? throw new TenantBundleCompositionException(
                "The Portal export plan carried no planHash, so the download cannot be tied to a " +
                "reviewed plan. Refusing rather than downloading an unacknowledged configuration.");

        return new PortalConfigurationPlan(
            planHash,
            Strings(payload["requiredSecrets"]),
            Strings(payload["skipped"]),
            ContentManifest(payload["contentManifest"]),
            payload["tenantExportIdentity"]?.GetValue<string>());
    }

    public Task<string> GetScriptAsync(string acknowledgedPlanHash, CancellationToken ct)
    {
        var query = $"?acknowledgedPlan={Uri.EscapeDataString(acknowledgedPlanHash)}";
        if (orchestratorAlias is not null)
            query += $"&orchestratorAlias={Uri.EscapeDataString(orchestratorAlias)}";

        // A stale acknowledgement returns 409, which PortalAdminClient raises as AdminCliException.
        // The composer turns that into a failed export rather than retrying without the hash.
        return client.GetTextAsync($"{BasePath}{query}", ct);
    }

    private static IReadOnlyList<string> Strings(JsonNode? node) =>
        node is JsonArray array
            ? [.. array.Select(item => item?.GetValue<string>()).Where(v => v is not null).Cast<string>()]
            : [];

    private static IReadOnlyList<PortalContentManifestItem> ContentManifest(JsonNode? node) =>
        node is JsonArray array
            ? [.. array.OfType<JsonObject>().Select(item => new PortalContentManifestItem(
                item["kind"]?.GetValue<string>() ?? "unknown",
                item["logical"]?.GetValue<string>() ?? "unknown",
                item["source"]?.GetValue<string>(),
                item["action"]?.GetValue<string>() ?? "unknown"))]
            : [];
}
