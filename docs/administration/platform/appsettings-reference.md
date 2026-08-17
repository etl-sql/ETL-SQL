# Configuration Settings Reference

This document is the canonical reference for all configuration options available in `appsettings.json`. 

ETL-SQL settings can be configured via `appsettings.json`, environment variables, or command-line parameters. When using environment variables, replace colons (`:`) with double underscores (`__`). For example, `Security:PathProtectionMode` maps to the environment variable `Security__PathProtectionMode`.

Additionally, many of these settings can be overridden ad-hoc for a single script session using the SQL-style `SET` command.

---

## Logging

Configures standard logging levels, directories, retention policies, and size limits for application, script, and test outputs.

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Logging:LogLevel:Default` | string | `Information` | — | Default log level threshold. |
| `Logging:LogLevel:Microsoft` | string | `Warning` | — | Log level threshold for Microsoft libraries. |
| `Logging:LogLevel:Microsoft.AspNetCore` | string | `Warning` | — | Log level threshold for ASP.NET Core framework components. |
| `Logging:AppLog:Directory` | string | `logs/app` | — | Directory where application logs are stored. |
| `Logging:AppLog:RetentionDays` | integer | `30` | — | Number of days to retain application log files before recycling. |
| `Logging:AppLog:FileSizeLimitMb` | integer | `10` | — | Maximum size in MB of an application log file before rolling over. |
| `Logging:ScriptLog:Directory` | string | `logs/scripts` | — | Target folder where run logs for scripts are saved. |
| `Logging:ScriptLog:DefaultRetentionDays` | integer | `30` | — | Number of days to retain script execution logs. |
| `Logging:ScriptLog:FileSizeLimitMb` | integer | `10` | — | Maximum size in MB of a script log file. |
| `Logging:TestLog:Directory` | string | `logs/tests` | — | Directory where testing/smoke logs are archived. |
| `Logging:TestLog:RetentionDays` | integer | `30` | — | Retention window for test executions. |
| `Logging:TestLog:FileSizeLimitMb` | integer | `50` | — | Maximum size in MB of test log files. |

---

## Security

Configures the zero-trust execution sandbox limits, folder restrictions, allowed environment variables, limits on external calls, and file-spill security.

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Security:PathProtectionMode` | string | `Restricted` | — | Controls boundary protection. Set to `Restricted` to block script reads/writes outside safe zones. |
| `Security:AllowedHosts` | array | `["*"]` | — | Hosts permitted to connect to HTTP endpoints. |
| `Security:ApprovedSafeZones` | array | `["c:\Users\chuck\scratch\ETL-SQL\samples"]` | — | Paths where scripts are permitted to write or read files when `PathProtectionMode` is restricted. |
| `Security:AllowedEnvVars` | array | `["TEMP", "USERDOMAIN", ...]` | — | Environment variables whitelisted for access within ETL scripts via the `ENV_VAR()` function. |
| `Security:AdditionalBlockedExtensions` | array | `[]` | — | Extra administrator-defined file extensions to deny. These only add restrictions and cannot weaken built-in blocked extensions. |
| `Security:AdditionalBlockedPaths` | array | `[]` | — | Extra administrator-defined paths or path segment names to deny. Rooted entries match by canonical path prefix; relative entries match path segments. |
| `Security:MaxFileOperationsPerScript` | integer | `100` | `SET ALLOW_FILE_OPERATIONS = n`<br>or `SET MAX_FILE_OPERATIONS = n` | Limits the number of file modifications a single script can perform. |
| `Security:MaxRecursiveNestingDepth` | integer | `5` | `SET ALLOW_RECURSIVE_LAYERS = n` | Limits nesting depth when run scripts call other scripts. |
| `Security:MaxParallelDegree` | integer | `32` | `SET MAX_PARALLEL_DEGREE = n` | Maximum concurrent threads used in parallel command blocks. |
| `Security:MaxStringResultSize` | integer | `104857600` | `SET MAX_STRING_RESULT_SIZE = n` | Maximum length in bytes allowed for string results (100MB by default). |
| `Security:MaxSmtpEmailsPerScript` | integer | `100` | `SET MAX_SMTP_EMAILS_PER_SCRIPT = n` | Anti-spam limit capping emails sent in a single script run. |
| `Security:RegexMatchTimeoutMs` | integer | `1000` | `SET REGEX_MATCH_TIMEOUT = n` | Capping execution duration for regex evaluations to prevent DOS. |
| `Security:MaxInternalOperations` | integer | `100000` | — | Limit on internal loop execution steps to block infinite loops. |
| `Security:SpillEncryptionEnabled` | boolean | `true` | `SET SPILL_ENCRYPTION ON\|OFF` | When true, buffers spilled to local disk during heavy queries are encrypted at rest. |
| `Security:SpillCompressionEnabled` | boolean | `true` | `SET SPILL_COMPRESSION ON\|OFF` | When true, spilled buffers are compressed to save disk space. |
| `Security:SpillFormat` | string | `Arrow` | `SET SPILL_FORMAT = 'AUTO'\|'JSON'\|'PARQUET'` | Serialization format for data spills. |

---

## Engine

