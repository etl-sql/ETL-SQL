namespace ETL_SQL.Core.Hosted;

public enum HostedRunOutcomeKind
{
    Completed,
    Refused,
    UnconfirmedUnknown,
    UnconfirmedNotFound,
    SessionExpired,
    Error
}

public sealed record HostedRunOutcome(
    HostedRunOutcomeKind Kind,
    string Message,
    PipelineRunDto? FinalRun = null,
    PipelineRunRefusalDto? Refusal = null,
    PendingSubmissionRecord? PendingSubmission = null);

public sealed record HostedRunCallbacks(
    Action<string>? OnInfo = null,
    Action<string>? OnWarning = null,
    Action<string>? OnError = null,
    Action<PipelineRunStatementDto, int>? OnStatement = null);

/// <summary>
/// UI-agnostic coordinator for hosted pipeline runs, UUIDv4 idempotency, dropped-response
/// recovery, caller-scoped pending submissions, polling, and cancellation.
/// Shared by ETL-SQL TUI (ST57) and Desktop Studio (ST58).
/// </summary>
public sealed class HostedRunCoordinator
{
    private static readonly TimeSpan RecoveryWindow = TimeSpan.FromHours(24);

    private readonly HostedClientSessionService _sessions;
    private readonly HostedPipelineClient _client;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _initialPollDelay;
    private readonly TimeSpan _maxPollDelay;
    private readonly Dictionary<(string Origin, string TenantId, int UserId), PendingSubmissionRecord> _pendingByScope = new();
    private readonly object _lock = new();

    private ActiveHostedRun? _activeRun;
    private HostedSession? _activeSession;
    private int _operationInProgress;

    public HostedRunCoordinator(
        HostedClientSessionService sessions,
        HostedPipelineClient client,
        TimeProvider? clock = null,
        TimeSpan? initialPollDelay = null,
        TimeSpan? maxPollDelay = null)
    {
        _sessions = sessions;
        _client = client;
        _clock = clock ?? TimeProvider.System;
        _initialPollDelay = initialPollDelay ?? TimeSpan.FromMilliseconds(250);
        _maxPollDelay = maxPollDelay ?? TimeSpan.FromSeconds(2);
    }

    public ActiveHostedRun? ActiveRun
    {
        get { lock (_lock) return _activeRun; }
    }

    /// <summary>
    /// Returns the unconfirmed submission only when the active signed-in session matches the
    /// exact Portal origin, tenant, and user that created it.
    /// </summary>
    public PendingSubmissionRecord? GetPendingSubmissionForCurrentCaller()
    {
        var session = _sessions.Session;
        if (session is null)
            return null;

        var key = ScopeKey(session.Server.Origin, session.TenantId, session.User.Id);
        lock (_lock)
        {
            if (!_sessions.IsCurrentSession(session) || !_pendingByScope.TryGetValue(key, out var pending))
                return null;

            if (_clock.GetUtcNow() - pending.CreatedAtUtc >= RecoveryWindow)
            {
                _pendingByScope.Remove(key);
                return null;
            }

            return pending;
        }
    }

    public void ForgetPendingSubmission(PendingSubmissionRecord pending)
    {
        var key = ScopeKey(pending.Origin, pending.TenantId, pending.UserId);
        lock (_lock)
        {
            if (_pendingByScope.TryGetValue(key, out var existing)
                && string.Equals(existing.IdempotencyKey, pending.IdempotencyKey, StringComparison.Ordinal))
            {
                _pendingByScope.Remove(key);
            }
        }
    }

    public async Task<HostedRunOutcome> SubmitNewRunAsync(
        RunnablePipelineDto pipeline,
        HostedRunCallbacks? callbacks = null,
        CancellationToken ct = default,
        HostedSession? expectedSession = null)
    {
        var session = expectedSession ?? _sessions.Session;
        if (session is null || !_sessions.IsCurrentSession(session))
            return new HostedRunOutcome(HostedRunOutcomeKind.SessionExpired, "Hosted Portal session is not signed in.");

        var pending = new PendingSubmissionRecord(
            IdempotencyKey: Guid.NewGuid().ToString("D"),
            Origin: session.Server.Origin,
            TenantId: session.TenantId,
            UserId: session.User.Id,
            Username: session.User.Username,
            PipelineId: pipeline.Id,
            PipelineName: pipeline.Name,
            Version: pipeline.Version,
            JobName: $"PIPELINE:{pipeline.Id}",
            Status: PendingSubmissionStatus.Unconfirmed,
            CreatedAtUtc: _clock.GetUtcNow());

        return await SubmitWithRecoveryAsync(pending, callbacks, ct, session);
    }

