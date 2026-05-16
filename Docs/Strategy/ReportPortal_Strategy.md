# ETL-SQL Report Portal — Development Strategy

**Status:** Design complete — implementation ready  
**Date:** 2026-04-24

---

## Vision

A self-hosted, cross-platform report portal for small-to-medium enterprises.
Comparable role to **SSRS**: organizations run the server internally, authors publish
`.rptsql` reports to folders, and end users log in via a browser to view, filter,
and subscribe to those reports.

The portal is a natural extension of the existing `ETL-SQL.ReportPlayer` (live-view
Kestrel server). It adds identity, a report catalog, Orchestrator-driven dataset
refreshes, and email delivery — without introducing external infrastructure
dependencies. A single `dotnet` process on a single server is the target deployment
unit for v1. Everything manageable via the web UI is also scriptable via ETL-SQL
statements.

---

## Comparison: Current vs. Target

| Capability | Today (ReportPlayer) | Target (Report Portal) |
| :--- | :--- | :--- |
| Report execution | One script per server process | Many reports in a catalog |
| Access control | None (open HTTP) | Login + role-based folder permissions |
| Dataset refresh | On-demand per request | Orchestrator-scheduled + manual trigger |
| Export | None | CSV (tables) and PDF (charts/visuals) |
| Delivery | Browser only | Browser + scheduled email subscription |
| User management | None | Web UI + scriptable ETL-SQL statements |
| Audit | None | Access log, refresh log |

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│                  ETL-SQL.ReportPortal                   │
│        (new ASP.NET Core / Kestrel project)             │
│                                                         │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌────────┐  │
│  │  Auth /  │  │  Report  │  │ Delivery │  │ Admin  │  │
│  │  RBAC    │  │ Catalog  │  │ (Email/  │  │   UI   │  │
│  │          │  │ + Viewer │  │  Export) │  │        │  │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘  └───┬────┘  │
│       └─────────────┴─────────────┴────────────┘       │
│                          │                             │
│              ┌───────────┴──────────┐                  │
│              │   Portal SQLite DB   │                  │
│              │  (users, roles,      │                  │
│              │   folders, catalog,  │                  │
│              │   subscriptions)     │                  │
│              └──────────────────────┘                  │
└──────────┬──────────────────────────────────────────────┘
           │
           ├── ETL-SQL.ReportHosting  (report sessions + parameter state)
           ├── ETL-SQL.Reporting      (manifest + Csv/Pdf exporters)
           ├── ETL-SQL.Engine         (query execution + SEND EMAIL handler)
           └── ETL-SQL.Orchestrator   (dataset refresh jobs — separate process)
