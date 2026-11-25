using System.Collections.Concurrent;
using ETL_SQL.Core.Hosted;
using Microsoft.Extensions.Hosting;

namespace ETL_SQL.WorkstationEditor;

public sealed record HostedServerStatusDto(
    string Alias,
    string Label,
    string Origin,
    string? Environment);

public sealed record HostedUserStatusDto(
    int Id,
    string Username,
    string? Email);

public sealed record HostedPendingSubmissionDto(
    string IdempotencyKey,
    string Origin,
    string TenantId,
    int UserId,
    string Username,
    int PipelineId,
    string PipelineName,
    long Version,
    string JobName,
    string Status,
    string? DocumentId,
    string? DocumentPath,
    DateTimeOffset CreatedAtUtc);

public sealed record HostedActiveRunDto(
    long RunId,
    int PipelineId,
    string PipelineName,
    string JobName,
    long Version,
    string Origin,
    string TenantId,
    int UserId,
    string Username,
    bool CancelRequested,
    string? DocumentId,
    string? DocumentPath);

public sealed record HostedStatusDto(
    string Mode,
    string TargetKey,
    string StatusLabel,
    HostedServerStatusDto? Server,
    bool SignedIn,
    string? TenantId,
    HostedUserStatusDto? User,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<HostedServerStatusDto> Servers,
    HostedPendingSubmissionDto? PendingSubmission,
    HostedActiveRunDto? ActiveRun);

public sealed record HostedRunnablePipelineDto(
    int Id,
    string Name,
    string FolderPath,
    long Version,
    bool CanAuthor);

public sealed record HostedRunnablePipelinePageDto(
    IReadOnlyList<HostedRunnablePipelineDto> Items,
    int? NextAfterId);

public sealed record HostedPipelineRunRefusalDto(
    string Code,
    string Error,
    long? CurrentVersion = null);

public sealed record HostedPipelineRunStatementDto(
    string Statement,
    long DurationMs,
    long RowsProcessed,
    bool Failed);

public sealed record HostedPipelineRunDto(
    long RunId,
    int PipelineId,
    string JobName,
    string Status,
    bool IsTerminal,
    DateTime StartTime,
    DateTime? EndTime,
    string? ErrorMessage,
    long RowsProcessed,
    string? ScriptHash,
    bool LogTruncated,
    IReadOnlyList<HostedPipelineRunStatementDto> Statements);

public sealed record HostedRunEventDto(string Level, string Text);

public sealed record HostedRunOperationDto(
    string OperationId,
    string? DocumentId,
    string? DocumentPath,
    string OwnerTargetKey,
    string OwnerOrigin,
    string OwnerTenantId,
    int OwnerUserId,
    string OwnerUsername,
    int PipelineId,
    string PipelineName,
    long Version,
    string JobName,
    string State,
    bool IsTerminal,
    long? RunId,
    bool CancelRequested,
    string? Message,
    HostedPipelineRunRefusalDto? Refusal,
    HostedPipelineRunDto? FinalRun,
    HostedPendingSubmissionDto? PendingSubmission,
    IReadOnlyList<HostedRunEventDto> Events);

public sealed record HostedAddServerRequest(
    string? Alias,
    string? Label,
    string? Origin,
    string? Environment = null);

public sealed record HostedConnectRequest(string? Alias);

public sealed record HostedStartRunRequest(
    int PipelineId,
    long ExpectedVersion,
    string? ExpectedOrigin,
    string? ExpectedTenantId,
    int ExpectedUserId,
    string? DocumentId = null,
    string? DocumentPath = null);

public sealed record HostedStopRunRequest(string? OperationId);

public sealed record HostedStopRunResponse(string Message, HostedStatusDto Status);

public sealed record HostedRecoverRunRequest(string? Mode = "check");

public sealed class HostedSelectionChangedException(string code, string message, long? currentVersion = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public long? CurrentVersion { get; } = currentVersion;
}

public sealed class HostedOperationForbiddenException(string message) : Exception(message);

public sealed class HostedOperationNotActiveException(string message) : Exception(message);

