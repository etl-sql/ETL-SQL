using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ETL_SQL.Tests.Architecture;

/// <summary>
/// Pins every place the Portal compares an object's <c>CreatedBy</c>/<c>OwnerId</c> against the
/// caller, so a new authorship short-circuit cannot be added without someone writing down why.
///
/// This exists because v0.17.0 briefly treated <c>CreatedBy == userId</c> as standing permission in
/// five places, which meant removing a user from every group revoked nothing they had authored —
/// deprovisioning did not deprovision. That regression was found by tests during the release gate
/// after a hand review of the same diff had cleared it. Reading a diff is not a reliable way to
/// catch this; an inventory that must be maintained is.
///
/// The rule this guards: <b>authorship upgrades an existing grant, it never substitutes for one.</b>
/// Each site below is classified as one of:
/// <list type="bullet">
///   <item><description><b>guarded</b> — escalates only when the caller already holds a grant.</description></item>
///   <item><description><b>ownership</b> — a deliberate ownership rule, documented at the call site.</description></item>
///   <item><description><b>not a permission decision</b> — counting, reassigning, or scoping a listing down.</description></item>
///   <item><description><b>unconditional</b> — a real short-circuit, tied to an open TODO.md item.</description></item>
/// </list>
///
/// The assertion is set equality, so the inventory can only shrink or change deliberately: removing
/// a short-circuit forces its entry out, and editing a condition changes its key.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class AuthorshipPermissionBoundaryTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static readonly string[] ScannedProjects =
        ["src/ETL-SQL.Portal", "src/ETL-SQL.Portal.Data"];

    /// <summary>Comment lines are inert, so they are not part of the inventory.</summary>
    private static readonly Regex AuthorshipComparison =
        new(@"\b(CreatedBy|OwnerId)\s*==", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Key is <c>file|trimmed source line</c>; value is why the site is acceptable.</summary>
    private static readonly IReadOnlyDictionary<string, string> KnownAuthorshipChecks =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ── Not permission decisions ──────────────────────────────────────────────
            ["AdminController.cs|var ownedFolders = await db.Folders.CountAsync(f => f.OwnerId == id);"] =
                "Counts what a user being deleted owns, so the admin sees the impact. No access is granted.",
            ["AdminController.cs|var ownedReports = await db.Reports.CountAsync(r => r.CreatedBy == id);"] =
                "Counts what a user being deleted owns. No access is granted.",
            ["AdminController.cs|var ownedDatasets = await db.Datasets.CountAsync(d => d.CreatedBy == id);"] =
                "Counts what a user being deleted owns. No access is granted.",
            ["AdminController.cs|await db.Folders.Where(f => f.OwnerId == id).ExecuteUpdateAsync(s => s"] =
                "Reassigns ownership away from a deleted user. Removes authority rather than granting it.",
            ["AdminController.cs|await db.Reports.Where(r => r.CreatedBy == id).ExecuteUpdateAsync(s => s"] =
                "Reassigns ownership away from a deleted user.",
            ["AdminController.cs|await db.Datasets.Where(d => d.CreatedBy == id).ExecuteUpdateAsync(s => s"] =
                "Reassigns ownership away from a deleted user.",
            ["AdminController.cs|.Where(d => d.CreatedBy == id)"] =
                "Collects the datasets being reassigned so the new owner's grant can be written. "
                + "Reads which rows to move; grants nothing by itself.",
            ["ReportsController.cs|.Where(a => a.ReportId == id && (IsAdmin || a.OwnerId == CurrentUserId))"] =
                "Scopes an alert listing down to the caller's own alerts. Narrows access, never widens it.",

            // ── Deliberate ownership rules ────────────────────────────────────────────
            ["FolderPermissionService.cs|&& await db.Folders.AnyAsync(f => f.Id == folderId && f.OwnerId == userId))"] =
                "Folder ownership implies Manage — documented at the call site, and the ownership "
                + "fallback that system-published datasets depend on.",
            ["FolderPermissionService.cs|.Where(f => ids.Contains(f.Id) && f.OwnerId == userId)"] =
                "Batch form of the folder-ownership rule above.",
            ["CatalogController.cs|db.Folders.Any(f => f.Id == r.FolderId && f.OwnerId == userId)"] =
                "Catalog visibility follows the same folder-ownership rule.",

            // ── Guarded: authorship upgrades an existing grant ────────────────────────
            ["FolderPermissionService.cs|if (report.CreatedBy == userId && (folderPerm.HasValue || directPerm.HasValue))"] =
                "The rule itself: report authorship lifts a surviving grant to Manage, and yields "
                + "nothing when every grant is gone.",
            ["FolderPermissionService.cs|if (report.CreatedBy == userId)"] =
                "DescribeReportGrantsAsync — names the grants behind an answer for the access "
                + "simulator. Explains a permission, never resolves one; the permission itself comes "
                + "from GetEffectiveReportPermissionAsync above.",

            // Datasets reach the same rule by a different route. DatasetAcl is group-scoped, so
            // there was no per-user grant for authorship to upgrade; a creator is now given an
            // explicit Owner row in DatasetUserAcls instead, and DatasetPermissionService and
            // ReportDependencyService read grants only. That is why no dataset entry appears here.
        };

    [Fact]
    public void AuthorshipComparisons_AreAllInventoried()
    {
        var live = FindAuthorshipComparisons();

        var unlisted = live.Where(site => !KnownAuthorshipChecks.ContainsKey(site)).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var stale = KnownAuthorshipChecks.Keys.Where(key => !live.Contains(key)).OrderBy(s => s, StringComparer.Ordinal).ToList();

        Assert.True(unlisted.Count == 0,
            "New CreatedBy/OwnerId comparison(s) found in the Portal. Authorship upgrades an existing "
            + "grant; it never substitutes for one. Add each site to KnownAuthorshipChecks with the "
            + "reason it is safe, or guard it on an existing grant:\n  "
            + string.Join("\n  ", unlisted));

        Assert.True(stale.Count == 0,
            "KnownAuthorshipChecks lists sites that no longer exist. Remove them so the inventory "
            + "keeps shrinking:\n  " + string.Join("\n  ", stale));
    }

    [Fact]
    public void EveryInventoriedSite_CarriesAJustification()
    {
        var unjustified = KnownAuthorshipChecks
            .Where(entry => string.IsNullOrWhiteSpace(entry.Value))
            .Select(entry => entry.Key)
            .ToList();

        Assert.True(unjustified.Count == 0,
            "An authorship check was inventoried without saying why it is safe:\n  "
            + string.Join("\n  ", unjustified));
    }

    private static HashSet<string> FindAuthorshipComparisons()
    {
        var sites = new HashSet<string>(StringComparer.Ordinal);

        foreach (var project in ScannedProjects)
        {
            var root = Path.Combine(RepoRoot, project);
            Assert.True(Directory.Exists(root), $"Scanned project '{project}' was not found under {RepoRoot}.");

            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (relative.StartsWith("obj/", StringComparison.Ordinal)
                    || relative.StartsWith("bin/", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var line in File.ReadLines(file))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal)
                        || trimmed.StartsWith("///", StringComparison.Ordinal)
                        || trimmed.StartsWith("*", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (AuthorshipComparison.IsMatch(trimmed))
                        sites.Add($"{Path.GetFileName(file)}|{trimmed}");
                }
            }
        }

        return sites;
    }
}