```

**Technology choices:**

| Concern | Choice | Rationale |
| :--- | :--- | :--- |
| Web framework | ASP.NET Core (Kestrel) | Already used by ReportPlayer; no IIS dependency |
| Portal metadata | SQLite via EF Core | Zero-install, single file, cross-platform |
| Authentication | ASP.NET Core Identity + JWT | Built-in; bcrypt password hashing by default |
| PDF export | `PdfExporter` + `SvgChartRenderer` (QuestPDF) | Already built in `ETL-SQL.Reporting`; no external runtime |
| CSV export | `CsvRenderer` | Shared with engine/portal export paths |
| Email delivery | Engine `SEND EMAIL` statement | Existing `EmailStatementHandler`; portal generates + executes the script |
| Deployment | `dotnet publish --self-contained` | Single-folder xcopy deploy |

---

## Decisions

1. **Script storage**: Filesystem. `.rptsql` files live under `ScriptRootPath` on the
   server; the catalog stores the relative path. Source control is the author's
   workflow — files are checked in/out normally and the portal detects changes
   automatically (see decision 5).

2. **Session isolation**: Per-user. Each user gets their own `ETL-SQL.ReportHosting.DashboardService`
   instance per report so parameter state (slicer selections, date ranges, etc.) is
   independent. Implemented as `ConcurrentDictionary<(reportId, userId), DashboardService>`
   with LRU eviction controlled by `SessionCacheMaxSize` (max entries) and
   `SessionCacheTtlMinutes` (idle expiry). When a session is evicted, the user's
   next parameter interaction transparently rebuilds it from the current snapshot.

3. **PDF rendering**: No Chromium. The existing `PdfExporter` + `SvgChartRenderer`
   in `ETL-SQL.Reporting` renders charts from the `ReportManifest` data directly —
   same pipeline as `etl-sql-report build --format pdf`. All native libs ship
   cross-platform in the QuestPDF NuGet package.

4. **Orchestrator integration**: The Orchestrator is a separate process and is unaware
   of the portal. The portal creates dataset-refresh jobs in the Orchestrator by
   executing `CREATE JOB ... AT orch` through the engine using an `ORCHESTRATOR`
   connection. The portal detects job completions by polling the Orchestrator's
   `ExecutionHistory` SQLite table every 60 seconds — no HTTP coupling.

5. **Snapshot invalidation**: Automatic on script file change. The `Reports` table
   stores `ScriptLastModified`; on each snapshot request the portal checks
   `File.GetLastWriteTimeUtc` and invalidates if it has advanced. A source-control
   push triggers fresh data on next view with no manual portal action.

6. **Email delivery**: The portal generates and executes an ETL-SQL script using the
   existing `SEND EMAIL ... AT` syntax. The subscription record stores the SMTP
   connection alias and recipient list; the portal builds the script at delivery time
   and runs it through the engine. No separate email library in the portal —
   `EmailStatementHandler` handles everything. The temp attachment file is wrapped
   in `try/finally` to guarantee cleanup on both success and failure.

7. **Password security**: Passwords are never stored or logged in plaintext anywhere.
   ASP.NET Core Identity stores bcrypt hashes (PBKDF2). Passwords in admin scripts
   use `ENC:` encrypted values or `USE PASSWORD PROMPT`; plaintext is never persisted.
   SMTP credentials are stored using `ENC:` in the portal DB. The first-run admin
   account requires a password change on first login. Password reset for regular
   users is admin-reset only in v1; self-service email reset is a v2 item.

8. **Resource management**: The Portal and Orchestrator manage system resources
   independently. Each process respects system limits through its own configuration;
   the OS resolves contention. Dataset refresh jobs are registered with and fully
   owned by the Orchestrator. Ad-hoc report execution runs directly through the
   Portal's own `Evaluator` instances, capped by `Resources:MaxConcurrentReportExecutions`.
   No coordination protocol exists between the two processes. Disk I/O contention
   (both processes spill to disk) is an operational concern — admins tune concurrency
   limits based on available storage throughput.

9. **Portal connection type**: Portal and Orchestrator are administered via dedicated
   connector types. All admin operations execute inside an `EXECUTE <alias> BEGIN...END`
   block — there is no per-statement `ON alias` qualifier. HTTP REST is the transport;
   direct SQLite access is not supported. Error behavior inside an admin block is
   stop-on-first-error with no cross-statement rollback. `GO` as a sub-batch separator
   is deferred to v1.1.

   ```sql
   CREATE CONNECTION portal ON REPORTPORTAL(
       HOST = 'report-server.company.com',
       PORT = 5001,
       USER = 'admin',
       PASSWORD = ENC:...
   );

   CREATE CONNECTION orch ON ORCHESTRATOR(
       HOST = 'orch-server.company.com',
       PORT = 5100,
       USER = 'admin',
       PASSWORD = ENC:...
   );
   ```

10. **Report script execution scope**: `.rptsql` files executed by the portal have
    full engine capability. No statement types are restricted. Trust is granted at
    the Publisher level — the same model as any ETL-SQL script author.

11. **Snapshot storage**: `ReportManifest` is written to disk via the existing
    `SnapshotStore` in `ETL-SQL.Reporting`. The `ReportSnapshots` table stores a
    file path (`ManifestPath`), not the JSON body. This avoids SQLite bloat from
    large manifests (large reports can be megabytes of JSON) and reuses the existing
    snapshot infrastructure.

12. **JWT token storage**: JWT is stored in browser `sessionStorage`. This is a
    conscious tradeoff — sessionStorage is accessible to JavaScript (XSS risk) but
    avoids CSRF complexity. Acceptable for an internal self-hosted portal. Document
    in the security section; revisit for v2 if the threat model changes.

13. **Subscription group membership**: Evaluated at delivery time against live group
    membership, not at subscription-creation time. This ensures that when a user
    joins or leaves a group, they automatically start or stop receiving reports on
    the next delivery without any subscription changes.

---

## Pre-Work (Lock In Before Phase 1)

### P0-A — Permission Model

```
User → Groups → Folder ACL → [Read | Execute | Manage]

