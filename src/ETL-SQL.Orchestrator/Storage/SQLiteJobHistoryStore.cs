using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Quality;

namespace ETL_SQL.Orchestrator.Storage
{
    /// <summary>
    /// Relational (provider-neutral) job history / bundle / lineage store. The connection, schema DDL,
    /// and the few non-portable SQL constructs come from an <see cref="IOrchestratorStoreDialect"/>, so
    /// the same logic runs on SQLite (default) and PostgreSQL (Practical HA). The SQLite entry point is
    /// <see cref="SQLiteJobHistoryStore"/>.
    /// </summary>
    public partial class RelationalJobHistoryStore : IJobHistoryStore, ITenantJobEvidenceStore, IJobScheduleQueryStore, IJobCatalogStore, IOrchestratorAuthorizationStore, IBundleStore, ILineageCatalogStore, ITenantLineageCatalogStore, INodeRegistryStore, IWriteEpochStore, IClusterLockStore, IHostMetricsStore, ISharedTenantLifecycleStore
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
                    // Identity is the surrogate Id; Name is the addressable key and is unique *per
                    // tenant*, case-insensitive. Two tenants may each own a job called 'daily_load',
                    // and everything that references a job — ACLs, schedule and notification links,
                    // history, state, metrics — references the Id, so a re-created name never inherits
                    // the previous object's grants or state.
                    //
                    // TenantId is NOT NULL with an empty-string sentinel meaning "unbound": a Solo or
                    // otherwise host-fixed deployment that never received a signed tenant. Empty is
                    // never a valid TenantId (TenantId rejects it), so the sentinel cannot collide with
                    // a real tenant, and it keeps unbound objects in one uniqueness namespace instead of
                    // the "every NULL is distinct" behaviour a nullable column would give. The domain
                    // record keeps TenantId nullable so the unbound state stays visible to callers —
                    // notably sandbox policy resolution, which must refuse an unbound job.
                    //
                    // COLLATE NOCASE is placed before the constraints because PostgreSQL requires that
                    // order; SQLite accepts either.
                    var createJobsTable = @"
                CREATE TABLE IF NOT EXISTS Jobs (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Name TEXT COLLATE NOCASE NOT NULL,
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
                    LeaseFenceToken INTEGER NOT NULL DEFAULT 0,
                    JobType TEXT NOT NULL DEFAULT 'Script',
                    TargetPath TEXT,
                    DisplayName TEXT,
                    Description TEXT,
                    Options TEXT,
                    CreatedBy TEXT,
                    ModifiedBy TEXT,
                    TenantId TEXT NOT NULL DEFAULT '',
                    UNIQUE (TenantId, Name)
                );";

                    // Schedules, notifications, and their attachments to jobs. The Orchestrator is the
                    // system of record for all three: it runs the jobs, so it holds the trigger,
                    // computes the next run, and dispatches the outcome.
                    var createCatalogTables = @"
                CREATE TABLE IF NOT EXISTS Schedules (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Name TEXT COLLATE NOCASE NOT NULL,
                    Cron TEXT NOT NULL,
                    TimeZone TEXT NOT NULL DEFAULT 'UTC',
                    IsEnabled INTEGER NOT NULL DEFAULT 1,
                    DisplayName TEXT,
                    Description TEXT,
                    Options TEXT,
                    CreatedBy TEXT,
                    ModifiedBy TEXT,
                    Version INTEGER NOT NULL DEFAULT 1,
                    TenantId TEXT NOT NULL DEFAULT '',
                    UNIQUE (TenantId, Name)
                );
                CREATE TABLE IF NOT EXISTS Notifications (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Name TEXT COLLATE NOCASE NOT NULL,
                    ConnectionName TEXT NOT NULL,
                    Recipient TEXT,
                    IsEnabled INTEGER NOT NULL DEFAULT 1,
                    DisplayName TEXT,
                    Description TEXT,
                    Options TEXT,
                    CreatedBy TEXT,
                    ModifiedBy TEXT,
                    Version INTEGER NOT NULL DEFAULT 1,
                    TenantId TEXT NOT NULL DEFAULT '',
                    UNIQUE (TenantId, Name)
                );
                CREATE TABLE IF NOT EXISTS JobSchedules (
                    JobId TEXT NOT NULL,
                    ScheduleId TEXT NOT NULL,
                    LastRun TEXT,
                    NextRun TEXT,
                    PRIMARY KEY (JobId, ScheduleId)
                );
                CREATE INDEX IF NOT EXISTS idx_js_schedule ON JobSchedules(ScheduleId);
                CREATE TABLE IF NOT EXISTS JobNotifications (
                    JobId TEXT NOT NULL,
                    NotificationId TEXT NOT NULL,
                    TriggerCondition TEXT NOT NULL,
                    PRIMARY KEY (JobId, NotificationId, TriggerCondition)
                );
                CREATE INDEX IF NOT EXISTS idx_jn_notification ON JobNotifications(NotificationId);";

                    // Grants hang off the object's surrogate Id, which is what makes them tenant-safe:
                    // resolving a name to an Id already required the caller's tenant, so a grant can
                    // never be read across a tenant boundary, and dropping an object retires its Id so
                    // a later object of the same name starts with no grants at all. ObjectKind is
                    // retained for audit and administration listings, not for lookup.
                    var createObjectAclTable = @"
                CREATE TABLE IF NOT EXISTS OrchestratorObjectAcls (
                    ObjectId TEXT NOT NULL,
                    ObjectKind TEXT NOT NULL,
                    PrincipalKind TEXT NOT NULL,
                    PrincipalId TEXT NOT NULL,
                    Permission TEXT NOT NULL,
                    GrantedBy TEXT NOT NULL,
                    Version INTEGER NOT NULL DEFAULT 1,
                    PRIMARY KEY (ObjectId, PrincipalKind, PrincipalId)
                );
                CREATE INDEX IF NOT EXISTS idx_ooa_principal
                    ON OrchestratorObjectAcls(PrincipalKind, PrincipalId, ObjectKind);";

