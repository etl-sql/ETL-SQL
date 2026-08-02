using ETL_SQL.App;
using ETL_SQL.Core.Governance;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public sealed class PiiSchemaScannerTests
{
    [Fact]
    public void BuildReport_UsesBuiltInAndWorkspaceRulesWithoutRowValues()
    {
        var policy = new WorkspacePolicyDocument
        {
            ProtectedDataPatterns =
            [
                new WorkspaceProtectedDataPattern
                {
                    Name = "customer identifier",
                    Regex = "customer_id$",
                    Classification = "confidential",
                    Scopes = ["COLUMN"]
                }
            ],
            RequiredTags =
            [
                new WorkspaceRequiredTagRule { Tag = "@owner", Scopes = ["COLUMN"] }
            ]
        };
        var schemas = new List<(string Source, string Table, IReadOnlyList<string> Columns, int Line)>
        {
            ("customers.csv", "customers.csv", ["email", "customer_id", "display_name"], 1)
        };

        var report = PiiSchemaScanner.BuildReport(schemas, policy, DateTimeOffset.UnixEpoch);

        Assert.Equal(PiiSchemaScanner.DefinitionVersion, report.DefinitionVersion);
        Assert.Contains(report.Findings, f => f.Column == "email" && f.SuggestedTag == "@pii");
        Assert.Contains(report.Findings, f => f.Column == "customer_id"
            && f.SuggestedTag == "@classification" && f.SuggestedValue == "confidential");
        Assert.Equal(3, report.Findings.Count(f => f.SuggestedTag == "@owner"));
        Assert.Equal(3, report.Scores.Count(s => s.ScopeType == "GLOBAL"));
        Assert.All(report.Scores, s => Assert.Equal("1.0", s.DefinitionVersion));
        Assert.All(report.Scores, score => Assert.Equal(
            score.Denominator - score.Numerator,
            report.Gaps.Count(g => g.ScopeType == score.ScopeType
                && g.ScopeName == score.ScopeName && g.Component == score.Component)));
        Assert.All(report.Findings, f => Assert.Equal(1, f.Line));
        Assert.DoesNotContain(report.Findings, f => f.Evidence.Contains("alice@example.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildReport_HonorsWorkspaceExclusions()
    {
        var policy = new WorkspacePolicyDocument
        {
            RequiredTags =
            [
                new WorkspaceRequiredTagRule
                {
                    Tag = "@steward", Scopes = ["COLUMN"], Exclude = ["*.technical_id"]
                }
            ]
        };
        var schemas = new List<(string Source, string Table, IReadOnlyList<string> Columns, int Line)>
        {
            ("schema.csv", "schema.csv", ["technical_id", "email"], 1)
        };

        var report = PiiSchemaScanner.BuildReport(schemas, policy);

        Assert.DoesNotContain(report.Findings, f => f.Column == "technical_id" && f.SuggestedTag == "@steward");
        Assert.Contains(report.Findings, f => f.Column == "email" && f.SuggestedTag == "@steward");
    }
}
