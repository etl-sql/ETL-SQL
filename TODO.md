# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; move a roadmap phase here only when work on that
release begins.

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

### Phase 3: Policy Authority & Operation-Boundary Enforcement

#### 3.1 Policy authority
- [ ] Add an administrator-only policy API and Portal workflow to validate, version, publish, supersede, and retrieve organization policies by tenant/environment.
- [ ] Sign envelopes with an external certificate/key-store reference; never persist an exportable private signing key in the Portal database, configuration export, logs, backups, or support bundles.
- [ ] Authenticate enrolled machines, bind responses to tenant/environment, support client certificates, and reject unknown, revoked, or reassigned machine identities.
- [ ] Preserve immutable published versions and record author, reviewer/publisher, timestamp, policy hash, superseded version, and rollout state.
- [ ] Support staged rollout and emergency rollback by publishing a newer signed version; clients must continue rejecting envelopes with older issuance times.
- [ ] Add policy-authority availability, signing-key rotation, machine revocation, and publication audit coverage.

#### 3.2 Shared enforcement context
- [ ] Define one immutable execution-policy snapshot containing enrollment, policy version/hash, actor, execution mode, script hash, job/correlation ID, and effective governed values.
- [ ] Capture the snapshot when execution begins and pass it through CLI, TUI, Report Player, Portal, Orchestrator, child processes, parallel branches, and scheduled jobs.
- [ ] Define policy-refresh semantics for work already running: security revocation and expired policy fail promptly; ordinary limit changes apply no later than the next operation boundary.
- [ ] Return structured allow/deny decisions with policy key, sanitized requested value/target, effective constraint, and correlation data.

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
- [ ] **[Logging · Low] obsolete `Logger.Instance`** in places that should use the injected `ILogger` (CLAUDE.md marks `Logger.Instance` obsolete).

### ETL-SQL.Engine
- [ ] **[Logging · Low] `Console.Write*` in library code** — `ResultFormatter.cs`, `Handlers/BundleStatementHandlers.cs` write to `Console` instead of `ILogger`. (`Services/PasswordPrompt.cs` console prompt is legitimate.)
- _Verified non-issue:_ the `.Result` hits first flagged in `AggregateEngine`/`WindowEngine`/`ExpressionEvaluator`/`PushdownEngine` are the `WhenClause.Result` **AST property**, not `Task.Result` — no sync-over-async there (comments confirm sort keys are pre-evaluated to avoid it).

### ETL-SQL.Connectors
- [ ] **[Logging · Low] `Console.Write*` in connectors** — `AzureBlobConnector`, `SharePoint/SharePointConnector`, `ActiveDirectory/ActiveDirectoryConnector`, `FtpConnector`, `SftpConnector`, `S3/S3Connector` log via `Console`; connector libraries should use injected `ILogger`.
- [ ] **[Security · Low-Med · verify] Snowflake identifier interpolation** — `Snowflake/SnowflakeDataSource.cs:138,284,354` build `SELECT * FROM {QuoteIdentifier(table)}`. Confirm `QuoteIdentifier` fully escapes and that table names come from trusted connection config, not raw user query text.

### ETL-SQL.Orchestrator
- [ ] **[Security · Low · verify] DDL/PRAGMA interpolation** — `Storage/SQLiteJobHistoryStore.cs:349` (`ALTER TABLE ... ADD COLUMN {ddl}`) and `Storage/SqliteOrchestratorDialect.cs:51` (`PRAGMA table_info({table})`). Identifiers/PRAGMA can't be parameterized; verify `ddl`/`table` come only from the internal schema, never external input.

### ETL-SQL.Reporting
- [ ] **[Perf · Low-Med] `EChartsSsrRenderer.cs:127`** — `_poolSemaphore.Wait()` blocks a thread in an async-capable path; use `await WaitAsync(ct)`.
- [ ] **[Perf · Low-Med] `PdfExporter.cs:150`** — `stream.CopyToAsync(memory).GetAwaiter().GetResult()` blocks inside the export; `await` it.
- [ ] **[Perf · Low] sync facade wrappers** — `PdfExporter.cs:41`, `BrowserReportPdfExporter.cs:27` (`ExportAsync(...).GetAwaiter().GetResult()`): acceptable as sync APIs, but prefer async callers.

### ETL-SQL.Analysis
- [ ] **[Perf · Low] `Linting/Rules/RunScriptDependencyPreflightRule.cs:74`** — `AnalyzeAsync(...).GetAwaiter().GetResult()` inside a `foreach` (sync-over-async in lint preflight); make the rule path async.

### ETL-SQL.LanguageServer
- [ ] **[Logging · Low] obsolete `Logger.Instance`** in `TextDocumentHandler.cs`, `Program.cs`, `DocumentStateStore.cs` → injected `ILogger`. (stdin/stdout wiring in `Program.cs` is correct — no JSON-RPC corruption.)

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
- [ ] **[Security · Low-Med · verify] user-supplied `ValidationRegex` ReDoS** — `App/PipelineGenerator.cs:1166` only *compiles* the column `ValidationRegex` to validate it (safe). Verify that wherever that regex is later *applied to data* it uses a `Regex` **match timeout** (the project already hardened `ParameterUtility`/`ConnectorExceptionWrapper` with `[GeneratedRegex]` + 1000 ms in v0.13.0 — apply the same here).
- [ ] **[Security · Low · verify] `Process.Start` sites** — `App/EngineRunner.cs:1125,1573` spawn external executables (by design for script `exec`/Docker — this is exactly what v0.14.0 Phase 3.5 process-enforcement must gate; cross-reference). `UseShellExecute=true` URL/path launchers in `TUI/ConsoleEditor.cs:657,659`, `TUI/ReportLauncher.cs`, `ReportPlayer/Program.cs`, `ReportBuilder.CLI/Program.cs` open local files/URLs — confirm targets are trusted/local, not attacker-influenced.
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
- [~] **[Perf · Med] `AsNoTracking` gap** — was 32 `AsNoTracking` vs 89 `ToListAsync`. *(User-catalog read now uses `AsNoTracking`; the broader sweep of read-only endpoints — metrics, audit, other catalogs — remains. Verify each is truly read-only before adding.)*

**Still owed (lower priority — not yet read line-by-line):**
- [ ] Remaining Portal controllers/services beyond `AdminController` (Reports, Subscriptions, Datasets) for the same EF patterns; index coverage for the hot audit/metrics queries.
- [ ] Connector streaming vs full-buffer reads on large payloads (per-connector).

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
- [ ] **[Perf · Low] non-streaming sort entry** — `SelectExecutionEngine:528-529` calls `SortExternal(List<Row>)`, which materializes the whole input before spilling (vs the streaming `SortStreamAsync` at :521). Confirm the large-data path always takes the streaming branch.
- [ ] **[Correctness · verify @scale] RIGHT/FULL OUTER via external hash join** — `ExternalJoinEngine.JoinPartitionDirect` only emits unmatched **LEFT** rows. Confirm RIGHT/FULL OUTER joins are swapped to LEFT (or otherwise emit unmatched right rows) before routing to the spilling path, or they will drop unmatched right rows at scale.
- [ ] **[Perf · note] WINDOW** buffers per partition (inherent to frame computation) — cert: 500k rows = 867 MB. Largest single partition bounds memory; document the per-partition limit / consider partition-streaming for frames that allow it.

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