    public Task<HostedRunOutcome> SubmitWithRecoveryAsync(
        PendingSubmissionRecord pending,
        HostedRunCallbacks? callbacks = null,
        CancellationToken ct = default,
        HostedSession? expectedSession = null) =>
        RunExclusiveAsync(() => SubmitCoreAsync(pending, callbacks, ct, expectedSession ?? _sessions.Session));

    private async Task<HostedRunOutcome> SubmitCoreAsync(
        PendingSubmissionRecord pending, HostedRunCallbacks? callbacks, CancellationToken ct, HostedSession? session)
    {
        if (session is null || !MatchesCaller(session, pending.Origin, pending.TenantId, pending.UserId)
            || !_sessions.IsCurrentSession(session))
            return new HostedRunOutcome(HostedRunOutcomeKind.SessionExpired, "Active hosted session does not match submission owner.");

        if (_clock.GetUtcNow() - pending.CreatedAtUtc >= RecoveryWindow)
        {
            ForgetPendingSubmission(pending);
            return new HostedRunOutcome(HostedRunOutcomeKind.Error, "The 24-hour recovery window has expired. Check recent runs before starting another run.");
        }

        if (!TryClaimPending(pending))
            return new HostedRunOutcome(HostedRunOutcomeKind.Error, "Check the prior unconfirmed submission before starting another run.");

        try
        {
            var submitted = await _client.SubmitPipelineRunAsync(pending.PipelineId, pending.Version, pending.IdempotencyKey, ct, session);
            if (!_sessions.IsCurrentSession(session))
                return new HostedRunOutcome(HostedRunOutcomeKind.SessionExpired, "Hosted session changed during submission.");

            if (submitted.Accepted && submitted.Receipt is not null)
            {
                return await AttachAndPollAsync(submitted.Receipt, pending, session, callbacks, ct);
            }

            var refusal = submitted.Refusal ?? new PipelineRunRefusalDto($"http_{submitted.StatusCode}", $"Submission failed ({submitted.StatusCode}).");

            if (submitted.StatusCode == 409 && string.Equals(refusal.Code, "submission_outcome_unknown", StringComparison.Ordinal))
            {
                var updated = pending with { Status = PendingSubmissionStatus.Unknown };
                StorePending(updated);
                var msg = $"Submission unconfirmed (request {pending.IdempotencyKey}): {refusal.Error}";
                callbacks?.OnWarning?.Invoke(msg);
                return new HostedRunOutcome(HostedRunOutcomeKind.UnconfirmedUnknown, msg, Refusal: refusal, PendingSubmission: updated);
            }

            if (submitted.StatusCode >= 500)
            {
                return await RecoverCoreAsync(pending, callbacks, ct, session);
            }

            // A definite refusal clears this request key.
            ForgetPendingSubmission(pending);
            var refusedMsg = $"Hosted run refused ({refusal.Code}): {refusal.Error}";
            callbacks?.OnError?.Invoke(refusedMsg);
            return new HostedRunOutcome(HostedRunOutcomeKind.Refused, refusedMsg, Refusal: refusal);
        }
        catch (HostedSessionExpiredException ex)
        {
            var msg = $"Hosted session expired while submission {pending.IdempotencyKey} was pending.";
            callbacks?.OnWarning?.Invoke(msg);
            return new HostedRunOutcome(HostedRunOutcomeKind.SessionExpired, ex.Message, PendingSubmission: pending);
        }
        catch (HostedNetworkException)
        {
            // Dropped acceptance response: keep exact Idempotency-Key and query by-request first.
            var unconfirmed = pending with { Status = PendingSubmissionStatus.Unconfirmed };
            StorePending(unconfirmed);
            callbacks?.OnWarning?.Invoke($"Submission unconfirmed (request {pending.IdempotencyKey}) — checking Portal for accepted run…");
            return await RecoverCoreAsync(unconfirmed, callbacks, ct, session);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new HostedRunOutcome(HostedRunOutcomeKind.Error, "Hosted submission cancelled.");
        }
        catch (Exception ex)
        {
            var safe = HostedSecretRedactor.Redact(ex.Message, _sessions.GetActiveSecrets());
            callbacks?.OnError?.Invoke($"Hosted submission error: {safe}");
            return new HostedRunOutcome(HostedRunOutcomeKind.Error, safe);
        }
    }