Controls parsing, query optimization, memory allocations, caching thresholds, and script execution policies.

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Engine:BatchSize` | integer | `10000` | `SET BATCHSIZE = n` | Number of rows processed per batch in streaming operations. |
| `Engine:MaxRecursiveDepth` | integer | `10000` | `SET MAX_RECURSIVE_DEPTH = n` | Maximum recursion iterations allowed for CTEs and hierarchical nodes. |
| `Engine:JoinSpillThreshold` | integer | `10000` | `SET JOIN_SPILL_THRESHOLD = n` | Row threshold at which memory-intensive JOINs spill buffers to disk. |
| `Engine:ExternalHashPartitions` | integer | `32` | `SET EXTERNAL_HASH_PARTITIONS = n` | Number of hash buckets created during out-of-core partition operations. |
| `Engine:ExternalSortChunkSize` | integer | `10000` | `SET EXTERNAL_SORT_CHUNK_SIZE = n` | Run size in rows for sorting buffers spilled to disk. |
| `Engine:WindowSpillThreshold` | integer | `10000` | `SET WINDOW_SPILL_THRESHOLD = n` | Rows in a partition before window functions spill to disk. |
| `Engine:OperatorMemoryGrantMB` | integer | `256` | `SET OPERATOR_MEMORY_GRANT = n` | RAM granted per execution operator in MB. |
| `Engine:TotalMemoryGrantMB` | integer | `-1` (auto) | — | RAM-governor ceiling for the engine's in-memory operator state; spilling/repartitioning keeps the process under it. `-1` (or unset) = auto: ~80% of physical RAM (honors container limits), floored at 512 MB. A value `> 0` sets an explicit ceiling in MB. `0` disables the governor (unbounded — can consume all RAM). |
| `Engine:MemoryGovernorPolicy` | string | `SpillOrFail` | — | Behaviour when an operator hits the ceiling and cannot reduce further: `SpillOrFail` aborts with a clear error; `SpillOnly` churns to completion (slower, higher RAM). |
| `Engine:TempTableSpillThresholdRows` | integer | `1000000` | `SET TEMP_TABLE_SPILL_THRESHOLD = n` | Rows stored in `#temp` tables before shifting from memory to disk. |
| `Engine:SubqueryCacheSize` | integer | `5000` | — | Number of unique subquery results stored in the evaluator cache. |
| `Engine:MaxLastResultRows` | integer | `5000` | `SET MAX_LAST_RESULT_ROWS = n` | Cap on visual rows kept in memory for client preview fetches. |
| `Engine:MaxInMemoryBatches` | integer | `100` | `SET MAX_IN_MEMORY_BATCHES = n` | Limit on queue batch counts stored concurrently in memory. |
| `Engine:ForeachPageSize` | integer | `10000` | `SET FOREACH_PAGE_SIZE = n` | Number of iterations per segment processed during parallel `FOREACH` loops. |
| `Engine:MaxMessages` | integer | `1000` | `SET MAX_MESSAGES = n` | Max console print lines or warning messages buffered for a script. |
| `Engine:MaxInternalOperations` | integer | `100000` | — | Limit on internal loop execution steps. |
| `Engine:MaxConnectionsPerScript` | integer | `100` | — | Maximum live non-temporary connections in one script. `0` disables the ceiling. Prefer staging and connection reuse well below this limit. |
| `Engine:MaxRowsProcessed` | integer | `0` (unlimited) | — | Rows one execution may process before it is aborted. Enforced where every statement handler accumulates rows, so it applies to any statement that moves data. A sandboxed attempt receives this from its server-owned execution profile. |
| `Engine:MaxTempTablesPerScript` | integer | `100` | — | Maximum live `#temp` tables in one script. Dropping a table releases capacity; `0` disables the ceiling. |
| `Engine:MaxVariablesPerScript` | integer | `100` | — | Maximum variables in the active script scope. Redeclaration does not consume additional capacity; `0` disables the ceiling. |
| `Engine:MaxVisualsPerScript` | integer | `100` | — | Maximum live visual definitions in one report script. Replacing a visual does not consume additional capacity; `0` disables the ceiling. |
| `Engine:TelemetryEnabled` | boolean | `true` | `SET TELEMETRY = ON\|OFF` | Transmits anonymous execution metrics to help refine optimization. |
| `Engine:LineageEnabled` | boolean | `true` | `SET LINEAGE = ON\|OFF` | Automatically parses sources/targets to construct lineage maps. |
| `Engine:AuditAdHocRuns` | boolean | `false` | `--record` / `--no-record` | When true, a script run from the local CLI is recorded in the **local** job history store and lineage catalog. Nothing is sent to a server. This is machine-wide; use `--record`/`--no-record` on `run` to decide per invocation, and `--job-name` to give a scheduled run a stable identity. |
| `Engine:ConnectionPreviewLimit` | integer | `10` | `SET CONNECTION_PREVIEW_LIMIT = n` | Rows previewed when validating connector definitions. Set to `0` to skip schema/data access during declaration and keep connections lazy until first use. |
| `Engine:DefaultHistoryLimit` | integer | `100` | — | Script run histories preserved in database storage. |
| `Engine:StartOfWeek` | string | `Monday` | `SET WEEK_START_DAY = 'day'` | Start day used by date calculations (e.g. `DATEPART(WEEK, ...)`). |
| `Engine:ScriptHashPolicy` | string | `Warn` | — | Behavior when running scripts with modified hashes (`Warn`, `Block`, or `Ignore`). |
| `Engine:CaseSensitiveComparison` | boolean | `false` | `SET CASE_SENSITIVE = ON\|OFF` | Controls case sensitivity inside in-memory engine expressions. |
| `Engine:AllowPlaintextSecrets` | boolean | `false` | `SET ALLOW_PLAINTEXT_SECRETS = ON\|OFF` | Blocks scripts from containing raw plaintext connection strings. |
| `Engine:NoSaveSensitive` | boolean | `false` | `SET NO_SAVE_SENSITIVE = ON\|OFF` | Blocks storing credentials in workspace memory caches. |
| `Engine:NoSaveConnection` | boolean | `false` | `SET NO_SAVE_CONNECTION = ON\|OFF` | Blocks saving connections to file/db stores. |
| `Engine:ConnectionEncryption` | boolean | `false` | `SET CONNECTION_ENCRYPTION = ON\|OFF` | Encrypts local connection configuration keys. |

---

