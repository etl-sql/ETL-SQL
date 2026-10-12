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
    public partial class RelationalJobHistoryStore : IJobHistoryStore, ITenantJobEvidenceStore, IJobScheduleQueryStore, IJobCatalogStore, IOrchestratorAuthorizationStore, IBundleStore, ILineageCatalogStore, ITenantLineageCatalogStore, INodeRegistryStore, IWriteEpochStore, IClusterLockStore, IHostMetricsStore, ISharedTenantLifecycleStore, ETL_SQL.Core.Portability.ITenantCutoverStateStore
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

        private static readonly (string OldName, string NewName)[] LegacyTables =
        [
            ("Jobs", "jobs"),
            ("Schedules", "schedules"),
            ("Notifications", "notifications"),
            ("JobSchedules", "job_schedules"),
            ("JobNotifications", "job_notifications"),
            ("OrchestratorObjectAcls", "orchestrator_object_acls"),
            ("JobHistory", "job_history"),
            ("JobColumnMetrics", "job_column_metrics"),
            ("JobDataQualityFailures", "job_data_quality_failures"),
            ("JobStatementMetrics", "job_statement_metrics"),
            ("TenantUsageRecords", "tenant_usage_records"),
            ("SharedTenantControlPlanes", "shared_tenant_control_planes"),
            ("SharedTenantLifecycleOperations", "shared_tenant_lifecycle_operations"),
            ("SharedTenantLifecycleFencedJobs", "shared_tenant_lifecycle_fenced_jobs"),
            ("TenantPortabilityCutovers", "tenant_portability_cutovers"),
            ("TenantPortabilityFencedJobs", "tenant_portability_fenced_jobs"),
            ("BundleVersions", "bundle_versions"),
            ("BundleFiles", "bundle_files"),
            ("BundleDependencies", "bundle_dependencies"),
            ("LineageHistory", "lineage_history"),
            ("Nodes", "nodes"),
            ("WriteEpochs", "write_epochs"),
            ("ClusterLocks", "cluster_locks"),
            ("JobState", "job_state"),
            ("HostMetrics", "host_metrics"),
            ("JobHistoryDaily", "job_history_daily"),
            ("HostMetricsDaily", "host_metrics_daily"),
        ];

        private async Task MigrateLegacySchemaAsync(DbConnection connection)
        {
            foreach (var (oldName, newName) in LegacyTables)
            {
                await OrchestratorSchemaMigration.MigrateLegacyTableAsync(_dialect, connection, oldName, newName);
            }
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
                CREATE TABLE IF NOT EXISTS jobs (
                    id TEXT NOT NULL PRIMARY KEY,
                    name TEXT COLLATE NOCASE NOT NULL,
                    script TEXT NOT NULL,
                    interval INTEGER NOT NULL,
                    unit TEXT NOT NULL,
                    at_time TEXT,
                    last_run TEXT,
                    next_run TEXT,
                    is_enabled INTEGER NOT NULL DEFAULT 1,
                    max_retries INTEGER NOT NULL DEFAULT 0,
                    retry_delay_seconds INTEGER NOT NULL DEFAULT 30,
                    version INTEGER NOT NULL DEFAULT 1,
                    lease_fence_token INTEGER NOT NULL DEFAULT 0,
                    run_state TEXT NOT NULL DEFAULT 'Idle',
                    job_type TEXT NOT NULL DEFAULT 'Script',
                    target_path TEXT,
                    display_name TEXT,
                    description TEXT,
                    options TEXT,
                    created_by TEXT,
                    modified_by TEXT,
                    tenant_id TEXT NOT NULL DEFAULT '',
                    UNIQUE (tenant_id, name)
                );";

                    // Schedules, notifications, and their attachments to jobs. The Orchestrator is the
                    // system of record for all three: it runs the jobs, so it holds the trigger,
                    // computes the next run, and dispatches the outcome.
                    var createCatalogTables = @"
                CREATE TABLE IF NOT EXISTS schedules (
                    id TEXT NOT NULL PRIMARY KEY,
                    name TEXT COLLATE NOCASE NOT NULL,
                    cron TEXT NOT NULL,
                    time_zone TEXT NOT NULL DEFAULT 'UTC',
                    is_enabled INTEGER NOT NULL DEFAULT 1,
                    display_name TEXT,
                    description TEXT,
                    options TEXT,
                    created_by TEXT,
                    modified_by TEXT,
                    version INTEGER NOT NULL DEFAULT 1,
                    tenant_id TEXT NOT NULL DEFAULT '',
                    UNIQUE (tenant_id, name)
                );
                CREATE TABLE IF NOT EXISTS notifications (
                    id TEXT NOT NULL PRIMARY KEY,
                    name TEXT COLLATE NOCASE NOT NULL,
                    connection_name TEXT NOT NULL,
                    recipient TEXT,
                    is_enabled INTEGER NOT NULL DEFAULT 1,
                    display_name TEXT,
                    description TEXT,
                    options TEXT,
                    created_by TEXT,
                    modified_by TEXT,
                    version INTEGER NOT NULL DEFAULT 1,
                    tenant_id TEXT NOT NULL DEFAULT '',
                    UNIQUE (tenant_id, name)
                );
                CREATE TABLE IF NOT EXISTS job_schedules (
                    job_id TEXT NOT NULL,
                    schedule_id TEXT NOT NULL,
                    last_run TEXT,
                    next_run TEXT,
                    PRIMARY KEY (job_id, schedule_id)
                );
                CREATE INDEX IF NOT EXISTS idx_js_schedule ON job_schedules(schedule_id);
                CREATE TABLE IF NOT EXISTS job_notifications (
                    job_id TEXT NOT NULL,
                    notification_id TEXT NOT NULL,
                    trigger_condition TEXT NOT NULL,
                    PRIMARY KEY (job_id, notification_id, trigger_condition)
                );
                CREATE INDEX IF NOT EXISTS idx_jn_notification ON job_notifications(notification_id);";

                    // Grants hang off the object's surrogate Id, which is what makes them tenant-safe:
                    // resolving a name to an Id already required the caller's tenant, so a grant can
                    // never be read across a tenant boundary, and dropping an object retires its Id so
                    // a later object of the same name starts with no grants at all. ObjectKind is
                    // retained for audit and administration listings, not for lookup.
                    var createObjectAclTable = @"
                CREATE TABLE IF NOT EXISTS orchestrator_object_acls (
                    object_id TEXT NOT NULL,
                    object_kind TEXT NOT NULL,
                    principal_kind TEXT NOT NULL,
                    principal_id TEXT NOT NULL,
                    permission TEXT NOT NULL,
                    granted_by TEXT NOT NULL,
                    version INTEGER NOT NULL DEFAULT 1,
                    PRIMARY KEY (object_id, principal_kind, principal_id)
                );
                CREATE INDEX IF NOT EXISTS idx_ooa_principal
                    ON orchestrator_object_acls(principal_kind, principal_id, object_kind);";

                    // JobId scopes the row; JobName is retained denormalized so history stays readable
                    // after its job is dropped, and so a run's name at the time it ran survives a later
                    // object of the same name. TenantId is carried directly rather than joined through
                    // Jobs because retention prunes history long after the job may be gone.
                    var createHistoryTable = @"
                CREATE TABLE IF NOT EXISTS job_history (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    job_id TEXT NOT NULL DEFAULT '',
                    tenant_id TEXT NOT NULL DEFAULT '',
                    job_name TEXT NOT NULL,
                    start_time TEXT NOT NULL,
                    end_time TEXT,
                    status TEXT NOT NULL,
                    error_message TEXT,
                    rows_processed INTEGER DEFAULT 0
                );";
                    // The index over (TenantId, JobId) is created after the column migrations rather
                    // than here: CREATE TABLE IF NOT EXISTS leaves an existing table alone, so on a
                    // database written by an earlier build those two columns do not exist yet and
                    // indexing them fails before the migration that adds them has run.

                    var createColumnMetricsTable = @"
                CREATE TABLE IF NOT EXISTS job_column_metrics (
                    job_history_id INTEGER NOT NULL,
                    tenant_id TEXT NOT NULL DEFAULT '',
                    target_table TEXT NOT NULL DEFAULT '',
                    column_name TEXT NOT NULL,
                    total_rows INTEGER NOT NULL,
                    null_rows INTEGER NOT NULL,
                    max_timestamp_utc TEXT,
                    PRIMARY KEY (job_history_id, target_table, column_name)
                );";

                    var createDataQualityFailuresTable = @"
                CREATE TABLE IF NOT EXISTS job_data_quality_failures (
                    job_history_id INTEGER NOT NULL,
                    tenant_id TEXT NOT NULL DEFAULT '',
                    target_table TEXT NOT NULL DEFAULT '',
                    column_name TEXT NOT NULL,
                    rule_text TEXT NOT NULL,
                    action TEXT NOT NULL,
                    failure_count INTEGER NOT NULL,
                    owner TEXT,
                    PRIMARY KEY (job_history_id, target_table, column_name, rule_text, action)
                );
                CREATE INDEX IF NOT EXISTS idx_dqf_history ON job_data_quality_failures(job_history_id);";

                    var createStatementMetricsTable = @"
                CREATE TABLE IF NOT EXISTS job_statement_metrics (
                    job_history_id INTEGER NOT NULL,
                    tenant_id TEXT NOT NULL DEFAULT '',
                    ordinal INTEGER NOT NULL,
                    statement TEXT NOT NULL,
                    duration_ms INTEGER NOT NULL,
                    rows_processed INTEGER NOT NULL,
                    cpu_time_ms INTEGER NOT NULL,
                    spilled_bytes INTEGER NOT NULL,
                    spill_read_bytes INTEGER NOT NULL,
                    partitions INTEGER NOT NULL,
                    queue_wait_ms INTEGER NOT NULL,
                    lock_wait_ms INTEGER NOT NULL,
                    index_used TEXT,
                    dq_rows_validated INTEGER NOT NULL,
                    dq_rows_quarantined INTEGER NOT NULL,
                    dq_rows_warned INTEGER NOT NULL,
                    dq_validation_ms INTEGER NOT NULL,
                    failed INTEGER NOT NULL,
                    PRIMARY KEY (job_history_id, ordinal)
                );
                CREATE INDEX IF NOT EXISTS idx_jsm_history ON job_statement_metrics(job_history_id);";

                    var createTenantUsageTable = @"
                CREATE TABLE IF NOT EXISTS tenant_usage_records (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    tenant_id TEXT NOT NULL,
                    job_history_id INTEGER NOT NULL,
                    workload_kind TEXT NOT NULL,
                    status TEXT NOT NULL,
                    rows_processed INTEGER NOT NULL,
                    peak_memory_bytes INTEGER NOT NULL,
                    cpu_time_seconds REAL NOT NULL,
                    duration_ms INTEGER NOT NULL,
                    recorded_at_utc TEXT NOT NULL,
                    UNIQUE (tenant_id, job_history_id)
                );
                CREATE INDEX IF NOT EXISTS idx_tur_tenant_time
                    ON tenant_usage_records(tenant_id, recorded_at_utc DESC);";

                    var createSharedTenantLifecycleTables = @"
                CREATE TABLE IF NOT EXISTS shared_tenant_control_planes (
                    tenant_id TEXT PRIMARY KEY,
                    state TEXT NOT NULL,
                    active_release TEXT NOT NULL,
                    max_concurrent_jobs INTEGER NOT NULL,
                    max_storage_mb INTEGER NOT NULL,
                    max_report_sessions INTEGER NOT NULL,
                    fence_epoch INTEGER NOT NULL DEFAULT 1,
                    created_at_utc TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    deleted_at_utc TEXT,
                    version INTEGER NOT NULL DEFAULT 1
                );
                CREATE INDEX IF NOT EXISTS idx_stcp_state
                    ON shared_tenant_control_planes(state);
                CREATE TABLE IF NOT EXISTS shared_tenant_lifecycle_operations (
                    operation_id TEXT PRIMARY KEY,
                    tenant_id TEXT NOT NULL,
                    kind TEXT NOT NULL,
                    status TEXT NOT NULL,
                    platform_operator TEXT NOT NULL,
                    authorization_reference TEXT NOT NULL,
                    target_release TEXT,
                    target_max_concurrent_jobs INTEGER,
                    target_max_storage_mb INTEGER,
                    target_max_report_sessions INTEGER,
                    started_at_utc TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    completed_at_utc TEXT,
                    UNIQUE (kind, authorization_reference)
                );
                CREATE INDEX IF NOT EXISTS idx_stlo_tenant_status
                    ON shared_tenant_lifecycle_operations(tenant_id, status);
                CREATE TABLE IF NOT EXISTS shared_tenant_lifecycle_fenced_jobs (
                    operation_id TEXT NOT NULL,
                    tenant_id TEXT NOT NULL,
                    job_name TEXT COLLATE NOCASE NOT NULL,
                    PRIMARY KEY (operation_id, job_name)
                );
                CREATE TABLE IF NOT EXISTS tenant_portability_cutovers (
                    tenant_id TEXT PRIMARY KEY,
                    operation_id TEXT NOT NULL,
                    authority TEXT NOT NULL,
                    fence_epoch INTEGER NOT NULL,
                    source_schedules_enabled INTEGER NOT NULL,
                    target_schedules_enabled INTEGER NOT NULL,
                    source_active_executions INTEGER NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    version INTEGER NOT NULL DEFAULT 1
                );
                CREATE TABLE IF NOT EXISTS tenant_portability_fenced_jobs (
                    tenant_id TEXT NOT NULL,
                    operation_id TEXT NOT NULL,
                    job_name TEXT COLLATE NOCASE NOT NULL,
                    PRIMARY KEY (tenant_id, operation_id, job_name)
                );";

                    // Bundle identity is (TenantId, BundleName, Version). Without the tenant, one
                    // tenant's publish becomes another tenant's `bundle://` resolution and its pinned
                    // latest version — a supply-chain crossing, not merely a disclosure.
                    var createBundleTables = @"
                CREATE TABLE IF NOT EXISTS bundle_versions (
                    tenant_id TEXT NOT NULL DEFAULT '',
                    bundle_name TEXT NOT NULL,
                    version INTEGER NOT NULL,
                    entry_path TEXT NOT NULL,
                    content_hash TEXT NOT NULL,
                    published_at TEXT NOT NULL,
                    publisher TEXT,
                    description TEXT,
                    encryption_mode TEXT NOT NULL DEFAULT 'MACHINE',
                    encryption_metadata TEXT,
                    PRIMARY KEY (tenant_id, bundle_name, version)
                );

                CREATE TABLE IF NOT EXISTS bundle_files (
                    tenant_id TEXT NOT NULL DEFAULT '',
                    bundle_name TEXT NOT NULL,
                    version INTEGER NOT NULL,
                    virtual_path TEXT NOT NULL,
                    content TEXT NOT NULL,
                    content_hash TEXT NOT NULL,
                    size_bytes INTEGER NOT NULL,
                    content_type TEXT NOT NULL,
                    PRIMARY KEY (tenant_id, bundle_name, version, virtual_path),
                    FOREIGN KEY (tenant_id, bundle_name, version) REFERENCES bundle_versions(tenant_id, bundle_name, version)
                );

                CREATE TABLE IF NOT EXISTS bundle_dependencies (
                    tenant_id TEXT NOT NULL DEFAULT '',
                    bundle_name TEXT NOT NULL,
                    version INTEGER NOT NULL,
                    from_path TEXT NOT NULL,
                    to_path TEXT NOT NULL,
                    PRIMARY KEY (tenant_id, bundle_name, version, from_path, to_path),
                    FOREIGN KEY (tenant_id, bundle_name, version) REFERENCES bundle_versions(tenant_id, bundle_name, version)
                );";

                    var createLineageHistoryTable = @"
                CREATE TABLE IF NOT EXISTS lineage_history (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    tenant_id TEXT NOT NULL DEFAULT 'portal-host',
                    run_at TEXT NOT NULL,
                    job_name TEXT,
                    script_path TEXT,
                    target_table TEXT NOT NULL,
                    target_column TEXT,
                    source_tables TEXT NOT NULL DEFAULT '[]',
                    source_columns TEXT NOT NULL DEFAULT '[]',
                    operation TEXT NOT NULL,
                    tags TEXT NOT NULL DEFAULT '{}',
                    source_file TEXT,
                    line INTEGER NOT NULL DEFAULT 0,
                    transformation_kind TEXT,
                    transformation_expression TEXT,
                    functions_applied TEXT NOT NULL DEFAULT '[]',
                    derived_from_descriptions TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_jobs_sched ON jobs(is_enabled, next_run);
                CREATE INDEX IF NOT EXISTS idx_jh_job_start ON job_history(job_name, start_time);
                CREATE INDEX IF NOT EXISTS idx_jh_start ON job_history(start_time);
                CREATE INDEX IF NOT EXISTS idx_jh_end ON job_history(end_time);";

                    // Cluster node registry (P1.7): one row per live Portal/Orchestrator process, kept fresh
                    // by a TTL heartbeat. NodeId is a process-unique generated id, so no NOCASE is needed.
                    var createNodesTable = @"
                CREATE TABLE IF NOT EXISTS nodes (
                    node_id TEXT PRIMARY KEY,
                    role TEXT NOT NULL,
                    first_seen_at TEXT NOT NULL,
                    last_heartbeat_at TEXT NOT NULL,
                    expires_at TEXT NOT NULL,
                    metadata TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_nodes_expires ON nodes(expires_at);";

                    // Write-epoch fencing for shared storage (P1.8): the highest fence token that has written
                    // each (Scope, EpochKey) resource, so a stale writer cannot overwrite a newer one.
                    var createWriteEpochsTable = @"
                CREATE TABLE IF NOT EXISTS write_epochs (
                    scope TEXT NOT NULL,
                    epoch_key TEXT NOT NULL,
                    epoch INTEGER NOT NULL,
                    PRIMARY KEY (scope, epoch_key)
                );";

                    // Distributed locks / leader election (P1.9): one TTL-leased holder per named lock. Lives
                    // here (a CREATE-IF-NOT-EXISTS store) rather than the EF-migrated catalog so it exists
                    // before any node runs migrations.
                    var createClusterLocksTable = @"
                CREATE TABLE IF NOT EXISTS cluster_locks (
                    lock_name TEXT PRIMARY KEY,
                    owner TEXT NOT NULL,
                    expires_at TEXT NOT NULL
                );";

                    // Watermarks live here. Keyed on the job's surrogate Id rather than its name: two
                    // tenants running a job of the same name sharing one high-water mark would silently
                    // load the wrong rows, which nothing would report.
                    var createJobStateTable = @"
                CREATE TABLE IF NOT EXISTS job_state (
                    job_id TEXT NOT NULL,
                    state_key TEXT NOT NULL,
                    state_value TEXT,
                    updated_at TEXT NOT NULL,
                    PRIMARY KEY (job_id, state_key)
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
                CREATE TABLE IF NOT EXISTS host_metrics (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    node_id TEXT NOT NULL,
                    node_tenant_id TEXT NOT NULL DEFAULT '',
                    capacity_pool TEXT NOT NULL DEFAULT '',
                    captured_at TEXT NOT NULL,
                    memory_load_percent REAL NOT NULL DEFAULT 0,
                    process_cpu_percent REAL NOT NULL DEFAULT 0,
                    host_cpu_percent REAL,
                    state_disk_free_bytes INTEGER NOT NULL DEFAULT 0,
                    spill_disk_free_bytes INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS idx_hm_node_time ON host_metrics(node_id, captured_at);";
                    // The tenant index waits for the migration that adds NodeTenantId — see the note
                    // on the JobHistory index above.

                    // Daily roll-up tables (capacity trend that survives raw pruning). Day is 'yyyy-MM-dd'.
                    var createRollupTables = @"
                CREATE TABLE IF NOT EXISTS job_history_daily (
                    day TEXT NOT NULL,
                    job_id TEXT NOT NULL,
                    tenant_id TEXT NOT NULL DEFAULT '',
                    job_name TEXT NOT NULL,
                    run_count INTEGER NOT NULL DEFAULT 0,
                    failure_count INTEGER NOT NULL DEFAULT 0,
                    total_rows INTEGER NOT NULL DEFAULT 0,
                    max_peak_memory_bytes INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (day, job_id)
                );
                CREATE TABLE IF NOT EXISTS host_metrics_daily (
                    day TEXT NOT NULL,
                    node_id TEXT NOT NULL,
                    node_tenant_id TEXT NOT NULL DEFAULT '',
                    capacity_pool TEXT NOT NULL DEFAULT '',
                    avg_memory_load_percent REAL NOT NULL DEFAULT 0,
                    max_memory_load_percent REAL NOT NULL DEFAULT 0,
                    avg_cpu_percent REAL NOT NULL DEFAULT 0,
                    max_cpu_percent REAL NOT NULL DEFAULT 0,
                    min_state_disk_free_bytes INTEGER NOT NULL DEFAULT 0,
                    min_spill_disk_free_bytes INTEGER NOT NULL DEFAULT 0,
                    avg_host_cpu_percent REAL,
                    max_host_cpu_percent REAL,
                    PRIMARY KEY (day, node_id)
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
                        .Replace("rows_processed INTEGER", $"rows_processed {_dialect.Int64Type}")
                        .Replace("total_rows INTEGER", $"total_rows {_dialect.Int64Type}")
                        .Replace("null_rows INTEGER", $"null_rows {_dialect.Int64Type}")
                        .Replace("failure_count INTEGER", $"failure_count {_dialect.Int64Type}")
                        .Replace("max_peak_memory_bytes INTEGER", $"max_peak_memory_bytes {_dialect.Int64Type}")
                        .Replace("state_disk_free_bytes INTEGER", $"state_disk_free_bytes {_dialect.Int64Type}")
                        .Replace("spill_disk_free_bytes INTEGER", $"spill_disk_free_bytes {_dialect.Int64Type}")
                        .Replace("min_state_disk_free_bytes INTEGER", $"min_state_disk_free_bytes {_dialect.Int64Type}")
                        .Replace("min_spill_disk_free_bytes INTEGER", $"min_spill_disk_free_bytes {_dialect.Int64Type}")
                        // Statement metrics: durations and byte counters must be 64-bit on
                        // PostgreSQL too, where INTEGER is 32-bit and a spill byte count overflows.
                        .Replace("duration_ms INTEGER", $"duration_ms {_dialect.Int64Type}")
                        .Replace("cpu_time_ms INTEGER", $"cpu_time_ms {_dialect.Int64Type}")
                        .Replace("spilled_bytes INTEGER", $"spilled_bytes {_dialect.Int64Type}")
                        .Replace("spill_read_bytes INTEGER", $"spill_read_bytes {_dialect.Int64Type}")
                        .Replace("queue_wait_ms INTEGER", $"queue_wait_ms {_dialect.Int64Type}")
                        .Replace("lock_wait_ms INTEGER", $"lock_wait_ms {_dialect.Int64Type}")
                        .Replace("dq_rows_validated INTEGER", $"dq_rows_validated {_dialect.Int64Type}")
                        .Replace("dq_rows_quarantined INTEGER", $"dq_rows_quarantined {_dialect.Int64Type}")
                        .Replace("dq_rows_warned INTEGER", $"dq_rows_warned {_dialect.Int64Type}")
                        .Replace("dq_validation_ms INTEGER", $"dq_validation_ms {_dialect.Int64Type}")
                        .Replace("peak_memory_bytes INTEGER", $"peak_memory_bytes {_dialect.Int64Type}");

                    await EnsureCollationExistsAsync(connection);
                    await MigrateLegacySchemaAsync(connection);

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
                        "CREATE INDEX IF NOT EXISTS idx_jh_tenant_job ON job_history(tenant_id, job_id, start_time);");
                    await ExecuteOptionalSqlAsync(
                        connection,
                        "CREATE INDEX IF NOT EXISTS idx_jh_tenant_job_end ON job_history(tenant_id, job_id, end_time);");
                    await ExecuteOptionalSqlAsync(
                        connection,
                        "CREATE INDEX IF NOT EXISTS idx_hm_tenant_time ON host_metrics(node_tenant_id, captured_at);");
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
            var columns = await _dialect.GetColumnNamesAsync(connection, "jobs");

            // A Jobs table with no Id predates surrogate object identity, when a job's name was its
            // primary key. That cannot be migrated by adding columns: the identity has to become the
            // key and the name has to become unique per tenant instead of globally. Refuse here, with
            // the remedy, rather than letting it surface later as "no such column: j.Id" from
            // whichever query happened to run first.
            if (columns.Count > 0 && !columns.Contains("id"))
                throw new InvalidOperationException(
                    "This orchestrator database predates surrogate job identity and cannot be " +
                    "upgraded in place. No release shipped with data in this schema, so the remedy " +
                    "is to delete the database file and let it be recreated.");

            await AddColumnIfMissingAsync(connection, columns, "jobs", "max_retries", "max_retries INTEGER NOT NULL DEFAULT 0");
            await AddColumnIfMissingAsync(connection, columns, "jobs", "retry_delay_seconds", "retry_delay_seconds INTEGER NOT NULL DEFAULT 30");
            await AddColumnIfMissingAsync(connection, columns, "jobs", "script_hash", "script_hash TEXT");
            await AddColumnIfMissingAsync(connection, columns, "jobs", "hash_policy", "hash_policy TEXT NOT NULL DEFAULT 'Warn'");
            await AddColumnIfMissingAsync(connection, columns, "jobs", "lease_owner", "lease_owner TEXT");
            await AddColumnIfMissingAsync(connection, columns, "jobs", "lease_expires_at", "lease_expires_at TEXT");
            await AddColumnIfMissingAsync(connection, columns, "jobs", "lease_fence_token", "lease_fence_token INTEGER NOT NULL DEFAULT 0");
            await AddColumnIfMissingAsync(connection, columns, "jobs", "run_state", "run_state TEXT NOT NULL DEFAULT 'Idle'");
            await AddColumnIfMissingAsync(connection, columns, "jobs", "version", "version INTEGER NOT NULL DEFAULT 1");

            // Unified job/schedule/notification model. A job now names what it acts on rather than
            // carrying an inline script body, and carries the human-facing label that lets its Name
            // stay a stable machine identity.
            await AddColumnIfMissingAsync(connection, columns, "jobs", "job_type", "job_type TEXT NOT NULL DEFAULT 'Script'");

            foreach (var column in new[] { "target_path", "display_name", "description", "options", "created_by", "modified_by", "tenant_id" })
                await AddColumnIfMissingAsync(connection, columns, "jobs", column, $"{column} TEXT");
        }

        private async Task EnsureHostMetricsColumnsExist(DbConnection connection)
        {
            // The node's tenant and capacity-pool binding, added after the table first shipped. These
            // describe which tenant a *node* is dedicated to, not which tenant a sample belongs to —
            // a Shared node's gauges do not decompose by tenant, and an empty binding says exactly
            // that rather than misattributing capacity.
            var columns = await _dialect.GetColumnNamesAsync(connection, "host_metrics");
            await AddColumnIfMissingAsync(
                connection, columns, "host_metrics", "node_tenant_id", "node_tenant_id TEXT NOT NULL DEFAULT ''");
            await AddColumnIfMissingAsync(
                connection, columns, "host_metrics", "capacity_pool", "capacity_pool TEXT NOT NULL DEFAULT ''");
        }

        private async Task EnsureHostMetricsDailyColumnsExist(DbConnection connection)
        {
            // Whole-host CPU joined the daily roll-up after the table first shipped; upgrade
            // existing databases in place (nullable REAL — no backfill possible for pruned raw rows).
            var columns = await _dialect.GetColumnNamesAsync(connection, "host_metrics_daily");

            await AddColumnIfMissingAsync(connection, columns, "host_metrics_daily", "avg_host_cpu_percent", "avg_host_cpu_percent REAL");
            await AddColumnIfMissingAsync(connection, columns, "host_metrics_daily", "max_host_cpu_percent", "max_host_cpu_percent REAL");
        }

        private async Task EnsureHistoryColumnsExist(DbConnection connection)
        {
            var columns = await _dialect.GetColumnNamesAsync(connection, "job_history");

            // Identity and tenant, matching the CREATE defaults. The empty default is meaningful in
            // both: '' is an ad-hoc run with no job, and '' is the unbound (Solo) tenant. Rows in a
            // table written by an earlier build of this schema are exactly that — unbound runs whose
            // job binding was never recorded — so the default states the truth rather than guessing.
            await AddColumnIfMissingAsync(connection, columns, "job_history", "job_id", "job_id TEXT NOT NULL DEFAULT ''");
            await AddColumnIfMissingAsync(connection, columns, "job_history", "tenant_id", "tenant_id TEXT NOT NULL DEFAULT ''");

            await AddColumnIfMissingAsync(connection, columns, "job_history", "peak_memory_bytes", "peak_memory_bytes INTEGER DEFAULT 0");
            await AddColumnIfMissingAsync(connection, columns, "job_history", "cpu_time_seconds", "cpu_time_seconds REAL DEFAULT 0");
            await AddColumnIfMissingAsync(connection, columns, "job_history", "script_hash_at_run_time", "script_hash_at_run_time TEXT");
            await AddColumnIfMissingAsync(connection, columns, "job_history", "hash_matched", "hash_matched INTEGER");

            // Data-quality outcomes per run (rolling-expand safe: additive, defaulted).
            await AddColumnIfMissingAsync(connection, columns, "job_history", "rows_quarantined", "rows_quarantined INTEGER DEFAULT 0");
            await AddColumnIfMissingAsync(connection, columns, "job_history", "rows_warned", "rows_warned INTEGER DEFAULT 0");

            // Compact "column:rule=count;..." payload — counts only, never sample values.
            await AddColumnIfMissingAsync(connection, columns, "job_history", "data_quality_failures", "data_quality_failures TEXT");
            await AddColumnIfMissingAsync(connection, columns, "job_history", "session_id", "session_id TEXT");
            await AddColumnIfMissingAsync(connection, columns, "job_history", "checkpoint_label", "checkpoint_label TEXT");
        }

        private async Task EnsureColumnMetricColumnsExist(DbConnection connection)
        {
            var columns = await _dialect.GetColumnNamesAsync(connection, "job_column_metrics");
            await AddColumnIfMissingAsync(connection, columns, "job_column_metrics", "max_timestamp_utc", "max_timestamp_utc TEXT");

            // The tenant of the run these measurements belong to, carried directly as
            // SharedBackupSurfaceInventory declares. It is copied from JobHistory at insert rather
            // than passed in by the caller, so it cannot disagree with the run it describes; the
            // reason to hold it at all is that backup, restore and tenant deletion then take a
            // predicate on this table instead of a join that a future query could forget.
            foreach (var table in new[] { "job_column_metrics", "job_data_quality_failures", "job_statement_metrics" })
            {
                var tableColumns = await _dialect.GetColumnNamesAsync(connection, table);
                await AddColumnIfMissingAsync(
                    connection, tableColumns, table, "tenant_id", "tenant_id TEXT NOT NULL DEFAULT ''");
            }
        }

        private async Task EnsureLineageHistoryColumnsExist(DbConnection connection)
        {
            var columns = await _dialect.GetColumnNamesAsync(connection, "lineage_history");

            async Task AddColumn(string name, string ddl)
            {
                await AddColumnIfMissingAsync(connection, columns, "lineage_history", name, ddl);
            }

            // SourceColumns predates this migration on new installs but may be
            // missing on databases created before it was added to the schema.
            await AddColumn("source_columns", "source_columns TEXT NOT NULL DEFAULT '[]'");
            await AddColumn("tenant_id", "tenant_id TEXT NOT NULL DEFAULT 'portal-host'");
            await AddColumn("transformation_kind", "transformation_kind TEXT");
            await AddColumn("transformation_expression", "transformation_expression TEXT");
            await AddColumn("functions_applied", "functions_applied TEXT NOT NULL DEFAULT '[]'");
            await AddColumn("derived_from_descriptions", "derived_from_descriptions TEXT");

            using var indexes = connection.CreateCommand();
            indexes.CommandText = @"
                CREATE INDEX IF NOT EXISTS idx_lh_tenant_target
                    ON lineage_history(tenant_id, target_table COLLATE NOCASE);
                CREATE INDEX IF NOT EXISTS idx_lh_tenant_runAt
                    ON lineage_history(tenant_id, run_at);";
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
                INSERT INTO jobs (id, name, script, interval, unit, at_time, last_run, next_run, is_enabled, max_retries, retry_delay_seconds, script_hash, hash_policy, job_type, target_path, display_name, description, options, created_by, modified_by, tenant_id)
                VALUES (@id, @name, @script, @interval, @unit, @atTime, @lastRun, @nextRun, @isEnabled, @maxRetries, @retryDelay, @scriptHash, @hashPolicy, @jobType, @targetPath, @displayName, @description, @options, @createdBy, @modifiedBy, @tenantId)
                ON CONFLICT(tenant_id, name) DO UPDATE SET
                    script            = excluded.script,
                    interval          = excluded.interval,
                    unit              = excluded.unit,
                    at_time            = excluded.at_time,
                    last_run           = excluded.last_run,
                    next_run           = excluded.next_run,
                    is_enabled         = excluded.is_enabled,
                    max_retries        = excluded.max_retries,
                    retry_delay_seconds = excluded.retry_delay_seconds,
                    script_hash        = excluded.script_hash,
                    hash_policy        = excluded.hash_policy,
                    job_type           = excluded.job_type,
                    target_path        = excluded.target_path,
                    display_name       = excluded.display_name,
                    description       = excluded.description,
                    options           = excluded.options,
                    modified_by        = excluded.modified_by,
                    version           = jobs.version + 1;";

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
                UPDATE jobs SET
                    script = @script,
                    interval = @interval,
                    unit = @unit,
                    at_time = @atTime,
                    last_run = @lastRun,
                    next_run = @nextRun,
                    is_enabled = @isEnabled,
                    max_retries = @maxRetries,
                    retry_delay_seconds = @retryDelay,
                    script_hash = @scriptHash,
                    hash_policy = @hashPolicy,
                    job_type = @jobType,
                    target_path = @targetPath,
                    display_name = @displayName,
                    description = @description,
                    options = @options,
                    modified_by = @modifiedBy,
                    version = version + 1
                WHERE id = @id AND version = @expectedVersion;";
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
                UPDATE jobs SET lease_owner = @owner, lease_expires_at = @expires, lease_fence_token = lease_fence_token + 1,
                                run_state = 'Idle'
                WHERE id = @id
                  AND is_enabled = 1
                  AND (lease_owner IS NULL OR lease_expires_at IS NULL OR lease_expires_at <= @now)
                  AND NOT EXISTS (
                      SELECT 1 FROM shared_tenant_control_planes lifecycle
                       WHERE lifecycle.tenant_id = jobs.tenant_id
                         AND lifecycle.state <> 'Active')
                  AND NOT EXISTS (
                      SELECT 1 FROM tenant_portability_cutovers portability
                       WHERE portability.tenant_id = jobs.tenant_id
                         AND (portability.authority <> 'Source'
                              OR portability.source_schedules_enabled = 0));";
            claim.AddParam("@owner", owner);
            claim.AddParam("@expires", now.Add(duration).ToString("O"));
            claim.AddParam("@id", jobId);
            claim.AddParam("@now", now.ToString("O"));

            if (await claim.ExecuteNonQueryAsync() != 1)
                return null;

            // We now own the row; read back the token we were granted.
            using var read = connection.CreateCommand();
            read.CommandText = "SELECT lease_fence_token FROM jobs WHERE id = @id AND lease_owner = @owner;";
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
            command.CommandText = "SELECT lease_fence_token FROM jobs WHERE id = @id;";
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
                UPDATE jobs SET last_run = @lastRun, next_run = @nextRun
                WHERE id = @id AND lease_fence_token = @token;";
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
                UPDATE jobs SET lease_expires_at = @expires
                WHERE id = @id AND lease_owner = @owner;";
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
                UPDATE jobs SET lease_owner = NULL, lease_expires_at = NULL, run_state = 'Idle'
                WHERE id = @id AND lease_owner = @owner;";
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
                INSERT INTO nodes (node_id, role, first_seen_at, last_heartbeat_at, expires_at, metadata)
                VALUES (@id, @role, @now, @now, @expires, @meta)
                ON CONFLICT(node_id) DO UPDATE SET
                    role            = excluded.role,
                    last_heartbeat_at = excluded.last_heartbeat_at,
                    expires_at       = excluded.expires_at,
                    metadata        = excluded.metadata;";
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
                SELECT node_id, role, first_seen_at, last_heartbeat_at, expires_at, metadata
                FROM nodes WHERE expires_at > @now ORDER BY role, node_id;";
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
                SELECT node_id, role, first_seen_at, last_heartbeat_at, expires_at, metadata
                FROM nodes ORDER BY role, node_id;";
            return await ReadNodesAsync(command);
        }

        public async Task DeregisterNodeAsync(string nodeId)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM nodes WHERE node_id = @id;";
            command.AddParam("@id", nodeId);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<int> PruneExpiredNodesAsync()
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM nodes WHERE expires_at <= @now;";
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
                INSERT INTO write_epochs (scope, epoch_key, epoch) VALUES (@scope, @key, @token)
                ON CONFLICT(scope, epoch_key) DO UPDATE SET epoch = excluded.epoch
                    WHERE excluded.epoch >= write_epochs.epoch;";
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
            command.CommandText = "SELECT epoch FROM write_epochs WHERE scope = @scope AND epoch_key = @key;";
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
                INSERT INTO cluster_locks (lock_name, owner, expires_at) VALUES (@name, @owner, @expires)
                ON CONFLICT(lock_name) DO UPDATE SET owner = excluded.owner, expires_at = excluded.expires_at
                    WHERE cluster_locks.expires_at <= @now OR cluster_locks.owner = excluded.owner;";
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
                UPDATE cluster_locks SET expires_at = @expires
                WHERE lock_name = @name AND owner = @owner;";
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
            command.CommandText = "DELETE FROM cluster_locks WHERE lock_name = @name AND owner = @owner;";
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
            command.CommandText = "SELECT owner FROM cluster_locks WHERE lock_name = @name AND expires_at > @now;";
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

            var sql = "SELECT * FROM jobs WHERE is_enabled = 1;";
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
                SELECT * FROM jobs
                WHERE is_enabled = 1
                  AND (next_run IS NULL OR next_run <= @now)
                  AND NOT EXISTS (SELECT 1 FROM job_schedules js WHERE js.job_id = jobs.id);";
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
            command.CommandText = "SELECT * FROM jobs;";

            var jobs = new List<JobDefinition>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                jobs.Add(ReadJob(reader));
            }
            return jobs;
        }

        public async Task<IEnumerable<JobDefinition>> GetJobsPageAsync(string? tenantId, int limit, int offset)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM jobs WHERE tenant_id = @tenant ORDER BY name LIMIT @limit OFFSET @offset;";
            command.AddParam("@tenant", TenantKey(tenantId));
            command.AddParam("@limit", Math.Clamp(limit, 1, 1000));
            command.AddParam("@offset", Math.Max(0, offset));

            var jobs = new List<JobDefinition>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) jobs.Add(ReadJob(reader));
            return jobs;
        }

        public async Task<IEnumerable<JobDefinition>> GetJobsPageAsync(int limit = 100, int offset = 0)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM jobs ORDER BY name LIMIT @limit OFFSET @offset;";
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
                "SELECT * FROM jobs WHERE tenant_id = @tenant AND name = @name COLLATE NOCASE LIMIT 1;";
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
            command.CommandText = "SELECT * FROM jobs WHERE id = @id LIMIT 1;";
            command.AddParam("@id", jobId);

            using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync() ? ReadJob(reader) : null;
        }

        private static JobDefinition ReadJob(DbDataReader reader)
        {
            var lastRunOrdinal = reader.GetOrdinal("last_run");
            var nextRunOrdinal = reader.GetOrdinal("next_run");
            var atTimeOrdinal = reader.GetOrdinal("at_time");
            var scriptHashOrdinal = reader.GetOrdinal("script_hash");
            var hashPolicyOrdinal = reader.GetOrdinal("hash_policy");
            var versionOrdinal = reader.GetOrdinal("version");
            return new JobDefinition(
                reader.GetString(reader.GetOrdinal("name")),
                reader.GetString(reader.GetOrdinal("script")),
                reader.GetInt32(reader.GetOrdinal("interval")),
                reader.GetString(reader.GetOrdinal("unit")),
                reader.IsDBNull(atTimeOrdinal) ? null : reader.GetString(atTimeOrdinal),
                reader.IsDBNull(lastRunOrdinal) ? null : DateTime.Parse(reader.GetString(lastRunOrdinal)),
                reader.IsDBNull(nextRunOrdinal) ? null : DateTime.Parse(reader.GetString(nextRunOrdinal)),
                reader.GetInt32(reader.GetOrdinal("is_enabled")) == 1,
                reader.GetInt32(reader.GetOrdinal("max_retries")),
                reader.GetInt32(reader.GetOrdinal("retry_delay_seconds")),
                reader.IsDBNull(scriptHashOrdinal) ? null : reader.GetString(scriptHashOrdinal),
                reader.IsDBNull(hashPolicyOrdinal) ? "Warn" : reader.GetString(hashPolicyOrdinal),
                reader.GetInt64(versionOrdinal),
                ReadJobType(reader),
                ReadOptionalString(reader, "target_path"),
                ReadOptionalString(reader, "display_name"),
                ReadOptionalString(reader, "description"),
                ReadOptionalString(reader, "options"),
                ReadOptionalString(reader, "created_by"),
                ReadOptionalString(reader, "modified_by"),
                TenantOrNull(ReadOptionalString(reader, "tenant_id")),
                JobId.From(ReadOptionalString(reader, "id")));
        }

        /// <summary>
        /// An unparseable stored value falls back to <see cref="JobTargetKind.Script"/> rather than
        /// throwing: a job whose type cannot be read is still a job an operator needs to see and drop,
        /// and failing the whole listing would hide every other job alongside it.
        /// </summary>
        private static JobTargetKind ReadJobType(DbDataReader reader)
        {
            var raw = ReadOptionalString(reader, "job_type");
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
                var sql1 = "DELETE FROM jobs WHERE id = @id;";
                using var command1 = connection.CreateCommand();
                command1.CommandText = sql1;
                command1.Transaction = transaction;
                command1.AddParam("@id", jobId);
                await command1.ExecuteNonQueryAsync();

                var sqlMetrics = "DELETE FROM job_column_metrics WHERE job_history_id IN (SELECT id FROM job_history WHERE job_id = @id);";
                using var commandMetrics = connection.CreateCommand();
                commandMetrics.CommandText = sqlMetrics;
                commandMetrics.Transaction = transaction;
                commandMetrics.AddParam("@id", jobId);
                await commandMetrics.ExecuteNonQueryAsync();

                var sqlFailures = "DELETE FROM job_data_quality_failures WHERE job_history_id IN (SELECT id FROM job_history WHERE job_id = @id);";
                using var commandFailures = connection.CreateCommand();
                commandFailures.CommandText = sqlFailures;
                commandFailures.Transaction = transaction;
                commandFailures.AddParam("@id", jobId);
                await commandFailures.ExecuteNonQueryAsync();

                var sqlStatements = "DELETE FROM job_statement_metrics WHERE job_history_id IN (SELECT id FROM job_history WHERE job_id = @id);";
                using var commandStatements = connection.CreateCommand();
                commandStatements.CommandText = sqlStatements;
                commandStatements.Transaction = transaction;
                commandStatements.AddParam("@id", jobId);
                await commandStatements.ExecuteNonQueryAsync();

                var sql2 = "DELETE FROM job_history WHERE job_id = @id;";
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
            deleteJob.CommandText = "DELETE FROM jobs WHERE id = @id AND version = @version;";
            deleteJob.AddParam("@id", jobId);
            deleteJob.AddParam("@version", expectedVersion);
            if (await deleteJob.ExecuteNonQueryAsync() != 1)
            {
                await transaction.RollbackAsync();
                return false;
            }

            using var deleteMetrics = connection.CreateCommand();
            deleteMetrics.Transaction = transaction;
            deleteMetrics.CommandText = "DELETE FROM job_column_metrics WHERE job_history_id IN (SELECT id FROM job_history WHERE job_id = @id);";
            deleteMetrics.AddParam("@id", jobId);
            await deleteMetrics.ExecuteNonQueryAsync();

            using var deleteFailures = connection.CreateCommand();
            deleteFailures.Transaction = transaction;
            deleteFailures.CommandText = "DELETE FROM job_data_quality_failures WHERE job_history_id IN (SELECT id FROM job_history WHERE job_id = @id);";
            deleteFailures.AddParam("@id", jobId);
            await deleteFailures.ExecuteNonQueryAsync();

            using var deleteStatements = connection.CreateCommand();
            deleteStatements.Transaction = transaction;
            deleteStatements.CommandText = "DELETE FROM job_statement_metrics WHERE job_history_id IN (SELECT id FROM job_history WHERE job_id = @id);";
            deleteStatements.AddParam("@id", jobId);
            await deleteStatements.ExecuteNonQueryAsync();

            using var deleteHistory = connection.CreateCommand();
            deleteHistory.Transaction = transaction;
            deleteHistory.CommandText = "DELETE FROM job_history WHERE job_id = @id;";
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

            var sql = "UPDATE jobs SET last_run = @lastRun, next_run = @nextRun WHERE id = @id;";
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
                @"INSERT INTO job_history (job_id, tenant_id, job_name, start_time, status)
                  SELECT j.id, j.tenant_id, j.name, @start, 'RUNNING' FROM jobs j WHERE j.id = @id", "id");
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
                @"INSERT INTO job_history (job_id, tenant_id, job_name, start_time, status)
                  VALUES ('', @tenant, @label, @start, 'RUNNING')", "id");
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

            var sql = "UPDATE job_history SET end_time = @end, status = @status, error_message = @err, rows_processed = @rows, peak_memory_bytes = @mem, cpu_time_seconds = @cpu, script_hash_at_run_time = @hash, hash_matched = @matched, rows_quarantined = @quarantined, rows_warned = @warned, data_quality_failures = @dqfail WHERE id = @id;";
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
            command.CommandText = "UPDATE job_history SET session_id = @session, checkpoint_label = @label WHERE id = @id;";
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
                    SELECT id FROM job_history
                    WHERE job_name = @name COLLATE NOCASE AND start_time = @start
                      AND ((end_time IS NULL AND @end IS NULL) OR end_time = @end)
                    ORDER BY id LIMIT 1;";
                existing.AddParam("@name", entry.JobName);
                existing.AddParam("@start", entry.StartTime.ToString("O"));
                existing.AddParam("@end", entry.EndTime.HasValue ? entry.EndTime.Value.ToString("O") : DBNull.Value);
                var found = await existing.ExecuteScalarAsync();
                if (found is not null && found != DBNull.Value)
                    return Convert.ToInt64(found);
            }

            var sql = _dialect.InsertReturningId(@"
                INSERT INTO job_history
                    (job_name, start_time, end_time, status, error_message, rows_processed,
                     peak_memory_bytes, cpu_time_seconds, script_hash_at_run_time, hash_matched,
                     rows_quarantined, rows_warned, data_quality_failures, session_id, checkpoint_label)
                VALUES
                    (@name, @start, @end, @status, @error, @rows,
                     @memory, @cpu, @hash, @matched, @quarantined, @warned, @failures, @session, @checkpoint)", "id");
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
                    INSERT INTO job_column_metrics (job_history_id, tenant_id, target_table, column_name, total_rows, null_rows, max_timestamp_utc)
                    VALUES (@historyId, COALESCE((SELECT tenant_id FROM job_history WHERE id = @historyId), ''), @target, @column, @total, @nulls, @maxTimestamp)
                    ON CONFLICT (job_history_id, target_table, column_name) DO UPDATE SET
                        total_rows = excluded.total_rows,
                        null_rows = excluded.null_rows,
                        max_timestamp_utc = excluded.max_timestamp_utc;";
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
                    INSERT INTO job_statement_metrics (
                        job_history_id, tenant_id, ordinal, statement, duration_ms, rows_processed, cpu_time_ms,
                        spilled_bytes, spill_read_bytes, partitions, queue_wait_ms, lock_wait_ms, index_used,
                        dq_rows_validated, dq_rows_quarantined, dq_rows_warned, dq_validation_ms, failed)
                    VALUES (
                        @historyId, COALESCE((SELECT tenant_id FROM job_history WHERE id = @historyId), ''),
                        @ordinal, @statement, @duration, @rows, @cpu,
                        @spilled, @spillRead, @partitions, @queueWait, @lockWait, @indexUsed,
                        @dqValidated, @dqQuarantined, @dqWarned, @dqMs, @failed)
                    ON CONFLICT (job_history_id, ordinal) DO NOTHING;";
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
                INSERT INTO tenant_usage_records (
                    tenant_id, job_history_id, workload_kind, status, rows_processed,
                    peak_memory_bytes, cpu_time_seconds, duration_ms, recorded_at_utc)
                VALUES (@tenant, @historyId, @kind, @status, @rows, @memory, @cpu, @duration, @recorded)
                ON CONFLICT (tenant_id, job_history_id) DO NOTHING;";
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
                SELECT id, tenant_id, job_history_id, workload_kind, status, rows_processed,
                       peak_memory_bytes, cpu_time_seconds, duration_ms, recorded_at_utc
                FROM tenant_usage_records
                WHERE tenant_id = @tenant
                  AND (@fromUtc IS NULL OR recorded_at_utc >= @fromUtc)
                ORDER BY recorded_at_utc DESC, id DESC
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
                    DELETE FROM job_statement_metrics
                    WHERE job_history_id IN (
                        SELECT h.id FROM job_history h
                        WHERE h.status <> 'RUNNING' AND h.start_time < @successCutoff
                          AND NOT EXISTS (
                              SELECT 1 FROM job_statement_metrics m
                              WHERE m.job_history_id = h.id AND m.failed <> 0
                          )
                    );";
                command.AddParam("@successCutoff", DateTime.Now.Subtract(successMaxAge).ToString("O"));
                removed += await command.ExecuteNonQueryAsync();
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    DELETE FROM job_statement_metrics
                    WHERE job_history_id IN (
                        SELECT h.id FROM job_history h
                        WHERE h.status <> 'RUNNING' AND h.start_time < @failedCutoff
                          AND EXISTS (
                              SELECT 1 FROM job_statement_metrics m
                              WHERE m.job_history_id = h.id AND m.failed <> 0
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
                SELECT statement, duration_ms, rows_processed, cpu_time_ms, spilled_bytes, spill_read_bytes,
                       partitions, queue_wait_ms, lock_wait_ms, index_used,
                       dq_rows_validated, dq_rows_quarantined, dq_rows_warned, dq_validation_ms, failed
                FROM job_statement_metrics
                WHERE job_history_id = @historyId
                ORDER BY ordinal;";
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
                    INSERT INTO job_data_quality_failures
                        (job_history_id, tenant_id, target_table, column_name, rule_text, action, failure_count, owner)
                    VALUES (@historyId, COALESCE((SELECT tenant_id FROM job_history WHERE id = @historyId), ''), @target, @column, @rule, @action, @count, @owner)
                    ON CONFLICT (job_history_id, target_table, column_name, rule_text, action) DO UPDATE SET
                        failure_count = excluded.failure_count,
                        owner = excluded.owner;";
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
                SELECT h.id, h.job_name, h.start_time, h.end_time, h.status,
                       m.ordinal, m.statement, m.duration_ms, m.rows_processed, m.cpu_time_ms,
                       m.spilled_bytes, m.spill_read_bytes, m.partitions, m.queue_wait_ms, m.lock_wait_ms,
                       m.index_used, m.dq_rows_validated, m.dq_rows_quarantined, m.dq_rows_warned,
                       m.dq_validation_ms, m.failed
                FROM job_statement_metrics m
                INNER JOIN job_history h ON h.id = m.job_history_id
                ORDER BY h.start_time DESC, h.id DESC, m.ordinal
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
                SELECT h.id, h.job_name, h.start_time, h.end_time, h.status,
                       f.target_table, f.column_name, f.rule_text, f.action, f.failure_count, f.owner
                FROM job_data_quality_failures f
                INNER JOIN job_history h ON h.id = f.job_history_id
                ORDER BY h.start_time DESC, h.id DESC, f.target_table, f.column_name, f.rule_text, f.action
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
                SELECT h.id, h.job_name, h.start_time, h.end_time, h.status,
                       f.target_table, f.column_name, f.rule_text, f.action, f.failure_count, f.owner
                FROM job_data_quality_failures f
                INNER JOIN job_history h ON h.id = f.job_history_id
                WHERE h.job_name = @jobName COLLATE NOCASE
                ORDER BY h.start_time DESC, h.id DESC, f.target_table, f.column_name, f.rule_text, f.action
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
                SELECT h.id, h.job_name, h.start_time, h.end_time, h.status,
                       f.target_table, f.column_name, f.rule_text, f.action, f.failure_count, f.owner
                FROM job_data_quality_failures f
                INNER JOIN job_history h ON h.id = f.job_history_id
                WHERE h.id = @historyId
                ORDER BY f.target_table, f.column_name, f.rule_text, f.action
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
                SELECT h.id, h.job_name, h.start_time, h.end_time, h.status,
                       h.rows_processed, h.rows_warned, h.rows_quarantined, h.error_message,
                       (SELECT COUNT(*) FROM job_data_quality_failures f WHERE f.job_history_id = h.id) AS FailedRuleCount,
                       (SELECT MAX(m.max_timestamp_utc) FROM job_column_metrics m WHERE m.job_history_id = h.id) AS FreshestValueUtc
                FROM job_history h
                ORDER BY h.start_time DESC, h.id DESC
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
                SELECT m.target_table, m.column_name, m.total_rows, m.null_rows, m.max_timestamp_utc
                FROM job_column_metrics m
                INNER JOIN job_history h ON h.id = m.job_history_id
                WHERE h.job_id = @job
                  AND h.end_time IS NOT NULL
                  AND (UPPER(h.status) = 'SUCCESS' OR UPPER(h.status) = 'COMPLETED')
                  AND m.column_name = @column COLLATE NOCASE
                  AND (@target IS NULL OR m.target_table = @target COLLATE NOCASE)
                ORDER BY h.end_time DESC, h.start_time DESC, h.id DESC
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
                    DELETE FROM job_column_metrics
                    WHERE job_history_id IN (
                        SELECT id FROM job_history WHERE status <> 'RUNNING' AND start_time < @cutoff
                    );";
                deleteMetrics.AddParam("@cutoff", cutoff);
                await deleteMetrics.ExecuteNonQueryAsync();
            }

            using (var deleteFailures = connection.CreateCommand())
            {
                deleteFailures.CommandText = @"
                    DELETE FROM job_data_quality_failures
                    WHERE job_history_id IN (
                        SELECT id FROM job_history WHERE status <> 'RUNNING' AND start_time < @cutoff
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
                    DELETE FROM job_statement_metrics
                    WHERE job_history_id IN (
                        SELECT id FROM job_history WHERE status <> 'RUNNING' AND start_time < @cutoff
                    );";
                deleteStatements.AddParam("@cutoff", cutoff);
                await deleteStatements.ExecuteNonQueryAsync();
            }

            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM job_history WHERE status <> 'RUNNING' AND start_time < @cutoff;";
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
                "UPDATE job_history SET status = 'INTERRUPTED', end_time = @end, " +
                "error_message = 'No completion recorded within the maximum job runtime; the orchestrator likely restarted or the job was killed.' " +
                "WHERE status = 'RUNNING' AND start_time < @cutoff;";
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
                "INSERT INTO host_metrics (node_id, captured_at, memory_load_percent, process_cpu_percent, host_cpu_percent, state_disk_free_bytes, spill_disk_free_bytes) " +
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
            var sql = "SELECT node_id, captured_at, memory_load_percent, process_cpu_percent, host_cpu_percent, state_disk_free_bytes, spill_disk_free_bytes " +
                      "FROM host_metrics WHERE captured_at >= @since ";
            if (!string.IsNullOrEmpty(nodeId)) { sql += "AND node_id = @node "; command.AddParam("@node", nodeId); }
            sql += "ORDER BY captured_at DESC LIMIT @limit;";
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
            command.CommandText = "DELETE FROM host_metrics WHERE captured_at < @cutoff;";
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
                del.CommandText = "DELETE FROM job_history_daily WHERE day IN (SELECT DISTINCT substr(start_time,1,10) FROM job_history WHERE status <> 'RUNNING');";
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
                    "INSERT INTO job_history_daily (day, job_id, tenant_id, job_name, run_count, failure_count, total_rows, max_peak_memory_bytes) " +
                    "SELECT substr(start_time,1,10), job_id, MAX(tenant_id), MAX(job_name), COUNT(*), " +
                    "SUM(CASE WHEN status <> 'SUCCESS' THEN 1 ELSE 0 END), SUM(rows_processed), MAX(peak_memory_bytes) " +
                    "FROM job_history WHERE status <> 'RUNNING' AND job_id <> '' " +
                    "GROUP BY substr(start_time,1,10), job_id;";
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
            var sql = "SELECT day, job_name, run_count, failure_count, total_rows, max_peak_memory_bytes FROM job_history_daily WHERE day >= @since ";
            if (!string.IsNullOrEmpty(jobId)) { sql += "AND job_id = @job "; command.AddParam("@job", jobId); }
            sql += "ORDER BY day DESC, job_name LIMIT @limit;";
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
            command.CommandText = "DELETE FROM job_history_daily WHERE day < @cutoff;";
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
                del.CommandText = "DELETE FROM host_metrics_daily WHERE day IN (SELECT DISTINCT substr(captured_at,1,10) FROM host_metrics);";
                await del.ExecuteNonQueryAsync();
            }
            int written;
            using (var ins = connection.CreateCommand())
            {
                ins.Transaction = tx;
                // AVG/MAX ignore NULL HostCpuPercent samples (and yield NULL when no sample has one),
                // so days recorded before the whole-host probe shipped roll up as NULL, not 0.
                ins.CommandText =
                    "INSERT INTO host_metrics_daily (day, node_id, avg_memory_load_percent, max_memory_load_percent, avg_cpu_percent, max_cpu_percent, min_state_disk_free_bytes, min_spill_disk_free_bytes, avg_host_cpu_percent, max_host_cpu_percent) " +
                    "SELECT substr(captured_at,1,10), node_id, AVG(memory_load_percent), MAX(memory_load_percent), " +
                    "AVG(process_cpu_percent), MAX(process_cpu_percent), MIN(state_disk_free_bytes), MIN(spill_disk_free_bytes), " +
                    "AVG(host_cpu_percent), MAX(host_cpu_percent) " +
                    "FROM host_metrics GROUP BY substr(captured_at,1,10), node_id;";
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
            var sql = "SELECT day, node_id, avg_memory_load_percent, max_memory_load_percent, avg_cpu_percent, max_cpu_percent, min_state_disk_free_bytes, min_spill_disk_free_bytes, avg_host_cpu_percent, max_host_cpu_percent FROM host_metrics_daily WHERE day >= @since ";
            if (!string.IsNullOrEmpty(nodeId)) { sql += "AND node_id = @node "; command.AddParam("@node", nodeId); }
            sql += "ORDER BY day DESC, node_id LIMIT @limit;";
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
            command.CommandText = "DELETE FROM host_metrics_daily WHERE day < @cutoff;";
            command.AddParam("@cutoff", DateTime.UtcNow.Subtract(maxAge).ToString("yyyy-MM-dd"));
            return await command.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<JobHistoryEntry>> GetHistoryAsync(JobId jobIdRef = default, int limit = 100)
        {
            var jobId = jobIdRef.IsAssigned ? jobIdRef.Value : null;
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();

            var sql = "SELECT * FROM job_history ";
            if (jobId != null) sql += "WHERE job_id = @jobId ";
            sql += "ORDER BY start_time DESC LIMIT @limit;";

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
                "SELECT * FROM job_history WHERE tenant_id = @tenant AND job_name = @name COLLATE NOCASE " +
                "ORDER BY start_time DESC LIMIT @limit;";
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
            command.CommandText = "SELECT * FROM job_history WHERE id = @historyId LIMIT 1;";
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
            command.CommandText = "SELECT * FROM job_history WHERE end_time > @after AND end_time <= @through " +
                "ORDER BY end_time, id LIMIT @limit OFFSET @offset;";
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
            reader.GetInt64(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("job_name")),
            DateTime.Parse(reader.GetString(reader.GetOrdinal("start_time"))),
            ReadOptionalDateTime(reader, "end_time"),
            reader.GetString(reader.GetOrdinal("status")),
            ReadOptionalString(reader, "error_message"),
            ReadOptionalInt64(reader, "rows_processed"),
            ReadOptionalInt64(reader, "peak_memory_bytes"),
            ReadOptionalDouble(reader, "cpu_time_seconds"),
            ReadOptionalString(reader, "script_hash_at_run_time"),
            ReadOptionalBool(reader, "hash_matched"),
            ReadOptionalInt64(reader, "rows_quarantined"),
            ReadOptionalInt64(reader, "rows_warned"),
            ReadOptionalString(reader, "data_quality_failures"),
            ReadOptionalString(reader, "session_id"),
            ReadOptionalString(reader, "checkpoint_label"),
            JobId.From(ReadOptionalString(reader, "job_id")),
            ReadOptionalString(reader, "tenant_id") is { Length: > 0 } tenant ? tenant : null);

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
            command.CommandText = "SELECT state_value FROM job_state WHERE job_id = @jobId AND state_key = @key;";
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
                INSERT INTO job_state (job_id, state_key, state_value, updated_at)
                VALUES (@jobId, @key, @value, @updatedAt)
                ON CONFLICT (job_id, state_key)
                DO UPDATE SET state_value = EXCLUDED.state_value, updated_at = EXCLUDED.updated_at;";

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
                SELECT j.name, s.state_key, s.state_value, s.updated_at
                FROM job_state s
                INNER JOIN jobs j ON j.id = s.job_id ";
            if (!string.IsNullOrEmpty(jobId)) { sql += "WHERE s.job_id = @job "; command.AddParam("@job", jobId); }
            sql += "ORDER BY j.name, s.state_key LIMIT @limit;";
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
                        INSERT INTO bundle_versions
                            (bundle_name, version, entry_path, content_hash, published_at, publisher, description, encryption_mode, encryption_metadata)
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
                        INSERT INTO bundle_files
                            (bundle_name, version, virtual_path, content, content_hash, size_bytes, content_type)
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
                        INSERT INTO bundle_dependencies
                            (bundle_name, version, from_path, to_path)
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
                        DELETE FROM bundle_files
                        WHERE bundle_name = @bundle
                          AND version NOT IN (
                              SELECT version FROM bundle_versions
                              WHERE bundle_name = @bundle
                              ORDER BY version DESC
                              LIMIT 5
                          );";
                    gcFiles.AddParam("@bundle", request.BundleName);
                    await gcFiles.ExecuteNonQueryAsync();
                }

                using (var gcDeps = connection.CreateCommand())
                {
                    gcDeps.Transaction = transaction;
                    gcDeps.CommandText = @"
                        DELETE FROM bundle_dependencies
                        WHERE bundle_name = @bundle
                          AND version NOT IN (
                              SELECT version FROM bundle_versions
                              WHERE bundle_name = @bundle
                              ORDER BY version DESC
                              LIMIT 5
                          );";
                    gcDeps.AddParam("@bundle", request.BundleName);
                    await gcDeps.ExecuteNonQueryAsync();
                }

                using (var gcVersions = connection.CreateCommand())
                {
                    gcVersions.Transaction = transaction;
                    gcVersions.CommandText = @"
                        DELETE FROM bundle_versions
                        WHERE bundle_name = @bundle
                          AND version NOT IN (
                              SELECT version FROM bundle_versions
                              WHERE bundle_name = @bundle
                              ORDER BY version DESC
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
            cmd.CommandText = @"SELECT bundle_name, version, entry_path, content_hash, published_at, publisher, description
                                FROM bundle_versions WHERE bundle_name = @bundle COLLATE NOCASE
                                ORDER BY version DESC LIMIT 1;";
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
            cmd.CommandText = @"SELECT bundle_name, version, entry_path, content_hash, published_at, publisher, description
                                FROM bundle_versions WHERE bundle_name = @bundle COLLATE NOCASE AND Version = @version LIMIT 1;";
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
            cmd.CommandText = @"SELECT bundle_name, version, virtual_path, content, content_hash, size_bytes, content_type
                                FROM bundle_files
                                WHERE bundle_name = @bundle COLLATE NOCASE AND version = @version AND virtual_path = @path COLLATE NOCASE
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
            cmd.CommandText = @"SELECT bv.bundle_name, bv.version, bv.entry_path, bv.content_hash, bv.published_at, bv.publisher, bv.description
                                FROM bundle_versions bv
                                INNER JOIN (
                                    SELECT bundle_name, MAX(version) AS Version FROM bundle_versions GROUP BY bundle_name
                                ) latest ON latest.bundle_name = bv.bundle_name AND latest.Version = bv.version
                                ORDER BY bv.bundle_name COLLATE NOCASE;";
            return await ReadBundleVersionsAsync(cmd);
        }

        public async Task<IEnumerable<BundleVersionInfo>> GetVersionsAsync(string bundleName)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT bundle_name, version, entry_path, content_hash, published_at, publisher, description
                                FROM bundle_versions WHERE bundle_name = @bundle COLLATE NOCASE
                                ORDER BY version DESC;";
            cmd.AddParam("@bundle", bundleName);
            return await ReadBundleVersionsAsync(cmd);
        }

        public async Task<IEnumerable<BundleFileInfo>> GetFilesAsync(string bundleName, int version)
        {
            await EnsureInitializedAsync();
            using var connection = _dialect.CreateConnection();
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT bundle_name, version, virtual_path, content, content_hash, size_bytes, content_type
                                FROM bundle_files WHERE bundle_name = @bundle COLLATE NOCASE AND version = @version
                                ORDER BY virtual_path COLLATE NOCASE;";
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
            cmd.CommandText = @"SELECT bundle_name, version, from_path, to_path
                                FROM bundle_dependencies WHERE bundle_name = @bundle COLLATE NOCASE AND version = @version
                                ORDER BY from_path COLLATE NOCASE, to_path COLLATE NOCASE;";
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
                        INSERT INTO lineage_history
                            (tenant_id, run_at, job_name, script_path, target_table, target_column, source_tables, source_columns, operation, tags, source_file, line,
                             transformation_kind, transformation_expression, functions_applied, derived_from_descriptions)
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
                SELECT id, run_at, job_name, script_path, target_table, target_column,
                       source_tables, operation, tags, source_file, line,
                       source_columns, transformation_kind, transformation_expression, functions_applied, derived_from_descriptions, tenant_id
                FROM lineage_history
                WHERE (@tenant IS NULL OR tenant_id = @tenant) AND target_table = @table COLLATE NOCASE
                ORDER BY run_at DESC, id DESC
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
                SELECT id, run_at, job_name, script_path, target_table, target_column,
                       source_tables, operation, tags, source_file, line,
                       source_columns, transformation_kind, transformation_expression, functions_applied, derived_from_descriptions, tenant_id
                FROM (
                    SELECT *, ROW_NUMBER() OVER (
                        PARTITION BY target_table COLLATE NOCASE
                        ORDER BY run_at DESC, id DESC) AS _rn
                    FROM lineage_history
                    WHERE (@tenant IS NULL OR tenant_id = @tenant)
                      AND target_table COLLATE NOCASE IN ({string.Join(", ", paramNames)})
                )
                WHERE _rn <= @limit
                ORDER BY run_at DESC, id DESC;";
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
                SELECT id, run_at, job_name, script_path, target_table, target_column,
                       source_tables, operation, tags, source_file, line,
                       source_columns, transformation_kind, transformation_expression, functions_applied, derived_from_descriptions, tenant_id
                FROM lineage_history
                WHERE (@tenant IS NULL OR tenant_id = @tenant) AND tags LIKE @pattern
                ORDER BY run_at DESC, id DESC
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
                SELECT id, run_at, job_name, script_path, target_table, target_column,
                       source_tables, operation, tags, source_file, line,
                       source_columns, transformation_kind, transformation_expression, functions_applied, derived_from_descriptions, tenant_id
                FROM lineage_history
                WHERE (@tenant IS NULL OR tenant_id = @tenant)
                ORDER BY run_at DESC, id DESC
                LIMIT @scanLimit;";
            cmd.AddParam("@scanLimit", Math.Max(limit * 20, limit));
            cmd.AddParam("@tenant", (object?)tenantId ?? DBNull.Value);

            var latestByTarget = LineageAssetCollapse.LatestPerAsset(await ReadLineageHistoryAsync(cmd));

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
                SELECT id, run_at, job_name, script_path, target_table, target_column,
                       source_tables, operation, tags, source_file, line,
                       source_columns, transformation_kind, transformation_expression, functions_applied, derived_from_descriptions, tenant_id
                FROM lineage_history
                WHERE (@tenant IS NULL OR tenant_id = @tenant)
                ORDER BY run_at DESC, id DESC
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
                SELECT id, run_at, job_name, script_path, target_table, target_column,
                       source_tables, operation, tags, source_file, line,
                       source_columns, transformation_kind, transformation_expression, functions_applied, derived_from_descriptions, tenant_id
                FROM lineage_history
                WHERE (@tenant IS NULL OR tenant_id = @tenant) AND job_name = @jobName COLLATE NOCASE
                ORDER BY run_at DESC, id DESC
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
                SELECT id, run_at, job_name, script_path, target_table, target_column,
                       source_tables, operation, tags, source_file, line,
                       source_columns, transformation_kind, transformation_expression, functions_applied, derived_from_descriptions, tenant_id
                FROM lineage_history
                WHERE (@tenant IS NULL OR tenant_id = @tenant) AND source_tables LIKE @pattern
                ORDER BY run_at DESC, id DESC
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
                SELECT id, run_at, job_name, script_path, target_table, target_column,
                       source_tables, operation, tags, source_file, line,
                       source_columns, transformation_kind, transformation_expression, functions_applied, derived_from_descriptions, tenant_id
                FROM lineage_history
                WHERE (@tenant IS NULL OR tenant_id = @tenant)
                  AND (source_file = @sourceFile COLLATE NOCASE
                   OR script_path = @sourceFile COLLATE NOCASE)
                ORDER BY run_at DESC, id DESC
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
