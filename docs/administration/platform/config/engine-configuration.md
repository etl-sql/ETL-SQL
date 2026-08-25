# Engine Configuration

> **Applies to:** Solo · Team · Enterprise · SaaS

Configure query execution behavior: batch sizes, memory governor, spill thresholds, caching, and execution policy controls.

ETL-SQL settings can be configured via `appsettings.json`, environment variables (replace `:` with `__`), or command-line parameters. Many keys can also be overridden per-session using the `SET` command.

---

## Batch and Processing Settings

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Engine:BatchSize` | integer | `10000` | `SET BATCHSIZE = n` | Number of rows processed per batch in streaming operations. |
| `Engine:MaxRecursiveDepth` | integer | `10000` | `SET MAX_RECURSIVE_DEPTH = n` | Maximum recursion iterations allowed for CTEs and hierarchical nodes. |
| `Engine:ForeachPageSize` | integer | `10000` | `SET FOREACH_PAGE_SIZE = n` | Number of iterations per segment processed during parallel `FOREACH` loops. |

---

## Memory Governor

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Engine:TotalMemoryGrantMB` | integer | `-1` (auto) | — | RAM ceiling for in-memory operator state. `-1` = auto (~80% of physical RAM, floored at 512 MB). `0` disables the governor (unbounded — can consume all RAM). |
| `Engine:MemoryGovernorPolicy` | string | `SpillOrFail` | — | Behavior when an operator hits the ceiling: `SpillOrFail` aborts with a clear error; `SpillOnly` churns to completion (slower, higher RAM). |
| `Engine:OperatorMemoryGrantMB` | integer | `256` | `SET OPERATOR_MEMORY_GRANT = n` | RAM granted per execution operator in MB. |

---

## Spill Thresholds

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Engine:JoinSpillThreshold` | integer | `10000` | `SET JOIN_SPILL_THRESHOLD = n` | Row threshold at which memory-intensive JOINs spill buffers to disk. |
| `Engine:ExternalHashPartitions` | integer | `32` | `SET EXTERNAL_HASH_PARTITIONS = n` | Number of hash buckets created during out-of-core partition operations. |
| `Engine:ExternalSortChunkSize` | integer | `10000` | `SET EXTERNAL_SORT_CHUNK_SIZE = n` | Run size in rows for sorting buffers spilled to disk. |
| `Engine:WindowSpillThreshold` | integer | `10000` | `SET WINDOW_SPILL_THRESHOLD = n` | Rows in a partition before window functions spill to disk. |
| `Engine:TempTableSpillThresholdRows` | integer | `1000000` | `SET TEMP_TABLE_SPILL_THRESHOLD = n` | Rows stored in `#temp` tables before shifting from memory to disk. |

---

## Resource Ceilings

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Engine:SubqueryCacheSize` | integer | `5000` | — | Unique subquery results stored in the evaluator cache. |
| `Engine:MaxLastResultRows` | integer | `5000` | `SET MAX_LAST_RESULT_ROWS = n` | Cap on visual rows kept in memory for client preview fetches. |
| `Engine:MaxInMemoryBatches` | integer | `100` | `SET MAX_IN_MEMORY_BATCHES = n` | Limit on queue batch counts stored concurrently in memory. |
| `Engine:MaxMessages` | integer | `1000` | `SET MAX_MESSAGES = n` | Max console print lines or warning messages buffered for a script. |
| `Engine:MaxInternalOperations` | integer | `100000` | — | Limit on internal loop execution steps. |
| `Engine:MaxConnectionsPerScript` | integer | `100` | — | Maximum live non-temporary connections in one script. `0` disables the ceiling. |
| `Engine:MaxRowsProcessed` | integer | `0` (unlimited) | — | Rows one execution may process before being aborted. Enforced across all statement handlers. A sandboxed attempt receives this from its server-owned execution profile. |
| `Engine:MaxTempTablesPerScript` | integer | `100` | — | Maximum live `#temp` tables in one script. Dropping a table releases capacity. `0` disables the ceiling. |
| `Engine:MaxVariablesPerScript` | integer | `100` | — | Maximum variables in the active script scope. Redeclaration does not consume additional capacity. `0` disables the ceiling. |
| `Engine:MaxVisualsPerScript` | integer | `100` | — | Maximum live visual definitions in one report script. `0` disables the ceiling. |