Read      = see the report exists; view cached snapshots and exports
Execute   = run the report with custom parameters; trigger manual refresh
Manage    = edit metadata, schedules, ACL; delete reports and folders
Admin     = implicit Manage everywhere + user/group/role administration
Publisher = Manage in folders they own or are granted Manage on
```

Permissions are granted to **groups**, not individual users. Users belong to one or
more groups. This mirrors the SSRS / Windows file share model and keeps ACL
maintenance tractable at scale.

### P0-B — Portal SQLite Schema

```sql
Users           (Id, Username, FirstName, LastName, MiddleInitial, Email, PasswordHash, IsActive, MustChangePassword, CreatedAt)
Roles           (Id, Name)                              -- Admin, Publisher, Viewer
UserRoles       (UserId, RoleId)
Groups          (Id, Name, Description)
UserGroups      (UserId, GroupId)
Folders         (Id, ParentId, Name, Path, OwnerId)
FolderAcl       (Id, FolderId, GroupId, Permission)     -- Read/Execute/Manage
Reports         (Id, FolderId, Name, Description, ScriptPath, ScriptLastModified,
                 CreatedBy, CreatedAt, UpdatedAt)
ReportSnapshots (Id, ReportId, ManifestPath, BuiltAt, BuiltBy, ParametersJson)
Subscriptions   (Id, ReportId, UserId, Schedule, DeliverOnRefresh, Format,
                 SmtpAlias, Recipients, LastSentAt, NextRunAt, FailCount, IsActive)
