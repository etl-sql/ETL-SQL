using System.Diagnostics;

namespace ETL_SQL.Tests.Docs;

/// <summary>
/// Every shared browser/runtime asset must be pinned to LF in <c>.gitattributes</c>.
///
/// <para>The canonical files under <c>ETL-SQL.ReportRuntime/Resources/Shared</c> are copied
/// byte-for-byte to host copies, and <c>sync-assets.js -Check</c> gates on them being identical. On
/// a Windows checkout with <c>core.autocrlf=true</c> an unpinned text file becomes CRLF, the
/// comparison stops matching, and the gate fails — <b>only in CI</b>, which is the expensive place
/// to find out.</para>
///
/// <para><c>.gitattributes</c> already explains this in a comment. The comment did not stop
/// <c>feedback.js</c> being added to the shared set without a pin, so the whole CI run failed on a
/// file whose content was correct. A comment describes the rule; this enforces it.</para>
/// </summary>
public sealed class SharedAssetLineEndingPinTests
{
    [Fact]
    public void EverySharedRuntimeAsset_IsPinnedToLf()
    {
        var root = RepoRoot();
        var sharedDir = Path.Combine(
            root, "src", "ETL-SQL.ReportRuntime", "Resources", "Shared");
        Assert.True(Directory.Exists(sharedDir), $"Shared asset root not found: {sharedDir}");

        var assets = Directory
            .EnumerateFiles(sharedDir, "*", SearchOption.AllDirectories)
            .ToList();
        Assert.NotEmpty(assets);

        var unpinned = new List<string>();
        foreach (var asset in assets)
        {
            var relative = Path.GetRelativePath(root, asset).Replace('\\', '/');
            if (!GitEolIsLf(root, relative))
                unpinned.Add(relative);
        }

        Assert.True(unpinned.Count == 0,
            $"{unpinned.Count} shared asset(s) are not pinned to LF in .gitattributes:\n  "
            + string.Join("\n  ", unpinned)
            + "\n\nA Windows CI checkout converts these to CRLF, the canonical and host copies stop "
            + "being byte-identical, and `sync-assets.js -Check` fails the build for a file whose "
            + "content is correct. Add a `<name> text eol=lf` line beside the others.");
    }

    /// <summary>Asks git itself, so the answer reflects the real attribute resolution.</summary>
    private static bool GitEolIsLf(string repoRoot, string relativePath)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"check-attr eol -- \"{relativePath}\"",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });

        if (process is null) return true; // no git available: do not fail the suite over tooling
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(20_000);

        return output.TrimEnd().EndsWith(": lf", StringComparison.Ordinal);
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
