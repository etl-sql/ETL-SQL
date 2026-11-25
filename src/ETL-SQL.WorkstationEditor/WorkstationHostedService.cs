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
    PipelineRunRefusalDto? Refusal,
    PipelineRunDto? FinalRun,
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
    string? DocumentId = null,
    string? DocumentPath = null);

public sealed record HostedRecoverRunRequest(string? Mode = "check");

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

    public Task<RunnablePipelinePageDto> ListRunnablePipelinesAsync(string? query, int afterId, CancellationToken ct = default) =>
        _client.GetRunnablePipelinesAsync(query, afterId, ct);

    public Task<RunnablePipelineDto> GetPipelineRevisionAsync(int pipelineId, CancellationToken ct = default) =>
        _client.GetRunnableRevisionAsync(pipelineId, ct);

    public async Task<HostedRunOperationDto> StartRunAsync(HostedStartRunRequest request, CancellationToken ct = default)
    {
        if (request.PipelineId <= 0)
            throw new ArgumentException("A valid saved pipeline ID is required.");

        var session = _sessions.Session
            ?? throw new HostedSessionExpiredException("Hosted Portal session is not signed in. Sign in again or switch to Local (Offline).");

        var existingPending = _coordinator.GetPendingSubmissionForCurrentCaller();
        if (existingPending is not null)
            throw new InvalidOperationException($"Check or recover unconfirmed submission {existingPending.IdempotencyKey} before starting another run.");

        var revision = await _client.GetRunnableRevisionAsync(request.PipelineId, ct, session);
        if (!_sessions.IsCurrentSession(session))
            throw new HostedSessionExpiredException("Hosted target or sign-in changed while preparing the run.");

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

        return op.ToDto(_coordinator.ActiveRun, MapPending);
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
                op.Complete("error", safe, null, null, MapPending(pending));
            }
            finally
            {
                if (ReferenceEquals(_currentOperation, op))
                    _currentOperation = null;
            }
        });

        return op.ToDto(_coordinator.ActiveRun, MapPending);
    }

    public HostedRunOperationDto? GetOperation(string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId) || !_operations.TryGetValue(operationId, out var op))
            return null;
        return op.ToDto(_coordinator.ActiveRun, MapPending);
    }

    public async Task<(string Message, HostedStatusDto Status)> RequestStopAsync(CancellationToken ct = default)
    {
        var current = _currentOperation;
        var callbacks = current is null ? null : CreateCallbacks(current);
        var message = await _coordinator.RequestStopAsync(callbacks, ct);
        return (Redact(message), GetStatus());
    }

    public string Redact(string? message) =>
        HostedSecretRedactor.Redact(message ?? string.Empty, _sessions.GetActiveSecrets());

    public async ValueTask DisposeAsync()
    {
        await _stoppingRegistration.DisposeAsync();
        await _sessions.DisposeAsync();
    }

    private HostedRunCallbacks CreateCallbacks(TrackedOperation op) => new(
        OnInfo: msg => op.AddEvent("info", Redact(msg)),
        OnWarning: msg => op.AddEvent("warn", Redact(msg)),
        OnError: msg => op.AddEvent("error", Redact(msg)),
        OnStatement: (stmt, index) =>
        {
            var preview = stmt.Statement.Length > 120 ? stmt.Statement[..120] + "…" : stmt.Statement;
            var status = stmt.Failed ? "Failed" : "Ran";
            op.AddEvent(stmt.Failed ? "error" : "info", $"[{index}] {status}: {preview} ({stmt.RowsProcessed} rows, {stmt.DurationMs} ms)");
        });

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

        var pendingDto = outcome.PendingSubmission is null ? null : ToPendingDto(outcome.PendingSubmission);
        op.Complete(state, Redact(outcome.Message), outcome.Refusal, outcome.FinalRun, pendingDto);
    }

    private HostedPendingSubmissionDto MapPending(PendingSubmissionRecord pending) => ToPendingDto(pending);

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
        private PipelineRunRefusalDto? _refusal;
        private PipelineRunDto? _finalRun;
        private HostedPendingSubmissionDto? _pendingSubmission;

        public string OperationId { get; } = OperationId;
        public string? DocumentId { get; } = DocumentId;
        public string? DocumentPath { get; } = DocumentPath;

        public void AddEvent(string level, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            lock (_lock) _events.Add(new HostedRunEventDto(level, text));
        }

        public void Complete(
            string state,
            string? message,
            PipelineRunRefusalDto? refusal,
            PipelineRunDto? finalRun,
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

        public HostedRunOperationDto ToDto(
            ActiveHostedRun? activeRun,
            Func<PendingSubmissionRecord, HostedPendingSubmissionDto> mapPending)
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
