# Configuration Settings Reference

This document is the canonical reference for all configuration options available in `appsettings.json`. 

ETL-SQL settings can be configured via `appsettings.json`, environment variables, or command-line parameters. When using environment variables, replace colons (`:`) with double underscores (`__`). For example, `Security:PathProtectionMode` maps to the environment variable `Security__PathProtectionMode`.

Additionally, many of these settings can be overridden ad-hoc for a single script session using the SQL-style `SET` command.

---

## 1. Logging

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

## 2. Security

Configures the zero-trust execution sandbox limits, folder restrictions, allowed environment variables, limits on external calls, and file-spill security.

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Security:PathProtectionMode` | string | `Restricted` | — | Controls boundary protection. Set to `Restricted` to block script reads/writes outside safe zones. |
| `Security:AllowedHosts` | array | `["*"]` | — | Hosts permitted to connect to HTTP endpoints. |
| `Security:ApprovedSafeZones` | array | `["c:\Users\chuck\scratch\ETL-SQL\samples"]` | — | Paths where scripts are permitted to write or read files when `PathProtectionMode` is restricted. |
| `Security:AllowedEnvVars` | array | `["TEMP", "USERDOMAIN", ...]` | — | Environment variables whitelisted for access within ETL scripts via the `ENV_VAR()` function. |
| `Security:MaxFileOperationsPerScript` | integer | `100` | `SET ALLOW_FILE_OPERATIONS = n`<br>or `SET MAX_FILE_OPERATIONS = n` | Limits the number of file modifications a single script can perform. |
| `Security:MaxRecursiveNestingDepth` | integer | `5` | `SET ALLOW_RECURSIVE_LAYERS = n` | Limits nesting depth when run scripts call other scripts. |
| `Security:MaxParallelDegree` | integer | `32` | `SET MAX_PARALLEL_DEGREE = n` | Maximum concurrent threads used in parallel command blocks. |
| `Security:MaxStringResultSize` | integer | `104857600` | `SET MAX_STRING_RESULT_SIZE = n` | Maximum length in bytes allowed for string results (100MB by default). |
| `Security:MaxSmtpEmailsPerScript` | integer | `100` | `SET MAX_SMTP_EMAILS_PER_SCRIPT = n` | Anti-spam limit capping emails sent in a single script run. |
| `Security:RegexMatchTimeoutMs` | integer | `1000` | `SET REGEX_MATCH_TIMEOUT = n` | Capping execution duration for regex evaluations to prevent DOS. |
| `Security:MaxInternalOperations` | integer | `100000` | — | Limit on internal loop execution steps to block infinite loops. |
| `Security:SpillEncryptionEnabled` | boolean | `true` | `SET SPILL_ENCRYPTION = ON\|OFF` | When true, buffers spilled to local disk during heavy queries are encrypted at rest. |
| `Security:SpillCompressionEnabled` | boolean | `true` | `SET SPILL_COMPRESSION = ON\|OFF` | When true, spilled buffers are compressed to save disk space. |
| `Security:SpillFormat` | string | `Arrow` | `SET SPILL_FORMAT = 'AUTO'\|'JSON'\|'PARQUET'` | Serialization format for data spills. |

---

## 3. Engine

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
| `Engine:MaxTempTablesPerScript` | integer | `100` | — | Maximum live `#temp` tables in one script. Dropping a table releases capacity; `0` disables the ceiling. |
| `Engine:MaxVariablesPerScript` | integer | `100` | — | Maximum variables in the active script scope. Redeclaration does not consume additional capacity; `0` disables the ceiling. |
| `Engine:MaxVisualsPerScript` | integer | `100` | — | Maximum live visual definitions in one report script. Replacing a visual does not consume additional capacity; `0` disables the ceiling. |
| `Engine:TelemetryEnabled` | boolean | `true` | `SET TELEMETRY = ON\|OFF` | Transmits anonymous execution metrics to help refine optimization. |
| `Engine:LineageEnabled` | boolean | `true` | `SET LINEAGE = ON\|OFF` | Automatically parses sources/targets to construct lineage maps. |
| `Engine:AuditAdHocRuns` | boolean | `false` | — | When true, every script launched via local CLI is sent to the audit server. |
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