## Orchestration & Scheduler

Configures concurrency limits, memory floors, and polling intervals for job execution.

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Orchestration:JobThrottle:MaxConcurrentJobs` | integer | `0` | — | Max concurrent scheduled jobs. `0` resolves dynamically to logical core count. |
| `Orchestration:JobThrottle:PollInitialDelayMs` | integer | `100` | — | Initial delay before retrying a saturated cross-process throttle slot. |
| `Orchestration:JobThrottle:PollMaxDelayMs` | integer | `2000` | — | Maximum throttle retry delay after exponential backoff. |
| `Orchestration:JobThrottle:PollJitterRatio` | number | `0.2` | — | Symmetric jitter applied to throttle retries to prevent synchronized database polling. |
| `Orchestration:JobThrottle:SlotLeaseSeconds` | integer | `60` | — | Expiry for an unrenewed throttle slot owned by another HA node. |
| `Orchestration:JobThrottle:SlotHeartbeatSeconds` | integer | `20` | — | Renewal interval for an active cross-node throttle slot. Clamped below half the lease. |
| `Orchestration:SandboxAdmission:Enabled` | boolean | `false` | — | Enables ledger-backed sandbox admission and retained-capacity reconciliation. Requires a runtime-provider `ISandboxRuntimeReconciler` binding. |
| `Orchestration:SandboxAdmission:PoolCapacities:{pool}` | integer | — | > 0 | Provider-owned capacity for one exact isolation/service-tier pool. Unknown pools never borrow capacity. |
| `Orchestration:SandboxAdmission:LeaseSeconds` | number | `120` | > 0 | Durable admission ownership lease. The active controller renews at one third of this duration. |
| `Orchestration:SandboxAdmission:ActivationPollMilliseconds` | number | `100` | > 0 | Delay before retrying a durable activation when relational capacity is unavailable. |
| `Orchestration:SandboxAdmission:ReconciliationSeconds` | number | `30` | > 0 | Interval for retaining expired leases and probing retained runtimes for proven detachment. |
| `Orchestration:SandboxAdmission:AbandonedQueueSeconds` | number | `600` | > ActivationPollMilliseconds | How long a queued admission may go unclaimed before reconciliation cancels it. A live waiter reclaims its place on every poll, so this only reaches entries whose node crashed, drained, or lost the work. |
| `Orchestration:SandboxExecution:Enabled` | boolean | `false` | — | Routes scheduled jobs through the hardened Docker provider. Requires SandboxAdmission to be enabled. |
| `Orchestration:SandboxExecution:Image` | string | — | digest-pinned OCI reference | Full engine image reference ending in `@sha256:...`; tags alone are refused. |
| `Orchestration:SandboxExecution:ImageDigest` | string | — | canonical SHA-256 | Digest that must match the image reference and Docker repository-digest evidence. |
| `Orchestration:SandboxExecution:Runtime` | string | — | allowlist | Registered `runsc`, containerd-runsc, or Kata runtime. `runc`/`crun` are never Hardened. |
| `Orchestration:SandboxExecution:HostPolicyVersion` | string | — | nonblank | Immutable host-hardening policy version recorded in provider evidence. |
| `Orchestration:SandboxExecution:PolicyVersion` | string | — | nonblank | Version of the server-owned workload profile/entitlement catalog. |
| `Orchestration:SandboxExecution:BindingVersion` | string | — | nonblank | Version of the deployment/runtime binding carried into every attempt. |
| `Orchestration:SandboxExecution:WorkspaceRoot` | path | — | absolute | Single-use assignment roots. Must be writable by the Orchestrator and never shared with another worker assignment. |
| `Orchestration:SandboxExecution:ArtifactRoot` | path | — | absolute | Append-only content-addressed script artifact root. |
| `Orchestration:SandboxExecution:SessionRoot` | path | — | absolute | Parent of tenant-specific persistent checkpoint/session roots. |
| `Orchestration:SandboxExecution:MachineKeyRoot` | path | — | absolute | Parent containing one provisioned `<tenant-id>.key` file per tenant (minimum 32 characters, no reparse points). |
| `Orchestration:SandboxExecution:DedicatedTenantId` | string | — | paired | Fixed tenant accepted by a Dedicated worker; must match `Orchestrator:TenantId`. |
| `Orchestration:SandboxExecution:DedicatedPoolId` | string | — | paired | Exact non-borrowing pool accepted by a Dedicated worker. Required with `DedicatedTenantId`. |
| `Orchestration:SandboxExecution:IopsThrottleDevice` | string | — | absolute device path | Host block device carrying sandbox I/O (for example `/dev/sda`). Required before any profile may declare `MaxIops`. |
| `Orchestration:SandboxExecution:Profiles:{name}:PoolId` | string | — | configured admission pool | Physical pool selected only by server policy. |
| `Orchestration:SandboxExecution:Profiles:{name}:IsolationTier` | enum | — | `Hardened` or `Dedicated` | Minimum provider evidence required before tenant code starts. |
| `Orchestration:SandboxExecution:Profiles:{name}:MaxDurationSeconds` | number | — | > 0 | Per-attempt wall-clock ceiling. |
| `Orchestration:SandboxExecution:Profiles:{name}:MaxMemoryBytes` | integer | — | > 0 | Hard memory and memory+swap ceiling. |
| `Orchestration:SandboxExecution:Profiles:{name}:MaxScratchBytes` | integer | — | > 0 | Size of the assignment-local noexec/nosuid/nodev tmpfs. |
| `Orchestration:SandboxExecution:Profiles:{name}:MaxProcesses` | integer | — | > 0 | Container PID ceiling. |
| `Orchestration:SandboxExecution:Profiles:{name}:MaxCpuCores` | number | — | > 0 | CPU cores the attempt may consume per wall-clock second (`--cpus`). Required: an unbounded workload starves every co-tenant on the host. |
| `Orchestration:SandboxExecution:Profiles:{name}:MaxIops` | integer | — | > 0 when present | Block-I/O ceiling applied to reads and writes. Requires `IopsThrottleDevice`; a host without one refuses the work rather than running it unthrottled. |
| `Orchestration:SandboxExecution:Profiles:{name}:MaxConnectorConcurrency` | integer | — | > 0 | Concurrent connector connections one attempt may hold, injected as the engine's `Engine:MaxConnectionsPerScript`. Server-owned, so it is not whatever the worker image happens to default to. |
| `Orchestration:SandboxExecution:Profiles:{name}:MaxRows` | integer | — | > 0 when present | Rows one attempt may process before the engine aborts it, injected as `Engine:MaxRowsProcessed`. Rows are a unit only the engine can count, so this is enforced in the engine rather than by the runtime. |
| `Orchestration:SandboxExecution:Tenants:{tenant}:DefaultProfile` | string | — | existing profile | Default server-owned profile for the tenant. |
| `Orchestration:SandboxExecution:Tenants:{tenant}:AllowedProfiles` | array | — | nonempty | Exact profile entitlements; workload metadata may request only one of these names. |
| `Orchestration:SandboxExecution:Tenants:{tenant}:Weight` | integer | — | 1–16 | Fair-admission weight. |
| `Orchestration:SandboxExecution:Tenants:{tenant}:MaxConcurrentAttempts` | integer | — | > 0 | Tenant running-attempt ceiling. |
| `Orchestration:SandboxExecution:Tenants:{tenant}:MaxQueuedAttempts` | integer | — | > 0 | Tenant queue backpressure ceiling. |
| `Orchestration:ResourceManagement:MaxGlobalMemoryMB` | integer | `2048` | — | Memory floor threshold for scheduling new background tasks (2GB). |
| `Orchestration:ResourceManagement:MaxStreamingCursors` | integer | `50` | — | Max open active cursors across all jobs. |
| `Orchestration:ResourceManagement:ResourceWaitTimeoutSeconds` | integer | `600` | — | Duration jobs will wait in queue for RAM to free up before timing out. |
| `Orchestration:ResourceManagement:HysteresisMemoryMB` | integer | `256` | — | Memory buffer required before queue restarts paused jobs. |
| `Orchestration:ResourceManagement:SystemMemoryFloorMB` | integer | `4096` | — | Target free system RAM floor. New jobs wait if host free RAM is lower. |
| `Scheduler:MetricsIntervalSeconds` | integer | `60` | — | Frequency of performance metrics sweeps. |
| `Scheduler:SleepIntervalSeconds` | integer | `30` | — | Frequency the scheduler wakes to scan database job queues. |
| `Scheduler:SessionReapIntervalMinutes` | integer | `60` | — | Schedule frequency to clear stale user sessions. |
| `Scheduler:ErrorSleepMs` | integer | `5000` | — | Recovery pause duration when database connections throw errors. |

---

## Jobs

Controls task execution parameters and process boundaries.

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Jobs:UseProcessSpawning` | boolean | `false` | — | When true, runs jobs in isolated OS sub-processes instead of thread tasks. |
| `Jobs:UseWarmRunner` | boolean | `false` | — | When true with process spawning, reuses warm `ETL-SQL runner` child processes instead of launching a fresh process for every job. Falls back to one-shot spawning if a runner fails. |
| `Jobs:ExecutablePath` | string | `""` | — | Absolute path to target `ETL-SQL.exe` engine when process spawning is active. |
| `Jobs:ArgumentsTemplate` | string | `""` | — | Overrides the arguments passed to a spawned job. Supports `{ScriptFile}` and `{SessionId}`. Empty uses the built-in `run {ScriptFile} --json`. **A custom template must keep `--json`** — see below. One-run variable overrides are appended as separate `--var @name=value` arguments after either path. Named-checkpoint recovery also appends `--resume` and, when the template omitted it, `--session <id>`. |
| `Jobs:TimeoutSeconds` | integer | `3600` | — | Maximum runtime permitted for a single job before terminating (1 hour). |
| `Jobs:WarmRunnerPoolSize` | integer | `2` | — | Maximum number of reusable runner processes for concurrent job execution. |
| `Jobs:WarmRunnerStartupTimeoutSeconds` | integer | `10` | — | Time allowed for a newly spawned warm runner to publish its ready handshake. |
| `Jobs:WarmRunnerBatchSize` | integer | `10000` | — | Batch size passed into warm runner execution sessions. |
| `Jobs:MaxConcurrentJobs` | integer | `0` | — | Process scale throttle. O denotes logical processor count. |

