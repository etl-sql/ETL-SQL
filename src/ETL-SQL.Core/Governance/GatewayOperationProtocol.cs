using System.Text.Json;

namespace ETL_SQL.Core.Governance;

/// <summary>Transport a Gateway session speaks. One versioned operation model over either.</summary>
public enum GatewayTransport
{
    /// <summary>Bidirectional gRPC streaming over HTTPS. The default.</summary>
    Grpc,

    /// <summary>Typed WebSocket, only where a restrictive proxy forbids gRPC.</summary>
    WebSocket
}

/// <summary>
/// Bounds every typed operation must carry. §11.5 makes deadlines, cancellation, bounded buffering,
/// flow control, maximum sizes, and concurrency limits mandatory — so this record has no
/// "unlimited" representation and validates itself.
/// </summary>
public sealed record GatewayOperationBounds(
    int TimeoutSeconds,
    long MaxRequestBytes,
    long MaxResponseBytes,
    long MaxRows,
    int MaxConcurrentStreams,
    int BufferedBatchLimit)
{
    public static GatewayOperationBounds Default { get; } = new(300, 1 << 20, 1L << 30, 1_000_000, 4, 16);

    public void Validate()
    {
        if (TimeoutSeconds <= 0) throw new GatewayProtocolException("A Gateway operation requires a positive deadline.");
        if (MaxRequestBytes <= 0 || MaxResponseBytes <= 0)
            throw new GatewayProtocolException("A Gateway operation requires positive request and response size limits.");
        if (MaxRows <= 0) throw new GatewayProtocolException("A Gateway operation requires a positive row limit.");
        if (MaxConcurrentStreams <= 0) throw new GatewayProtocolException("A Gateway operation requires a positive concurrency limit.");
        if (BufferedBatchLimit <= 0) throw new GatewayProtocolException("A Gateway operation requires a positive buffering limit.");
    }

    /// <summary>Narrows to the tighter of two bound sets. A resource's registered limits can only restrict, never widen.</summary>
    public GatewayOperationBounds NarrowTo(GatewayResourceLimits limits) => new(
        Math.Min(TimeoutSeconds, limits.TimeoutSeconds),
        MaxRequestBytes,
        Math.Min(MaxResponseBytes, limits.MaxBytes),
        Math.Min(MaxRows, limits.MaxRows),
        Math.Min(MaxConcurrentStreams, limits.MaxConcurrency),
        BufferedBatchLimit);
}

/// <summary>Thrown when a typed operation violates the protocol contract.</summary>
public sealed class GatewayProtocolException(
    string message,
    GatewayOutcomeState? outcomeState = null,
    string? operationId = null) : Exception(message)
{
    public GatewayOutcomeState? OutcomeState { get; } = outcomeState;
    public string? OperationId { get; } = operationId;
}

/// <summary>Whether an operation can change state on the far side. Drives the reconnect rule.</summary>
public enum GatewayOperationEffect
{
    /// <summary>Cannot change far-side state, so a repeat is safe.</summary>
    ReadOnly,

    /// <summary>May change far-side state. A repeat is only safe with a committed outcome.</summary>
    Mutating
}

/// <summary>
/// A typed operation handle. Containers receive one of these per operation, never reusable tunnel
/// authority: it names a resource and an operation class, and it cannot express a host, port, or
/// protocol.
/// </summary>
public sealed record GatewayOperation(
    string OperationId,
    string TenantId,
    string GatewayId,
    string ResourceId,
    GatewayOperationClass Class,
    GatewayOperationEffect Effect,
    GatewayOperationBounds Bounds,
    string CorrelationId,
    string? GatewayNodeId = null,
    DateTimeOffset? DispatchedAtUtc = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(OperationId))
            throw new GatewayProtocolException("A Gateway operation requires an operation ID for reconnect.");
        if (string.IsNullOrWhiteSpace(TenantId))
            throw new GatewayProtocolException("A Gateway operation requires a server-derived tenant.");
        if (Class == GatewayOperationClass.None)
            throw new GatewayProtocolException("A Gateway operation requires an operation class.");
        Bounds.Validate();
    }
}

/// <summary>Durable state of an operation, as the outcome ledger knows it.</summary>
public enum GatewayOutcomeState
{
    /// <summary>Dispatched, no terminal outcome recorded.</summary>
    InFlight,

    /// <summary>Completed and its effect is known to have been applied.</summary>
    Committed,

    /// <summary>Completed without applying its effect. Safe to retry.</summary>
    Failed,

    /// <summary>
    /// The connection dropped and the far-side effect is unknown. Never retried blindly and never
    /// reported as safely failed.
    /// </summary>
    Ambiguous
}

/// <summary>What a reconnecting caller should do with an operation it had in flight.</summary>
public enum GatewayReconnectAction
{
    /// <summary>No record: the operation never reached the Gateway, so dispatch it.</summary>
    Dispatch,

    /// <summary>A terminal committed outcome exists; return it instead of repeating the work.</summary>
    ReturnRecordedOutcome,

    /// <summary>A terminal failure exists and the effect did not apply; retry is safe.</summary>
    RetrySafely,

    /// <summary>Outcome unknown. Surface it for reconciliation; do not retry and do not call it failed.</summary>
    EscalateAmbiguous
}

/// <summary>An operation's durable outcome record.</summary>
public sealed record GatewayOperationOutcome(
    string OperationId,
    string TenantId,
    GatewayOutcomeState State,
    GatewayOperationEffect Effect,
    long RowsProduced = 0,
    string? Detail = null);