SmtpConnections (Id, Alias, Host, Port, Username, EncryptedPassword, FromAddress, UseSsl)
AuditLog        (Id, UserId, Action, ResourceType, ResourceId, Timestamp, Detail)
DatasetJobs     (Id, ReportId, OrchestratorJobName, RefreshInterval, LastRefreshedAt)
RefreshTokens   (Id, UserId, Token, ExpiresAt, RevokedAt)
```

**Key schema notes:**
- `ReportSnapshots.ManifestPath` — relative path to the manifest file on disk managed
  by `SnapshotStore`. Never the JSON body.
- `Subscriptions.SmtpAlias` — references `SmtpConnections.Alias` by name.
- `SmtpConnections` — registered via admin script; credentials stored `ENC:` encrypted
  via .NET Data Protection API.
- SQLite WAL mode must be explicitly enabled on `portal.db`. The portal's HTTP
  request handlers and both background services (`SubscriptionDispatcher`,
  Orchestrator poller) write concurrently — the default journal mode will cause
  contention.

### P0-B — CASCADE Semantics

| Statement | Without CASCADE | With CASCADE |
| :--- | :--- | :--- |
| `DROP FOLDER '/path'` | Error if has reports or subfolders | Removes all reports, subfolders, ACL entries |
| `DROP GROUP 'name'` | Error if has members or ACL entries | Removes memberships and ACL entries; users remain |
| `DROP USER 'name'` | Error if has active subscriptions | Removes subscriptions, sessions, group memberships |
| `DROP REPORT 'name'` | Error if has active subscriptions | Removes subscriptions and snapshot file |

---

## Phase 1 — Identity, Catalog & Admin Scripting

**Goal:** Users can log in. Reports exist in a folder tree. All management is
available both via the web UI and via ETL-SQL admin scripts.

### 1.1 Auth
- ASP.NET Core Identity backed by the portal SQLite DB
- JWT bearer tokens (stateless; suitable for both browser and API consumers)
- Refresh token rotation stored in `RefreshTokens` table
- First-run bootstraps a default `admin` account; `MustChangePassword = true` forces
  a password change before any other action
- Login endpoint returns `{ token, refreshToken, expiresAt }`
- Rate-limited: 5 failed attempts per IP per 15 minutes before lockout
- JWT stored in browser `sessionStorage` (see Decision 12)

### 1.2 Folder Management
Report Portal folders are organizational containers — distinct from filesystem
directories (`CREATE DIRECTORY`). Folders exist only in the portal SQLite DB and
carry ACL entries. File paths are never exposed to end users.

`CREATE DIRECTORY` inside a `.rptsql` report script is a linter error
(`CreateDirectoryInReport` rule) — authors use `CREATE FOLDER` instead.

### 1.3 Portal Admin Script Language
All portal administration is scriptable by executing an ETL-SQL script against a
`REPORTPORTAL` connection. Every admin statement executes inside an
`EXECUTE portal BEGIN...END` block.

Full syntax reference: **Grammar.md Appendix B**.

```sql
CREATE CONNECTION portal ON REPORTPORTAL(
    HOST = 'report-server.company.com',
    PORT = 5001,
    USER = 'admin',
    PASSWORD = ENC:...
);

