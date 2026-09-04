using ETL_SQL.Core.Data;
using Xunit;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// Collapsing lineage history down to one row per asset.
///
/// <para>These are written against the defect that produced the helper: a script that writes five
/// <c>INSERT TAG FOR TABLE</c> statements reached the steward's estate carrying one tag — the last
/// one executed — because every governance surface sampled the newest lineage row instead of
/// replaying the run. The author saw five tags in their script, the steward saw one, and nothing
/// reported a difference.</para>
/// </summary>
public sealed class LineageAssetCollapseTests
{
    private static readonly DateTime Run1 = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Run2 = new(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);

    private static LineageHistoryEntry Entry(
        long id,
        DateTime runAt,
        string table,
        string? column = null,
        string operation = "TABLE_TAGS",
        params (string Key, string Value)[] tags) =>
        new(id, runAt, "job", "script.etlsql", table, column, [], operation,
            tags.ToDictionary(t => t.Key, t => t.Value, StringComparer.OrdinalIgnoreCase),
            "script.etlsql", 1);

    /// <summary>The defect itself: five statements, five rows, and all five tags have to survive.</summary>
    [Fact]
    public void EveryTagStatementInARunReachesTheAsset()
    {
        var entries = new[]
        {
            Entry(1, Run1, "sales", operation: "SELECT_INTO"),
            Entry(2, Run1, "sales", tags: [("quality", "gold")]),
            Entry(3, Run1, "sales", tags: [("classification", "internal")]),
            Entry(4, Run1, "sales", tags: [("contact", "analytics@example.invalid")]),
            Entry(5, Run1, "sales", tags: [("steward", "analytics")]),
            Entry(6, Run1, "sales", tags: [("owner", "analytics")]),
        };

        var asset = Assert.Single(LineageAssetCollapse.LatestPerAsset(entries));

        Assert.Equal(
            new[] { "classification", "contact", "owner", "quality", "steward" },
            asset.Tags.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Two statements writing the same tag: the one that executed last decides.</summary>
    [Fact]
    public void TheLastStatementToWriteATagWins()
    {
        var entries = new[]
        {
            Entry(1, Run1, "sales", tags: [("owner", "first")]),
            Entry(2, Run1, "sales", tags: [("owner", "second")]),
        };

        var asset = Assert.Single(LineageAssetCollapse.LatestPerAsset(entries));

        Assert.Equal("second", asset.Tags["owner"]);
    }

    /// <summary>
    /// A tag an earlier run wrote and this run does not is gone. Merging across runs would show the
    /// steward a tag no statement in the current script explains.
    /// </summary>
    [Fact]
    public void AnEarlierRunsTagsDoNotSurviveIntoALaterRun()
    {
        var entries = new[]
        {
            Entry(1, Run1, "sales", tags: [("owner", "analytics")], operation: "TABLE_TAGS"),
            Entry(2, Run2, "sales", tags: [("steward", "analytics")]),
        };

        var asset = Assert.Single(LineageAssetCollapse.LatestPerAsset(entries));

        Assert.Equal(new[] { "steward" }, asset.Tags.Keys.ToArray());
        Assert.Equal(Run2, asset.RunAt);
    }

    /// <summary>A delete inside the run removes the tag rather than being merged in as a value.</summary>
    [Fact]
    public void ADeleteInTheSameRunRemovesTheTag()
    {
        var entries = new[]
        {
            Entry(1, Run1, "sales", tags: [("owner", "analytics"), ("steward", "analytics")]),
            Entry(2, Run1, "sales", operation: "TABLE_TAG_DELETE", tags: [("owner", "")]),
        };

        var asset = Assert.Single(LineageAssetCollapse.LatestPerAsset(entries));

        Assert.Equal(new[] { "steward" }, asset.Tags.Keys.ToArray());
    }

    /// <summary>
    /// Everything other than the tag set still comes from the newest row, because that is the run
    /// whose script path, sources and timestamp describe the asset as it stands.
    /// </summary>
    [Fact]
    public void TheAssetIsOtherwiseTheNewestRow()
    {
        var entries = new[]
        {
            Entry(7, Run1, "sales", operation: "SELECT_INTO"),
            Entry(9, Run2, "sales", operation: "TABLE_TAGS", tags: [("owner", "analytics")]),
            Entry(8, Run2, "sales", operation: "SELECT_INTO"),
        };

        var asset = Assert.Single(LineageAssetCollapse.LatestPerAsset(entries));

        Assert.Equal(9L, asset.Id);
        Assert.Equal("TABLE_TAGS", asset.Operation);
    }

    /// <summary>
    /// A column asset and a table asset whose names concatenate to the same string are two assets.
    /// The key that produced this helper's call sites joined them with nothing at all.
    /// </summary>
    [Fact]
    public void TableAndColumnAreNotConcatenatedIntoOneKey()
    {
        var entries = new[]
        {
            Entry(1, Run1, "ab", "c", tags: [("owner", "left")]),
            Entry(2, Run1, "a", "bc", tags: [("owner", "right")]),
        };

        var assets = LineageAssetCollapse.LatestPerAsset(entries).ToList();

        Assert.Equal(2, assets.Count);
    }
}
