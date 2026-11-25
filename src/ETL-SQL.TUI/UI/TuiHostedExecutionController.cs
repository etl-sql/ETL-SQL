using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Hosted;

namespace ETL_SQL.TUI.UI;

/// <summary>
/// TUI-specific orchestration around the shared <see cref="HostedClientSessionService"/>,
/// <see cref="HostedPipelineClient"/>, and <see cref="HostedRunCoordinator"/> (ST57).
/// </summary>
public sealed class TuiHostedExecutionController : IAsyncDisposable
{
    private readonly Action<string, ConsoleColor> _log;
    private readonly Action<string> _showStatus;
    private readonly Func<string, IReadOnlyList<string>, Task<int>> _choose;
    private readonly Func<string, string, bool, Task<string?>> _prompt;
    private readonly Dictionary<(string Origin, string Tenant, int User, string FilePath), int> _linkedPipelinesByScope = new();

    public HostedClientSessionService Sessions { get; }
    public HostedPipelineClient Client { get; }
    public HostedRunCoordinator Coordinator { get; }

    public bool IsHosted => Sessions.IsHosted;
    public string StatusLabel => Sessions.FormatStatusLabel();

    public TuiHostedExecutionController(
        Action<string, ConsoleColor> log,
        Action<string> showStatus,
        Func<string, IReadOnlyList<string>, Task<int>> choose,
        Func<string, string, bool, Task<string?>> prompt,
        HostedClientSessionService? sessions = null,
        HostedPipelineClient? client = null,
        HostedRunCoordinator? coordinator = null)
    {
        _log = log;
        _showStatus = showStatus;
        _choose = choose;
        _prompt = prompt;
        Sessions = sessions ?? new HostedClientSessionService(HostedClientIds.Tui, HostedClientScopes.TuiDefault);
        Client = client ?? new HostedPipelineClient(Sessions);
        Coordinator = coordinator ?? new HostedRunCoordinator(Sessions, Client);
    }

    public async Task SelectTargetAsync(CancellationToken ct = default)
    {
        var servers = Sessions.ServerRegistry.Load();
        var options = new List<string>
        {
            IsHosted ? "Local (Offline) — switch to local workstation execution" : "Local (Offline) [Active]"
        };

        var actions = new List<Func<Task>>
        {
            async () => await DisconnectAsync(ct)
        };

        var currentOrigin = Sessions.Session?.Server.Origin;
        foreach (var server in servers)
        {
            var isCurrent = string.Equals(currentOrigin, server.Origin, StringComparison.OrdinalIgnoreCase);
            var suffix = isCurrent && Sessions.Session is { } s
                ? $" [Connected: {s.TenantId} · {s.User.Username}]"
                : $" ({server.Origin})";
            options.Add($"Hosted: {server.Label} [{server.Alias}]{suffix}");
            var captured = server;
            actions.Add(async () => await ConnectToServerAsync(captured, ct));
        }

        options.Add("Add Hosted Server…");
        actions.Add(async () =>
        {
            var added = await AddServerInteractiveAsync();
            if (added is not null)
                await ConnectToServerAsync(added, ct);
        });

        if (IsHosted)
        {
            options.Add("Disconnect from Hosted Portal (return to Local)");
            actions.Add(async () => await DisconnectAsync(ct));
        }

        var chosen = await _choose("Execution Target", options);
        if (chosen >= 0 && chosen < actions.Count)
            await actions[chosen]();
    }

    public async Task<HostedServerEntry?> AddServerInteractiveAsync()
    {
        var rawOrigin = await _prompt("Hosted Portal URL (https://... or http://127.0.0.1:...)", "", false);
        if (string.IsNullOrWhiteSpace(rawOrigin))
            return null;

        if (!HostedOriginValidator.TryNormalizeTrustedOrigin(rawOrigin, out var origin, out var host, out var error))
        {
            _log($"[HOSTED] {error}", ConsoleColor.Red);
            _showStatus(error ?? "Invalid Portal URL.");
            return null;
        }

        var label = (await _prompt("Friendly label for this hosted Portal", host, false))?.Trim();
        if (string.IsNullOrWhiteSpace(label))
            return null;

        var defaultAlias = new string(label.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '-').ToArray()).Trim('-');
        if (string.IsNullOrWhiteSpace(defaultAlias))
            defaultAlias = "portal";

