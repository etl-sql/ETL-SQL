using ETL_SQL.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Models;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Turns a lineage entry into an asset's governance posture: which required tags are missing,
/// whether it carries protected data, and whether it has gone stale.
///
/// <para>This lives outside <c>CatalogController</c> because the governance scan needs the same
/// answer the stewardship view shows. Two copies of "is this asset missing metadata?" would let the
/// dashboard's queue and its findings disagree about the same asset, and a steward has no way to
/// tell which one is wrong. One definition, both callers.</para>
/// </summary>
public static class StewardshipProjection
{
    public static string NormalizeView(string? view)
    {
        view = view?.Trim().ToLowerInvariant();
        return view is "sensitive" or "missing" or "stale" or "queue" ? view : "all";
    }

    /// <summary>The stable identity a finding, badge, or review attaches to.</summary>
    public static string AssetKey(string targetTable, string? targetColumn) =>
        string.IsNullOrWhiteSpace(targetColumn) ? targetTable : $"{targetTable}.{targetColumn}";

    public static string AssetKey(StewardshipAssetDto asset) =>
        AssetKey(asset.TargetTable, asset.TargetColumn);

    /// <summary>
    /// The version a decision is scoped to. Script path plus last run is the strongest identity
    /// available from lineage today; when it changes, prior decisions stop applying rather than
    /// silently carrying forward onto content nobody reviewed.
    /// </summary>
    public static string AssetVersion(StewardshipAssetDto asset) =>
        $"{asset.ScriptPath ?? "(unknown)"}@{asset.RunAt.ToUniversalTime():O}";

    public static StewardshipAssetDto ToAsset(LineageHistoryEntry entry, int staleAfterDays)
    {
        var tags = new Dictionary<string, string>(entry.Tags, StringComparer.OrdinalIgnoreCase);
        var missing = StewardshipTagCatalog.RequiredStewardshipTags
            .Where(tag => !tags.ContainsKey(tag) || string.IsNullOrWhiteSpace(tags[tag]))
            .ToList();

        var isRestricted = HasTagValue(tags, "classification", "restricted");
        var isSensitive = LineageProtectedData.IsProtected(tags);

        var (isStale, staleReason) = GetStaleState(entry.RunAt, tags, staleAfterDays);

        return new StewardshipAssetDto(
            entry.TargetTable,
            entry.TargetColumn,
            entry.RunAt,
            entry.JobName,
            entry.ScriptPath,
            entry.SourceTables,
            tags,
            missing,
            isSensitive,
            isRestricted,
            isStale,
            staleReason,
            GetTag(tags, "owner"),
            GetTag(tags, "steward"),
            GetTag(tags, "contact"),
            GetTag(tags, "domain"),
            GetTag(tags, "classification"),
            GetTag(tags, "quality"),
            GetTag(tags, "freshness"));
    }

    public static bool MatchesQuery(StewardshipAssetDto item, string query)
    {
        static bool Contains(string? value, string query) =>
            value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

        return Contains(item.TargetTable, query)
            || Contains(item.TargetColumn, query)
            || Contains(item.JobName, query)
            || Contains(item.ScriptPath, query)
            || item.SourceTables.Any(s => Contains(s, query))
            || item.Tags.Any(t => Contains(t.Key, query) || Contains(t.Value, query));
    }

    private static (bool IsStale, string Reason) GetStaleState(
        DateTime runAt, IReadOnlyDictionary<string, string> tags, int staleAfterDays)
    {
        var now = DateTime.UtcNow;
        var freshness = GetTag(tags, "freshness");
        if (!string.IsNullOrWhiteSpace(freshness) && TryParseFreshness(freshness, out var freshnessWindow))
        {
            var staleByFreshness = runAt.ToUniversalTime().Add(freshnessWindow) < now;
            return (staleByFreshness, staleByFreshness ? $"Freshness window {freshness} expired" : "Fresh");
        }

        var staleByDefault = runAt.ToUniversalTime().AddDays(staleAfterDays) < now;
        return (staleByDefault, staleByDefault ? $"No lineage in {staleAfterDays} days" : "Fresh");
    }

    private static bool TryParseFreshness(string value, out TimeSpan span)
    {
        span = TimeSpan.Zero;
        value = value.Trim();
        if (value.Length < 2 || !double.TryParse(value[..^1], out var amount)) return false;
        span = char.ToLowerInvariant(value[^1]) switch
        {
            's' => TimeSpan.FromSeconds(amount),
            'm' => TimeSpan.FromMinutes(amount),
            'h' => TimeSpan.FromHours(amount),
            'd' => TimeSpan.FromDays(amount),
            _ => TimeSpan.Zero
        };
        return span > TimeSpan.Zero;
    }

    private static bool HasTagValue(IReadOnlyDictionary<string, string> tags, string key, string expected) =>
        tags.TryGetValue(key, out var value) && value.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static string? GetTag(IReadOnlyDictionary<string, string> tags, string key) =>
        tags.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}
