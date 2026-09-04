using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.Core.Data;

/// <summary>
/// Collapses lineage history — one row per statement — into one row per asset, which is the shape
/// every governance surface reads.
///
/// <para><b>Why this exists rather than a <c>GroupBy(...).First()</c> at each call site.</b> Tags are
/// written one statement at a time: five <c>INSERT TAG FOR TABLE</c> statements produce five rows,
/// each carrying only the tags that statement set. Taking the newest row therefore reports the last
/// statement's tags as if they were the asset's whole tag set, and the other four vanish — the author
/// sees five tags in their script and the steward sees one, with nothing reported. Collapsing has to
/// replay the run, not sample it.</para>
///
/// <para><b>Scoped to the asset's most recent run.</b> Tags are the state a run left behind, so a tag
/// an earlier run wrote and this one no longer writes is gone. Merging across runs would resurrect
/// it, and no statement in the current script would explain where it came from.</para>
/// </summary>
public static class LineageAssetCollapse
{
    /// <summary>The lineage operation that removes tags rather than setting them.</summary>
    private const string TagDeleteOperation = "TABLE_TAG_DELETE";

    /// <summary>
    /// Separates the table from the column in a grouping key. A character no identifier can contain,
    /// because plain concatenation reads <c>a</c>.<c>bc</c> and <c>ab</c>.<c>c</c> as one asset.
    /// </summary>
    private const char KeySeparator = '\u001f';

    /// <summary>The grouping identity of an asset.</summary>
    public static string GroupingKey(LineageHistoryEntry entry) =>
        $"{entry.TargetTable}{KeySeparator}{entry.TargetColumn ?? string.Empty}";

    /// <summary>
    /// One entry per asset — the newest, with <see cref="LineageHistoryEntry.Tags"/> replaced by the
    /// tag set that asset's most recent run actually ended with.
    /// </summary>
    public static IEnumerable<LineageHistoryEntry> LatestPerAsset(IEnumerable<LineageHistoryEntry> entries) =>
        entries
            .GroupBy(GroupingKey, StringComparer.OrdinalIgnoreCase)
            .Select(Collapse);

    private static LineageHistoryEntry Collapse(IEnumerable<LineageHistoryEntry> assetEntries)
    {
        var ordered = assetEntries.OrderBy(e => e.RunAt).ThenBy(e => e.Id).ToList();
        var newest = ordered[^1];

        var lastRunAt = newest.RunAt;
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ordered.Where(e => e.RunAt == lastRunAt))
        {
            if (entry.Operation.Equals(TagDeleteOperation, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var key in entry.Tags.Keys) tags.Remove(key);
                continue;
            }

            foreach (var kv in entry.Tags) tags[kv.Key] = kv.Value;
        }

        return newest with { Tags = tags };
    }
}