### A custom `Jobs:ArgumentsTemplate` must keep `--json`

`--json` is how a spawned job reports what it did. The scheduler reads a single JSON envelope from
the child's output to learn the row count, the data-quality column metrics, and the rule failures.

If a custom template omits it there is no envelope, and the scheduler falls back to the process exit
code alone. A successful run then records **success with zero rows and no data-quality metrics** —
no error, no warning in the run history, just numbers that are quietly always zero. The failure is
silent by construction, and it lands only on deployments that customised the template.

The Orchestrator logs a warning at startup when it detects this, but the setting is worth getting
right rather than relying on someone reading the log:

```jsonc
// Correct — keeps the envelope
"ArgumentsTemplate": "run {ScriptFile} --json --session {SessionId}"

// Broken — row counts and quality metrics silently become zero
"ArgumentsTemplate": "run {ScriptFile} --session {SessionId}"
```

---

## Report Player & Session

Settings for direct HTML report generation and session lifecycle.

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `ReportPlayer:Port` | integer | `5200` | — | Port listening for report preview sessions. |
| `ReportPlayer:ExecutionTimeoutSeconds` | integer | `300` | — | Max timeout for generating an interactive preview chart (5 mins). |
| `Session:StaleSessionRetentionDays` | integer | `7` | — | Days to preserve inactive user session records. |
| `Session:PersistentSessionTTLHours` | integer | `24` | — | TTL duration for cookie session configurations. |
| `Session:PersistenceDefault` | boolean | `true` | — | Saves session histories by default. |
| `Session:Root` | string | `null` | — | Path to store user session cache objects (defaults to temp/appdata). |

