# Orchestrator Configuration

> **Applies to:** Team · Enterprise · SaaS

Configure the Orchestrator service: job scheduling, concurrency throttles, sandbox execution, process spawning, warm runners, and the Orchestrator API.

ETL-SQL settings can be configured via `appsettings.json`, environment variables (replace `:` with `__`), or command-line parameters.

---

## Orchestrator API and Identity

`Orchestrator:IdentitySigningSecret` and `Orchestrator:RequireFederatedIdentity` together choose the authorization model: federated identity with per-object grants, or Solo legacy mode with no grants.

| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Orchestrator:ApiKey` | string | `""` | Secret token used to authenticate calls to the Orchestrator API. Configure from a protected source. |
| `Orchestrator:IdentitySigningSecret` | string | `""` | Dedicated 32+ byte secret used to verify short-lived Portal caller assertions. Required when federated identity is enabled. |
| `Orchestrator:RequireFederatedIdentity` | boolean | network-dependent | Requires a signed caller assertion in addition to the API key. Defaults to `true` for non-loopback listeners. `false` is Solo-only legacy mode: no principals, no grants, and the API key is a root key over the catalog. |
| `Orchestrator:PreviousApiKeys` | array | `[]` | Previously-valid API keys accepted temporarily during secret rotation. |
| `Orchestrator:MaxPreviousApiKeys` | integer | `1` | Maximum number of previous API keys accepted during rotation overlap. |
| `Orchestrator:TenantId` | string | `null` | Fixed canonical tenant authority for a Dedicated Orchestrator. If a signed Portal assertion also carries a tenant, both values must match. |

---

## Orchestrator Database and Storage

| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Orchestrator:Database:Provider` | string | `Sqlite` | Database backing storage: `Sqlite` or `Postgres`. Use `Postgres` for HA deployments. |
| `Orchestrator:Database:ConnectionString` | string | `""` | Connection details when `Postgres` provider is specified. |
| `Orchestrator:DatabasePath` | string | `null` | SQLite database path. `null` uses the canonical `%LocalAppData%/ETL-SQL/etlsql.db` default. |
| `Orchestrator:ScriptRoot` | string | `""` | Target folder for Orchestrator scripts and scheduling plans. |

---

## Statement and History Retention

| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Orchestrator:MaxStatementsPerRun` | integer | `25` | Maximum non-failed statements retained per run; failed statements are always retained. |
| `Orchestrator:MaxStatementTextLength` | integer | `512` | Maximum normalized statement-text length carried in process envelopes and durable history. |
| `Orchestrator:SuccessfulStatementMetricsRetentionDays` | integer | `7` | Days to retain statement detail for successful runs. Set to `0` to disable early detail pruning. |
| `Orchestrator:FailedStatementMetricsRetentionDays` | integer | `30` | Days to retain failed-run statement detail. Parent history pruning still removes detail with its run. |

---

## Job Throttle

| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Orchestration:JobThrottle:MaxConcurrentJobs` | integer | `0` | Max concurrent scheduled jobs. `0` resolves dynamically to logical core count. |
| `Orchestration:JobThrottle:PollInitialDelayMs` | integer | `100` | Initial delay before retrying a saturated cross-process throttle slot. |
| `Orchestration:JobThrottle:PollMaxDelayMs` | integer | `2000` | Maximum throttle retry delay after exponential backoff. |
| `Orchestration:JobThrottle:PollJitterRatio` | number | `0.2` | Symmetric jitter applied to throttle retries to prevent synchronized database polling. |
| `Orchestration:JobThrottle:SlotLeaseSeconds` | integer | `60` | Expiry for an unrenewed throttle slot owned by another HA node. |
| `Orchestration:JobThrottle:SlotHeartbeatSeconds` | integer | `20` | Renewal interval for an active cross-node throttle slot. Clamped below half the lease. |

---

## Resource Management

| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Orchestration:ResourceManagement:MaxGlobalMemoryMB` | integer | `2048` | Memory floor threshold for scheduling new background tasks. |
| `Orchestration:ResourceManagement:MaxStreamingCursors` | integer | `50` | Max open active cursors across all jobs. |
| `Orchestration:ResourceManagement:ResourceWaitTimeoutSeconds` | integer | `600` | Duration jobs wait in queue for RAM to free before timing out. |
| `Orchestration:ResourceManagement:HysteresisMemoryMB` | integer | `256` | Memory buffer required before the queue restarts paused jobs. |
| `Orchestration:ResourceManagement:SystemMemoryFloorMB` | integer | `4096` | Target free system RAM floor; new jobs wait if host free RAM is lower. |

---

## Scheduler

| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Scheduler:MetricsIntervalSeconds` | integer | `60` | Frequency of performance metrics sweeps. |
| `Scheduler:SleepIntervalSeconds` | integer | `30` | Frequency the scheduler wakes to scan database job queues. |
| `Scheduler:SessionReapIntervalMinutes` | integer | `60` | Schedule frequency to clear stale user sessions. |
| `Scheduler:ErrorSleepMs` | integer | `5000` | Recovery pause duration when database connections throw errors. |

