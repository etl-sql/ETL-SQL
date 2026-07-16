using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.RegularExpressions;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.ReportPortal.Services;

public sealed record ScriptSourceControlCommit(string? Revision, bool Committed);

/// <summary>
/// Optional local-git write-back for source-controlled portal scripts.
/// </summary>
public sealed partial class PortalScriptSourceControlService(PortalConfig config)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RepositoryGates = new(StringComparer.OrdinalIgnoreCase);

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

        return await WithRepositoryLockAsync(async () =>
        {
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
        }, ct);
    }

    public void ValidateScriptTextForCommit(string scriptText)
    {
        if (!IsEnabled) return;

        var text = scriptText ?? string.Empty;
        ValidateParsedConnectionDefinitions(text);

        var match = PlaintextSecretOptionRegex().Match(text);
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

    private async Task<T> WithRepositoryLockAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        var repoRoot = Path.GetFullPath(config.SourceControl.RepositoryRoot);
        var gate = RepositoryGates.GetOrAdd(repoRoot, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            await using var fileLock = await AcquireRepositoryFileLockAsync(repoRoot, ct);
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<FileStream> AcquireRepositoryFileLockAsync(string repoRoot, CancellationToken ct)
    {
        var gitDirectory = Path.Combine(repoRoot, ".git");
        var lockDirectory = Directory.Exists(gitDirectory) ? gitDirectory : repoRoot;
        Directory.CreateDirectory(lockDirectory);
        var lockPath = Path.Combine(lockDirectory, "etlsql-portal-source-control.lock");

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
            }
        }
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

    private static void ValidateParsedConnectionDefinitions(string scriptText)
    {
        Script script;
        try
        {
            var tokens = new Lexer(scriptText).Tokenize();
            script = new CoreParser(tokens, scriptText).Parse();
        }
        catch
        {
            return;
        }

        if (script.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return;

        foreach (var statement in script.Statements)
            ValidateStatement(statement);
    }

    private static void ValidateStatement(Statement statement)
    {
        switch (statement)
        {
            case CreateConnectionStatement create:
                ValidateConnection(create.ConnectionName, create.ConnectionType, create.TargetExpression, create.Options, create);
                break;
            case AlterConnectionStatement alter:
                ValidateConnection(alter.ConnectionName, alter.ConnectionType, alter.TargetExpression, alter.Options, alter);
                break;
            case BlockStatement block:
                foreach (var nested in block.Statements) ValidateStatement(nested);
                break;
            case IfStatement ifStatement:
                ValidateStatement(ifStatement.IfBody);
                if (ifStatement.ElseIfClauses != null)
                    foreach (var elseIf in ifStatement.ElseIfClauses) ValidateStatement(elseIf.Body);
                if (ifStatement.ElseBody != null) ValidateStatement(ifStatement.ElseBody);
                break;
            case WhileStatement whileStatement:
                ValidateStatement(whileStatement.Body);
                break;
            case ForStatement forStatement:
                ValidateStatement(forStatement.Body);
                break;
            case ForeachStatement foreachStatement:
                ValidateStatement(foreachStatement.Body);
                break;
            case TryCatchStatement tryCatch:
                ValidateStatement(tryCatch.TryBody);
                ValidateStatement(tryCatch.CatchBody);
                break;
        }
    }

    private static void ValidateConnection(
        string connectionName,
        string? connectorType,
        Expression? target,
        Dictionary<string, Expression>? options,
        AstNode node)
    {
        if (target is LiteralExpression { Value: string targetValue })
            ValidateConnectionTarget(connectionName, connectorType, targetValue, node);

        if (options == null) return;
        foreach (var (key, expression) in options)
        {
            if (expression is LiteralExpression { Value: string value })
                ValidateConnectionOption(connectionName, connectorType, key, value, node);
        }
    }

    private static void ValidateConnectionOption(
        string connectionName,
        string? connectorType,
        string key,
        string value,
        AstNode node)
    {
        if (IsAllowedSecretReference(value))
            return;

        if (SecretResolvableFields.IsCredential(key)
            || IsAdditionalCredentialKey(key)
            || ContainsCredentialBearingHeader(value)
            || ContainsPlaintextConnectionStringCredential(value, connectorType)
            || ContainsPlaintextUriCredential(value))
        {
            ThrowPlaintextSecret(connectionName, key, node);
        }
    }

    private static void ValidateConnectionTarget(
        string connectionName,
        string? connectorType,
        string value,
        AstNode node)
    {
        if (IsAllowedSecretReference(value)
            || value.TrimStart().StartsWith("SHARED:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (ContainsPlaintextConnectionStringCredential(value, connectorType)
            || ContainsPlaintextUriCredential(value)
            || ContainsCredentialBearingHeader(value))
        {
            ThrowPlaintextSecret(connectionName, "target", node);
        }
    }

    private static bool ContainsPlaintextConnectionStringCredential(string value, string? connectorType)
    {
        foreach (Match match in ConnectionStringCredentialRegex().Matches(value))
        {
            var key = match.Groups["key"].Value;
            var credentialValue = match.Groups["value"].Value.Trim().Trim('\'', '"');
            if ((SecretResolvableFields.IsCredential(key)
                    || SecretResolvableFields.IsConnectorDesignated(key, connectorType)
                    || IsAdditionalCredentialKey(key))
                && !IsAllowedSecretReference(credentialValue))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsPlaintextUriCredential(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || string.IsNullOrWhiteSpace(uri.UserInfo)
            || !uri.UserInfo.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        var password = Uri.UnescapeDataString(uri.UserInfo[(uri.UserInfo.IndexOf(':') + 1)..]);
        return !IsAllowedSecretReference(password);
    }

    private static bool ContainsCredentialBearingHeader(string value)
    {
        foreach (Match match in HeaderCredentialRegex().Matches(value))
        {
            var credentialValue = match.Groups["value"].Value.Trim();
            if (!IsAllowedSecretReference(credentialValue))
                return true;
        }

        return false;
    }

    private static bool IsAllowedSecretReference(string value)
    {
        var trimmed = value.TrimStart();
        return trimmed.StartsWith("SECRET:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("ENC:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAdditionalCredentialKey(string key) =>
        key.Equals("AUTHORIZATION", StringComparison.OrdinalIgnoreCase)
        || key.Equals("AUTHORIZATION_HEADER", StringComparison.OrdinalIgnoreCase)
        || key.Equals("BEARER_TOKEN", StringComparison.OrdinalIgnoreCase)
        || key.Equals("HEADER_AUTHORIZATION", StringComparison.OrdinalIgnoreCase);

    private static void ThrowPlaintextSecret(string connectionName, string key, AstNode node)
    {
        var location = node.Line > 0 ? $" at line {node.Line}, column {node.Column}" : string.Empty;
        throw new InvalidOperationException(
            $"Source-controlled scripts must not contain raw credential values in connection '{connectionName}' field '{key}'{location}. Use SECRET:name or ENC:... references.");
    }

    [GeneratedRegex(@"\b(?<key>PASSWORD|PWD|API_KEY|APIKEY|TOKEN|ACCESS_TOKEN|REFRESH_TOKEN|SECRET|SECRET_KEY|CLIENT_SECRET|SASL_PASSWORD|SAS_TOKEN|ACCOUNT_KEY|PASSPHRASE|PRIVATE_KEY|AUTHORIZATION|BEARER_TOKEN)\s*=\s*'(?!(?:SECRET:|ENC:))[^']+'", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex PlaintextSecretOptionRegex();

    [GeneratedRegex(@"(?:^|;)\s*(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<quote>['""]?)(?<value>[^;'""]+)\k<quote>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ConnectionStringCredentialRegex();

    [GeneratedRegex(@"(?im)^\s*(?:Authorization|Proxy-Authorization|X-API-Key|Api-Key)\s*[:=]\s*(?<value>.+?)\s*$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex HeaderCredentialRegex();
}
