using System.Security.Cryptography;
using System.Text;

namespace ETL_SQL.Core.Portability;

public enum TenantInventoryDisposition { Included, Excluded, Skipped, Redacted, Failed }

/// <summary>One complete inventory row, including authority definitions needed for reconciliation.</summary>
public sealed record TenantInventoryItem(
    string StableId,
    string ResourceClass,
    TenantInventoryDisposition Disposition,
    long ByteLength,
    string? Sha256,
    string? Owner,
    IReadOnlyList<string> AclEntries,
    string? Reason,
    string? Remediation,
    string? SourceTenantId = null,
    string? ContainerLogicalId = null);

public sealed record TenantInventorySummary(
    IReadOnlyDictionary<string, int> Counts,
    long IncludedBytes,
    string Digest);

public sealed record TenantInventoryReconciliation(
    bool IsComplete,
    IReadOnlyList<string> Errors,
    TenantInventorySummary Summary);

/// <summary>Validates stable ids, hashes, ownership/ACL declarations, exclusions, and tenant scope.</summary>
public static class TenantPortabilityInventory
{
    public static TenantInventoryReconciliation Reconcile(
        string tenantId, IEnumerable<TenantInventoryItem> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(items);
        var ordered = items.OrderBy(x => x.StableId, StringComparer.Ordinal).ToArray();
        var errors = new List<string>();

        foreach (var duplicate in ordered.GroupBy(x => x.StableId, StringComparer.Ordinal).Where(x => x.Count() > 1))
            errors.Add($"Stable id '{duplicate.Key}' appears {duplicate.Count()} times.");

        foreach (var item in ordered)
        {
            if (item.SourceTenantId is not null && !string.Equals(item.SourceTenantId, tenantId, StringComparison.Ordinal))
                errors.Add($"'{item.StableId}' belongs to foreign tenant '{item.SourceTenantId}'.");
            if (item.Disposition == TenantInventoryDisposition.Included)
            {
                if (item.ByteLength < 0 || string.IsNullOrWhiteSpace(item.Sha256) || item.Sha256.Length != 64)
                    errors.Add($"Included item '{item.StableId}' lacks a valid byte count or SHA-256 hash.");
                if (string.IsNullOrWhiteSpace(item.Owner))
                    errors.Add($"Included item '{item.StableId}' has no ownership definition.");
            }
            else if (string.IsNullOrWhiteSpace(item.Reason))
                errors.Add($"Non-included item '{item.StableId}' has no explicit reason.");
        }

        var counts = ordered.GroupBy(x => x.Disposition.ToString(), StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        var canonical = string.Join("\n", ordered.Select(x => string.Join("|",
            x.StableId, x.ResourceClass, x.Disposition, x.ByteLength, x.Sha256 ?? "", x.Owner ?? "",
            string.Join(",", x.AclEntries.OrderBy(a => a, StringComparer.Ordinal)), x.Reason ?? "",
            x.Remediation ?? "", x.ContainerLogicalId ?? "")));
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        var summary = new TenantInventorySummary(counts,
            ordered.Where(x => x.Disposition == TenantInventoryDisposition.Included).Sum(x => x.ByteLength), digest);
        return new TenantInventoryReconciliation(errors.Count == 0, errors, summary);
    }
}
