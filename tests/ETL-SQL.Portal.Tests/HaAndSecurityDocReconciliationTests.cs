using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Reconciles the HA, threat-model, and verification-runbook documents against the source they
/// describe, in the same spirit as <see cref="ArchitectureDocReconciliationTests"/>.
///
/// <para>These three documents fail differently from an architecture overview. Someone reading the
/// architecture document is trying to understand the system; someone reading the HA readiness
/// contract is holding an outage, and someone reading the coverage map is deciding whether a
/// control has been certified. A wrong answer there is acted on immediately.</para>
///
/// <para>Only mechanically checkable claims are asserted — finding codes, check keys, cited test
/// names, cited commands, and the read-only fleet boundary. Prose about intent is left alone.</para>
/// </summary>
[Trait("Category", "Portal")]
public sealed class HaAndSecurityDocReconciliationTests
{
    private static string HaDoc() => File.ReadAllText(Path.Combine(
        RepoRoot(), "docs", "architecture", "decisions", "ha-topology-failure-certification.md"));

    private static string ReadinessSource() => File.ReadAllText(Path.Combine(
        RepoRoot(), "src", "ETL-SQL.Portal", "Services", "PortalTopologyReadinessService.cs"));

    /// <summary>
    /// A readiness finding code is an operator-facing contract: it is what a 503 says about itself,
    /// and it is the string somebody greps for at 3am. An emitted code that the certification
    /// document does not list is a failure mode with no documented remedy.
    /// </summary>
    [Fact]
    public void EveryReadinessFindingCode_IsDocumented()
    {
        var emitted = Regex.Matches(ReadinessSource(), @"findings\.Add\(""(?<code>[^""]+)""\)")
            .Select(m => m.Groups["code"].Value)
            .Distinct()
            .ToList();
        Assert.NotEmpty(emitted);

        var doc = HaDoc();
        var missing = emitted.Where(code => !doc.Contains(code, StringComparison.Ordinal)).ToList();

        Assert.True(missing.Count == 0,
            $"PortalTopologyReadinessService emits {emitted.Count} finding codes; these are absent "
            + "from docs/architecture/decisions/HA_Topology_Failure_Certification.md:\n  "
            + string.Join("\n  ", missing)
            + "\n\nAn undocumented finding code is a 503 an operator cannot look up.");
    }

    /// <summary>
    /// The <c>checks</c> object is the readiness response contract. A key the document does not
    /// list is a dependency a load balancer can be removed for without the operator knowing it was
    /// ever probed.
    /// </summary>
    [Fact]
    public void EveryReadinessCheckKey_IsDocumented()
    {
        var keys = Regex.Matches(ReadinessSource(), @"checks\[""(?<key>[A-Za-z]+)""\]\s*=")
            .Select(m => m.Groups["key"].Value)
            .Distinct()
            .ToList();
        Assert.NotEmpty(keys);

        var doc = HaDoc();
        var missing = keys.Where(key => !doc.Contains($"checks.{key}", StringComparison.Ordinal)).ToList();

        Assert.True(missing.Count == 0,
            "These /healthz check keys are not described in the readiness contract:\n  "
            + string.Join("\n  ", missing));
    }

