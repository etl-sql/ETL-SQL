using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ETL_SQL.WorkstationEditor;

public sealed class WorkstationGitService(WorkstationWorkspace workspace)
{
    private static readonly Regex RevisionPattern = new("^[0-9a-fA-F]{7,40}$", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> CommitExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".etlsql",
        ".rptsql"
    };

    public GitStatusResponse GetStatus()
    {
        var root = workspace.Root;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return new GitStatusResponse(null, [], [], [], false);

        var (branchExit, branch, _) = RunGitCommand(root, "rev-parse", "--abbrev-ref", "HEAD");
        if (branchExit != 0 || string.IsNullOrWhiteSpace(branch))
            return new GitStatusResponse(null, [], [], [], false);

        var (statusExit, statusOutput, _) = RunGitCommand(root, "status", "--porcelain");
        if (statusExit != 0)
            return new GitStatusResponse(null, [], [], [], false);

        var modified = new List<string>();
        var untracked = new List<string>();
        var staged = new List<string>();

        foreach (var line in statusOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 4) continue;
            char x = line[0];
            char y = line[1];
            string file = line[3..].Trim();

            if (x is 'A' or 'M' or 'R' or 'C') staged.Add(file);
            if (y == 'M') modified.Add(file);
            else if (x == '?' && y == '?') untracked.Add(file);
            else if (x == ' ' && y == 'M') modified.Add(file);
        }

