using System.Text;
using System.Text.Json;
using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Models;

namespace ETL_SQL.Portal.Services;

public sealed record ReportPublishingPolicyResult(bool Allowed, IReadOnlyList<string> Errors)
{
    public static ReportPublishingPolicyResult Success { get; } = new(true, []);
}

/// <summary>Enforces the active, signed organization metadata policy before report catalog mutation.</summary>
public sealed class ReportPublishingPolicyService(
    IPolicyAuthorityStore authority,
    IPolicyEnvelopeSigner signer,
    IConfiguration configuration)
{
    private static readonly JsonSerializerOptions EnvelopeJson = new() { PropertyNameCaseInsensitive = true };

    public async Task<ReportPublishingPolicyResult> ValidateAsync(
        IReadOnlyDictionary<string, string> reportMetadata,
        IReadOnlyList<ReportDependencyLineageDto> lineage,
        CancellationToken cancellationToken = default)
    {
        var tenant = configuration["Portal:PolicyAuthority:Tenant"] ?? "default";
        var environment = configuration["Portal:PolicyAuthority:Environment"]
            ?? Environment.GetEnvironmentVariable("ETLSQL_ENV")
            ?? "default";
        var active = await authority.GetActiveAsync(tenant, environment, cancellationToken);
        if (active is null) return ReportPublishingPolicyResult.Success;

        OrganizationPolicyDocument policy;
        try
        {
            var envelope = JsonSerializer.Deserialize<SignedOrganizationPolicyEnvelope>(active.SignedEnvelopeJson, EnvelopeJson)
                ?? throw new InvalidOperationException("Active policy envelope is empty.");
            if (!string.Equals(envelope.Tenant, tenant, StringComparison.Ordinal)
                || envelope.ExpiresAtUtc <= DateTimeOffset.UtcNow
                || !EnterprisePolicySignature.VerifiesWithKey(envelope, signer.PublicKeyPem))
                throw new InvalidOperationException("Active policy envelope is invalid, expired, or belongs to another tenant.");
            policy = OrganizationPolicySchema.ParseAndValidateJson(
                Encoding.UTF8.GetString(Convert.FromBase64String(envelope.PolicyPayload)));
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or PolicyAuthorityException)
        {
            return new(false, ["Organization metadata policy could not be verified; publishing is blocked."]);
        }

        var errors = new List<string>();
        foreach (var rule in policy.Metadata.RequiredTags)
        {
            var tag = rule.Tag.TrimStart('@');
            if (rule.Scopes.Contains("REPORT", StringComparer.OrdinalIgnoreCase)
                && !HasValue(reportMetadata, tag))
                errors.Add($"Organization policy requires {rule.Tag} on the report.");

            if (!rule.Scopes.Any(scope => scope.Equals("DATASET", StringComparison.OrdinalIgnoreCase)
                || scope.Equals("COLUMN", StringComparison.OrdinalIgnoreCase))) continue;

            var datasetColumns = lineage
                .Where(entry => entry.Target.StartsWith("dataset:", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (datasetColumns.Count == 0)
            {
                errors.Add($"Organization policy requires {rule.Tag} on underlying dataset metadata, but the report declares no verifiable dataset columns.");
                continue;
            }
            foreach (var entry in datasetColumns.Where(entry => !HasValue(entry.Tags, tag)))
                errors.Add($"Organization policy requires {rule.Tag} on dataset column '{entry.Target}.{entry.TargetColumn}'.");
        }

        return errors.Count == 0 ? ReportPublishingPolicyResult.Success : new(false, errors.Distinct().ToList());
    }

    private static bool HasValue(IReadOnlyDictionary<string, string> metadata, string tag) =>
        metadata.TryGetValue(tag, out var value) && !string.IsNullOrWhiteSpace(value);
}
