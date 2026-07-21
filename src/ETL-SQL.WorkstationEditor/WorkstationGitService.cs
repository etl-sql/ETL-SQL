using System.Diagnostics;

namespace ETL_SQL.WorkstationEditor;

public sealed class WorkstationGitService(WorkstationWorkspace workspace)
{
    public GitStatusResponse GetStatus()
    {
        var root = workspace.Root;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return new GitStatusResponse(null, [], [], [], false);

        var (branchExit, branch, _) = RunGitCommand(root, "rev-parse --abbrev-ref HEAD");
        if (branchExit != 0 || string.IsNullOrWhiteSpace(branch))
            return new GitStatusResponse(null, [], [], [], false);

        var (statusExit, statusOutput, _) = RunGitCommand(root, "status --porcelain");
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

        // 1. Stage all changes
        var (addExit, _, addError) = RunGitCommand(root, "add -A");
        if (addExit != 0)
        {
            string errorMsg = string.IsNullOrWhiteSpace(addError) ? "git add failed." : addError.Trim();
            return new GitCommitResponse(false, null, errorMsg);
        }

        // 2. Commit
        string safeComment = comment.Replace("\"", "\\\"");
        var (commitExit, commitOutput, commitError) = RunGitCommand(root, $"commit -m \"{safeComment}\"");
        string combinedOutput = $"{commitOutput}\n{commitError}";

        if (combinedOutput.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase))
            return new GitCommitResponse(false, null, "Nothing to commit.");

        if (commitExit != 0)
        {
            string errorMsg = !string.IsNullOrWhiteSpace(commitError) ? commitError.Trim() : (!string.IsNullOrWhiteSpace(commitOutput) ? commitOutput.Trim() : "git commit failed.");
            return new GitCommitResponse(false, null, errorMsg);
        }

        // 3. Resolve HEAD revision
        var (revExit, revOutput, _) = RunGitCommand(root, "rev-parse --short HEAD");
        if (revExit != 0 || string.IsNullOrWhiteSpace(revOutput))
            return new GitCommitResponse(false, null, "Could not resolve commit HEAD revision.");

        return new GitCommitResponse(true, revOutput.Trim(), "Committed successfully.");
    }

    private static (int ExitCode, string Output, string Error) RunGitCommand(string workingDir, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return (-1, string.Empty, "Failed to start git process.");
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit(3000);
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
