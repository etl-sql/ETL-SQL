# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; move a roadmap phase here only when work on that
release begins.

---

## Active priority override — Billion-row execution performance

**Decision (2026-06-30):** pause new Enterprise Phase 3–5 feature work after the already-committed
execution-policy and filesystem-authorizer foundations. Resume enterprise enforcement after the engine
has a credible, measured path to large analytical workloads. Security fixes and regressions remain
in scope; this pause applies to new enterprise capability.

The active implementation plan is under **Scale & operator-algorithm assessment** below. The target is
an ETL-SQL-owned columnar execution path—not embedding DuckDB or delegating the product's execution
model to another database.

---

## v0.14.0 — Enterprise Policy Enforcement & Monitoring

Completes the enterprise controls whose protected enrollment and authoritative client runtime shipped
in v0.13.0. Standalone installations must remain unenrolled, unrestricted by organization policy, and
independent of network services.

**Shipped foundation (v0.13.0, do not redo):** machine-level enrollment, protected bootstrap, trust
key, machine identity, enroll/status/unenroll CLI (`4850f3c0`); tenant-bound RSA-PSS signed policy
retrieval, protected cache, rollback/expiry checks, configuration precedence, diagnostics, dynamic
reload, fail-closed host refresh (`9e0dfbc`). All v0.14.0 work consumes `EnterprisePolicyRuntime.Current`
— do **not** introduce a second policy loader or configuration-precedence path.

> **Before starting any item:** verify it against the current code first — some foundations already
> exist (e.g. `SecurityService` path validation, the governance audit outbox, fail-closed audit
> interceptor) and parts of these phases may be partially implemented. Don't treat a roadmap line as
> net-new work until confirmed.
>
> **Scope note:** ROADMAP Phase 6 (Operations Control Plane) is *candidate scope* and stays in
> `ROADMAP.md` — promote the highest-value Phase 6 items here only after Phases 3–5 expose the final
> operational requirements.

### Phase 3: Policy Authority & Operation-Boundary Enforcement — PAUSED

> **Paused 2026-06-30:** preserve completed work and tests, but do not start another Phase 3 slice until
> the active billion-row performance program reaches its certification gates.

#### 3.1 Policy authority
- [ ] Add an administrator-only policy API and Portal workflow to validate, version, publish, supersede, and retrieve organization policies by tenant/environment.
- [ ] Sign envelopes with an external certificate/key-store reference; never persist an exportable private signing key in the Portal database, configuration export, logs, backups, or support bundles.
- [ ] Authenticate enrolled machines, bind responses to tenant/environment, support client certificates, and reject unknown, revoked, or reassigned machine identities.
- [ ] Preserve immutable published versions and record author, reviewer/publisher, timestamp, policy hash, superseded version, and rollout state.
- [ ] Support staged rollout and emergency rollback by publishing a newer signed version; clients must continue rejecting envelopes with older issuance times.
- [ ] Add policy-authority availability, signing-key rotation, machine revocation, and publication audit coverage.

#### 3.2 Shared enforcement context
- [x] Define one immutable execution-policy snapshot containing enrollment, policy version/hash, actor, execution mode, script hash, job/correlation ID, and effective governed values. *(Core `ExecutionPolicySnapshot` is captured by the evaluator at top-level execution and preserved across parallel forks.)*
- [ ] Capture the snapshot when execution begins and pass it through CLI, TUI, Report Player, Portal, Orchestrator, child processes, parallel branches, and scheduled jobs.
- [~] Define policy-refresh semantics for work already running: security revocation and expired policy fail promptly; ordinary limit changes apply no later than the next operation boundary. *(Snapshot freshness contract now distinguishes terminal unavailable/expired policy from ordinary version/hash refresh; operation authorizers still need to invoke it.)*
- [x] Return structured allow/deny decisions with policy key, sanitized requested value/target, effective constraint, and correlation data. *(`OperationPolicyDecision` is the shared operation-boundary result contract.)*

#### 3.3 Filesystem enforcement
- [ ] Route all script-driven reads, writes, deletes, moves, copies, archive extraction, directory enumeration, spill, export, snapshot, and artifact paths through one canonical path-authorizer.
- [ ] Enforce approved roots, read/write distinctions, maximum recursive depth, file-operation count, extension/type restrictions, and protected application/system paths.
- [ ] Resolve canonical targets before access and prevent bypass through `..`, relative paths, mixed separators/case, UNC/device paths, alternate data streams, symbolic links, junctions, hard links, and archive traversal.
- [ ] Re-check immediately before mutation to reduce check/use races; use handle-based validation where the platform supports it.
- [ ] Keep engine-owned spill/cache paths separate from user-selected destinations while applying explicit policy limits to both.

#### 3.4 Network and connector enforcement
- [ ] Enforce connector allowlists and destination host/port/scheme rules before DNS resolution and connection creation.
- [ ] Protect against DNS rebinding, redirects to denied destinations, proxy bypass, IPv4/IPv6 literal variants, loopback/link-local/private ranges, and credentials embedded in URLs.
- [ ] Apply the same authorization to REST, database, email, SFTP, object storage, remote policy/vault access, and connector-specific discovery/probe operations.
- [ ] Ensure aliases, plugins, saved connections, and connection-string forms cannot bypass connector classification or destination checks.

#### 3.5 Process, Docker, resource, and script-setting enforcement
- [ ] Gate external executables, arguments, working directories, environment inheritance, shell invocation, Docker images/registries, mounts, networks, privilege flags, and host access.
- [ ] Enforce parallelism, recursion, file-operation, email, string/result, memory/spill, execution-time, and other governed resource ceilings at runtime.
- [ ] Prevent `SET`, environment variables, command-line options, report parameters, saved sessions, plugins, and child processes from weakening locked or constrained values.
- [ ] Permit users to choose stricter limits; reject weaker values before execution and retain the enterprise value.
- [ ] Make every denial deterministic across in-process and spawned-process execution.

#### Phase 3 completion gates
- [ ] Every governed key maps to a named enforcement boundary or is removed from the policy schema as non-enforceable.
- [ ] A repository-wide security review finds no direct sensitive operation that bypasses the shared authorizer.
- [ ] Bypass suites cover Windows and Linux paths, links, DNS/redirect behavior, connector aliases, child processes, Docker mounts, script overrides, and concurrent policy refresh.
- [ ] Existing standalone tests prove no enterprise endpoint, certificate, cache, or organization restriction is required when unenrolled.

### Phase 4: Central Security Events

#### Event contract and emission
- [ ] Define a versioned structured security-event schema with stable event ID, severity/type, timestamp, actor/effective identity, host/node, tenant, script/job/correlation IDs, policy version/hash, sanitized target, decision, and reason.
- [ ] Emit events for override attempts, denied filesystem/network/connector/process/Docker operations, policy signature/expiry/rollback failures, stale or unavailable policy, machine enrollment changes, and repeated resource-limit violations.
- [ ] Separate security events from ordinary diagnostic logs and existing governance audit records while preserving correlation between all three.
- [ ] Redact credentials, query parameters, connection strings, environment values, filesystem data, and exception details before persistence or transport.