/// <summary>
/// Desktop Studio hosted execution facade over the shared ST57 .NET client
/// (<see cref="HostedClientSessionService"/>, <see cref="HostedPipelineClient"/>,
/// <see cref="HostedRunCoordinator"/>). Keeps all Portal access/refresh tokens in
/// .NET host memory and never returns them to the browser UI, logs, or registries.
/// </summary>
public sealed class WorkstationHostedService : IAsyncDisposable
{
    private readonly HostedClientSessionService _sessions;
    private readonly HostedPipelineClient _client;
    private readonly HostedRunCoordinator _coordinator;
    private readonly StudioHostLifecycleService _lifecycle;
    private readonly ConcurrentDictionary<string, TrackedOperation> _operations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (string? DocumentId, string? DocumentPath)> _pendingDocuments = new(StringComparer.Ordinal);
    private readonly CancellationTokenRegistration _stoppingRegistration;

    private volatile TrackedOperation? _currentOperation;

    public WorkstationHostedService(
        HostedClientSessionService sessions,
        HostedPipelineClient client,
        HostedRunCoordinator coordinator,
        StudioHostLifecycleService lifecycle,
        IHostApplicationLifetime hostLifetime)
    {
        _sessions = sessions;
        _client = client;
        _coordinator = coordinator;
        _lifecycle = lifecycle;
        _stoppingRegistration = hostLifetime.ApplicationStopping.Register(() =>
        {
            try
            {
                _sessions.DisconnectAsync(TimeSpan.FromSeconds(2), CancellationToken.None).GetAwaiter().GetResult();
            }
            catch
            {
                // Best-effort revocation during host shutdown.
            }
        });
    }

    public HostedStatusDto GetStatus()
    {
        var target = _sessions.Target;
        var session = _sessions.Session;
        var servers = _sessions.ServerRegistry.Load()
            .Select(ToServerDto)
            .ToArray();

        HostedPendingSubmissionDto? pendingDto = null;
        var pending = _coordinator.GetPendingSubmissionForCurrentCaller();
        if (pending is not null)
            pendingDto = ToPendingDto(pending);

        HostedActiveRunDto? activeDto = null;
        var active = _coordinator.ActiveRun;
        if (active is not null && session is not null
            && string.Equals(active.Origin, session.Server.Origin, StringComparison.OrdinalIgnoreCase)
            && string.Equals(active.TenantId, session.TenantId, StringComparison.OrdinalIgnoreCase)
            && active.UserId == session.User.Id)
        {
            var currentOp = _currentOperation;
            activeDto = new HostedActiveRunDto(
                active.RunId,
                active.PipelineId,
                active.PipelineName,
                active.JobName,
                active.Version,
                active.Origin,
                active.TenantId,
                active.UserId,
                active.Username,
                active.CancelRequested,
                currentOp?.DocumentId,
                currentOp?.DocumentPath);
        }

        return new HostedStatusDto(
            Mode: target.Kind == ExecutionTargetKind.Hosted ? "hosted" : "local",
            TargetKey: BuildTargetKey(target, session),
            StatusLabel: _sessions.FormatStatusLabel(),
            Server: target.Server is null ? null : ToServerDto(target.Server),
            SignedIn: session is not null,
            TenantId: session?.TenantId,
            User: session is null ? null : new HostedUserStatusDto(session.User.Id, session.User.Username, session.User.Email),
            Scopes: session?.Scopes ?? [],
            Servers: servers,
            PendingSubmission: pendingDto,
            ActiveRun: activeDto);
    }

    public HostedStatusDto AddOrUpdateServer(HostedAddServerRequest request)
    {
        _sessions.ServerRegistry.AddOrUpdate(
            request.Alias ?? string.Empty,
            request.Label ?? string.Empty,
            request.Origin ?? string.Empty,
            request.Environment);
        return GetStatus();
    }

    public HostedStatusDto RemoveServer(string alias)
    {
        _sessions.ServerRegistry.Remove(alias);
        return GetStatus();
    }

    public async Task<HostedStatusDto> ConnectAsync(string alias, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(alias))
            throw new ArgumentException("Server alias is required.");

        var server = _sessions.ServerRegistry.FindByAlias(alias)
            ?? throw new KeyNotFoundException($"No trusted Portal server with alias '{alias.Trim()}' is registered.");