---

## Job Process Spawning

| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Jobs:UseProcessSpawning` | boolean | `false` | When true, runs jobs in isolated OS sub-processes instead of thread tasks. |
| `Jobs:UseWarmRunner` | boolean | `false` | When true with process spawning, reuses warm `ETL-SQL runner` child processes. Falls back to one-shot spawning if a runner fails. |
| `Jobs:ExecutablePath` | string | `""` | Absolute path to `ETL-SQL.exe` when process spawning is active. |
| `Jobs:ArgumentsTemplate` | string | `""` | Overrides arguments passed to a spawned job. Supports `{ScriptFile}` and `{SessionId}`. **A custom template must keep `--json`** — see below. |
| `Jobs:TimeoutSeconds` | integer | `3600` | Maximum runtime per job before termination (1 hour). |
| `Jobs:WarmRunnerPoolSize` | integer | `2` | Maximum number of reusable runner processes for concurrent job execution. |
| `Jobs:WarmRunnerStartupTimeoutSeconds` | integer | `10` | Time allowed for a newly spawned warm runner to publish its ready handshake. |
| `Jobs:WarmRunnerBatchSize` | integer | `10000` | Batch size passed into warm runner execution sessions. |
| `Jobs:MaxConcurrentJobs` | integer | `0` | Process scale throttle. `0` = logical processor count. |

### A custom `Jobs:ArgumentsTemplate` must keep `--json`

`--json` is how a spawned job reports what it did — row count, data-quality column metrics, and rule failures. If a custom template omits it, a successful run records **success with zero rows and no data-quality metrics**. The Orchestrator logs a warning at startup when it detects this.

```jsonc
// Correct — keeps the reporting envelope
"ArgumentsTemplate": "run {ScriptFile} --json --session {SessionId}"

// Broken — row counts and quality metrics silently become zero
"ArgumentsTemplate": "run {ScriptFile} --session {SessionId}"
```

---

## Sandbox Admission and Execution

Sandbox execution routes scheduled jobs through a hardened Docker provider. It requires `SandboxAdmission` to be enabled first.

| Key | Type | Description |
| :--- | :--- | :--- |
| `Orchestration:SandboxAdmission:Enabled` | boolean | Enables ledger-backed sandbox admission. Requires a runtime-provider `ISandboxRuntimeReconciler` binding. |
| `Orchestration:SandboxAdmission:LeaseSeconds` | number | Durable admission ownership lease duration. |
| `Orchestration:SandboxAdmission:AbandonedQueueSeconds` | number | How long a queued admission may go unclaimed before reconciliation cancels it. |
| `Orchestration:SandboxExecution:Enabled` | boolean | Routes scheduled jobs through the hardened Docker provider. |
| `Orchestration:SandboxExecution:Image` | string | Full OCI image reference ending in `@sha256:...`; tags alone are refused. |
| `Orchestration:SandboxExecution:Runtime` | string | Registered `runsc`, containerd-runsc, or Kata runtime. `runc`/`crun` are never Hardened. |
| `Orchestration:SandboxExecution:WorkspaceRoot` | path | Single-use assignment roots. Must be writable by the Orchestrator and never shared with another worker. |
| `Orchestration:SandboxExecution:Profiles:{name}:MaxDurationSeconds` | number | Per-attempt wall-clock ceiling. |
| `Orchestration:SandboxExecution:Profiles:{name}:MaxMemoryBytes` | integer | Hard memory and memory+swap ceiling. |
| `Orchestration:SandboxExecution:Profiles:{name}:MaxCpuCores` | number | CPU cores the attempt may consume per wall-clock second. Required; an unbounded workload starves co-tenants. |

> [!NOTE]
> See [SaaS Tenant Isolation Architecture](../../../architecture/SaaSTenantIsolation.md) for the full sandbox profile and tenant entitlement model.

---

## Related

- [Configuration Settings Reference](../appsettings-reference.md) — full config hub
- [Engine Configuration](engine-configuration.md) — query execution settings
- [Portal Configuration](portal-configuration.md) — Portal-to-Orchestrator integration keys
- [Job Orchestration](../../orchestration/README.md)
- [Platform Administration](../README.md)