EXECUTE portal BEGIN

    -- Users
    CREATE USER 'john.doe'
        WITH (EMAIL = 'john@company.com', PASSWORD = ENC:..., ROLE = Viewer);
    ALTER USER 'john.doe' SET ROLE = Publisher;
    ALTER USER 'john.doe' SET DISABLE;
    DROP USER 'john.doe' CASCADE;

    -- Groups
    CREATE GROUP 'Finance' WITH (DESCRIPTION = 'Finance department');
    ADD USER 'john.doe' TO GROUP 'Finance';

    -- Folders
    CREATE FOLDER '/Finance';
    CREATE FOLDER '/Finance/Monthly';
    DROP FOLDER '/Finance' CASCADE;

    -- Permissions
    GRANT READ    ON FOLDER '/Finance' TO GROUP 'Finance';
    GRANT EXECUTE ON FOLDER '/Finance' TO GROUP 'FinanceAnalysts';
    GRANT MANAGE  ON FOLDER '/Finance' TO GROUP 'FinanceAdmins';
    REVOKE READ   ON FOLDER '/Finance' FROM GROUP 'Finance';

    -- Report catalog
    PUBLISH REPORT 'Monthly Sales'
        FROM '/reports/finance/monthly_sales.rptsql'
        IN FOLDER '/Finance'
        WITH (DESCRIPTION = 'Monthly revenue by region');
    ALTER REPORT 'Monthly Sales' SET FOLDER = '/Finance/Archive';

    CREATE SETS !PROD
    BEGIN
        @PortalEnvironment = 'PROD';
        SET WITH_PROMPT ON;
    END
    USE SETS !PROD;
    IF @PortalEnvironment = 'PROD'
    BEGIN
        PUBLISH REPORT 'Monthly Sales'
            FROM 'C:\Reports\Prod\monthly_sales.rptsql'
            IN FOLDER '/Finance'
            WITH (TAGS = 'finance,monthly,certified');
    END
    DROP REPORT 'Monthly Sales' CASCADE;

    -- Dataset refresh jobs (registered in the Orchestrator)
    CREATE REFRESH JOB FOR REPORT 'Monthly Sales'
        SCHEDULE '0 2 * * *'
        AT orch;
    REFRESH REPORT 'Monthly Sales';
    DROP REFRESH JOB FOR REPORT 'Monthly Sales';

    -- Dataset registry
    REFRESH DATASET 'Sales Summary' IN FOLDER '/Finance';
    ALTER DATASET 'Sales Summary' IN FOLDER '/Finance'
        SET ACCESS = PUBLIC, TTL = '2h';
    GRANT EDITOR ON DATASET 'Sales Summary' IN FOLDER '/Finance' TO GROUP 'FinanceAnalysts';
    REVOKE EDITOR ON DATASET 'Sales Summary' IN FOLDER '/Finance' FROM GROUP 'FinanceAnalysts';
    DROP DATASET 'Sales Summary' IN FOLDER '/Finance';

    -- Snapshots
    DROP SNAPSHOT FOR REPORT 'Monthly Sales';
    REBUILD SNAPSHOT FOR REPORT 'Monthly Sales';

    -- SMTP connections
    CREATE CONNECTION corporate-smtp ON SMTP(
        HOST = 'mail.company.com', PORT = 587,
        USER = 'reports@company.com', PASSWORD = ENC:...,
        USE_SSL = true, DEFAULT_FROM = 'reports@company.com'
    );

    -- Subscriptions (group membership evaluated at delivery time)
    CREATE SUBSCRIPTION FOR REPORT '/Finance/MonthlySales'
        DELIVER TO 'john.doe'
        SCHEDULE '0 8 * * MON'
        FORMAT PDF
        AT corporate-smtp;

    CREATE SUBSCRIPTION FOR REPORT '/Finance/MonthlySales'
        DELIVER TO GROUP 'Finance'
        ON REFRESH
        FORMAT BOTH
        AT corporate-smtp;

    ALTER SUBSCRIPTION 5 SET SCHEDULE = '0 9 * * MON';
    ALTER SUBSCRIPTION 5 SET ENABLE;
    DROP SUBSCRIPTION 5;

    -- Session management
    DISCONNECT USER 'john.doe';
    REVOKE TOKENS FOR USER 'john.doe';

    -- Service control (responds 202 before acting)
    RESTART PORTAL;
    SHUTDOWN PORTAL;

    -- Metadata queries (full SELECT syntax supported)
    SHOW USERS;
    SHOW REPORTS IN FOLDER '/Finance';
    SHOW ACTIVE SESSIONS;
    SELECT * FROM portal.AuditLog
    WHERE Action = 'LOGIN_FAILED'
      AND Timestamp > DATEADD(DAY, -7, GETDATE());