        await _sessions.SignInAsync(server, ct: ct);
        return GetStatus();
    }

    public async Task<HostedStatusDto> DisconnectAsync(CancellationToken ct = default)
    {
        await _sessions.DisconnectAsync(ct: ct);
        return GetStatus();
    }

    public async Task<HostedRunnablePipelinePageDto> ListRunnablePipelinesAsync(string? query, int afterId, CancellationToken ct = default)
    {
        var page = await _client.GetRunnablePipelinesAsync(query, afterId, ct);
        return new HostedRunnablePipelinePageDto(
            page.Items.Select(ToRunnableDto).ToArray(),
            page.NextAfterId);
    }

    public async Task<HostedRunnablePipelineDto> GetPipelineRevisionAsync(int pipelineId, CancellationToken ct = default) =>
        ToRunnableDto(await _client.GetRunnableRevisionAsync(pipelineId, ct));

    public async Task<HostedRunOperationDto> StartRunAsync(HostedStartRunRequest request, CancellationToken ct = default)
    {
        if (request.PipelineId <= 0)
            throw new ArgumentException("A valid saved pipeline ID is required.");
        if (request.ExpectedVersion <= 0)
            throw new ArgumentException("A valid saved pipeline version is required.");
        if (string.IsNullOrWhiteSpace(request.ExpectedOrigin)
            || string.IsNullOrWhiteSpace(request.ExpectedTenantId)
            || request.ExpectedUserId <= 0)
            throw new ArgumentException("Expected Portal origin, tenant, and signed-in user are required.");

        var session = _sessions.Session
            ?? throw new HostedSessionExpiredException("Hosted Portal session is not signed in. Sign in again or switch to Local (Offline).");

        if (!MatchesExpectedTarget(session, request.ExpectedOrigin, request.ExpectedTenantId, request.ExpectedUserId))
            throw new HostedSelectionChangedException(
                "target_changed",
                "The hosted Portal target or signed-in account changed since this pipeline was selected. Choose a saved pipeline on the current target and try again.");

        var existingPending = _coordinator.GetPendingSubmissionForCurrentCaller();
        if (existingPending is not null)
            throw new InvalidOperationException($"Check or recover unconfirmed submission {existingPending.IdempotencyKey} before starting another run.");

        var revision = await _client.GetRunnableRevisionAsync(request.PipelineId, ct, session);
        if (!_sessions.IsCurrentSession(session))
            throw new HostedSelectionChangedException(
                "target_changed",
                "The hosted Portal target or signed-in account changed while preparing the run. Choose again before running.");

        if (revision.Version != request.ExpectedVersion)
            throw new HostedSelectionChangedException(
                "revision_changed",
                $"Saved pipeline '{revision.Name}' changed from v{request.ExpectedVersion} to v{revision.Version}. Review and choose again before running.",
                revision.Version);

        var ownerTargetKey = BuildTargetKey(ExecutionTarget.Hosted(session.Server), session);
        var op = new TrackedOperation(
            OperationId: Guid.NewGuid().ToString("N"),
            DocumentId: request.DocumentId,
            DocumentPath: request.DocumentPath,
            OwnerTargetKey: ownerTargetKey,
            OwnerOrigin: session.Server.Origin,
            OwnerTenantId: session.TenantId,
            OwnerUserId: session.User.Id,
            OwnerUsername: session.User.Username,
            OwnerSession: session,
            PipelineId: revision.Id,
            PipelineName: revision.Name,
            Version: revision.Version,
            JobName: $"PIPELINE:{revision.Id}");

        _operations[op.OperationId] = op;
        _currentOperation = op;
        op.AddEvent("sys", $"Submitting saved revision {revision.Name} (v{revision.Version}) to {session.Server.Label} ({session.TenantId} · {session.User.Username})…");

        var callbacks = CreateCallbacks(op);
        _ = Task.Run(async () =>
        {
            using var runLease = _lifecycle.BeginRun();
            try
            {
                var outcome = await _coordinator.SubmitNewRunAsync(revision, callbacks, CancellationToken.None, session);
                CompleteOperation(op, outcome);
            }
            catch (Exception ex)
            {
                var safe = Redact(ex.Message);
                op.AddEvent("error", safe);
                op.Complete("error", safe, null, null, null);
            }
            finally
            {
                if (ReferenceEquals(_currentOperation, op))
                    _currentOperation = null;
            }
        });

        return op.ToDto(_coordinator.ActiveRun);
    }

    public HostedRunOperationDto StartRecovery(HostedRecoverRunRequest request)
    {
        var session = _sessions.Session
            ?? throw new HostedSessionExpiredException("Hosted Portal session is not signed in. Sign in again as the original user to recover.");

        var pending = _coordinator.GetPendingSubmissionForCurrentCaller()
            ?? throw new KeyNotFoundException("No pending hosted submission belongs to the current signed-in user, tenant, and Portal.");

        var (docId, docPath) = _pendingDocuments.TryGetValue(pending.IdempotencyKey, out var owner)
            ? owner
            : (null, null);

        var ownerTargetKey = BuildTargetKey(ExecutionTarget.Hosted(session.Server), session);
        var retry = string.Equals(request.Mode, "retry", StringComparison.OrdinalIgnoreCase);

        var op = new TrackedOperation(
            OperationId: Guid.NewGuid().ToString("N"),
            DocumentId: docId,
            DocumentPath: docPath,
            OwnerTargetKey: ownerTargetKey,
            OwnerOrigin: session.Server.Origin,
            OwnerTenantId: session.TenantId,
            OwnerUserId: session.User.Id,
            OwnerUsername: session.User.Username,
            OwnerSession: session,
            PipelineId: pending.PipelineId,
            PipelineName: pending.PipelineName,
            Version: pending.Version,
            JobName: pending.JobName);

        _operations[op.OperationId] = op;
        _currentOperation = op;
        op.AddEvent("sys", retry
            ? $"Retrying {pending.PipelineName} (v{pending.Version}) with original request key {pending.IdempotencyKey}…"
            : $"Checking Portal for accepted run (request {pending.IdempotencyKey})…");

        var callbacks = CreateCallbacks(op);
        _ = Task.Run(async () =>
        {
            using var runLease = _lifecycle.BeginRun();
            try
            {
                var outcome = retry
                    ? await _coordinator.SubmitWithRecoveryAsync(pending, callbacks, CancellationToken.None, session)
                    : await _coordinator.RecoverPendingSubmissionAsync(pending, callbacks, CancellationToken.None, session);
                CompleteOperation(op, outcome);
            }
            catch (Exception ex)
            {
                var safe = Redact(ex.Message);
                op.AddEvent("error", safe);
                op.Complete("error", safe, null, null, ToPendingDto(pending));
            }
            finally
            {
                if (ReferenceEquals(_currentOperation, op))
                    _currentOperation = null;
            }
        });

        return op.ToDto(_coordinator.ActiveRun);
    }

    public HostedRunOperationDto GetOperation(string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId) || !_operations.TryGetValue(operationId, out var op))
            throw new KeyNotFoundException("Hosted run operation not found.");

        var session = _sessions.Session
            ?? throw new HostedSessionExpiredException("Hosted Portal session is not signed in.");

        if (!IsOwnedBySession(op, session))
            throw new HostedOperationForbiddenException("This hosted run operation belongs to a different Portal, tenant, or signed-in user.");

        return op.ToDto(_coordinator.ActiveRun);
    }

    public async Task<HostedStopRunResponse> RequestStopAsync(HostedStopRunRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.OperationId))
            throw new ArgumentException("OperationId is required to stop a hosted run.");

        if (!_operations.TryGetValue(request.OperationId.Trim(), out var op))
            throw new KeyNotFoundException("Hosted run operation not found.");

        var session = _sessions.Session
            ?? throw new HostedSessionExpiredException("Hosted Portal session is not signed in.");

        if (!IsOwnedBySession(op, session))
            throw new HostedOperationForbiddenException("Cannot stop a hosted run owned by a different Portal, tenant, or signed-in user.");

        if (op.IsTerminal || !ReferenceEquals(_currentOperation, op))
            throw new HostedOperationNotActiveException("That hosted run operation is no longer active; Stop was not applied to any later run.");

        var runId = op.RunId;
        if (runId is null)
            throw new HostedOperationNotActiveException("That hosted run has not been accepted by the Orchestrator yet.");

        var stopTarget = new HostedRunStopTarget(op.PipelineId, runId.Value, op.OwnerOrigin, op.OwnerTenantId, op.OwnerUserId);
        var message = await _coordinator.RequestStopAsync(CreateCallbacks(op), ct, stopTarget, op.OwnerSession);
        return new HostedStopRunResponse(Redact(message), GetStatus());
    }

    public string Redact(string? message) =>
        HostedSecretRedactor.Redact(message ?? string.Empty, _sessions.GetActiveSecrets());

    public async ValueTask DisposeAsync()
    {
        await _stoppingRegistration.DisposeAsync();
        await _sessions.DisposeAsync();
    }

    private bool IsOwnedBySession(TrackedOperation op, HostedSession session) =>
        _sessions.IsCurrentSession(session)
        && MatchesExpectedTarget(session, op.OwnerOrigin, op.OwnerTenantId, op.OwnerUserId);

    private static bool MatchesExpectedTarget(HostedSession session, string origin, string tenantId, int userId) =>
        string.Equals(session.Server.Origin, origin.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
        && string.Equals(session.TenantId, tenantId.Trim(), StringComparison.OrdinalIgnoreCase)
        && session.User.Id == userId;

    private HostedRunCallbacks CreateCallbacks(TrackedOperation op) => new(
        OnInfo: msg => op.AddEvent("info", Redact(msg)),
        OnWarning: msg => op.AddEvent("warn", Redact(msg)),
        OnError: msg => op.AddEvent("error", Redact(msg)),
        OnStatement: (stmt, index) =>
        {
            var preview = stmt.Statement.Length > 120 ? stmt.Statement[..120] + "…" : stmt.Statement;
            var status = stmt.Failed ? "Failed" : "Ran";
            op.AddEvent(stmt.Failed ? "error" : "info", $"[{index}] {status}: {preview} ({stmt.RowsProcessed} rows, {stmt.DurationMs} ms)");
        },
        OnAccepted: receipt => op.SetRunId(receipt.RunId));

    private void CompleteOperation(TrackedOperation op, HostedRunOutcome outcome)
    {
        if (outcome.PendingSubmission is { } pending)
            _pendingDocuments[pending.IdempotencyKey] = (op.DocumentId, op.DocumentPath);

        var state = outcome.Kind switch
        {
            HostedRunOutcomeKind.Completed => "completed",
            HostedRunOutcomeKind.Refused => "refused",
            HostedRunOutcomeKind.UnconfirmedUnknown => "unconfirmed_unknown",
            HostedRunOutcomeKind.UnconfirmedNotFound => "unconfirmed_not_found",
            HostedRunOutcomeKind.SessionExpired => "session_expired",
            _ => "error"
        };

        var refusalDto = outcome.Refusal is null
            ? null
            : new HostedPipelineRunRefusalDto(outcome.Refusal.Code, Redact(outcome.Refusal.Error), outcome.Refusal.CurrentVersion);

        var finalRunDto = outcome.FinalRun is null
            ? null
            : new HostedPipelineRunDto(
                outcome.FinalRun.RunId,
                outcome.FinalRun.PipelineId,
                outcome.FinalRun.JobName,
                outcome.FinalRun.Status,
                outcome.FinalRun.IsTerminal,
                outcome.FinalRun.StartTime,
                outcome.FinalRun.EndTime,
                outcome.FinalRun.ErrorMessage is null ? null : Redact(outcome.FinalRun.ErrorMessage),
                outcome.FinalRun.RowsProcessed,
                outcome.FinalRun.ScriptHash,
                outcome.FinalRun.LogTruncated,
                outcome.FinalRun.Statements
                    .Select(s => new HostedPipelineRunStatementDto(s.Statement, s.DurationMs, s.RowsProcessed, s.Failed))
                    .ToArray());

        var pendingDto = outcome.PendingSubmission is null ? null : ToPendingDto(outcome.PendingSubmission);
        op.Complete(state, Redact(outcome.Message), refusalDto, finalRunDto, pendingDto);
    }

    private HostedPendingSubmissionDto ToPendingDto(PendingSubmissionRecord pending)
    {
        var (docId, docPath) = _pendingDocuments.TryGetValue(pending.IdempotencyKey, out var owner)
            ? owner
            : (null, null);

        return new HostedPendingSubmissionDto(
            pending.IdempotencyKey,
            pending.Origin,
            pending.TenantId,
            pending.UserId,
            pending.Username,
            pending.PipelineId,
            pending.PipelineName,
            pending.Version,
            pending.JobName,
            pending.Status.ToString(),
            docId,
            docPath,
            pending.CreatedAtUtc);
    }

    private static HostedRunnablePipelineDto ToRunnableDto(RunnablePipelineDto dto) =>
        new(dto.Id, dto.Name, dto.FolderPath, dto.Version, dto.CanAuthor);

    private static HostedServerStatusDto ToServerDto(HostedServerEntry server) =>
        new(server.Alias, server.Label, server.Origin, server.Environment);

    public static string BuildTargetKey(ExecutionTarget target, HostedSession? session)
    {
        if (target.Kind != ExecutionTargetKind.Hosted || target.Server is null)
            return "local";
        if (session is null)
            return $"hosted|{target.Server.Origin}|signed-out";
        return $"hosted|{session.Server.Origin}|{session.TenantId}|{session.User.Id}";
    }

    private sealed class TrackedOperation(
        string OperationId,
        string? DocumentId,
        string? DocumentPath,
        string OwnerTargetKey,
        string OwnerOrigin,
        string OwnerTenantId,
        int OwnerUserId,
        string OwnerUsername,
        HostedSession OwnerSession,
        int PipelineId,
        string PipelineName,
        long Version,
        string JobName)
    {
        private readonly object _lock = new();
        private readonly List<HostedRunEventDto> _events = [];
        private string _state = "submitting";
        private bool _isTerminal;
        private long? _runId;
        private string? _message;
        private HostedPipelineRunRefusalDto? _refusal;
        private HostedPipelineRunDto? _finalRun;
        private HostedPendingSubmissionDto? _pendingSubmission;

        public string OperationId { get; } = OperationId;
        public string? DocumentId { get; } = DocumentId;
        public string? DocumentPath { get; } = DocumentPath;
        public string OwnerOrigin { get; } = OwnerOrigin;
        public string OwnerTenantId { get; } = OwnerTenantId;
        public int OwnerUserId { get; } = OwnerUserId;
        public HostedSession OwnerSession { get; } = OwnerSession;
        public int PipelineId { get; } = PipelineId;
        public long? RunId { get { lock (_lock) return _runId; } }
        public bool IsTerminal { get { lock (_lock) return _isTerminal; } }

        public void SetRunId(long runId)
        {
            lock (_lock) _runId = runId;
        }

        public void AddEvent(string level, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            lock (_lock) _events.Add(new HostedRunEventDto(level, text));
        }

        public void Complete(
            string state,
            string? message,
            HostedPipelineRunRefusalDto? refusal,
            HostedPipelineRunDto? finalRun,
            HostedPendingSubmissionDto? pendingSubmission)
        {
            lock (_lock)
            {
                _state = state;
                _isTerminal = true;
                _message = message;
                _refusal = refusal;
                _finalRun = finalRun;
                if (finalRun is not null) _runId = finalRun.RunId;
                _pendingSubmission = pendingSubmission;
            }
        }

        public HostedRunOperationDto ToDto(ActiveHostedRun? activeRun)
        {
            lock (_lock)
            {
                var state = _state;
                var runId = _runId;
                var cancelRequested = false;

                if (!_isTerminal && activeRun is not null
                    && activeRun.PipelineId == PipelineId
                    && string.Equals(activeRun.Origin, OwnerOrigin, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(activeRun.TenantId, OwnerTenantId, StringComparison.OrdinalIgnoreCase)
                    && activeRun.UserId == OwnerUserId)
                {
                    state = "running";
                    runId = activeRun.RunId;
                    cancelRequested = activeRun.CancelRequested;
                }

                return new HostedRunOperationDto(
                    OperationId,
                    DocumentId,
                    DocumentPath,
                    OwnerTargetKey,
                    OwnerOrigin,
                    OwnerTenantId,
                    OwnerUserId,
                    OwnerUsername,
                    PipelineId,
                    PipelineName,
                    Version,
                    JobName,
                    state,
                    _isTerminal,
                    runId,
                    cancelRequested,
                    _message,
                    _refusal,
                    _finalRun,
                    _pendingSubmission,
                    _events.ToArray());
            }
        }
    }
}
