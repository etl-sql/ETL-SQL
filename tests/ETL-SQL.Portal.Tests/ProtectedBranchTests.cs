using System.Diagnostics;
using System.Security.Claims;
using ETL_SQL.Portal.Services;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Protected branches, which are what the draft-approval workflow is <em>for</em>.
///
/// <para>Protecting a branch without a review path only blocks people; providing a review path
/// without protecting anything only asks nicely. Together they mean a change reaching a protected
/// branch has been read by someone other than its author — and because the reviewer goes into a
/// commit trailer, that fact survives outside the Portal's database, where anyone reading
/// <c>git log</c> can see it.</para>
///
/// <para>The pattern-matching cases are exhaustive on purpose. A protection rule that quietly fails
/// to match is indistinguishable from no protection: commits succeed, nothing errors, and the
/// branch is unguarded until someone happens to notice.</para>
/// </summary>
[Trait("Category", "Portal")]
[Trait("Category", "Smoke.Security")]
public sealed class ProtectedBranchTests : IDisposable
{
    private readonly string root =
        Path.Combine(Path.GetTempPath(), "etlsql-protected-" + Guid.NewGuid().ToString("N"));

    // ── Pattern matching ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("main", "main", true)]
    [InlineData("main", "MAIN", true)]
    [InlineData("main", "maintenance", false)]
    [InlineData("main", "feature/main", false)]
    [InlineData("release/*", "release/v0.18.0", true)]
    [InlineData("release/*", "release/", true)]
    [InlineData("release/*", "released", false)]
    [InlineData("release/*", "hotfix/release/x", false)]
    public void PatternMatching_IsExactUnlessItEndsInAStar(
        string pattern, string branch, bool expected) =>
        Assert.Equal(expected, Service(pattern).IsProtectedBranch(branch));

