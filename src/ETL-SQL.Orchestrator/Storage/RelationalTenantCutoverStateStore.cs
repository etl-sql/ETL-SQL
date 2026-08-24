using System.Data.Common;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Core.Portability;

namespace ETL_SQL.Orchestrator.Storage;

public partial class RelationalJobHistoryStore
{
    /// <summary>
    /// Establishes the initial source-owned authority record. The tenant comes from verified server
    /// context; replay is idempotent and never replaces an existing cutover operation.
    /// </summary>
    public async Task<TenantCutoverState> EnsureTenantCutoverSourceAuthorityAsync(
        TenantContext tenant, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        if (tenant.Origin is not (TenantContextOrigin.VerifiedCredential or TenantContextOrigin.PlatformAuthorization))
            throw new UnauthorizedAccessException("Cutover authority requires verified tenant context.");
        await EnsureInitializedAsync().ConfigureAwait(false);
        var tenantId = tenant.Tenant.Value;
        var existing = await ReadAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (existing is not null) return existing;
        var initial = new TenantCutoverState(tenantId, "source", TenantExecutionAuthorityLocation.Source,
            1, SourceSchedulesEnabled: true, TargetSchedulesEnabled: false,
            SourceActiveExecutions: 0, now, Version: 1);
        if (!await TryWriteAsync(null, initial, cancellationToken).ConfigureAwait(false))
            return await ReadAsync(tenantId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Cutover authority initialization lost without a durable winner.");
        return (await ReadAsync(tenantId, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<TenantCutoverState?> ReadAsync(
        string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        await EnsureInitializedAsync().ConfigureAwait(false);
        await using var connection = _dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadCutoverAsync(connection, null, tenantId, refreshActive: true, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> TryWriteAsync(
        TenantCutoverState? expected, TenantCutoverState next,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (expected is not null && !string.Equals(expected.TenantId, next.TenantId, StringComparison.Ordinal))
            throw new ArgumentException("Cutover compare-and-swap cannot change tenant identity.", nameof(next));
        await EnsureInitializedAsync().ConfigureAwait(false);
        await using var connection = _dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (expected is null)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = @"
                    INSERT INTO TenantPortabilityCutovers
                        (TenantId, OperationId, Authority, FenceEpoch, SourceSchedulesEnabled,
                         TargetSchedulesEnabled, SourceActiveExecutions, UpdatedAtUtc, Version)
                    VALUES (@tenant, @operation, @authority, @fence, @source, @target, @active, @now, 1)
                    ON CONFLICT (TenantId) DO NOTHING;";
                AddCutoverParameters(insert, next, sourceActive: next.SourceActiveExecutions);
                var inserted = await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return inserted;
            }

            var current = await ReadCutoverAsync(connection, transaction, next.TenantId,
                refreshActive: false, cancellationToken).ConfigureAwait(false);
            if (current is null || current.Version != expected.Version)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            var active = await CountActiveCutoverJobsAsync(connection, transaction, next.TenantId,
                next.UpdatedAtUtc, cancellationToken).ConfigureAwait(false);
            if (expected.Authority == TenantExecutionAuthorityLocation.Source
                && next.Authority == TenantExecutionAuthorityLocation.None)
            {
                await RememberAndDisableCutoverJobsAsync(connection, transaction, next, cancellationToken)
                    .ConfigureAwait(false);
                active = await CountActiveCutoverJobsAsync(connection, transaction, next.TenantId,
                    next.UpdatedAtUtc, cancellationToken).ConfigureAwait(false);
            }
            if (expected.Authority == TenantExecutionAuthorityLocation.None
                && next.Authority == TenantExecutionAuthorityLocation.Target && active != 0)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = @"
                UPDATE TenantPortabilityCutovers
                   SET OperationId = @operation, Authority = @authority, FenceEpoch = @fence,
                       SourceSchedulesEnabled = @source, TargetSchedulesEnabled = @target,
                       SourceActiveExecutions = @active, UpdatedAtUtc = @now, Version = Version + 1
                 WHERE TenantId = @tenant AND Version = @expectedVersion;";
            AddCutoverParameters(update, next, active);
            update.AddParam("@expectedVersion", expected.Version);
            var won = await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
            if (won) await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            else await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return won;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<TenantCutoverState?> ReadCutoverAsync(
        DbConnection connection, DbTransaction? transaction, string tenantId, bool refreshActive,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            SELECT OperationId, Authority, FenceEpoch, SourceSchedulesEnabled,
                   TargetSchedulesEnabled, SourceActiveExecutions, UpdatedAtUtc, Version
              FROM TenantPortabilityCutovers WHERE TenantId = @tenant;";
        command.AddParam("@tenant", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        var state = new TenantCutoverState(tenantId, reader.GetString(0),
            Enum.Parse<TenantExecutionAuthorityLocation>(reader.GetString(1), ignoreCase: false),
            reader.GetInt64(2), reader.GetInt32(3) != 0, reader.GetInt32(4) != 0,
            reader.GetInt32(5), ParseDateTimeOffset(reader.GetValue(6)), reader.GetInt64(7));
        await reader.DisposeAsync().ConfigureAwait(false);
        if (!refreshActive || state.Authority == TenantExecutionAuthorityLocation.Target) return state;
        var active = await CountActiveCutoverJobsAsync(connection, transaction, tenantId,
            DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        return state with { SourceActiveExecutions = active };
    }

    private static async Task RememberAndDisableCutoverJobsAsync(
        DbConnection connection, DbTransaction transaction, TenantCutoverState next,
        CancellationToken cancellationToken)
    {
        await using (var remember = connection.CreateCommand())
        {
            remember.Transaction = transaction;
            remember.CommandText = @"
                INSERT INTO TenantPortabilityFencedJobs (TenantId, OperationId, JobName)
                SELECT @tenant, @operation, Name FROM Jobs
                 WHERE TenantId = @tenant AND IsEnabled = 1
                ON CONFLICT (TenantId, OperationId, JobName) DO NOTHING;";
            remember.AddParam("@tenant", next.TenantId);
            remember.AddParam("@operation", next.OperationId);
            await remember.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using var disable = connection.CreateCommand();
        disable.Transaction = transaction;
        disable.CommandText = @"
            UPDATE Jobs SET IsEnabled = 0, Version = Version + 1, ModifiedBy = @operation
             WHERE TenantId = @tenant AND IsEnabled = 1;";
        disable.AddParam("@tenant", next.TenantId);
        disable.AddParam("@operation", next.OperationId);
        await disable.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> CountActiveCutoverJobsAsync(
        DbConnection connection, DbTransaction? transaction, string tenantId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            SELECT COUNT(*) FROM Jobs
             WHERE TenantId = @tenant AND LeaseOwner IS NOT NULL
               AND LeaseExpiresAt IS NOT NULL AND LeaseExpiresAt > @now;";
        command.AddParam("@tenant", tenantId);
        command.AddParam("@now", now.UtcDateTime.ToString("O"));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private static void AddCutoverParameters(DbCommand command, TenantCutoverState state, int sourceActive)
    {
        command.AddParam("@tenant", state.TenantId);
        command.AddParam("@operation", state.OperationId);
        command.AddParam("@authority", state.Authority.ToString());
        command.AddParam("@fence", state.FenceEpoch);
        command.AddParam("@source", state.SourceSchedulesEnabled ? 1 : 0);
        command.AddParam("@target", state.TargetSchedulesEnabled ? 1 : 0);
        command.AddParam("@active", sourceActive);
        command.AddParam("@now", state.UpdatedAtUtc.ToString("O"));
    }
}
