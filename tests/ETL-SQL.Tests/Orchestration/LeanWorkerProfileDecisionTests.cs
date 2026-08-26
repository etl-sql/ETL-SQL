using System.Text.Json;

namespace ETL_SQL.Tests.Orchestration;

public sealed class LeanWorkerProfileDecisionTests
{
    [Fact]
    public void BoundaryEvidenceMeasuresEveryRequiredDimensionAndRejectsPublication()
    {
        using var document = ReadEvidence("boundary-measurement.json");
        var root = document.RootElement;
        var baseline = root.GetProperty("baseline");
        var candidate = root.GetProperty("candidate");

        Assert.True(baseline.GetProperty("publishedBytes").GetInt64() > 0);
        Assert.True(candidate.GetProperty("publishedBytes").GetInt64() > 0);
        Assert.True(baseline.GetProperty("coldStartMs").GetProperty("samples").GetInt32() >= 3);
        Assert.True(baseline.GetProperty("workingSetBytes").GetProperty("median").GetDouble() > 0);
        Assert.True(baseline.GetProperty("loadedAssemblyCount").GetProperty("median").GetDouble() > 0);
        Assert.True(baseline.GetProperty("dependencyClosure").GetProperty("count").GetInt32() > 0);
        Assert.True(root.GetProperty("sandbox").GetProperty("baseline")
            .GetProperty("lifetimeMs").GetProperty("median").GetDouble() > 0);
        Assert.True(root.GetProperty("sandbox").GetProperty("candidate")
            .GetProperty("lifetimeMs").GetProperty("median").GetDouble() > 0);
        Assert.True(root.GetProperty("costSensitivity").GetProperty("monthlyExecutions").GetInt64() > 0);

        var decision = root.GetProperty("decisionThreshold");
        Assert.False(decision.GetProperty("materialBoundaryBenefit").GetBoolean());
        Assert.False(decision.GetProperty("artifactPublicationAuthorized").GetBoolean());
    }

    [Fact]
    public void TrimEvidenceRejectsTheReflectionRegression()
    {
        using var document = ReadEvidence("trimmed-experiment.json");
        var root = document.RootElement;

        Assert.Equal("rejected", root.GetProperty("status").GetString());
        Assert.Contains("Reflection-based serialization has been disabled",
            root.GetProperty("failure").GetProperty("diagnostic").GetString(),
            StringComparison.Ordinal);
        Assert.False(root.GetProperty("artifactPublicationAuthorized").GetBoolean());
    }

    [Fact]
    public void ExperimentIsReproducibleButOutsideTheProductSolution()
    {
        var root = RepoRoot();
        var solution = File.ReadAllText(Path.Combine(root, "ETL-SQL.slnx"));
        var script = File.ReadAllText(Path.Combine(root, "scripts", "Measure-LeanWorkerProfile.ps1"));
        var fixture = Path.Combine(root, "tools", "lean-worker-experiment", "ETL-SQL.Worker.csproj");

        Assert.True(File.Exists(fixture));
        Assert.DoesNotContain("lean-worker-experiment", solution, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ETL-SQL.Worker", solution, StringComparison.Ordinal);
        Assert.Contains("requiredPublishedSizeReductionPercent = 20", script, StringComparison.Ordinal);
        Assert.Contains("requiredColdStartOrWorkingSetReductionPercent = 15", script, StringComparison.Ordinal);
        Assert.Contains("artifactPublicationAuthorized = $false", script, StringComparison.Ordinal);
        Assert.Contains("$MeasureSandbox", script, StringComparison.Ordinal);
        Assert.Contains("$TrimExperiment", script, StringComparison.Ordinal);
    }

    private static JsonDocument ReadEvidence(string name) => JsonDocument.Parse(File.ReadAllText(
        Path.Combine(RepoRoot(), "certification-results", "lean-worker", name)));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