---

## Connectors

Defines default settings, delays, timeouts, and credentials for remote systems.

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Connectors:Retry:MaxAttempts` | integer | `3` | — | Number of times connection attempts are retried. |
| `Connectors:Retry:BaseDelaySeconds` | float | `1.0` | — | Initial delay backing off between failed requests. |
| `Connectors:DataWarehouse:DefaultCommandTimeoutSeconds` | integer | `1800` | — | Max duration of SQL commands executed on target database servers (30 mins). |
| `Connectors:DataWarehouse:SchemaCacheTtlSeconds` | integer | `300` | — | Duration target table schemas are cached to skip query re-verification. |
| `Connectors:DataWarehouse:SchemaSoftRefreshIntervalSeconds` | integer | `300` | — | Age at which cached metadata remains usable but triggers background refresh. |
| `Connectors:DataWarehouse:SchemaDiskCacheMaxAgeDays` | integer | `14` | — | Maximum age for on-disk schema cache files before they are ignored and pruned. |
| `Connectors:Ftp` | object | `{"Host": "localhost", ...}` | — | Connection settings for FTP connections. |
| `Connectors:Sftp` | object | `{"Host": "localhost", ...}` | — | Connection settings for SFTP connections. |
| `Connectors:AzureBlob` | object | `{"ConnectionString": ...}` | — | Storage string and default container for Azure Blob connectivity. |

---

## Orchestrator

Configuration details for the background runner service.

`IdentitySigningSecret` and `RequireFederatedIdentity` together choose the authorization model:
federated identity with per-object grants, or Solo legacy mode with none. What each verb then
requires is listed in the
[permission matrix](../orchestration/orchestrator-portal.md#permission-matrix); why the Portal holds
the principals is in
[the Portal is the control plane](../portal/orchestrator-integration.md#the-portal-is-the-control-plane).

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Orchestrator:ApiKey` | string | `""` | — | Secret token used to authenticate request calls to the scheduler API. |
| `Orchestrator:IdentitySigningSecret` | string | `""` | — | Dedicated 32+ byte secret used to verify short-lived Portal caller assertions. Required when federated identity is enabled. |
| `Orchestrator:RequireFederatedIdentity` | boolean | network-dependent | — | Requires a signed caller assertion in addition to the API key. Defaults to `true` for non-loopback listeners. `false` is Solo-only legacy mode: no principals, no grants, and the API key is a root key over the catalog. The mode is reported at startup and on `GET /health`. |
| `Orchestrator:PreviousApiKeys` | array | `[]` | — | Rolled api keys accepted temporarily during secret rotation phases. |
| `Orchestrator:MaxPreviousApiKeys` | integer | `1` | — | Maximum number of previous API keys accepted during a temporary rotation overlap. |
| `Orchestrator:ScriptRoot` | string | `""` | — | Path target folder for orchestrator scripts and scheduling plans. |
| `Orchestrator:DatabasePath` | string | `null` | — | SQLite database path. `null` uses the canonical `%LocalAppData%/ETL-SQL/etlsql.db` default. |
| `Orchestrator:MaxStatementsPerRun` | integer | `25` | — | Maximum non-failed statements retained per run; failed statements are always retained. |
| `Orchestrator:MaxStatementTextLength` | integer | `512` | — | Maximum normalized statement-text length carried in process envelopes and durable history. |
| `Orchestrator:SuccessfulStatementMetricsRetentionDays` | integer | `7` | — | Retains statement detail for successful runs for this many days. Set either statement-retention value to `0` to disable early detail pruning. |
| `Orchestrator:FailedStatementMetricsRetentionDays` | integer | `30` | — | Retains failed-run statement detail longer; parent history pruning still removes detail with its run. |
| `Orchestrator:Database:Provider` | string | `Sqlite` | — | Database backing storage (`Sqlite` or `Postgres`). |
| `Orchestrator:Database:ConnectionString` | string | `""` | — | DB Connection details when `Postgres` provider is specified. |
| `Orchestrator:TenantId` | string | `null` | — | Fixed canonical tenant authority for a Dedicated Orchestrator. If a signed Portal assertion also carries a tenant, both values must match. |

---