END
```

Credentials in admin scripts use `ENC:` encrypted values. Script files containing
`CREATE USER` or `ALTER USER SET PASSWORD` should be treated as secrets — not
committed to source control, or committed with placeholder values and a separate
secrets file.

### 1.4 REST API (Phase 1 surface)
- `POST /api/auth/login` — returns `{ token, refreshToken, expiresAt }`
- `POST /api/auth/refresh` — exchange refresh token for new JWT
- `GET /api/folders` — ACL-filtered folder tree
- `GET /api/folders/{id}/reports` — reports the user can Read
- `GET /api/catalog/search?q=...` — permission-aware search across visible folders and reports
- `POST /api/folders` — create folder (Publisher+)
- `POST /api/reports` — publish a report (Publisher+)
- `PUT /api/reports/{id}` — update metadata (Manage)
- `DELETE /api/reports/{id}` — soft-delete (Manage)
- `GET /api/admin/users` — user list (Admin)
- `POST /api/admin/users` — create user (Admin)
- `GET /api/admin/audit` — paginated audit log (Admin)

### 1.4.1 Report metadata contract

Report catalog metadata is script-first. The portal reads header metadata comments
from the `.rptsql` file on publish and republish, then stores the recognized values
as report catalog fields. Explicit REST or script-admin fields override script
metadata when both are supplied.

Canonical portal tags:

| Tag | Catalog field |
| :--- | :--- |
| `@owner` | Owner/team |
| `@contact` | Support contact |
| `@tags` | Comma-separated search/category tags |
| `@category` | Primary catalog category |
| `@domain` | Business/data domain |
| `@steward` | Steward |
| `@certification` or `@trusted` | Trust/certification marker |
| `@description` or `@d` | Description fallback |

### 1.5 Deliverable
REST API with Swagger UI. Parser additions for the portal admin language. No
frontend yet.

---

## Phase 2 — Report Execution & Snapshot Service

**Goal:** Authorized users can run reports. Snapshots are cached and automatically
invalidated on dataset refresh or script change.

### 2.1 Report Execution
- `POST /api/reports/{id}/execute` — runs the `.rptsql` script via `ETL-SQL.ReportHosting.DashboardService`.
  Requires `Execute` permission.
- Parameters passed in request body; validated against declared page parameters.
- Async: returns `jobId`; client polls `GET /api/jobs/{jobId}`.
- On completion, manifest written to disk via `SnapshotStore`; `ManifestPath` stored
  in `ReportSnapshots`.
- Execution is capped by `Resources:MaxConcurrentReportExecutions` (default 4) and
  cancelled after `Resources:ExecutionTimeoutSeconds` (default 300).

### 2.2 Snapshot Cache
- `GET /api/reports/{id}/snapshot` — returns the manifest from `ManifestPath` without
  re-execution. Requires `Read` permission.
- If `ManifestPath` is null or `ScriptLastModified` has advanced since the snapshot
  was built, a "stale" banner is shown. Users with `Execute` can trigger a refresh;
  `Read`-only users see the last known good snapshot.
- Slicer/parameter interactions do not touch the snapshot — they are applied live
  against the per-user `DashboardService` session.

### 2.3 Orchestrator Integration
- Dataset refresh jobs are created in the Orchestrator via `CREATE JOB ... AT orch`
  executed through the engine. `DatasetJobs.OrchestratorJobName` stores the job name.
- A portal `BackgroundService` polls the Orchestrator's `ExecutionHistory` SQLite
  table every 60 seconds for job completions.
- On completion, the portal invalidates the snapshot and queues a background
  re-execution. The UI shows a "Refreshing…" badge until the new snapshot is ready.
- If the Orchestrator is unreachable, the portal continues serving cached snapshots
  normally — this is a **degraded** state, not a failure (see §6.3).

### 2.4 Manual Refresh
- `POST /api/reports/{id}/refresh` — background re-execution. Requires `Execute`.
- Debounced: if a refresh is already running, returns the in-progress `jobId`.

---

## Phase 3 — Web Frontend

**Goal:** End users experience this as an SSRS-style report browser.

### 3.1 Technology
Vanilla JS + HTMX served from `wwwroot`. No build pipeline required; output bundles
cleanly into the published artifact. Upgrade to React/Vue in v2 if complexity demands.

### 3.2 Pages

| Route | Description |
| :--- | :--- |
| `/login` | Username + password → JWT stored in `sessionStorage` |
| `/` | Folder tree sidebar + report list for selected folder |
| `/reports/{id}` | Report viewer — renders manifest via `report-runtime.js` |
| `/reports/{id}/export` | Export modal (CSV / PDF download) |
| `/admin/users` | User/group management (Admin only) |
| `/admin/folders` | Folder + ACL management |
| `/admin/subscriptions` | Global subscription overview |
| `/profile/subscriptions` | User's own subscription list and preferences |

### 3.3 Report Viewer Reuse
`report-runtime.js`, CSS, and browser dependencies are synced from
`src/ETL-SQL.ReportRuntime/Resources/Shared` into ReportPlayer, ReportPortal, and
the VS Code extension. This ensures every host ships the same report canvas.

### 3.4 Parameter Interaction
Slicer and parameter changes POST to the backend, which calls `SetParameterAsync` on
the user's `DashboardService` session and returns an updated manifest. The snapshot
is not touched — parameter state lives in the session only. If the session was evicted
(LRU), the backend transparently rebuilds it from the current snapshot before applying
the parameter.

---

## Phase 4 — Export (CSV & PDF)

### 4.1 CSV Export
- `GET /api/reports/{id}/export/csv?visual=SalesTable`
- Reads rows from the manifest at `ManifestPath`. No re-execution.
- RFC 4180 writer; no additional dependency.
- Requires `Read`.

### 4.2 PDF Export
- `GET /api/reports/{id}/export/pdf`
- Calls `new PdfExporter().Export(manifest)` — same pipeline as
  `etl-sql-report build --format pdf`.
- Charts rendered by `SvgChartRenderer` (server-side SVG from manifest data).
  Tables laid out natively by QuestPDF. No Chromium, no external runtime.
- Requires `Read`.

---

## Phase 5 — Email Subscriptions & Scheduled Delivery

### 5.1 Subscription Model
Stored in the `Subscriptions` table. Key fields:
- `Schedule` — cron expression (`0 8 * * MON`) or null if `DeliverOnRefresh = true`
- `Format` — `PDF`, `CSV`, or `BOTH`
- `SmtpAlias` — references `SmtpConnections.Alias`
- `Recipients` — comma-separated addresses or group name
- Group membership resolved at delivery time (not at subscription-creation time)

### 5.2 Email Delivery via Engine Script
The portal does **not** implement a separate email library. When a delivery fires,
the portal generates and executes the following ETL-SQL script through the engine:

```sql
-- Generated by SubscriptionDispatcher at delivery time
CREATE CONNECTION _smtp ON SMTP(
    HOST     = 'mail.company.com',
    PORT     = 587,
    USER     = 'reports@company.com',
    PASSWORD = ENC:...   -- resolved from SmtpConnections table
);

