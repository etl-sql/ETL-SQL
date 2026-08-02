using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Tests.Core;
using Xunit;

namespace ETL_SQL.Tests.Analysis;

public sealed class WorkspacePolicyRequiredTagsRuleTests
{
    [Fact]
    public async Task MissingOwnerAndStewardAreErrorsForLocalAutomation()
    {
        using var workspace = new PolicyWorkspace();
        var scriptPath = workspace.WriteScript("SELECT Email INTO #out FROM #source;");

        var results = await new WorkspacePolicyRequiredTagsRule().AnalyzeAsync(
            TestHelpers.Parse(await File.ReadAllTextAsync(scriptPath)),
            new DefaultLintContext { DocumentUri = scriptPath });

        Assert.Equal(2, results.Count());
        Assert.All(results, result =>
        {
            Assert.Equal(LintSeverity.Error, result.Severity);
            Assert.Equal(WorkspacePolicyRequiredTagsRule.MissingRequiredTagCode, result.Code);
        });
        Assert.Contains(results, result => result.Message.Contains("@owner", StringComparison.Ordinal));
        Assert.Contains(results, result => result.Message.Contains("@steward", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PresentTagsAndExplicitExclusionPass()
    {
        using var workspace = new PolicyWorkspace(exclude: ["#out.Ignored"]);
        var scriptPath = workspace.WriteScript("""
            SELECT
              Email /* @owner: 'sales'; @steward: 'data-office' */,
              Ignored
            INTO #out FROM #source;
            """);

        var results = await new WorkspacePolicyRequiredTagsRule().AnalyzeAsync(
            TestHelpers.Parse(await File.ReadAllTextAsync(scriptPath)),
            new DefaultLintContext { DocumentUri = new Uri(scriptPath).AbsoluteUri });

        Assert.Empty(results);
    }

    private sealed class PolicyWorkspace : IDisposable
    {
        private readonly string _root = Path.Combine(AppContext.BaseDirectory, "PolicyWorkspaces", Guid.NewGuid().ToString("N"));

        public PolicyWorkspace(IReadOnlyList<string>? exclude = null)
        {
            Directory.CreateDirectory(_root);
            var exclusions = exclude is { Count: > 0 }
                ? ", \"exclude\": [" + string.Join(", ", exclude.Select(JsonString)) + "]"
                : string.Empty;
            File.WriteAllText(Path.Combine(_root, "etlsql-policy.json"), $$"""
                {
                  "schemaVersion": "1.0",
                  "requiredTags": [
                    { "tag": "@owner", "scopes": ["COLUMN"]{{exclusions}} },
                    { "tag": "@steward", "scopes": ["COLUMN"]{{exclusions}} }
                  ]
                }
                """);
        }

        public string WriteScript(string source)
        {
            var path = Path.Combine(_root, "pipeline.etlsql");
            File.WriteAllText(path, source);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        private static string JsonString(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
