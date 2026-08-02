using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using Xunit;

namespace ETL_SQL.Tests.Core;

public sealed class StewardshipScoringTests
{
    [Fact]
    public void ScoresAndGapsReconcileExactlyAndRetainSourceLocation()
    {
        var policy = new WorkspacePolicyDocument
        {
            RequiredTags =
            [
                new WorkspaceRequiredTagRule { Tag = "@owner", Scopes = ["TABLE"] },
                new WorkspaceRequiredTagRule { Tag = "@classification", Scopes = ["COLUMN"], Exclude = ["*.ignored"] }
            ],
            StewardshipWeights = new WorkspaceStewardshipWeights
            {
                RequiredTagCompleteness = 2m,
                ProtectedDataCoverage = 3m,
                QualityRuleCoverage = 4m
            }
        };
        var assets = new[]
        {
            Asset("orders", null, new Dictionary<string, string> { ["owner"] = "ops" }, 3),
            Asset("orders", "email", new Dictionary<string, string>
            {
                ["pii"] = "true", ["classification"] = "restricted", ["expect"] = "NOT NULL"
            }, 7),
            Asset("orders", "ignored", new Dictionary<string, string>(), 9)
        };

        var evaluation = StewardshipScoring.Evaluate(assets, policy,
            new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero));
        var global = evaluation.Scores.Where(s => s.ScopeType == "GLOBAL").ToList();

        Assert.Collection(global.OrderBy(s => s.Component),
            score => Assert.Equal("protected_data_coverage", score.Component),
            score => Assert.Equal("quality_rule_coverage", score.Component),
            score => Assert.Equal("required_tag_completeness", score.Component));
        Assert.All(global, score =>
            Assert.Equal(score.Denominator - score.Numerator,
                evaluation.Gaps.Count(g => g.ScopeType == score.ScopeType && g.ScopeName == score.ScopeName && g.Component == score.Component)));
        Assert.Equal(2m, global.Single(s => s.Component == "required_tag_completeness").Weight);
        Assert.Equal(3m, global.Single(s => s.Component == "protected_data_coverage").Weight);
        Assert.Equal(4m, global.Single(s => s.Component == "quality_rule_coverage").Weight);
        var ownerGap = Assert.Single(evaluation.Gaps,
            g => g.ScopeType == "GLOBAL" && g.Requirement.Contains("@owner"));
        Assert.Equal("pipelines/orders.etlsql", ownerGap.SourceFile);
        Assert.Equal(7, ownerGap.Line);
        Assert.Equal(StewardshipScoring.DefinitionVersion, ownerGap.DefinitionVersion);
    }

    [Fact]
    public void ComponentPercentagesAreTransparentAndNoCompositeIsInvented()
    {
        var evaluation = StewardshipScoring.Evaluate(
        [
            Asset("customers", "email", new Dictionary<string, string>
            {
                ["pii"] = "true", ["owner"] = "steward", ["classification"] = "restricted"
            }, 12)
        ]);

        var global = evaluation.Scores.Where(s => s.ScopeType == "GLOBAL").ToList();
        Assert.Equal(3, global.Count);
        Assert.DoesNotContain(global, s => s.Component.Contains("composite", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(100m, global.Single(s => s.Component == "protected_data_coverage").Percentage);
        Assert.Equal(0m, global.Single(s => s.Component == "quality_rule_coverage").Percentage);
        Assert.Equal(5, global.Single(s => s.Component == "required_tag_completeness").Denominator);
    }

    [Fact]
    public void NewestFirstAssetWinsDeterministicHistoryDeduplication()
    {
        var policy = new WorkspacePolicyDocument
        {
            RequiredTags = [new WorkspaceRequiredTagRule { Tag = "@owner", Scopes = ["COLUMN"] }]
        };
        var evaluation = StewardshipScoring.Evaluate(
        [
            Asset("customers", "email", new Dictionary<string, string> { ["owner"] = "new-owner" }, 20),
            Asset("customers", "email", new Dictionary<string, string>(), 10)
        ], policy);

        var score = evaluation.Scores.Single(s => s.ScopeType == "GLOBAL"
            && s.Component == "required_tag_completeness");
        Assert.Equal(1, score.Numerator);
        Assert.Equal(1, score.Denominator);
    }

    private static StewardshipAsset Asset(
        string table, string? column, IReadOnlyDictionary<string, string> tags, int line) =>
        new("nightly", table, column, tags, "pipelines/orders.etlsql", line);
}
