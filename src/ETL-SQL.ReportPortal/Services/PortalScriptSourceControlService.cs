using System.Diagnostics;
using System.Security.Claims;
using System.Text.RegularExpressions;
using ETL_SQL.Core.Common;

namespace ETL_SQL.ReportPortal.Services;

public sealed record ScriptSourceControlCommit(string? Revision, bool Committed);

/// <summary>
/// Optional local-git write-back for source-controlled portal scripts.
/// </summary>
public sealed partial class PortalScriptSourceControlService(PortalConfig config)
{
    public bool IsEnabled =>
        config.SourceControl.Enabled
        && string.Equals(config.SourceControl.Provider, "Git", StringComparison.OrdinalIgnoreCase);

    public async Task<string?> GetCurrentRevisionAsync(CancellationToken ct = default)
    {
        if (!IsEnabled) return null;
        var result = await RunGitAsync(["rev-parse", "HEAD"], ct);
        return result.ExitCode == 0 ? result.Stdout.Trim() : null;
    }

    public async Task<ScriptSourceControlCommit> CommitScriptAsync(
        string scriptKey,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        if (!IsEnabled)
            return new ScriptSourceControlCommit(null, false);

        var relPath = ResolveRepositoryRelativeScriptPath(scriptKey);
        var add = await RunGitAsync(["add", "--", relPath], ct);
        add.EnsureSuccess("stage script");

        var diff = await RunGitAsync(["diff", "--cached", "--quiet", "--", relPath], ct);
        if (diff.ExitCode == 0)
            return new ScriptSourceControlCommit(await GetCurrentRevisionAsync(ct), false);
        if (diff.ExitCode != 1)
            diff.EnsureSuccess("check staged script changes");

        var message = $"Update portal report script {scriptKey}";
        var commit = await RunGitAsync(["commit", "-m", message, "--", relPath], ct, BuildIdentityEnvironment(user));
        commit.EnsureSuccess("commit script");

        if (config.SourceControl.PushOnSave)
        {
            var args = string.IsNullOrWhiteSpace(config.SourceControl.Branch)
                ? new[] { "push", config.SourceControl.Remote }
                : ["push", config.SourceControl.Remote, config.SourceControl.Branch];
            (await RunGitAsync(args, ct)).EnsureSuccess("push script commit");
        }

        return new ScriptSourceControlCommit(await GetCurrentRevisionAsync(ct), true);
    }

    public void ValidateScriptTextForCommit(string scriptText)
    {
        if (!IsEnabled) return;

        var match = PlaintextSecretOptionRegex().Match(scriptText ?? string.Empty);
        if (match.Success)
            throw new InvalidOperationException(
                $"Source-controlled scripts must not contain raw {match.Groups["key"].Value} values. Use SECRET:name or ENC:... references.");
    }

    public bool IsBaseRevisionCurrent(string? baseRevision, string? currentRevision)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(baseRevision) || string.IsNullOrWhiteSpace(currentRevision))
            return true;
        return string.Equals(baseRevision.Trim(), currentRevision.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveRepositoryRelativeScriptPath(string scriptKey)
    {
        if (string.IsNullOrWhiteSpace(config.SourceControl.RepositoryRoot))
            throw new InvalidOperationException("Portal:SourceControl:RepositoryRoot is required when Git source control is enabled.");

        var repoRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(config.SourceControl.RepositoryRoot));
        var scriptRoot = Path.GetFullPath(config.ScriptRootPath);
        var fullScript = Path.GetFullPath(Path.Combine(scriptRoot, scriptKey.Replace('/', Path.DirectorySeparatorChar)));

        if (!IsWithinRoot(repoRoot, fullScript))
            throw new InvalidOperationException("Portal:ScriptRootPath must be inside Portal:SourceControl:RepositoryRoot when Git source control is enabled.");

        return Path.GetRelativePath(repoRoot, fullScript).Replace('\\', '/');
    }

    private static bool IsWithinRoot(string root, string path)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedRoot, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
    }

    private IReadOnlyDictionary<string, string> BuildIdentityEnvironment(ClaimsPrincipal user)
    {
        var name = user.FindFirstValue(ClaimTypes.Name)
            ?? user.Identity?.Name
            ?? config.SourceControl.CommitterName;
        return new Dictionary<string, string>
        {
            ["GIT_AUTHOR_NAME"] = name,
            ["GIT_AUTHOR_EMAIL"] = config.SourceControl.CommitterEmail,
            ["GIT_COMMITTER_NAME"] = config.SourceControl.CommitterName,
            ["GIT_COMMITTER_EMAIL"] = config.SourceControl.CommitterEmail
        };
    }

    private async Task<GitResult> RunGitAsync(
        IReadOnlyList<string> args,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var repoRoot = Path.GetFullPath(config.SourceControl.RepositoryRoot);
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        if (environment != null)
            foreach (var (key, value) in environment)
                start.Environment[key] = value;

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start git.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return new GitResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private sealed record GitResult(int ExitCode, string Stdout, string Stderr)
    {
        public void EnsureSuccess(string operation)
        {
            if (ExitCode == 0) return;
            var detail = SecretRedactor.Redact(string.IsNullOrWhiteSpace(Stderr) ? Stdout : Stderr);
            throw new InvalidOperationException($"Git {operation} failed: {detail}");
        }
    }

    [GeneratedRegex(@"\b(?<key>PASSWORD|API_KEY|TOKEN|SECRET|CLIENT_SECRET)\s*=\s*'(?!(?:SECRET:|ENC:))[^']+'", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex PlaintextSecretOptionRegex();
}
