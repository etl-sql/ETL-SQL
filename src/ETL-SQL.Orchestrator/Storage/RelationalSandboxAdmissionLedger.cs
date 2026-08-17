using System.Data.Common;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Execution;

namespace ETL_SQL.Orchestrator.Storage;

public enum SandboxAdmissionState
{
    Queued,
    Active,
    Retained,
    Completed,
    Cancelled
}

public sealed record SandboxAdmissionLedgerEntry(
    long Sequence,
    string AdmissionId,
    string TenantId,
    string PoolId,
    int TenantWeight,
    int MaxConcurrentAttempts,
    int MaxQueuedAttempts,
    SandboxAdmissionState State,
    string? LeaseOwner,
    DateTimeOffset? LeaseExpiresUtc,
    long FenceToken,
    DateTimeOffset EnqueuedUtc,
    DateTimeOffset UpdatedUtc,
    string? ReconciliationReason,
    string? ClaimedByNode = null,
    DateTimeOffset? ClaimHeartbeatUtc = null);

/// <summary>
/// One cluster-global weighted-fair selection decision for a pool: the durable admission that must be
/// activated next, and — when the caller was refused — the admission that outranked it.
/// </summary>
public sealed record SandboxAdmissionSelection(
    string? EligibleAdmissionId,
    string? EligibleTenantId,
    IReadOnlyList<string> ContendingTenantIds);

public interface ISandboxAdmissionLedger
{
    Task<bool> EnqueueAsync(
        string admissionId,
        TenantContext tenant,
        ResolvedSandboxAdmissionPolicy policy,
        CancellationToken cancellationToken = default);