    /// <summary>
    /// Every topology and load-balancer setting must appear in the configuration reference. These
    /// decide whether <c>/healthz</c> returns 200, so one that exists in code and not in the
    /// reference is a way to take a node out of rotation that nobody can find afterwards.
    /// </summary>
    [Fact]
    public void EveryTopologySetting_AppearsInTheConfigurationReference()
    {
        var config = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "ETL-SQL.Reporting", "PortalConfig.cs"));

        var settings = new List<string>();
        foreach (var (className, prefix) in new[]
                 {
                     ("PortalTopologyConfig", "Topology"),
                     ("PortalLoadBalancerConfig", "LoadBalancer")
                 })
        {
            var body = Regex.Match(config, @"class " + className + @"\s*\{(?<body>(?:[^{}]|\{[^{}]*\})*)\}");
            Assert.True(body.Success, $"Could not locate {className} in PortalConfig.cs.");

            settings.AddRange(Regex
                .Matches(body.Groups["body"].Value, @"public [^\s]+\??\s+(?<name>\w+)\s*\{\s*get;")
                .Select(m => $"{prefix}.{m.Groups["name"].Value}"));
        }
        Assert.NotEmpty(settings);

        var reference = File.ReadAllText(Path.Combine(
            RepoRoot(), "docs", "administration", "portal", "portal-config-reference.md"));
        var missing = settings.Where(s => !reference.Contains(s, StringComparison.Ordinal)).ToList();

        Assert.True(missing.Count == 0,
            $"{settings.Count} readiness-affecting settings exist; these are absent from the "
            + "configuration reference:\n  "
            + string.Join("\n  ", missing));
    }

    /// <summary>
    /// The Automated Coverage Map is a certification claim: it says a named test proves a named
    /// failure scenario. A renamed or deleted test leaves the claim standing and the scenario
    /// uncovered — and a coverage map is exactly the document nobody re-derives before citing it.
    /// </summary>
    [Fact]
    public void EveryTestCitedInTheCoverageMap_Exists()
    {
        var doc = HaDoc();
        var section = doc[doc.IndexOf("## Automated Coverage Map", StringComparison.Ordinal)..];

        // Cited as `Class.Method` or as a bare `Method` continuing the previous row's class.
        var cited = Regex.Matches(section, @"`(?<name>[A-Z][A-Za-z0-9]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?)`")
            .Select(m => m.Groups["name"].Value)
            .Select(name => name.Contains('.', StringComparison.Ordinal) ? name.Split('.')[^1] : name)
            .Where(name => name.Contains('_', StringComparison.Ordinal))
            .Distinct()
            .ToList();
        Assert.NotEmpty(cited);

        var testSources = Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), "tests"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToList();

        var missing = cited
            .Where(name => !testSources.Any(src => src.Contains(name, StringComparison.Ordinal)))
            .ToList();

        Assert.True(missing.Count == 0,
            "The HA coverage map cites tests that no longer exist:\n  "
            + string.Join("\n  ", missing)
            + "\n\nA coverage map naming a deleted test claims certification nobody performed.");
    }

    /// <summary>
    /// Every <c>etl-sql admin ha-soak</c> subcommand cited by the certification and evidence
    /// documents must exist in the CLI. A runbook is followed by typing what it says.
    /// </summary>
    [Fact]
    public void EveryHaSoakSubcommandCitedInTheRunbooks_ExistsInTheCli()
    {
        var cli = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "ETL-SQL.App", "App", "CliOrchestrator.cs"));
        var defined = Regex.Matches(cli, @"haSoak\w*Command = new Command\(""(?<name>[^""]+)""")
            .Select(m => m.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(defined);

        var docs = new[]
        {
            Path.Combine(RepoRoot(), "docs", "architecture", "decisions", "ha-topology-failure-certification.md"),
            Path.Combine(RepoRoot(), "TODO.md")
        };

        var missing = new List<string>();
        foreach (var path in docs)
        {
            var text = File.ReadAllText(path);
            // Only inside a command span: "`... ha-soak <sub>`". Prose such as "the `ha-soak`
            // sequence" closes the backtick first and is not a citation of a subcommand.
            foreach (var cited in Regex.Matches(text, @"ha-soak (?<sub>[a-z][a-z-]*)")
                         .Select(m => m.Groups["sub"].Value)
                         .Distinct())
            {
                if (!defined.Contains(cited))
                    missing.Add($"{Path.GetFileName(path)} cites `ha-soak {cited}`");
            }
        }

        Assert.True(missing.Count == 0,
            "These runbook steps name a subcommand the CLI does not define:\n  "
            + string.Join("\n  ", missing));
    }

    /// <summary>
    /// The Enterprise Security Review Packet approves fleet aggregation as read-only status polling
    /// and explicitly does not approve remote mutation. That is a trust boundary, and a boundary
    /// stated only in a document is a boundary until the first convenient POST.
    /// </summary>
    [Fact]
    public void FleetAggregation_ExposesNoMutatingRoutes()
    {
        var controllers = Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), "src", "ETL-SQL.Portal", "Controllers"), "*.cs")
            .Where(f => File.ReadAllText(f).Contains("[Route(\"api/fleet", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(controllers);

        var mutating = new List<string>();
        foreach (var file in controllers)
        {
            foreach (var verb in Regex.Matches(File.ReadAllText(file), @"\[Http(?<verb>Post|Put|Delete|Patch)")
                         .Select(m => m.Groups["verb"].Value))
            {
                mutating.Add($"{Path.GetFileName(file)} exposes an Http{verb} route");
            }
        }

        Assert.True(mutating.Count == 0,
            "Fleet aggregation is approved as read-only status polling only:\n  "
            + string.Join("\n  ", mutating)
            + "\n\nRemote fleet mutation requires its own threat model before any route exists. See "
            + "docs/architecture/decisions/Enterprise_Security_Review_Packet.md.");
    }

    /// <summary>
    /// Auto mode resolves to HighAvailability as soon as a shared key ring is configured — even on
    /// a single SQLite node — and HA mode then requires PostgreSQL, so <c>/healthz</c> fails closed
    /// and a load balancer removes the node.
    ///
    /// <para>This is reasonable behaviour (a shared key ring is a multi-node signal) and a trap for
    /// anyone who merely moved the key ring off its default path. It is asserted here rather than
    /// argued, because the whole point of the document it backs is telling an operator why a node
    /// that looks fine is not being routed to.</para>
    /// </summary>
    [Fact]
    public async Task AutoMode_TreatsAConfiguredKeyRingAsHighAvailability_AndFailsClosedWithoutPostgres()
    {
        var keyRing = Path.Combine(Path.GetTempPath(), "etlsql-readiness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(keyRing);
        try
        {
            using var factory = new HostedPortalFactory(portalConfig: config =>
            {
                config.Storage.KeyRingPath = keyRing;
                config.Topology.ExpectedMode = "Auto";
            });
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/healthz");
            var body = await response.Content.ReadFromJsonAsync<JsonObject>();

            Assert.Equal("HighAvailability", body!["mode"]!.GetValue<string>());
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

            var findings = body["findings"]!.AsArray().Select(f => f!.GetValue<string>()).ToList();
            Assert.Contains("ha-requires-portal-postgres", findings);
        }
        finally
        {
            try { Directory.Delete(keyRing, recursive: true); } catch { }
        }
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
