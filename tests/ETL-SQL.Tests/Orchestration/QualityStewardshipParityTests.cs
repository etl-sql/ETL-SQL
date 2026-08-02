using System.Text.Json;
using ETL_SQL.App;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Engine;
using ETL_SQL.Tests.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public sealed class QualityStewardshipParityTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void SharedAcceptanceFixtures_CoverEveryRequiredOperatorState()
    {
        var scenarios = Load();
        var expected = new[]
        {
            "empty-history", "first-run", "clean-run", "warning", "quarantine", "critical-failure",
            "stale-data", "missing-tags", "unowned-protected-data", "recovery"
        };
        Assert.Equal(expected.Order(), scenarios.Select(s => s.Name).Order());
    }

    [Fact]
    public void WorkstationOrchestratorAndPortalContracts_AgreeForEveryFixture()
    {
        var policy = new WorkspacePolicyDocument
        {
            RequiredTags = [new WorkspaceRequiredTagRule { Tag = "@owner", Scopes = ["COLUMN"] }]
        };

        foreach (var scenario in Load())
        {
            var workstationRuns = scenario.Runs;
            var localOrchestratorRuns = RoundTrip(workstationRuns);
            var portalApiRuns = RoundTrip(localOrchestratorRuns);
            Assert.Equal(Summarize(workstationRuns), Summarize(localOrchestratorRuns));
            Assert.Equal(Summarize(workstationRuns), Summarize(portalApiRuns));
            Assert.Equal(scenario.Expected.LatestStatus, workstationRuns.FirstOrDefault()?.Status);

            var assets = scenario.Assets.Select(a => new StewardshipAsset(
                "fixture", a.Table, a.Column, a.Tags, a.SourceFile, a.Line)).ToList();
            var workstationScore = StewardshipScoring.Evaluate(assets, policy, DateTimeOffset.UnixEpoch);
            var orchestratorScore = RoundTrip(workstationScore);
            var portalScore = RoundTrip(orchestratorScore);

            Assert.Equal(Project(workstationScore), Project(orchestratorScore));
            Assert.Equal(Project(workstationScore), Project(portalScore));
            Assert.Equal(scenario.Expected.GlobalGaps,
                workstationScore.Gaps.Count(g => g.ScopeType == "GLOBAL"));
            Assert.All(workstationScore.Scores, score => Assert.Equal(
                score.Denominator - score.Numerator,
                workstationScore.Gaps.Count(g => g.ScopeType == score.ScopeType
                    && g.ScopeName == score.ScopeName && g.Component == score.Component)));

            var summary = Summarize(workstationRuns);
            Assert.Equal(scenario.Expected.Warned, summary.Warned);
            Assert.Equal(scenario.Expected.Quarantined, summary.Quarantined);
            Assert.Equal(scenario.Expected.CriticalFailures, summary.CriticalFailures);
            Assert.Equal(scenario.Expected.Stale, summary.Stale);
        }
    }

    [Fact]
    public void FixturesAndVersionedContracts_AreCountsOnlyAndSecretFree()
    {
        var raw = File.ReadAllText(FixturePath());
        Assert.DoesNotContain("sampleValue", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connectionString", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET:", raw, StringComparison.OrdinalIgnoreCase);
        Assert.All(Load().SelectMany(s => s.Assets), asset =>
        {
            Assert.False(string.IsNullOrWhiteSpace(asset.SourceFile));
            Assert.True(asset.Line > 0);
        });
    }

    [Fact]
    public async Task OnePersonQualityPipeline_IsRunnableAndPassesItsGate()
    {
        var path = Path.Combine(RepoRoot(), "samples", "quality-loop", "customer_quality.etlsql");
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.CurrentScriptPath = path;

        await evaluator.Evaluate(TestHelpers.Parse(await File.ReadAllTextAsync(path)));

        Assert.NotNull(evaluator.LastResult);
        Assert.Equal(2, Convert.ToInt32(evaluator.LastResult!.Rows.Single()["CleanRows"]));
    }

    private static (long Warned, long Quarantined, int CriticalFailures, int Stale) Summarize(
        IReadOnlyList<FixtureRun> runs) =>
        (runs.Sum(r => r.RowsWarned), runs.Sum(r => r.RowsQuarantined),
            runs.Count(r => r.Status == "FAILED"), runs.Count(r => r.FreshnessState == "STALE"));

    private static IReadOnlyList<(string ScopeType, string ScopeName, string Component, int Numerator,
        int Denominator, decimal Percentage, string Version)> Project(StewardshipEvaluation value) =>
        value.Scores.Select(s => (s.ScopeType, s.ScopeName, s.Component, s.Numerator, s.Denominator,
            s.Percentage, s.DefinitionVersion)).ToList();

    private static T RoundTrip<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;

    private static IReadOnlyList<FixtureScenario> Load() =>
        JsonSerializer.Deserialize<List<FixtureScenario>>(File.ReadAllText(FixturePath()), Json)!;

    private static string FixturePath()
        => Path.Combine(RepoRoot(), "tests", "fixtures", "quality-stewardship-scenarios.json");

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed record FixtureScenario(
        string Name,
        IReadOnlyList<FixtureRun> Runs,
        IReadOnlyList<FixtureAsset> Assets,
        FixtureExpected Expected);
    private sealed record FixtureRun(
        string RunId,
        string Status,
        long RowsProcessed,
        long RowsWarned,
        long RowsQuarantined,
        int FailedRuleCount,
        string FreshnessState);
    private sealed record FixtureAsset(
        string Table,
        string Column,
        IReadOnlyDictionary<string, string> Tags,
        string SourceFile,
        int Line);
    private sealed record FixtureExpected(
        long Warned,
        long Quarantined,
        int CriticalFailures,
        int Stale,
        int GlobalGaps,
        string? LatestStatus);
}
