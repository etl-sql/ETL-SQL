# ETL-SQL Development Roadmap
## VS Code issues
- [x] **On Error should go directly to the messages tab**  Auto-switches to Messages tab on status transition to `'error'`; Pipeline tab shows red glowing dot badge.
- [x] **Paths surrounded in "" the "" should be ignored** `ResolvePath` now strips surrounding double-quotes before resolution, covering all file operations engine-wide.
- [x] **Sometimes when executing there is a serious lag**  REPL process pre-warmed on `.etlsql` file activation; shimmer loading bar + animated Pipeline spinner added during execution.
- [x] **Sample path resolution broken in VS Code debug mode**  `workspaceRoot` passed per-run in REPL JSON protocol; `Evaluator.WorkingDirectory` used as base for `ResolvePath` instead of process CWD.

## TUI — Bug Fixes
- [x] **Execution errors not appearing in Messages panel** (`ConsoleEditor.cs:344`)  When a statement handler throws (e.g., `CREATE CONNECTION` on a duplicate name), the exception is caught but only shown in the status bar — `evaluator.Messages` never receives it.  Fix: in the `catch (Exception ex)` block, also call `_evaluator.Log(ex.Message)` (or the equivalent `AddMessage`) so the message panel shows the error alongside the faulted tree node.

## TUI — Status Bar Improvements
- [x] **Left/center/right zones** — shortcuts left, file+mode pill center, cursor+elapsed right
- [x] **Active mode pill** — colored: grey Pipeline, yellow Results/Focus, cyan Perf, magenta Compare, red Error
- [x] **Elapsed time** — `⏱ Xms` shown after each run
- [x] **Dirty indicator** — `●` unsaved, `○` clean

## TUI — F1 Help Menu Improvements
- [x] **Grouped by category** — View, Execution, File, Editing, Navigation sections
- [x] **Live state annotations** — F6 shows `now: EDITOR/RESULTS`, F4 shows `now: PIPELINE/RESULTS/PERF`
- [x] **Left-aligned overlay** — no longer clears half the screen
- [x] **Any key to close**

## TUI — Results Panel Improvements
- [x] **Column filter** — Ctrl+F in Results focus; Escape clears; header shows match count
- [x] **Export to CSV** — Ctrl+P; proper RFC 4180 escaping; exports active result set
- [x] **Compare mode** — F7 enters; auto-maximizes; all sets stacked; F8 cycles pane; per-pane scroll+filter

## TUI — SHOW Command Output
- [x] **All SHOW commands surface in Results panel** — All 12 handlers now add to `context.LastResultSets`; `SHOW PROFILE` no longer calls `AnsiConsole.Write()` directly.

## Documentation

## Up Next
- [x] **Credential Auto-Decryption Expansion** `decryptSensitive: true` applied to all credential-bearing handlers: CREATE/ALTER CONNECTION, BULK INSERT, ENCRYPT/DECRYPT FILE, ENCRYPT/DECRYPT DIRECTORY, CREATE SSH KEY PAIR.

- [x] **Version 0.7.0: Arrow Columnar Format — Phase A (SpillStore IPC)**
    - Strategy document complete: `Docs/Strategy/Arrow_Columnar_Strategy.md`
    - **Phase A implemented:** `ArrowSpillWriter`/`ArrowSpillReader` replace JSON-line spill in `SpillStore.cs`.
    - `CREATE COLUMNAR TABLE` syntax and full `DataTable` replacement (Phase B/C) explicitly deferred.
    - **`Security:SpillFormat`** config key added — `"Arrow"` (default).
        
- [ ] **Security Manifest**: Strategy document for script signing.
- [ ] **Data Lake Connection brainstorm**: Strategy document complete.
- [ ] **Fresh Eyes Deep Code Architecture & Refactor Audit**
    - [ ] **De-bloat `Evaluator.cs`**: Extract concerns (Reporting, Metrics, Variable Scoping) to specialized services; current class is a "God Object" (60KB).
    - [ ] **Refactor `SelectStatementHandler.cs` (SRP Violation)**: Move CTE registration, Lineage tracking, and Pushdown logic to dedicated engines/helpers.
    - [ ] **Harden `CreateConnectionStatementHandler`**: Replace hardcoded `fileConnectors` list with interface-based capability detection for `ResolvePath` enforcement.
    - [ ] **Centralize Security Guardrails**: Move manual recursion and `IncrementOperationCount` logic in `DirectoryOperationStatementHandler` to a centralized file system security policy.
    - [ ] **Simplify `ExpressionEvaluator`**: Move ANSI string/date functions (`SUBSTRING`, `OVERLAY`, etc.) to `FunctionRegistry` and investigate performance of `ResolveIdentifierFallback` on wide rows.