/// <summary>
/// The durable outcome ledger reconnect keys off (§11.5).
///
/// <para>The rule this exists to enforce: <b>an ambiguous write is never retried blindly nor
/// reported as safely failed.</b> Those are the two comfortable answers and both are wrong — a blind
/// retry can double-apply a write, and calling it failed tells the caller the data is not there when
/// it may be. The same reasoning the sandbox coordinator already applies to an ambiguous teardown,
/// where uncertain state is retained for reconciliation rather than assumed clean.</para>
///
/// <para>A read-only operation is different and is allowed to simply re-run, because repeating it
/// cannot change anything.</para>
/// </summary>
public sealed class GatewayOutcomeLedger
{
    private readonly Dictionary<(string TenantId, string OperationId), GatewayOperationOutcome> _outcomes = new();
    private readonly Lock _gate = new();
    private readonly string? _persistencePath;

    /// <summary>
    /// Creates an in-memory ledger for tests, or a restart-durable ledger when an absolute local
    /// persistence path is supplied. The file is replaced atomically after every state transition.
    /// </summary>
    public GatewayOutcomeLedger(string? persistencePath = null)
    {
        if (persistencePath is null)
            return;
        if (!Path.IsPathFullyQualified(persistencePath))
            throw new ArgumentException("The Gateway outcome ledger path must be absolute.", nameof(persistencePath));

        _persistencePath = Path.GetFullPath(persistencePath);
        var directory = Path.GetDirectoryName(_persistencePath)
            ?? throw new ArgumentException("The Gateway outcome ledger path has no parent directory.", nameof(persistencePath));
        Directory.CreateDirectory(directory);
        if (!File.Exists(_persistencePath))
            return;

        try
        {
            var records = JsonSerializer.Deserialize<List<GatewayOperationOutcome>>(
                File.ReadAllText(_persistencePath)) ?? [];
            foreach (var record in records)
                _outcomes[Key(record.TenantId, record.OperationId)] = record;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new GatewayProtocolException(
                "The durable Gateway outcome ledger could not be loaded; operations are refused to avoid replaying an ambiguous write.");
        }
    }

    public void RecordDispatched(GatewayOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        operation.Validate();
        lock (_gate)
        {
            if (_outcomes.TryGetValue(Key(operation.TenantId, operation.OperationId), out var existing)
                && existing.State != GatewayOutcomeState.InFlight)
            {
                throw new GatewayProtocolException(
                    $"Operation '{operation.OperationId}' already has a terminal outcome and cannot be re-dispatched.");
            }

            _outcomes[Key(operation.TenantId, operation.OperationId)] = new GatewayOperationOutcome(
                operation.OperationId, operation.TenantId, GatewayOutcomeState.InFlight, operation.Effect);
            PersistLocked();
        }
    }

    public void RecordTerminal(
        string tenantId, string operationId, GatewayOutcomeState state, long rowsProduced = 0, string? detail = null)
    {
        if (state == GatewayOutcomeState.InFlight)
            throw new GatewayProtocolException("InFlight is not a terminal outcome.");

        lock (_gate)
        {
            if (!_outcomes.TryGetValue(Key(tenantId, operationId), out var existing))
                throw new GatewayProtocolException($"Operation '{operationId}' was never dispatched for this tenant.");

            // A committed outcome is final. Letting a later ambiguous report overwrite it would turn
            // a known-good write back into an unknown one.
            if (existing.State == GatewayOutcomeState.Committed && state != GatewayOutcomeState.Committed)
                throw new GatewayProtocolException(
                    $"Operation '{operationId}' is already committed and its outcome cannot be downgraded.");

            _outcomes[Key(tenantId, operationId)] =
                existing with { State = state, RowsProduced = rowsProduced, Detail = detail };
            PersistLocked();
        }
    }

    /// <summary>Decides what a reconnecting caller may do. The only place that judgement is made.</summary>
    public GatewayReconnectAction DecideReconnect(string tenantId, string operationId)
    {
        lock (_gate)
        {
            if (!_outcomes.TryGetValue(Key(tenantId, operationId), out var existing))
                return GatewayReconnectAction.Dispatch;

            return existing.State switch
            {
                GatewayOutcomeState.Committed => GatewayReconnectAction.ReturnRecordedOutcome,
                GatewayOutcomeState.Failed => GatewayReconnectAction.RetrySafely,

                // A dropped connection on a mutating operation is ambiguous, not failed. A read-only
                // operation may simply be re-run, because repeating it changes nothing.
                GatewayOutcomeState.Ambiguous or GatewayOutcomeState.InFlight =>
                    existing.Effect == GatewayOperationEffect.ReadOnly
                        ? GatewayReconnectAction.Dispatch
                        : GatewayReconnectAction.EscalateAmbiguous,

                _ => GatewayReconnectAction.EscalateAmbiguous
            };
        }
    }

    public GatewayOperationOutcome? Find(string tenantId, string operationId)
    {
        lock (_gate)
        {
            return _outcomes.GetValueOrDefault(Key(tenantId, operationId));
        }
    }

    // Tenant is half of the key, so equal operation IDs across tenants are different operations and
    // one tenant can never observe or resolve another's outcome. A tuple key rather than a
    // concatenated string: with a string, ("a", "bc") and ("ab", "c") would collide into one
    // another's outcome, and that class of bug cannot occur here.
    private static (string, string) Key(string tenantId, string operationId) => (tenantId, operationId);

    private void PersistLocked()
    {
        if (_persistencePath is null)
            return;

        var temporaryPath = _persistencePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_outcomes.Values));
            File.Move(temporaryPath, _persistencePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporaryPath); } catch { }
            throw new GatewayProtocolException(
                "The durable Gateway outcome ledger could not record the operation; execution is refused.");
        }
    }
}
