using System.Diagnostics;

namespace ETL_SQL.WorkstationEditor;

public sealed class WorkstationGitService(WorkstationWorkspace workspace)
{
    public GitStatusResponse GetStatus()
    {
        var root = workspace.Root;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return new GitStatusResponse(null, [], [], [], false);

        string? branch = RunGitCommand(root, "rev-parse --abbrev-ref HEAD");
        if (string.IsNullOrWhiteSpace(branch))
            return new GitStatusResponse(null, [], [], [], false);

        string statusOutput = RunGitCommand(root, "status --porcelain") ?? string.Empty;
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

        // Stage all changes
        RunGitCommand(root, "add -A");

        // Commit
        string safeComment = comment.Replace("\"", "\\\"");
        string commitOutput = RunGitCommand(root, $"commit -m \"{safeComment}\"") ?? string.Empty;

        if (commitOutput.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase))
            return new GitCommitResponse(false, null, "Nothing to commit.");

        string? rev = RunGitCommand(root, "rev-parse --short HEAD");
        return new GitCommitResponse(true, rev?.Trim(), "Committed successfully.");
    }

    private static string? RunGitCommand(string workingDir, string arguments)
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
            if (process == null) return null;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            return process.ExitCode == 0 ? output : output;
        }
        catch
        {
            return null;
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
