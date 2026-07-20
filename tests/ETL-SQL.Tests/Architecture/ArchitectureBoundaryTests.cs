using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace ETL_SQL.Tests.Architecture;

/// <summary>
/// Enforces the documented tier layering (CLAUDE.md / AGENTS.md §8): a project may only reference
/// projects in the same or a lower tier, and specific layers may not take dependencies on packages
/// that belong to higher layers (presentation, heavy infrastructure). The rules are asserted against
/// the actual <c>src/*.csproj</c> reference graph.
///
/// Today's known violations are pinned in explicit allow-lists tied to open TODO.md items. The tests
/// assert the live set of violations equals the allow-list, so a NEW violation fails CI immediately,
/// and resolving a pinned violation (removing the edge) forces its allow-list entry to be removed too
/// — the lists can only shrink.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class ArchitectureBoundaryTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    // Per-project tier. Lower may not reference higher. Assigned per project (not per CLAUDE.md group)
    // so intended edges are legal: the report *libraries* (Reporting/ReportBuilder/ReportRuntime) sit
    // just above Engine — Engine must not consume them — while the shells and hosts on top may.
    private static readonly IReadOnlyDictionary<string, int> Tier = new Dictionary<string, int>
    {
        ["Core"] = 0,
        ["Analysis"] = 1,
        ["Engine"] = 1,
        ["Reporting"] = 2,
        ["ReportBuilder"] = 2,
        ["ReportRuntime"] = 2,
        ["Portal.Data"] = 2,
        ["Connectors.Common"] = 2,
        ["Connectors"] = 3,
        // The per-domain connector groups are peers of Connectors.Files: each references only Core
        // and Connectors.Common, never Engine.
        ["Connectors.Cloud"] = 3,
        ["Connectors.Databases"] = 3,
        ["Connectors.Files"] = 3,
        ["Connectors.Messaging"] = 3,
        ["Connectors.Remote"] = 3,
        ["Infrastructure.Docker"] = 3,
        ["Infrastructure.Logging"] = 3,
        ["Infrastructure.Sqlite"] = 3,
        ["Orchestrator"] = 3,
        ["ReportHosting"] = 3,
        ["Portal.Migrations.Postgres"] = 3,
        ["LanguageServer"] = 4,
        ["Orchestrator.Service"] = 4,
        ["ReportPlayer"] = 4,
        ["WorkstationEditor"] = 4,
        ["Portal"] = 5,
        ["App"] = 6,
        ["TUI"] = 6,
        ["ReportBuilder.CLI"] = 7,
    };

    // Upward project references that exist today. Each is an open layering-debt item in TODO.md.
    // New upward edges are not permitted.
    private static readonly HashSet<(string From, string To)> KnownUpwardReferences =
    [
    ];

    // Packages a given layer must not depend on. Core is meant to be a contracts/domain layer (no heavy
    // infrastructure); Engine must not depend on presentation packages. Match is by id prefix.
    private static readonly IReadOnlyDictionary<string, string[]> BannedPackagePrefixes =
        new Dictionary<string, string[]>
        {
            ["Core"] = ["Testcontainers", "Docker.DotNet", "Microsoft.Data.Sqlite", "SQLitePCLRaw"],
            ["Engine"] = ["Spectre.Console"],
        };

    // Banned package dependencies that exist today, pinned to their TODO.md items ("Restore ETL-SQL.Core
    // as a contracts/domain layer" and "Correct the Engine dependency direction"). New ones are not
    // permitted; resolving one requires removing its entry here.
    private static readonly HashSet<(string Project, string Package)> KnownBannedPackages =
    [
    ];

    [Fact]
    public void EveryProject_IsAssignedATier()
    {
        var unmapped = Projects().Keys.Where(p => !Tier.ContainsKey(p)).OrderBy(p => p).ToList();
        Assert.True(unmapped.Count == 0,
            "src projects missing from the Tier map (add them so layering stays enforced): "
            + string.Join(", ", unmapped));
    }

    [Fact]
    public void ProjectReferences_ObeyTierDirection_ExceptPinnedViolations()
    {
        var projects = Projects();
        var actualUpward = new HashSet<(string, string)>();

        foreach (var (name, csproj) in projects)
        {
            if (!Tier.TryGetValue(name, out var fromTier)) continue;
            foreach (var target in csproj.ProjectReferences)
            {
                if (!Tier.TryGetValue(target, out var toTier)) continue;
                if (toTier > fromTier)
                    actualUpward.Add((name, target));
            }
        }

        var newViolations = actualUpward.Except(KnownUpwardReferences).OrderBy(x => x).ToList();
        var resolved = KnownUpwardReferences.Except(actualUpward).OrderBy(x => x).ToList();

        Assert.True(newViolations.Count == 0,
            "New upward project references (a project may not reference a higher tier):\n"
            + string.Join("\n", newViolations.Select(v => $"  {v.Item1} -> {v.Item2}")));

        Assert.True(resolved.Count == 0,
            "Pinned upward references no longer exist — remove them from KnownUpwardReferences:\n"
            + string.Join("\n", resolved.Select(v => $"  {v.Item1} -> {v.Item2}")));
    }

    [Fact]
    public void BannedPackages_AreAbsent_ExceptPinnedViolations()
    {
        var projects = Projects();
        var actual = new HashSet<(string, string)>();

        foreach (var (project, prefixes) in BannedPackagePrefixes)
        {
            if (!projects.TryGetValue(project, out var csproj)) continue;
            foreach (var pkg in csproj.PackageReferences)
                if (prefixes.Any(p => pkg.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    actual.Add((project, pkg));
        }

        var newViolations = actual.Except(KnownBannedPackages).OrderBy(x => x).ToList();
        var resolved = KnownBannedPackages.Except(actual).OrderBy(x => x).ToList();

        Assert.True(newViolations.Count == 0,
            "New banned package dependencies for a restricted layer:\n"
            + string.Join("\n", newViolations.Select(v => $"  {v.Item1} depends on {v.Item2}")));

        Assert.True(resolved.Count == 0,
            "Pinned banned package dependencies no longer exist — remove them from KnownBannedPackages:\n"
            + string.Join("\n", resolved.Select(v => $"  {v.Item1} depends on {v.Item2}")));
    }

    private sealed record ProjectInfo(
        IReadOnlyList<string> ProjectReferences, IReadOnlyList<string> PackageReferences);

    private static Dictionary<string, ProjectInfo> Projects()
    {
        var srcDir = Path.Combine(RepoRoot, "src");
        Assert.True(Directory.Exists(srcDir), $"Expected src directory at {srcDir}");

        var result = new Dictionary<string, ProjectInfo>();
        foreach (var csproj in Directory.GetFiles(srcDir, "ETL-SQL.*.csproj", SearchOption.AllDirectories))
        {
            var name = StripName(Path.GetFileNameWithoutExtension(csproj));
            var doc = XDocument.Load(csproj);

            var projRefs = doc.Descendants("ProjectReference")
                .Select(e => (string?)e.Attribute("Include"))
                .Where(v => v is not null)
                .Select(v => StripName(Path.GetFileNameWithoutExtension(v!.Replace('\\', '/'))))
                .ToList();

            var pkgRefs = doc.Descendants("PackageReference")
                .Select(e => (string?)e.Attribute("Include"))
                .Where(v => v is not null)
                .Select(v => v!)
                .ToList();

            result[name] = new ProjectInfo(projRefs, pkgRefs);
        }

        Assert.True(result.Count > 0, $"No ETL-SQL.*.csproj projects found under {srcDir}");
        return result;
    }

    // "ETL-SQL.Portal.Data" -> "Portal.Data"; "ETL-SQL.Core" -> "Core".
    private static string StripName(string fileName) =>
        fileName.StartsWith("ETL-SQL.", StringComparison.Ordinal)
            ? fileName["ETL-SQL.".Length..]
            : fileName;
}