#### Durable delivery and monitoring
- [ ] Provide a durable local security-event outbox for every executable, with bounded storage, atomic append, retry, batching, deduplication, jittered backoff, and crash recovery.
- [ ] Deliver to an HTTPS/SIEM collector using machine identity; define acknowledgement and idempotency behavior.
- [ ] Add Windows Event Log and syslog/structured-file sinks for bootstrap failures that occur before HTTPS delivery is available.
- [ ] Support policy-controlled severity filters so enterprises can forward security warnings/denials without centrally shipping all diagnostic logs.
- [ ] Add optional fail-closed thresholds for terminal delivery failure, oldest-event age, pending count, and outbox bytes; standalone mode remains local-only by default.
- [ ] Expose queue health, last delivery, failures, drops, and collector reachability through diagnostics and fleet status.

#### Phase 4 completion gates
- [ ] Fault-injection tests cover collector outage, duplicate delivery, acknowledgement loss, corrupt outbox state, disk pressure, process crash, redaction, and recovery.
- [ ] A denial is blocked first and then reported; no enforcement decision depends on successful remote logging unless fail-closed monitoring is explicitly enabled.
- [ ] Documentation includes example mappings for common SIEM products without coupling the core event contract to one vendor.

### Phase 5: Certification & Operations

#### Certification lanes
- [ ] Add Windows and Linux enterprise certification lanes for enrollment, signed retrieval, cache/offline operation, dynamic refresh, operation enforcement, and event delivery.
- [ ] Certify Portal, Orchestrator, CLI, TUI, Report Player, Report Builder, Language Server, scheduled jobs, spawned runners, and parallel execution.
- [ ] Run malicious-input and bypass drills covering policy tampering, stale/expired policy, signing-key rotation, machine revocation, path/link races, DNS rebinding, connector aliases, Docker escape-oriented options, and log injection.
- [ ] Prove standalone regression behavior with no enrollment, no enterprise network calls, and unchanged local workflows.

#### Deployment and recovery
- [ ] Document policy-authority deployment, signing-key custody/rotation, machine enrollment/revocation, service-identity permissions, staged rollout, emergency policy publication, and unenrollment governance.
- [ ] Document cache and outbox backup/restore rules; restored machines must not duplicate machine identity or silently reuse credentials in another environment.
- [ ] Define upgrade ordering and compatibility across bootstrap, envelope, policy, event, and collector schema versions.
- [ ] Provide outage runbooks for policy authority, certificate expiry, invalid publication, SIEM outage, disk exhaustion, and fail-closed fleet recovery.
- [ ] Add support-bundle diagnostics that expose versions, hashes, timestamps, and health without policy payload values, trust material, credentials, or sensitive event targets.

### v0.14.0 release gates
- [ ] Complete threat-model and senior security review with all high-severity findings resolved.
- [ ] Pass full functional, performance, migration, recovery, enterprise certification, and standalone regression suites.
- [ ] Confirm documentation never claims OS-level containment against administrators or arbitrary alternate executables; mandate WDAC/AppLocker or equivalent controls where that boundary is required.

---

## Code Review Findings — round 1 (2026-06-28)