---

## Report Portal (v1)

> Strategy: `Docs/Strategy/ReportPortal_Strategy.md`

### P0 — Foundation

- [x] **P0-A: New project scaffold** — Create `ETL-SQL.ReportPortal` as an ASP.NET Core / Kestrel project; add to `ETL-SQL.slnx`; wire project references to `ETL-SQL.Engine`, `ETL-SQL.ReportBuilder`, `ETL-SQL.Connectors`.
- [x] **P0-B: EF Core schema + migrations** — Define all 13 tables (`Users`, `Roles`, `UserRoles`, `Groups`, `UserGroups`, `Folders`, `FolderAcl`, `Reports`, `ReportSnapshots`, `Subscriptions`, `SmtpConnections`, `AuditLog`, `DatasetJobs`, `RefreshTokens`) as EF Core `record`/entity types; initial migration; enable SQLite WAL mode on `portal.db` startup.
- [x] **P0-C: Configuration skeleton** — `appsettings.json` with `Portal:DatabasePath`, `ScriptRootPath`, `SnapshotDirectory`, `Resources`, `Jwt`, and `FirstRun` sections; startup guard: refuse to start if `Jwt:Secret` is missing or < 32 chars.

### Phase 1 — Identity, Catalog & Admin Scripting

- [x] **1.1 ASP.NET Core Identity + JWT** — Wire Identity to the portal SQLite DB; `POST /api/auth/login` returns `{ token, refreshToken, expiresAt }`; `POST /api/auth/refresh`; refresh token rotation in `RefreshTokens`; first-run bootstraps `admin` with `MustChangePassword = true`; rate-limit: 5 failed attempts / IP / 15 min.
- [x] **1.2 Folder management REST API** — `GET /api/folders` (ACL-filtered tree), `POST /api/folders` (Publisher+), folder hierarchy stored in `Folders` table only (not filesystem).
- [x] **1.3 Report catalog REST API** — `GET /api/folders/{id}/reports`, `POST /api/reports` (publish), `PUT /api/reports/{id}` (update metadata), `DELETE /api/reports/{id}` (Manage permission); `ScriptLastModified` tracked for snapshot invalidation.
- [x] **1.4 Admin user & group REST API** — `GET /api/admin/users`, `POST /api/admin/users`, role assignment, group membership, `GET /api/admin/audit` (paginated); `GRANT`/`REVOKE` ACL endpoints.
- [x] **1.5 Parser additions — portal admin language** — Add token types and AST nodes in `ETL-SQL.Core` for: `CREATE/ALTER/DROP USER`, `CREATE/DROP GROUP`, `ADD USER TO GROUP`, `CREATE/DROP FOLDER`, `GRANT/REVOKE ON FOLDER TO GROUP`, `PUBLISH/ALTER/DROP REPORT`, `CREATE/TRIGGER/DROP REFRESH JOB`, `DROP/REBUILD SNAPSHOT`, `CREATE/ALTER/DROP SUBSCRIPTION`, `DISCONNECT USER`, `REVOKE TOKENS FOR USER`, `RESTART/SHUTDOWN PORTAL`, `SHOW USERS/REPORTS/ACTIVE SESSIONS`. Register `REPORTPORTAL` connection type in `ETL-SQL.Connectors`; all admin ops execute inside `EXECUTE portal BEGIN…END`.
- [x] **1.6 Swagger UI** — Swashbuckle wired; all Phase 1 endpoints documented and manually smoke-tested via Swagger.

### Phase 2 — Report Execution & Snapshot Service

- [x] **2.1 Async report execution** — `POST /api/reports/{id}/execute` (Execute permission required); parameters validated against declared page params; async job model returns `jobId`; `GET /api/jobs/{jobId}` for status; capped by `MaxConcurrentReportExecutions` (default 4); cancelled after `ExecutionTimeoutSeconds` (default 300); manifest written via `SnapshotStore`; `ManifestPath` stored in `ReportSnapshots`.
- [x] **2.2 Snapshot endpoint** — `GET /api/reports/{id}/snapshot` returns manifest from disk (Read permission); checks `File.GetLastWriteTimeUtc` vs `ScriptLastModified` and returns `{ stale: true }` if outdated; does not re-execute.
- [x] **2.3 Per-user session cache** — `ConcurrentDictionary<(reportId, userId), DashboardService>` with LRU eviction at `SessionCacheMaxSize` entries and `SessionCacheTtlMinutes` idle TTL; transparent rebuild from snapshot on eviction.
- [x] **2.4 Orchestrator poller** — `BackgroundService` polling `ExecutionHistory` SQLite every 60 s; on job completion: invalidate snapshot, queue background re-execution, expose "Refreshing…" status; gracefully degraded when Orchestrator is unreachable.
- [x] **2.5 Manual refresh** — `POST /api/reports/{id}/refresh` (Execute); debounced — returns in-progress `jobId` if refresh already running.

