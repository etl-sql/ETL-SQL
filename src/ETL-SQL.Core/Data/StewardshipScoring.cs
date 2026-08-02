using System.Text.RegularExpressions;
using ETL_SQL.Common;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Core.Data;

public sealed record StewardshipAsset(
    string? JobName,
    string TargetTable,
    string? TargetColumn,
    IReadOnlyDictionary<string, string> Tags,
    string? SourceFile,
    int Line);

public sealed record StewardshipScore(
    string ScopeType,
    string ScopeName,
    string Component,
    int Numerator,
    int Denominator,
    decimal Percentage,
    int AssetCount,
    int ColumnCount,
    decimal Weight,
    DateTimeOffset EvaluatedAtUtc,
    string DefinitionVersion);

public sealed record StewardshipGap(
    string ScopeType,
    string ScopeName,
    string Component,
    string TargetTable,
    string? TargetColumn,
    string Requirement,
    string? SourceFile,
    int Line,
    DateTimeOffset EvaluatedAtUtc,
    string DefinitionVersion);

public sealed record StewardshipEvaluation(
    IReadOnlyList<StewardshipScore> Scores,
    IReadOnlyList<StewardshipGap> Gaps);

/// <summary>
/// One deterministic stewardship calculation shared by CLI, Engine catalogs, Orchestrator HTTP,
/// and Portal consumers. It reports component numerators/denominators; it deliberately does not
/// manufacture a composite badge from the optional component weights.
/// </summary>
public static class StewardshipScoring
{
    public const string DefinitionVersion = "1.0";
    private static readonly IReadOnlyList<WorkspaceRequiredTagRule> DefaultRequiredTags =
        StewardshipTagCatalog.RequiredStewardshipTags.Select(tag => new WorkspaceRequiredTagRule
        {
            Tag = "@" + tag,
            Scopes = ["TABLE", "COLUMN"]
        }).ToList();