## 4. Orchestration & Scheduler

Configures concurrency limits, memory floors, and polling intervals for job execution.

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Orchestration:JobThrottle:MaxConcurrentJobs` | integer | `0` | — | Max concurrent scheduled jobs. `0` resolves dynamically to logical core count. |
| `Orchestration:JobThrottle:PollInitialDelayMs` | integer | `100` | — | Initial delay before retrying a saturated cross-process throttle slot. |
| `Orchestration:JobThrottle:PollMaxDelayMs` | integer | `2000` | — | Maximum throttle retry delay after exponential backoff. |
| `Orchestration:JobThrottle:PollJitterRatio` | number | `0.2` | — | Symmetric jitter applied to throttle retries to prevent synchronized database polling. |
| `Orchestration:JobThrottle:SlotLeaseSeconds` | integer | `60` | — | Expiry for an unrenewed throttle slot owned by another HA node. |
| `Orchestration:JobThrottle:SlotHeartbeatSeconds` | integer | `20` | — | Renewal interval for an active cross-node throttle slot. Clamped below half the lease. |
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

## 5. Jobs

Controls task execution parameters and process boundaries.

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Jobs:UseProcessSpawning` | boolean | `false` | — | When true, runs jobs in isolated OS sub-processes instead of thread tasks. |
| `Jobs:UseWarmRunner` | boolean | `false` | — | When true with process spawning, reuses warm `ETL-SQL runner` child processes instead of launching a fresh process for every job. Falls back to one-shot spawning if a runner fails. |
| `Jobs:ExecutablePath` | string | `""` | — | Absolute path to target `ETL-SQL.exe` engine when process spawning is active. |
| `Jobs:TimeoutSeconds` | integer | `3600` | — | Maximum runtime permitted for a single job before terminating (1 hour). |
| `Jobs:WarmRunnerPoolSize` | integer | `2` | — | Maximum number of reusable runner processes for concurrent job execution. |
| `Jobs:WarmRunnerStartupTimeoutSeconds` | integer | `10` | — | Time allowed for a newly spawned warm runner to publish its ready handshake. |
| `Jobs:WarmRunnerBatchSize` | integer | `10000` | — | Batch size passed into warm runner execution sessions. |
| `Jobs:MaxConcurrentJobs` | integer | `0` | — | Process scale throttle. O denotes logical processor count. |

---

## 6. Report Player & Session

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

## 7. Connectors

Defines default settings, delays, timeouts, and credentials for remote systems.

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Connectors:Retry:MaxAttempts` | integer | `3` | — | Number of times connection attempts are retried. |
| `Connectors:Retry:BaseDelaySeconds` | float | `1.0` | — | Initial delay backing off between failed requests. |
| `Connectors:DataWarehouse:DefaultCommandTimeoutSeconds` | integer | `1800` | — | Max duration of SQL commands executed on target database servers (30 mins). |
| `Connectors:DataWarehouse:SchemaCacheTtlSeconds` | integer | `300` | — | Duration target table schemas are cached to skip query re-verification. |
| `Connectors:Ftp` | object | `{"Host": "localhost", ...}` | — | Connection settings for FTP connections. |
| `Connectors:Sftp` | object | `{"Host": "localhost", ...}` | — | Connection settings for SFTP connections. |
| `Connectors:AzureBlob` | object | `{"ConnectionString": ...}` | — | Storage string and default container for Azure Blob connectivity. |

---

## 8. Orchestrator

Configuration details for the background runner service.

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Orchestrator:ApiKey` | string | `""` | — | Secret token used to authenticate request calls to the scheduler API. |
| `Orchestrator:PreviousApiKeys` | array | `[]` | — | Rolled api keys accepted temporarily during secret rotation phases. |
| `Orchestrator:ScriptRoot` | string | `""` | — | Path target folder for orchestrator scripts and scheduling plans. |
| `Orchestrator:Database:Provider` | string | `Sqlite` | — | Database backing storage (`Sqlite` or `Postgres`). |
| `Orchestrator:Database:ConnectionString` | string | `""` | — | DB Connection details when `Postgres` provider is specified. |