    public Task<HostedRunOutcome> RecoverPendingSubmissionAsync(
        PendingSubmissionRecord pending,
        HostedRunCallbacks? callbacks = null,
        CancellationToken ct = default,
        HostedSession? expectedSession = null) =>
        RunExclusiveAsync(() => RecoverCoreAsync(pending, callbacks, ct, expectedSession ?? _sessions.Session));

    private async Task<HostedRunOutcome> RecoverCoreAsync(
        PendingSubmissionRecord pending, HostedRunCallbacks? callbacks, CancellationToken ct, HostedSession? session)
    {
        if (session is null || !MatchesCaller(session, pending.Origin, pending.TenantId, pending.UserId)
            || !_sessions.IsCurrentSession(session))
            return new HostedRunOutcome(HostedRunOutcomeKind.SessionExpired, "Active hosted session does not match pending submission owner.");

        if (_clock.GetUtcNow() - pending.CreatedAtUtc >= RecoveryWindow)
        {
            ForgetPendingSubmission(pending);
            return new HostedRunOutcome(HostedRunOutcomeKind.Error, "The 24-hour recovery window has expired. Check recent runs before starting another run.");
        }

        if (!TryClaimPending(pending))
            return new HostedRunOutcome(HostedRunOutcomeKind.Error, "A different submission is pending for this account.");

        try
        {
            var lookup = await _client.GetSubmissionByRequestAsync(pending.PipelineId, pending.IdempotencyKey, ct, session, pending.Version);
            if (!_sessions.IsCurrentSession(session))
                return new HostedRunOutcome(HostedRunOutcomeKind.SessionExpired, "Hosted session changed during recovery.");

            if (lookup.Kind == LookupByRequestKind.Found && lookup.Receipt is not null)
            {
                callbacks?.OnInfo?.Invoke($"Recovered accepted run #{lookup.Receipt.RunId} for request {pending.IdempotencyKey}.");
                return await AttachAndPollAsync(lookup.Receipt, pending, session, callbacks, ct);
            }

            if (lookup.Kind == LookupByRequestKind.Unknown)
            {
                var unknown = pending with { Status = PendingSubmissionStatus.Unknown };
                StorePending(unknown);
                var msg = $"Submission unconfirmed (request {pending.IdempotencyKey}): {lookup.Refusal?.Error ?? "Portal cannot confirm whether Orchestrator accepted this run."}";
                callbacks?.OnWarning?.Invoke(msg);
                return new HostedRunOutcome(HostedRunOutcomeKind.UnconfirmedUnknown, msg, Refusal: lookup.Refusal, PendingSubmission: unknown);
            }

            if (lookup.Kind == LookupByRequestKind.NotFound)
            {
                var missing = pending with { Status = PendingSubmissionStatus.NotFound };
                StorePending(missing);
                var msg = $"Submission unconfirmed (request {pending.IdempotencyKey}): Portal has no record of this request. Retry using the same key or check recent runs.";
                callbacks?.OnWarning?.Invoke(msg);
                return new HostedRunOutcome(HostedRunOutcomeKind.UnconfirmedNotFound, msg, Refusal: lookup.Refusal, PendingSubmission: missing);
            }

            var refusedMsg = $"Recovery check refused ({lookup.Refusal?.Code ?? "error"}): {lookup.Refusal?.Error ?? "Unable to check submission."}";
            callbacks?.OnError?.Invoke(refusedMsg);
            return new HostedRunOutcome(HostedRunOutcomeKind.Refused, refusedMsg, Refusal: lookup.Refusal, PendingSubmission: pending);
        }
        catch (HostedSessionExpiredException ex)
        {
            var msg = $"Hosted session expired while checking submission {pending.IdempotencyKey}. Sign in again as {pending.Username} ({pending.TenantId}) to resume recovery.";
            callbacks?.OnWarning?.Invoke(msg);
            return new HostedRunOutcome(HostedRunOutcomeKind.SessionExpired, ex.Message, PendingSubmission: pending);
        }
        catch (HostedNetworkException ex)
        {
            var msg = $"Submission unconfirmed (request {pending.IdempotencyKey}): {ex.Message}";
            callbacks?.OnWarning?.Invoke(msg);
            return new HostedRunOutcome(HostedRunOutcomeKind.UnconfirmedUnknown, msg, PendingSubmission: pending);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new HostedRunOutcome(HostedRunOutcomeKind.Error, "Hosted recovery was interrupted.", PendingSubmission: pending);
        }
        catch (Exception ex)
        {
            var safe = HostedSecretRedactor.Redact(ex.Message, session.ActiveSecrets);
            callbacks?.OnError?.Invoke($"Hosted recovery error: {safe}");
            return new HostedRunOutcome(HostedRunOutcomeKind.Error, safe, PendingSubmission: pending);
        }
    }

