namespace ETL_SQL.Core.Portability;

public enum TenantExecutionAuthorityLocation { Source, None, Target }

public sealed record TenantCutoverState(
    string TenantId,
    string OperationId,
    TenantExecutionAuthorityLocation Authority,
    long FenceEpoch,
    bool SourceSchedulesEnabled,
    bool TargetSchedulesEnabled,
    int SourceActiveExecutions,
    DateTimeOffset UpdatedAtUtc,
    long Version = 0);

/// <summary>Durable compare-and-swap store for tenant cutover ownership.</summary>
public interface ITenantCutoverStateStore
{
    Task<TenantCutoverState?> ReadAsync(string tenantId, CancellationToken cancellationToken = default);
    Task<bool> TryWriteAsync(TenantCutoverState? expected, TenantCutoverState next,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Transfers scheduling/execution authority through an explicit no-owner state. The target can never
/// be enabled while the source owns authority or retains active execution leases.
/// </summary>
public static class TenantCutoverAuthority
{
    public static async Task<TenantCutoverState> FenceSourceAsync(
        ITenantCutoverStateStore store, string tenantId, string operationId, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var current = await Required(store, tenantId, cancellationToken).ConfigureAwait(false);
        if (current.Authority == TenantExecutionAuthorityLocation.None
            && current.OperationId == operationId) return current;
        if (current.Authority != TenantExecutionAuthorityLocation.Source)
            throw new InvalidOperationException($"Tenant '{tenantId}' source does not own execution authority.");
        var next = current with
        {
            OperationId = operationId,
            Authority = TenantExecutionAuthorityLocation.None,
            FenceEpoch = checked(current.FenceEpoch + 1),
            SourceSchedulesEnabled = false,
            TargetSchedulesEnabled = false,
            UpdatedAtUtc = now
        };
        if (!await store.TryWriteAsync(current, next, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Cutover state changed concurrently; no authority was transferred.");
        return next;
    }

    public static async Task<TenantCutoverState> TransferToTargetAsync(
        ITenantCutoverStateStore store, string tenantId, string operationId, long expectedFenceEpoch,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var current = await Required(store, tenantId, cancellationToken).ConfigureAwait(false);
        if (current.Authority == TenantExecutionAuthorityLocation.Target
            && current.OperationId == operationId && current.FenceEpoch == expectedFenceEpoch) return current;
        if (current.Authority != TenantExecutionAuthorityLocation.None
            || current.OperationId != operationId
            || current.FenceEpoch != expectedFenceEpoch)
            throw new InvalidOperationException("Target activation does not match the active source fence.");
        if (current.SourceActiveExecutions != 0)
            throw new InvalidOperationException(
                $"Source retains {current.SourceActiveExecutions} active execution lease(s); target schedules remain disabled.");
        var next = current with
        {
            Authority = TenantExecutionAuthorityLocation.Target,
            SourceSchedulesEnabled = false,
            TargetSchedulesEnabled = true,
            UpdatedAtUtc = now
        };
        if (!await store.TryWriteAsync(current, next, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Cutover state changed concurrently; target schedules remain disabled.");
        return next;
    }

    private static async Task<TenantCutoverState> Required(
        ITenantCutoverStateStore store, string tenantId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        return await store.ReadAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant '{tenantId}' has no execution-authority record.");
    }
}
