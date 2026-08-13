using System.Data.Common;
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
                INSERT INTO SharedTenantControlPlanes
                    (TenantId, State, ActiveRelease, MaxConcurrentJobs, MaxStorageMb,
                     MaxReportSessions, FenceEpoch, CreatedAtUtc, UpdatedAtUtc, Version)
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
                UPDATE SharedTenantControlPlanes
                   SET State = 'Active', ActiveRelease = @release,
                       MaxConcurrentJobs = @jobs, MaxStorageMb = @storage,
                       MaxReportSessions = @reports, UpdatedAtUtc = @now,
                       Version = Version + 1
                 WHERE TenantId = @tenant AND State = 'Upgrading';";
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
                UPDATE SharedTenantControlPlanes
                   SET State = 'Deleted', DeletedAtUtc = @now, UpdatedAtUtc = @now,
                       FenceEpoch = FenceEpoch + 1, Version = Version + 1
                 WHERE TenantId = @tenant AND State = 'Deleting';";
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
            SELECT TenantId, Kind, PlatformOperator, AuthorizationReference, TargetRelease,
                   TargetMaxConcurrentJobs, TargetMaxStorageMb, TargetMaxReportSessions, Status
              FROM SharedTenantLifecycleOperations WHERE OperationId = @operation;";
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
            INSERT INTO SharedTenantLifecycleOperations
                (OperationId, TenantId, Kind, Status, PlatformOperator, AuthorizationReference,
                 TargetRelease, TargetMaxConcurrentJobs, TargetMaxStorageMb,
                 TargetMaxReportSessions, StartedAtUtc, UpdatedAtUtc)
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
                UPDATE SharedTenantControlPlanes
                   SET State = @state,
                       FenceEpoch = CASE WHEN State = 'Active' THEN FenceEpoch + 1 ELSE FenceEpoch END,
                       UpdatedAtUtc = @now, Version = Version + 1
                 WHERE TenantId = @tenant AND State IN ('Active', @state);";
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
                INSERT INTO SharedTenantLifecycleFencedJobs (OperationId, TenantId, JobName)
                SELECT @operation, @tenant, Name FROM Jobs
                 WHERE TenantId = @tenant AND IsEnabled = 1
                ON CONFLICT (OperationId, JobName) DO NOTHING;";
            remember.AddParam("@operation", command.OperationId);
            remember.AddParam("@tenant", tenantId);
            await remember.ExecuteNonQueryAsync(ct);
        }
        await using var disable = connection.CreateCommand();
        disable.Transaction = transaction;
        disable.CommandText = @"
            UPDATE Jobs SET IsEnabled = 0, Version = Version + 1, ModifiedBy = @operator
             WHERE TenantId = @tenant AND IsEnabled = 1;";
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
            SELECT COUNT(*) FROM Jobs
             WHERE TenantId = @tenant AND LeaseOwner IS NOT NULL
               AND LeaseExpiresAt IS NOT NULL AND LeaseExpiresAt > @now;";
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
            UPDATE Jobs SET IsEnabled = 1, Version = Version + 1
             WHERE TenantId = @tenant AND Name IN
                   (SELECT JobName FROM SharedTenantLifecycleFencedJobs
                     WHERE OperationId = @operation AND TenantId = @tenant);";
        restore.AddParam("@tenant", tenantId);
        restore.AddParam("@operation", operationId);
        await restore.ExecuteNonQueryAsync(ct);
    }

    private static async Task DeleteTenantJobsAsync(
        DbConnection connection, DbTransaction transaction, string tenantId, CancellationToken ct)
    {
        foreach (var sql in new[]
        {
            "DELETE FROM JobColumnMetrics WHERE JobHistoryId IN (SELECT h.Id FROM JobHistory h JOIN Jobs j ON j.Name = h.JobName WHERE j.TenantId = @tenant);",
            "DELETE FROM JobDataQualityFailures WHERE JobHistoryId IN (SELECT h.Id FROM JobHistory h JOIN Jobs j ON j.Name = h.JobName WHERE j.TenantId = @tenant);",
            "DELETE FROM JobStatementMetrics WHERE JobHistoryId IN (SELECT h.Id FROM JobHistory h JOIN Jobs j ON j.Name = h.JobName WHERE j.TenantId = @tenant);",
            "DELETE FROM JobHistory WHERE JobName IN (SELECT Name FROM Jobs WHERE TenantId = @tenant);",
            "DELETE FROM JobSchedules WHERE JobName IN (SELECT Name FROM Jobs WHERE TenantId = @tenant);",
            "DELETE FROM JobNotifications WHERE JobName IN (SELECT Name FROM Jobs WHERE TenantId = @tenant);",
            "DELETE FROM TenantUsageRecords WHERE TenantId = @tenant;",
            "DELETE FROM Jobs WHERE TenantId = @tenant;"
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
            UPDATE SharedTenantLifecycleOperations
               SET Status = 'Completed', UpdatedAtUtc = @now, CompletedAtUtc = @now
             WHERE OperationId = @operation;";
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
            SELECT TenantId, State, ActiveRelease, MaxConcurrentJobs, MaxStorageMb,
                   MaxReportSessions, FenceEpoch, UpdatedAtUtc, DeletedAtUtc, Version
              FROM SharedTenantControlPlanes WHERE TenantId = @tenant;";
        command.AddParam("@tenant", tenantId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
            reader.GetInt32(4), reader.GetInt32(5), reader.GetInt64(6),
            DateTimeOffset.Parse(reader.GetString(7)),
            reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8)), reader.GetInt64(9));
    }

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
