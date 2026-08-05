using System.Text.RegularExpressions;
using ETL_SQL.Portal.Data;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Guards the one thing about <see cref="FolderPermission"/> that is not self-evident from reading
/// it: <b>its numeric values are storage, and its declaration order is not authority order.</b>
///
/// <para><c>Author</c> is persisted as 3 and <c>Manage</c> as 2, because inserting <c>Author</c>
/// into its rightful place would have renumbered <c>Manage</c> and silently reinterpreted every ACL
/// row already in the database. The consequence is that <c>permission &gt;= FolderPermission.Manage</c>
/// — the obvious thing to write, and what the code did in about forty places — is now true for
/// <c>Author</c>, handing the weaker grant everything the stronger one has.</para>
///
/// <para>That is a privilege escalation an author would introduce by writing perfectly ordinary
/// C#. So it is caught here rather than left to review.</para>
/// </summary>
[Trait("Category", "Portal")]
[Trait("Category", "Smoke.Security")]
public sealed class FolderPermissionOrderingTests
{
    [Fact]
    public void StoredValues_AreFixed_SoExistingGrantsKeepTheirMeaning()
    {
        // Every ACL row in every deployed database holds one of these integers. Changing one
        // reinterprets grants that are already in force, with no migration able to detect it —
        // the rows are still valid, they just mean something else.
        Assert.Equal(0, (int)FolderPermission.Read);
        Assert.Equal(1, (int)FolderPermission.Execute);
        Assert.Equal(2, (int)FolderPermission.Manage);
        Assert.Equal(3, (int)FolderPermission.Author);
    }

    [Theory]
    [InlineData(FolderPermission.Read, FolderPermission.Execute, false)]
    [InlineData(FolderPermission.Execute, FolderPermission.Author, false)]
    [InlineData(FolderPermission.Author, FolderPermission.Manage, false)]
    [InlineData(FolderPermission.Manage, FolderPermission.Author, true)]
    [InlineData(FolderPermission.Author, FolderPermission.Execute, true)]
    [InlineData(FolderPermission.Author, FolderPermission.Read, true)]
    public void Rank_OrdersByAuthority_NotByStoredValue(
        FolderPermission held, FolderPermission required, bool expected) =>
        Assert.Equal(expected, held.AtLeast(required));

    [Fact]
    public void Max_PrefersTheStrongerGrant_NotTheLargerNumber()
    {
        // Someone granted Author on a report and Manage on its folder must end up with Manage.
        // An integer max would give them Author and quietly take Manage away.
        Assert.Equal(FolderPermission.Manage,
            FolderPermissions.Max(FolderPermission.Author, FolderPermission.Manage));
        Assert.Equal(FolderPermission.Manage,
            FolderPermissions.Max(FolderPermission.Manage, FolderPermission.Author));
        Assert.Equal(FolderPermission.Author,
            FolderPermissions.Max(FolderPermission.Author, FolderPermission.Execute));
    }

    [Fact]
    public void NoProductionCode_ComparesPermissionsOrdinallyAgainstAnythingAboveRead()
    {
        var root = Path.Combine(RepoRoot(), "src");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                // `>= FolderPermission.Read` is the one safe ordinal form: Read is 0, so it is true
                // for every value and stays true whatever is appended. It survives in a handful of
                // EF queries where an extension method could not be translated to SQL.
                if (!Regex.IsMatch(line, @"(>=|<=|<|>)\s*FolderPermission\.(Execute|Manage|Author)")
                    && !Regex.IsMatch(line, @"FolderPermission\.(Execute|Manage|Author)\s*(>=|<=|<|>)"))
                    continue;

                offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: {line.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "These compare FolderPermission ordinally. The enum's numeric order is storage, not "
            + "authority — Author is stored above Manage — so `>=` grants Author everything Manage "
            + "has. Use AtLeast()/Rank() from FolderPermissions instead:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The same rule where both sides are variables, which the check above cannot see.
    ///
    /// <para>This is not hypothetical tightening. <c>FolderPermissionService.HasPermissionAsync</c>
    /// shipped with <c>effective.Value &gt;= required</c> — no literal anywhere on the line, so the
    /// literal-matching check read it as clean — and it let an <c>Author</c> grant publish a new
    /// report into a folder, the one act Author is defined not to permit. A guard that only catches
    /// the obvious spelling of a mistake is worth less than it appears, because the obvious spelling
    /// is the one people already avoid.</para>
    /// </summary>
    [Fact]
    public void NoProductionCode_ComparesPermissionVariablesOrdinally()
    {
        var root = Path.Combine(RepoRoot(), "src");
        // The small, stable vocabulary these comparisons are actually written in.
        const string Names = @"(effective|required|permission|perm|held|granted|existing|current|best)\w*";
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            var text = File.ReadAllText(file);
            // Only files that deal in folder permissions at all; DatasetPermission is a separate
            // enum whose storage order *is* its authority order, so `>=` is correct there.
            if (!text.Contains("FolderPermission", StringComparison.Ordinal)) continue;

            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                if (!Regex.IsMatch(line, $@"\b{Names}(\.Value)?\s*(>=|<=|<|>)\s*\b{Names}",
                        RegexOptions.IgnoreCase))
                    continue;

                offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: {line.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "These compare permission values ordinally through variables. Storage order is not "
            + "authority order, so the comparison is wrong even with no literal in sight. Use "
            + "AtLeast():\n  " + string.Join("\n  ", offenders));
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