        var alias = (await _prompt("Short server alias", defaultAlias, false))?.Trim();
        if (string.IsNullOrWhiteSpace(alias))
            return null;

        var entry = Sessions.ServerRegistry.AddOrUpdate(alias, label, origin);
        _log($"[HOSTED] Saved trusted server '{entry.Alias}' ({entry.Origin}) in user settings.", ConsoleColor.Cyan);
        _showStatus($"Saved hosted server '{entry.Alias}'.");
        return entry;
    }

    public async Task<bool> ConnectToServerAsync(HostedServerEntry server, CancellationToken ct = default)
    {
        _showStatus($"Opening browser to sign in to {server.Label}…");
        try
        {
            var session = await Sessions.SignInAsync(server, ct: ct);
            var msg = $"Connected to {session.Server.Label} ({session.Server.Origin}) | tenant={session.TenantId} | user={session.User.Username}";
            _log($"[HOSTED] {msg}", ConsoleColor.Green);
            _showStatus(Sessions.FormatStatusLabel());
            return true;
        }
        catch (Exception ex)
        {
            var safe = HostedSecretRedactor.Redact(ex.Message, Sessions.GetActiveSecrets());
            _log($"[HOSTED SIGN-IN FAILED] {safe}", ConsoleColor.Red);
            _showStatus($"Hosted sign-in failed: {safe}");
            return false;
        }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await Sessions.DisconnectAsync(ct: ct);
        _log("[HOSTED] Disconnected. Execution target is now Local (Offline).", ConsoleColor.Yellow);
        _showStatus("Local (Offline)");
    }

    /// <summary>
    /// Prepares a hosted pipeline submission on the UI thread (checks pending recovery, session expiry,
    /// runnable pipeline discovery, and dirty-buffer confirmation). Returns a delegate to execute on
    /// the background run task, or null if cancelled / already resolved.
    /// </summary>
    public async Task<Func<CancellationToken, Task<HostedRunOutcome>>?> PrepareHostedRunAsync(
        string filePath,
        bool isDirty,
        CancellationToken ct = default)
    {
        if (!Sessions.IsHosted || Sessions.Target.Server is null)
            return null;

        // If session expired, require explicit re-sign-in or explicit switch to Local.
        if (!Sessions.IsSignedIn)
        {
            var resumed = await HandleExpiredSessionChoiceAsync(Sessions.Target.Server, pending: null, ct);
            if (!resumed)
                return null;
        }

        var session = Sessions.Session;
        if (session is null)
            return null;

        // If this exact (origin, tenant, user) has an unconfirmed submission, recover it first.
        var pending = Coordinator.GetPendingSubmissionForCurrentCaller();
        if (pending is not null)
        {
            var options = new List<string>
            {
                "Check Status Again (same request key)",
                "Switch to Local (Offline)",
                "I checked recent runs — start a new run",
                "Cancel"
            };
            if (pending.Status == PendingSubmissionStatus.NotFound)
                options.Add($"Retry the same request (saved v{pending.Version})");
            var choice = await _choose(
                $"Unconfirmed submission ({pending.IdempotencyKey[..8]}…) for {pending.PipelineName}", options);
            if (!Sessions.IsCurrentSession(session))
                return null;
            if (choice == 4 && pending.Status == PendingSubmissionStatus.NotFound)
                return runCt => RunAndHandleOutcomeAsync(
                    () => Coordinator.SubmitWithRecoveryAsync(pending, BuildCallbacks(), runCt, session));

            if (choice == 0)
            {
                return runCt => RunAndHandleOutcomeAsync(
                    () => Coordinator.RecoverPendingSubmissionAsync(pending, BuildCallbacks(), runCt, session));
            }
            if (choice == 1)
            {
                await DisconnectAsync(ct);
                return null;
            }
            if (choice != 2)
                return null;

            Coordinator.ForgetPendingSubmission(pending);
        }

        // Resolve runnable pipeline using Execute-only endpoints (/api/studio/pipelines/runnable & /{id}/revision).
        RunnablePipelineDto? pipeline;
        try
        {
            pipeline = await ResolveRunnablePipelineAsync(session, filePath, ct);
        }
        catch (HostedSessionExpiredException)
        {
            _log("[HOSTED] Session expired before submission. Editor buffer preserved.", ConsoleColor.Yellow);
            _showStatus("Hosted session expired — sign in again or switch to Local (Offline).");
            return null;
        }
        catch (Exception ex)
        {
            var safe = HostedSecretRedactor.Redact(ex.Message, Sessions.GetActiveSecrets());
            _log($"[HOSTED ERROR] {safe}", ConsoleColor.Red);
            _showStatus($"Hosted error: {safe}");
            return null;
        }

        if (pipeline is null || !Sessions.IsCurrentSession(session))
            return null;

        // Dirty local buffer: offer Run Last Saved Revision or Cancel (hosted editing/saving is separate).
        if (isDirty)
        {
            var choice = await _choose(
                $"'{pipeline.Name}' has unsaved local edits (Portal saved v{pipeline.Version})",
                [
                    $"Run Last Saved Revision (v{pipeline.Version})",
                    "Cancel"
                ]);
            if (choice != 0)
            {
                _showStatus("Hosted run cancelled. Local editor buffer preserved.");
                return null;
            }
        }

        if (!Sessions.IsCurrentSession(session))
            return null;

        // Surface active host, tenant, and signed-in user before submission.
        _log(
            $"[HOSTED] Target={session.Server.Origin} | Tenant={session.TenantId} | User={session.User.Username} | Pipeline={pipeline.Name} (v{pipeline.Version})",
            ConsoleColor.Cyan);
        _showStatus($"Running {pipeline.Name} v{pipeline.Version} on {session.Server.Label} ({session.TenantId}) as {session.User.Username}…");

        return runCt => RunAndHandleOutcomeAsync(
            () => Coordinator.SubmitNewRunAsync(pipeline, BuildCallbacks(), runCt, session));
    }

    public async Task<HostedRunOutcome?> CheckPendingSubmissionAsync(CancellationToken ct = default)
    {
        var work = await PreparePendingRecoveryAsync(ct);
        return work is null ? null : await work(ct);
    }

    public async Task<Func<CancellationToken, Task<HostedRunOutcome>>?> PreparePendingRecoveryAsync(CancellationToken ct = default)
    {
        if (!Sessions.IsHosted)
        {
            _showStatus("Target is Local (Offline). No hosted submission to check.");
            return null;
        }

        if (!Sessions.IsSignedIn && Sessions.Target.Server is not null)
        {
            if (!await ConnectToServerAsync(Sessions.Target.Server, ct))
                return null;
        }

        var session = Sessions.Session;
        var pending = Coordinator.GetPendingSubmissionForCurrentCaller();
        if (pending is null || session is null)
        {
            _showStatus("No unconfirmed hosted submission for the active host, tenant, and account.");
            return null;
        }

        return runCt => RunAndHandleOutcomeAsync(
            () => Coordinator.RecoverPendingSubmissionAsync(pending, BuildCallbacks(), runCt, session));
    }

    public async Task<string> RequestStopAsync(CancellationToken ct = default)
    {
        var msg = await Coordinator.RequestStopAsync(BuildCallbacks(), ct);
        _showStatus(msg);
        return msg;
    }

    private async Task<HostedRunOutcome> RunAndHandleOutcomeAsync(Func<Task<HostedRunOutcome>> action)
    {
        var outcome = await action();
        if (outcome.Kind == HostedRunOutcomeKind.SessionExpired)
            _showStatus("Hosted session expired — editor buffer preserved. Sign in again or switch to Local.");
        else
            _showStatus(outcome.Message);
        return outcome;
    }

    private async Task<RunnablePipelineDto?> ResolveRunnablePipelineAsync(
        HostedSession session, string filePath, CancellationToken ct)
    {
        var key = (session.Server.Origin, session.TenantId, session.User.Id, Path.GetFullPath(filePath));
        if (_linkedPipelinesByScope.TryGetValue(key, out var linkedId))
            return await Client.GetRunnableRevisionAsync(linkedId, ct, session);

        var fileName = Path.GetFileName(filePath);
        string? query = Path.GetFileNameWithoutExtension(fileName);
        var page = await Client.GetRunnablePipelinesAsync(query, ct: ct, expectedSession: session);
        if (page.Items.Count == 0 && page.NextAfterId is null && !string.IsNullOrWhiteSpace(query))
        {
            query = null;
            page = await Client.GetRunnablePipelinesAsync(ct: ct, expectedSession: session);
        }

        var canAutoSelect = page.NextAfterId is null;
        while (Sessions.IsCurrentSession(session))
        {
            if (page.Items.Count == 0 && page.NextAfterId is null)
            {
                _showStatus("No runnable Portal pipelines found.");
                return null;
            }

            var exact = page.Items.Where(item => string.Equals(item.Name, fileName, StringComparison.OrdinalIgnoreCase)).ToArray();
            int selectedId;
            if (canAutoSelect && exact.Length == 1)
            {
                selectedId = exact[0].Id;
            }
            else
            {
                var labels = page.Items.Select(item => $"{item.Name} (v{item.Version} · {item.FolderPath}) [#{item.Id}]").ToList();
                if (page.NextAfterId is not null)
                    labels.Add("Show more pipelines");
                var idx = await _choose($"Select Saved Portal Pipeline ({session.Server.Alias})", labels);
                if (idx < 0 || idx >= labels.Count || !Sessions.IsCurrentSession(session))
                    return null;
                if (idx == page.Items.Count && page.NextAfterId is int next)
                {
                    page = await Client.GetRunnablePipelinesAsync(query, next, ct, session);
                    continue;
                }
                selectedId = page.Items[idx].Id;
            }

            var revision = await Client.GetRunnableRevisionAsync(selectedId, ct, session);
            _linkedPipelinesByScope[key] = revision.Id;
            return revision;
        }

        return null;
    }

    private async Task<bool> HandleExpiredSessionChoiceAsync(
        HostedServerEntry server,
        PendingSubmissionRecord? pending,
        CancellationToken ct)
    {
        var title = pending is not null
            ? $"Session expired while submission {pending.IdempotencyKey[..8]}… is pending"
            : $"Hosted session for {server.Label} expired";
        var choice = await _choose(title,
        [
            "Sign In Again",
            "Switch to Local (Offline)",
            "Cancel (keep Hosted target & buffer)"
        ]);

        if (choice == 0)
            return await ConnectToServerAsync(server, ct);
        if (choice == 1)
        {
            await DisconnectAsync(ct);
            return false;
        }
        return false;
    }

    private HostedRunCallbacks BuildCallbacks() => new(
        OnInfo: msg => _log($"[HOSTED] {msg}", ConsoleColor.Cyan),
        OnWarning: msg => _log($"[HOSTED] {msg}", ConsoleColor.Yellow),
        OnError: msg => _log($"[HOSTED ERROR] {msg}", ConsoleColor.Red),
        OnStatement: (stmt, index) =>
        {
            var status = stmt.Failed ? "FAILED" : "OK";
            _log($"[HOSTED STMT {index}] {status} | {stmt.DurationMs}ms | {stmt.RowsProcessed} rows | {stmt.Statement}",
                stmt.Failed ? ConsoleColor.Red : ConsoleColor.Gray);
        });

    public ValueTask DisposeAsync() => Sessions.DisposeAsync();
}