SEND EMAIL
    TO      'john.doe@company.com'
    SUBJECT 'Monthly Sales Report — 2026-04-24'
    BODY    '<p>Please find the attached report.</p>'
    ATTACH  'C:\portal\temp\snapshot_42.pdf'
    AT      _smtp;
```

`EmailStatementHandler` handles delivery. The portal builds the script, resolves
credentials from `SmtpConnections`, renders the attachment, and cleans up the temp
file in a `try/finally` block. Delivery failures are logged to `AuditLog` and
retried on the next tick up to 3 times before the subscription is disabled.

### 5.3 Scheduler
- `SubscriptionDispatcher` — a `BackgroundService` that wakes every minute.
- Queries `Subscriptions` where `NextRunAt <= now AND IsActive = true`.
- For each due subscription: loads the manifest from `ManifestPath`, renders the
  export, executes the email script, updates `LastSentAt` / `NextRunAt`.
- Cron parsing: `Cronos` NuGet package (lightweight, no Quartz needed at this scale).

### 5.4 Orchestrator-Triggered Delivery
Subscriptions with `DeliverOnRefresh = true` fire immediately when the portal detects
a dataset refresh completion (Phase 2.3), regardless of the cron schedule. This
covers the "send the report whenever the nightly ETL finishes" pattern.

---

## Phase 6 — Admin Hardening & Operations

### 6.1 Configuration

```json
{
  "Portal": {
    "DatabasePath": "./portal.db",
    "ScriptRootPath": "./Reports",
    "SnapshotDirectory": "./Snapshots",
    "Resources": {
      "MaxConcurrentReportExecutions": 4,
      "ExecutionTimeoutSeconds": 300,
      "SessionCacheMaxSize": 50,
      "SessionCacheTtlMinutes": 30
    },
    "Jwt": {
      "Secret": "",
      "ExpiryMinutes": 60,
      "RefreshExpiryDays": 7
    },
    "FirstRun": {
      "AdminUsername": "admin"
    }
  }
}
```

The portal refuses to start if `Jwt:Secret` is empty or fewer than 32 characters.
`SnapshotDirectory` is where `SnapshotStore` writes manifest files; defaults to a
`Snapshots` subdirectory next to the database file.

### 6.2 Security Rules
- **Passwords:** bcrypt/PBKDF2 via ASP.NET Core Identity. Never logged, never returned
  in API responses, never stored in plaintext. Admin scripts use `ENC:` values. SMTP
  credentials stored via .NET Data Protection API encryption.
- **Path traversal:** all script paths resolved through `IExecutionContext.ResolvePath()`
  and validated to remain within `ScriptRootPath`.
- **Login rate limiting:** 5 failed attempts per IP per 15 minutes → 15-minute lockout.
  Heavy export endpoints (PDF generation) also rate-limited per user.
- **JWT:** minimum 32-character secret enforced at startup.
- **HTTPS:** `UseHttpsRedirection` + HSTS enforced in production; HTTP permitted in
  development only.
- **`MustChangePassword`:** set on first-run admin and any admin-reset password;
  blocks all portal actions until changed.
- **Audit log:** all auth events, report executions, exports, and admin actions
  recorded. All parameter values are redacted regardless of content.
- **SQLite WAL mode:** explicitly enabled on `portal.db` to handle concurrent writes
  from HTTP handlers and background services.
- **JWT storage:** `sessionStorage` (see Decision 12).

### 6.3 Health Endpoint
`GET /health` returns status at two severity levels:

| Check | Failure type |
| :--- | :--- |
| Portal DB connectivity | **Failed** — portal cannot serve requests |
| JWT configuration present | **Failed** — portal cannot issue tokens |
| Active execution count vs cap | **Degraded** — approaching resource limit |
| Orchestrator SQLite reachability | **Degraded** — auto-refresh won't fire; portal still serves cached snapshots |
| Pending subscription count | Informational |
| SMTP connector reachability | Informational (optional ping) |

### 6.4 Audit Log UI
Admin page: who ran which report, when; export history; subscription delivery
outcomes; failed logins; admin actions. Exportable as CSV.
All parameter values are shown as `[REDACTED]` in the UI and export.

---

## Development Order

```
P0-A  Permission model (complete — this document)
P0-B  EF Core schema + migrations, SQLite WAL mode        (2 days)
  │
  ▼
