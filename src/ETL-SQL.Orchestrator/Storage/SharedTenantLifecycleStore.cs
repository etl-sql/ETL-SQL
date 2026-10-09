using System.Data.Common;
using System.Globalization;
using ETL_SQL.Core.Multitenancy;

namespace ETL_SQL.Orchestrator.Storage;

public enum SharedTenantLifecycleKind { Provision, Upgrade, Delete }

public sealed record SharedTenantLifecycleCommand(
    string OperationId,
    SharedTenantLifecycleKind Kind,
    string PlatformOperator,
    string AuthorizationReference,
    string TargetRelease,
    int MaxConcurrentJobs,
    int MaxStorageMb,
    int MaxReportSessions,
    DateTimeOffset NowUtc,
    int ExternalActiveWork = 0);

public sealed record SharedTenantControlPlaneState(
    string TenantId,
    string State,
    string ActiveRelease,
    int MaxConcurrentJobs,
    int MaxStorageMb,
    int MaxReportSessions,
    long FenceEpoch,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? DeletedAtUtc,
    long Version);

public sealed record SharedTenantLifecycleResult(
    string OperationId,
    string TenantId,
    SharedTenantLifecycleKind Kind,
    string Status,
    string Phase,
    int ActiveWork,
    SharedTenantControlPlaneState State);

public interface ISharedTenantLifecycleStore
{
    Task<SharedTenantLifecycleResult> ApplySharedTenantLifecycleAsync(
        TenantContext tenant,
        SharedTenantLifecycleCommand command,
        CancellationToken cancellationToken = default);

