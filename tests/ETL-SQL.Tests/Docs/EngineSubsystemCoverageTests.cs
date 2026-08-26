namespace ETL_SQL.Tests.Docs;

/// <summary>
/// Every engine subsystem is either documented in an architecture page or explicitly recorded as
/// not needing one.
///
/// <para>The failure this prevents is <b>omission</b>, which is what architecture documentation
/// actually suffers from. A wrong type name is caught the moment someone follows it; a subsystem
/// nobody wrote down is invisible — `Engine.md` described the external spill engines 69 times and
/// data-quality rules, the columnar plan family, row-level security and adaptive execution zero
/// times, for three releases, without anything noticing.</para>
///
/// <para><b>Why an inventory rather than a text search.</b> Matching directory names against the
/// prose was tried and is useless in both directions: `Data`, `Common` and `Services` match
/// incidental English everywhere, while `Planning` reads as undocumented even though its types are
/// described by name. So this does not try to infer coverage. It forces a decision — a new
/// subsystem fails the build until someone says where it is documented, or why it does not need to
/// be. That is the same shape as <c>AuthorshipPermissionBoundaryTests</c>, and for the same
/// reason.</para>
/// </summary>
public sealed class EngineSubsystemCoverageTests
{
    /// <param name="Document">The architecture page that covers it, or null.</param>
    /// <param name="Marker">Text that page must still contain, proving the claim holds.</param>
    /// <param name="IsKnownGap">
    /// True when the subsystem is genuinely undocumented. Distinct from "no architecture surface":
    /// one is a decision, the other is a debt. Conflating them would let this inventory launder
    /// omissions into approvals, which is the opposite of its purpose.
    /// </param>
    private sealed record Coverage(string? Document, string? Marker, string Reason, bool IsKnownGap = false);

