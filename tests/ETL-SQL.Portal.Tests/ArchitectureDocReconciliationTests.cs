using System.Text.RegularExpressions;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Reconciles the Portal architecture document against the source it describes.
///
/// <para>Documentation drift is not a tidiness problem here. `Portal.md` said three Identity roles
/// were seeded when the answer was eight — five of them security-relevant, including every
/// governance role — and it said so for as long as nobody re-read it against `Program.cs`. An
/// architecture document that is confidently wrong is worse than a missing one: a missing document
/// sends people to the code, and a wrong one stops them.</para>
///
/// <para>Only mechanically checkable claims are asserted. Prose about intent cannot be verified from
/// source and is not attempted — a test that pretended to would either be vacuous or would block
/// every honest rewording.</para>
/// </summary>
[Trait("Category", "Portal")]
public sealed class ArchitectureDocReconciliationTests
{
    private static string PortalDoc() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "docs", "architecture", "portal.md"));

    [Fact]
    public void EverySeededIdentityRole_IsDocumented()
    {
        // The roles actually seeded, read from the source rather than restated here — a second copy
        // of the list would be one more thing to drift.
        var program = File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "ETL-SQL.Portal", "Program.cs"));
        var match = Regex.Match(program, @"foreach \(var role in new\[\]\s*\{(?<roles>[^}]*)\}");
        Assert.True(match.Success, "Could not find the role-seeding loop in Program.cs.");

        var seeded = Regex.Matches(match.Groups["roles"].Value, "\"(?<r>[^\"]+)\"")
            .Select(m => m.Groups["r"].Value)
            .ToList();
        Assert.NotEmpty(seeded);

        var doc = PortalDoc();
        var undocumented = seeded.Where(role => !doc.Contains($"`{role}`", StringComparison.Ordinal)).ToList();

        Assert.True(undocumented.Count == 0,
            $"Program.cs seeds {seeded.Count} roles; these are absent from docs/architecture/portal.md:\n  "
            + string.Join("\n  ", undocumented)
            + "\n\nA reader who trusts the document will grant the wrong access.");
    }

    [Fact]
    public void EveryPersistedEntity_AppearsInTheEntitySummary()
    {
        var context = File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "ETL-SQL.Portal.Data", "PortalDbContext.cs"));
        var entities = Regex.Matches(context, @"public DbSet<(?<e>[A-Za-z0-9_]+)>")
            .Select(m => m.Groups["e"].Value)
            .Distinct()
            .ToList();
        Assert.NotEmpty(entities);

        var doc = PortalDoc();
        var missing = entities.Where(e => !doc.Contains(e, StringComparison.Ordinal)).ToList();

        Assert.True(missing.Count == 0,
            $"{missing.Count} persisted entities are absent from docs/architecture/portal.md:\n  "
            + string.Join("\n  ", missing)
            + "\n\nThe data model section is the map people use before reading the schema.");
    }

    [Fact]
    public void EveryAuthorizationPolicy_IsDocumented()
    {
        // A named policy is an authority boundary. One that exists in code and not in the document
        // is a boundary nobody reviewing the architecture knows to ask about.
        var program = File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "ETL-SQL.Portal", "Program.cs"));
        var policies = Regex.Matches(program, @"opt\.AddPolicy\(""(?<p>[^""]+)""")
            .Select(m => m.Groups["p"].Value)
            .Distinct()
            .ToList();
        Assert.NotEmpty(policies);

        var doc = PortalDoc();
        var missing = policies.Where(p => !doc.Contains(p, StringComparison.Ordinal)).ToList();

        Assert.True(missing.Count == 0,
            $"These authorization policies are not mentioned in docs/architecture/portal.md:\n  "
            + string.Join("\n  ", missing));
    }

    [Fact]
    public void EveryApiControllerArea_HasAnApiReferenceSection()
    {
        // Route-prefix level, not per-endpoint. Requiring every endpoint to be listed would make the
        // document a generated artifact and the test a chore that gets suppressed; requiring every
        // *area* to appear catches a whole surface shipping undocumented, which is the failure that
        // matters to someone trying to understand what the Portal exposes.
        var controllers = Directory.EnumerateFiles(
            Path.Combine(RepoRoot(), "src", "ETL-SQL.Portal", "Controllers"), "*.cs");

        var doc = PortalDoc();
        var missing = new List<string>();

        foreach (var file in controllers)
        {
            var source = File.ReadAllText(file);
            var route = Regex.Match(source, @"\[Route\(""(?<r>api/[^""{]*)""\)\]");
            if (!route.Success) continue;

            var prefix = route.Groups["r"].Value.TrimEnd('/');
            // Sub-resources of an area already covered by their parent's section.
            if (prefix is "api" or "api/jobs") continue;

            if (!doc.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                missing.Add($"{Path.GetFileName(file)} exposes /{prefix}");
        }

        Assert.True(missing.Count == 0,
            "These API areas have no mention in the Portal architecture document:\n  "
            + string.Join("\n  ", missing));
    }

    [Fact]
    public void EveryStudioCapability_AppearsInTheConfigurationReference()
    {
        // Capabilities are configured by name in Portal:Studio:RoleCapabilities, so an operator
        // grants them by typing them. One that exists in code and not in the reference is one
        // nobody can grant deliberately — and the filter rejects an unknown name rather than
        // storing a typo, so the failure is silent from the reference's side: the capability simply
        // never gets used.
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "ETL-SQL.Portal", "Services", "StudioAuthorizationService.cs"));
        var capabilities = Regex.Matches(source, @"public const string (?<c>\w+) = nameof\(")
            .Select(m => m.Groups["c"].Value)
            .Where(c => c != "CapabilityClaim")
            .ToList();
        Assert.NotEmpty(capabilities);

        var reference = File.ReadAllText(Path.Combine(
            RepoRoot(), "docs", "administration", "platform", "config", "portal-configuration.md"));
        var missing = capabilities.Where(c => !reference.Contains(c, StringComparison.Ordinal)).ToList();

        Assert.True(missing.Count == 0,
            $"{capabilities.Count} Studio capabilities exist; these are absent from the "
            + "configuration reference operators use to grant them:\n  "
            + string.Join("\n  ", missing));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ETL-SQL.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
