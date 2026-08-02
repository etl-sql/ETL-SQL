using ETL_SQL.Core.Governance;

namespace ETL_SQL.Tests.Core;

public sealed class WorkspacePolicyDocumentTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"etlsql-policy-{Guid.NewGuid():N}");

    public WorkspacePolicyDocumentTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ExamplePolicy_LoadsWithAllLocalFirstSections()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var path = Path.Combine(repoRoot, "samples", "05_Security_Diagnostics", "etlsql-policy.example.json");

        var result = WorkspacePolicyLoader.Load(path);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotEmpty(result.Policy!.RequiredTags);
        Assert.NotEmpty(result.Policy.ProtectedDataPatterns);
        Assert.NotNull(result.Policy.QualityThresholds.Warning.WarnPercent);
        Assert.NotNull(result.Policy.QualityThresholds.Failure.WarnPercent);
    }

    [Fact]
    public void InvalidPolicy_ReportsPathLineAndColumn()
    {
        var path = Path.Combine(_directory, WorkspacePolicyLoader.FileName);
        File.WriteAllText(path, """
            {
              "schemaVersion": "1.0",
              "requiredTags": [{ "tag": "owner", "scopes": ["DATABASE"] }],
              "protectedDataPatterns": [{ "name": "bad", "regex": "[", "classification": "PII", "scopes": ["COLUMN"] }],
              "qualityThresholds": {
                "warning": { "warnPercent": 0.5 },
                "failure": { "warnPercent": 0.1 }
              }
            }
            """);

        var result = WorkspacePolicyLoader.Load(path);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("start with '@'", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("Unsupported scope", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("Invalid protected-data regex", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("cannot exceed", StringComparison.Ordinal));
        Assert.All(result.Diagnostics, d =>
        {
            Assert.Equal(Path.GetFullPath(path), d.Path);
            Assert.True(d.Line > 0);
            Assert.True(d.Column > 0);
        });
    }

    [Fact]
    public void Find_WalksFromScriptDirectoryToWorkspaceRoot()
    {
        var nested = Path.Combine(_directory, "pipelines", "daily");
        Directory.CreateDirectory(nested);
        var policy = Path.Combine(_directory, WorkspacePolicyLoader.FileName);
        File.WriteAllText(policy, """
            { "schemaVersion": "1.0", "requiredTags": [], "protectedDataPatterns": [],
              "qualityThresholds": { "warning": {}, "failure": {} } }
            """);

        Assert.Equal(policy, WorkspacePolicyLoader.Find(nested));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