---

## 9. Snippets

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Snippets:UserSnippetsPath` | string | `""` | — | Directory containing user-defined editor autocomplete snippets. |

---

## 10. Report Portal

Configuration settings for the Report Portal UI server, shared storage, and active integrations.

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Portal:DatabasePath` | string | `./portal.db` | — | Local file path for portal SQLite database. Used when provider is `Sqlite`. |
| `Portal:Database:Provider` | string | `Sqlite` | — | Database backing portal configuration state (`Sqlite` or `Postgres`). |
| `Portal:Database:ConnectionString` | string | `""` | — | Database connection details when `Postgres` provider is used (required for HA). |
| `Portal:SubscriptionRetryDelaySeconds` | integer | `60` | — | Wait time to retry failed email subscription dispatches. |
| `Portal:ScriptRootPath` | string | `./Reports` | — | Folder containing reports and dashboard scripts (`.rptsql`). |
| `Portal:SnapshotDirectory` | string | `./Snapshots` | — | Directory where PDF/CSV dashboard exports are saved. |
| `Portal:MapRootPath` | string | `./data/maps` | — | Base folder path storing GeoJSON files. |
| `Portal:DatasetRootPath` | string | `./data/datasets` | — | Folder storing shared dataset Parquet archives. |
| `Portal:Storage:Provider` | string | `Local` | — | Shared file system provider (`Local` or `Smb` / UNC). |
| `Portal:Storage:KeyRingPath` | string | `null` | — | Folder storing Data Protection decryption keys (must be shared in HA). |
| `Portal:Modules:Reporting` | boolean | `true` | — | Enables the report library entry page, reporting APIs, session cache, execution worker, dataset key validation, snapshot migration, and reporting startup reconciliation. Disabled routes return 404. |
| `Portal:Modules:Designer` | boolean | `true` | — | Enables the browser report designer entry page and API routes. Disabled routes return 404. |
| `Portal:Modules:ConnectionCatalog` | boolean | `true` | — | Enables the shared connection catalog and diagnostics API routes. Disabled routes return 404. |
| `Portal:Modules:SecretStore` | boolean | `true` | — | Enables the Portal-managed secret store API routes and secret resolution surface. Disabled routes return 404. |
| `Portal:Modules:Scheduling` | boolean | `true` | — | Enables scheduled refresh/orchestrator poller work when Reporting is also enabled. |
| `Portal:Modules:Operations` | boolean | `true` | — | Enables operational digest and native admin digest worker loops. Route fencing lands in a later modularization slice. |
| `Portal:Modules:Documentation` | boolean | `true` | — | Enables Portal-hosted documentation surfaces module flag. Route fencing lands in a later modularization slice. |
| `Portal:MaxPreviewRows` | integer | `50000` | — | Maximum preview lines displayed in GUI tables. |

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

## 11. Lineage

Controls telemetry endpoints and catalog metadata parsing rules.

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Lineage:Namespace` | string | `etl-sql` | `SET LINEAGE_NAMESPACE = 'ns'` | Default namespace namespace-tagging lineage output. |
| `Lineage:OpenLineageFile` | string | `null` | — | Local target file path to append OpenLineage JSON events. |
| `Lineage:OpenLineageEndpoint` | string | `null` | — | Target collector URL for OpenLineage telemetry. |
| `Lineage:ImportCatalogMetadata` | boolean | `false` | `SET LINEAGE_IMPORT_CATALOG = ON\|OFF` | Queries database catalogs for schema comments on first tables scans. |

---

## 12. ConnectionStrings

Standard ASP.NET Core connection configurations.

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `ConnectionStrings:DefaultConnection` | string | `Data Source=portal.db` | — | Fallback connection configuration for database targets. |

---

## References
- [Administrators Guide](../Administrators_Guide.md)
- [Report Portal Administrators Guide](../ReportPortal_Administrators_Guide.md)
- [Orchestrators Guide](../Orchestrators_Guide.md)