        return new GitStatusResponse(branch.Trim(), modified.Distinct().ToList(), untracked.Distinct().ToList(), staged.Distinct().ToList(), true);
    }

    public GitCommitResponse Commit(GitCommitRequest request)
    {
        if (workspace.ReadOnly)
            throw new InvalidOperationException("Workspace is read-only.");

        var root = workspace.Root;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return new GitCommitResponse(false, null, "Workspace directory does not exist.");

        var comment = request.Comment?.Trim();
        if (string.IsNullOrWhiteSpace(comment))
            return new GitCommitResponse(false, null, "Commit message cannot be empty.");

        var filesToStage = GetCommitCandidateFiles().ToList();
        if (filesToStage.Count == 0)
            return new GitCommitResponse(false, null, "No editable script changes to commit.");

        var addArgs = new List<string> { "add", "--" };
        addArgs.AddRange(filesToStage);

        // 1. Stage only files the Workstation editor can own. The workspace may be a full repo with
        // local debug output, generated artifacts, or secrets beside the scripts; a script editor
        // commit button must not sweep those into source control.
        var (addExit, _, addError) = RunGitCommand(root, addArgs.ToArray());
        if (addExit != 0)
        {
            string errorMsg = string.IsNullOrWhiteSpace(addError) ? "git add failed." : addError.Trim();
            return new GitCommitResponse(false, null, errorMsg);
        }

        var commitArgs = new List<string> { "commit", "-m", comment, "--" };
        commitArgs.AddRange(filesToStage);

        // 2. Commit the same pathspec, so unrelated changes staged before the editor action do not
        // slip into the Workstation-generated commit.
        var (commitExit, commitOutput, commitError) = RunGitCommand(root, commitArgs.ToArray());
        string combinedOutput = $"{commitOutput}\n{commitError}";

        if (combinedOutput.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase))
            return new GitCommitResponse(false, null, "Nothing to commit.");

        if (commitExit != 0)
        {
            string errorMsg = !string.IsNullOrWhiteSpace(commitError) ? commitError.Trim() : (!string.IsNullOrWhiteSpace(commitOutput) ? commitOutput.Trim() : "git commit failed.");
            return new GitCommitResponse(false, null, errorMsg);
        }

        // 3. Resolve HEAD revision
        var (revExit, revOutput, _) = RunGitCommand(root, "rev-parse", "--short", "HEAD");
        if (revExit != 0 || string.IsNullOrWhiteSpace(revOutput))
            return new GitCommitResponse(false, null, "Could not resolve commit HEAD revision.");

        return new GitCommitResponse(true, revOutput.Trim(), "Committed successfully.");
    }

    public GitHistoryResponse GetHistory(string? path, int limit = 20)
    {
        var root = workspace.Root;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return new GitHistoryResponse(false, []);

        var relativePath = ResolveGitPath(path);
        var boundedLimit = Math.Clamp(limit, 1, 50).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var (exitCode, output, _) = RunGitCommand(
            root,
            "log",
            $"--max-count={boundedLimit}",
            "--format=%H%x1f%h%x1f%aI%x1f%an%x1f%s",
            "--",
            relativePath);
        if (exitCode != 0)
            return new GitHistoryResponse(false, []);

        var entries = new List<GitHistoryEntry>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('\u001f');
            if (fields.Length < 5 || !RevisionPattern.IsMatch(fields[0])) continue;
            entries.Add(new GitHistoryEntry(fields[0], fields[1], fields[2], fields[3], string.Join('\u001f', fields[4..])));
        }

        return new GitHistoryResponse(true, entries);
    }

    public GitDiffResponse GetDiff(GitDiffRequest request)
    {
        var root = workspace.Root;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            throw new InvalidOperationException("Workspace directory does not exist.");

        var relativePath = ResolveGitPath(request.Path);
        var requestedRevision = string.IsNullOrWhiteSpace(request.Revision) ? "HEAD" : request.Revision.Trim();
        if (!string.Equals(requestedRevision, "HEAD", StringComparison.OrdinalIgnoreCase)
            && !RevisionPattern.IsMatch(requestedRevision))
        {
            throw new ArgumentException("The Git revision is invalid.", nameof(request));
        }

        var (revisionExit, fullRevision, _) = RunGitCommand(root, "rev-parse", "--verify", requestedRevision);
        if (revisionExit != 0 || string.IsNullOrWhiteSpace(fullRevision))
            throw new ArgumentException("The requested Git revision does not exist.", nameof(request));

        var resolvedRevision = fullRevision.Trim();
        var (showExit, baseline, showError) = RunGitCommand(root, "show", $"{resolvedRevision}:{relativePath}");
        if (showExit != 0)
        {
            // A valid revision can legitimately predate a newly added script. Treat that as an
            // empty baseline; other failures remain visible without returning raw git diagnostics.
            var (treeExit, _, _) = RunGitCommand(root, "cat-file", "-e", $"{resolvedRevision}^{{tree}}");
            if (treeExit != 0 || (!showError.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
                && !showError.Contains("exists on disk", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Git could not read the requested script revision.");
            }
            baseline = string.Empty;
        }

        var shortRevision = resolvedRevision[..Math.Min(8, resolvedRevision.Length)];
        return new GitDiffResponse(
            relativePath,
            resolvedRevision,
            string.Equals(requestedRevision, "HEAD", StringComparison.OrdinalIgnoreCase) ? $"HEAD {shortRevision}" : shortRevision,
            baseline,
            request.Content ?? string.Empty);
    }

    private string ResolveGitPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A script path is required.", nameof(path));

        var fullPath = workspace.ResolveEditablePath(path);
        return Path.GetRelativePath(workspace.Root, fullPath).Replace('\\', '/');
    }

    private IEnumerable<string> GetCommitCandidateFiles()
    {
        var root = workspace.Root;
        var (statusExit, statusOutput, _) = RunGitCommand(root, "status", "--porcelain", "--untracked-files=all", "--");
        if (statusExit != 0) yield break;

        foreach (var line in statusOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var file = ParseStatusPath(line);
            if (string.IsNullOrWhiteSpace(file)) continue;
            if (!CommitExtensions.Contains(Path.GetExtension(file))) continue;

            string fullPath;
            try
            {
                fullPath = workspace.ResolveEditablePath(file);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException)
            {
                continue;
            }

            if (File.Exists(fullPath))
                yield return file.Replace('\\', '/');
        }
    }

    private static string? ParseStatusPath(string line)
    {
        if (line.Length < 4) return null;
        var file = line[3..].Trim();
        var renameSeparator = file.IndexOf(" -> ", StringComparison.Ordinal);
        if (renameSeparator >= 0)
            file = file[(renameSeparator + 4)..].Trim();
        return file.Trim('"');
    }

    internal static (int ExitCode, string Output, string Error) RunGitCommand(string workingDir, params string[] arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
            {
                psi.ArgumentList.Add(argument);
            }

            using var process = Process.Start(psi);
            if (process == null) return (-1, string.Empty, "Failed to start git process.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(3000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return (-1, string.Empty, "git command timed out.");
            }

            string output = outputTask.GetAwaiter().GetResult();
            string error = errorTask.GetAwaiter().GetResult();
            return (process.ExitCode, output, error);
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }
    }
}

public sealed record GitStatusResponse(
    string? Branch,
    IReadOnlyList<string> Modified,
    IReadOnlyList<string> Untracked,
    IReadOnlyList<string> Staged,
    bool IsGitRepository);

public sealed record GitCommitRequest(string? Comment);

public sealed record GitCommitResponse(bool Committed, string? SourceRevision, string Message);

public sealed record GitHistoryResponse(bool IsGitRepository, IReadOnlyList<GitHistoryEntry> Entries);

public sealed record GitHistoryEntry(
    string Revision,
    string ShortRevision,
    string AuthoredAt,
    string Author,
    string Subject);

public sealed record GitDiffRequest(string? Path, string? Content, string? Revision);

public sealed record GitDiffResponse(
    string Path,
    string Revision,
    string BaselineLabel,
    string BaselineContent,
    string WorkingContent);
