using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Orchestrator.Storage
{
    /// <summary>
    /// Relational (provider-neutral) job history / bundle / lineage store. The connection, schema DDL,
    /// and the few non-portable SQL constructs come from an <see cref="IOrchestratorStoreDialect"/>, so
    /// the same logic runs on SQLite (default) and PostgreSQL (Practical HA). The SQLite entry point is
    /// <see cref="SQLiteJobHistoryStore"/>.
    /// </summary>
    public class RelationalJobHistoryStore : IJobHistoryStore, IJobScheduleQueryStore, IBundleStore, ILineageCatalogStore, INodeRegistryStore, IWriteEpochStore, IClusterLockStore
    {
        private readonly IOrchestratorStoreDialect _dialect;
        private bool _initialized;
        private readonly System.Threading.SemaphoreSlim _initLock = new(1, 1);

        public RelationalJobHistoryStore(IOrchestratorStoreDialect dialect)
        {
            _dialect = dialect;
        }

        private async Task EnsureInitializedAsync()
        {
            await InitializeAsync();
        }

        /// <summary>Initializes the SQLite database and creates the necessary tables if they don't exist.</summary>
        public async Task InitializeAsync()
        {
            if (_initialized) return;
            await _initLock.WaitAsync();
            try
            {
                if (_initialized) return;

                using var connection = _dialect.CreateConnection();
                await connection.OpenAsync();
                await ExecuteOptionalSqlAsync(connection, _dialect.SchemaInitializationLockSql);

                try
                {
                    var createJobsTable = @"
                CREATE TABLE IF NOT EXISTS Jobs (
                    Name TEXT PRIMARY KEY,
                    Script TEXT NOT NULL,
                    Interval INTEGER NOT NULL,
                    Unit TEXT NOT NULL,
                    AtTime TEXT,
                    LastRun TEXT,
                    NextRun TEXT,
                    IsEnabled INTEGER NOT NULL DEFAULT 1,
                    MaxRetries INTEGER NOT NULL DEFAULT 0,
                    RetryDelaySeconds INTEGER NOT NULL DEFAULT 30,
                    Version INTEGER NOT NULL DEFAULT 1,
                    LeaseFenceToken INTEGER NOT NULL DEFAULT 0
                );";

                    var createHistoryTable = @"
                CREATE TABLE IF NOT EXISTS JobHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    JobName TEXT NOT NULL,
                    StartTime TEXT NOT NULL,
                    EndTime TEXT,
                    Status TEXT NOT NULL,
                    ErrorMessage TEXT,
                    RowsProcessed INTEGER DEFAULT 0
                );";

                    var createBundleTables = @"
                CREATE TABLE IF NOT EXISTS BundleVersions (
                    BundleName TEXT NOT NULL,
                    Version INTEGER NOT NULL,
                    EntryPath TEXT NOT NULL,
                    ContentHash TEXT NOT NULL,
                    PublishedAt TEXT NOT NULL,
                    Publisher TEXT,
                    Description TEXT,
                    EncryptionMode TEXT NOT NULL DEFAULT 'MACHINE',
                    EncryptionMetadata TEXT,
                    PRIMARY KEY (BundleName, Version)
                );

                CREATE TABLE IF NOT EXISTS BundleFiles (
                    BundleName TEXT NOT NULL,
                    Version INTEGER NOT NULL,
                    VirtualPath TEXT NOT NULL,
                    Content TEXT NOT NULL,
                    ContentHash TEXT NOT NULL,
                    SizeBytes INTEGER NOT NULL,
                    ContentType TEXT NOT NULL,
                    PRIMARY KEY (BundleName, Version, VirtualPath),
                    FOREIGN KEY (BundleName, Version) REFERENCES BundleVersions(BundleName, Version)
                );

                CREATE TABLE IF NOT EXISTS BundleDependencies (
                    BundleName TEXT NOT NULL,
                    Version INTEGER NOT NULL,
                    FromPath TEXT NOT NULL,
                    ToPath TEXT NOT NULL,
                    PRIMARY KEY (BundleName, Version, FromPath, ToPath),
                    FOREIGN KEY (BundleName, Version) REFERENCES BundleVersions(BundleName, Version)
                );";

                    var createLineageHistoryTable = @"
                CREATE TABLE IF NOT EXISTS LineageHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    RunAt TEXT NOT NULL,
                    JobName TEXT,
                    ScriptPath TEXT,
                    TargetTable TEXT NOT NULL,
                    TargetColumn TEXT,
                    SourceTables TEXT NOT NULL DEFAULT '[]',
                    SourceColumns TEXT NOT NULL DEFAULT '[]',
                    Operation TEXT NOT NULL,
                    Tags TEXT NOT NULL DEFAULT '{}',
                    SourceFile TEXT,
                    Line INTEGER NOT NULL DEFAULT 0,
                    TransformationKind TEXT,
                    TransformationExpression TEXT,
                    FunctionsApplied TEXT NOT NULL DEFAULT '[]',
                    DerivedFromDescriptions TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_jobs_sched ON Jobs(IsEnabled, NextRun);
                CREATE INDEX IF NOT EXISTS idx_jh_job_start ON JobHistory(JobName, StartTime);
                CREATE INDEX IF NOT EXISTS idx_lh_target ON LineageHistory(TargetTable COLLATE NOCASE);
                CREATE INDEX IF NOT EXISTS idx_lh_runAt ON LineageHistory(RunAt);";

                    // Cluster node registry (P1.7): one row per live Portal/Orchestrator process, kept fresh
                    // by a TTL heartbeat. NodeId is a process-unique generated id, so no NOCASE is needed.
                    var createNodesTable = @"
                CREATE TABLE IF NOT EXISTS Nodes (
                    NodeId TEXT PRIMARY KEY,
                    Role TEXT NOT NULL,
                    FirstSeenAt TEXT NOT NULL,
                    LastHeartbeatAt TEXT NOT NULL,
                    ExpiresAt TEXT NOT NULL,
                    Metadata TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_nodes_expires ON Nodes(ExpiresAt);";

                    // Write-epoch fencing for shared storage (P1.8): the highest fence token that has written
                    // each (Scope, EpochKey) resource, so a stale writer cannot overwrite a newer one.
                    var createWriteEpochsTable = @"
                CREATE TABLE IF NOT EXISTS WriteEpochs (
                    Scope TEXT NOT NULL,
                    EpochKey TEXT NOT NULL,
                    Epoch INTEGER NOT NULL,
                    PRIMARY KEY (Scope, EpochKey)
                );";

                    // Distributed locks / leader election (P1.9): one TTL-leased holder per named lock. Lives
                    // here (a CREATE-IF-NOT-EXISTS store) rather than the EF-migrated catalog so it exists
                    // before any node runs migrations.
                    var createClusterLocksTable = @"
                CREATE TABLE IF NOT EXISTS ClusterLocks (
                    LockName TEXT PRIMARY KEY,
                    Owner TEXT NOT NULL,
                    ExpiresAt TEXT NOT NULL
                );";

                    var createJobStateTable = @"
                CREATE TABLE IF NOT EXISTS JobState (
                    JobName TEXT NOT NULL,
                    StateKey TEXT NOT NULL,
                    StateValue TEXT,
                    UpdatedAt TEXT NOT NULL,
                    PRIMARY KEY (JobName, StateKey)
                );";

                    var schema = createJobsTable + createHistoryTable + createBundleTables
                        + createLineageHistoryTable + createNodesTable + createWriteEpochsTable + createClusterLocksTable + createJobStateTable;
                    // SQLite's auto-increment PK literal is the default; the dialect rewrites it for other
                    // providers (e.g. PostgreSQL identity columns). CollationDdl (if any) runs first so the
                    // COLLATE NOCASE indexes/queries resolve.
                    schema = schema.Replace("INTEGER PRIMARY KEY AUTOINCREMENT", _dialect.AutoIncrementPrimaryKey);

                    await EnsureCollationExistsAsync(connection);

                    using var command = connection.CreateCommand();
                    command.CommandText = schema;
                    await command.ExecuteNonQueryAsync();

                    // 8B-2: Schema migration — add resource tracking columns if missing
                    await EnsureHistoryColumnsExist(connection);
                    await EnsureJobColumnsExist(connection);
                    await EnsureLineageHistoryColumnsExist(connection);
                }
                finally
                {
                    await ExecuteOptionalSqlAsync(connection, _dialect.SchemaInitializationUnlockSql);
                }

                _initialized = true;
            }
            finally
            {
                _initLock.Release();
            }
        }

        private static async Task ExecuteOptionalSqlAsync(DbConnection connection, string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
                return;

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        private async Task EnsureCollationExistsAsync(DbConnection connection)
        {
            if (string.IsNullOrWhiteSpace(_dialect.CollationDdl))
                return;

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = _dialect.CollationDdl;
                await command.ExecuteNonQueryAsync();
            }
            catch (DbException ex) when (IsDuplicateObjectRace(ex))
            {
                // PostgreSQL's CREATE COLLATION IF NOT EXISTS can still race before the
                // catalog row is visible to another concurrent startup process. Treat the
                // duplicate as success; the winning process created the required collation.
            }
        }

        private static bool IsDuplicateObjectRace(DbException ex)
        {
            var sqlState = ex.GetType().GetProperty("SqlState")?.GetValue(ex) as string;
            return string.Equals(sqlState, "23505", StringComparison.Ordinal);
        }

        private async Task EnsureJobColumnsExist(DbConnection connection)
        {
            var columns = await _dialect.GetColumnNamesAsync(connection, "Jobs");

            if (!columns.Contains("MaxRetries"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Jobs ADD COLUMN MaxRetries INTEGER NOT NULL DEFAULT 0;";
                await cmd.ExecuteNonQueryAsync();
            }

            if (!columns.Contains("RetryDelaySeconds"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Jobs ADD COLUMN RetryDelaySeconds INTEGER NOT NULL DEFAULT 30;";
                await cmd.ExecuteNonQueryAsync();
            }

            if (!columns.Contains("ScriptHash"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Jobs ADD COLUMN ScriptHash TEXT;";
                await cmd.ExecuteNonQueryAsync();
            }

            if (!columns.Contains("HashPolicy"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Jobs ADD COLUMN HashPolicy TEXT NOT NULL DEFAULT 'Warn';";
                await cmd.ExecuteNonQueryAsync();
            }

            if (!columns.Contains("LeaseOwner"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Jobs ADD COLUMN LeaseOwner TEXT;";
                await cmd.ExecuteNonQueryAsync();
            }

            if (!columns.Contains("LeaseExpiresAt"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Jobs ADD COLUMN LeaseExpiresAt TEXT;";
                await cmd.ExecuteNonQueryAsync();
            }

            if (!columns.Contains("LeaseFenceToken"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Jobs ADD COLUMN LeaseFenceToken INTEGER NOT NULL DEFAULT 0;";
                await cmd.ExecuteNonQueryAsync();
            }

            if (!columns.Contains("Version"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Jobs ADD COLUMN Version INTEGER NOT NULL DEFAULT 1;";
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private async Task EnsureHistoryColumnsExist(DbConnection connection)
        {
            var columns = await _dialect.GetColumnNamesAsync(connection, "JobHistory");

            if (!columns.Contains("PeakMemoryBytes"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE JobHistory ADD COLUMN PeakMemoryBytes INTEGER DEFAULT 0;";
                await cmd.ExecuteNonQueryAsync();
            }

            if (!columns.Contains("CpuTimeSeconds"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE JobHistory ADD COLUMN CpuTimeSeconds REAL DEFAULT 0;";
                await cmd.ExecuteNonQueryAsync();
            }

            if (!columns.Contains("ScriptHashAtRunTime"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE JobHistory ADD COLUMN ScriptHashAtRunTime TEXT;";
                await cmd.ExecuteNonQueryAsync();
            }

            if (!columns.Contains("HashMatched"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE JobHistory ADD COLUMN HashMatched INTEGER;";
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private async Task EnsureLineageHistoryColumnsExist(DbConnection connection)
        {
            var columns = await _dialect.GetColumnNamesAsync(connection, "LineageHistory");

            async Task AddColumn(string name, string ddl)
            {
                if (columns.Contains(name)) return;
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"ALTER TABLE LineageHistory ADD COLUMN {ddl};";
                await cmd.ExecuteNonQueryAsync();
            }

            // SourceColumns predates this migration on new installs but may be
            // missing on databases created before it was added to the schema.
            await AddColumn("SourceColumns", "SourceColumns TEXT NOT NULL DEFAULT '[]'");
            await AddColumn("TransformationKind", "TransformationKind TEXT");
            await AddColumn("TransformationExpression", "TransformationExpression TEXT");
            await AddColumn("FunctionsApplied", "FunctionsApplied TEXT NOT NULL DEFAULT '[]'");
            await AddColumn("DerivedFromDescriptions", "DerivedFromDescriptions TEXT");
        }

        public async Task SaveJobAsync(JobDefinition job)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            // Upsert (not INSERT OR REPLACE): REPLACE deletes and reinserts the row, which would
            // silently clear an active execution lease whenever a job definition is re-saved.
            var sql = @"
                INSERT INTO Jobs (Name, Script, Interval, Unit, AtTime, LastRun, NextRun, IsEnabled, MaxRetries, RetryDelaySeconds, ScriptHash, HashPolicy)
                VALUES (@name, @script, @interval, @unit, @atTime, @lastRun, @nextRun, @isEnabled, @maxRetries, @retryDelay, @scriptHash, @hashPolicy)
                ON CONFLICT(Name) DO UPDATE SET
                    Script            = excluded.Script,
                    Interval          = excluded.Interval,
                    Unit              = excluded.Unit,
                    AtTime            = excluded.AtTime,
                    LastRun           = excluded.LastRun,
                    NextRun           = excluded.NextRun,
                    IsEnabled         = excluded.IsEnabled,
                    MaxRetries        = excluded.MaxRetries,
                    RetryDelaySeconds = excluded.RetryDelaySeconds,
                    ScriptHash        = excluded.ScriptHash,
                    HashPolicy        = excluded.HashPolicy,
                    Version           = Jobs.Version + 1;";

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.AddParam("@name", job.Name);
            command.AddParam("@script", job.Script);
            command.AddParam("@interval", job.Interval);
            command.AddParam("@unit", job.Unit);
            command.AddParam("@atTime", (object?)job.AtTime ?? DBNull.Value);
            command.AddParam("@lastRun", (object?)job.LastRun?.ToString("O") ?? DBNull.Value);
            command.AddParam("@nextRun", (object?)job.NextRun?.ToString("O") ?? DBNull.Value);
            command.AddParam("@isEnabled", job.IsEnabled ? 1 : 0);
            command.AddParam("@maxRetries", job.MaxRetries);
            command.AddParam("@retryDelay", job.RetryDelaySeconds);
            command.AddParam("@scriptHash", (object?)job.ScriptHash ?? DBNull.Value);
            command.AddParam("@hashPolicy", job.HashPolicy);

            await command.ExecuteNonQueryAsync();
        }

        public async Task<bool> TrySaveJobAsync(JobDefinition job, long expectedVersion)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Jobs SET
                    Script = @script,
                    Interval = @interval,
                    Unit = @unit,
                    AtTime = @atTime,
                    LastRun = @lastRun,
                    NextRun = @nextRun,
                    IsEnabled = @isEnabled,
                    MaxRetries = @maxRetries,
                    RetryDelaySeconds = @retryDelay,
                    ScriptHash = @scriptHash,
                    HashPolicy = @hashPolicy,
                    Version = Version + 1
                WHERE Name = @name COLLATE NOCASE AND Version = @expectedVersion;";
            AddJobParameters(command, job);
            command.AddParam("@expectedVersion", expectedVersion);
            return await command.ExecuteNonQueryAsync() == 1;
        }

        private static void AddJobParameters(DbCommand command, JobDefinition job)
        {
            command.AddParam("@name", job.Name);
            command.AddParam("@script", job.Script);
            command.AddParam("@interval", job.Interval);
            command.AddParam("@unit", job.Unit);
            command.AddParam("@atTime", (object?)job.AtTime ?? DBNull.Value);
            command.AddParam("@lastRun", (object?)job.LastRun?.ToString("O") ?? DBNull.Value);
            command.AddParam("@nextRun", (object?)job.NextRun?.ToString("O") ?? DBNull.Value);
            command.AddParam("@isEnabled", job.IsEnabled ? 1 : 0);
            command.AddParam("@maxRetries", job.MaxRetries);
            command.AddParam("@retryDelay", job.RetryDelaySeconds);
            command.AddParam("@scriptHash", (object?)job.ScriptHash ?? DBNull.Value);
            command.AddParam("@hashPolicy", job.HashPolicy);
        }

        // ── Execution lease (P1.1) ────────────────────────────────────────────────
        // Lease times are UTC ISO-8601 ("O") strings: they compare correctly both lexically
        // and via SQLite's date functions, and stay unambiguous across hosts in different
        // time zones. SQLite's single-writer model makes each UPDATE atomic, which is the
        // entire claim mechanism — but it also means the lease only coordinates processes
        // that share this database file (see the P3.1 topology decision).

        public async Task<bool> TryAcquireJobLeaseAsync(string jobName, string owner, TimeSpan duration)
            => await AcquireJobLeaseAsync(jobName, owner, duration) is not null;

        public async Task<long?> AcquireJobLeaseAsync(string jobName, string owner, TimeSpan duration)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            var now = DateTime.UtcNow;
            // A successful claim advances the fence token (a renewal, elsewhere, does not). The token is
            // therefore strictly increasing across ownership changes, which is what fences out a stale
            // owner that resumes after a partition.
            using var claim = connection.CreateCommand();
            claim.CommandText = @"
                UPDATE Jobs SET LeaseOwner = @owner, LeaseExpiresAt = @expires, LeaseFenceToken = LeaseFenceToken + 1
                WHERE Name = @name
                  AND (LeaseOwner IS NULL OR LeaseExpiresAt IS NULL OR LeaseExpiresAt <= @now);";
            claim.AddParam("@owner", owner);
            claim.AddParam("@expires", now.Add(duration).ToString("O"));
            claim.AddParam("@name", jobName);
            claim.AddParam("@now", now.ToString("O"));

            if (await claim.ExecuteNonQueryAsync() != 1)
                return null;

            // We now own the row; read back the token we were granted.
            using var read = connection.CreateCommand();
            read.CommandText = "SELECT LeaseFenceToken FROM Jobs WHERE Name = @name AND LeaseOwner = @owner;";
            read.AddParam("@name", jobName);
            read.AddParam("@owner", owner);
            var token = await read.ExecuteScalarAsync();
            return token is null or DBNull ? null : Convert.ToInt64(token);
        }

        public async Task<bool> ValidateFenceTokenAsync(string jobName, long fenceToken)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT LeaseFenceToken FROM Jobs WHERE Name = @name;";
            command.AddParam("@name", jobName);
            var current = await command.ExecuteScalarAsync();
            // A token is valid only if it is the latest issued — a newer acquisition would have advanced
            // the stored token beyond the holder's.
            return current is not (null or DBNull) && fenceToken >= Convert.ToInt64(current);
        }

        public async Task<bool> TryUpdateJobLastRunFencedAsync(string name, DateTime lastRun, DateTime? nextRun, long fenceToken)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            // The write carries the fence token: it lands only while the holder is still the current
            // owner (token unchanged). If a newer owner has acquired the lease, the token moved and this
            // UPDATE matches zero rows — the stale writer is fenced out.
            command.CommandText = @"
                UPDATE Jobs SET LastRun = @lastRun, NextRun = @nextRun
                WHERE Name = @name AND LeaseFenceToken = @token;";
            command.AddParam("@lastRun", lastRun.ToString("O"));
            command.AddParam("@nextRun", (object?)nextRun?.ToString("O") ?? DBNull.Value);
            command.AddParam("@name", name);
            command.AddParam("@token", fenceToken);

            return await command.ExecuteNonQueryAsync() == 1;
        }

        public async Task<bool> TryRenewJobLeaseAsync(string jobName, string owner, TimeSpan duration)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Jobs SET LeaseExpiresAt = @expires
                WHERE Name = @name AND LeaseOwner = @owner;";
            command.AddParam("@expires", DateTime.UtcNow.Add(duration).ToString("O"));
            command.AddParam("@name", jobName);
            command.AddParam("@owner", owner);

            return await command.ExecuteNonQueryAsync() == 1;
        }

        public async Task ReleaseJobLeaseAsync(string jobName, string owner)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Jobs SET LeaseOwner = NULL, LeaseExpiresAt = NULL
                WHERE Name = @name AND LeaseOwner = @owner;";
            command.AddParam("@name", jobName);
            command.AddParam("@owner", owner);

            await command.ExecuteNonQueryAsync();
        }

        // ── Node registry (P1.7) ──────────────────────────────────────────────────
        // Times are UTC ISO-8601 ("O") strings, like the execution lease: lexically and chronologically
        // ordered, and unambiguous across hosts in different time zones.

        public async Task RegisterOrRenewNodeAsync(string nodeId, string role, TimeSpan ttl, string? metadata = null)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            var now = await GetDatabaseUtcNowAsync(connection);
            using var command = connection.CreateCommand();
            // Upsert: a renewal preserves FirstSeenAt (only excluded.* for the mutable columns).
            command.CommandText = @"
                INSERT INTO Nodes (NodeId, Role, FirstSeenAt, LastHeartbeatAt, ExpiresAt, Metadata)
                VALUES (@id, @role, @now, @now, @expires, @meta)
                ON CONFLICT(NodeId) DO UPDATE SET
                    Role            = excluded.Role,
                    LastHeartbeatAt = excluded.LastHeartbeatAt,
                    ExpiresAt       = excluded.ExpiresAt,
                    Metadata        = excluded.Metadata;";
            command.AddParam("@id", nodeId);
            command.AddParam("@role", role);
            command.AddParam("@now", now.ToString("O"));
            command.AddParam("@expires", now.Add(ttl).ToString("O"));
            command.AddParam("@meta", (object?)metadata ?? DBNull.Value);

            await command.ExecuteNonQueryAsync();
        }

        public async Task<IReadOnlyList<NodeHeartbeat>> GetLiveNodesAsync()
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT NodeId, Role, FirstSeenAt, LastHeartbeatAt, ExpiresAt, Metadata
                FROM Nodes WHERE ExpiresAt > @now ORDER BY Role, NodeId;";
            command.AddParam("@now", (await GetDatabaseUtcNowAsync(connection)).ToString("O"));
            return await ReadNodesAsync(command);
        }

        public async Task<IReadOnlyList<NodeHeartbeat>> GetAllNodesAsync()
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT NodeId, Role, FirstSeenAt, LastHeartbeatAt, ExpiresAt, Metadata
                FROM Nodes ORDER BY Role, NodeId;";
            return await ReadNodesAsync(command);
        }

        public async Task DeregisterNodeAsync(string nodeId)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Nodes WHERE NodeId = @id;";
            command.AddParam("@id", nodeId);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<int> PruneExpiredNodesAsync()
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Nodes WHERE ExpiresAt <= @now;";
            command.AddParam("@now", (await GetDatabaseUtcNowAsync(connection)).ToString("O"));
            return await command.ExecuteNonQueryAsync();
        }

        private async Task<DateTime> GetDatabaseUtcNowAsync(DbConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = _dialect.UtcNowSql;
            var value = await command.ExecuteScalarAsync();
            return value switch
            {
                DateTimeOffset dto => dto.UtcDateTime,
                DateTime dt => dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc),
                string text => ParseUtc(text),
                _ => DateTime.UtcNow
            };
        }

        private static async Task<IReadOnlyList<NodeHeartbeat>> ReadNodesAsync(DbCommand command)
        {
            var nodes = new List<NodeHeartbeat>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                nodes.Add(new NodeHeartbeat(
                    reader.GetString(0),
                    reader.GetString(1),
                    ParseUtc(reader.GetString(2)),
                    ParseUtc(reader.GetString(3)),
                    ParseUtc(reader.GetString(4)),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
            }
            return nodes;
        }

        private static DateTime ParseUtc(string iso) =>
            DateTime.Parse(iso, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();

        // ── Write-epoch fencing for shared storage (P1.8) ─────────────────────────

        public async Task<bool> TryClaimWriteEpochAsync(string scope, string key, long token)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            // Atomic compare-and-advance: the conflicting UPDATE only fires when the incoming token is at
            // least the stored epoch, so a stale (lower) token leaves the row untouched and affects zero
            // rows. The conditional ON CONFLICT ... WHERE is supported by both SQLite and PostgreSQL.
            command.CommandText = @"
                INSERT INTO WriteEpochs (Scope, EpochKey, Epoch) VALUES (@scope, @key, @token)
                ON CONFLICT(Scope, EpochKey) DO UPDATE SET Epoch = excluded.Epoch
                    WHERE excluded.Epoch >= WriteEpochs.Epoch;";
            command.AddParam("@scope", scope);
            command.AddParam("@key", key);
            command.AddParam("@token", token);

            return await command.ExecuteNonQueryAsync() == 1;
        }

        public async Task<long> GetWriteEpochAsync(string scope, string key)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Epoch FROM WriteEpochs WHERE Scope = @scope AND EpochKey = @key;";
            command.AddParam("@scope", scope);
            command.AddParam("@key", key);
            var epoch = await command.ExecuteScalarAsync();
            return epoch is null or DBNull ? 0 : Convert.ToInt64(epoch);
        }

        // ── Distributed locks / leader election (P1.9) ────────────────────────────

        public async Task<bool> TryAcquireLockAsync(string lockName, string owner, TimeSpan ttl)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            var now = DateTime.UtcNow;
            using var command = connection.CreateCommand();
            // Claim on insert (free lock) or on conflict only when the existing lease has expired or we
            // already own it. A live lock held by another owner leaves the row untouched (zero rows).
            command.CommandText = @"
                INSERT INTO ClusterLocks (LockName, Owner, ExpiresAt) VALUES (@name, @owner, @expires)
                ON CONFLICT(LockName) DO UPDATE SET Owner = excluded.Owner, ExpiresAt = excluded.ExpiresAt
                    WHERE ClusterLocks.ExpiresAt <= @now OR ClusterLocks.Owner = excluded.Owner;";
            command.AddParam("@name", lockName);
            command.AddParam("@owner", owner);
            command.AddParam("@expires", now.Add(ttl).ToString("O"));
            command.AddParam("@now", now.ToString("O"));

            return await command.ExecuteNonQueryAsync() == 1;
        }

        public async Task<bool> TryRenewLockAsync(string lockName, string owner, TimeSpan ttl)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE ClusterLocks SET ExpiresAt = @expires
                WHERE LockName = @name AND Owner = @owner;";
            command.AddParam("@expires", DateTime.UtcNow.Add(ttl).ToString("O"));
            command.AddParam("@name", lockName);
            command.AddParam("@owner", owner);

            return await command.ExecuteNonQueryAsync() == 1;
        }

        public async Task ReleaseLockAsync(string lockName, string owner)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM ClusterLocks WHERE LockName = @name AND Owner = @owner;";
            command.AddParam("@name", lockName);
            command.AddParam("@owner", owner);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<string?> GetLockHolderAsync(string lockName)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Owner FROM ClusterLocks WHERE LockName = @name AND ExpiresAt > @now;";
            command.AddParam("@name", lockName);
            command.AddParam("@now", DateTime.UtcNow.ToString("O"));
            var holder = await command.ExecuteScalarAsync();
            return holder is null or DBNull ? null : (string)holder;
        }

        public async Task<IEnumerable<JobDefinition>> GetActiveJobsAsync()
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            var sql = "SELECT * FROM Jobs WHERE IsEnabled = 1;";
            using var command = connection.CreateCommand();
            command.CommandText = sql;

            var jobs = new List<JobDefinition>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                jobs.Add(ReadJob(reader));
            }
            return jobs;
        }

        public async Task<IEnumerable<JobDefinition>> GetDueJobsAsync(DateTime now)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Jobs WHERE IsEnabled = 1 AND (NextRun IS NULL OR NextRun <= @now);";
            command.AddParam("@now", now.ToString("O"));

            var jobs = new List<JobDefinition>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                jobs.Add(ReadJob(reader));
            }
            return jobs;
        }

        public async Task<IEnumerable<JobDefinition>> GetAllJobsAsync()
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Jobs;";

            var jobs = new List<JobDefinition>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                jobs.Add(ReadJob(reader));
            }
            return jobs;
        }

        public async Task<JobDefinition?> GetJobAsync(string name)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Jobs WHERE Name = @name COLLATE NOCASE LIMIT 1;";
            command.AddParam("@name", name);

            using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            return ReadJob(reader);
        }

        private static JobDefinition ReadJob(DbDataReader reader)
        {
            var lastRunOrdinal = reader.GetOrdinal("LastRun");
            var nextRunOrdinal = reader.GetOrdinal("NextRun");
            var atTimeOrdinal = reader.GetOrdinal("AtTime");
            var scriptHashOrdinal = reader.GetOrdinal("ScriptHash");
            var hashPolicyOrdinal = reader.GetOrdinal("HashPolicy");
            var versionOrdinal = reader.GetOrdinal("Version");
            return new JobDefinition(
                reader.GetString(reader.GetOrdinal("Name")),
                reader.GetString(reader.GetOrdinal("Script")),
                reader.GetInt32(reader.GetOrdinal("Interval")),
                reader.GetString(reader.GetOrdinal("Unit")),
                reader.IsDBNull(atTimeOrdinal) ? null : reader.GetString(atTimeOrdinal),
                reader.IsDBNull(lastRunOrdinal) ? null : DateTime.Parse(reader.GetString(lastRunOrdinal)),
                reader.IsDBNull(nextRunOrdinal) ? null : DateTime.Parse(reader.GetString(nextRunOrdinal)),
                reader.GetInt32(reader.GetOrdinal("IsEnabled")) == 1,
                reader.GetInt32(reader.GetOrdinal("MaxRetries")),
                reader.GetInt32(reader.GetOrdinal("RetryDelaySeconds")),
                reader.IsDBNull(scriptHashOrdinal) ? null : reader.GetString(scriptHashOrdinal),
                reader.IsDBNull(hashPolicyOrdinal) ? "Warn" : reader.GetString(hashPolicyOrdinal),
                reader.GetInt64(versionOrdinal));
        }

        public async Task DeleteJobAsync(string name)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                var sql1 = "DELETE FROM Jobs WHERE Name = @name;";
                using var command1 = connection.CreateCommand();
                command1.CommandText = sql1;
                command1.Transaction = transaction;
                command1.AddParam("@name", name);
                await command1.ExecuteNonQueryAsync();

                var sql2 = "DELETE FROM JobHistory WHERE JobName = @name;";
                using var command2 = connection.CreateCommand();
                command2.CommandText = sql2;
                command2.Transaction = transaction;
                command2.AddParam("@name", name);
                await command2.ExecuteNonQueryAsync();

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<bool> TryDeleteJobAsync(string name, long expectedVersion)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            using var deleteJob = connection.CreateCommand();
            deleteJob.Transaction = transaction;
            deleteJob.CommandText = "DELETE FROM Jobs WHERE Name = @name COLLATE NOCASE AND Version = @version;";
            deleteJob.AddParam("@name", name);
            deleteJob.AddParam("@version", expectedVersion);
            if (await deleteJob.ExecuteNonQueryAsync() != 1)
            {
                await transaction.RollbackAsync();
                return false;
            }

            using var deleteHistory = connection.CreateCommand();
            deleteHistory.Transaction = transaction;
            deleteHistory.CommandText = "DELETE FROM JobHistory WHERE JobName = @name;";
            deleteHistory.AddParam("@name", name);
            await deleteHistory.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
            return true;
        }

        public async Task UpdateJobLastRunAsync(string name, DateTime lastRun, DateTime? nextRun)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            var sql = "UPDATE Jobs SET LastRun = @lastRun, NextRun = @nextRun WHERE Name = @name;";
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.AddParam("@name", name);
            command.AddParam("@lastRun", lastRun.ToString("O"));
            command.AddParam("@nextRun", (object?)nextRun?.ToString("O") ?? DBNull.Value);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<long> LogJobStartAsync(string jobName)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            var sql = _dialect.InsertReturningId(
                "INSERT INTO JobHistory (JobName, StartTime, Status) VALUES (@name, @start, 'RUNNING')", "Id");
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.AddParam("@name", jobName);
            command.AddParam("@start", DateTime.Now.ToString("O"));

            // SQLite's last_insert_rowid() returns long; Postgres RETURNING id returns int — normalize.
            return Convert.ToInt64((await command.ExecuteScalarAsync())!);
        }

        public async Task LogJobEndAsync(long entryId, string status, string? errorMessage = null, long rowsProcessed = 0, long peakMemoryBytes = 0, double cpuTimeSeconds = 0, string? scriptHashAtRunTime = null, bool? hashMatched = null)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            var sql = "UPDATE JobHistory SET EndTime = @end, Status = @status, ErrorMessage = @err, RowsProcessed = @rows, PeakMemoryBytes = @mem, CpuTimeSeconds = @cpu, ScriptHashAtRunTime = @hash, HashMatched = @matched WHERE Id = @id;";
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.AddParam("@id", entryId);
            command.AddParam("@end", DateTime.Now.ToString("O"));
            command.AddParam("@status", status);
            command.AddParam("@err", (object?)errorMessage ?? DBNull.Value);
            command.AddParam("@rows", rowsProcessed);
            command.AddParam("@mem", peakMemoryBytes);
            command.AddParam("@cpu", cpuTimeSeconds);
            command.AddParam("@hash", (object?)scriptHashAtRunTime ?? DBNull.Value);
            command.AddParam("@matched", hashMatched.HasValue ? (object)(hashMatched.Value ? 1 : 0) : DBNull.Value);

            await command.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<JobHistoryEntry>> GetHistoryAsync(string? jobName = null, int limit = 100)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            var sql = "SELECT * FROM JobHistory ";
            if (jobName != null) sql += "WHERE JobName = @name ";
            sql += "ORDER BY StartTime DESC LIMIT @limit;";

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            if (jobName != null) command.AddParam("@name", jobName);
            command.AddParam("@limit", limit);

            var entries = new List<JobHistoryEntry>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                entries.Add(new JobHistoryEntry(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    DateTime.Parse(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3)),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetInt64(6),
                    reader.IsDBNull(7) ? 0 : reader.GetInt64(7),
                    reader.IsDBNull(8) ? 0 : reader.GetDouble(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : (bool?)(reader.GetInt32(10) != 0)
                ));
            }
            return entries;
        }

        public async Task<string?> GetJobStateAsync(string jobName, string key)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT StateValue FROM JobState WHERE JobName = @jobName AND StateKey = @key;";
            command.AddParam("@jobName", jobName);
            command.AddParam("@key", key);

            var result = await command.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : (string?)result;
        }

        public async Task SetJobStateAsync(string jobName, string key, string? value)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO JobState (JobName, StateKey, StateValue, UpdatedAt)
                VALUES (@jobName, @key, @value, @updatedAt)
                ON CONFLICT (JobName, StateKey)
                DO UPDATE SET StateValue = EXCLUDED.StateValue, UpdatedAt = EXCLUDED.UpdatedAt;";

            command.AddParam("@jobName", jobName);
            command.AddParam("@key", key);
            command.AddParam("@value", value);
            command.AddParam("@updatedAt", DateTime.UtcNow.ToString("o"));

            await command.ExecuteNonQueryAsync();
        }

        public async Task<BundleVersionInfo> PublishBundleAsync(BundlePublishRequest request)
        {
            await EnsureInitializedAsync();
            var latest = await GetLatestVersionAsync(request.BundleName);
            if (latest != null && string.Equals(latest.ContentHash, request.ContentHash, StringComparison.OrdinalIgnoreCase))
                return latest;

            var nextVersion = (latest?.Version ?? 0) + 1;
            var publishedAt = DateTime.Now;

            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            try
            {
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO BundleVersions
                            (BundleName, Version, EntryPath, ContentHash, PublishedAt, Publisher, Description, EncryptionMode, EncryptionMetadata)
                        VALUES
                            (@bundle, @version, @entry, @hash, @published, @publisher, @description, @mode, @metadata);";
                    cmd.AddParam("@bundle", request.BundleName);
                    cmd.AddParam("@version", nextVersion);
                    cmd.AddParam("@entry", NormalizeVirtualPath(request.EntryPath));
                    cmd.AddParam("@hash", request.ContentHash);
                    cmd.AddParam("@published", publishedAt.ToString("O"));
                    cmd.AddParam("@publisher", (object?)request.Publisher ?? DBNull.Value);
                    cmd.AddParam("@description", (object?)request.Description ?? DBNull.Value);
                    cmd.AddParam("@mode", request.EncryptionMode);
                    cmd.AddParam("@metadata", (object?)request.EncryptionMetadata ?? DBNull.Value);
                    await cmd.ExecuteNonQueryAsync();
                }

                foreach (var file in request.Files)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO BundleFiles
                            (BundleName, Version, VirtualPath, Content, ContentHash, SizeBytes, ContentType)
                        VALUES
                            (@bundle, @version, @path, @content, @hash, @size, @type);";
                    cmd.AddParam("@bundle", request.BundleName);
                    cmd.AddParam("@version", nextVersion);
                    cmd.AddParam("@path", NormalizeVirtualPath(file.VirtualPath));
                    cmd.AddParam("@content", file.Content);
                    cmd.AddParam("@hash", file.ContentHash);
                    cmd.AddParam("@size", file.SizeBytes);
                    cmd.AddParam("@type", file.ContentType);
                    await cmd.ExecuteNonQueryAsync();
                }

                foreach (var dep in request.Dependencies)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO BundleDependencies
                            (BundleName, Version, FromPath, ToPath)
                        VALUES
                            (@bundle, @version, @from, @to)
                        ON CONFLICT DO NOTHING;";
                    cmd.AddParam("@bundle", request.BundleName);
                    cmd.AddParam("@version", nextVersion);
                    cmd.AddParam("@from", NormalizeVirtualPath(dep.FromPath));
                    cmd.AddParam("@to", NormalizeVirtualPath(dep.ToPath));
                    await cmd.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            var versionInfo = new BundleVersionInfo(request.BundleName, nextVersion, NormalizeVirtualPath(request.EntryPath),
                request.ContentHash, publishedAt, request.Publisher, request.Description);
            var lineage = BuildBundleLineage(request, versionInfo);
            if (lineage.Count > 0)
            {
                await SaveLineageAsync(
                    lineage,
                    $"bundle:{versionInfo.BundleName}@{versionInfo.Version}",
                    $"orch://{versionInfo.BundleName}@{versionInfo.Version}/{versionInfo.EntryPath}",
                    versionInfo.PublishedAt);
            }

            return versionInfo;
        }

        private static IReadOnlyList<LineageEntry> BuildBundleLineage(BundlePublishRequest request, BundleVersionInfo version)
        {
            var entries = new List<LineageEntry>();
            foreach (var file in request.Files.Where(IsScriptFile))
            {
                var virtualPath = NormalizeVirtualPath(file.VirtualPath);
                try
                {
                    var tokens = new Lexer(file.Content).Tokenize();
                    var script = new Parser(tokens, file.Content).Parse();
                    var tracker = new LineageTracker(NullLogger.Instance);
                    tracker.GlobalMetadata["bundle"] = version.BundleName;
                    tracker.GlobalMetadata["bundle_version"] = version.Version.ToString();
                    tracker.GlobalMetadata["bundle_path"] = virtualPath;
                    new LineageAnalyzer(tracker).Analyze(script);

                    foreach (var entry in tracker.GetFullLineage())
                    {
                        entry.SourceFile = virtualPath;
                        entry.Metadata["bundle"] = version.BundleName;
                        entry.Metadata["bundle_version"] = version.Version.ToString();
                        entry.Metadata["bundle_path"] = virtualPath;
                        entries.Add(entry);
                    }
                }
                catch
                {
                    // Bundle content has already been accepted by the publish path. Lineage is best-effort.
                }
            }

            return entries;
        }

        private static bool IsScriptFile(BundlePublishFile file) =>
            file.ContentType.Equals("application/etlsql", StringComparison.OrdinalIgnoreCase)
            || file.ContentType.Equals("application/rptsql", StringComparison.OrdinalIgnoreCase)
            || file.VirtualPath.EndsWith(".etlsql", StringComparison.OrdinalIgnoreCase)
            || file.VirtualPath.EndsWith(".rptsql", StringComparison.OrdinalIgnoreCase);

        public async Task<BundleVersionInfo?> GetLatestVersionAsync(string bundleName)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT BundleName, Version, EntryPath, ContentHash, PublishedAt, Publisher, Description
                                FROM BundleVersions WHERE BundleName = @bundle COLLATE NOCASE
                                ORDER BY Version DESC LIMIT 1;";
            cmd.AddParam("@bundle", bundleName);
            using var reader = await cmd.ExecuteReaderAsync();
            return await reader.ReadAsync() ? ReadBundleVersion(reader) : null;
        }

        public async Task<BundleVersionInfo?> GetVersionAsync(string bundleName, int version)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT BundleName, Version, EntryPath, ContentHash, PublishedAt, Publisher, Description
                                FROM BundleVersions WHERE BundleName = @bundle COLLATE NOCASE AND Version = @version LIMIT 1;";
            cmd.AddParam("@bundle", bundleName);
            cmd.AddParam("@version", version);
            using var reader = await cmd.ExecuteReaderAsync();
            return await reader.ReadAsync() ? ReadBundleVersion(reader) : null;
        }

        public async Task<BundleFileInfo?> GetFileAsync(string bundleName, int version, string virtualPath)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT BundleName, Version, VirtualPath, Content, ContentHash, SizeBytes, ContentType
                                FROM BundleFiles
                                WHERE BundleName = @bundle COLLATE NOCASE AND Version = @version AND VirtualPath = @path COLLATE NOCASE
                                LIMIT 1;";
            cmd.AddParam("@bundle", bundleName);
            cmd.AddParam("@version", version);
            cmd.AddParam("@path", NormalizeVirtualPath(virtualPath));
            using var reader = await cmd.ExecuteReaderAsync();
            return await reader.ReadAsync() ? ReadBundleFile(reader) : null;
        }

        public async Task<IEnumerable<BundleVersionInfo>> GetBundlesAsync()
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT bv.BundleName, bv.Version, bv.EntryPath, bv.ContentHash, bv.PublishedAt, bv.Publisher, bv.Description
                                FROM BundleVersions bv
                                INNER JOIN (
                                    SELECT BundleName, MAX(Version) AS Version FROM BundleVersions GROUP BY BundleName
                                ) latest ON latest.BundleName = bv.BundleName AND latest.Version = bv.Version
                                ORDER BY bv.BundleName COLLATE NOCASE;";
            return await ReadBundleVersionsAsync(cmd);
        }

        public async Task<IEnumerable<BundleVersionInfo>> GetVersionsAsync(string bundleName)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT BundleName, Version, EntryPath, ContentHash, PublishedAt, Publisher, Description
                                FROM BundleVersions WHERE BundleName = @bundle COLLATE NOCASE
                                ORDER BY Version DESC;";
            cmd.AddParam("@bundle", bundleName);
            return await ReadBundleVersionsAsync(cmd);
        }

        public async Task<IEnumerable<BundleFileInfo>> GetFilesAsync(string bundleName, int version)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT BundleName, Version, VirtualPath, Content, ContentHash, SizeBytes, ContentType
                                FROM BundleFiles WHERE BundleName = @bundle COLLATE NOCASE AND Version = @version
                                ORDER BY VirtualPath COLLATE NOCASE;";
            cmd.AddParam("@bundle", bundleName);
            cmd.AddParam("@version", version);
            var files = new List<BundleFileInfo>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) files.Add(ReadBundleFile(reader));
            return files;
        }

        public async Task<IEnumerable<BundleDependencyInfo>> GetDependenciesAsync(string bundleName, int version)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT BundleName, Version, FromPath, ToPath
                                FROM BundleDependencies WHERE BundleName = @bundle COLLATE NOCASE AND Version = @version
                                ORDER BY FromPath COLLATE NOCASE, ToPath COLLATE NOCASE;";
            cmd.AddParam("@bundle", bundleName);
            cmd.AddParam("@version", version);
            var deps = new List<BundleDependencyInfo>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                deps.Add(new BundleDependencyInfo(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3)));
            return deps;
        }

        private static async Task<IEnumerable<BundleVersionInfo>> ReadBundleVersionsAsync(DbCommand cmd)
        {
            var versions = new List<BundleVersionInfo>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) versions.Add(ReadBundleVersion(reader));
            return versions;
        }

        private static BundleVersionInfo ReadBundleVersion(DbDataReader reader) => new(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.GetString(3),
            DateTime.Parse(reader.GetString(4)),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6));

        private static BundleFileInfo ReadBundleFile(DbDataReader reader) => new(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt64(5),
            reader.GetString(6));

        private static string NormalizeVirtualPath(string path)
            => path.Replace('\\', '/').TrimStart('/');

        // ── ILineageCatalogStore ──────────────────────────────────────────────

        public async Task SaveLineageAsync(IEnumerable<LineageEntry> entries, string? jobName, string? scriptPath, DateTime runAt)
        {
            await EnsureInitializedAsync();
            var runAtStr = runAt.ToString("O");
            var entriesList = entries.ToList();
            if (entriesList.Count == 0) return;

            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            try
            {
                foreach (var entry in entriesList)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO LineageHistory
                            (RunAt, JobName, ScriptPath, TargetTable, TargetColumn, SourceTables, SourceColumns, Operation, Tags, SourceFile, Line,
                             TransformationKind, TransformationExpression, FunctionsApplied, DerivedFromDescriptions)
                        VALUES
                            (@runAt, @job, @script, @target, @col, @sources, @srcCols, @op, @tags, @file, @line,
                             @tkind, @texpr, @fns, @derived);";
                    cmd.AddParam("@runAt", runAtStr);
                    cmd.AddParam("@job", (object?)jobName ?? DBNull.Value);
                    cmd.AddParam("@script", (object?)scriptPath ?? DBNull.Value);
                    cmd.AddParam("@target", entry.TargetTable);
                    cmd.AddParam("@col", (object?)entry.TargetColumn ?? DBNull.Value);
                    cmd.AddParam("@sources", JsonSerializer.Serialize(entry.SourceTables));
                    cmd.AddParam("@srcCols", JsonSerializer.Serialize(entry.SourceColumns));
                    cmd.AddParam("@op", entry.Operation);
                    cmd.AddParam("@tags", JsonSerializer.Serialize(entry.Metadata));
                    cmd.AddParam("@file", (object?)entry.SourceFile ?? DBNull.Value);
                    cmd.AddParam("@line", entry.Line);
                    cmd.AddParam("@tkind", entry.TransformationKind == ETL_SQL.Core.TransformationKind.Unknown ? (object)DBNull.Value : entry.TransformationKind.ToString());
                    cmd.AddParam("@texpr", (object?)entry.TransformationExpression ?? DBNull.Value);
                    cmd.AddParam("@fns", JsonSerializer.Serialize(entry.FunctionsApplied ?? (IReadOnlyList<string>)System.Array.Empty<string>()));
                    cmd.AddParam("@derived", (object?)entry.DerivedFromDescriptions ?? DBNull.Value);
                    await cmd.ExecuteNonQueryAsync();
                }
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTableAsync(string tableName, int limit = 100)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, RunAt, JobName, ScriptPath, TargetTable, TargetColumn,
                       SourceTables, Operation, Tags, SourceFile, Line,
                       SourceColumns, TransformationKind, TransformationExpression, FunctionsApplied, DerivedFromDescriptions
                FROM LineageHistory
                WHERE TargetTable = @table COLLATE NOCASE
                ORDER BY RunAt DESC, Id DESC
                LIMIT @limit;";
            cmd.AddParam("@table", tableName);
            cmd.AddParam("@limit", limit);
            return await ReadLineageHistoryAsync(cmd);
        }

        public async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTablesAsync(
            IReadOnlyCollection<string> tableNames, int limitPerTable = 100)
        {
            if (tableNames.Count == 0) return Array.Empty<LineageHistoryEntry>();

            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();

            // One round-trip for the whole set: a per-table ROW_NUMBER window applies the limit
            // independently to each table (a plain LIMIT would cap the combined result instead).
            var paramNames = new List<string>(tableNames.Count);
            var i = 0;
            foreach (var name in tableNames)
            {
                var p = "@t" + i++;
                paramNames.Add(p);
                cmd.AddParam(p, name);
            }
            cmd.AddParam("@limit", limitPerTable);
            cmd.CommandText = $@"
                SELECT Id, RunAt, JobName, ScriptPath, TargetTable, TargetColumn,
                       SourceTables, Operation, Tags, SourceFile, Line,
                       SourceColumns, TransformationKind, TransformationExpression, FunctionsApplied, DerivedFromDescriptions
                FROM (
                    SELECT *, ROW_NUMBER() OVER (
                        PARTITION BY TargetTable COLLATE NOCASE
                        ORDER BY RunAt DESC, Id DESC) AS _rn
                    FROM LineageHistory
                    WHERE TargetTable COLLATE NOCASE IN ({string.Join(", ", paramNames)})
                )
                WHERE _rn <= @limit
                ORDER BY RunAt DESC, Id DESC;";
            return await ReadLineageHistoryAsync(cmd);
        }

        public async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTagAsync(string tagKey, string? tagValue = null, int limit = 100)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            // Tags is stored as a JSON object. Use LIKE to find the key; refine with value if provided.
            var pattern = tagValue == null
                ? $"%\"{tagKey}\"%"
                : $"%\"{tagKey}\":\"{tagValue}\"%";
            cmd.CommandText = @"
                SELECT Id, RunAt, JobName, ScriptPath, TargetTable, TargetColumn,
                       SourceTables, Operation, Tags, SourceFile, Line,
                       SourceColumns, TransformationKind, TransformationExpression, FunctionsApplied, DerivedFromDescriptions
                FROM LineageHistory
                WHERE Tags LIKE @pattern
                ORDER BY RunAt DESC, Id DESC
                LIMIT @limit;";
            cmd.AddParam("@pattern", pattern);
            cmd.AddParam("@limit", limit);
            return await ReadLineageHistoryAsync(cmd);
        }

        public async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForJobAsync(string jobName, int limit = 100)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, RunAt, JobName, ScriptPath, TargetTable, TargetColumn,
                       SourceTables, Operation, Tags, SourceFile, Line,
                       SourceColumns, TransformationKind, TransformationExpression, FunctionsApplied, DerivedFromDescriptions
                FROM LineageHistory
                WHERE JobName = @jobName COLLATE NOCASE
                ORDER BY RunAt DESC, Id DESC
                LIMIT @limit;";
            cmd.AddParam("@jobName", jobName);
            cmd.AddParam("@limit", limit);
            return await ReadLineageHistoryAsync(cmd);
        }

        public async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForSourceAsync(string sourceName, int limit = 100)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, RunAt, JobName, ScriptPath, TargetTable, TargetColumn,
                       SourceTables, Operation, Tags, SourceFile, Line,
                       SourceColumns, TransformationKind, TransformationExpression, FunctionsApplied, DerivedFromDescriptions
                FROM LineageHistory
                WHERE SourceTables LIKE @pattern
                ORDER BY RunAt DESC, Id DESC
                LIMIT @scanLimit;";
            cmd.AddParam("@pattern", $"%\"{sourceName}\"%");
            cmd.AddParam("@scanLimit", Math.Max(limit * 5, limit));

            return (await ReadLineageHistoryAsync(cmd))
                .Where(e => e.SourceTables.Any(s => string.Equals(s, sourceName, StringComparison.OrdinalIgnoreCase)))
                .Take(limit)
                .ToList();
        }

        public async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForSourceFileAsync(string sourceFile, int limit = 100)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, RunAt, JobName, ScriptPath, TargetTable, TargetColumn,
                       SourceTables, Operation, Tags, SourceFile, Line,
                       SourceColumns, TransformationKind, TransformationExpression, FunctionsApplied, DerivedFromDescriptions
                FROM LineageHistory
                WHERE SourceFile = @sourceFile COLLATE NOCASE
                   OR ScriptPath = @sourceFile COLLATE NOCASE
                ORDER BY RunAt DESC, Id DESC
                LIMIT @limit;";
            cmd.AddParam("@sourceFile", sourceFile);
            cmd.AddParam("@limit", limit);
            return await ReadLineageHistoryAsync(cmd);
        }

        private static async Task<IEnumerable<LineageHistoryEntry>> ReadLineageHistoryAsync(DbCommand cmd)
        {
            var results = new List<LineageHistoryEntry>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var sourceTables = JsonSerializer.Deserialize<List<string>>(reader.GetString(6)) ?? new List<string>();
                var tags = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(8)) ?? new Dictionary<string, string>();
                var sourceColumns = reader.IsDBNull(11) ? new List<string>() : (JsonSerializer.Deserialize<List<string>>(reader.GetString(11)) ?? new List<string>());
                var functions = reader.IsDBNull(14) ? new List<string>() : (JsonSerializer.Deserialize<List<string>>(reader.GetString(14)) ?? new List<string>());
                results.Add(new LineageHistoryEntry(
                    reader.GetInt64(0),
                    DateTime.Parse(reader.GetString(1)),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    sourceTables,
                    reader.GetString(7),
                    tags,
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
                    sourceColumns,
                    reader.IsDBNull(12) ? null : reader.GetString(12),
                    reader.IsDBNull(13) ? null : reader.GetString(13),
                    functions,
                    reader.IsDBNull(15) ? null : reader.GetString(15)
                ));
            }
            return results;
        }
    }

    /// <summary>
    /// SQLite entry point for the Orchestrator store — the default, fully-supported standalone backend.
    /// A thin subclass over <see cref="RelationalJobHistoryStore"/> that wires the SQLite dialect, so
    /// every existing <c>new SQLiteJobHistoryStore(path)</c> call site keeps working unchanged.
    /// </summary>
    public sealed class SQLiteJobHistoryStore : RelationalJobHistoryStore
    {
        public SQLiteJobHistoryStore(string? dbPath = null)
            : base(new SqliteOrchestratorDialect($"Data Source={dbPath ?? DefaultDbPath()}"))
        {
        }

        /// <summary>
        /// Canonical global DB path in LocalApplicationData so all instances on the same machine share
        /// the same job history regardless of their working directory.
        /// </summary>
        public static string DefaultDbPath()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ETL-SQL");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "etlsql.db");
        }
    }
}