## Snippets

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Snippets:UserSnippetsPath` | string | `""` | — | Directory containing user-defined editor autocomplete snippets. |

---

## Portal

Configuration settings for the Portal UI server, shared storage, and active integrations.

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Portal:DatabasePath` | string | `./portal.db` | — | Local file path for portal SQLite database. Used when provider is `Sqlite`. |
| `Portal:TenantId` | string | unset | — | Server-owned tenant identity for a host-fixed Managed Dedicated Portal. Required for SaaS portability export identity and included in reviewed support/export evidence. |
| `Portal:SharedTenancy:Enabled` | boolean | `false` | — | Enables fail-closed Shared tenant context enforcement. |
| `Portal:SharedTenancy:LifecycleManagementKey` | string | unset | — | Separate 32+ character platform credential enabling Shared lifecycle APIs and request fencing. Supply from protected configuration; it is not a tenant credential. |
| `Portal:SharedTenancy:DefaultRelease` | string | `unversioned` | — | Server-owned initial Shared release assignment used by signed provisioning. |
| `Portal:SharedTenancy:DefaultMaxConcurrentJobs` | integer | `1` | — | Initial per-tenant scheduled-job concurrency assigned by provisioning. |
| `Portal:SharedTenancy:DefaultMaxStorageMb` | integer | `1024` | — | Initial per-tenant storage assignment; minimum `128`. |
| `Portal:SharedTenancy:DefaultMaxReportSessions` | integer | `1` | — | Initial per-tenant interactive report-session assignment. |
| `Portal:Database:Provider` | string | `Sqlite` | — | Database backing portal configuration state (`Sqlite` or `Postgres`). |
| `Portal:Database:ConnectionString` | string | `""` | — | Database connection details when `Postgres` provider is used (required for HA). |
| `Portal:Orchestrator:ApiUrl` | string | `http://localhost:5001` | — | Base URL of the Orchestrator Service. |
| `Portal:Orchestrator:ApiKey` | string | `""` | — | Service credential sent to the Orchestrator; configure from a protected source. |
| `Portal:Orchestrator:IdentitySigningSecret` | string | `""` | — | Dedicated 32+ byte secret used to sign short-lived caller assertions; must match the Orchestrator. |
| `Portal:Orchestrator:PollIntervalSeconds` | integer | `60` | — | How often the Portal polls Orchestrator job history for dataset-refresh and subscription completions. The Portal runs in degraded mode (cached snapshots) while the Orchestrator is unreachable. |
| `Portal:SubscriptionRetryDelaySeconds` | integer | `60` | — | Wait time to retry failed email subscription dispatches. |
| `Portal:ScriptRootPath` | string | `./Reports` | — | Folder containing reports and dashboard scripts (`.rptsql`). |
| `Portal:SnapshotDirectory` | string | `./Snapshots` | — | Directory where PDF/CSV dashboard exports are saved. |
| `Portal:MapRootPath` | string | `./data/maps` | — | Base folder path storing GeoJSON files. |
| `Portal:DatasetRootPath` | string | `./data/datasets` | — | Folder storing shared dataset Parquet archives. |
| `Portal:Storage:Provider` | string | `Local` | — | Shared file system provider (`Local` or `Smb` / UNC). |
| `Portal:Storage:KeyRingPath` | string | `null` | — | Folder storing Data Protection decryption keys (must be shared in HA). |
| `Portal:Modules:Reporting` | boolean | `true` | — | Enables the report library entry page, reporting APIs, session cache, execution worker, dataset key validation, snapshot migration, and reporting startup reconciliation. Disabled routes return 404. |
| `Portal:Modules:Designer` | boolean | `true` | — | Enables the browser report designer entry page and API routes. Disabled routes return 404. |
| `Portal:Studio:Mode` | enum | `CatalogOnly` | — | Server-side Studio boundary: `Disabled`, `CatalogOnly`, or `SourceControlled`. |
| `Portal:Studio:RoleCapabilities:<role>` | string array | `[]` | — | Explicit Studio capabilities granted to a role. Empty mappings deny authoring; role names never bypass capability checks. |
| `Portal:Modules:ConnectionCatalog` | boolean | `true` | — | Enables the shared connection catalog and diagnostics API routes. Disabled routes return 404. |
| `Portal:Modules:SecretStore` | boolean | `true` | — | Enables the Portal-managed secret store API routes and secret resolution surface. Disabled routes return 404. |
| `Portal:Modules:Scheduling` | boolean | `true` | — | Enables scheduled refresh/orchestrator poller work when Reporting is also enabled. |
| `Portal:Modules:Operations` | boolean | `true` | — | Enables operational digest and native admin digest worker loops. Route fencing lands in a later modularization slice. |
| `Portal:Modules:Documentation` | boolean | `true` | — | Enables Portal-hosted documentation surfaces module flag. Route fencing lands in a later modularization slice. |
| `Portal:MaxPreviewRows` | integer | `50000` | — | Maximum preview lines displayed in GUI tables. |
| `Portal:Dataset:PreviewCacheMaxRows` | integer | `250000` | — | Global row-weight budget for in-memory dataset preview cache entries. |
| `Portal:DesignerLimits:MaxDataPreviewRows` | integer | `100` | `1`–`1000` | Maximum rows returned by an interactive Studio run or governed source/temp-table preview. |
| `Portal:DesignerLimits:MaxDataPreviewBytes` | integer | `262144` | `1024`–`16777216` | Maximum serialized row payload returned by an interactive Studio run or preview. |
| `Portal:DesignerLimits:MaxDataPreviewSeconds` | integer | `15` | `1`–`300` | Wall-clock timeout for an interactive Studio run or preview. |

### Portal Modules (`Portal:Modules`)
- `Reporting` (default: `true`): Report catalog, report player, datasets, and subscriptions.
- `Designer` (default: `true`): Browser report designer and design-time APIs.
- `ConnectionCatalog` (default: `true`): Shared connection catalog APIs and diagnostics.
- `SecretStore` (default: `true`): Portal-managed secret vault APIs and secret resolution.
- `Scheduling` (default: `true`): Refresh scheduling, orchestrator polling, and scheduled work.
- `Operations` (default: `true`): Operational health, fleet status, audit, and administrative telemetry.
- `Documentation` (default: `true`): Portal-hosted documentation surfaces.

These flags are the shared configuration contract for Portal modularization. Reporting and Designer
fence their frontend entry pages and API routes; ConnectionCatalog and SecretStore fence their admin
API routes. Reporting, Scheduling, and Operations also fence their owned background worker loops.
Security, identity, audit, database migration, and node heartbeat services remain active because they
protect the host itself.

### Portal Studio (`Portal:Studio`)