Phase 1  Identity + Catalog + Admin Scripts                (1.5 weeks)
  │      (parser additions for portal admin language)
  ▼
Phase 2  Execution + Snapshots                             (1 week)
  │
  ├── Phase 3  Web Frontend                                (2 weeks, parallel with Phase 4)
  │
  └── Phase 4  CSV + PDF Export                            (0.5 week — mostly already built)
        │
        ▼
      Phase 5  Email Subscriptions                         (1 week)
        │
        ▼
      Phase 6  Admin Hardening                             (1 week)
```

**Critical path:** P0-B → Phase 1 → Phase 2 → Phase 5. Frontend and export run in
parallel. Phase 4 is short because `PdfExporter` and CSV generation already exist.

---

## What Can Be Reused Today

| Asset | Location | Reuse |
| :--- | :--- | :--- |
| `DashboardService` | `ReportHosting` | Per-user instance per report, LRU-pooled |
| `ManifestBuilder` | `Reporting` | Produces the manifest written to disk by `SnapshotStore` |
| `SnapshotStore` | `Reporting` | Writes/reads manifest files; portal stores the path in SQLite |
| `PdfExporter` + `SvgChartRenderer` | `Reporting` | Direct call; no changes needed |
| `CsvRenderer` | `Reporting` | Shared CSV table export behavior |
| `report-runtime.js` + ECharts | `ReportRuntime/Resources/Shared` | Canonical assets synced into host `wwwroot`/media folders |
| `EmailStatementHandler` | `Engine` | Portal generates + executes the `SEND EMAIL` script |
| `IExecutionContext.ResolvePath` | `Engine` | Script path boundary enforcement |
| Orchestrator `ExecutionHistory` | `Orchestrator` | Polled every 60s for dataset refresh completions |