    private async Task<HostedRunOutcome> RunExclusiveAsync(Func<Task<HostedRunOutcome>> operation)
    {
        if (Interlocked.CompareExchange(ref _operationInProgress, 1, 0) != 0)
            return new HostedRunOutcome(HostedRunOutcomeKind.Error, "A hosted run or recovery is already in progress.");

        try { return await operation(); }
        finally { Volatile.Write(ref _operationInProgress, 0); }
    }

    public async Task<string> RequestStopAsync(HostedRunCallbacks? callbacks = null, CancellationToken ct = default)
    {
        ActiveHostedRun? active;
        HostedSession? session;
        lock (_lock) { active = _activeRun; session = _activeSession; }

        if (active is null || session is null || !_sessions.IsCurrentSession(session))
            return "No active hosted run to stop.";

        try
        {
            var result = await _client.CancelPipelineRunAsync(active.PipelineId, active.RunId, ct, session);
            if (result.Kind == CancelRunKind.Accepted)
            {
                active.CancelRequested = true;
                var msg = $"Cancellation requested for run #{active.RunId} — waiting for Orchestrator";
                callbacks?.OnWarning?.Invoke(msg);
                return msg;
            }

            if (result.Kind == CancelRunKind.AlreadyFinished)
            {
                var msg = $"Run #{active.RunId} already finished before cancellation could be applied.";
                callbacks?.OnInfo?.Invoke(msg);
                return msg;
            }

            var err = $"Cancel refused for run #{active.RunId}: {result.Refusal?.Error ?? "Request refused."}";
            callbacks?.OnError?.Invoke(err);
            return err;
        }
        catch (HostedSessionExpiredException)
        {
            var msg = $"Hosted session expired while requesting cancellation for run #{active.RunId}.";
            callbacks?.OnWarning?.Invoke(msg);
            return msg;
        }
        catch (Exception ex)
        {
            var safe = HostedSecretRedactor.Redact(ex.Message, _sessions.GetActiveSecrets());
            var msg = $"Cancel failed for run #{active.RunId}: {safe}";
            callbacks?.OnError?.Invoke(msg);
            return msg;
        }
    }