- `Mode=Disabled` removes the entry page and all authoring APIs.
- `Mode=CatalogOnly` permits only explicitly granted catalog read/preview/run/save operations and
  returns 404 for external ingress, path-based publish, commit, and push. The Studio home creates
  active reports through an internal catalog artifact key only when the caller has both
  `ScriptSave` and `ReportPublish`; that key is not exposed by the Studio API.
- `Mode=SourceControlled` allows those external/source operations only when their individual
  capabilities are also granted.
- Capability names are `StudioAccess`, `ScriptRead`, `ScriptPreview`, `ScriptRun`, `ScriptSave`,
  `ReportPublish`, `ScriptIngress`, `SourceCommit`, and `SourcePush`.
- `SourcePush` is checked separately whenever `SourceControl.PushOnSave=true`.
- `/studio.html`, `/designer.html`, and their APIs return 404 when Studio is disabled. Other Portal
  workspaces show the Studio navigation only after the capability-aware session endpoint succeeds.
- `POST /api/designer/data-preview` requires `ScriptPreview`. Shared-source previews resolve the
  caller's tenant-scoped catalog entry and ACL before the server constructs a query; `#temp`
  previews replay only the read-only prefix that materializes the selected table. Results are
  redacted, cancellable, audited through the interactive-run boundary, and bounded by
  `Portal:DesignerLimits:MaxDataPreviewRows`, `MaxDataPreviewBytes`, and `MaxDataPreviewSeconds`.

Certified topology profiles include:
- **Gateway node**: `Reporting=false`, `Designer=false`, `Scheduling=false`, `Operations=false`,
  `ConnectionCatalog=true`, `SecretStore=true`.
- **Reporting node**: `Reporting=true`, `Designer=false`, `Scheduling=false`, `Operations=false`,
  `ConnectionCatalog=false`, `SecretStore=false`.

### Portal Resource Controls (`Portal:Resources`)
- `MaxConcurrentReportExecutions` (default: `4`): Max total active queries allowed.
- `MaxConcurrentExecutionsPerUser` (default: `2`): Concurrency cap per user.
- `MaxConcurrentExecutionsPerGroup` (default: `0`): Concurrency limit per AD group (0 = unlimited).
- `InteractiveExecutionWeight` (default: `2`): Concurrency cost weight for real-time user clicks.
- `RefreshExecutionWeight` (default: `1`): Concurrency cost weight for background dashboard refreshes.
- `ExecutionTimeoutSeconds` (default: `300`): Max runtime allowed for portal queries.
- `SessionCacheMaxSize` (default: `50`): Max concurrent cached sessions stored.
- `SessionCacheTtlMinutes` (default: `30`): TTL duration for cached interactive sessions.
- `PersistAdHocInteractions` (default: `true`): Saves dashboard parameters on close.
- `SnapshotRetentionPerReport` (default: `20`): Maximum history snapshots retained per visual.
- `StorageUsageSampleIntervalSeconds` (default: `30`): Background cadence for dataset/snapshot storage usage samples.
- `StorageUsageSampleTimeoutSeconds` (default: `10`): Per-sample timeout before retaining the previous successful storage usage values and surfacing failure telemetry.
- `StorageUsageSampleMaxFiles` (default: `100000`): Maximum files visited per storage root sample before retaining the previous successful values and surfacing failure telemetry.

### Portal Load Balancer (`Portal:LoadBalancer`)
- `SessionAffinityEnabled` (default: `true`): Emits a cookie indicating target node.
- `SessionAffinityCookieName` (default: `ETLSQL_PORTAL_AFFINITY`): Cookie name load balancers use to route requests stickily.
- `SessionAffinityCookieMinutes` (default: `480`): Duration of sticky session cookie.

### Portal JWT Secrets (`Portal:Jwt`)
- `Secret` (default: `""`): Signing secret token for JWT authorization cookies. Must be 32+ characters in production.
- `PreviousSecrets` (default: `[]`): Older keys accepted during token rotation phases.
- `ExpiryMinutes` (default: `60`): Lifetime duration of access tokens.
- `RefreshExpiryDays` (default: `7`): Lifetime duration of refresh tokens.

### Portal Security (`Portal:Security`)
- `FrameAncestors` (default: `[]`): Whitelist of URLs allowed to iframe the Portal (Content Security Policy headers).

### Portal Rate Limiting (`Portal:RateLimit`)
- `AuthPermitLimit` (default: `20`): Auth requests permitted in the rate window.
- `AuthWindowSeconds` (default: `60`): Rate limit tracking duration for Auth requests.
- `AnonymousTokenPermitLimit` (default: `60`): Guest requests permitted.
- `AnonymousTokenWindowSeconds` (default: `60`): Rate limit window for guest accounts.

### Portal Dataset Cryptography (`Portal:Dataset`)
- `AtRestKey` (default: `""`): Base64 32-byte key used to encrypt cached Parquet datasets. Required in production.
- `AtRestKeyVersion` (default: `v1`): Current encryption key version identifier.
- `PreviousAtRestKeys` (default: `{}`): Map of older keys to decrypt existing historical datasets.
- `AllowMachineFallback` (default: `false`): Permits OS-level machine key fallbacks. Disable in multi-host HA clusters.
- `PreviewCacheMaxRows` (default: `250000`): Global row-weight budget for cached dataset previews. Each cache entry is weighted by the number of preview rows loaded.

`Portal:Dataset` is the compatibility configuration for existing deployments. New Enterprise
deployments should use `Portal:KeyManagement`, which keeps resolved material outside appsettings,
portable exports, job payloads, and execution images:

```json
{
  "Portal": {
    "TenantId": "tenant-acme",
    "KeyManagement": {
      "Enabled": true,
      "Bindings": [
        { "Purpose": "Dataset", "Version": "v1", "KeyId": "dataset-key", "EnvironmentVariable": "ETLSQL_KEY_DATASET_V1", "IsCurrent": true },
        { "Purpose": "Credential", "Version": "v1", "KeyId": "credential-key", "EnvironmentVariable": "ETLSQL_KEY_CREDENTIAL_V1", "IsCurrent": true },
        { "Purpose": "Artifact", "Version": "v1", "KeyId": "artifact-key", "EnvironmentVariable": "ETLSQL_KEY_ARTIFACT_V1", "IsCurrent": true },
        { "Purpose": "Checkpoint", "Version": "v1", "KeyId": "checkpoint-key", "EnvironmentVariable": "ETLSQL_KEY_CHECKPOINT_V1", "IsCurrent": true }
      ]
    }
  }
}
```

Each environment variable must contain a distinct base64 value decoding to at least 32 bytes. The
Portal derives the scope from its configured `TenantId` (or `portal-host` for a non-tenant host),
never from a request or job. Previous versions remain as additional bindings with
`IsCurrent: false`; exactly one current binding is required per purpose. Configuration exports carry
only the non-secret binding metadata and environment-variable names.

In Shared mode, each binding instead requires a server-configured `Scope` naming its tenant. Every
tenant must have independently resolvable current bindings for all four purposes; startup fails if
any tenant/purpose is absent. Equal version names are safe because scope is part of the provider key:

```json
{
  "Portal": {
    "SharedTenancy": { "Enabled": true },
    "KeyManagement": {
      "Enabled": true,
      "Bindings": [
        { "Scope": "tenant-alpha", "Purpose": "Dataset", "Version": "v1", "KeyId": "alpha-dataset", "EnvironmentVariable": "ETLSQL_ALPHA_DATASET_V1", "IsCurrent": true },
        { "Scope": "tenant-beta", "Purpose": "Dataset", "Version": "v1", "KeyId": "beta-dataset", "EnvironmentVariable": "ETLSQL_BETA_DATASET_V1", "IsCurrent": true }
      ]
    }
  }
}
```

The shortened example shows the namespace rule only; production configuration must also provide
Credential, Artifact, and Checkpoint bindings for both tenants. `Scope` is deployment configuration,
not a request, token, route, or job-payload selector. Dedicated deployments may omit `Scope`; if
present, it must exactly match `Portal:TenantId`.

### Portal Identity Providers (`Portal:Identity`)

Defines authentication configuration. Supported providers: `Local`, `Oidc` (OpenID Connect), and `Ldap`.

- `Provider` (default: `Local`): Main authentication provider model. Use `Local` or `Oidc`; LDAP logins are enabled alongside the selected provider when `Ldap:Enabled` is true.
- **OIDC Configuration (`Portal:Identity:Oidc`)**:
  - `Enabled` (default: `false`): Enables federated OIDC login and startup validation.
  - `Authority`: HTTPS identity authority/discovery endpoint URL.
  - `ClientId`: Client application ID.
  - `ClientSecret`: Confidential client secret; provide through `Portal__Identity__Oidc__ClientSecret` or another protected configuration source.
  - `TenantId`: Optional target directory tenant ID.
  - `Scopes` (default: `["openid", "profile", "email"]`): Authorization scopes. `openid` is required.
  - `CallbackPath` (default: `/api/auth/oidc/callback`): Redirect path registered with the identity provider.
  - `PostLoginRedirectPath` (default: `/index.html`): Portal page loaded after the callback establishes the session.
  - `GroupClaimTypes`: Array of claims parsed to resolve user groups (e.g. `["groups", "roles"]`).
  - `UsernameClaimType` (default: `preferred_username`): Claim used as the portal username.
  - `EmailClaimType` (default: `email`): Claim used as the user's email address.
  - `AdditionalAudiences` (default: `[]`): Extra accepted id_token audiences beyond `ClientId`.
  - `RequiredClaims` (default: `[]`): Claim types that must be present in the validated id_token.
  - `ClockSkewSeconds` (default: `60`): Allowed id_token lifetime clock skew.
- **LDAP Configuration (`Portal:Identity:Ldap`)**:
  - `Enabled` (default: `false`): Enables LDAP verification checks.
  - `Server` (default: `localhost`): Domain controller address.
  - `Port` (default: `389`): Target directory port.
  - `UseSsl` (default: `false`): Runs LDAP queries over secure connection channels.
  - `AllowSelfSignedCertificates` (default: `false`): Allows self-signed LDAP TLS certificates for isolated development/test directories.
  - `Domain` (default: `""`): Active Directory DNS or NetBIOS domain name.
  - `BaseDn`: Base Distinguished Name for scope searches.
  - `ServiceUser` / `ServicePassword`: LDAP bind service account.
  - `RoleMappings`: JSON key-value map mapping LDAP groups to portal roles (e.g. `"GG-Admins": "Admin"`).

---

## Lineage

Controls telemetry endpoints and catalog metadata parsing rules.

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Lineage:Namespace` | string | `etl-sql` | `SET LINEAGE_NAMESPACE = 'ns'` | Default namespace namespace-tagging lineage output. |
| `Lineage:OpenLineageFile` | string | `null` | — | Local target file path to append OpenLineage JSON events. |
| `Lineage:OpenLineageEndpoint` | string | `null` | — | Target collector URL for OpenLineage telemetry. |
| `Lineage:ImportCatalogMetadata` | boolean | `false` | `SET LINEAGE_IMPORT_CATALOG = ON\|OFF` | Queries database catalogs for schema comments on first tables scans. |

---

## ConnectionStrings

Standard ASP.NET Core connection configurations.

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `ConnectionStrings:DefaultConnection` | string | `Data Source=portal.db` | — | Fallback connection configuration for database targets. |

---

## References
- [Platform Administration](README.md)
- [Portal Administration](../portal/README.md)
- [Orchestration](../orchestration/README.md)
- [SET Commands](../../reference/set-commands/README.md)