---

## Execution Policy Controls

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Engine:ScriptHashPolicy` | string | `Warn` | — | Behavior when running scripts with modified hashes: `Warn`, `Block`, or `Ignore`. |
| `Engine:CaseSensitiveComparison` | boolean | `false` | `SET CASE_SENSITIVE = ON\|OFF` | Controls case sensitivity inside in-memory engine expressions. |
| `Engine:AllowPlaintextSecrets` | boolean | `false` | `SET ALLOW_PLAINTEXT_SECRETS = ON\|OFF` | Blocks scripts from containing raw plaintext connection strings. |
| `Engine:NoSaveSensitive` | boolean | `false` | `SET NO_SAVE_SENSITIVE = ON\|OFF` | Blocks storing credentials in workspace memory caches. |
| `Engine:NoSaveConnection` | boolean | `false` | `SET NO_SAVE_CONNECTION = ON\|OFF` | Blocks saving connections to file or database stores. |
| `Engine:ConnectionEncryption` | boolean | `false` | `SET CONNECTION_ENCRYPTION = ON\|OFF` | Encrypts local connection configuration keys. |

---

## Observability and History

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Engine:TelemetryEnabled` | boolean | `true` | `SET TELEMETRY = ON\|OFF` | Transmits anonymous execution metrics to help refine optimization. |
| `Engine:LineageEnabled` | boolean | `true` | `SET LINEAGE = ON\|OFF` | Automatically parses sources/targets to construct lineage maps. |
| `Engine:AuditAdHocRuns` | boolean | `false` | `--record` / `--no-record` | When true, ad-hoc CLI runs are recorded in the local job history store and lineage catalog. Use `--record`/`--no-record` per invocation to decide. |
| `Engine:ConnectionPreviewLimit` | integer | `10` | `SET CONNECTION_PREVIEW_LIMIT = n` | Rows previewed when validating connector definitions. Set to `0` to skip schema/data access during declaration and keep connections lazy until first use. |
| `Engine:DefaultHistoryLimit` | integer | `100` | — | Script run histories preserved in database storage. |

---

## Miscellaneous

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Engine:StartOfWeek` | string | `Monday` | `SET WEEK_START_DAY = 'day'` | Start day used by date calculations (e.g., `DATEPART(WEEK, ...)`). |

---

## Report Formatting

Report formatting is resolved on the server. Nothing is inferred from the viewer's browser, so one report renders identically in the browser, a PDF, an email, and the terminal.

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Reporting:DefaultLocale` | string | `""` | `SET REPORT LOCALE = 'culture'` | Culture used to format dates, times, and computed numbers in reports. The empty string is the invariant culture. Validated with `CultureInfo.GetCultureInfo`; an unknown culture fails rather than falling back silently. |
| `Reporting:DefaultNullLabel` | string | `"-"` | `SET REPORT NULL_LABEL = 'text'` | Text rendered in place of a NULL value. An explicitly empty string renders nothing. |
| `Scheduler:DefaultTimeZone` | string | `"UTC"` | `SET REPORT TIME_ZONE = 'zone'` | Time zone every date and time in a report is rendered in. Documented with the scheduler keys in [Orchestrator Configuration](orchestrator-configuration.md). |

Precedence, most specific first:

- **Time zone** — `SET REPORT TIME_ZONE`, then `Scheduler:DefaultTimeZone`, then `UTC`.
- **Locale** — `SET REPORT LOCALE`, then `Reporting:DefaultLocale`, then the invariant culture.
- **NULL label** — a visual's `OPTIONS (NULL_LABEL = '...')`, then `SET REPORT NULL_LABEL`, then `Reporting:DefaultNullLabel`, then `-`.

---

## Related

- [Configuration Settings Reference](../appsettings-reference.md) — full config hub
- [Security Configuration](security-configuration.md) — sandbox limits and egress fence
- [SET REPORT](../../../reference/set-commands/set-report.md) — per-script formatting overrides
- [Orchestrator Configuration](orchestrator-configuration.md) — job scheduling and concurrency
- [Platform Administration](../README.md)
