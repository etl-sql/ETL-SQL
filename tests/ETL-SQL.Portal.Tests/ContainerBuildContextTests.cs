using System.Text.RegularExpressions;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Keeps <c>.dockerignore</c> and the Dockerfiles honest about each other.
///
/// <para>Two failures are possible and neither announces itself. Excluding a path a Dockerfile
/// copies breaks the image build, but only for whoever builds a container next — which on a
/// repository where most work is local can be days later. Failing to exclude a path nothing copies
/// costs nothing visible at all: the build just quietly ships the whole tree to the daemon first,
/// and <c>tests/</c> alone is about 14 GB.</para>
///
/// <para>So both directions are asserted from the files themselves rather than from a list somebody
/// maintains.</para>
/// </summary>
[Trait("Category", "Portal")]
public sealed class ContainerBuildContextTests
{
    private static readonly string[] Dockerfiles =
    [
        Path.Combine("src", "ETL-SQL.Portal", "Dockerfile"),
        Path.Combine("src", "ETL-SQL.Orchestrator.Service", "Dockerfile"),
    ];

    [Fact]
    public void NothingADockerfileCopies_IsExcludedFromTheBuildContext()
    {
        var root = RepoRoot();
        var ignored = IgnoredTopLevelPaths(root);
        var violations = new List<string>();

        foreach (var dockerfile in Dockerfiles)
        {
            var path = Path.Combine(root, dockerfile);
            Assert.True(File.Exists(path), $"Dockerfile not found at {dockerfile}");

            foreach (var copied in CopiedContextPaths(File.ReadAllText(path)))
            {
                var top = copied.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (top is null) continue;
                if (ignored.Contains(top))
                    violations.Add($"{dockerfile} copies '{copied}', but '{top}/' is in .dockerignore");
            }
        }

        Assert.True(violations.Count == 0,
            "These images would fail to build because their source is excluded from the context:\n  "
            + string.Join("\n  ", violations));
    }

    [Fact]
    public void TheLargestDirectoriesNoDockerfileCopies_AreExcluded()
    {
        // tests/ is the one that actually costs something. Naming it explicitly means removing the
        // exclusion is a decision someone makes rather than a line that quietly disappears in a
        // merge, and the cost is invisible when it does.
        var root = RepoRoot();
        var ignored = IgnoredTopLevelPaths(root);

        var copied = Dockerfiles
            .Select(d => File.ReadAllText(Path.Combine(root, d)))
            .SelectMany(CopiedContextPaths)
            .Select(p => p.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .Where(p => p is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

        foreach (var directory in new[] { "tests" })
        {
            Assert.False(copied.Contains(directory),
                $"'{directory}/' is now copied by a Dockerfile; the exclusion below needs revisiting.");
            Assert.Contains(directory, ignored);
        }
    }

    /// <summary>Top-level paths <c>.dockerignore</c> excludes, normalized to bare directory names.</summary>
    private static HashSet<string> IgnoredTopLevelPaths(string root)
    {
        var path = Path.Combine(root, ".dockerignore");
        Assert.True(File.Exists(path), ".dockerignore is missing.");

        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('!')) continue;
            // Only whole top-level directories matter here; glob and file patterns cannot hide a
            // COPY source from this check because a Dockerfile names its sources by directory.
            if (line.Contains('*') || !line.EndsWith('/')) continue;
            ignored.Add(line.TrimEnd('/'));
        }
        return ignored;
    }

    /// <summary>
    /// Context-relative sources of every <c>COPY</c>, skipping <c>--from</c> stages, which read
    /// from an earlier image rather than from the build context.
    /// </summary>
    private static IEnumerable<string> CopiedContextPaths(string dockerfile)
    {
        foreach (Match match in Regex.Matches(dockerfile, @"^\s*COPY\s+(?<rest>.+)$", RegexOptions.Multiline))
        {
            var rest = match.Groups["rest"].Value.Trim();
            if (rest.StartsWith("--from", StringComparison.OrdinalIgnoreCase)) continue;

            // Both forms appear: COPY ["a", "b"] and COPY a b
            var tokens = rest.StartsWith('[')
                ? Regex.Matches(rest, "\"(?<v>[^\"]+)\"").Select(m => m.Groups["v"].Value).ToList()
                : [.. rest.Split(' ', StringSplitOptions.RemoveEmptyEntries)];

            // The last token is the destination inside the image.
            for (var i = 0; i < tokens.Count - 1; i++)
            {
                var source = tokens[i].Replace('\\', '/').TrimStart('.', '/');
                if (source.Length > 0) yield return source;
            }
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