    private async Task<HostedRunOutcome> AttachAndPollAsync(
        PipelineRunSubmittedDto receipt,
        PendingSubmissionRecord pending,
        HostedSession session,
        HostedRunCallbacks? callbacks,
        CancellationToken ct)
    {
        var active = new ActiveHostedRun(
            receipt.PipelineId,
            pending.PipelineName,
            receipt.RunId,
            receipt.JobName,
            pending.Origin,
            pending.TenantId,
            pending.UserId,
            pending.Username,
            receipt.Version,
            receipt.ScriptHash);

        lock (_lock)
        {
            if (_activeRun is not null)
                return new HostedRunOutcome(HostedRunOutcomeKind.Error, "A hosted run is already being monitored.");
            _activeRun = active;
            _activeSession = session;
        }

        callbacks?.OnInfo?.Invoke(
            $"Accepted run #{receipt.RunId} ({receipt.JobName}, v{receipt.Version}{(receipt.Replayed ? ", replayed" : "")})");

        var delay = _initialPollDelay;
        var renderedStatements = 0;

        try
        {
            while (MatchesActive(active) && !ct.IsCancellationRequested)
            {
                var polled = await _client.GetPipelineRunAsync(active.PipelineId, active.RunId, ct, session);
                if (!MatchesActive(active))
                    return new HostedRunOutcome(HostedRunOutcomeKind.SessionExpired, "Hosted session changed while polling run.", PendingSubmission: pending);

                if (!polled.Ok || polled.Run is null)
                {
                    var msg = $"Run #{active.RunId} status check refused ({polled.Refusal?.Code ?? "error"}): {polled.Refusal?.Error ?? "Unknown error"}";
                    callbacks?.OnError?.Invoke(msg);
                    return new HostedRunOutcome(HostedRunOutcomeKind.Refused, msg, Refusal: polled.Refusal);
                }

                var run = polled.Run;
                if (run.ScriptHash is not null && run.ScriptHash != active.ScriptHash)
                    throw new HostedNetworkException("Portal returned status for a different script revision.");
                if (run.Statements is not null)
                {
                    for (var i = renderedStatements; i < run.Statements.Count; i++)
                        callbacks?.OnStatement?.Invoke(run.Statements[i], i + 1);
                    renderedStatements = run.Statements.Count;
                }

                if (run.IsTerminal)
                {
                    ForgetPendingSubmission(pending);
                    var summary = $"Hosted run #{run.RunId} finished: {run.Status} ({run.RowsProcessed} rows)";
                    if (string.Equals(run.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase))
                        callbacks?.OnInfo?.Invoke(summary);
                    else if (string.Equals(run.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                        callbacks?.OnWarning?.Invoke(summary);
                    else
                        callbacks?.OnError?.Invoke(string.IsNullOrWhiteSpace(run.ErrorMessage) ? summary : $"{summary} — {run.ErrorMessage}");

                    return new HostedRunOutcome(HostedRunOutcomeKind.Completed, summary, FinalRun: run);
                }

                await Task.Delay(delay, ct);
                var nextMs = Math.Min(_maxPollDelay.TotalMilliseconds, delay.TotalMilliseconds * 1.5);
                delay = TimeSpan.FromMilliseconds(nextMs);
            }

            return new HostedRunOutcome(HostedRunOutcomeKind.Error, $"Stopped polling run #{active.RunId}.");
        }
        catch (HostedSessionExpiredException ex)
        {
            var msg = $"Hosted session expired while polling run #{active.RunId}.";
            callbacks?.OnWarning?.Invoke(msg);
            return new HostedRunOutcome(HostedRunOutcomeKind.SessionExpired, ex.Message, PendingSubmission: pending);
        }
        catch (HostedNetworkException ex)
        {
            var message = $"Lost contact while monitoring run #{active.RunId}. Check the original request key to resume.";
            callbacks?.OnWarning?.Invoke(message);
            return new HostedRunOutcome(HostedRunOutcomeKind.UnconfirmedUnknown, $"{message} {ex.Message}", PendingSubmission: pending);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new HostedRunOutcome(HostedRunOutcomeKind.Error, $"Polling interrupted for run #{active.RunId}.");
        }
        finally
        {
            lock (_lock)
            {
                if (ReferenceEquals(_activeRun, active))
                {
                    _activeRun = null;
                    _activeSession = null;
                }
            }
        }
    }

    private bool MatchesActive(ActiveHostedRun active)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(_activeRun, active))
                return false;
        }
        return _activeSession is { } session && _sessions.IsCurrentSession(session);
    }

    private static bool MatchesCaller(HostedSession? session, string origin, string tenantId, int userId) =>
        session is not null && session.Server.Origin == origin && session.TenantId == tenantId && session.User.Id == userId;

    private bool TryClaimPending(PendingSubmissionRecord pending)
    {
        var key = ScopeKey(pending.Origin, pending.TenantId, pending.UserId);
        lock (_lock)
        {
            if (_pendingByScope.TryGetValue(key, out var previous)
                && previous.IdempotencyKey != pending.IdempotencyKey
                && _clock.GetUtcNow() - previous.CreatedAtUtc < RecoveryWindow)
                return false;
            _pendingByScope[key] = pending;
            return true;
        }
    }

    private void StorePending(PendingSubmissionRecord pending)
    {
        var key = ScopeKey(pending.Origin, pending.TenantId, pending.UserId);
        lock (_lock) _pendingByScope[key] = pending;
    }

    private static (string Origin, string TenantId, int UserId) ScopeKey(string origin, string tenantId, int userId) =>
        (origin, tenantId, userId);
}