    public static StewardshipEvaluation Evaluate(
        IEnumerable<StewardshipAsset> source,
        WorkspacePolicyDocument? policy = null,
        DateTimeOffset? evaluatedAtUtc = null)
    {
        var now = evaluatedAtUtc ?? DateTimeOffset.UtcNow;
        var assets = source
            .Where(a => !string.IsNullOrWhiteSpace(a.TargetTable))
            .GroupBy(a => $"{a.JobName}\u001f{a.TargetTable}\u001f{a.TargetColumn}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        var groups = new List<(string Type, string Name, List<StewardshipAsset> Assets)>
        {
            ("GLOBAL", "*", assets)
        };
        groups.AddRange(assets.Where(a => !string.IsNullOrWhiteSpace(a.JobName))
            .GroupBy(a => a.JobName!, StringComparer.OrdinalIgnoreCase)
            .Select(g => ("JOB", g.Key, g.ToList())));
        groups.AddRange(assets.GroupBy(a => a.TargetTable, StringComparer.OrdinalIgnoreCase)
            .Select(g => ("TABLE", g.Key, g.ToList())));

        var scores = new List<StewardshipScore>();
        var gaps = new List<StewardshipGap>();
        foreach (var group in groups)
            EvaluateGroup(group.Type, group.Name, group.Assets, policy, now, scores, gaps);
        return new(scores, gaps);
    }

    public static IReadOnlyList<StewardshipAsset> FromHistory(IEnumerable<LineageHistoryEntry> entries) =>
        entries.Select(e => new StewardshipAsset(
            e.JobName, e.TargetTable, e.TargetColumn, e.Tags, e.SourceFile ?? e.ScriptPath, e.Line)).ToList();

    public static IReadOnlyList<StewardshipAsset> FromCurrent(
        IEnumerable<LineageEntry> entries,
        string? jobName = null,
        string? currentScriptPath = null) =>
        entries.Select(e => new StewardshipAsset(
            jobName, e.TargetTable, e.TargetColumn, e.Metadata, e.SourceFile ?? currentScriptPath, e.Line)).ToList();

    private static void EvaluateGroup(
        string scopeType,
        string scopeName,
        List<StewardshipAsset> assets,
        WorkspacePolicyDocument? policy,
        DateTimeOffset now,
        List<StewardshipScore> scores,
        List<StewardshipGap> gaps)
    {
        var assetCount = assets.Select(a => a.TargetTable).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var columnCount = assets.Where(a => a.TargetColumn != null)
            .Select(a => $"{a.TargetTable}.{a.TargetColumn}").Distinct(StringComparer.OrdinalIgnoreCase).Count();

        var requiredNumerator = 0;
        var requiredDenominator = 0;
        // A checked-in policy is authoritative, including an intentionally empty requiredTags list.
        // Without one, retain the product's documented standard stewardship requirements.
        var requiredRules = policy?.RequiredTags ?? DefaultRequiredTags;
        foreach (var asset in assets)
        {
            var assetScope = asset.TargetColumn == null ? "TABLE" : "COLUMN";
            var assetName = asset.TargetColumn == null ? asset.TargetTable : $"{asset.TargetTable}.{asset.TargetColumn}";
            foreach (var rule in requiredRules.Where(r => r.Scopes.Contains(assetScope, StringComparer.OrdinalIgnoreCase)))
            {
                if (rule.Exclude.Any(pattern => WildcardMatches(assetName, pattern))) continue;
                requiredDenominator++;
                var tag = rule.Tag.TrimStart('@');
                if (HasNonEmptyTag(asset.Tags, tag)) requiredNumerator++;
                else AddGap("required_tag_completeness", asset, rule.Tag);
            }
        }

        var protectedNumerator = 0;
        var protectedDenominator = 0;
        foreach (var asset in assets.Where(a => LineageProtectedData.IsProtected(a.Tags)))
        {
            protectedDenominator += 2;
            if (HasAnyTag(asset.Tags, "owner", "steward", "contact")) protectedNumerator++;
            else AddGap("protected_data_coverage", asset, "@owner|@steward|@contact");
            if (HasNonEmptyTag(asset.Tags, "classification")) protectedNumerator++;
            else AddGap("protected_data_coverage", asset, "@classification");
        }

        var qualityAssets = assets.Where(a => a.TargetColumn != null).ToList();
        var qualityDenominator = qualityAssets.Count;
        var qualityNumerator = 0;
        foreach (var asset in qualityAssets)
        {
            if (asset.Tags.Keys.Any(k => k.Equals("expect", StringComparison.OrdinalIgnoreCase)
                || k.StartsWith("expect_", StringComparison.OrdinalIgnoreCase))) qualityNumerator++;
            else AddGap("quality_rule_coverage", asset, "@expect");
        }

        AddScore("required_tag_completeness", requiredNumerator, requiredDenominator,
            policy?.StewardshipWeights.RequiredTagCompleteness ?? 1m);
        AddScore("protected_data_coverage", protectedNumerator, protectedDenominator,
            policy?.StewardshipWeights.ProtectedDataCoverage ?? 1m);
        AddScore("quality_rule_coverage", qualityNumerator, qualityDenominator,
            policy?.StewardshipWeights.QualityRuleCoverage ?? 1m);
        return;

        void AddScore(string component, int numerator, int denominator, decimal weight) => scores.Add(new(
            scopeType, scopeName, component, numerator, denominator,
            denominator == 0 ? 100m : decimal.Round(numerator * 100m / denominator, 2),
            assetCount, columnCount, weight, now, DefinitionVersion));

        void AddGap(string component, StewardshipAsset asset, string requirement) => gaps.Add(new(
            scopeType, scopeName, component, asset.TargetTable, asset.TargetColumn, requirement,
            asset.SourceFile, asset.Line, now, DefinitionVersion));
    }

    private static bool HasNonEmptyTag(IReadOnlyDictionary<string, string> tags, string key) =>
        tags.Any(t => t.Key.TrimStart('@').Equals(key, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(t.Value));

    private static bool HasAnyTag(IReadOnlyDictionary<string, string> tags, params string[] keys) =>
        keys.Any(key => HasNonEmptyTag(tags, key));

    private static bool WildcardMatches(string value, string pattern)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(250));
    }
}