    /// <summary>
    /// Keyed by "<c>project/directory</c>". <c>Document</c> null means either no architecture
    /// surface or a known gap — <c>Reason</c> must say which and why.
    /// </summary>
    private static readonly Dictionary<string, Coverage> Inventory = new(StringComparer.Ordinal)
    {
        // ── ETL-SQL.Engine ────────────────────────────────────────────────────────────────────
        ["ETL-SQL.Engine/Engines"] = new("engine.md", "ColumnarJoinSelectPlan",
            "Execution strategies, columnar plans and the external spill engines."),
        ["ETL-SQL.Engine/Handlers"] = new("engine.md", "IStatementHandler",
            "One handler per statement type; the dispatch loop is documented in full."),
        ["ETL-SQL.Engine/Functions"] = new("expression-evaluation.md", "function",
            "Function registry and evaluation semantics."),
        ["ETL-SQL.Engine/Governance"] = new("engine.md", "organization policy",
            "Policy enforcement at the engine boundary."),
        ["ETL-SQL.Engine/Lineage"] = new("lineage.md", "lineage",
            "Lineage capture has its own architecture page."),
        ["ETL-SQL.Engine/Planning"] = new("engine.md", "PlanDecisionReasonCodes",
            "Plan decisions and the reason codes explaining a declined fast path."),
        ["ETL-SQL.Engine/Scheduling"] = new("orchestrator.md", "schedul",
            "Job scheduling is owned by the Orchestrator page."),
        ["ETL-SQL.Engine/Services"] = new("engine.md", "ColumnQualityValidator",
            "Row-pipeline services: quality validation, quarantine writing, secret resolution."),
        ["ETL-SQL.Engine/Spill"] = new("engine.md", "SpillStore",
            "Encrypted spill I/O."),
        ["ETL-SQL.Engine/Storage"] = new("engine.md", "InMemoryDataSource",
            "Engine-side table storage."),
        ["ETL-SQL.Engine/Extensions"] = new(null, null,
            "Language-level extension methods with no architectural surface of their own."),

        // ── ETL-SQL.Core ──────────────────────────────────────────────────────────────────────
        ["ETL-SQL.Core/Adaptive"] = new("engine.md", "AdaptiveExecutionController",
            "Bounded setpoint advice; documented including that no pipeline consumes it yet."),
        ["ETL-SQL.Core/Parser"] = new("parser-lexer.md", "Lexer",
            "Tokenizer, parser and AST."),
        ["ETL-SQL.Core/Quality"] = new("engine.md", "ColumnRuleParser",
            "Data-quality rules in the row pipeline."),
        ["ETL-SQL.Core/Governance"] = new("engine.md", "RowLevelSecurityScan",
            "Row-level security scanning, secret providers, enrollment protection."),
        ["ETL-SQL.Core/Planning"] = new("engine.md", "PlanDecisionReasonCodes",
            "Plan decision records shared with the engine."),
        ["ETL-SQL.Core/Profiling"] = new("engine.md", "StatementMetricsPayload",
            "Normalized, bounded statement metrics shared by every job execution transport."),
        ["ETL-SQL.Core/Execution"] = new("engine.md", "IExecutionContext",
            "Execution context contracts."),
        ["ETL-SQL.Core/Formatting"] = new("language-server.md", "FormattingProvider",
            "AST serialization behind the LSP formatting path."),
        ["ETL-SQL.Core/Statements"] = new("parser-lexer.md", "Statement",
            "Statement AST nodes."),
        ["ETL-SQL.Core/Metadata"] = new("language-server.md", "IMetadataManager",
            "Schema metadata used by completion and validation."),
        ["ETL-SQL.Core/Observability"] = new("engine.md", "ObservabilityConventions",
            "Shared low-cardinality tag and metric names, and the instrumenting decorators."),
        ["ETL-SQL.Core/Spill"] = new("engine.md", "SpillStore",
            "Spill contracts shared with the engine."),
        ["ETL-SQL.Core/Storage"] = new("engine.md", "FencedArtifactStorage",
            "Artifact areas, providers, and the guard/fence decorators HA depends on."),
        ["ETL-SQL.Core/Data"] = new("engine.md", "IDataSource",
            "Data source and sink contracts."),
        ["ETL-SQL.Core/Interfaces"] = new("engine.md", "IExecutionContext",
            "The context interfaces the evaluator implements."),
        ["ETL-SQL.Core/Analysis"] = new("engine.md", "Linting",
            "Analysis contracts behind the linting pipeline."),
        ["ETL-SQL.Core/Diagnostics"] = new("engine.md", "EXPLAIN",
            "Diagnostic output including EXPLAIN."),
        ["ETL-SQL.Core/Security"] = new("engine.md", "SecretRedactor",
            "Redaction and crypto utilities."),
        ["ETL-SQL.Core/Services"] = new(null, null,
            "Assorted internal helpers with no single architectural story."),
        ["ETL-SQL.Core/Common"] = new(null, null,
            "Shared primitives (logging facade, exceptions, string helpers); described where used."),
        ["ETL-SQL.Core/Functions"] = new("expression-evaluation.md", "function",
            "Function contracts shared with the engine registry."),
        ["ETL-SQL.Core/Multitenancy"] = new("saas-tenant-isolation.md", "TenantContext",
            "Server-derived tenant context and identity, platform access grants, storage capability."),
        ["ETL-SQL.Core/Portability"] = new("tenant-portability.md", "One Unified Bundle",
            "Signed tenant-bundle composition, validation and encryption behind export/import."),
        ["ETL-SQL.Core/Dialects"] = new("engine.md", "Dialects",
            "Target-specific SQL translation boundaries."),
        ["ETL-SQL.Core/Reporting"] = new("reporting.md", "ResolvedReportState",
            "Shared versioned resolved-state envelope for author bookmarks and Portal saved views."),
        ["ETL-SQL.Core/Reliability"] = new("decisions/provider-neutral-fault-certification.md", "ProviderNeutralFaultScenarios",
            "Provider-neutral fault certification and production canary harnesses."),
    };