    Task<SharedTenantControlPlaneState?> GetSharedTenantStateAsync(
        TenantContext tenant,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates the control-plane record of every tenant, for fleet rollout planning. It reads
    /// operational metadata only — tenant id, lifecycle state, release, capacity assignment — and
    /// confers no authority over any tenant: each cutover still needs its own signed grant.
    /// </summary>
    Task<IReadOnlyList<SharedTenantControlPlaneState>> ListSharedTenantStatesAsync(
        FleetInventoryAuthorization authorization,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

public partial class RelationalJobHistoryStore
{
    public async Task<SharedTenantControlPlaneState?> GetSharedTenantStateAsync(
        TenantContext tenant,
        CancellationToken cancellationToken = default)
    {
        var tenantId = RequireLifecycleTenant(tenant);
        await EnsureInitializedAsync();
        await using var connection = _dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await ReadStateAsync(connection, null, tenantId, cancellationToken);
    }

    public async Task<IReadOnlyList<SharedTenantControlPlaneState>> ListSharedTenantStatesAsync(
        FleetInventoryAuthorization authorization,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        if (authorization.IsExpiredAt(now))
            throw new UnauthorizedAccessException(
                "The fleet inventory authorization has expired; enumerating the tenant population " +
                "requires a current, attributed authorization.");

        await EnsureInitializedAsync();
        await using var connection = _dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT tenant_id, state, active_release, max_concurrent_jobs, max_storage_mb,
                   max_report_sessions, fence_epoch, updated_at_utc, deleted_at_utc, version
              FROM shared_tenant_control_planes ORDER BY tenant_id;";
        var states = new List<SharedTenantControlPlaneState>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            states.Add(new(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                reader.GetInt32(4), reader.GetInt32(5), reader.GetInt64(6),
                ParseDateTimeOffset(reader.GetValue(7)),
                reader.IsDBNull(8) ? null : ParseDateTimeOffset(reader.GetValue(8)), reader.GetInt64(9)));
        }
        return states;
    }

    public async Task<SharedTenantLifecycleResult> ApplySharedTenantLifecycleAsync(
        TenantContext tenant,
        SharedTenantLifecycleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenantId = RequireLifecycleTenant(tenant);
        ValidateCommand(command);
        await EnsureInitializedAsync();

        await using var connection = _dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var existingOperation = await ReadOperationAsync(
                connection, transaction, command.OperationId, cancellationToken);
            var state = await ReadStateAsync(connection, transaction, tenantId, cancellationToken);
            if (existingOperation is not null)
                ValidateReplay(existingOperation, tenantId, command);
            else
            {
                if (command.Kind == SharedTenantLifecycleKind.Provision && state is not null)
                    throw new InvalidOperationException(
                        $"Tenant '{tenantId}' already has a {state.State} Shared control-plane assignment.");
                if (command.Kind != SharedTenantLifecycleKind.Provision
                    && (state is null || state.State != "Active"))
                    throw new InvalidOperationException(
                        $"Tenant '{tenantId}' is not active or is fenced by another lifecycle operation.");
                await InsertOperationAsync(connection, transaction, tenantId, command, cancellationToken);
            }
            SharedTenantLifecycleResult result;
            if (command.Kind == SharedTenantLifecycleKind.Provision)
                result = await ProvisionAsync(connection, transaction, tenantId, command, state, cancellationToken);
            else if (command.Kind == SharedTenantLifecycleKind.Upgrade)
                result = await UpgradeAsync(connection, transaction, tenantId, command, state, cancellationToken);
            else
                result = await DeleteAsync(connection, transaction, tenantId, command, state, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<SharedTenantLifecycleResult> ProvisionAsync(
        DbConnection connection, DbTransaction transaction, string tenantId,
        SharedTenantLifecycleCommand command, SharedTenantControlPlaneState? state,
        CancellationToken cancellationToken)
    {
        if (state is not null)
        {
            if (state.State == "Active"
                && state.ActiveRelease == command.TargetRelease
                && state.MaxConcurrentJobs == command.MaxConcurrentJobs
                && state.MaxStorageMb == command.MaxStorageMb
                && state.MaxReportSessions == command.MaxReportSessions)
            {
                await CompleteOperationAsync(connection, transaction, command.OperationId, command.NowUtc, cancellationToken);
                return new(command.OperationId, tenantId, command.Kind, "Completed", "Active", 0, state);
            }
            throw new InvalidOperationException(
                $"Tenant '{tenantId}' already has a {state.State} Shared control-plane assignment.");
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = @"
                INSERT INTO shared_tenant_control_planes
                    (tenant_id, state, active_release, max_concurrent_jobs, max_storage_mb,
                     max_report_sessions, fence_epoch, created_at_utc, updated_at_utc, version)
                VALUES (@tenant, 'Active', @release, @jobs, @storage, @reports, 1, @now, @now, 1);";
            AddCommand(insert, command, tenantId);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await CompleteOperationAsync(connection, transaction, command.OperationId, command.NowUtc, cancellationToken);
        var created = await ReadStateAsync(connection, transaction, tenantId, cancellationToken)
            ?? throw new InvalidOperationException("Shared tenant provisioning did not persist its assignment.");
        return new(command.OperationId, tenantId, command.Kind, "Completed", "Active", 0, created);
    }

    private async Task<SharedTenantLifecycleResult> UpgradeAsync(
        DbConnection connection, DbTransaction transaction, string tenantId,
        SharedTenantLifecycleCommand command, SharedTenantControlPlaneState? state,
        CancellationToken cancellationToken)
    {
        if (state is null || state.State == "Deleted")
            throw new InvalidOperationException($"Tenant '{tenantId}' is not provisioned.");

        await FenceJobsAsync(connection, transaction, tenantId, command, "Upgrading", cancellationToken);
        var active = checked(await CountActiveJobsAsync(
            connection, transaction, tenantId, command.NowUtc, cancellationToken)
            + command.ExternalActiveWork);
        if (active > 0)
        {
            var draining = await ReadStateAsync(connection, transaction, tenantId, cancellationToken)!;
            return new(command.OperationId, tenantId, command.Kind, "Draining", "OrchestratorDrain", active, draining!);
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = @"
                UPDATE shared_tenant_control_planes
                   SET state = 'Active', active_release = @release,
                       max_concurrent_jobs = @jobs, max_storage_mb = @storage,
                       max_report_sessions = @reports, updated_at_utc = @now,
                       version = version + 1
                 WHERE tenant_id = @tenant AND state = 'Upgrading';";
            AddCommand(update, command, tenantId);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("Shared tenant upgrade lost its lifecycle fence.");
        }
        await RestoreFencedJobsAsync(connection, transaction, tenantId, command.OperationId, cancellationToken);
        await CompleteOperationAsync(connection, transaction, command.OperationId, command.NowUtc, cancellationToken);
        var completed = await ReadStateAsync(connection, transaction, tenantId, cancellationToken)!;
        return new(command.OperationId, tenantId, command.Kind, "Completed", "Active", 0, completed!);
    }

    private async Task<SharedTenantLifecycleResult> DeleteAsync(
        DbConnection connection, DbTransaction transaction, string tenantId,
        SharedTenantLifecycleCommand command, SharedTenantControlPlaneState? state,
        CancellationToken cancellationToken)
    {
        if (state is null)
            throw new InvalidOperationException($"Tenant '{tenantId}' is not provisioned.");
        if (state.State == "Deleted")
        {
            await CompleteOperationAsync(connection, transaction, command.OperationId, command.NowUtc, cancellationToken);
            return new(command.OperationId, tenantId, command.Kind, "Completed", "Deleted", 0, state);
        }

        await FenceJobsAsync(connection, transaction, tenantId, command, "Deleting", cancellationToken);
        var active = checked(await CountActiveJobsAsync(
            connection, transaction, tenantId, command.NowUtc, cancellationToken)
            + command.ExternalActiveWork);
        if (active > 0)
        {
            var draining = await ReadStateAsync(connection, transaction, tenantId, cancellationToken)!;
            return new(command.OperationId, tenantId, command.Kind, "Draining", "OrchestratorDrain", active, draining!);
        }

        await DeleteTenantJobsAsync(connection, transaction, tenantId, cancellationToken);
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = @"
                UPDATE shared_tenant_control_planes
                   SET state = 'Deleted', deleted_at_utc = @now, updated_at_utc = @now,
                       fence_epoch = fence_epoch + 1, version = version + 1
                 WHERE tenant_id = @tenant AND state = 'Deleting';";
            update.AddParam("@tenant", tenantId);
            update.AddParam("@now", command.NowUtc.ToString("O"));
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("Shared tenant deletion lost its lifecycle fence.");
        }
        await CompleteOperationAsync(connection, transaction, command.OperationId, command.NowUtc, cancellationToken);
        var deleted = await ReadStateAsync(connection, transaction, tenantId, cancellationToken)!;
        return new(command.OperationId, tenantId, command.Kind, "Completed", "Deleted", 0, deleted!);
    }

    private static string RequireLifecycleTenant(TenantContext tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        if (tenant.Origin is not (TenantContextOrigin.VerifiedCredential or TenantContextOrigin.PlatformAuthorization))
            throw new UnauthorizedAccessException(
                "Shared lifecycle tenant authority must come from a verified platform assertion.");
        return tenant.Tenant.Value;
    }

    private static void ValidateCommand(SharedTenantLifecycleCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.OperationId) || command.OperationId.Length > 64
            || string.IsNullOrWhiteSpace(command.PlatformOperator)
            || string.IsNullOrWhiteSpace(command.AuthorizationReference)
            || string.IsNullOrWhiteSpace(command.TargetRelease)
            || command.TargetRelease.Length > 256
            || command.MaxConcurrentJobs < 1 || command.MaxStorageMb < 128
            || command.MaxReportSessions < 1 || command.ExternalActiveWork < 0)
            throw new ArgumentException("Shared lifecycle command is incomplete or contains invalid capacity.");
    }

    private sealed record StoredOperation(
        string TenantId, string Kind, string PlatformOperator, string AuthorizationReference,
        string? TargetRelease, int? Jobs, int? Storage, int? Reports, string Status);

    private static void ValidateReplay(
        StoredOperation stored, string tenantId, SharedTenantLifecycleCommand command)
    {
        if (stored.TenantId != tenantId || stored.Kind != command.Kind.ToString()
            || stored.PlatformOperator != command.PlatformOperator
            || stored.AuthorizationReference != command.AuthorizationReference
            || stored.TargetRelease != command.TargetRelease
            || stored.Jobs != command.MaxConcurrentJobs || stored.Storage != command.MaxStorageMb
            || stored.Reports != command.MaxReportSessions)
            throw new InvalidOperationException(
                "The lifecycle operation id was already used for a different tenant or mutation.");
    }

    private static async Task<StoredOperation?> ReadOperationAsync(
        DbConnection connection, DbTransaction transaction, string operationId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            SELECT tenant_id, kind, platform_operator, authorization_reference, target_release,
                   target_max_concurrent_jobs, target_max_storage_mb, target_max_report_sessions, status
              FROM shared_tenant_lifecycle_operations WHERE operation_id = @operation;";
        command.AddParam("@operation", operationId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5),
            reader.IsDBNull(6) ? null : reader.GetInt32(6),
            reader.IsDBNull(7) ? null : reader.GetInt32(7), reader.GetString(8));
    }

    private static async Task InsertOperationAsync(
        DbConnection connection, DbTransaction transaction, string tenantId,
        SharedTenantLifecycleCommand command, CancellationToken ct)
    {
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = @"
            INSERT INTO shared_tenant_lifecycle_operations
                (operation_id, tenant_id, kind, status, platform_operator, authorization_reference,
                 target_release, target_max_concurrent_jobs, target_max_storage_mb,
                 target_max_report_sessions, started_at_utc, updated_at_utc)
            VALUES (@operation, @tenant, @kind, 'Started', @operator, @authorization,
                    @release, @jobs, @storage, @reports, @now, @now);";
        insert.AddParam("@operation", command.OperationId);
        insert.AddParam("@kind", command.Kind.ToString());
        insert.AddParam("@operator", command.PlatformOperator);
        insert.AddParam("@authorization", command.AuthorizationReference);
        AddCommand(insert, command, tenantId);
        await insert.ExecuteNonQueryAsync(ct);
    }

    private static async Task FenceJobsAsync(
        DbConnection connection, DbTransaction transaction, string tenantId,
        SharedTenantLifecycleCommand command, string state, CancellationToken ct)
    {
        await using (var fenceState = connection.CreateCommand())
        {
            fenceState.Transaction = transaction;
            fenceState.CommandText = @"
                UPDATE shared_tenant_control_planes
                   SET state = @state,
                       fence_epoch = CASE WHEN state = 'Active' THEN fence_epoch + 1 ELSE fence_epoch END,
                       updated_at_utc = @now, version = version + 1
                 WHERE tenant_id = @tenant AND state IN ('Active', @state);";
            fenceState.AddParam("@state", state);
            fenceState.AddParam("@tenant", tenantId);
            fenceState.AddParam("@now", command.NowUtc.ToString("O"));
            if (await fenceState.ExecuteNonQueryAsync(ct) != 1)
                throw new InvalidOperationException("Tenant is already fenced by another lifecycle operation.");
        }
        await using (var remember = connection.CreateCommand())
        {
            remember.Transaction = transaction;
            remember.CommandText = @"
                INSERT INTO shared_tenant_lifecycle_fenced_jobs (operation_id, tenant_id, job_name)
                SELECT @operation, @tenant, name FROM jobs
                 WHERE tenant_id = @tenant AND is_enabled = 1
                ON CONFLICT (operation_id, job_name) DO NOTHING;";
            remember.AddParam("@operation", command.OperationId);
            remember.AddParam("@tenant", tenantId);
            await remember.ExecuteNonQueryAsync(ct);
        }
        await using var disable = connection.CreateCommand();
        disable.Transaction = transaction;
        disable.CommandText = @"
            UPDATE jobs SET is_enabled = 0, version = version + 1, modified_by = @operator
             WHERE tenant_id = @tenant AND is_enabled = 1;";
        disable.AddParam("@operator", command.PlatformOperator);
        disable.AddParam("@tenant", tenantId);
        await disable.ExecuteNonQueryAsync(ct);
    }

    private static async Task<int> CountActiveJobsAsync(
        DbConnection connection, DbTransaction transaction, string tenantId,
        DateTimeOffset now, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            SELECT COUNT(*) FROM jobs
             WHERE tenant_id = @tenant AND lease_owner IS NOT NULL
               AND lease_expires_at IS NOT NULL AND lease_expires_at > @now;";
        command.AddParam("@tenant", tenantId);
        command.AddParam("@now", now.UtcDateTime.ToString("O"));
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    private static async Task RestoreFencedJobsAsync(
        DbConnection connection, DbTransaction transaction, string tenantId,
        string operationId, CancellationToken ct)
    {
        await using var restore = connection.CreateCommand();
        restore.Transaction = transaction;
        restore.CommandText = @"
            UPDATE jobs SET is_enabled = 1, version = version + 1
             WHERE tenant_id = @tenant AND name IN
                   (SELECT job_name FROM shared_tenant_lifecycle_fenced_jobs
                     WHERE operation_id = @operation AND tenant_id = @tenant);";
        restore.AddParam("@tenant", tenantId);
        restore.AddParam("@operation", operationId);
        await restore.ExecuteNonQueryAsync(ct);
    }

    private static async Task DeleteTenantJobsAsync(
        DbConnection connection, DbTransaction transaction, string tenantId, CancellationToken ct)
    {
        // Every predicate here is a tenant column or a surrogate id, never a job *name*. Names repeat
        // across tenants, so the name-resolved form these statements used to take would delete another
        // tenant's history, state, and links the moment two tenants shared a job name — a silent,
        // unrecoverable loss in the one operation that must not overreach.
        foreach (var sql in new[]
        {
            "DELETE FROM job_column_metrics WHERE tenant_id = @tenant;",
            "DELETE FROM job_data_quality_failures WHERE tenant_id = @tenant;",
            "DELETE FROM job_statement_metrics WHERE tenant_id = @tenant;",
            // History and its rollup carry the tenant directly: they outlive the job, so a join
            // through Jobs would strand the rows of an already-dropped job in the tenant's partition.
            "DELETE FROM job_history_daily WHERE tenant_id = @tenant;",
            "DELETE FROM job_history WHERE tenant_id = @tenant;",
            "DELETE FROM orchestrator_object_acls WHERE object_id IN (SELECT id FROM jobs WHERE tenant_id = @tenant);",
            "DELETE FROM orchestrator_object_acls WHERE object_id IN (SELECT id FROM schedules WHERE tenant_id = @tenant);",
            "DELETE FROM orchestrator_object_acls WHERE object_id IN (SELECT id FROM notifications WHERE tenant_id = @tenant);",
            "DELETE FROM job_state WHERE job_id IN (SELECT id FROM jobs WHERE tenant_id = @tenant);",
            "DELETE FROM job_schedules WHERE job_id IN (SELECT id FROM jobs WHERE tenant_id = @tenant);",
            "DELETE FROM job_notifications WHERE job_id IN (SELECT id FROM jobs WHERE tenant_id = @tenant);",
            "DELETE FROM tenant_usage_records WHERE tenant_id = @tenant;",
            "DELETE FROM schedules WHERE tenant_id = @tenant;",
            "DELETE FROM notifications WHERE tenant_id = @tenant;",
            "DELETE FROM jobs WHERE tenant_id = @tenant;"
        })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.AddParam("@tenant", tenantId);
            await command.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task CompleteOperationAsync(
        DbConnection connection, DbTransaction transaction, string operationId,
        DateTimeOffset now, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            UPDATE shared_tenant_lifecycle_operations
               SET status = 'Completed', updated_at_utc = @now, completed_at_utc = @now
             WHERE operation_id = @operation;";
        command.AddParam("@operation", operationId);
        command.AddParam("@now", now.ToString("O"));
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<SharedTenantControlPlaneState?> ReadStateAsync(
        DbConnection connection, DbTransaction? transaction, string tenantId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            SELECT tenant_id, state, active_release, max_concurrent_jobs, max_storage_mb,
                   max_report_sessions, fence_epoch, updated_at_utc, deleted_at_utc, version
              FROM shared_tenant_control_planes WHERE tenant_id = @tenant;";
        command.AddParam("@tenant", tenantId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
            reader.GetInt32(4), reader.GetInt32(5), reader.GetInt64(6),
            ParseDateTimeOffset(reader.GetValue(7)),
            reader.IsDBNull(8) ? null : ParseDateTimeOffset(reader.GetValue(8)), reader.GetInt64(9));
    }

    private static DateTimeOffset ParseDateTimeOffset(object value) => value switch
    {
        DateTimeOffset dto => dto,
        DateTime dt => dt.Kind == DateTimeKind.Utc ? new DateTimeOffset(dt) : new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
        string text => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        _ => DateTimeOffset.UtcNow
    };

    private static void AddCommand(
        DbCommand command, SharedTenantLifecycleCommand value, string tenantId)
    {
        command.AddParam("@tenant", tenantId);
        command.AddParam("@release", value.TargetRelease);
        command.AddParam("@jobs", value.MaxConcurrentJobs);
        command.AddParam("@storage", value.MaxStorageMb);
        command.AddParam("@reports", value.MaxReportSessions);
        command.AddParam("@now", value.NowUtc.ToString("O"));
    }
}