    [Fact]
    public void WithNothingConfigured_NoBranchIsProtected()
    {
        // The default. A deployment that has not chosen protected branches behaves exactly as it did
        // before this existed.
        var service = Service();
        Assert.False(service.IsProtectedBranch("main"));
        Assert.False(service.IsProtectedBranch("release/v1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnUnknownBranch_IsNotTreatedAsProtected(string? branch)
    {
        // Detached HEAD, or git unavailable. Failing open here is deliberate and narrow: treating
        // "I could not tell" as "protected" would block every commit on a machine where the branch
        // cannot be read, turning a diagnostic gap into an outage.
        Assert.False(Service("main", "release/*").IsProtectedBranch(branch));
    }

    [Fact]
    public void BlankPatterns_AreIgnoredRatherThanMatchingEverything()
    {
        // An empty entry left in configuration is a typo, not an instruction to protect everything.
        // Reading it as `*` would lock a deployment out of committing at all.
        var service = Service("", "   ", "main");
        Assert.True(service.IsProtectedBranch("main"));
        Assert.False(service.IsProtectedBranch("feature/x"));
    }

    [Fact]
    public void ReviewedProvenance_RequiresAnActualReviewer()
    {
        // IsReviewed drives the whole protection, so a blank reviewer must not read as reviewed —
        // otherwise a caller that resolved no approver would still get through.
        Assert.False(CommitProvenance.Unreviewed.IsReviewed);
        Assert.False(new CommitProvenance(null, "sha256:abc").IsReviewed);
        Assert.False(new CommitProvenance("", "sha256:abc").IsReviewed);
        Assert.False(new CommitProvenance("   ", "sha256:abc").IsReviewed);
        Assert.True(new CommitProvenance("dana", "sha256:abc").IsReviewed);
    }

    // ── Against a real repository ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnUnreviewedCommitToAProtectedBranch_IsRefused()
    {
        var script = await InitRepositoryAsync("main");
        var service = Service("main");
        var before = await service.GetCurrentRevisionAsync();

        await File.WriteAllTextAsync(script, "SELECT 2 AS Value;");

        var refused = await Assert.ThrowsAsync<ProtectedBranchException>(
            () => service.CommitScriptAsync("sales.rptsql", Principal()));
        Assert.Contains("protected branch", refused.Message);

        // Nothing was committed. A refusal that still moved HEAD would be worse than no refusal,
        // because the operator would believe the branch was guarded.
        Assert.Equal(before, await service.GetCurrentRevisionAsync());
    }

    [Fact]
    public async Task AReviewedCommitToAProtectedBranch_LandsAndCarriesTheReviewerInTheHistory()
    {
        var script = await InitRepositoryAsync("main");
        var service = Service("main");
        await File.WriteAllTextAsync(script, "SELECT 2 AS Value;");

        var result = await service.CommitScriptAsync(
            "sales.rptsql", Principal(), new CommitProvenance("dana", "sha256:deadbeef"));

        Assert.True(result.Committed);

        // The trailer is the point: the review outlives the Portal's database, so someone auditing
        // the branch a year later does not need the Portal to answer "who approved this?".
        var message = await GitOutputAsync("log", "-1", "--pretty=%B");
        Assert.Contains("Reviewed-by: dana", message);
        Assert.Contains("Script-hash: sha256:deadbeef", message);
    }

    [Fact]
    public async Task AnUnreviewedCommitToAnUnprotectedBranch_IsUnaffected()
    {
        var script = await InitRepositoryAsync("feature/experiment");
        var service = Service("main", "release/*");
        await File.WriteAllTextAsync(script, "SELECT 2 AS Value;");

        // Non-vacuous counterpart to the refusal above: the same unreviewed commit, the same
        // configuration, a different branch. Protection has to be about the branch, not a blanket
        // requirement that would make ordinary work impossible.
        var result = await service.CommitScriptAsync("sales.rptsql", Principal());
        Assert.True(result.Committed);

        var message = await GitOutputAsync("log", "-1", "--pretty=%B");
        Assert.DoesNotContain("Reviewed-by:", message);
    }

    [Fact]
    public async Task ProtectionFollowsTheCheckedOutBranch_NotTheConfiguredOne()
    {
        var script = await InitRepositoryAsync("main");
        await GitAsync("checkout", "-b", "release/v0.18.0");
        var service = Service("release/*");
        await File.WriteAllTextAsync(script, "SELECT 3 AS Value;");

        // The branch a commit lands on is whatever is checked out, so that is what protection has
        // to read. Matching against the configured push branch would guard the wrong thing.
        await Assert.ThrowsAsync<ProtectedBranchException>(
            () => service.CommitScriptAsync("sales.rptsql", Principal()));
    }

    // ── fixtures ────────────────────────────────────────────────────────────────────────────────

    private async Task<string> InitRepositoryAsync(string branch)
    {
        Directory.CreateDirectory(root);
        var reports = Path.Combine(root, "Reports");
        Directory.CreateDirectory(reports);
        var script = Path.Combine(reports, "sales.rptsql");
        await File.WriteAllTextAsync(script, "SELECT 1 AS Value;");

        await GitAsync("init", "-b", branch);
        await GitAsync("config", "user.name", "Test User");
        await GitAsync("config", "user.email", "test@example.local");
        await GitAsync("add", ".");
        await GitAsync("commit", "-m", "initial");
        return script;
    }

    private PortalScriptSourceControlService Service(params string[] protectedBranches) =>
        new(new PortalConfig
        {
            ScriptRootPath = Path.Combine(root, "Reports"),
            SourceControl = new PortalSourceControlConfig
            {
                Enabled = true,
                Provider = "Git",
                RepositoryRoot = root,
                ProtectedBranches = protectedBranches,
                CommitterName = "Portal Bot",
                CommitterEmail = "portal@example.local",
            }
        });

    private static ClaimsPrincipal Principal() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "author")], "Test"));

    private Task GitAsync(params string[] args) => GitOutputAsync(args);

    private async Task<string> GitOutputAsync(params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', args)} failed: {stderr}");
        return stdout;
    }

    public void Dispose()
    {
        if (!Directory.Exists(root)) return;
        try
        {
            // Git marks objects read-only, which blocks a plain recursive delete on Windows.
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