*Scope: cross-cutting anti-pattern scans (performance, security, bugs, linting, logging) across all*
*`src` projects, with targeted verification and spot deep-reads of hotspots. This is **not** yet an*
*exhaustive line-by-line pass — the deeper per-file performance review of the large engine, connector,*
*and Portal-EF query paths (where last release's findings concentrated) is listed under "Round 2" and*
*is the next layer. Every item below was verified at the cited file:line.*

### ETL-SQL.Core
- [x] **[Bug · Med-High] `Parser/Parser.cs:850`** — subquery alias was generated with `"Sub_" + new Random().Next(1000,9999)` (clock-seeded per call, 9,000 values → collisions → ambiguous/incorrect resolution). *(Fixed: monotonic per-parser `_generatedAliasCounter`. 69 subquery/derived tests pass.)*
- [x] **[Logging · Low] obsolete `Logger.Instance`** in places that should use the injected `ILogger` (CLAUDE.md marks `Logger.Instance` obsolete).

### ETL-SQL.Engine
- [x] **[Logging · Low] `Console.Write*` in library code** — `ResultFormatter.cs`, `Handlers/BundleStatementHandlers.cs` write to `Console` instead of `ILogger`. (`Services/PasswordPrompt.cs` console prompt is legitimate.)
- _Verified non-issue:_ the `.Result` hits first flagged in `AggregateEngine`/`WindowEngine`/`ExpressionEvaluator`/`PushdownEngine` are the `WhenClause.Result` **AST property**, not `Task.Result` — no sync-over-async there (comments confirm sort keys are pre-evaluated to avoid it).

### ETL-SQL.Connectors
- [x] **[Logging · Low] `Console.Write*` in connectors** — `AzureBlobConnector`, `SharePoint/SharePointConnector`, `ActiveDirectory/ActiveDirectoryConnector`, `FtpConnector`, `SftpConnector`, `S3/S3Connector` log via `Console`; connector libraries should use injected `ILogger`.
- [x] **[Security · Low-Med · verify] Snowflake identifier interpolation** — `Snowflake/SnowflakeDataSource.cs:138,284,354` build `SELECT * FROM {QuoteIdentifier(table)}`. Confirm `QuoteIdentifier` fully escapes and that table names come from trusted connection config, not raw user query text.

### ETL-SQL.Orchestrator
- [x] **[Security · Low · verify] DDL/PRAGMA interpolation** — `Storage/SQLiteJobHistoryStore.cs:349` (`ALTER TABLE ... ADD COLUMN {ddl}`) and `Storage/SqliteOrchestratorDialect.cs:51` (`PRAGMA table_info({table})`). Identifiers/PRAGMA can't be parameterized; verify `ddl`/`table` come only from the internal schema, never external input.

### ETL-SQL.Reporting
- [x] **[Perf · Low-Med] `EChartsSsrRenderer.cs:127`** — `_poolSemaphore.Wait()` blocks a thread in an async-capable path; use `await WaitAsync(ct)`.
- [x] **[Perf · Low-Med] `PdfExporter.cs:150`** — `stream.CopyToAsync(memory).GetAwaiter().GetResult()` blocks inside the export; `await` it.
- [x] **[Perf · Low] sync facade wrappers** — `PdfExporter.cs:41`, `BrowserReportPdfExporter.cs:27` (`ExportAsync(...).GetAwaiter().GetResult()`): acceptable as sync APIs, but prefer async callers.

### ETL-SQL.Analysis
- [x] **[Perf · Low] `Linting/Rules/RunScriptDependencyPreflightRule.cs:74`** — `AnalyzeAsync(...).GetAwaiter().GetResult()` inside a `foreach` (sync-over-async in lint preflight); make the rule path async.

### ETL-SQL.LanguageServer
- [x] **[Logging · Low] obsolete `Logger.Instance`** in `TextDocumentHandler.cs`, `Program.cs`, `DocumentStateStore.cs` → injected `ILogger`. (stdin/stdout wiring in `Program.cs` is correct — no JSON-RPC corruption.)

### App / TUI / ReportPlayer / ReportBuilder.CLI
- _No findings:_ `Console` output is the intended CLI/TUI/console UI here (not a logging smell).

### Cross-cutting — verified clean
- **Weak crypto:** `MD5`/`SHA1` appear only in user-facing `HASH`/`HASHBYTES`/`FILE_HASH`/`VERIFY` functions (data checksums, caller's choice) and are explicitly **rejected** for encryption key derivation (`Common/EncryptionOptions.cs`, `Analysis/.../ConnectionEncryptionRule.cs`). No weak-crypto security issue.
- **`async void`:** none in `src`.
- **Insecure `Random`:** only the Parser alias bug above matters; `DataGenerator`, `USING SAMPLE` seed, and the TUI demo are benign.
- **Empty `catch {}`:** ~85 across `src`, predominantly best-effort cleanup/dispose; spot-review for any that swallow a real error (not individually flagged).

### Round 2 additions (double-check pass — missed in round 1)
- [~] **[Perf · Med] `ReportPortal` EF read paths** — was **32 `AsNoTracking`** vs **89 `ToListAsync`**; ~57 read-only materializations carry change-tracking overhead. *(User-catalog read fixed; broader audit of metrics/audit/other read endpoints remains.)*
- [x] **[Perf · Med] `Controllers/AdminController.cs:294` N+1** — `BulkUpdateUserStatus` queried each user individually in a loop. *(Fixed: single `Where(u => requestedIds.Contains(u.Id)).ToDictionaryAsync`; per-user `SaveChangesAsync` kept for conflict isolation. Still TODO: check groups/members bulk endpoints for the same shape.)*
- [x] **[Security · Low-Med · verify] user-supplied `ValidationRegex` ReDoS** — `App/PipelineGenerator.cs:1166` only *compiles* the column `ValidationRegex` to validate it (safe). Verify that wherever that regex is later *applied to data* it uses a `Regex` **match timeout** (the project already hardened `ParameterUtility`/`ConnectorExceptionWrapper` with [GeneratedRegex] + 1000 ms in v0.13.0 — apply the same here).
- [x] **[Security · Low · verify] `Process.Start` sites** — `App/EngineRunner.cs:1125,1573` spawn external executables (by design for script `exec`/Docker — this is exactly what v0.14.0 Phase 3.5 process-enforcement must gate; cross-reference). `UseShellExecute=true` URL/path launchers in `TUI/ConsoleEditor.cs:657,659`, `TUI/ReportLauncher.cs`, `ReportPlayer/Program.cs`, `ReportBuilder.CLI/Program.cs` open local files/URLs — confirm targets are trusted/local, not attacker-influenced.
- _Verified non-issues:_ no `BinaryFormatter`/`TypeNameHandling`/`JavaScriptSerializer`; `XmlDataSource.cs:148` is **XXE-safe** (on .NET Core+/.NET 10 `XmlReaderSettings` defaults to `DtdProcessing.Prohibit` + `XmlResolver = null`); large `ReadToEnd`/`ReadAllBytes` hits are bounded (decrypt buffers, 32-byte key file, small embedded resources); no `new Regex` over **user input without a timeout** at match time except the `ValidationRegex` item above.

### Round 2 — deep performance pass (engine execution paths + Portal EF)

**Engine — findings:**
- [x] **[Perf · Med] `Engines/SelectExecutionEngine.cs` per-row sort-key extraction** — ORDER BY key resolution repeated row-invariant column lookups per row, and a 5-arg `ExtractSortKeys` overload **recompiled every order/column expression per call** (used in the WITH TIES tail loop → per-row recompilation). *(Fixed: new `SortKeyExtractor` built once per sort via `BuildSortKeyExtractor` — resolves each ORDER BY expr to a column-name/compiled-delegate/fallback plan once; per-row extraction is now O(orderKeys) with no scans or recompilation. All 5 call sites (in-memory sort, Top-N heap, streaming + post-sort WITH TIES) updated. 143 sort/Top/WithTies tests + full 3893 suite pass.)*

**Engine — verified well-optimized (no action):**
- Expressions are **pre-compiled once** via `RowExpressionCompiler` (`StreamingQueryEngine`, `SelectExecutionEngine` build `compiled*` arrays before the row loop) — no per-row recompilation.
- GROUP BY keys use a `CompoundKey` **struct** with 1/2/3-arg specializations (`AggregateEngine`) — no per-row string-key allocation/boxing.
- Results **stream** as `IAsyncEnumerable<DataTable>` batches; external Aggregate/Window/Join/Sort engines spill, and their `.ToList()` calls operate on bounded chunks, not the whole result.
- No string concatenation in hot loops (the `+=` sites are numeric accumulators); `JoinEngine` `IndexOf` is per-batch schema building, not per-row.

**ReportPortal EF — findings:**
- [x] **[Perf · Med] `Controllers/AdminController.cs:134-151`** (user search) — loaded the **entire** users table then filtered/paginated in C# (`Contains(..., StringComparison.OrdinalIgnoreCase)` forced client eval). *(Fixed: server-side `u.UserName.ToLower().Contains(term)` → `LOWER(col) LIKE '%term%'` (case-insensitive on SQLite + Postgres) with SQL `Skip/Take`; added `AsNoTracking()`.)*
- [x] **[Perf · Med] `AdminController.cs:294` N+1** — *(Fixed; see Round-2-additions entry above.)*
- [x] **[Perf · Med] `AsNoTracking` gap** — was 32 `AsNoTracking` vs 89 `ToListAsync`. *(Fixed: Added AsNoTracking to all read-only query paths in ReportsController, SubscriptionsController, and DatasetController to eliminate EF change-tracking overhead.)*

- [x] Remaining Portal controllers/services beyond `AdminController` (Reports, Subscriptions, Datasets) for the same EF patterns; index coverage for the hot audit/metrics queries. *(Fixed: Audited and optimized Reports, Subscriptions, and Datasets controllers. Optimized report matches lookup in SubscriptionsController to run case-insensitive matching on the DB instead of loading all reports in memory.)*
- [x] Connector streaming vs full-buffer reads on large payloads (per-connector). *(Verified: All key relational (Postgres, SqlServer, MySql, Oracle, Odbc, Sqlite, Snowflake) and file/API (REST, Excel, FlatFile, AzureBlob, SFTP, FTP, S3) connectors implement ReadBatchesCore via IAsyncEnumerable and stream rows incrementally using ReadAsync / stream-parsing rather than buffering the entire payload.)*

## Scale & operator-algorithm assessment (large-tier behavior)

*Verdict: SORT and JOIN have genuinely strong algorithms; **plain GROUP BY is already single-pass
incremental (O(groups))** — correcting an earlier draft that wrongly called it a gap. The real
remaining weak spots at the ≈50M tier are: DISTINCT single-level partitioning; sort merge fan-in;
statistical/holistic aggregates (VARIANCE/STDDEV/COVAR/CORR/PERCENTILE/STRING_AGG) that still buffer
rows via `GenericState`; and the external aggregate spilling even when in-memory would fit.
FILTER/EXCLUDE are fine. The scale cert proves spill works functionally (it force-shrinks memory
grants at small row counts) but does not exercise a true 50M tier — these are reasoned from the code.*

**Sound at scale (no change needed):**
- **WHERE / FILTER** — streamed, compiled predicate per row, O(1)/row, constant memory. Scales arbitrarily.
- **SELECT * EXCLUDE / projection** — column-level, O(columns)/row, bounded. "Large EXCLUDE" isn't a data-scale concern (columns are bounded).
- **ORDER BY** — true external **k-way merge sort** (sorted runs → min-heap merge, native typed keys, streamed output). Correct algorithm.
- **JOIN** — **grace hash join with recursive repartitioning** (partitions both sides; re-partitions oversized partitions with a depth-salted hash up to depth 8 for skew). Best-in-class.

**Algorithmic gaps to address (where "large hurts"):**
- [x] **[Perf — RE-ASSESSED; original claim was wrong for the common path]** — `AggregateEngine.ApplyAggregation` is **already single-pass incremental**: `Dictionary<CompoundKey, IAggregateState[]>` with incremental states for SUM/TOTAL/COUNT/AVG/MIN/MAX/EVERY/ANY/SOME/APPROX_COUNT_DISTINCT (O(groups); DISTINCT keeps an O(distinct) set, inherent). The external engine delegates to it per partition, so **plain GROUP BY does not buffer rows** — no OOM there. The cert's 289 MB is partition/spill overhead, not per-group row buffering. *Real, narrower gaps remain:*
  - [x] **[Perf · Med] Algebraic stats buffer rows** — `VARIANCE/VAR_*/STDDEV/STDEV/COVAR_*/CORR` fell to `GenericState` (`List<Row>` per group, O(rows)). *(Fixed 2026-06-29, commit 6fb53e39: incremental `VarianceState` (Welford) + `CovarianceState` (online co-moments; correlation from population co-moments). O(1)/group, null-pairing preserved. `EvaluateAggregate`/`Calculate*` kept for WindowEngine frame aggregates. Correctness + per-group + n=1-null tests added; 134 aggregate/window/grouping tests pass.)*
  - [ ] **[Perf · Low-Med] Holistic aggregates buffer whole rows** — `PERCENTILE_CONT/DISC`, `MEDIAN`, `STRING_AGG`, `GROUP_CONCAT`, `ARRAY_AGG` must buffer, but `GenericState` buffers full `Row` objects; buffer only the argument value(s) to cut memory. *(Deferred: these are genuinely holistic and several need within-group ORDER BY / extra-column context at finalize, so reducing to a single arg value is not a drop-in; lower priority than the algebraic ones above.)*
  - [ ] **[Perf · Med] External aggregate always spills when triggered** — it partitions all input to disk even when incremental in-memory aggregation would fit (low/medium cardinality). Try in-memory incremental first; partition/spill only when the group-state set exceeds a memory bound.
- [~] **[Perf · Med @50M] DISTINCT and GROUP BY are single-level partitioned (no recursion)** — `ExternalDistinctEngine` **[FIXED 2026-06-29, commit 1ff7737f]**: now recursively repartitions oversized partitions with a depth-salted route key (mirrors the join), capped at depth 8, dedup equality on the unsalted full-row key, in-memory fallback when a partition can't split. `ExternalAggregateEngine` (plain GROUP BY) **still single-level — deliberately deferred**: a row-count repartition trigger (the join/distinct pattern) would thrash the *common* low-cardinality / high-row case (few groups, many rows) since plain GROUP BY is already O(groups) incremental and row count is a poor proxy for group-state size there. The correct fix is a memory-bounded group dictionary that spills when the live group-state set (not row count) exceeds a bound — a larger change. Only pathological near-unique GROUP BY keys at 50M are at risk; revisit if the 50M run dies specifically on plain GROUP BY.
- [x] **[Perf · Med @50M] External sort merge fan-in is unbounded** — *(Fixed 2026-06-29, commit fd4406cb: `MaxMergeFanIn`=64 with bounded multi-pass merge — groups of 64 chunks merge into intermediate runs (consumed inputs deleted immediately) until the remainder fits one final pass; open readers capped at 64 regardless of row count. Multi-pass regression test added; 142 sort/spill/orderby/withties tests pass.)*
- [x] **[Perf · Low] non-streaming sort entry** — `SelectExecutionEngine:528-529` calls `SortExternal(List<Row>)`, which materializes the whole input before spilling (vs the streaming `SortStreamAsync` at :521). *(Verified non-issue: SelectExecutionEngine:521 takes the streaming SortStreamAsync branch when externalEngineStream is active; SortExternal is only called as a fallback when the data is already materialized in memory via allRows.)*
- [x] **[Correctness · verify @scale] RIGHT/FULL OUTER via external hash join** — `ExternalJoinEngine.JoinPartitionDirect` only emits unmatched **LEFT** rows. *(Fixed: Added matched right rows tracking and emission of unmatched right rows per partition inside ExternalJoinEngine.ProbeJoin to support RIGHT and FULL outer joins correctly at scale.)*
- [ ] **[Perf · note] WINDOW** buffers per partition (inherent to frame computation) — cert: 500k rows = 867 MB. Largest single partition bounds memory; document the per-partition limit / consider partition-streaming for frames that allow it.

### Active plan — ETL-SQL-owned columnar execution path (revised 2026-06-30)

**Engineering conclusion:** compact columnar `#temp` storage is necessary but insufficient. If every
read immediately reconstructs boxed `Row`/`DataTable` values, memory improves while CPU and allocation
cost remain fundamentally row-at-a-time. The performance architecture must expose native column
buffers directly to common operators, with the existing row path retained as a compatibility fallback.

**Scope discipline:** do not attempt to reproduce all of DuckDB at once. Build columnar fast paths for
the high-volume relational core while preserving ETL-SQL orchestration, connectors, governance,
lineage, scripting, and heterogeneous-source behavior.

#### P0 — Make certification truthful and diagnostic

- [x] Replace the misleading `peakManagedMemoryMB` metric. It previously sampled
  `GC.GetTotalMemory(forceFullCollection: true)` only after a scenario, so it is neither peak managed
  memory nor process working set. *(Continuous 100 ms scenario sampler now gates peak process working
  set and reports private bytes and managed heap.)*
- [~] Sample and report peak process working set, private bytes, managed heap, allocation bytes, GC
  counts/pause time, CPU time/utilization, spill read/write bytes, spill extent count, rows/second, and
  partition-pass count while each scenario runs. *(Process/GC/CPU/throughput and spill-write metrics
  shipped; spill-read, extent, and partition-pass counters require P1 engine telemetry.)*
- [x] Enforce a fixed machine-independent memory ceiling. Remove row-proportional Huge defaults (the
  current formula permits 80,000 MB at 10M rows). For the final lane, process peak must remain below
  16 GB; configure the engine grant around 8–10 GB to leave runtime/GC headroom. *(Defaults are now
  fixed at Smoke=1 GB, Standard=4 GB, Stress=8 GB, Huge=16 GB; explicit override remains supported.)*
- [x] Split correctness, memory, and throughput gates. A scenario must not report “certified” merely
  because it returned the right checksum after spilling. *(Separate fields are emitted; optional
  `CERT_MIN_ROWS_PER_SECOND` activates the throughput gate.)*
- [~] Capture a checked-in baseline for 10M and 50M before changing storage. Use Release + server GC,
  record machine CPU/RAM/disk, and report per-scenario metrics rather than one multi-scenario process
  whose retained state contaminates later measurements. *(Isolated 10M core baseline captured in
  `certification-results/baseline-10m.json`; resumable 50M capture remains.)*

#### P1 — Fix spill I/O amplification before adding new operators

Current `InMemoryDataSource.WriteBatches()` clones and validates every row, serially awaits each spill,
and emits one chunk per processed batch. At 1B rows with 10K batches this can approach 100,000 spill
chunks, making filesystem metadata and reader/writer setup dominate execution.

- [ ] Introduce large sequential spill extents (initial target 64–256 MB), appending multiple logical
  batches per extent. Bound open files and record extent count in certification.
- [ ] Add a bounded double-buffered pipeline so producing/encoding the next batch can overlap writing
  the current extent without violating the memory grant or cancellation semantics.
- [ ] Avoid `Row.Clone()` plus per-value Arrow rebuilding when input is already a native column batch.
- [ ] Measure compression as an explicit disk-vs-CPU tradeoff; do not enable it by assumption.
- [ ] Wire pressure-based spill into every host composition root, not only Orchestrator. Keep the row
  threshold as a backstop, but make bytes the authoritative trigger.

#### P2 — Add a native column-batch contract and append-only `#temp` store

- [ ] Add an internal `ColumnBatch` model with typed buffers, null bitmaps, row count, and schema.
  Storage uses arrays/`Memory<T>`/pooled buffers; `Span<T>` is only a synchronous loop view.
- [ ] Add a dual-path source contract such as `IColumnarDataSource.ReadColumnBatches()`. Preserve
  `IDataSource.ReadBatches()` as the compatibility adapter for connectors and features that still need
  rows. Do not force column batches through `Row` between column-capable operators.
- [ ] Implement append-only segmented `#temp` storage first: bounded mutable head, immutable native
  segments, large spill extents, deterministic disposal, and a row adapter only at fallback boundaries.
- [ ] Store integral types as native integral widths, floating-point as `double`, `decimal` only for SQL
  decimal, dates/times as native fixed-width values, booleans as bit/byte buffers, and strings using
  offset/data or dictionary encoding selected from measured cardinality.
- [ ] Use pooled/slab allocation and explicit ownership so large buffers do not create uncontrolled LOH
  churn. Account allocated capacity—not only logical payload—against the memory grant.
- [ ] Replace PK/unique full-row caches with compact key/hash structures before declaring constrained
  columnar temp tables memory-bounded.

#### P3 — Columnar fast paths (“columnar islands”)

Operators use native batches when supported and fall back to the existing row engine for complex or
unsupported expressions. Scalar typed-buffer loops come first; SIMD is an optimization after profiling.

- [ ] Scan and projection without materializing `Row` objects.
- [ ] Comparison, null, boolean, and simple arithmetic predicates using selection vectors.
- [ ] `COUNT`, `SUM`, `MIN`, `MAX`, and `AVG` over native buffers.
- [ ] Low-cardinality `GROUP BY` with compact typed keys and memory-bounded aggregate state.
- [ ] Hash partition routing directly from column buffers.
- [ ] Equi-join build/probe over typed key vectors and packed payload columns.
- [ ] Sort-key extraction and run generation without boxing; retain the bounded external merge.
- [ ] Add adapters and differential tests proving columnar and row paths return identical results,
  null behavior, type coercion, collation behavior, lineage, and cancellation semantics.

#### P4 — Partition sizing and spill-read fast paths

- [ ] Replace fixed `ExternalHashPartitions=32` with fan-out derived from sampled/known input bytes,
  key width, cardinality/skew evidence, and per-partition memory budget. Target one partitioning pass
  for normal distributions.
- [ ] Read spill extents as column batches. Do not reconstruct boxed rows merely to hash, filter,
  aggregate, or repartition them.
- [ ] Detect skew and unsplittable hot keys explicitly; use bounded specialized handling or fail with a
  diagnostic under `SpillOrFail` rather than silently consuming unbounded RAM.

#### P5 — Mutation and broader semantics after the fast path is proven

- [ ] Add delete tombstones, update delta segments, and compaction only after append-only storage and
  columnar operator gates pass. Mutation complexity must not delay proof of the central architecture.
- [ ] Make `MERGE` bounded; it currently materializes source and target lists and is outside the initial
  billion-row claim.
- [ ] Reduce holistic aggregates to argument/order-key storage rather than full rows.
- [ ] Add streaming/specialized window paths where frame semantics permit; a single huge window
  partition remains outside the initial claim.
- [ ] Extend columnar kernels based on measured hotspots. Use `System.Numerics.Vector<T>` or
  `System.Runtime.Intrinsics` only where benchmarks show a material gain.

#### Required incremental gates

- [x] **Gate A — harness:** 10M baseline reports trustworthy peak process memory and throughput data.
  *(Each core scenario runs in a fresh Release/server-GC test host via `Test-ScaleBaseline.ps1`.)*
- [ ] **Gate B — spill:** 50M append-only temp round-trip uses bounded large extents, stays below the
  configured ceiling, and materially improves rows/second versus the checked-in baseline.
- [ ] **Gate C — storage:** native columnar `#temp` uses materially less memory than `List<Row>` and
  does not decode to rows during a columnar scan/count/checksum.
- [ ] **Gate D — operators:** scan/filter/project and low-cardinality aggregate fast paths pass
  differential correctness and show a substantial throughput improvement at 10M/50M.
- [ ] **Gate E — external operators:** equi-join and sort stay bounded with dynamic fan-out, bounded
  extent/file counts, and no unnecessary columnar→row→columnar round-trip.
- [ ] **Gate F — initial 1B claim:** append-only scan, filter, projection, low-cardinality aggregate,
  and `#temp` round-trip complete below 16 GB process peak with documented CPU, disk, elapsed time,
  spill volume, and hardware. This does **not** initially certify arbitrary `MERGE`, holistic
  aggregates, single-partition windows, billion-distinct-key aggregation, or adversarial skew.

#### Non-goals and guardrails

- Do not add DuckDB or another embedded database as the hidden execution engine.
- Do not promise blanket “DuckDB performance”; publish operator-specific throughput and limits.
- Do not remove the row engine. It remains the semantic fallback during incremental migration.
- Do not optimize solely for the 1B headline at the expense of normal small/medium workloads; every
  fast path needs crossover thresholds and regression benchmarks.
- Do not claim the current scale report proves peak memory containment until Gate A lands.

### Historical analysis — boxed rows, byte accounting, and original storage proposal (2026-06-29)

> This section records completed work and the diagnosis that led to the revised plan. Its original
> “columnar storage behind an unchanged Row interface” sequence is superseded by P0–P5 above wherever
> they conflict.

*Context: the 50M Huge cert still pins the whole box even though the **algorithms** are already the
textbook DB playbook (grace hash join + recursive repartition; partitioned single-pass hash
aggregate; k-way merge sort; memory-guarded builds). The algorithm is not the problem — the
**per-row in-memory representation**, the **imprecise governor**, and **fixed partition fan-out** are.
Real engines (Postgres `work_mem`, SQL Server memory grants, DuckDB/Spark vectorized columnar) bound
the working set by **bytes**, not by row-count luck, and keep tuples packed/columnar rather than as
boxed object graphs. Plan: B then A (done; proves the approach on the cheapest, safest change first),
then **F** (columnar `#temp` storage — the 1B-row/<16 GB memory goal) and **C** (dynamic fan-out).
**D** is a smaller spill-read optimization; **E** (vectorized *execution*) is CPU-only and deferred —
it builds on F, it does not replace it.*

**Root cause — the build-side tuple is ~6–7× fatter than the data it holds.** A `Row`
(`DataModel.cs:139`) is a `TableSchema` + `object?[]` of **boxed** values, and per the runtime rule
*every* number is stored as `decimal` (INT/BIGINT included). A 5-number logical row (~40 B of data)
becomes ~250–300 B once in the join/aggregate `Dictionary<CompoundKey, List<Row>>`: ~64 B array +
~5×32 B boxed decimals + `Row`/`List`/bucket overhead. Hold 50M build rows → ~13 GB for data that is
~2 GB on disk. That is the "ate my 31 GB box."

- [x] **(B) [Perf · LOW effort · DONE 2026-06-29] Precise byte accounting, not GC sampling.** Replaced
  `HeapGrowthGuard` (GC sampling) with `MemoryBudgetGuard` (per-operator byte counter) in
  `MemoryGovernor.cs`; added allocation-free `Row.EstimateHeapBytes()` + `Row.EstimateValueBytes()`
  (Core) and `RowMemory.EstimateKeyBytes/EstimateValuesBytes` (Engine). Wired into
  `ExternalJoinEngine.BuildJoinHashTable` (per-row + per-new-key), `AggregateEngine.ApplyAggregation`
  (per-new-group key + fixed per-state cost — O(groups), the real agg memory), `ExternalSortEngine`
  (per chunk row + sort key), `ExternalDistinctEngine.DedupToList` (per retained row + seen-set key).
  Detection is now deterministic and per-operator, *before* allocation. The two governor tests that
  used a 1-byte ceiling sentinel were updated to feasible ceilings (agg 64 KB, join 256 KB) — under
  byte accounting a 1-byte ceiling is infeasible (can't fit one group/row), so "completes via
  repartition" required a ceiling that fits once split; correctness assertions unchanged. 18 scale-cert
  + 327 agg/join/sort/distinct + 43 governor/external tests pass. *Holistic aggregates (GenericState
  row buffering) still grow beyond the per-group estimate — unchanged, tracked separately above.*
- [x] **(A) [Perf · HIGH leverage · JOIN DONE 2026-06-29] Shrink the build-side tuple (packed byte[]).**
  New `RowPacker` (`Engine/Engines/RowPacker.cs`): type-tagged, lossless per-row binary codec
  (null/bool/decimal/double/DateTime+Kind/string + JSON fallback; integrals stored as decimal; one
  reused `MemoryStream` per build). `ExternalJoinEngine` build side is now a `PackedBuildTable`
  (column order captured once + flat `List<byte[]>` + `Dictionary<CompoundKey,List<int>>` index); a blob
  is decoded to a `Row` only on a probe-key match. LEFT/RIGHT/FULL outer preserved via a per-index
  `bool[]` matched bitset (more correct than the old reference-identity `HashSet<Row>`). Byte accounting
  now uses the exact blob length. 95 join + 5 RowPacker codec + 18 scale-cert tests pass.
  *Approach chosen: packed byte[] (low risk), not runtime de-boxing.*
  - **Aggregate build needs no packing:** `Dictionary<CompoundKey, IAggregateState[]>` holds incremental
    states (O(groups)), not rows — the common path never retains rows. (Holistic `GenericState` row
    buffering is the separate item above.)
  - [x] **DISTINCT packed too (2026-06-29)** — `ExternalDistinctEngine.DedupToList` now returns
    `PackedRows` (captured columns + `List<byte[]>`) instead of `List<Row>`; blobs decode to a `Row`
    only on yield. The `seen` `HashSet<CompoundKey>` stays (needed for the duplicate check); only the
    retained output rows are packed. Byte accounting uses exact blob length. 24 distinct tests pass.
  - [ ] **(A-better, deferred/risky) De-box numerics at the source** — carry native `long`/`double` in
    `Row` where the column type allows, reserving `decimal` for DECIMAL columns. Largest win (CPU too)
    but touches the deep "every number is decimal at runtime" invariant and many tests; own effort.
- [ ] **(C) [Historical; carried into active P4] Size partition fan-out from an input estimate.** `PartitionCount`
  is static (`Engine:ExternalHashPartitions`, default 32). On a billion-row input each partition is
  still huge → forced recursive repartition, and every recursion level is a full re-read + re-write to
  disk. Choose `PartitionCount ≈ estimatedBytes / perPartitionBudget` up front (or after the first
  pass) so each partition fits the grant in **one** pass — turns 3-pass operations into 1-pass.
- [ ] **(D) [Historical; carried into active P4] Avoid the columnar→boxed `Row` round-trip on spill re-read.** *Correction to
  an earlier verbal claim: disk spill is **already Arrow columnar by default** (`SpillFormat=Arrow`,
  `appsettings.json:40`); JSON is only a legacy fallback for reading old persistent spill files — so
  this is not "switch to columnar."* The real gap is the **read side**: `ArrowSpillReader.ExtractValue`
  (`SpillStore.cs:768`) re-boxes every value into `object?` and converts native `Int64` back to
  `decimal` (`:774`), throwing away the columnar advantage in memory. For the partition→re-read→rebuild
  loop, aggregate/probe directly against the Arrow `RecordBatch` where possible instead of
  `ExtractRow` → boxed `Row` → re-hash.
- [~] **(E) [Historical; replaced by incremental active P3 fast paths] Vectorized columnar execution.**
  Batch-at-a-time column-vector *operators* (evaluate predicates/arithmetic/aggregates over 1–4K-row
  vectors) — the DuckDB **CPU** win. **This is the CPU track, distinct from (F) below, which is the
  *storage* track.** Requires rewriting `ExpressionEvaluator` (today strictly row-at-a-time) to operate
  on column vectors + custom `System.Runtime.Intrinsics` kernels (the C# `Apache.Arrow` lib ships **no**
  compute kernels — see `Docs/Strategy/Arrow_Columnar_Strategy.md §2.2`), cascading into every handler.
  **Does NOT replace (F); it builds on it** — (F) makes `#temp` storage columnar so a later (E) has
  column vectors to vectorize over. The user's goal is **memory containment**, which (F) delivers
  *without* (E). Park (E) in `ROADMAP.md`; only revisit for throughput, not RAM.

- [~] **(F) [Historical; replaced by active P1–P3] Columnar segment storage for `#temp` tables.**
  The storage diagnosis remains useful, but storage behind an unchanged row-at-a-time execution
  interface is not sufficient for the memory-and-throughput target.

  **Corrected diagnosis (verified):** `#temp` tables are `InMemoryDataSource` (`DataSources.cs:139`),
  which **already spills** to Arrow columnar chunks (`TempTableSpillThresholdRows` at `:753` + reactive
  `SpillAsync` at `:203`, via `ISpillable`/`IBufferManager`); a 10M `TempTableSpill` cert passes
  RAM-bounded. So the gap is NOT "no spill." It is: (1) the **resident** rows are still fat boxed
  `Row`/`List<Row>` (the ~6–7× blow-up) — spill bounds *how many* are resident, not how fat each is;
  (2) pressure-spill (`IBufferManager`) is registered in Orchestrator DI but **not** App/engine DI, so
  off-Orchestrator it relies only on the row-count threshold, not real bytes; (3) PK/Unique constraint
  caches hold a **second full copy** of every row (`DataModel.cs:577`).

  **Two design corrections (do not repeat the dead ends):**
  - `Span<T>`/`ReadOnlySpan<T>` are `ref struct`s — stack-only, can't be fields, can't cross `await`.
    They are **not** a storage type in this async engine. Storage = `Memory<T>` / typed arrays / column
    buffers; `Span<T>` only inside synchronous inner loops.
  - Immutability is the *model*, not the obstacle (Arrow/DuckDB transform input segments into new ones,
    never mutate in place). Mutation is handled by tombstones + delta + compaction (below), supporting
    **both** rare/small and frequent/large `UPDATE`/`DELETE`.

  **Approach — dense native-typed column segments behind the existing `ReadBatches()`/`Row` interface
  (no handler rewrite):**
  - **Segment model:** a `#temp` = ordered list of immutable **column segments**, ~N rows each (N tied
    to `BatchSize`; default 2048–8192), one **native-typed buffer per column** + null bitmap (`long[]`
    integral, `double[]` float, `decimal[]` **only** for DECIMAL, `bool[]`, `DateTime`→`long[]` ticks+kind,
    dictionary-encoded `string`). Native widths + no per-value object headers = the memory win (kills
    boxing). Column-local de-boxing — no engine-wide invariant change (cf. the deferred A-better item).
  - **Reads (`Row` adapter):** iterate frozen segments → skip tombstoned rows → decode column buffers to
    a `Row` per live row → yield `BatchSize` `DataTable`s; then drain the delta. Handlers unchanged.
    Reuse `RowPacker` for any per-row encode.
  - **DELETE:** evaluate predicate, flip tombstone bits. O(matched), no rewrite — cheap small *and* large.
  - **UPDATE:** = tombstone old row + append new version to the **mutable delta/head** buffer (freezes to
    an immutable segment when it fills; spills if big). Uniform cost for small and large edits.
  - **Compaction:** when tombstone density (>~30%) or delta size crosses a threshold, rewrite affected
    segments (drop tombstoned, merge delta) to reclaim RAM/disk. Bulk cost paid lazily — bounds
    large-edit workloads.
  - **Spill = the segment** (already columnar → near zero-conversion vs today's per-spill `Row`→Arrow).
    Reuse `ArrowSpillWriter`/`ISpillable`. **Wire `IBufferManager`/`MemoryGrantArbiter.Shared` into the
    App composition root** so byte-pressure spill fires outside Orchestrator (keep row threshold as
    backstop).
  - **Constraints:** replace PK/Unique `HashSet<Row>` (full second copy) with a **key-hash** structure so
    constrained `#temp`s don't defeat the density win.

  **Critical files:** `src/ETL-SQL.Core/Data/DataSources.cs` (`InMemoryDataSource` — the heart);
  **new** `src/ETL-SQL.Core/Data/ColumnSegment.cs` (typed buffers + null/tombstone bitmaps + delta +
  freeze/decode-to-`Row`); `src/ETL-SQL.Core/Data/DataModel.cs` (`Row` decode adapter; constraint-cache
  key change); `Handlers/UpdateStatementHandler.cs` + `DeleteStatementHandler.cs` (route mutations
  through tombstone/delta ops); `Spill/SpillStore.cs` (segment-native spill); App
  `DependencyInjectionSetup` (register `IBufferManager`). **Reuse:** `ISpillable`/`SpillAsync`/`ReadBatches`,
  `ArrowSpillWriter`, `MemoryGrantArbiter.Shared`, `RowPacker`, `MemoryBudgetGuard`.

  **Order (memory-first, incremental):** 1) columnar resident store behind `ReadBatches()`/`Row` +
  segment-native spill (largest, do first); 2) mutation: tombstones (DELETE) + delta + UPDATE=tombstone
  +append; 3) compaction trigger + reclaim; 4) wire byte-pressure spill into engine DI + key-hash
  constraint cache.

  **Verification:** existing `TempTableSpill_*`/`SpillCleanup_*` certs still pass; at equal row counts
  peak managed MB materially below the `List<Row>` baseline (assert via cert harness `peakManagedMemoryMB`);
  `SELECT * INTO #t` (10M+) → mutate → read-back checksum unchanged; DELETE skips tombstones, UPDATE
  returns new values, compaction preserves results + reclaims; large `UPDATE`/`DELETE` over 10M stays
  RAM-bounded (new small- and large-mutation cert scenarios); PK/Unique still enforced via key-hash.