    [Fact]
    public void EveryEngineSubsystem_IsInTheCoverageInventory()
    {
        var onDisk = DiscoverSubsystems();

        var undeclared = onDisk.Except(Inventory.Keys, StringComparer.Ordinal).OrderBy(x => x).ToList();
        var stale = Inventory.Keys.Except(onDisk, StringComparer.Ordinal).OrderBy(x => x).ToList();

        Assert.True(undeclared.Count == 0,
            "These engine subsystems exist in source and are not in the coverage inventory:\n  "
            + string.Join("\n  ", undeclared)
            + "\n\nSay which architecture page documents each, or record why none is needed. A "
            + "subsystem nobody wrote down is the failure mode this exists for — it is invisible "
            + "rather than wrong, so nothing else will report it.");

        Assert.True(stale.Count == 0,
            "These inventory entries no longer exist in source:\n  " + string.Join("\n  ", stale)
            + "\n\nRemove them, so the inventory keeps describing the code that is actually there.");
    }

    [Fact]
    public void EveryDocumentedSubsystem_IsStillMentionedByItsPage()
    {
        var root = RepoRoot();
        var missing = new List<string>();

        foreach (var (subsystem, coverage) in Inventory.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (coverage.Document is null || coverage.Marker is null) continue;

            var path = Path.Combine(root, "docs", "architecture", coverage.Document);
            Assert.True(File.Exists(path), $"{subsystem} names a document that does not exist: {coverage.Document}");

            var text = File.ReadAllText(path);
            if (!text.Contains(coverage.Marker, StringComparison.OrdinalIgnoreCase))
                missing.Add($"{subsystem} -> {coverage.Document} no longer mentions '{coverage.Marker}'");
        }

        Assert.True(missing.Count == 0,
            "Coverage claimed in the inventory is no longer true:\n  " + string.Join("\n  ", missing)
            + "\n\nEither the page dropped the subsystem or the marker moved. Both are worth "
            + "knowing: the inventory is only useful while its claims hold.");
    }

    /// <summary>
    /// The known documentation gaps, pinned by set equality so a new one fails the build.
    ///
    /// <para>Deliberately not a failing test for the existing two. Turning today's debt red would
    /// only invite someone to weaken the inventory to get green — and an inventory that launders
    /// omissions into approvals is worse than none. Pinning the set instead means the debt stays
    /// visible, shrinks when someone writes the page, and cannot grow quietly.</para>
    /// </summary>
    [Fact]
    public void KnownDocumentationGaps_HaveNotGrown()
    {
        // Empty, and that is the point of keeping the test: the two gaps this list was created
        // with — Core/Observability and Core/Storage — were closed by writing the pages rather
        // than by relaxing anything. A new entry here should be rare and deliberate.
        string[] expected = [];

        var actual = Inventory
            .Where(entry => entry.Value.IsKnownGap)
            .Select(entry => entry.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.True(expected.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(actual),
            "The set of undocumented engine subsystems changed.\n  expected: "
            + string.Join(", ", expected.OrderBy(x => x, StringComparer.Ordinal))
            + "\n  actual:   " + string.Join(", ", actual)
            + "\n\nIf a gap was closed, remove it from both lists. If a new one appeared, write the "
            + "page rather than adding it here — the list is a record of debt, not a place to put "
            + "things.");
    }

    private static List<string> DiscoverSubsystems()
    {
        var root = RepoRoot();
        var subsystems = new List<string>();

        foreach (var project in new[] { "ETL-SQL.Engine", "ETL-SQL.Core" })
        {
            var projectDir = Path.Combine(root, "src", project);
            Assert.True(Directory.Exists(projectDir), $"Project not found: {projectDir}");

            foreach (var dir in Directory.EnumerateDirectories(projectDir))
            {
                var name = Path.GetFileName(dir);
                if (name is "bin" or "obj") continue;
                // Directories with no C# in them are layout, not subsystems.
                if (!Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories).Any()) continue;
                subsystems.Add($"{project}/{name}");
            }
        }

        Assert.NotEmpty(subsystems);
        return subsystems;
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
