using ETL_SQL.Core.Governance;

namespace ETL_SQL.Gateway;

/// <summary>
/// Executes one bounded typed operation against a Gateway-local resource. Supplied by the host so
/// the dispatcher stays free of connector dependencies; the Gateway resolves the credential and the
/// target itself and hands the executor only what it needs.
/// </summary>
public interface IGatewayResourceExecutor
{
    Task<GatewayExecutionResult> ExecuteAsync(
        GatewayResource resource,
        GatewayOperationClass operationClass,
        string? request,
        IReadOnlyList<string>? parameters,
        GatewayOperationBounds bounds,
        CancellationToken cancellationToken);
}

/// <summary>A bounded result: columns plus row batches already clipped to the operation's limits.</summary>
public sealed record GatewayExecutionResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    bool Truncated = false);

/// <summary>
/// The Gateway-side half of the typed operation protocol.
///
/// <para>Everything that makes the Gateway a policy enforcement point rather than a relay happens
/// here: the requested resource must be approved locally, the operation class must be one the
/// resource permits, the cloud-supplied bounds are narrowed by the resource's registered limits and
/// never widened by them, and the outcome is recorded before the result is returned so a reconnect
/// cannot re-run a write. A refusal names the resource ID and nothing else — never the local target,
/// never the credential reference.</para>
/// </summary>
public sealed class GatewayOperationDispatcher(
    GatewayResourceRegistry registry,
    IGatewayResourceExecutor executor,
    GatewayOutcomeLedger ledger)
{
    public async Task<IReadOnlyList<GatewayFrame>> DispatchAsync(
        GatewayFrame frame, string sessionTenantId, string sessionGatewayId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Kind != GatewayFrameKind.Operation)
            return [GatewayFrame.Fault(frame.OperationId, "Only operation frames are accepted on an established session.")];
        if (string.IsNullOrWhiteSpace(frame.OperationId))
            return [GatewayFrame.Fault(null, "An operation frame requires an operation ID.")];
        if (string.IsNullOrWhiteSpace(frame.ResourceId))
            return [GatewayFrame.Fault(frame.OperationId, "An operation frame requires a resource ID.")];

        // The session's identity is authoritative, not anything the frame claims. A frame naming a
        // different tenant or Gateway is refused rather than served under the session's authority.
        if (!string.IsNullOrWhiteSpace(frame.TenantId)
            && !string.Equals(frame.TenantId, sessionTenantId, StringComparison.Ordinal))
        {
            return [GatewayFrame.Fault(frame.OperationId, "The operation names a different tenant than the session.")];
        }
        if (!string.IsNullOrWhiteSpace(frame.GatewayId)
            && !string.Equals(frame.GatewayId, sessionGatewayId, StringComparison.Ordinal))
        {
            return [GatewayFrame.Fault(frame.OperationId, "The operation names a different Gateway than the session.")];
        }

        var operation = new GatewayOperation(
            frame.OperationId!, sessionTenantId, sessionGatewayId, frame.ResourceId!,
            frame.OperationClass, frame.Effect,
            frame.Bounds ?? GatewayOperationBounds.Default,
            frame.CorrelationId ?? frame.OperationId!);

        try
        {
            operation.Validate();
        }
        catch (GatewayProtocolException ex)
        {
            return [GatewayFrame.Fault(frame.OperationId, ex.Message)];
        }

        // Reconnect: a committed write is returned, never repeated; an ambiguous one is escalated
        // rather than silently re-run or reported as a clean failure.
        switch (ledger.DecideReconnect(sessionTenantId, operation.OperationId))
        {
            case GatewayReconnectAction.ReturnRecordedOutcome:
                var recorded = ledger.Find(sessionTenantId, operation.OperationId)!;
                return
                [
                    new GatewayFrame
                    {
                        Kind = GatewayFrameKind.Complete,
                        OperationId = operation.OperationId,
                        OutcomeState = recorded.State,
                        RowsProduced = recorded.RowsProduced,
                        Reason = "Replayed a recorded outcome; the operation was not executed again."
                    }
                ];

            case GatewayReconnectAction.EscalateAmbiguous:
                return
                [
                    new GatewayFrame
                    {
                        Kind = GatewayFrameKind.Fault,
                        OperationId = operation.OperationId,
                        OutcomeState = GatewayOutcomeState.Ambiguous,
                        Reason = "The previous attempt's outcome is unknown; it is neither retried nor reported as failed."
                    }
                ];
        }

        GatewayResource resource;
        try
        {
            resource = await registry
                .ResolveForExecutionAsync(operation.ResourceId, operation.Class, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GatewayResourceException ex)
        {
            return [GatewayFrame.Fault(operation.OperationId, ex.Message)];
        }

        // A resource's registered limits can only tighten what the cloud asked for.
        var bounds = operation.Bounds.NarrowTo(resource.Limits);
        ledger.RecordDispatched(operation with { Bounds = bounds });

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(bounds.TimeoutSeconds));

        try
        {
            var result = await executor
                .ExecuteAsync(resource, operation.Class, frame.Request, frame.Parameters, bounds, deadline.Token)
                .ConfigureAwait(false);

            var rows = result.Rows.Count > bounds.MaxRows
                ? result.Rows.Take((int)Math.Min(bounds.MaxRows, int.MaxValue)).ToList()
                : result.Rows;

            ledger.RecordTerminal(
                sessionTenantId, operation.OperationId, GatewayOutcomeState.Committed, rows.Count);

            return
            [
                new GatewayFrame
                {
                    Kind = GatewayFrameKind.RowBatch,
                    OperationId = operation.OperationId,
                    Columns = result.Columns,
                    Rows = rows
                },
                new GatewayFrame
                {
                    Kind = GatewayFrameKind.Complete,
                    OperationId = operation.OperationId,
                    OutcomeState = GatewayOutcomeState.Committed,
                    RowsProduced = rows.Count,
                    Reason = rows.Count < result.Rows.Count || result.Truncated
                        ? "Result truncated at the operation row limit."
                        : null
                }
            ];
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // A deadline on a mutating operation leaves the far side unknown. Recording it as Failed
            // would tell the caller the write did not happen, which is not something we know.
            var state = operation.Effect == GatewayOperationEffect.Mutating
                ? GatewayOutcomeState.Ambiguous
                : GatewayOutcomeState.Failed;
            ledger.RecordTerminal(sessionTenantId, operation.OperationId, state);
            return
            [
                new GatewayFrame
                {
                    Kind = GatewayFrameKind.Fault,
                    OperationId = operation.OperationId,
                    OutcomeState = state,
                    Reason = "The operation exceeded its deadline."
                }
            ];
        }
        catch (Exception ex) when (ex is not GatewayProtocolException)
        {
            // The executor talks to a real local system, whose exception text can carry the host or
            // the credential. Only a fixed message crosses back to the cloud side.
            var state = operation.Effect == GatewayOperationEffect.Mutating
                ? GatewayOutcomeState.Ambiguous
                : GatewayOutcomeState.Failed;
            ledger.RecordTerminal(sessionTenantId, operation.OperationId, state);
            return
            [
                new GatewayFrame
                {
                    Kind = GatewayFrameKind.Fault,
                    OperationId = operation.OperationId,
                    OutcomeState = state,
                    Reason = "The operation failed on the Gateway."
                }
            ];
        }
    }
}