                    // JobId scopes the row; JobName is retained denormalized so history stays readable
                    // after its job is dropped, and so a run's name at the time it ran survives a later
                    // object of the same name. TenantId is carried directly rather than joined through
                    // Jobs because retention prunes history long after the job may be gone.
                    var createHistoryTable = @"
                CREATE TABLE IF NOT EXISTS JobHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    JobId TEXT NOT NULL DEFAULT '',
                    TenantId TEXT NOT NULL DEFAULT '',
                    JobName TEXT NOT NULL,
                    StartTime TEXT NOT NULL,
                    EndTime TEXT,
                    Status TEXT NOT NULL,
                    ErrorMessage TEXT,
                    RowsProcessed INTEGER DEFAULT 0
                );";
                    // The index over (TenantId, JobId) is created after the column migrations rather
                    // than here: CREATE TABLE IF NOT EXISTS leaves an existing table alone, so on a
                    // database written by an earlier build those two columns do not exist yet and
                    // indexing them fails before the migration that adds them has run.

                    var createColumnMetricsTable = @"
                CREATE TABLE IF NOT EXISTS JobColumnMetrics (
                    JobHistoryId INTEGER NOT NULL,
                    TenantId TEXT NOT NULL DEFAULT '',
                    TargetTable TEXT NOT NULL DEFAULT '',
                    ColumnName TEXT NOT NULL,
                    TotalRows INTEGER NOT NULL,
                    NullRows INTEGER NOT NULL,
                    MaxTimestampUtc TEXT,
                    PRIMARY KEY (JobHistoryId, TargetTable, ColumnName)
                );";

                    var createDataQualityFailuresTable = @"
                CREATE TABLE IF NOT EXISTS JobDataQualityFailures (
                    JobHistoryId INTEGER NOT NULL,
                    TenantId TEXT NOT NULL DEFAULT '',
                    TargetTable TEXT NOT NULL DEFAULT '',
                    ColumnName TEXT NOT NULL,
                    RuleText TEXT NOT NULL,
                    Action TEXT NOT NULL,
                    FailureCount INTEGER NOT NULL,
                    Owner TEXT,
                    PRIMARY KEY (JobHistoryId, TargetTable, ColumnName, RuleText, Action)
                );
                CREATE INDEX IF NOT EXISTS idx_dqf_history ON JobDataQualityFailures(JobHistoryId);";

                    var createStatementMetricsTable = @"
                CREATE TABLE IF NOT EXISTS JobStatementMetrics (
                    JobHistoryId INTEGER NOT NULL,
                    TenantId TEXT NOT NULL DEFAULT '',
                    Ordinal INTEGER NOT NULL,
                    Statement TEXT NOT NULL,
                    DurationMs INTEGER NOT NULL,
                    RowsProcessed INTEGER NOT NULL,
                    CpuTimeMs INTEGER NOT NULL,
                    SpilledBytes INTEGER NOT NULL,
                    SpillReadBytes INTEGER NOT NULL,
                    Partitions INTEGER NOT NULL,
                    QueueWaitMs INTEGER NOT NULL,
                    LockWaitMs INTEGER NOT NULL,
                    IndexUsed TEXT,
                    DqRowsValidated INTEGER NOT NULL,
                    DqRowsQuarantined INTEGER NOT NULL,
                    DqRowsWarned INTEGER NOT NULL,
                    DqValidationMs INTEGER NOT NULL,
                    Failed INTEGER NOT NULL,
                    PRIMARY KEY (JobHistoryId, Ordinal)
                );
                CREATE INDEX IF NOT EXISTS idx_jsm_history ON JobStatementMetrics(JobHistoryId);";

                    var createTenantUsageTable = @"
                CREATE TABLE IF NOT EXISTS TenantUsageRecords (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    TenantId TEXT NOT NULL,
                    JobHistoryId INTEGER NOT NULL,
                    WorkloadKind TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    RowsProcessed INTEGER NOT NULL,
                    PeakMemoryBytes INTEGER NOT NULL,
                    CpuTimeSeconds REAL NOT NULL,
                    DurationMs INTEGER NOT NULL,
                    RecordedAtUtc TEXT NOT NULL,
                    UNIQUE (TenantId, JobHistoryId)
                );
                CREATE INDEX IF NOT EXISTS idx_tur_tenant_time
                    ON TenantUsageRecords(TenantId, RecordedAtUtc DESC);";

                    var createSharedTenantLifecycleTables = @"
                CREATE TABLE IF NOT EXISTS SharedTenantControlPlanes (
                    TenantId TEXT PRIMARY KEY,
                    State TEXT NOT NULL,
                    ActiveRelease TEXT NOT NULL,
                    MaxConcurrentJobs INTEGER NOT NULL,
                    MaxStorageMb INTEGER NOT NULL,
                    MaxReportSessions INTEGER NOT NULL,
                    FenceEpoch INTEGER NOT NULL DEFAULT 1,
                    CreatedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL,
                    DeletedAtUtc TEXT,
                    Version INTEGER NOT NULL DEFAULT 1
                );
                CREATE INDEX IF NOT EXISTS idx_stcp_state
                    ON SharedTenantControlPlanes(State);
                CREATE TABLE IF NOT EXISTS SharedTenantLifecycleOperations (
                    OperationId TEXT PRIMARY KEY,
                    TenantId TEXT NOT NULL,
                    Kind TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    PlatformOperator TEXT NOT NULL,
                    AuthorizationReference TEXT NOT NULL,
                    TargetRelease TEXT,
                    TargetMaxConcurrentJobs INTEGER,
                    TargetMaxStorageMb INTEGER,
                    TargetMaxReportSessions INTEGER,
                    StartedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL,
                    CompletedAtUtc TEXT,
                    UNIQUE (Kind, AuthorizationReference)
                );
                CREATE INDEX IF NOT EXISTS idx_stlo_tenant_status
                    ON SharedTenantLifecycleOperations(TenantId, Status);
                CREATE TABLE IF NOT EXISTS SharedTenantLifecycleFencedJobs (
                    OperationId TEXT NOT NULL,
                    TenantId TEXT NOT NULL,
                    JobName TEXT COLLATE NOCASE NOT NULL,
                    PRIMARY KEY (OperationId, JobName)
                );";

                    // Bundle identity is (TenantId, BundleName, Version). Without the tenant, one
                    // tenant's publish becomes another tenant's `bundle://` resolution and its pinned
                    // latest version — a supply-chain crossing, not merely a disclosure.
                    var createBundleTables = @"
                CREATE TABLE IF NOT EXISTS BundleVersions (
                    TenantId TEXT NOT NULL DEFAULT '',
                    BundleName TEXT NOT NULL,
                    Version INTEGER NOT NULL,
                    EntryPath TEXT NOT NULL,
                    ContentHash TEXT NOT NULL,
                    PublishedAt TEXT NOT NULL,
                    Publisher TEXT,
                    Description TEXT,
                    EncryptionMode TEXT NOT NULL DEFAULT 'MACHINE',
                    EncryptionMetadata TEXT,
                    PRIMARY KEY (TenantId, BundleName, Version)
                );

                CREATE TABLE IF NOT EXISTS BundleFiles (
                    TenantId TEXT NOT NULL DEFAULT '',
                    BundleName TEXT NOT NULL,
                    Version INTEGER NOT NULL,
                    VirtualPath TEXT NOT NULL,
                    Content TEXT NOT NULL,
                    ContentHash TEXT NOT NULL,
                    SizeBytes INTEGER NOT NULL,
                    ContentType TEXT NOT NULL,
                    PRIMARY KEY (TenantId, BundleName, Version, VirtualPath),
                    FOREIGN KEY (TenantId, BundleName, Version) REFERENCES BundleVersions(TenantId, BundleName, Version)
                );

                CREATE TABLE IF NOT EXISTS BundleDependencies (
                    TenantId TEXT NOT NULL DEFAULT '',
                    BundleName TEXT NOT NULL,
                    Version INTEGER NOT NULL,
                    FromPath TEXT NOT NULL,
                    ToPath TEXT NOT NULL,
                    PRIMARY KEY (TenantId, BundleName, Version, FromPath, ToPath),
                    FOREIGN KEY (TenantId, BundleName, Version) REFERENCES BundleVersions(TenantId, BundleName, Version)
                );";

                    var createLineageHistoryTable = @"
                CREATE TABLE IF NOT EXISTS LineageHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    TenantId TEXT NOT NULL DEFAULT 'portal-host',
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
                CREATE INDEX IF NOT EXISTS idx_jh_start ON JobHistory(StartTime);
                CREATE INDEX IF NOT EXISTS idx_jh_end ON JobHistory(EndTime);";

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

                    // Watermarks live here. Keyed on the job's surrogate Id rather than its name: two
                    // tenants running a job of the same name sharing one high-water mark would silently
                    // load the wrong rows, which nothing would report.
                    var createJobStateTable = @"
                CREATE TABLE IF NOT EXISTS JobState (
                    JobId TEXT NOT NULL,
                    StateKey TEXT NOT NULL,
                    StateValue TEXT,
                    UpdatedAt TEXT NOT NULL,
                    PRIMARY KEY (JobId, StateKey)
                );";

                    // Host-utilization time series (capacity planning): one row per sample per node.
                    //
                    // These are host gauges, so they carry the node's tenant and capacity-pool *binding*
                    // rather than a per-sample tenant: on a Shared node running several tenants' work,
                    // MemoryLoadPercent does not decompose by tenant, and a TenantId column here would
                    // be a number that looks meterable and is not. A Dedicated host is fixed to exactly
                    // one tenant and pool, so its capacity attributes cleanly; a Shared host records the
                    // unbound sentinel and is honestly marked shared. Per-tenant metering comes from
                    // TenantUsageRecords, which measures the run rather than the host.
                    var createHostMetricsTable = @"
                CREATE TABLE IF NOT EXISTS HostMetrics (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    NodeId TEXT NOT NULL,
                    NodeTenantId TEXT NOT NULL DEFAULT '',
                    CapacityPool TEXT NOT NULL DEFAULT '',
                    CapturedAt TEXT NOT NULL,
                    MemoryLoadPercent REAL NOT NULL DEFAULT 0,
                    ProcessCpuPercent REAL NOT NULL DEFAULT 0,
                    HostCpuPercent REAL,
                    StateDiskFreeBytes INTEGER NOT NULL DEFAULT 0,
                    SpillDiskFreeBytes INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS idx_hm_node_time ON HostMetrics(NodeId, CapturedAt);";
                    // The tenant index waits for the migration that adds NodeTenantId — see the note
                    // on the JobHistory index above.

                    // Daily roll-up tables (capacity trend that survives raw pruning). Day is 'yyyy-MM-dd'.
                    var createRollupTables = @"
                CREATE TABLE IF NOT EXISTS JobHistoryDaily (
                    Day TEXT NOT NULL,
                    JobId TEXT NOT NULL,
                    TenantId TEXT NOT NULL DEFAULT '',
                    JobName TEXT NOT NULL,
                    RunCount INTEGER NOT NULL DEFAULT 0,
                    FailureCount INTEGER NOT NULL DEFAULT 0,
                    TotalRows INTEGER NOT NULL DEFAULT 0,
                    MaxPeakMemoryBytes INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (Day, JobId)
                );
                CREATE TABLE IF NOT EXISTS HostMetricsDaily (
                    Day TEXT NOT NULL,
                    NodeId TEXT NOT NULL,
                    NodeTenantId TEXT NOT NULL DEFAULT '',
                    CapacityPool TEXT NOT NULL DEFAULT '',
                    AvgMemoryLoadPercent REAL NOT NULL DEFAULT 0,
                    MaxMemoryLoadPercent REAL NOT NULL DEFAULT 0,
                    AvgCpuPercent REAL NOT NULL DEFAULT 0,
                    MaxCpuPercent REAL NOT NULL DEFAULT 0,
                    MinStateDiskFreeBytes INTEGER NOT NULL DEFAULT 0,
                    MinSpillDiskFreeBytes INTEGER NOT NULL DEFAULT 0,
                    AvgHostCpuPercent REAL,
                    MaxHostCpuPercent REAL,
                    PRIMARY KEY (Day, NodeId)
                );";

                    var schema = createJobsTable + createCatalogTables + createObjectAclTable + createHistoryTable + createColumnMetricsTable
                        + createDataQualityFailuresTable + createStatementMetricsTable + createBundleTables
                        + createLineageHistoryTable + createNodesTable + createWriteEpochsTable + createClusterLocksTable + createJobStateTable
                        + createHostMetricsTable + createRollupTables + createTenantUsageTable
                        + createSharedTenantLifecycleTables;
                    // SQLite's auto-increment PK literal is the default; the dialect rewrites it for other
                    // providers (e.g. PostgreSQL identity columns). CollationDdl (if any) runs first so the
                    // COLLATE NOCASE indexes/queries resolve.
                    schema = schema.Replace("INTEGER PRIMARY KEY AUTOINCREMENT", _dialect.AutoIncrementPrimaryKey);
                    schema = schema
                        .Replace("RowsProcessed INTEGER", $"RowsProcessed {_dialect.Int64Type}")
                        .Replace("TotalRows INTEGER", $"TotalRows {_dialect.Int64Type}")
                        .Replace("NullRows INTEGER", $"NullRows {_dialect.Int64Type}")
                        .Replace("FailureCount INTEGER", $"FailureCount {_dialect.Int64Type}")
                        .Replace("MaxPeakMemoryBytes INTEGER", $"MaxPeakMemoryBytes {_dialect.Int64Type}")
                        .Replace("StateDiskFreeBytes INTEGER", $"StateDiskFreeBytes {_dialect.Int64Type}")
                        .Replace("SpillDiskFreeBytes INTEGER", $"SpillDiskFreeBytes {_dialect.Int64Type}")
                        .Replace("MinStateDiskFreeBytes INTEGER", $"MinStateDiskFreeBytes {_dialect.Int64Type}")
                        .Replace("MinSpillDiskFreeBytes INTEGER", $"MinSpillDiskFreeBytes {_dialect.Int64Type}")
                        // Statement metrics: durations and byte counters must be 64-bit on
                        // PostgreSQL too, where INTEGER is 32-bit and a spill byte count overflows.
                        .Replace("DurationMs INTEGER", $"DurationMs {_dialect.Int64Type}")
                        .Replace("CpuTimeMs INTEGER", $"CpuTimeMs {_dialect.Int64Type}")
                        .Replace("SpilledBytes INTEGER", $"SpilledBytes {_dialect.Int64Type}")
                        .Replace("SpillReadBytes INTEGER", $"SpillReadBytes {_dialect.Int64Type}")
                        .Replace("QueueWaitMs INTEGER", $"QueueWaitMs {_dialect.Int64Type}")
                        .Replace("LockWaitMs INTEGER", $"LockWaitMs {_dialect.Int64Type}")
                        .Replace("DqRowsValidated INTEGER", $"DqRowsValidated {_dialect.Int64Type}")
                        .Replace("DqRowsQuarantined INTEGER", $"DqRowsQuarantined {_dialect.Int64Type}")
                        .Replace("DqRowsWarned INTEGER", $"DqRowsWarned {_dialect.Int64Type}")
                        .Replace("DqValidationMs INTEGER", $"DqValidationMs {_dialect.Int64Type}")
                        .Replace("PeakMemoryBytes INTEGER", $"PeakMemoryBytes {_dialect.Int64Type}");

                    await EnsureCollationExistsAsync(connection);

                    using var command = connection.CreateCommand();
                    command.CommandText = schema;
                    await command.ExecuteNonQueryAsync();

                    // 8B-2: Schema migration — add resource tracking columns if missing
                    await EnsureHistoryColumnsExist(connection);
                    await EnsureColumnMetricColumnsExist(connection);
                    await EnsureJobColumnsExist(connection);
                    await EnsureLineageHistoryColumnsExist(connection);
                    await EnsureHostMetricsDailyColumnsExist(connection);
                    await EnsureHostMetricsColumnsExist(connection);

                    // Indexes over migrated columns, once those columns are guaranteed to exist.
                    await ExecuteOptionalSqlAsync(
                        connection,
                        "CREATE INDEX IF NOT EXISTS idx_jh_tenant_job ON JobHistory(TenantId, JobId, StartTime);");
                    await ExecuteOptionalSqlAsync(
                        connection,
                        "CREATE INDEX IF NOT EXISTS idx_hm_tenant_time ON HostMetrics(NodeTenantId, CapturedAt);");
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

        private static bool IsDuplicateColumnRace(DbException ex)
        {
            // PostgreSQL reports duplicate_column. SQLite exposes no provider-neutral SQL state,
            // so constrain its fallback to the provider type and exact diagnostic category.
            var sqlState = ex.GetType().GetProperty("SqlState")?.GetValue(ex) as string;
            if (string.Equals(sqlState, "42701", StringComparison.Ordinal))
                return true;

            return string.Equals(
                       ex.GetType().FullName,
                       "Microsoft.Data.Sqlite.SqliteException",
                       StringComparison.Ordinal)
                   && ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task AddColumnIfMissingAsync(
            DbConnection connection,
            ISet<string> knownColumns,
            string table,
            string column,
            string definition)
        {
            if (knownColumns.Contains(column))
                return;

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"ALTER TABLE {table} ADD COLUMN {definition};";
                await command.ExecuteNonQueryAsync();
            }
            catch (DbException ex) when (IsDuplicateColumnRace(ex))
            {
                // Another process or store instance won the additive migration after this
                // instance read its column snapshot. The desired schema is already present.
            }

            knownColumns.Add(column);
        }

        private async Task EnsureJobColumnsExist(DbConnection connection)
        {
            var columns = await _dialect.GetColumnNamesAsync(connection, "Jobs");

            // A Jobs table with no Id predates surrogate object identity, when a job's name was its
            // primary key. That cannot be migrated by adding columns: the identity has to become the
            // key and the name has to become unique per tenant instead of globally. Refuse here, with
            // the remedy, rather than letting it surface later as "no such column: j.Id" from
            // whichever query happened to run first.
            if (columns.Count > 0 && !columns.Contains("Id"))
                throw new InvalidOperationException(
                    "This orchestrator database predates surrogate job identity and cannot be " +
                    "upgraded in place. No release shipped with data in this schema, so the remedy " +
                    "is to delete the database file and let it be recreated.");

            await AddColumnIfMissingAsync(connection, columns, "Jobs", "MaxRetries", "MaxRetries INTEGER NOT NULL DEFAULT 0");
            await AddColumnIfMissingAsync(connection, columns, "Jobs", "RetryDelaySeconds", "RetryDelaySeconds INTEGER NOT NULL DEFAULT 30");
            await AddColumnIfMissingAsync(connection, columns, "Jobs", "ScriptHash", "ScriptHash TEXT");
            await AddColumnIfMissingAsync(connection, columns, "Jobs", "HashPolicy", "HashPolicy TEXT NOT NULL DEFAULT 'Warn'");
            await AddColumnIfMissingAsync(connection, columns, "Jobs", "LeaseOwner", "LeaseOwner TEXT");
            await AddColumnIfMissingAsync(connection, columns, "Jobs", "LeaseExpiresAt", "LeaseExpiresAt TEXT");
            await AddColumnIfMissingAsync(connection, columns, "Jobs", "LeaseFenceToken", "LeaseFenceToken INTEGER NOT NULL DEFAULT 0");
            await AddColumnIfMissingAsync(connection, columns, "Jobs", "Version", "Version INTEGER NOT NULL DEFAULT 1");

            // Unified job/schedule/notification model. A job now names what it acts on rather than
            // carrying an inline script body, and carries the human-facing label that lets its Name
            // stay a stable machine identity.
            await AddColumnIfMissingAsync(connection, columns, "Jobs", "JobType", "JobType TEXT NOT NULL DEFAULT 'Script'");

            foreach (var column in new[] { "TargetPath", "DisplayName", "Description", "Options", "CreatedBy", "ModifiedBy", "TenantId" })
                await AddColumnIfMissingAsync(connection, columns, "Jobs", column, $"{column} TEXT");
        }

        private async Task EnsureHostMetricsColumnsExist(DbConnection connection)
        {
            // The node's tenant and capacity-pool binding, added after the table first shipped. These
            // describe which tenant a *node* is dedicated to, not which tenant a sample belongs to —
            // a Shared node's gauges do not decompose by tenant, and an empty binding says exactly
            // that rather than misattributing capacity.
            var columns = await _dialect.GetColumnNamesAsync(connection, "HostMetrics");
            await AddColumnIfMissingAsync(
                connection, columns, "HostMetrics", "NodeTenantId", "NodeTenantId TEXT NOT NULL DEFAULT ''");
            await AddColumnIfMissingAsync(
                connection, columns, "HostMetrics", "CapacityPool", "CapacityPool TEXT NOT NULL DEFAULT ''");
        }

        private async Task EnsureHostMetricsDailyColumnsExist(DbConnection connection)
        {
            // Whole-host CPU joined the daily roll-up after the table first shipped; upgrade
            // existing databases in place (nullable REAL — no backfill possible for pruned raw rows).
            var columns = await _dialect.GetColumnNamesAsync(connection, "HostMetricsDaily");

            await AddColumnIfMissingAsync(connection, columns, "HostMetricsDaily", "AvgHostCpuPercent", "AvgHostCpuPercent REAL");
            await AddColumnIfMissingAsync(connection, columns, "HostMetricsDaily", "MaxHostCpuPercent", "MaxHostCpuPercent REAL");
        }

        private async Task EnsureHistoryColumnsExist(DbConnection connection)
        {
            var columns = await _dialect.GetColumnNamesAsync(connection, "JobHistory");

            // Identity and tenant, matching the CREATE defaults. The empty default is meaningful in
            // both: '' is an ad-hoc run with no job, and '' is the unbound (Solo) tenant. Rows in a
            // table written by an earlier build of this schema are exactly that — unbound runs whose
            // job binding was never recorded — so the default states the truth rather than guessing.
            await AddColumnIfMissingAsync(connection, columns, "JobHistory", "JobId", "JobId TEXT NOT NULL DEFAULT ''");
            await AddColumnIfMissingAsync(connection, columns, "JobHistory", "TenantId", "TenantId TEXT NOT NULL DEFAULT ''");

            await AddColumnIfMissingAsync(connection, columns, "JobHistory", "PeakMemoryBytes", "PeakMemoryBytes INTEGER DEFAULT 0");
            await AddColumnIfMissingAsync(connection, columns, "JobHistory", "CpuTimeSeconds", "CpuTimeSeconds REAL DEFAULT 0");
            await AddColumnIfMissingAsync(connection, columns, "JobHistory", "ScriptHashAtRunTime", "ScriptHashAtRunTime TEXT");
            await AddColumnIfMissingAsync(connection, columns, "JobHistory", "HashMatched", "HashMatched INTEGER");

            // Data-quality outcomes per run (rolling-expand safe: additive, defaulted).
            await AddColumnIfMissingAsync(connection, columns, "JobHistory", "RowsQuarantined", "RowsQuarantined INTEGER DEFAULT 0");
            await AddColumnIfMissingAsync(connection, columns, "JobHistory", "RowsWarned", "RowsWarned INTEGER DEFAULT 0");

            // Compact "column:rule=count;..." payload — counts only, never sample values.
            await AddColumnIfMissingAsync(connection, columns, "JobHistory", "DataQualityFailures", "DataQualityFailures TEXT");
            await AddColumnIfMissingAsync(connection, columns, "JobHistory", "SessionId", "SessionId TEXT");
            await AddColumnIfMissingAsync(connection, columns, "JobHistory", "CheckpointLabel", "CheckpointLabel TEXT");
        }

        private async Task EnsureColumnMetricColumnsExist(DbConnection connection)
        {
            var columns = await _dialect.GetColumnNamesAsync(connection, "JobColumnMetrics");
            await AddColumnIfMissingAsync(connection, columns, "JobColumnMetrics", "MaxTimestampUtc", "MaxTimestampUtc TEXT");

            // The tenant of the run these measurements belong to, carried directly as
            // SharedBackupSurfaceInventory declares. It is copied from JobHistory at insert rather
            // than passed in by the caller, so it cannot disagree with the run it describes; the
            // reason to hold it at all is that backup, restore and tenant deletion then take a
            // predicate on this table instead of a join that a future query could forget.
            foreach (var table in new[] { "JobColumnMetrics", "JobDataQualityFailures", "JobStatementMetrics" })
            {
                var tableColumns = await _dialect.GetColumnNamesAsync(connection, table);
                await AddColumnIfMissingAsync(
                    connection, tableColumns, table, "TenantId", "TenantId TEXT NOT NULL DEFAULT ''");
            }
        }

        private async Task EnsureLineageHistoryColumnsExist(DbConnection connection)
        {
            var columns = await _dialect.GetColumnNamesAsync(connection, "LineageHistory");

            async Task AddColumn(string name, string ddl)
            {
                await AddColumnIfMissingAsync(connection, columns, "LineageHistory", name, ddl);
            }

            // SourceColumns predates this migration on new installs but may be
            // missing on databases created before it was added to the schema.
            await AddColumn("SourceColumns", "SourceColumns TEXT NOT NULL DEFAULT '[]'");
            await AddColumn("TenantId", "TenantId TEXT NOT NULL DEFAULT 'portal-host'");
            await AddColumn("TransformationKind", "TransformationKind TEXT");
            await AddColumn("TransformationExpression", "TransformationExpression TEXT");
            await AddColumn("FunctionsApplied", "FunctionsApplied TEXT NOT NULL DEFAULT '[]'");
            await AddColumn("DerivedFromDescriptions", "DerivedFromDescriptions TEXT");

            using var indexes = connection.CreateCommand();
            indexes.CommandText = @"
                CREATE INDEX IF NOT EXISTS idx_lh_tenant_target
                    ON LineageHistory(TenantId, TargetTable COLLATE NOCASE);
                CREATE INDEX IF NOT EXISTS idx_lh_tenant_runAt
                    ON LineageHistory(TenantId, RunAt);";
            await indexes.ExecuteNonQueryAsync();
        }

        public async Task SaveJobAsync(JobDefinition job)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            // Upsert (not INSERT OR REPLACE): REPLACE deletes and reinserts the row, which would
            // silently clear an active execution lease whenever a job definition is re-saved.
            // The conflict target is (TenantId, Name), which is what makes the tenant binding
            // immutable without a guard clause: a save naming another tenant's job simply does not
            // conflict with it, so it can never rewrite that row. Id is set on insert only and never
            // updated, so an object keeps its identity — and its grants — across re-saves.
            var sql = @"
                INSERT INTO Jobs (Id, Name, Script, Interval, Unit, AtTime, LastRun, NextRun, IsEnabled, MaxRetries, RetryDelaySeconds, ScriptHash, HashPolicy, JobType, TargetPath, DisplayName, Description, Options, CreatedBy, ModifiedBy, TenantId)
                VALUES (@id, @name, @script, @interval, @unit, @atTime, @lastRun, @nextRun, @isEnabled, @maxRetries, @retryDelay, @scriptHash, @hashPolicy, @jobType, @targetPath, @displayName, @description, @options, @createdBy, @modifiedBy, @tenantId)
                ON CONFLICT(TenantId, Name) DO UPDATE SET
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
                    JobType           = excluded.JobType,
                    TargetPath        = excluded.TargetPath,
                    DisplayName       = excluded.DisplayName,
                    Description       = excluded.Description,
                    Options           = excluded.Options,
                    ModifiedBy        = excluded.ModifiedBy,
                    Version           = Jobs.Version + 1;";

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            AddJobParameters(command, job);

            if (await command.ExecuteNonQueryAsync() != 1)
                throw new InvalidOperationException("The scheduled job could not be saved.");
        }

        /// <summary>
        /// Optimistic-concurrency update of one job definition.
        ///
        /// <para><c>CreatedBy</c> is not in the update set, and is not filled in when it is missing
        /// either. An object with no owner is administrators-only until it is adopted, and adoption is
        /// an explicit, audited act — letting an edit confer ownership would mean the answer to "who is
        /// accountable for this job" was decided by whoever happened to touch it, quietly, with no
        /// record. Reassignment goes through <see cref="SetObjectOwnerAsync"/>.</para>
        /// </summary>
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
                    JobType = @jobType,
                    TargetPath = @targetPath,
                    DisplayName = @displayName,
                    Description = @description,
                    Options = @options,
                    ModifiedBy = @modifiedBy,
                    Version = Version + 1
                WHERE Id = @id AND Version = @expectedVersion;";
            AddJobParameters(command, job);
            command.AddParam("@expectedVersion", expectedVersion);
            return await command.ExecuteNonQueryAsync() == 1;
        }

        private static void AddJobParameters(DbCommand command, JobDefinition job)
        {
            command.AddParam("@id", NewOrExistingId(job.Id));
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
            command.AddParam("@jobType", job.JobType.ToString());
            command.AddParam("@targetPath", (object?)job.TargetPath ?? DBNull.Value);
            command.AddParam("@displayName", (object?)job.DisplayName ?? DBNull.Value);
            command.AddParam("@description", (object?)job.Description ?? DBNull.Value);
            command.AddParam("@options", (object?)job.Options ?? DBNull.Value);
            command.AddParam("@createdBy", (object?)job.CreatedBy ?? DBNull.Value);
            command.AddParam("@modifiedBy", (object?)job.ModifiedBy ?? DBNull.Value);
            command.AddParam("@tenantId", string.IsNullOrWhiteSpace(job.TenantId)
                ? UnboundTenantSentinel
                : TenantId.FromTrustedSource(job.TenantId).Value);
        }

        /// <summary>
        /// Identity is assigned once, on first insert, and never reassigned. A caller that already
        /// holds a definition round-trips its id so a re-save updates the same object rather than
        /// orphaning its grants, links, history, and watermarks behind a new one.
        /// </summary>
        internal static string NewOrExistingId(JobId existing) =>
            existing.IsAssigned ? existing.Value : JobId.New().Value;

        internal static string NewOrExistingId(ScheduleId existing) =>
            existing.IsAssigned ? existing.Value : ScheduleId.New().Value;

        internal static string NewOrExistingId(NotificationId existing) =>
            existing.IsAssigned ? existing.Value : NotificationId.New().Value;

        // ── Execution lease (P1.1) ────────────────────────────────────────────────
        // Lease times are UTC ISO-8601 ("O") strings: they compare correctly both lexically
        // and via SQLite's date functions, and stay unambiguous across hosts in different
        // time zones. SQLite's single-writer model makes each UPDATE atomic, which is the
        // entire claim mechanism — but it also means the lease only coordinates processes
        // that share this database file (see the P3.1 topology decision).

        public async Task<bool> TryAcquireJobLeaseAsync(JobId jobId, string owner, TimeSpan duration)
            => await AcquireJobLeaseAsync(jobId, owner, duration) is not null;

        public async Task<long?> AcquireJobLeaseAsync(JobId jobIdRef, string owner, TimeSpan duration)
        {
            var jobId = jobIdRef.Require();
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
                WHERE Id = @id
                  AND IsEnabled = 1
                  AND (LeaseOwner IS NULL OR LeaseExpiresAt IS NULL OR LeaseExpiresAt <= @now)
                  AND NOT EXISTS (
                      SELECT 1 FROM SharedTenantControlPlanes lifecycle
                       WHERE lifecycle.TenantId = Jobs.TenantId
                         AND lifecycle.State <> 'Active');";
            claim.AddParam("@owner", owner);
            claim.AddParam("@expires", now.Add(duration).ToString("O"));
            claim.AddParam("@id", jobId);
            claim.AddParam("@now", now.ToString("O"));

            if (await claim.ExecuteNonQueryAsync() != 1)
                return null;

            // We now own the row; read back the token we were granted.
            using var read = connection.CreateCommand();
            read.CommandText = "SELECT LeaseFenceToken FROM Jobs WHERE Id = @id AND LeaseOwner = @owner;";
            read.AddParam("@id", jobId);
            read.AddParam("@owner", owner);
            var token = await read.ExecuteScalarAsync();
            return token is null or DBNull ? null : Convert.ToInt64(token);
        }

        public async Task<bool> ValidateFenceTokenAsync(JobId jobIdRef, long fenceToken)
        {
            var jobId = jobIdRef.Require();
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT LeaseFenceToken FROM Jobs WHERE Id = @id;";
            command.AddParam("@id", jobId);
            var current = await command.ExecuteScalarAsync();
            // A token is valid only if it is the latest issued — a newer acquisition would have advanced
            // the stored token beyond the holder's.
            return current is not (null or DBNull) && fenceToken >= Convert.ToInt64(current);
        }

        public async Task<bool> TryUpdateJobLastRunFencedAsync(JobId jobIdRef, DateTime lastRun, DateTime? nextRun, long fenceToken)
        {
            var jobId = jobIdRef.Require();
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            // The write carries the fence token: it lands only while the holder is still the current
            // owner (token unchanged). If a newer owner has acquired the lease, the token moved and this
            // UPDATE matches zero rows — the stale writer is fenced out.
            command.CommandText = @"
                UPDATE Jobs SET LastRun = @lastRun, NextRun = @nextRun
                WHERE Id = @id AND LeaseFenceToken = @token;";
            command.AddParam("@lastRun", lastRun.ToString("O"));
            command.AddParam("@nextRun", (object?)nextRun?.ToString("O") ?? DBNull.Value);
            command.AddParam("@id", jobId);
            command.AddParam("@token", fenceToken);

            return await command.ExecuteNonQueryAsync() == 1;
        }

        public async Task<bool> TryRenewJobLeaseAsync(JobId jobIdRef, string owner, TimeSpan duration)
        {
            var jobId = jobIdRef.Require();
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Jobs SET LeaseExpiresAt = @expires
                WHERE Id = @id AND LeaseOwner = @owner;";
            command.AddParam("@expires", DateTime.UtcNow.Add(duration).ToString("O"));
            command.AddParam("@id", jobId);
            command.AddParam("@owner", owner);

            return await command.ExecuteNonQueryAsync() == 1;
        }

        public async Task ReleaseJobLeaseAsync(JobId jobIdRef, string owner)
        {
            var jobId = jobIdRef.Require();
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Jobs SET LeaseOwner = NULL, LeaseExpiresAt = NULL
                WHERE Id = @id AND LeaseOwner = @owner;";
            command.AddParam("@id", jobId);
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
            // Legacy interval trigger, for jobs with no schedule attached. A job that has links is
            // driven by GetJobsDueByScheduleAsync instead, and must be excluded here: its
            // Jobs.NextRun is a derived display value that starts NULL, and NULL means "due now" on
            // this path — so without the exclusion a link-scheduled job would fire on every tick.
            // This whole branch goes away once CREATE JOB stops producing interval jobs.
            command.CommandText = @"
                SELECT * FROM Jobs
                WHERE IsEnabled = 1
                  AND (NextRun IS NULL OR NextRun <= @now)
                  AND NOT EXISTS (SELECT 1 FROM JobSchedules js WHERE js.JobId = Jobs.Id);";
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

        public async Task<IEnumerable<JobDefinition>> GetJobsPageAsync(int limit = 100, int offset = 0)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Jobs ORDER BY Name LIMIT @limit OFFSET @offset;";
            command.AddParam("@limit", Math.Clamp(limit, 1, 1000));
            command.AddParam("@offset", Math.Max(0, offset));

            var jobs = new List<JobDefinition>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) jobs.Add(ReadJob(reader));
            return jobs;
        }

        public async Task<JobDefinition?> GetJobAsync(string? tenantId, string name)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT * FROM Jobs WHERE TenantId = @tenant AND Name = @name COLLATE NOCASE LIMIT 1;";
            command.AddParam("@tenant", TenantKey(tenantId));
            command.AddParam("@name", name);

            using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            return ReadJob(reader);
        }

        public async Task<JobDefinition?> GetJobByIdAsync(JobId jobIdRef)
        {
            var jobId = jobIdRef.Require();
            if (string.IsNullOrWhiteSpace(jobId)) return null;
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Jobs WHERE Id = @id LIMIT 1;";
            command.AddParam("@id", jobId);

            using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync() ? ReadJob(reader) : null;
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
                reader.GetInt64(versionOrdinal),
                ReadJobType(reader),
                ReadOptionalString(reader, "TargetPath"),
                ReadOptionalString(reader, "DisplayName"),
                ReadOptionalString(reader, "Description"),
                ReadOptionalString(reader, "Options"),
                ReadOptionalString(reader, "CreatedBy"),
                ReadOptionalString(reader, "ModifiedBy"),
                TenantOrNull(ReadOptionalString(reader, "TenantId")),
                JobId.From(ReadOptionalString(reader, "Id")));
        }

        /// <summary>
        /// An unparseable stored value falls back to <see cref="JobTargetKind.Script"/> rather than
        /// throwing: a job whose type cannot be read is still a job an operator needs to see and drop,
        /// and failing the whole listing would hide every other job alongside it.
        /// </summary>
        private static JobTargetKind ReadJobType(DbDataReader reader)
        {
            var raw = ReadOptionalString(reader, "JobType");
            return Enum.TryParse<JobTargetKind>(raw, ignoreCase: true, out var kind) ? kind : JobTargetKind.Script;
        }

        public async Task DeleteJobAsync(JobId jobIdRef)
        {
            var jobId = jobIdRef.Require();
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                var sql1 = "DELETE FROM Jobs WHERE Id = @id;";
                using var command1 = connection.CreateCommand();
                command1.CommandText = sql1;
                command1.Transaction = transaction;
                command1.AddParam("@id", jobId);
                await command1.ExecuteNonQueryAsync();

                var sqlMetrics = "DELETE FROM JobColumnMetrics WHERE JobHistoryId IN (SELECT Id FROM JobHistory WHERE JobId = @id);";
                using var commandMetrics = connection.CreateCommand();
                commandMetrics.CommandText = sqlMetrics;
                commandMetrics.Transaction = transaction;
                commandMetrics.AddParam("@id", jobId);
                await commandMetrics.ExecuteNonQueryAsync();

                var sqlFailures = "DELETE FROM JobDataQualityFailures WHERE JobHistoryId IN (SELECT Id FROM JobHistory WHERE JobId = @id);";
                using var commandFailures = connection.CreateCommand();
                commandFailures.CommandText = sqlFailures;
                commandFailures.Transaction = transaction;
                commandFailures.AddParam("@id", jobId);
                await commandFailures.ExecuteNonQueryAsync();

                var sqlStatements = "DELETE FROM JobStatementMetrics WHERE JobHistoryId IN (SELECT Id FROM JobHistory WHERE JobId = @id);";
                using var commandStatements = connection.CreateCommand();
                commandStatements.CommandText = sqlStatements;
                commandStatements.Transaction = transaction;
                commandStatements.AddParam("@id", jobId);
                await commandStatements.ExecuteNonQueryAsync();

                var sql2 = "DELETE FROM JobHistory WHERE JobId = @id;";
                using var command2 = connection.CreateCommand();
                command2.CommandText = sql2;
                command2.Transaction = transaction;
                command2.AddParam("@id", jobId);
                await command2.ExecuteNonQueryAsync();

                // Attachments cascade with the job: a link has no meaning without one side of it.
                // The schedules and notifications themselves survive — they are shared objects.
                await DeleteJobLinksAsync(connection, transaction, jobId);

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<bool> TryDeleteJobAsync(JobId jobIdRef, long expectedVersion)
        {
            var jobId = jobIdRef.Require();
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            using var deleteJob = connection.CreateCommand();
            deleteJob.Transaction = transaction;
            deleteJob.CommandText = "DELETE FROM Jobs WHERE Id = @id AND Version = @version;";
            deleteJob.AddParam("@id", jobId);
            deleteJob.AddParam("@version", expectedVersion);
            if (await deleteJob.ExecuteNonQueryAsync() != 1)
            {
                await transaction.RollbackAsync();
                return false;
            }

            using var deleteMetrics = connection.CreateCommand();
            deleteMetrics.Transaction = transaction;
            deleteMetrics.CommandText = "DELETE FROM JobColumnMetrics WHERE JobHistoryId IN (SELECT Id FROM JobHistory WHERE JobId = @id);";
            deleteMetrics.AddParam("@id", jobId);
            await deleteMetrics.ExecuteNonQueryAsync();

            using var deleteFailures = connection.CreateCommand();
            deleteFailures.Transaction = transaction;
            deleteFailures.CommandText = "DELETE FROM JobDataQualityFailures WHERE JobHistoryId IN (SELECT Id FROM JobHistory WHERE JobId = @id);";
            deleteFailures.AddParam("@id", jobId);
            await deleteFailures.ExecuteNonQueryAsync();

            using var deleteStatements = connection.CreateCommand();
            deleteStatements.Transaction = transaction;
            deleteStatements.CommandText = "DELETE FROM JobStatementMetrics WHERE JobHistoryId IN (SELECT Id FROM JobHistory WHERE JobId = @id);";
            deleteStatements.AddParam("@id", jobId);
            await deleteStatements.ExecuteNonQueryAsync();

            using var deleteHistory = connection.CreateCommand();
            deleteHistory.Transaction = transaction;
            deleteHistory.CommandText = "DELETE FROM JobHistory WHERE JobId = @id;";
            deleteHistory.AddParam("@id", jobId);
            await deleteHistory.ExecuteNonQueryAsync();

            await DeleteJobLinksAsync(connection, transaction, jobId);

            await transaction.CommitAsync();
            return true;
        }

        public async Task UpdateJobLastRunAsync(JobId jobIdRef, DateTime lastRun, DateTime? nextRun)
        {
            var jobId = jobIdRef.Require();
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            var sql = "UPDATE Jobs SET LastRun = @lastRun, NextRun = @nextRun WHERE Id = @id;";
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.AddParam("@id", jobId);
            command.AddParam("@lastRun", lastRun.ToString("O"));
            command.AddParam("@nextRun", (object?)nextRun?.ToString("O") ?? DBNull.Value);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<long> LogJobStartAsync(JobId jobIdRef)
        {
            var jobId = jobIdRef.Require();
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            // Name and tenant are copied from the job row rather than passed in: history outlives the
            // job, and a run must record the name it actually ran under even if that name is later
            // taken by a different object.
            var sql = _dialect.InsertReturningId(
                @"INSERT INTO JobHistory (JobId, TenantId, JobName, StartTime, Status)
                  SELECT j.Id, j.TenantId, j.Name, @start, 'RUNNING' FROM Jobs j WHERE j.Id = @id", "Id");
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.AddParam("@id", jobId);
            command.AddParam("@start", DateTime.Now.ToString("O"));

            // SQLite's last_insert_rowid() returns long; Postgres RETURNING id returns int — normalize.
            // A missing job produces no row and so no scalar: that means the caller asked to record a
            // run of something that does not exist, which is worth saying out loud.
            var inserted = await command.ExecuteScalarAsync();
            if (inserted is null || inserted == DBNull.Value)
                throw new InvalidOperationException(
                    $"Cannot record a run for job '{jobId}': no such job. It may have been dropped.");
            return Convert.ToInt64(inserted);
        }

        public async Task<long> LogAdHocRunStartAsync(string label, string? tenantId = null)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            // No job binding: this run is not a job. JobId stays the empty sentinel so the row can
            // never be mistaken for — or joined to — a real job's history.
            var sql = _dialect.InsertReturningId(
                @"INSERT INTO JobHistory (JobId, TenantId, JobName, StartTime, Status)
                  VALUES ('', @tenant, @label, @start, 'RUNNING')", "Id");
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.AddParam("@tenant", TenantKey(tenantId));
            command.AddParam("@label", label);
            command.AddParam("@start", DateTime.Now.ToString("O"));
            return Convert.ToInt64((await command.ExecuteScalarAsync())!);
        }

        public async Task LogJobEndAsync(long entryId, string status, string? errorMessage = null, long rowsProcessed = 0, long peakMemoryBytes = 0, double cpuTimeSeconds = 0, string? scriptHashAtRunTime = null, bool? hashMatched = null, long rowsQuarantined = 0, long rowsWarned = 0, string? dataQualityFailures = null)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            var sql = "UPDATE JobHistory SET EndTime = @end, Status = @status, ErrorMessage = @err, RowsProcessed = @rows, PeakMemoryBytes = @mem, CpuTimeSeconds = @cpu, ScriptHashAtRunTime = @hash, HashMatched = @matched, RowsQuarantined = @quarantined, RowsWarned = @warned, DataQualityFailures = @dqfail WHERE Id = @id;";
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
            command.AddParam("@quarantined", rowsQuarantined);
            command.AddParam("@warned", rowsWarned);
            command.AddParam("@dqfail", (object?)dataQualityFailures ?? DBNull.Value);

            await command.ExecuteNonQueryAsync();
        }

        public async Task UpdateJobResumeMetadataAsync(long entryId, string? sessionId, string? checkpointLabel)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE JobHistory SET SessionId = @session, CheckpointLabel = @label WHERE Id = @id;";
            command.AddParam("@id", entryId);
            command.AddParam("@session", (object?)sessionId ?? DBNull.Value);
            command.AddParam("@label", (object?)checkpointLabel ?? DBNull.Value);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<long> ImportJobHistoryAsync(JobHistoryEntry entry)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using (var existing = connection.CreateCommand())
            {
                existing.CommandText = @"
                    SELECT Id FROM JobHistory
                    WHERE JobName = @name COLLATE NOCASE AND StartTime = @start
                      AND ((EndTime IS NULL AND @end IS NULL) OR EndTime = @end)
                    ORDER BY Id LIMIT 1;";
                existing.AddParam("@name", entry.JobName);
                existing.AddParam("@start", entry.StartTime.ToString("O"));
                existing.AddParam("@end", entry.EndTime.HasValue ? entry.EndTime.Value.ToString("O") : DBNull.Value);
                var found = await existing.ExecuteScalarAsync();
                if (found is not null && found != DBNull.Value)
                    return Convert.ToInt64(found);
            }

            var sql = _dialect.InsertReturningId(@"
                INSERT INTO JobHistory
                    (JobName, StartTime, EndTime, Status, ErrorMessage, RowsProcessed,
                     PeakMemoryBytes, CpuTimeSeconds, ScriptHashAtRunTime, HashMatched,
                     RowsQuarantined, RowsWarned, DataQualityFailures, SessionId, CheckpointLabel)
                VALUES
                    (@name, @start, @end, @status, @error, @rows,
                     @memory, @cpu, @hash, @matched, @quarantined, @warned, @failures, @session, @checkpoint)", "Id");
            using var insert = connection.CreateCommand();
            insert.CommandText = sql;
            insert.AddParam("@name", entry.JobName);
            insert.AddParam("@start", entry.StartTime.ToString("O"));
            insert.AddParam("@end", entry.EndTime.HasValue ? entry.EndTime.Value.ToString("O") : DBNull.Value);
            insert.AddParam("@status", entry.Status);
            insert.AddParam("@error", (object?)entry.ErrorMessage ?? DBNull.Value);
            insert.AddParam("@rows", entry.RowsProcessed);
            insert.AddParam("@memory", entry.PeakMemoryBytes);
            insert.AddParam("@cpu", entry.CpuTimeSeconds);
            insert.AddParam("@hash", (object?)entry.ScriptHashAtRunTime ?? DBNull.Value);
            insert.AddParam("@matched", entry.HashMatched.HasValue ? (object)(entry.HashMatched.Value ? 1 : 0) : DBNull.Value);
            insert.AddParam("@quarantined", entry.RowsQuarantined);
            insert.AddParam("@warned", entry.RowsWarned);
            insert.AddParam("@failures", (object?)entry.DataQualityFailures ?? DBNull.Value);
            insert.AddParam("@session", (object?)entry.SessionId ?? DBNull.Value);
            insert.AddParam("@checkpoint", (object?)entry.CheckpointLabel ?? DBNull.Value);
            return Convert.ToInt64((await insert.ExecuteScalarAsync())!);
        }

        public async Task SaveJobColumnMetricsAsync(long entryId, IEnumerable<DataQualityColumnMetric> metrics)
        {
            var rows = metrics.Where(m => m.TotalRows > 0).ToList();
            if (rows.Count == 0) return;

            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            foreach (var metric in rows)
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO JobColumnMetrics (JobHistoryId, TenantId, TargetTable, ColumnName, TotalRows, NullRows, MaxTimestampUtc)
                    VALUES (@historyId, COALESCE((SELECT TenantId FROM JobHistory WHERE Id = @historyId), ''), @target, @column, @total, @nulls, @maxTimestamp)
                    ON CONFLICT (JobHistoryId, TargetTable, ColumnName) DO UPDATE SET
                        TotalRows = excluded.TotalRows,
                        NullRows = excluded.NullRows,
                        MaxTimestampUtc = excluded.MaxTimestampUtc;";
                command.AddParam("@historyId", entryId);
                var target = string.IsNullOrWhiteSpace(metric.TargetTable)
                    ? null
                    : metric.TargetTable.Trim().TrimStart('#');
                command.AddParam("@target", target ?? "");
                command.AddParam("@column", metric.ColumnName);
                command.AddParam("@total", metric.TotalRows);
                command.AddParam("@nulls", metric.NullRows);
                command.AddParam("@maxTimestamp", metric.MaxTimestampUtc.HasValue
                    ? metric.MaxTimestampUtc.Value.ToUniversalTime().ToString("O")
                    : DBNull.Value);
                await command.ExecuteNonQueryAsync();
            }
        }

        public async Task SaveJobStatementMetricsAsync(
            long entryId, IEnumerable<ETL_SQL.Core.Profiling.StatementMetricsPayload> statements)
        {
            var rows = statements as IList<ETL_SQL.Core.Profiling.StatementMetricsPayload> ?? statements.ToList();
            if (rows.Count == 0) return;

            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            for (var ordinal = 0; ordinal < rows.Count; ordinal++)
            {
                var statement = rows[ordinal];
                using var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO JobStatementMetrics (
                        JobHistoryId, TenantId, Ordinal, Statement, DurationMs, RowsProcessed, CpuTimeMs,
                        SpilledBytes, SpillReadBytes, Partitions, QueueWaitMs, LockWaitMs, IndexUsed,
                        DqRowsValidated, DqRowsQuarantined, DqRowsWarned, DqValidationMs, Failed)
                    VALUES (
                        @historyId, COALESCE((SELECT TenantId FROM JobHistory WHERE Id = @historyId), ''),
                        @ordinal, @statement, @duration, @rows, @cpu,
                        @spilled, @spillRead, @partitions, @queueWait, @lockWait, @indexUsed,
                        @dqValidated, @dqQuarantined, @dqWarned, @dqMs, @failed)
                    ON CONFLICT (JobHistoryId, Ordinal) DO NOTHING;";
                command.AddParam("@historyId", entryId);
                command.AddParam("@ordinal", ordinal);
                // Normalized upstream by StatementMetricsPayload.From; never raw statement text.
                command.AddParam("@statement", statement.Statement ?? "");
                command.AddParam("@duration", statement.DurationMs);
                command.AddParam("@rows", statement.RowsProcessed);
                command.AddParam("@cpu", statement.CpuTimeMs);
                command.AddParam("@spilled", statement.SpilledBytes);
                command.AddParam("@spillRead", statement.SpillReadBytes);
                command.AddParam("@partitions", statement.Partitions);
                command.AddParam("@queueWait", statement.QueueWaitMs);
                command.AddParam("@lockWait", statement.LockWaitMs);
                command.AddParam("@indexUsed", (object?)statement.IndexUsed ?? DBNull.Value);
                command.AddParam("@dqValidated", statement.DataQualityRowsValidated);
                command.AddParam("@dqQuarantined", statement.DataQualityRowsQuarantined);
                command.AddParam("@dqWarned", statement.DataQualityRowsWarned);
                command.AddParam("@dqMs", statement.DataQualityValidationMs);
                command.AddParam("@failed", statement.Failed ? 1 : 0);
                await command.ExecuteNonQueryAsync();
            }
        }

        public async Task SaveTenantUsageAsync(TenantUsageRecord usage)
        {
            ArgumentNullException.ThrowIfNull(usage);
            var tenant = TenantId.FromTrustedSource(usage.TenantId).Value;
            if (usage.JobHistoryId <= 0 || usage.RowsProcessed < 0 || usage.PeakMemoryBytes < 0
                || usage.CpuTimeSeconds < 0 || usage.DurationMs < 0)
                throw new ArgumentException("Tenant usage measures and history identity must be non-negative.", nameof(usage));

            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO TenantUsageRecords (
                    TenantId, JobHistoryId, WorkloadKind, Status, RowsProcessed,
                    PeakMemoryBytes, CpuTimeSeconds, DurationMs, RecordedAtUtc)
                VALUES (@tenant, @historyId, @kind, @status, @rows, @memory, @cpu, @duration, @recorded)
                ON CONFLICT (TenantId, JobHistoryId) DO NOTHING;";
            command.AddParam("@tenant", tenant);
            command.AddParam("@historyId", usage.JobHistoryId);
            command.AddParam("@kind", usage.WorkloadKind);
            command.AddParam("@status", usage.Status);
            command.AddParam("@rows", usage.RowsProcessed);
            command.AddParam("@memory", usage.PeakMemoryBytes);
            command.AddParam("@cpu", usage.CpuTimeSeconds);
            command.AddParam("@duration", usage.DurationMs);
            command.AddParam("@recorded", usage.RecordedAtUtc.ToUniversalTime().ToString("O"));
            await command.ExecuteNonQueryAsync();
        }

        public async Task<IReadOnlyList<TenantUsageRecord>> GetTenantUsageAsync(
            string tenantId, DateTime? fromUtc = null, int limit = 1000)
        {
            var tenant = TenantId.FromTrustedSource(tenantId).Value;
            limit = Math.Clamp(limit, 1, 10_000);
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, TenantId, JobHistoryId, WorkloadKind, Status, RowsProcessed,
                       PeakMemoryBytes, CpuTimeSeconds, DurationMs, RecordedAtUtc
                FROM TenantUsageRecords
                WHERE TenantId = @tenant
                  AND (@fromUtc IS NULL OR RecordedAtUtc >= @fromUtc)
                ORDER BY RecordedAtUtc DESC, Id DESC
                LIMIT @limit;";
            command.AddParam("@tenant", tenant);
            command.AddParam("@fromUtc", fromUtc.HasValue
                ? fromUtc.Value.ToUniversalTime().ToString("O")
                : DBNull.Value);
            command.AddParam("@limit", limit);
            var rows = new List<TenantUsageRecord>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new TenantUsageRecord(
                    reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2),
                    reader.GetString(3), reader.GetString(4), reader.GetInt64(5),
                    reader.GetInt64(6), reader.GetDouble(7), reader.GetInt64(8),
                    DateTime.Parse(reader.GetString(9), CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind)));
            }
            return rows;
        }

        public async Task<int> PruneStatementMetricsAsync(TimeSpan successMaxAge, TimeSpan failedMaxAge)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            var removed = 0;

            // A run with a statement marked failed is a failed run; the flag lives on the
            // statement rows rather than being inferred again from a status vocabulary.
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    DELETE FROM JobStatementMetrics
                    WHERE JobHistoryId IN (
                        SELECT h.Id FROM JobHistory h
                        WHERE h.Status <> 'RUNNING' AND h.StartTime < @successCutoff
                          AND NOT EXISTS (
                              SELECT 1 FROM JobStatementMetrics m
                              WHERE m.JobHistoryId = h.Id AND m.Failed <> 0
                          )
                    );";
                command.AddParam("@successCutoff", DateTime.Now.Subtract(successMaxAge).ToString("O"));
                removed += await command.ExecuteNonQueryAsync();
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    DELETE FROM JobStatementMetrics
                    WHERE JobHistoryId IN (
                        SELECT h.Id FROM JobHistory h
                        WHERE h.Status <> 'RUNNING' AND h.StartTime < @failedCutoff
                          AND EXISTS (
                              SELECT 1 FROM JobStatementMetrics m
                              WHERE m.JobHistoryId = h.Id AND m.Failed <> 0
                          )
                    );";
                command.AddParam("@failedCutoff", DateTime.Now.Subtract(failedMaxAge).ToString("O"));
                removed += await command.ExecuteNonQueryAsync();
            }

            return removed;
        }

        public async Task<IReadOnlyList<ETL_SQL.Core.Profiling.StatementMetricsPayload>>
            GetJobStatementMetricsAsync(long entryId)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Statement, DurationMs, RowsProcessed, CpuTimeMs, SpilledBytes, SpillReadBytes,
                       Partitions, QueueWaitMs, LockWaitMs, IndexUsed,
                       DqRowsValidated, DqRowsQuarantined, DqRowsWarned, DqValidationMs, Failed
                FROM JobStatementMetrics
                WHERE JobHistoryId = @historyId
                ORDER BY Ordinal;";
            command.AddParam("@historyId", entryId);

            var result = new List<ETL_SQL.Core.Profiling.StatementMetricsPayload>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new ETL_SQL.Core.Profiling.StatementMetricsPayload
                {
                    Statement = reader.GetString(0),
                    DurationMs = Convert.ToInt64(reader.GetValue(1)),
                    RowsProcessed = Convert.ToInt64(reader.GetValue(2)),
                    CpuTimeMs = Convert.ToInt64(reader.GetValue(3)),
                    SpilledBytes = Convert.ToInt64(reader.GetValue(4)),
                    SpillReadBytes = Convert.ToInt64(reader.GetValue(5)),
                    Partitions = Convert.ToInt32(reader.GetValue(6)),
                    QueueWaitMs = Convert.ToInt64(reader.GetValue(7)),
                    LockWaitMs = Convert.ToInt64(reader.GetValue(8)),
                    IndexUsed = reader.IsDBNull(9) ? null : reader.GetString(9),
                    DataQualityRowsValidated = Convert.ToInt64(reader.GetValue(10)),
                    DataQualityRowsQuarantined = Convert.ToInt64(reader.GetValue(11)),
                    DataQualityRowsWarned = Convert.ToInt64(reader.GetValue(12)),
                    DataQualityValidationMs = Convert.ToInt64(reader.GetValue(13)),
                    Failed = Convert.ToInt64(reader.GetValue(14)) != 0
                });
            }
            return result;
        }

        public async Task SaveJobDataQualityFailuresAsync(long entryId, IEnumerable<DataQualityRuleFailureMetric> failures)
        {
            var rows = failures.Where(f => f.FailureCount > 0).ToList();
            if (rows.Count == 0) return;

            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            foreach (var failure in rows)
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO JobDataQualityFailures
                        (JobHistoryId, TenantId, TargetTable, ColumnName, RuleText, Action, FailureCount, Owner)
                    VALUES (@historyId, COALESCE((SELECT TenantId FROM JobHistory WHERE Id = @historyId), ''), @target, @column, @rule, @action, @count, @owner)
                    ON CONFLICT (JobHistoryId, TargetTable, ColumnName, RuleText, Action) DO UPDATE SET
                        FailureCount = excluded.FailureCount,
                        Owner = excluded.Owner;";
                command.AddParam("@historyId", entryId);
                command.AddParam("@target", string.IsNullOrWhiteSpace(failure.TargetTable)
                    ? "" : failure.TargetTable.Trim().TrimStart('#'));
                command.AddParam("@column", failure.ColumnName);
                command.AddParam("@rule", failure.Rule);
                command.AddParam("@action", failure.Action.ToUpperInvariant());
                command.AddParam("@count", failure.FailureCount);
                command.AddParam("@owner", (object?)failure.Owner ?? DBNull.Value);
                await command.ExecuteNonQueryAsync();
            }
        }

        public async Task<IReadOnlyList<JobStatementMetric>> GetStatementMetricsAsync(int limit = 1000)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT h.Id, h.JobName, h.StartTime, h.EndTime, h.Status,
                       m.Ordinal, m.Statement, m.DurationMs, m.RowsProcessed, m.CpuTimeMs,
                       m.SpilledBytes, m.SpillReadBytes, m.Partitions, m.QueueWaitMs, m.LockWaitMs,
                       m.IndexUsed, m.DqRowsValidated, m.DqRowsQuarantined, m.DqRowsWarned,
                       m.DqValidationMs, m.Failed
                FROM JobStatementMetrics m
                INNER JOIN JobHistory h ON h.Id = m.JobHistoryId
                ORDER BY h.StartTime DESC, h.Id DESC, m.Ordinal
                LIMIT @limit;";
            command.AddParam("@limit", Math.Clamp(limit, 1, 10000));

            var results = new List<JobStatementMetric>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new JobStatementMetric(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    DateTime.Parse(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3)),
                    reader.GetString(4),
                    Convert.ToInt32(reader.GetValue(5)),
                    new ETL_SQL.Core.Profiling.StatementMetricsPayload
                    {
                        Statement = reader.GetString(6),
                        DurationMs = Convert.ToInt64(reader.GetValue(7)),
                        RowsProcessed = Convert.ToInt64(reader.GetValue(8)),
                        CpuTimeMs = Convert.ToInt64(reader.GetValue(9)),
                        SpilledBytes = Convert.ToInt64(reader.GetValue(10)),
                        SpillReadBytes = Convert.ToInt64(reader.GetValue(11)),
                        Partitions = Convert.ToInt32(reader.GetValue(12)),
                        QueueWaitMs = Convert.ToInt64(reader.GetValue(13)),
                        LockWaitMs = Convert.ToInt64(reader.GetValue(14)),
                        IndexUsed = reader.IsDBNull(15) ? null : reader.GetString(15),
                        DataQualityRowsValidated = Convert.ToInt64(reader.GetValue(16)),
                        DataQualityRowsQuarantined = Convert.ToInt64(reader.GetValue(17)),
                        DataQualityRowsWarned = Convert.ToInt64(reader.GetValue(18)),
                        DataQualityValidationMs = Convert.ToInt64(reader.GetValue(19)),
                        Failed = Convert.ToInt64(reader.GetValue(20)) != 0
                    }));
            }
            return results;
        }

        public async Task<IReadOnlyList<JobDataQualityFailure>> GetDataQualityFailuresAsync(int limit = 1000)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT h.Id, h.JobName, h.StartTime, h.EndTime, h.Status,
                       f.TargetTable, f.ColumnName, f.RuleText, f.Action, f.FailureCount, f.Owner
                FROM JobDataQualityFailures f
                INNER JOIN JobHistory h ON h.Id = f.JobHistoryId
                ORDER BY h.StartTime DESC, h.Id DESC, f.TargetTable, f.ColumnName, f.RuleText, f.Action
                LIMIT @limit;";
            command.AddParam("@limit", Math.Clamp(limit, 1, 10000));

            var results = new List<JobDataQualityFailure>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new JobDataQualityFailure(
                    reader.GetInt64(0), reader.GetString(1), DateTime.Parse(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3)), reader.GetString(4),
                    reader.IsDBNull(5) || string.IsNullOrEmpty(reader.GetString(5)) ? null : reader.GetString(5),
                    reader.GetString(6), reader.GetString(7),
                    reader.GetString(8), reader.GetInt64(9), reader.IsDBNull(10) ? null : reader.GetString(10)));
            }
            return results;
        }

        /// <summary>
        /// Failures recorded under a job <em>name</em>, which is what the interface member this
        /// overrides is addressed by — and what the caller has: a name typed into a script or a
        /// query string. It briefly filtered on <c>h.JobId</c> while keeping the interface's
        /// parameter type, so it still overrode the member and simply matched nothing.
        /// </summary>
        public async Task<IReadOnlyList<JobDataQualityFailure>> GetDataQualityFailuresForJobAsync(string jobName, int limit = 1000)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT h.Id, h.JobName, h.StartTime, h.EndTime, h.Status,
                       f.TargetTable, f.ColumnName, f.RuleText, f.Action, f.FailureCount, f.Owner
                FROM JobDataQualityFailures f
                INNER JOIN JobHistory h ON h.Id = f.JobHistoryId
                WHERE h.JobName = @jobName COLLATE NOCASE
                ORDER BY h.StartTime DESC, h.Id DESC, f.TargetTable, f.ColumnName, f.RuleText, f.Action
                LIMIT @limit;";
            command.AddParam("@jobName", jobName);
            command.AddParam("@limit", Math.Clamp(limit, 1, 10000));

            var results = new List<JobDataQualityFailure>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new JobDataQualityFailure(
                    reader.GetInt64(0), reader.GetString(1), DateTime.Parse(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3)), reader.GetString(4),
                    reader.IsDBNull(5) || string.IsNullOrEmpty(reader.GetString(5)) ? null : reader.GetString(5),
                    reader.GetString(6), reader.GetString(7),
                    reader.GetString(8), reader.GetInt64(9), reader.IsDBNull(10) ? null : reader.GetString(10)));
            }
            return results;
        }

        public async Task<IReadOnlyList<JobDataQualityFailure>> GetDataQualityFailuresForRunAsync(long entryId, int limit = 1000)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT h.Id, h.JobName, h.StartTime, h.EndTime, h.Status,
                       f.TargetTable, f.ColumnName, f.RuleText, f.Action, f.FailureCount, f.Owner
                FROM JobDataQualityFailures f
                INNER JOIN JobHistory h ON h.Id = f.JobHistoryId
                WHERE h.Id = @historyId
                ORDER BY f.TargetTable, f.ColumnName, f.RuleText, f.Action
                LIMIT @limit;";
            command.AddParam("@historyId", entryId);
            command.AddParam("@limit", Math.Clamp(limit, 1, 10000));

            var results = new List<JobDataQualityFailure>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new JobDataQualityFailure(
                    reader.GetInt64(0), reader.GetString(1), DateTime.Parse(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3)), reader.GetString(4),
                    reader.IsDBNull(5) || string.IsNullOrEmpty(reader.GetString(5)) ? null : reader.GetString(5),
                    reader.GetString(6), reader.GetString(7),
                    reader.GetString(8), reader.GetInt64(9), reader.IsDBNull(10) ? null : reader.GetString(10)));
            }
            return results;
        }

        public async Task<IReadOnlyList<JobDataQualityStatus>> GetDataQualityStatusesAsync(int limit = 1000)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT h.Id, h.JobName, h.StartTime, h.EndTime, h.Status,
                       h.RowsProcessed, h.RowsWarned, h.RowsQuarantined, h.ErrorMessage,
                       (SELECT COUNT(*) FROM JobDataQualityFailures f WHERE f.JobHistoryId = h.Id) AS FailedRuleCount,
                       (SELECT MAX(m.MaxTimestampUtc) FROM JobColumnMetrics m WHERE m.JobHistoryId = h.Id) AS FreshestValueUtc
                FROM JobHistory h
                ORDER BY h.StartTime DESC, h.Id DESC
                LIMIT @limit;";
            command.AddParam("@limit", Math.Clamp(limit, 1, 10000));

            var results = new List<JobDataQualityStatus>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var freshest = reader.IsDBNull(10) ? (DateTimeOffset?)null : DateTimeOffset.Parse(reader.GetString(10));
                results.Add(new JobDataQualityStatus(
                    reader.GetInt64(0).ToString(CultureInfo.InvariantCulture), reader.GetString(1),
                    DateTime.Parse(reader.GetString(2)), reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3)),
                    reader.GetString(4), reader.GetInt64(5), ReadInt64(reader, 6), ReadInt64(reader, 7),
                    Convert.ToInt32(reader.GetValue(9)), freshest, freshest.HasValue ? "OBSERVED" : "NOT_TRACKED",
                    reader.IsDBNull(8) ? null : SecretRedactor.Redact(reader.GetString(8))));
            }
            return results;
        }

        private static long ReadInt64(DbDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? 0 : Convert.ToInt64(reader.GetValue(ordinal));

        public async Task<IReadOnlyList<ColumnRunMetrics>> GetRecentColumnMetricsAsync(
            JobId jobIdRef, string? targetTable, string columnName, int limit = 100)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT m.TargetTable, m.ColumnName, m.TotalRows, m.NullRows, m.MaxTimestampUtc
                FROM JobColumnMetrics m
                INNER JOIN JobHistory h ON h.Id = m.JobHistoryId
                WHERE h.JobId = @job
                  AND h.EndTime IS NOT NULL
                  AND (UPPER(h.Status) = 'SUCCESS' OR UPPER(h.Status) = 'COMPLETED')
                  AND m.ColumnName = @column COLLATE NOCASE
                  AND (@target IS NULL OR m.TargetTable = @target COLLATE NOCASE)
                ORDER BY h.EndTime DESC, h.StartTime DESC, h.Id DESC
                LIMIT @limit;";
            command.AddParam("@job", jobIdRef.Require());
            command.AddParam("@column", columnName);
            command.AddParam("@target", string.IsNullOrWhiteSpace(targetTable) ? DBNull.Value : targetTable.Trim().TrimStart('#'));
            command.AddParam("@limit", limit);

            var results = new List<ColumnRunMetrics>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new ColumnRunMetrics(
                    reader.IsDBNull(0) || string.IsNullOrEmpty(reader.GetString(0)) ? null : reader.GetString(0),
                    reader.GetString(1),
                    Convert.ToInt64(reader.GetValue(2)),
                    Convert.ToInt64(reader.GetValue(3)),
                    reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4))));
            }
            return results;
        }

        public async Task<int> PruneHistoryAsync(TimeSpan maxAge)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            // StartTime is stored as a round-trip ("O") timestamp; a same-format cutoff compares
            // correctly. RUNNING rows (in-flight jobs) are preserved regardless of age.
            var cutoff = DateTime.Now.Subtract(maxAge).ToString("O");
            using (var deleteMetrics = connection.CreateCommand())
            {
                deleteMetrics.CommandText = @"
                    DELETE FROM JobColumnMetrics
                    WHERE JobHistoryId IN (
                        SELECT Id FROM JobHistory WHERE Status <> 'RUNNING' AND StartTime < @cutoff
                    );";
                deleteMetrics.AddParam("@cutoff", cutoff);
                await deleteMetrics.ExecuteNonQueryAsync();
            }

            using (var deleteFailures = connection.CreateCommand())
            {
                deleteFailures.CommandText = @"
                    DELETE FROM JobDataQualityFailures
                    WHERE JobHistoryId IN (
                        SELECT Id FROM JobHistory WHERE Status <> 'RUNNING' AND StartTime < @cutoff
                    );";
                deleteFailures.AddParam("@cutoff", cutoff);
                await deleteFailures.ExecuteNonQueryAsync();
            }

            using (var deleteStatements = connection.CreateCommand())
            {
                // Statement detail is the bulk of a run's rows, so it must go with the history row
                // it belongs to. Orphaning it here is how the flight recorder would grow without
                // bound on a 200-jobs-a-day estate.
                deleteStatements.CommandText = @"
                    DELETE FROM JobStatementMetrics
                    WHERE JobHistoryId IN (
                        SELECT Id FROM JobHistory WHERE Status <> 'RUNNING' AND StartTime < @cutoff
                    );";
                deleteStatements.AddParam("@cutoff", cutoff);
                await deleteStatements.ExecuteNonQueryAsync();
            }

            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM JobHistory WHERE Status <> 'RUNNING' AND StartTime < @cutoff;";
            command.AddParam("@cutoff", cutoff);
            return await command.ExecuteNonQueryAsync();
        }

        public async Task<int> ReconcileStaleRunningAsync(TimeSpan maxRuntime)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            var now = DateTime.Now;
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE JobHistory SET Status = 'INTERRUPTED', EndTime = @end, " +
                "ErrorMessage = 'No completion recorded within the maximum job runtime; the orchestrator likely restarted or the job was killed.' " +
                "WHERE Status = 'RUNNING' AND StartTime < @cutoff;";
            command.AddParam("@end", now.ToString("O"));
            command.AddParam("@cutoff", now.Subtract(maxRuntime).ToString("O"));
            return await command.ExecuteNonQueryAsync();
        }

        // ── Host utilization time series (IHostMetricsStore) ─────────────────────────
        // Timestamps are stored UTC round-trip ("O") so string comparison is instant-correct
        // regardless of the sample's DateTimeKind.

        public async Task AppendHostMetricAsync(HostMetricSample sample)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO HostMetrics (NodeId, CapturedAt, MemoryLoadPercent, ProcessCpuPercent, HostCpuPercent, StateDiskFreeBytes, SpillDiskFreeBytes) " +
                "VALUES (@node, @at, @mem, @cpu, @hostcpu, @state, @spill);";
            command.AddParam("@node", sample.NodeId);
            command.AddParam("@at", sample.CapturedAt.ToUniversalTime().ToString("O"));
            command.AddParam("@mem", sample.MemoryLoadPercent);
            command.AddParam("@cpu", sample.ProcessCpuPercent);
            command.AddParam("@hostcpu", (object?)sample.HostCpuPercent ?? DBNull.Value);
            command.AddParam("@state", sample.StateDiskFreeBytes);
            command.AddParam("@spill", sample.SpillDiskFreeBytes);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<IReadOnlyList<HostMetricSample>> GetHostMetricsAsync(string? nodeId, DateTime since, int limit = 1000)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            var sql = "SELECT NodeId, CapturedAt, MemoryLoadPercent, ProcessCpuPercent, HostCpuPercent, StateDiskFreeBytes, SpillDiskFreeBytes " +
                      "FROM HostMetrics WHERE CapturedAt >= @since ";
            if (!string.IsNullOrEmpty(nodeId)) { sql += "AND NodeId = @node "; command.AddParam("@node", nodeId); }
            sql += "ORDER BY CapturedAt DESC LIMIT @limit;";
            command.CommandText = sql;
            command.AddParam("@since", since.ToUniversalTime().ToString("O"));
            command.AddParam("@limit", limit);

            var results = new List<HostMetricSample>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new HostMetricSample(
                    reader.GetString(0),
                    DateTime.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    Convert.ToDouble(reader.GetValue(2)),
                    Convert.ToDouble(reader.GetValue(3)),
                    reader.IsDBNull(4) ? null : Convert.ToDouble(reader.GetValue(4)),
                    Convert.ToInt64(reader.GetValue(5)),
                    Convert.ToInt64(reader.GetValue(6))));
            }
            return results;
        }

        public async Task<int> PruneHostMetricsAsync(TimeSpan maxAge)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM HostMetrics WHERE CapturedAt < @cutoff;";
            command.AddParam("@cutoff", DateTime.UtcNow.Subtract(maxAge).ToString("O"));
            return await command.ExecuteNonQueryAsync();
        }

        // ── Daily roll-ups ───────────────────────────────────────────────────────────
        // Day = substr(timestamp, 1, 10) = 'yyyy-MM-dd' from the round-trip ("O") string, portable
        // across SQLite/Postgres. DELETE-then-INSERT the days still present in raw (transactional +
        // idempotent), leaving already-pruned days' summaries intact so trend outlives raw retention.

        public async Task<int> RollUpJobHistoryAsync()
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var tx = connection.BeginTransaction();

            using (var del = connection.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM JobHistoryDaily WHERE Day IN (SELECT DISTINCT substr(StartTime,1,10) FROM JobHistory WHERE Status <> 'RUNNING');";
                await del.ExecuteNonQueryAsync();
            }
            int written;
            using (var ins = connection.CreateCommand())
            {
                ins.Transaction = tx;
                // Grouped by identity, not name: two tenants may both have a job called 'nightly',
                // and summing them into one row would report each tenant the other's volume. The
                // name and tenant are carried along for display and for tenant-scoped deletion.
                // Ad-hoc script runs (JobId '') are excluded — they are not jobs, so they have no
                // per-job trend, and the table's key could not hold them anyway.
                ins.CommandText =
                    "INSERT INTO JobHistoryDaily (Day, JobId, TenantId, JobName, RunCount, FailureCount, TotalRows, MaxPeakMemoryBytes) " +
                    "SELECT substr(StartTime,1,10), JobId, MAX(TenantId), MAX(JobName), COUNT(*), " +
                    "SUM(CASE WHEN Status <> 'SUCCESS' THEN 1 ELSE 0 END), SUM(RowsProcessed), MAX(PeakMemoryBytes) " +
                    "FROM JobHistory WHERE Status <> 'RUNNING' AND JobId <> '' " +
                    "GROUP BY substr(StartTime,1,10), JobId;";
                written = await ins.ExecuteNonQueryAsync();
            }
            tx.Commit();
            return written;
        }

        public async Task<IReadOnlyList<JobHistoryDailySummary>> GetJobHistoryDailyAsync(JobId jobIdRef, DateTime sinceDay, int limit = 1000)
        {
            var jobId = jobIdRef.IsAssigned ? jobIdRef.Value : null;
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            var sql = "SELECT Day, JobName, RunCount, FailureCount, TotalRows, MaxPeakMemoryBytes FROM JobHistoryDaily WHERE Day >= @since ";
            if (!string.IsNullOrEmpty(jobId)) { sql += "AND JobId = @job "; command.AddParam("@job", jobId); }
            sql += "ORDER BY Day DESC, JobName LIMIT @limit;";
            command.CommandText = sql;
            command.AddParam("@since", sinceDay.ToString("yyyy-MM-dd"));
            command.AddParam("@limit", limit);

            var results = new List<JobHistoryDailySummary>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                results.Add(new JobHistoryDailySummary(reader.GetString(0), reader.GetString(1),
                    Convert.ToInt32(reader.GetValue(2)), Convert.ToInt32(reader.GetValue(3)),
                    Convert.ToInt64(reader.GetValue(4)), Convert.ToInt64(reader.GetValue(5))));
            return results;
        }

        public async Task<int> PruneJobHistoryDailyAsync(TimeSpan maxAge)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM JobHistoryDaily WHERE Day < @cutoff;";
            command.AddParam("@cutoff", DateTime.UtcNow.Subtract(maxAge).ToString("yyyy-MM-dd"));
            return await command.ExecuteNonQueryAsync();
        }

        public async Task<int> RollUpHostMetricsAsync()
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var tx = connection.BeginTransaction();

            using (var del = connection.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM HostMetricsDaily WHERE Day IN (SELECT DISTINCT substr(CapturedAt,1,10) FROM HostMetrics);";
                await del.ExecuteNonQueryAsync();
            }
            int written;
            using (var ins = connection.CreateCommand())
            {
                ins.Transaction = tx;
                // AVG/MAX ignore NULL HostCpuPercent samples (and yield NULL when no sample has one),
                // so days recorded before the whole-host probe shipped roll up as NULL, not 0.
                ins.CommandText =
                    "INSERT INTO HostMetricsDaily (Day, NodeId, AvgMemoryLoadPercent, MaxMemoryLoadPercent, AvgCpuPercent, MaxCpuPercent, MinStateDiskFreeBytes, MinSpillDiskFreeBytes, AvgHostCpuPercent, MaxHostCpuPercent) " +
                    "SELECT substr(CapturedAt,1,10), NodeId, AVG(MemoryLoadPercent), MAX(MemoryLoadPercent), " +
                    "AVG(ProcessCpuPercent), MAX(ProcessCpuPercent), MIN(StateDiskFreeBytes), MIN(SpillDiskFreeBytes), " +
                    "AVG(HostCpuPercent), MAX(HostCpuPercent) " +
                    "FROM HostMetrics GROUP BY substr(CapturedAt,1,10), NodeId;";
                written = await ins.ExecuteNonQueryAsync();
            }
            tx.Commit();
            return written;
        }

        public async Task<IReadOnlyList<HostMetricsDailySummary>> GetHostMetricsDailyAsync(string? nodeId, DateTime sinceDay, int limit = 1000)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            var sql = "SELECT Day, NodeId, AvgMemoryLoadPercent, MaxMemoryLoadPercent, AvgCpuPercent, MaxCpuPercent, MinStateDiskFreeBytes, MinSpillDiskFreeBytes, AvgHostCpuPercent, MaxHostCpuPercent FROM HostMetricsDaily WHERE Day >= @since ";
            if (!string.IsNullOrEmpty(nodeId)) { sql += "AND NodeId = @node "; command.AddParam("@node", nodeId); }
            sql += "ORDER BY Day DESC, NodeId LIMIT @limit;";
            command.CommandText = sql;
            command.AddParam("@since", sinceDay.ToString("yyyy-MM-dd"));
            command.AddParam("@limit", limit);

            var results = new List<HostMetricsDailySummary>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                results.Add(new HostMetricsDailySummary(reader.GetString(0), reader.GetString(1),
                    Convert.ToDouble(reader.GetValue(2)), Convert.ToDouble(reader.GetValue(3)),
                    Convert.ToDouble(reader.GetValue(4)), Convert.ToDouble(reader.GetValue(5)),
                    Convert.ToInt64(reader.GetValue(6)), Convert.ToInt64(reader.GetValue(7)),
                    reader.IsDBNull(8) ? null : Convert.ToDouble(reader.GetValue(8)),
                    reader.IsDBNull(9) ? null : Convert.ToDouble(reader.GetValue(9))));
            return results;
        }

        public async Task<int> PruneHostMetricsDailyAsync(TimeSpan maxAge)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM HostMetricsDaily WHERE Day < @cutoff;";
            command.AddParam("@cutoff", DateTime.UtcNow.Subtract(maxAge).ToString("yyyy-MM-dd"));
            return await command.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<JobHistoryEntry>> GetHistoryAsync(JobId jobIdRef = default, int limit = 100)
        {
            var jobId = jobIdRef.IsAssigned ? jobIdRef.Value : null;
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            var sql = "SELECT * FROM JobHistory ";
            if (jobId != null) sql += "WHERE JobId = @jobId ";
            sql += "ORDER BY StartTime DESC LIMIT @limit;";

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            if (jobId != null) command.AddParam("@jobId", jobId);
            command.AddParam("@limit", Math.Clamp(limit, 1, 1000));

            var entries = new List<JobHistoryEntry>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) entries.Add(ReadHistoryEntry(reader));
            return entries;
        }

        public async Task<IEnumerable<JobHistoryEntry>> GetHistoryForNameAsync(
            string? tenantId, string jobName, int limit = 100)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            // Deliberately matched on the recorded name, not on a resolved identity: a run's history
            // outlives the job, so this answers "what ran under this name here", which is the question
            // SHOW JOB HISTORY asks and which an identity lookup could not answer after a DROP. The
            // tenant predicate is what keeps it from reaching another tenant's job of the same name.
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT * FROM JobHistory WHERE TenantId = @tenant AND JobName = @name COLLATE NOCASE " +
                "ORDER BY StartTime DESC LIMIT @limit;";
            command.AddParam("@tenant", TenantKey(tenantId));
            command.AddParam("@name", jobName);
            command.AddParam("@limit", Math.Clamp(limit, 1, 1000));

            var entries = new List<JobHistoryEntry>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) entries.Add(ReadHistoryEntry(reader));
            return entries;
        }

        public async Task<JobHistoryEntry?> GetHistoryEntryAsync(long entryId)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM JobHistory WHERE Id = @historyId LIMIT 1;";
            command.AddParam("@historyId", entryId);
            using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync() ? ReadHistoryEntry(reader) : null;
        }

        public async Task<IEnumerable<JobHistoryEntry>> GetCompletedHistoryAsync(
            DateTime completedAfter, DateTime completedThrough, int limit = 1000, int offset = 0)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM JobHistory WHERE EndTime > @after AND EndTime <= @through " +
                "ORDER BY EndTime, Id LIMIT @limit OFFSET @offset;";
            // JobHistory is currently persisted with DateTime.Now, including the local UTC offset.
            // Compare using the same representation; parsing at the API boundary still returns
            // absolute instants. A future schema migration can normalize the column itself to UTC.
            command.AddParam("@after", completedAfter.ToLocalTime().ToString("O"));
            command.AddParam("@through", completedThrough.ToLocalTime().ToString("O"));
            command.AddParam("@limit", Math.Clamp(limit, 1, 1000));
            command.AddParam("@offset", Math.Max(0, offset));

            var entries = new List<JobHistoryEntry>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) entries.Add(ReadHistoryEntry(reader));
            return entries;
        }

        // Read by name, never by ordinal. These rows come back from `SELECT *`, so every position
        // depends on the column order of the table as created — and JobId and TenantId were inserted
        // near the front, while an additively migrated database appends them at the end instead.
        // Positional reads had silently shifted: JobName returned the job's id, and StartTime
        // returned the tenant, which surfaced only because an empty string will not parse as a date.
        private static JobHistoryEntry ReadHistoryEntry(DbDataReader reader) => new(
            reader.GetInt64(reader.GetOrdinal("Id")),
            reader.GetString(reader.GetOrdinal("JobName")),
            DateTime.Parse(reader.GetString(reader.GetOrdinal("StartTime"))),
            ReadOptionalDateTime(reader, "EndTime"),
            reader.GetString(reader.GetOrdinal("Status")),
            ReadOptionalString(reader, "ErrorMessage"),
            ReadOptionalInt64(reader, "RowsProcessed"),
            ReadOptionalInt64(reader, "PeakMemoryBytes"),
            ReadOptionalDouble(reader, "CpuTimeSeconds"),
            ReadOptionalString(reader, "ScriptHashAtRunTime"),
            ReadOptionalBool(reader, "HashMatched"),
            ReadOptionalInt64(reader, "RowsQuarantined"),
            ReadOptionalInt64(reader, "RowsWarned"),
            ReadOptionalString(reader, "DataQualityFailures"),
            ReadOptionalString(reader, "SessionId"),
            ReadOptionalString(reader, "CheckpointLabel"),
            JobId.From(ReadOptionalString(reader, "JobId")),
            ReadOptionalString(reader, "TenantId") is { Length: > 0 } tenant ? tenant : null);

        private static DateTime? ReadOptionalDateTime(DbDataReader reader, string columnName)
        {
            var text = ReadOptionalString(reader, columnName);
            return string.IsNullOrEmpty(text) ? null : DateTime.Parse(text);
        }

        private static double ReadOptionalDouble(DbDataReader reader, string columnName)
        {
            int ordinal = TryGetOrdinal(reader, columnName);
            return ordinal < 0 || reader.IsDBNull(ordinal) ? 0 : Convert.ToDouble(reader.GetValue(ordinal));
        }

        private static bool? ReadOptionalBool(DbDataReader reader, string columnName)
        {
            int ordinal = TryGetOrdinal(reader, columnName);
            return ordinal < 0 || reader.IsDBNull(ordinal)
                ? null
                : Convert.ToInt64(reader.GetValue(ordinal)) != 0;
        }

        private static long ReadOptionalInt64(DbDataReader reader, string columnName)
        {
            int ordinal = TryGetOrdinal(reader, columnName);
            return ordinal < 0 || reader.IsDBNull(ordinal) ? 0 : reader.GetInt64(ordinal);
        }

        private static string? ReadOptionalString(DbDataReader reader, string columnName)
        {
            int ordinal = TryGetOrdinal(reader, columnName);
            return ordinal < 0 || reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        private static int TryGetOrdinal(DbDataReader reader, string columnName)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        public Task<string?> GetJobStateAsync(JobId jobIdRef, string key) =>
            ReadStateAsync(jobIdRef.Require(), key);

        public async Task SetJobStateAsync(JobId jobIdRef, string key, string? value)
        {
            var jobId = jobIdRef.Require();
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            await WriteStateAsync(jobId, key, value);
        }

        /// <summary>
        /// Reserved namespace for host-scoped markers in the job-state table. A job identity is a GUID
        /// in "N" form, so a prefixed key can never collide with one.
        /// </summary>
        private static string HostStateKey(string scope) => "host:" + scope.Trim().ToLowerInvariant();

        public Task<string?> GetHostStateAsync(string scope, string key) =>
            ReadStateAsync(HostStateKey(scope), key);

        public Task SetHostStateAsync(string scope, string key, string? value) =>
            WriteStateAsync(HostStateKey(scope), key, value);

        private async Task<string?> ReadStateAsync(string owner, string key)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT StateValue FROM JobState WHERE JobId = @jobId AND StateKey = @key;";
            command.AddParam("@jobId", owner);
            command.AddParam("@key", key);

            var result = await command.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : (string?)result;
        }

        private async Task WriteStateAsync(string owner, string key, string? value)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO JobState (JobId, StateKey, StateValue, UpdatedAt)
                VALUES (@jobId, @key, @value, @updatedAt)
                ON CONFLICT (JobId, StateKey)
                DO UPDATE SET StateValue = EXCLUDED.StateValue, UpdatedAt = EXCLUDED.UpdatedAt;";

            command.AddParam("@jobId", owner);
            command.AddParam("@key", key);
            command.AddParam("@value", (object?)value ?? DBNull.Value);
            command.AddParam("@updatedAt", DateTime.UtcNow.ToString("o"));

            await command.ExecuteNonQueryAsync();
        }

        public async Task<IReadOnlyList<JobStateEntry>> GetJobStatesAsync(JobId jobIdRef = default, int limit = 1000)
        {
            var jobId = jobIdRef.IsAssigned ? jobIdRef.Value : null;
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            // The row is keyed by identity but this read model is what an operator sees, so it joins
            // back to the job for the name they typed. Returning the id in the name's place would put
            // an opaque GUID in front of every `SHOW JOB STATE` reader. The join is inner: state whose
            // job is gone is not addressable state, and state is deleted with its job in any case.
            var sql = @"
                SELECT j.Name, s.StateKey, s.StateValue, s.UpdatedAt
                FROM JobState s
                INNER JOIN Jobs j ON j.Id = s.JobId ";
            if (!string.IsNullOrEmpty(jobId)) { sql += "WHERE s.JobId = @job "; command.AddParam("@job", jobId); }
            sql += "ORDER BY j.Name, s.StateKey LIMIT @limit;";
            command.CommandText = sql;
            command.AddParam("@limit", limit);

            var results = new List<JobStateEntry>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                results.Add(new JobStateEntry(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    DateTime.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind)));
            return results;
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

                using (var gcFiles = connection.CreateCommand())
                {
                    gcFiles.Transaction = transaction;
                    gcFiles.CommandText = @"
                        DELETE FROM BundleFiles
                        WHERE BundleName = @bundle
                          AND Version NOT IN (
                              SELECT Version FROM BundleVersions
                              WHERE BundleName = @bundle
                              ORDER BY Version DESC
                              LIMIT 5
                          );";
                    gcFiles.AddParam("@bundle", request.BundleName);
                    await gcFiles.ExecuteNonQueryAsync();
                }

                using (var gcDeps = connection.CreateCommand())
                {
                    gcDeps.Transaction = transaction;
                    gcDeps.CommandText = @"
                        DELETE FROM BundleDependencies
                        WHERE BundleName = @bundle
                          AND Version NOT IN (
                              SELECT Version FROM BundleVersions
                              WHERE BundleName = @bundle
                              ORDER BY Version DESC
                              LIMIT 5
                          );";
                    gcDeps.AddParam("@bundle", request.BundleName);
                    await gcDeps.ExecuteNonQueryAsync();
                }

                using (var gcVersions = connection.CreateCommand())
                {
                    gcVersions.Transaction = transaction;
                    gcVersions.CommandText = @"
                        DELETE FROM BundleVersions
                        WHERE BundleName = @bundle
                          AND Version NOT IN (
                              SELECT Version FROM BundleVersions
                              WHERE BundleName = @bundle
                              ORDER BY Version DESC
                              LIMIT 5
                          );";
                    gcVersions.AddParam("@bundle", request.BundleName);
                    await gcVersions.ExecuteNonQueryAsync();
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
            => await SaveLineageCoreAsync("portal-host", entries, jobName, scriptPath, runAt);

        public async Task SaveLineageAsync(
            TenantContext tenant,
            IEnumerable<LineageEntry> entries,
            string? jobName,
            string? scriptPath,
            DateTime runAt)
        {
            RequireRuntimeTenant(tenant);
            await SaveLineageCoreAsync(tenant.Tenant.Value, entries, jobName, scriptPath, runAt);
        }

        private async Task SaveLineageCoreAsync(
            string tenantId,
            IEnumerable<LineageEntry> entries,
            string? jobName,
            string? scriptPath,
            DateTime runAt)
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
                            (TenantId, RunAt, JobName, ScriptPath, TargetTable, TargetColumn, SourceTables, SourceColumns, Operation, Tags, SourceFile, Line,
                             TransformationKind, TransformationExpression, FunctionsApplied, DerivedFromDescriptions)
                        VALUES
                            (@tenant, @runAt, @job, @script, @target, @col, @sources, @srcCols, @op, @tags, @file, @line,
                             @tkind, @texpr, @fns, @derived);";
                    cmd.AddParam("@tenant", tenantId);
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
            => await GetHistoryForTableCoreAsync(null, tableName, limit);

        public async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTableAsync(
            TenantContext tenant, string tableName, int limit = 100)
        {
            RequireRuntimeTenant(tenant);
            return await GetHistoryForTableCoreAsync(tenant.Tenant.Value, tableName, limit);
        }

        private async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTableCoreAsync(
            string? tenantId, string tableName, int limit)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, RunAt, JobName, ScriptPath, TargetTable, TargetColumn,
                       SourceTables, Operation, Tags, SourceFile, Line,
                       SourceColumns, TransformationKind, TransformationExpression, FunctionsApplied, DerivedFromDescriptions, TenantId
                FROM LineageHistory
                WHERE (@tenant IS NULL OR TenantId = @tenant) AND TargetTable = @table COLLATE NOCASE
                ORDER BY RunAt DESC, Id DESC
                LIMIT @limit;";
            cmd.AddParam("@table", tableName);
            cmd.AddParam("@tenant", (object?)tenantId ?? DBNull.Value);
            cmd.AddParam("@limit", limit);
            return await ReadLineageHistoryAsync(cmd);
        }

        public async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTablesAsync(
            IReadOnlyCollection<string> tableNames, int limitPerTable = 100)
            => await GetHistoryForTablesCoreAsync(null, tableNames, limitPerTable);

        public async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTablesAsync(
            TenantContext tenant, IReadOnlyCollection<string> tableNames, int limitPerTable = 100)
        {
            RequireRuntimeTenant(tenant);
            return await GetHistoryForTablesCoreAsync(tenant.Tenant.Value, tableNames, limitPerTable);
        }

        private async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTablesCoreAsync(
            string? tenantId, IReadOnlyCollection<string> tableNames, int limitPerTable)
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
            cmd.AddParam("@tenant", (object?)tenantId ?? DBNull.Value);
            cmd.CommandText = $@"
                SELECT Id, RunAt, JobName, ScriptPath, TargetTable, TargetColumn,
                       SourceTables, Operation, Tags, SourceFile, Line,
                       SourceColumns, TransformationKind, TransformationExpression, FunctionsApplied, DerivedFromDescriptions, TenantId
                FROM (
                    SELECT *, ROW_NUMBER() OVER (
                        PARTITION BY TargetTable COLLATE NOCASE
                        ORDER BY RunAt DESC, Id DESC) AS _rn
                    FROM LineageHistory
                    WHERE (@tenant IS NULL OR TenantId = @tenant)
                      AND TargetTable COLLATE NOCASE IN ({string.Join(", ", paramNames)})
                )
                WHERE _rn <= @limit
                ORDER BY RunAt DESC, Id DESC;";
            return await ReadLineageHistoryAsync(cmd);
        }

        public async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTagAsync(string tagKey, string? tagValue = null, int limit = 100)
            => await GetHistoryForTagCoreAsync(null, tagKey, tagValue, limit);

        public async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTagAsync(
            TenantContext tenant, string tagKey, string? tagValue = null, int limit = 100)
        {
            RequireRuntimeTenant(tenant);
            return await GetHistoryForTagCoreAsync(tenant.Tenant.Value, tagKey, tagValue, limit);
        }

        private async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForTagCoreAsync(
            string? tenantId, string tagKey, string? tagValue, int limit)
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
                       SourceColumns, TransformationKind, TransformationExpression, FunctionsApplied, DerivedFromDescriptions, TenantId
                FROM LineageHistory
                WHERE (@tenant IS NULL OR TenantId = @tenant) AND Tags LIKE @pattern
                ORDER BY RunAt DESC, Id DESC
                LIMIT @limit;";
            cmd.AddParam("@pattern", pattern);
            cmd.AddParam("@tenant", (object?)tenantId ?? DBNull.Value);
            cmd.AddParam("@limit", limit);
            return await ReadLineageHistoryAsync(cmd);
        }

        public async Task<IEnumerable<LineageMissingMetadataEntry>> GetMissingMetadataAsync(
            IReadOnlyCollection<string> requiredTags,
            int limit = 100)
            => await GetMissingMetadataCoreAsync(null, requiredTags, limit);

        public async Task<IEnumerable<LineageMissingMetadataEntry>> GetMissingMetadataAsync(
            TenantContext tenant,
            IReadOnlyCollection<string> requiredTags,
            int limit = 100)
        {
            RequireRuntimeTenant(tenant);
            return await GetMissingMetadataCoreAsync(tenant.Tenant.Value, requiredTags, limit);
        }

        private async Task<IEnumerable<LineageMissingMetadataEntry>> GetMissingMetadataCoreAsync(
            string? tenantId,
            IReadOnlyCollection<string> requiredTags,
            int limit)
        {
            if (requiredTags.Count == 0) return Array.Empty<LineageMissingMetadataEntry>();

            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, RunAt, JobName, ScriptPath, TargetTable, TargetColumn,
                       SourceTables, Operation, Tags, SourceFile, Line,
                       SourceColumns, TransformationKind, TransformationExpression, FunctionsApplied, DerivedFromDescriptions, TenantId
                FROM LineageHistory
                WHERE (@tenant IS NULL OR TenantId = @tenant)
                ORDER BY RunAt DESC, Id DESC
                LIMIT @scanLimit;";
            cmd.AddParam("@scanLimit", Math.Max(limit * 20, limit));
            cmd.AddParam("@tenant", (object?)tenantId ?? DBNull.Value);

            var latestByTarget = (await ReadLineageHistoryAsync(cmd))
                .GroupBy(
                    e => $"{e.TargetTable}\u001f{e.TargetColumn ?? string.Empty}",
                    StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First());

            var required = requiredTags
                .Select(ETL_SQL.Common.StewardshipTagCatalog.Canonicalize)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return latestByTarget
                .Select(e =>
                {
                    var tags = new Dictionary<string, string>(e.Tags, StringComparer.OrdinalIgnoreCase);
                    var missing = required
                        .Where(tag => !tags.ContainsKey(tag))
                        .ToList();
                    return (entry: e, missing, tags);
                })
                .Where(x => x.missing.Count > 0)
                .Take(limit)
                .Select(x => new LineageMissingMetadataEntry(
                    x.entry.TargetTable,
                    x.entry.TargetColumn,
                    x.missing,
                    x.tags,
                    x.entry.RunAt,
                    x.entry.JobName,
                    x.entry.ScriptPath))
                .ToList();
        }

        public async Task<IEnumerable<LineageHistoryEntry>> GetRecentLineageAsync(int limit = 1000)
            => await GetRecentLineageCoreAsync(null, limit);

        public async Task<IEnumerable<LineageHistoryEntry>> GetRecentLineageAsync(
            TenantContext tenant, int limit = 1000)
        {
            RequireRuntimeTenant(tenant);
            return await GetRecentLineageCoreAsync(tenant.Tenant.Value, limit);
        }

        private async Task<IEnumerable<LineageHistoryEntry>> GetRecentLineageCoreAsync(
            string? tenantId, int limit)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, RunAt, JobName, ScriptPath, TargetTable, TargetColumn,
                       SourceTables, Operation, Tags, SourceFile, Line,
                       SourceColumns, TransformationKind, TransformationExpression, FunctionsApplied, DerivedFromDescriptions, TenantId
                FROM LineageHistory
                WHERE (@tenant IS NULL OR TenantId = @tenant)
                ORDER BY RunAt DESC, Id DESC
                LIMIT @limit;";
            cmd.AddParam("@limit", Math.Clamp(limit, 1, 10000));
            cmd.AddParam("@tenant", (object?)tenantId ?? DBNull.Value);
            return await ReadLineageHistoryAsync(cmd);
        }

        public async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForJobAsync(string jobName, int limit = 100)
            => await GetHistoryForJobCoreAsync(null, jobName, limit);

        public async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForJobAsync(
            TenantContext tenant, string jobName, int limit = 100)
        {
            RequireRuntimeTenant(tenant);
            return await GetHistoryForJobCoreAsync(tenant.Tenant.Value, jobName, limit);
        }

        private async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForJobCoreAsync(
            string? tenantId, string jobName, int limit)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, RunAt, JobName, ScriptPath, TargetTable, TargetColumn,
                       SourceTables, Operation, Tags, SourceFile, Line,
                       SourceColumns, TransformationKind, TransformationExpression, FunctionsApplied, DerivedFromDescriptions, TenantId
                FROM LineageHistory
                WHERE (@tenant IS NULL OR TenantId = @tenant) AND JobName = @jobName COLLATE NOCASE
                ORDER BY RunAt DESC, Id DESC
                LIMIT @limit;";
            cmd.AddParam("@jobName", jobName);
            cmd.AddParam("@tenant", (object?)tenantId ?? DBNull.Value);
            cmd.AddParam("@limit", limit);
            return await ReadLineageHistoryAsync(cmd);
        }

        public async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForSourceAsync(string sourceName, int limit = 100)
            => await GetHistoryForSourceCoreAsync(null, sourceName, limit);

        public async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForSourceAsync(
            TenantContext tenant, string sourceName, int limit = 100)
        {
            RequireRuntimeTenant(tenant);
            return await GetHistoryForSourceCoreAsync(tenant.Tenant.Value, sourceName, limit);
        }

        private async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForSourceCoreAsync(
            string? tenantId, string sourceName, int limit)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, RunAt, JobName, ScriptPath, TargetTable, TargetColumn,
                       SourceTables, Operation, Tags, SourceFile, Line,
                       SourceColumns, TransformationKind, TransformationExpression, FunctionsApplied, DerivedFromDescriptions, TenantId
                FROM LineageHistory
                WHERE (@tenant IS NULL OR TenantId = @tenant) AND SourceTables LIKE @pattern
                ORDER BY RunAt DESC, Id DESC
                LIMIT @scanLimit;";
            cmd.AddParam("@pattern", $"%\"{sourceName}\"%");
            cmd.AddParam("@tenant", (object?)tenantId ?? DBNull.Value);
            cmd.AddParam("@scanLimit", Math.Max(limit * 5, limit));

            return (await ReadLineageHistoryAsync(cmd))
                .Where(e => e.SourceTables.Any(s => string.Equals(s, sourceName, StringComparison.OrdinalIgnoreCase)))
                .Take(limit)
                .ToList();
        }

        public async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForSourceFileAsync(string sourceFile, int limit = 100)
            => await GetHistoryForSourceFileCoreAsync(null, sourceFile, limit);

        public async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForSourceFileAsync(
            TenantContext tenant, string sourceFile, int limit = 100)
        {
            RequireRuntimeTenant(tenant);
            return await GetHistoryForSourceFileCoreAsync(tenant.Tenant.Value, sourceFile, limit);
        }

        private async Task<IEnumerable<LineageHistoryEntry>> GetHistoryForSourceFileCoreAsync(
            string? tenantId, string sourceFile, int limit)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, RunAt, JobName, ScriptPath, TargetTable, TargetColumn,
                       SourceTables, Operation, Tags, SourceFile, Line,
                       SourceColumns, TransformationKind, TransformationExpression, FunctionsApplied, DerivedFromDescriptions, TenantId
                FROM LineageHistory
                WHERE (@tenant IS NULL OR TenantId = @tenant)
                  AND (SourceFile = @sourceFile COLLATE NOCASE
                   OR ScriptPath = @sourceFile COLLATE NOCASE)
                ORDER BY RunAt DESC, Id DESC
                LIMIT @limit;";
            cmd.AddParam("@sourceFile", sourceFile);
            cmd.AddParam("@tenant", (object?)tenantId ?? DBNull.Value);
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
                    reader.IsDBNull(15) ? null : reader.GetString(15),
                    reader.GetString(16)
                ));
            }
            return results;
        }

        private static void RequireRuntimeTenant(TenantContext tenant)
        {
            ArgumentNullException.ThrowIfNull(tenant);
            if (tenant.Origin is not (TenantContextOrigin.HostFixed or TenantContextOrigin.VerifiedCredential))
                throw new UnauthorizedAccessException(
                    "Lineage access requires host-fixed or verified-credential tenant authority.");
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
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(baseDir))
                baseDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(baseDir))
                baseDir = Path.GetTempPath();

            var dir = Path.Combine(baseDir, "etlsql-data");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "etlsql.db");
        }
    }
}