    Task<long?> TryActivateAsync(
        string admissionId,
        string leaseOwner,
        int poolCapacity,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports the cluster-global weighted-fair selection for a pool without changing any state. It is
    /// diagnostic only: activation still re-evaluates the decision inside its own transaction, so a
    /// caller cannot turn this answer into an authorization to run.
    /// </summary>
    Task<SandboxAdmissionSelection> PeekEligibleAsync(
        string poolId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new SandboxAdmissionSelection(null, null, []));

    Task<bool> TryRenewAsync(
        string admissionId,
        string leaseOwner,
        long fenceToken,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task<bool> TryCompleteAsync(
        string admissionId,
        string leaseOwner,
        long fenceToken,
        CancellationToken cancellationToken = default);

    Task<bool> TryRetainAsync(
        string admissionId,
        string leaseOwner,
        long fenceToken,
        string reason,
        CancellationToken cancellationToken = default);

    Task<int> RetainExpiredAsync(
        DateTimeOffset now,
        string reason,
        CancellationToken cancellationToken = default);

    Task<bool> ReleaseRetainedAsync(
        string admissionId,
        long fenceToken,
        CancellationToken cancellationToken = default);

    Task<bool> TryCancelQueuedAsync(
        string admissionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels queued admissions that no node is waiting on any more — the node crashed, drained, or
    /// lost the work before it ever ran. Without this, a shared queue accumulates phantom entries that
    /// consume a tenant's durable queue depth and misreport how much work the fleet still owes.
    /// </summary>
    Task<int> CancelAbandonedQueuedAsync(
        DateTimeOffset now,
        TimeSpan abandonedAfter,
        string reason,
        CancellationToken cancellationToken = default) => Task.FromResult(0);

    Task<SandboxAdmissionLedgerEntry?> ReadAsync(
        string admissionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SandboxAdmissionLedgerEntry>> ListOpenAsync(
        string poolId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SandboxAdmissionLedgerEntry>> ListTenantOpenAsync(
        TenantContext tenant,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SandboxAdmissionLedgerEntry>>([]);

    Task<int> CancelTenantQueuedAsync(
        TenantContext tenant,
        CancellationToken cancellationToken = default) => Task.FromResult(0);

    Task<int> PurgeTenantTerminalAsync(
        TenantContext tenant,
        CancellationToken cancellationToken = default) => Task.FromResult(0);
}

/// <summary>
/// Durable SQLite/PostgreSQL admission queue and fenced reservation ledger. It makes ownership,
/// restart recovery, and ambiguous teardown state authoritative across orchestrator nodes, and it owns
/// the cluster-global weighted-fair selection: a node's process-local ordering can only propose a
/// candidate, never decide which tenant consumes the next slot of a shared pool.
/// </summary>
/// <param name="claimFreshness">
/// How long a queued admission's dispatch claim keeps competing for capacity after the claiming node
/// last polled for it. A node that stops polling — because it crashed, drained, or lost the work —
/// stops blocking the rest of the cluster once its claim goes stale, so fairness cannot cost liveness.
/// </param>
public sealed class RelationalSandboxAdmissionLedger(
    IOrchestratorStoreDialect dialect,
    TimeSpan? claimFreshness = null)
    : ISandboxAdmissionLedger
{
    /// <summary>Default staleness horizon for a queued admission's dispatch claim.</summary>
    public static readonly TimeSpan DefaultClaimFreshness = TimeSpan.FromSeconds(15);

    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private readonly TimeSpan _claimFreshness = Validate(claimFreshness ?? DefaultClaimFreshness);
    private bool _initialized;

    private static TimeSpan Validate(TimeSpan claimFreshness) =>
        claimFreshness > TimeSpan.Zero
            ? claimFreshness
            : throw new ArgumentOutOfRangeException(
                nameof(claimFreshness), "The dispatch-claim freshness horizon must be positive.");

    public async Task<bool> EnqueueAsync(
        string admissionId,
        TenantContext tenant,
        ResolvedSandboxAdmissionPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(admissionId);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        await EnsureInitializedAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow.ToString("O");
        await using var connection = dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SandboxAdmissions
                (AdmissionId, TenantId, PoolId, TenantWeight, MaxConcurrentAttempts,
                 MaxQueuedAttempts, State, FenceToken, EnqueuedUtc, UpdatedUtc)
            VALUES
                (@id, @tenant, @pool, @weight, @maxActive, @maxQueued,
                 'Queued', 0, @now, @now)
            ON CONFLICT (AdmissionId) DO NOTHING;
            """;
        command.AddParam("@id", admissionId);
        command.AddParam("@tenant", tenant.Tenant.Value);
        command.AddParam("@pool", policy.PoolId);
        command.AddParam("@weight", policy.TenantWeight);
        command.AddParam("@maxActive", policy.MaxConcurrentAttempts);
        command.AddParam("@maxQueued", policy.MaxQueuedAttempts);
        command.AddParam("@now", now);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<long?> TryActivateAsync(
        string admissionId,
        string leaseOwner,
        int poolCapacity,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ValidateLeaseArguments(admissionId, leaseOwner, leaseDuration);
        if (poolCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(poolCapacity));
        await EnsureInitializedAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        // The claim is committed on its own so a losing attempt still advertises this node as a live
        // contender. Inside the activation transaction it would be rolled back with the refusal, and
        // every node would keep believing it was the only one waiting.
        await RecordDispatchClaimAsync(admissionId, leaseOwner, now, cancellationToken);

        await using var connection = dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var queued = await ReadEntryAsync(connection, transaction, admissionId, "Queued", cancellationToken);
        if (queued is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        // Every grant in a pool serializes on the pool row before any virtual time is read, so two
        // nodes cannot both select against the same stale fair-share snapshot.
        await LockPoolAsync(connection, transaction, queued.PoolId, cancellationToken);
        var winner = await SelectEligibleAsync(
            connection, transaction, queued.PoolId, admissionId, now, cancellationToken);
        if (winner?.AdmissionId != admissionId)
        {
            // Another tenant is further behind its weighted share of this pool. Refusing here is what
            // makes fairness cluster-global: without it, per-node ordering lets whichever node polls
            // first take every freed slot.
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (!await TryReserveCapacityAsync(
                connection, transaction, queued.PoolId, queued.TenantId,
                poolCapacity, queued.MaxConcurrentAttempts, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await ChargeFairShareAsync(connection, transaction, winner, cancellationToken);

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE SandboxAdmissions
               SET State = 'Active', LeaseOwner = @owner, LeaseExpiresUtc = @expires,
                   FenceToken = FenceToken + 1, UpdatedUtc = @now, ReconciliationReason = NULL
             WHERE AdmissionId = @id AND State = 'Queued';
            """;
        update.AddParam("@id", admissionId);
        update.AddParam("@owner", leaseOwner);
        update.AddParam("@expires", now.Add(leaseDuration).ToString("O"));
        update.AddParam("@now", now.ToString("O"));
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT FenceToken FROM SandboxAdmissions WHERE AdmissionId = @id;";
        read.AddParam("@id", admissionId);
        var token = Convert.ToInt64(await read.ExecuteScalarAsync(cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return token;
    }

    public async Task<bool> TryRenewAsync(
        string admissionId,
        string leaseOwner,
        long fenceToken,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ValidateLeaseArguments(admissionId, leaseOwner, leaseDuration);
        if (fenceToken <= 0) throw new ArgumentOutOfRangeException(nameof(fenceToken));
        await EnsureInitializedAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        return await ExecuteOwnedUpdateAsync(
            """
            UPDATE SandboxAdmissions
               SET LeaseExpiresUtc = @expires, UpdatedUtc = @now
             WHERE AdmissionId = @id AND State = 'Active'
               AND LeaseOwner = @owner AND FenceToken = @fence;
            """,
            admissionId, leaseOwner, fenceToken, now, now.Add(leaseDuration), null, cancellationToken);
    }

    public async Task<bool> TryCompleteAsync(
        string admissionId,
        string leaseOwner,
        long fenceToken,
        CancellationToken cancellationToken = default)
    {
        ValidateOwnedMutation(admissionId, leaseOwner, fenceToken);
        await EnsureInitializedAsync(cancellationToken);
        return await TryTransitionAndReleaseCapacityAsync(
            admissionId, "Active", "Completed", leaseOwner, fenceToken,
            clearReason: true, cancellationToken);
    }

    public async Task<bool> TryRetainAsync(
        string admissionId,
        string leaseOwner,
        long fenceToken,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ValidateOwnedMutation(admissionId, leaseOwner, fenceToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        await EnsureInitializedAsync(cancellationToken);
        return await ExecuteOwnedUpdateAsync(
            """
            UPDATE SandboxAdmissions
               SET State = 'Retained', LeaseOwner = NULL, LeaseExpiresUtc = NULL,
                   UpdatedUtc = @now, ReconciliationReason = @reason
             WHERE AdmissionId = @id AND State = 'Active'
               AND LeaseOwner = @owner AND FenceToken = @fence;
            """,
            admissionId, leaseOwner, fenceToken, DateTimeOffset.UtcNow, null, reason, cancellationToken);
    }

    public async Task<int> RetainExpiredAsync(
        DateTimeOffset now,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SandboxAdmissions
               SET State = 'Retained', LeaseOwner = NULL, LeaseExpiresUtc = NULL,
                   UpdatedUtc = @now, ReconciliationReason = @reason
             WHERE State = 'Active' AND LeaseExpiresUtc <= @now;
            """;
        command.AddParam("@now", now.ToString("O"));
        command.AddParam("@reason", reason);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> ReleaseRetainedAsync(
        string admissionId,
        long fenceToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(admissionId);
        if (fenceToken <= 0) throw new ArgumentOutOfRangeException(nameof(fenceToken));
        await EnsureInitializedAsync(cancellationToken);
        return await TryTransitionAndReleaseCapacityAsync(
            admissionId, "Retained", "Completed", null, fenceToken,
            clearReason: true, cancellationToken);
    }

    public async Task<SandboxAdmissionLedgerEntry?> ReadAsync(
        string admissionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(admissionId);
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM SandboxAdmissions WHERE AdmissionId = @id;";
        command.AddParam("@id", admissionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadEntry(reader) : null;
    }

    public async Task<bool> TryCancelQueuedAsync(
        string admissionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(admissionId);
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SandboxAdmissions
               SET State = 'Cancelled', UpdatedUtc = @now
             WHERE AdmissionId = @id AND State = 'Queued';
            """;
        command.AddParam("@id", admissionId);
        command.AddParam("@now", DateTimeOffset.UtcNow.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<int> CancelAbandonedQueuedAsync(
        DateTimeOffset now,
        TimeSpan abandonedAfter,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (abandonedAfter <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(abandonedAfter));
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // A never-claimed row is measured from when it was enqueued, so an admission whose node died
        // between enqueue and its first poll is reclaimed on the same horizon as one abandoned later.
        command.CommandText = """
            UPDATE SandboxAdmissions
               SET State = 'Cancelled', UpdatedUtc = @now, ReconciliationReason = @reason
             WHERE State = 'Queued'
               AND COALESCE(ClaimHeartbeatUtc, EnqueuedUtc) <= @abandoned;
            """;
        command.AddParam("@now", now.ToString("O"));
        command.AddParam("@reason", reason);
        command.AddParam("@abandoned", now.Subtract(abandonedAfter).ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SandboxAdmissionLedgerEntry>> ListOpenAsync(
        string poolId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(poolId);
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT * FROM SandboxAdmissions
             WHERE PoolId = @pool AND State IN ('Queued', 'Active', 'Retained')
             ORDER BY Sequence;
            """;
        command.AddParam("@pool", poolId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<SandboxAdmissionLedgerEntry>();
        while (await reader.ReadAsync(cancellationToken))
            entries.Add(ReadEntry(reader));
        return entries;
    }

    public async Task<IReadOnlyList<SandboxAdmissionLedgerEntry>> ListTenantOpenAsync(
        TenantContext tenant,
        CancellationToken cancellationToken = default)
    {
        var tenantId = RequireSharedTenant(tenant);
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Sequence, AdmissionId, TenantId, PoolId, TenantWeight,
                   MaxConcurrentAttempts, MaxQueuedAttempts, State, LeaseOwner,
                   LeaseExpiresUtc, FenceToken, EnqueuedUtc, UpdatedUtc, ReconciliationReason,
                   ClaimedByNode, ClaimHeartbeatUtc
              FROM SandboxAdmissions
             WHERE TenantId = @tenant AND State IN ('Queued', 'Active', 'Retained')
             ORDER BY Sequence;
            """;
        command.AddParam("@tenant", tenantId);
        var values = new List<SandboxAdmissionLedgerEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) values.Add(ReadEntry(reader));
        return values;
    }

    public async Task<int> CancelTenantQueuedAsync(
        TenantContext tenant,
        CancellationToken cancellationToken = default)
    {
        var tenantId = RequireSharedTenant(tenant);
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SandboxAdmissions
               SET State = 'Cancelled', UpdatedUtc = @now,
                   ReconciliationReason = 'TenantLifecycleFence'
             WHERE TenantId = @tenant AND State = 'Queued';
            """;
        command.AddParam("@tenant", tenantId);
        command.AddParam("@now", DateTimeOffset.UtcNow.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> PurgeTenantTerminalAsync(
        TenantContext tenant,
        CancellationToken cancellationToken = default)
    {
        var tenantId = RequireSharedTenant(tenant);
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var deleteAdmissions = connection.CreateCommand();
        deleteAdmissions.Transaction = transaction;
        deleteAdmissions.CommandText = """
            DELETE FROM SandboxAdmissions
             WHERE TenantId = @tenant AND State IN ('Completed', 'Cancelled');
            """;
        deleteAdmissions.AddParam("@tenant", tenantId);
        var deleted = await deleteAdmissions.ExecuteNonQueryAsync(cancellationToken);
        await using var deleteCapacity = connection.CreateCommand();
        deleteCapacity.Transaction = transaction;
        deleteCapacity.CommandText = """
            DELETE FROM SandboxAdmissionTenantCapacity
             WHERE TenantId = @tenant AND ActiveCount = 0;
            """;
        deleteCapacity.AddParam("@tenant", tenantId);
        await deleteCapacity.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }

    private static string RequireSharedTenant(TenantContext tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        if (tenant.Origin != TenantContextOrigin.VerifiedCredential)
            throw new UnauthorizedAccessException(
                "Shared admission lifecycle requires a verified tenant assertion.");
        return tenant.Tenant.Value;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        await _initializeGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            await using var connection = dialect.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(dialect.CollationDdl))
            {
                await using var collation = connection.CreateCommand();
                collation.CommandText = dialect.CollationDdl;
                await collation.ExecuteNonQueryAsync(cancellationToken);
            }

            var lockHeld = false;
            try
            {
                if (!string.IsNullOrWhiteSpace(dialect.SchemaInitializationLockSql))
                {
                    await using var schemaLock = connection.CreateCommand();
                    schemaLock.CommandText = dialect.SchemaInitializationLockSql;
                    await schemaLock.ExecuteNonQueryAsync(cancellationToken);
                    lockHeld = true;
                }

                await using var create = connection.CreateCommand();
                create.CommandText = $"""
                    CREATE TABLE IF NOT EXISTS SandboxAdmissions (
                        Sequence                 {dialect.AutoIncrementPrimaryKey},
                        AdmissionId              TEXT NOT NULL UNIQUE,
                        TenantId                 TEXT NOT NULL,
                        PoolId                   TEXT NOT NULL,
                        TenantWeight             INTEGER NOT NULL,
                        MaxConcurrentAttempts    INTEGER NOT NULL,
                        MaxQueuedAttempts        INTEGER NOT NULL,
                        State                    TEXT NOT NULL,
                        LeaseOwner               TEXT NULL,
                        LeaseExpiresUtc          TEXT NULL,
                        FenceToken               {dialect.Int64Type} NOT NULL DEFAULT 0,
                        EnqueuedUtc               TEXT NOT NULL,
                        UpdatedUtc                TEXT NOT NULL,
                        ReconciliationReason     TEXT NULL,
                        ClaimedByNode            TEXT NULL,
                        ClaimHeartbeatUtc        TEXT NULL
                    );
                    CREATE INDEX IF NOT EXISTS IX_SandboxAdmissions_PoolStateSequence
                        ON SandboxAdmissions (PoolId, State, Sequence);
                    CREATE INDEX IF NOT EXISTS IX_SandboxAdmissions_TenantState
                        ON SandboxAdmissions (TenantId, State);
                    CREATE TABLE IF NOT EXISTS SandboxAdmissionPools (
                        PoolId       TEXT PRIMARY KEY,
                        ActiveCount  INTEGER NOT NULL DEFAULT 0,
                        VirtualBase  {dialect.Int64Type} NOT NULL DEFAULT 0
                    );
                    CREATE TABLE IF NOT EXISTS SandboxAdmissionTenantCapacity (
                        PoolId       TEXT NOT NULL,
                        TenantId     TEXT NOT NULL,
                        ActiveCount  INTEGER NOT NULL DEFAULT 0,
                        VirtualTime  {dialect.Int64Type} NOT NULL DEFAULT 0,
                        PRIMARY KEY (PoolId, TenantId)
                    );
                    """;
                await create.ExecuteNonQueryAsync(cancellationToken);
                await AddFairSelectionColumnsAsync(connection, cancellationToken);
                _initialized = true;
            }
            finally
            {
                if (lockHeld)
                {
                    await using var unlock = connection.CreateCommand();
                    unlock.CommandText = dialect.SchemaInitializationUnlockSql;
                    await unlock.ExecuteNonQueryAsync(CancellationToken.None);
                }
            }
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    /// <summary>
    /// Additive migration for a ledger created before cluster-global fair selection existed. An older
    /// database simply has no claims and no accumulated virtual time, so every queued row starts
    /// unclaimed and every tenant starts level.
    /// </summary>
    private async Task AddFairSelectionColumnsAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await AddColumnIfMissingAsync("SandboxAdmissions", "ClaimedByNode", "TEXT NULL");
        await AddColumnIfMissingAsync("SandboxAdmissions", "ClaimHeartbeatUtc", "TEXT NULL");
        await AddColumnIfMissingAsync("SandboxAdmissionPools", "VirtualBase", $"{dialect.Int64Type} NOT NULL DEFAULT 0");
        await AddColumnIfMissingAsync(
            "SandboxAdmissionTenantCapacity", "VirtualTime", $"{dialect.Int64Type} NOT NULL DEFAULT 0");
        return;

        async Task AddColumnIfMissingAsync(string table, string column, string definition)
        {
            var columns = await dialect.GetColumnNamesAsync(connection, table, cancellationToken);
            if (columns.Contains(column))
                return;
            await using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
            try
            {
                await alter.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (DbException)
            {
                // Another node won the same additive migration between the snapshot and this ALTER.
            }
        }
    }

    private async Task<bool> ExecuteOwnedUpdateAsync(
        string sql,
        string admissionId,
        string leaseOwner,
        long fenceToken,
        DateTimeOffset now,
        DateTimeOffset? expires,
        string? reason,
        CancellationToken cancellationToken)
    {
        await using var connection = dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.AddParam("@id", admissionId);
        command.AddParam("@owner", leaseOwner);
        command.AddParam("@fence", fenceToken);
        command.AddParam("@now", now.ToString("O"));
        if (expires.HasValue) command.AddParam("@expires", expires.Value.ToString("O"));
        if (reason is not null) command.AddParam("@reason", reason);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task<SandboxAdmissionLedgerEntry?> ReadEntryAsync(
        DbConnection connection,
        DbTransaction transaction,
        string admissionId,
        string expectedState,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT * FROM SandboxAdmissions WHERE AdmissionId = @id AND State = @state;";
        command.AddParam("@id", admissionId);
        command.AddParam("@state", expectedState);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadEntry(reader) : null;
    }

    /// <summary>
    /// Marks this node as actively waiting on a queued admission. Only rows with a fresh claim compete
    /// in the weighted-fair selection, so an abandoned queue entry cannot hold a pool hostage.
    /// </summary>
    private async Task RecordDispatchClaimAsync(
        string admissionId,
        string leaseOwner,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SandboxAdmissions
               SET ClaimedByNode = @owner, ClaimHeartbeatUtc = @now
             WHERE AdmissionId = @id AND State = 'Queued';
            """;
        command.AddParam("@id", admissionId);
        command.AddParam("@owner", leaseOwner);
        command.AddParam("@now", now.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SandboxAdmissionSelection> PeekEligibleAsync(
        string poolId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(poolId);
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var (winner, contenders) = await SelectWithContendersAsync(
            connection, transaction, poolId, requestedAdmissionId: null, DateTimeOffset.UtcNow, cancellationToken);
        await transaction.RollbackAsync(cancellationToken);
        return new SandboxAdmissionSelection(winner?.AdmissionId, winner?.TenantId, contenders);
    }

    private async Task<Candidate?> SelectEligibleAsync(
        DbConnection connection,
        DbTransaction transaction,
        string poolId,
        string? requestedAdmissionId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        (await SelectWithContendersAsync(
            connection, transaction, poolId, requestedAdmissionId, now, cancellationToken)).Winner;

    /// <summary>
    /// Weighted fair queuing over the pool's live-claimed queue. Each tenant carries a durable virtual
    /// time that advances by <c>Scale / weight</c> every time it is granted a slot, so a heavier weight
    /// buys proportionally more grants rather than unconditional priority — the next slot always goes
    /// to whichever backlogged tenant is furthest behind its share. A tenant that was idle is lifted to
    /// the pool's virtual base first, so it can neither hoard credit while away nor stay permanently
    /// behind after a busy neighbour ran. Equal virtual time falls back to durable enqueue order, and a
    /// tenant already at its concurrency maximum is not a candidate at all.
    /// </summary>
    private async Task<(Candidate? Winner, IReadOnlyList<string> Contenders)> SelectWithContendersAsync(
        DbConnection connection,
        DbTransaction transaction,
        string poolId,
        string? requestedAdmissionId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var poolBase = await ReadPoolVirtualBaseAsync(connection, transaction, poolId, cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT a.AdmissionId, a.TenantId, a.Sequence, a.TenantWeight, a.MaxConcurrentAttempts,
                   COALESCE(c.ActiveCount, 0), COALESCE(c.VirtualTime, 0)
              FROM SandboxAdmissions a
              LEFT JOIN SandboxAdmissionTenantCapacity c
                     ON c.PoolId = a.PoolId AND c.TenantId = a.TenantId
             WHERE a.PoolId = @pool AND a.State = 'Queued'
               AND (a.ClaimHeartbeatUtc >= @fresh OR a.AdmissionId = @requested)
             ORDER BY a.Sequence;
            """;
        command.AddParam("@pool", poolId);
        command.AddParam("@fresh", now.Subtract(_claimFreshness).ToString("O"));
        command.AddParam("@requested", (object?)requestedAdmissionId ?? string.Empty);

        var heads = new List<Candidate>();
        var seenTenants = new HashSet<string>(StringComparer.Ordinal);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var tenantId = reader.GetString(1);
                // Rows arrive in durable sequence order, so the first row of a tenant is its head; a
                // tenant never competes with itself for the same slot.
                if (!seenTenants.Add(tenantId))
                    continue;
                var activeCount = Convert.ToInt32(reader.GetValue(5));
                var maxConcurrent = reader.GetInt32(4);
                if (activeCount >= maxConcurrent)
                    continue;
                heads.Add(new Candidate(
                    reader.GetString(0),
                    tenantId,
                    poolId,
                    Convert.ToInt64(reader.GetValue(2)),
                    reader.GetInt32(3),
                    Math.Max(Convert.ToInt64(reader.GetValue(6)), poolBase)));
            }
        }

        var winner = heads.Count == 0
            ? null
            : heads.Aggregate(static (best, next) => Outranks(next, best) ? next : best);
        return (winner, heads.Select(candidate => candidate.TenantId).ToArray());
    }

    private static bool Outranks(Candidate candidate, Candidate incumbent) =>
        candidate.EffectiveVirtualTime != incumbent.EffectiveVirtualTime
            ? candidate.EffectiveVirtualTime < incumbent.EffectiveVirtualTime
            : candidate.Sequence < incumbent.Sequence;

    /// <summary>
    /// Charges the granted tenant its weighted cost and advances the pool's virtual base to the grant
    /// that just happened. The base is monotonic, so a tenant cannot rewind the pool by going idle.
    /// </summary>
    private static async Task ChargeFairShareAsync(
        DbConnection connection,
        DbTransaction transaction,
        Candidate winner,
        CancellationToken cancellationToken)
    {
        await using (var chargeTenant = connection.CreateCommand())
        {
            chargeTenant.Transaction = transaction;
            chargeTenant.CommandText = """
                UPDATE SandboxAdmissionTenantCapacity
                   SET VirtualTime = @charged
                 WHERE PoolId = @pool AND TenantId = @tenant;
                """;
            chargeTenant.AddParam("@charged", winner.EffectiveVirtualTime + VirtualTimeScale / winner.TenantWeight);
            chargeTenant.AddParam("@pool", winner.PoolId);
            chargeTenant.AddParam("@tenant", winner.TenantId);
            if (await chargeTenant.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("Sandbox tenant fair-share ledger is inconsistent.");
        }

        await using var advanceBase = connection.CreateCommand();
        advanceBase.Transaction = transaction;
        advanceBase.CommandText = """
            UPDATE SandboxAdmissionPools
               SET VirtualBase = @base
             WHERE PoolId = @pool AND VirtualBase < @base;
            """;
        advanceBase.AddParam("@base", winner.EffectiveVirtualTime);
        advanceBase.AddParam("@pool", winner.PoolId);
        await advanceBase.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> ReadPoolVirtualBaseAsync(
        DbConnection connection,
        DbTransaction transaction,
        string poolId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT VirtualBase FROM SandboxAdmissionPools WHERE PoolId = @pool;";
        command.AddParam("@pool", poolId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? 0 : Convert.ToInt64(value);
    }

    /// <summary>
    /// Takes the pool's write lock so all grants for one pool serialize before any fair-share state is
    /// read. Without it two nodes could select against the same snapshot and both believe they won.
    /// </summary>
    private static async Task LockPoolAsync(
        DbConnection connection,
        DbTransaction transaction,
        string poolId,
        CancellationToken cancellationToken)
    {
        await EnsurePoolRowAsync(connection, transaction, poolId, cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE SandboxAdmissionPools
               SET VirtualBase = VirtualBase
             WHERE PoolId = @pool;
            """;
        command.AddParam("@pool", poolId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsurePoolRowAsync(
        DbConnection connection,
        DbTransaction transaction,
        string poolId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO SandboxAdmissionPools (PoolId, ActiveCount)
            VALUES (@pool, 0)
            ON CONFLICT (PoolId) DO NOTHING;
            """;
        command.AddParam("@pool", poolId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Virtual-time unit for one grant at weight 1. It is the least common multiple of every allowed
    /// tenant weight (1..16), so <c>Scale / weight</c> is exact for all of them and fairness never
    /// drifts through rounding.
    /// </summary>
    private const long VirtualTimeScale = 720720;

    private sealed record Candidate(
        string AdmissionId,
        string TenantId,
        string PoolId,
        long Sequence,
        int TenantWeight,
        long EffectiveVirtualTime);

    private static async Task<bool> TryReserveCapacityAsync(
        DbConnection connection,
        DbTransaction transaction,
        string poolId,
        string tenantId,
        int poolCapacity,
        int tenantCapacity,
        CancellationToken cancellationToken)
    {
        await EnsurePoolRowAsync(connection, transaction, poolId, cancellationToken);

        await using (var reservePool = connection.CreateCommand())
        {
            reservePool.Transaction = transaction;
            reservePool.CommandText = """
                UPDATE SandboxAdmissionPools
                   SET ActiveCount = ActiveCount + 1
                 WHERE PoolId = @pool AND ActiveCount < @capacity;
                """;
            reservePool.AddParam("@pool", poolId);
            reservePool.AddParam("@capacity", poolCapacity);
            if (await reservePool.ExecuteNonQueryAsync(cancellationToken) != 1)
                return false;
        }

        await using (var ensureTenant = connection.CreateCommand())
        {
            ensureTenant.Transaction = transaction;
            ensureTenant.CommandText = """
                INSERT INTO SandboxAdmissionTenantCapacity (PoolId, TenantId, ActiveCount)
                VALUES (@pool, @tenant, 0)
                ON CONFLICT (PoolId, TenantId) DO NOTHING;
                """;
            ensureTenant.AddParam("@pool", poolId);
            ensureTenant.AddParam("@tenant", tenantId);
            await ensureTenant.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var reserveTenant = connection.CreateCommand();
        reserveTenant.Transaction = transaction;
        reserveTenant.CommandText = """
            UPDATE SandboxAdmissionTenantCapacity
               SET ActiveCount = ActiveCount + 1
             WHERE PoolId = @pool AND TenantId = @tenant AND ActiveCount < @capacity;
            """;
        reserveTenant.AddParam("@pool", poolId);
        reserveTenant.AddParam("@tenant", tenantId);
        reserveTenant.AddParam("@capacity", tenantCapacity);
        return await reserveTenant.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private async Task<bool> TryTransitionAndReleaseCapacityAsync(
        string admissionId,
        string expectedState,
        string targetState,
        string? leaseOwner,
        long fenceToken,
        bool clearReason,
        CancellationToken cancellationToken)
    {
        await using var connection = dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var entry = await ReadEntryAsync(
            connection, transaction, admissionId, expectedState, cancellationToken);
        if (entry is null || entry.FenceToken != fenceToken ||
            (leaseOwner is not null && entry.LeaseOwner != leaseOwner))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await using (var transition = connection.CreateCommand())
        {
            transition.Transaction = transaction;
            transition.CommandText = $"""
                UPDATE SandboxAdmissions
                   SET State = @target, LeaseOwner = NULL, LeaseExpiresUtc = NULL,
                       UpdatedUtc = @now{(clearReason ? ", ReconciliationReason = NULL" : string.Empty)}
                 WHERE AdmissionId = @id AND State = @expected AND FenceToken = @fence
                       {(leaseOwner is null ? string.Empty : "AND LeaseOwner = @owner")};
                """;
            transition.AddParam("@target", targetState);
            transition.AddParam("@now", DateTimeOffset.UtcNow.ToString("O"));
            transition.AddParam("@id", admissionId);
            transition.AddParam("@expected", expectedState);
            transition.AddParam("@fence", fenceToken);
            if (leaseOwner is not null) transition.AddParam("@owner", leaseOwner);
            if (await transition.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        await using (var releasePool = connection.CreateCommand())
        {
            releasePool.Transaction = transaction;
            releasePool.CommandText = """
                UPDATE SandboxAdmissionPools
                   SET ActiveCount = ActiveCount - 1
                 WHERE PoolId = @pool AND ActiveCount > 0;
                """;
            releasePool.AddParam("@pool", entry.PoolId);
            if (await releasePool.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("Sandbox pool capacity ledger is inconsistent.");
        }

        await using (var releaseTenant = connection.CreateCommand())
        {
            releaseTenant.Transaction = transaction;
            releaseTenant.CommandText = """
                UPDATE SandboxAdmissionTenantCapacity
                   SET ActiveCount = ActiveCount - 1
                 WHERE PoolId = @pool AND TenantId = @tenant AND ActiveCount > 0;
                """;
            releaseTenant.AddParam("@pool", entry.PoolId);
            releaseTenant.AddParam("@tenant", entry.TenantId);
            if (await releaseTenant.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("Sandbox tenant capacity ledger is inconsistent.");
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static SandboxAdmissionLedgerEntry ReadEntry(DbDataReader reader) => new(
        Convert.ToInt64(reader.GetValue(reader.GetOrdinal("Sequence"))),
        reader.GetString(reader.GetOrdinal("AdmissionId")),
        reader.GetString(reader.GetOrdinal("TenantId")),
        reader.GetString(reader.GetOrdinal("PoolId")),
        reader.GetInt32(reader.GetOrdinal("TenantWeight")),
        reader.GetInt32(reader.GetOrdinal("MaxConcurrentAttempts")),
        reader.GetInt32(reader.GetOrdinal("MaxQueuedAttempts")),
        Enum.Parse<SandboxAdmissionState>(reader.GetString(reader.GetOrdinal("State")), true),
        ReadNullableString(reader, "LeaseOwner"),
        ReadNullableDateTimeOffset(reader, "LeaseExpiresUtc"),
        reader.GetInt64(reader.GetOrdinal("FenceToken")),
        DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("EnqueuedUtc"))),
        DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("UpdatedUtc"))),
        ReadNullableString(reader, "ReconciliationReason"),
        ReadNullableString(reader, "ClaimedByNode"),
        ReadNullableDateTimeOffset(reader, "ClaimHeartbeatUtc"));

    private static string? ReadNullableString(DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(DbDataReader reader, string name)
    {
        var value = ReadNullableString(reader, name);
        return value is null ? null : DateTimeOffset.Parse(value);
    }

    private static void ValidateLeaseArguments(string admissionId, string leaseOwner, TimeSpan duration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(admissionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
    }

    private static void ValidateOwnedMutation(string admissionId, string leaseOwner, long fenceToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(admissionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        if (fenceToken <= 0) throw new ArgumentOutOfRangeException(nameof(fenceToken));
    }
}