### Phase 3 — Web Frontend

- [ ] **3.1 Static asset pipeline** — Copy `report-runtime.js` + ECharts bundle from `ETL-SQL.ReportPlayer/wwwroot` via `.csproj` file-copy target (build-time, not manual); serve from `ReportPortal/wwwroot`.
- [ ] **3.2 Login page (`/login`)** — Username + password form; JWT stored to `sessionStorage`; redirect to `/` on success; `MustChangePassword` flow blocks access until password changed.
- [ ] **3.3 Report browser (`/`)** — Folder tree sidebar (ACL-filtered); report list for selected folder; stale/refreshing badges.
- [ ] **3.4 Report viewer (`/reports/{id}`)** — Renders manifest via `report-runtime.js`; slicer/parameter POST → `SetParameterAsync` on session → updated manifest returned; no snapshot write on parameter change.
- [ ] **3.5 Export modal (`/reports/{id}/export`)** — CSV and PDF download buttons; triggers Phase 4 endpoints.
- [ ] **3.6 Admin pages** — `/admin/users` (user/group management), `/admin/folders` (folder + ACL), `/admin/subscriptions` (global overview), `/profile/subscriptions` (user self-service view).

### Phase 4 — Export

- [ ] **4.1 CSV export** — `GET /api/reports/{id}/export/csv?visual=<name>`; reads rows from manifest at `ManifestPath`; RFC 4180 writer; no re-execution; Read permission.
- [ ] **4.2 PDF export** — `GET /api/reports/{id}/export/pdf`; calls `PdfExporter.Export(manifest)`; `SvgChartRenderer` for charts; QuestPDF for tables; no Chromium; Read permission; rate-limited per user.

### Phase 5 — Email Subscriptions & Scheduled Delivery

- [x] **5.1 Subscription REST API** — CRUD for `Subscriptions`; `SmtpConnections` management (admin-only) CRUD endpoints; recipient email falls back to user profile.
- [x] **5.2 Orchestrator job creation** — `POST /api/subscriptions` generates a `.etlsql` job script (`EXPORT REPORT … FORMAT … TO … ; SEND EMAIL …`) under `ScriptRoot/subscriptions/`, registers it as a `JobDefinition` in the Orchestrator's SQLite DB. `DELETE /api/subscriptions/{id}` removes the job and script. `GET /api/subscriptions/{id}/history` returns Orchestrator `JobHistory` entries. Orchestrator owns all scheduling, retries, and execution — the portal has no dispatcher.
- [x] **5.3 EXPORT REPORT statement** — `EXPORT REPORT 'path.rptsql' FORMAT PDF|CSV|MARKDOWN TO 'output'` added to language (Lexer, AST, parser, `ExportReportStatementHandler` in `ETL-SQL.ReportBuilder`). DI assembly scan extended to include ReportBuilder handlers. Orchestrator `SchedulerService.CalculateNextRun` extended with WEEK and MONTH units.
- [x] **5.4 SMTP credential encryption** — `SmtpPasswordProtector` wraps .NET Data Protection API; passwords stored encrypted in DB; decrypted inline when generating job scripts.
- [ ] **5.5 Subscription parameters / date filters** — Allow subscribers to specify report parameter overrides (e.g., a `@StartDate` / `@EndDate` pair so a monthly subscription automatically scopes to last month). Requires: parameter declaration on subscription create, storage in a `ParametersJson` column, and injection as `SET @Param = ...` lines at the top of the generated `.etlsql` job script.

### Phase 6 — Admin Hardening & Operations

- [x] **6.1 Health endpoint** — `GET /health` with JSON response; DB, Orchestrator, and execution-capacity checks; Healthy/Degraded/Unhealthy roll-up.
- [x] **6.2 Audit log UI** — Audit tab in admin.html; login events, report actions, admin actions; RFC 4180 CSV export via `GET /api/admin/audit/export/csv`.
- [x] **6.3 Security hardening** — HTTPS + HSTS in production; script path traversal guard in ReportsController; `MustChangePassword` middleware blocks `/api/*` until password changed; SMTP credentials stored via Data Protection API; JWT secret validated at startup via `JwtSecretValidationService`.
- [x] **6.4 End-to-end integration tests** — 9 xUnit integration tests via `WebApplicationFactory<PortalMarker>` + in-process SQLite: health, auth flow, MustChangePassword, user/folder CRUD, report publish, subscription CRUD, audit log, CSV export; all 9 passing.

---