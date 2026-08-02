using System.Text.Json;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public sealed class DeploymentProfileJourneyFixtureTests
{
    private static readonly string[] RequiredJourneys =
    [
        "portable-pipeline-execution",
        "connection-and-secret-rebinding",
        "schedules-and-notifications",
        "quality-and-stewardship",
        "report-publication",
        "identity-and-ownership",
        "backup-and-restore",
        "environment-promotion",
        "topology-growth",
        "n-to-n-plus-one-upgrade",
        "saas-import-and-export",
        "tenant-isolation-and-failure-containment"
    ];

    [Fact]
    public void FixtureDefinesEveryRequiredJourneyWithPositiveNegativeAndContinuityProof()
    {
        var fixture = Load();

        Assert.Equal("etl-sql.deployment-profile-journeys/v1", fixture.SchemaVersion);
        Assert.Equal(RequiredJourneys.Order(), fixture.Journeys.Select(j => j.Name).Order());
        Assert.Equal(fixture.Journeys.Count, fixture.Journeys.Select(j => j.Name).Distinct().Count());
        Assert.All(fixture.Journeys, journey =>
        {
            Assert.NotEmpty(journey.Profiles);
            Assert.NotEmpty(journey.PortableState);
            Assert.NotEmpty(journey.HostOwnedState);
            Assert.False(string.IsNullOrWhiteSpace(journey.PositiveProof));
            Assert.False(string.IsNullOrWhiteSpace(journey.NegativeProof));
            Assert.NotEmpty(journey.Continuity);
        });
    }

    [Fact]
    public void FixtureCoversEveryProfileAndSupportedTransition()
    {
        var fixture = Load();

        Assert.Equal(new[] { "Enterprise", "SaaS", "Solo", "Team" },
            fixture.Journeys.SelectMany(j => j.Profiles).Distinct().Order());
        Assert.Equal(new[] { "EnterpriseToSaaS", "SoloToSaaS", "SoloToTeam", "TeamToEnterprise", "Upgrade" },
            fixture.Journeys.SelectMany(j => j.Transitions).Distinct().Order());
    }

    [Fact]
    public void FixtureNeverTreatsResolvedSecretsOrTenantBoundariesAsPortable()
    {
        var fixture = Load();

        Assert.DoesNotContain(fixture.Journeys.SelectMany(j => j.PortableState), value =>
            value.Contains("secret-value", StringComparison.OrdinalIgnoreCase)
            || value.Contains("connection-string", StringComparison.OrdinalIgnoreCase)
            || value.Equals("tenant-id", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fixture.Journeys.SelectMany(j => j.HostOwnedState), value => value == "secret-value");
        Assert.Contains(fixture.Journeys.SelectMany(j => j.HostOwnedState), value => value == "tenant-id");
    }

    private static JourneyFixture Load() => JsonSerializer.Deserialize<JourneyFixture>(
        File.ReadAllText(Path.Combine(RepoRoot(), "tests", "fixtures", "deployment-profile-journeys.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

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

    private sealed record JourneyFixture(string SchemaVersion, IReadOnlyList<Journey> Journeys);
    private sealed record Journey(
        string Name,
        IReadOnlyList<string> Profiles,
        IReadOnlyList<string> Transitions,
        IReadOnlyList<string> PortableState,
        IReadOnlyList<string> HostOwnedState,
        string PositiveProof,
        string NegativeProof,
        IReadOnlyList<string> Continuity);
}