### Target: certify 1,000,000,000 rows below 16 GB process peak

**Status: target, not an existing capability or proven outcome.** The current implementation and
certification harness do not justify a blanket billion-row claim. The initial claim is deliberately
narrow and is earned only by Gate F above.

The first certified workload covers append-only scan, filter, projection, low-cardinality aggregation,
and `#temp` round-trip. Join and sort have separate Gate E requirements and may join the published 1B
matrix only after their measured throughput, spill passes, and skew behavior are acceptable. Arbitrary
`MERGE`, holistic aggregates, single-partition windows, billion-distinct-key grouping, and adversarial
skew remain explicitly outside the initial claim.

Success requires both bounded memory and useful throughput. “Finished eventually after spilling” is a
correctness result, not a performance certification. Every published result must include hardware,
Release/GC configuration, peak process memory, elapsed time, rows/second, CPU utilization, spill bytes,
extent count, partition passes, and required free disk.

**Other scale dimensions (the user's matrix):**
- [ ] **Script size** (10k lines × 10) — verify lexer/parser is O(n) and AST memory is bounded for very large scripts; `RUN SCRIPT` parse-caching already helps repeated targets.
- [ ] **Object count per script** (10/50/100 CONNECTION/VISUAL/@param/#temp) — agree with capping via policy limits (ties into v0.14.0 Phase 3.5 resource ceilings); 100 live connections in one script is an anti-pattern → guidance + a configurable limit rather than an algorithm change.
- [ ] **Server/orchestrator scale** (20/100/1000 reports/jobs) — the EF findings above (N+1, `AsNoTracking`, client-eval) are the relevant levers; verify scheduler/job-history queries paginate and index well at 1000 jobs.

### Measured: Stress tier (5M, 100x) — 2026-06-29

All 13 scenarios **passed** at 5M (correctness holds at scale; validates the ORDER BY sort-key refactor). Observations:
- **Streaming ops scale excellently** — `StreamingSelect`, `CsvIngest`, `ParquetRoundTrip` ran in 1–4 s with **0 spill**. Confirms FILTER/projection are not data-scale-bound.
- **Heaviest = `CUBE` grouping sets** (160 s, 2.24 GB spill) — consistent with the one real row-buffering path (external grouping-set `List<Row>` per group). **[FIXED 2026-06-29, commit 5e27c5ee]** — the grouping-set path now streams each partition once per set into the already-incremental `ApplyAggregation` (via `ReadPartitionForSet`/`__SET_IDX`), skipping empty `(partition,set)` pairs; no more per-group `List<Row>` buffering. 111 grouping/cube/rollup/aggregate tests pass.
- **Join slowest single op** (130 s / 5M, grace hash + repartition) — works; the perf cost of spilling joins.
- **Sort** handled 5M (66 s, 876 MB) with the cert's forced 5k chunk size = ~1000 spill readers open at merge; validates the **merge fan-in** concern is latent for 50M (~5–10k readers).
- [ ] **Investigate: peak process memory climbed monotonically to ~10 GB** across the 13 sequential 5M scenarios (876 MB → ~10 GB, with partial GC dips). Could be a Debug/no-server-GC artifact or real cross-scenario retention (static `LastResult`/session/registry state). Re-measure with server GC / `GC.Collect` between scenarios to distinguish.

### 50M (Huge tier) — could not complete in this environment
Wired and attempted twice; both runs were killed at the identical point with **no OOM/exception** — the agent's background runner kills tasks during the multi-minute **silent** 50M row-generation stretches (no stdout). **Not an engine fault.** Partial observation: the engine **generated 50M rows and switched to `ExternalAggregateEngine` without OOM**. To get full 50M metrics + `baseline-huge.json`, run on a capable host in the **foreground**: `pwsh ./scripts/Test-ScaleCertification.ps1 -Tier Huge`.

---

## VS Code Extension Code Review Findings (v0.14.0)

### Performance & Usability
- [x] **[Perf/Usability · Med] Duplicated executable path resolution & missing shadow-copy in Notebooks**: `notebookController.ts` implements its own `_getExecutablePath` which misses the shadow-copying mechanism (`shadowCopyExecutable`). Executing `.etlnb` cells runs the engine directly from `extensionPath/bin/ETL-SQL.exe`, locking the binary folder and causing VS Code updates or extension uninstalls to fail. It should reuse `getExecutablePath` from `extension.ts`.
- [x] **[Perf · Low] Synchronous I/O in UI Panel creation**: `WelcomeView.ts` and `resultsPanel.ts` load HTML content using synchronous `fs.readFileSync` during activation/initialization. This blocks the VS Code extension host main thread and should use async file reading or inline/pre-cached templates.
- [x] **[Usability · Low] Missing robust shell escaping in Terminal command builder**: `terminalCommandBuilder.ts` wraps arguments with spaces in double-quotes but does not handle or escape nested double-quotes, backslashes, or shell-specific control characters. This could cause execution truncation or syntax errors if paths contain shell-special characters.

### Security & Privacy
- [x] **[Security · Med] Insecure session token storage in globalState**: `portalPublishCommand.ts` saves the Portal authentication access token (`portalToken_${portalUrl}`) directly in `context.globalState` which is serialized as plain text on disk. Secure credentials and session tokens must be stored in `context.secrets` (VS Code's secure keychain wrapper).
- [x] **[Security/Logging · Low-Med] Leak of API Key in logs on fetch network error**: `generateScriptFromSpec.ts` builds the Gemini URL with the API key in the query string (`?key=${apiKey}`). On connection failures, the Node.js `fetch` client throws a `TypeError` containing the full URL with the query string. This is caught and logged directly to the output channel/log files via `logger.log`, leaking the raw API key in plain text.
- [x] **[Security/Logging · Low-Med] Secrets / ENC keys printed to VS Code output channel**: In `logger.ts`, `logger.log()` prints unredacted logs directly to the OutputChannel (`channel.appendLine(formatted)`), only redacting them for the memory buffer. Furthermore, in `ReplManager.ts`, stdout and stderr from the engine process are written directly to the output channel without running through `redactSecrets`, potentially leaking passwords, connection strings, or decrypted keys during run failures.
- [x] **[Security/Usability · Med-High] System-wide taskkill on deactivation**: In `extension.ts:deactivate()`, `forceKillProcesses()` executes `taskkill /F /IM ETL-SQL.exe /IM ETL-SQL-LSP.exe ...` on Windows and `pkill -9` on Unix. In multi-user systems (terminal servers, shared VMs, Citrix), this terminates the ETL-SQL/LSP processes of all other active developers on the machine, causing data loss. It should filter Windows taskkill by username (`/FI "USERNAME eq %USERNAME%"`) or use tracked PIDs.

### Bugs
- [x] **[Bug · Med] ConnectionsProvider.getConnections() returns empty array**: In `connectionsProvider.ts`, `getConnections()` is hardcoded to return `[]`. Because of this, when `syncConnectionsToLsp` runs, it sends an empty list to the LSP client and sidebar explorer, effectively clearing the connection cache upon LSP restarts. It should aggregate all active connections from `scriptConnectionsByUri`.


