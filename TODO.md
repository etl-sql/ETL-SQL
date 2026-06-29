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
- [ ] **[Bug · Med-High] `Parser/Parser.cs:850`** — subquery alias is generated with `"Sub_" + new Random().Next(1000,9999)`: a fresh clock-seeded `Random` per call over only 9,000 values. Two subqueries parsed close together can collide on the same alias → ambiguous/incorrect resolution. Fix: monotonic per-parser counter (or GUID suffix).
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
- [ ] **[Perf · Med] `ReportPortal` EF read paths** — only **32 `AsNoTracking`** vs **89 `ToListAsync`**; ~57 read-only materializations carry EF change-tracking overhead. Audit read endpoints (admin user/group lists, metrics, catalogs) and add `AsNoTracking()`.
- [ ] **[Perf · Med] `Controllers/AdminController.cs:294` N+1** — `BulkUpdateUserStatus` loops over the request items and runs `db.Users.FirstOrDefaultAsync(... == item.Id)` once per user. Batch it: `db.Users.Where(u => ids.Contains(u.Id)).ToListAsync()`, then process in memory. (Check the other bulk endpoints — groups/members — for the same shape.)
- [ ] **[Security · Low-Med · verify] user-supplied `ValidationRegex` ReDoS** — `App/PipelineGenerator.cs:1166` only *compiles* the column `ValidationRegex` to validate it (safe). Verify that wherever that regex is later *applied to data* it uses a `Regex` **match timeout** (the project already hardened `ParameterUtility`/`ConnectorExceptionWrapper` with `[GeneratedRegex]` + 1000 ms in v0.13.0 — apply the same here).
- [ ] **[Security · Low · verify] `Process.Start` sites** — `App/EngineRunner.cs:1125,1573` spawn external executables (by design for script `exec`/Docker — this is exactly what v0.14.0 Phase 3.5 process-enforcement must gate; cross-reference). `UseShellExecute=true` URL/path launchers in `TUI/ConsoleEditor.cs:657,659`, `TUI/ReportLauncher.cs`, `ReportPlayer/Program.cs`, `ReportBuilder.CLI/Program.cs` open local files/URLs — confirm targets are trusted/local, not attacker-influenced.
- _Verified non-issues:_ no `BinaryFormatter`/`TypeNameHandling`/`JavaScriptSerializer`; `XmlDataSource.cs:148` is **XXE-safe** (on .NET Core+/.NET 10 `XmlReaderSettings` defaults to `DtdProcessing.Prohibit` + `XmlResolver = null`); large `ReadToEnd`/`ReadAllBytes` hits are bounded (decrypt buffers, 32-byte key file, small embedded resources); no `new Regex` over **user input without a timeout** at match time except the `ValidationRegex` item above.

### Round 2 — deeper per-file review still owed (likely where the performance bulk is)
- [ ] **Engine performance pass** — `Evaluator`, `ExpressionEvaluator`, `SelectExecutionEngine`, and the external `Aggregate`/`Window`/`Join`/`Sort`/`Distinct` engines: unbounded `.ToList()` materialization in streaming paths, repeated enumeration, per-row allocations/boxing, redundant re-parse/re-analysis.
- [ ] **ReportPortal / ReportPortal.Data EF review** — N+1 queries, missing `AsNoTracking()` on read paths, client-side evaluation, and index coverage for the hot admin/metrics/audit queries.
- [ ] **Connector streaming** — full-buffer reads (`ReadAllBytes`/`ReadToEnd`) on large payloads vs. streaming; per-row boxing.

