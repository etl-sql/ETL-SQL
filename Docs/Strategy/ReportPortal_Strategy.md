# ETL-SQL Report Portal — Development Strategy

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
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌────────┐   │
│  │  Auth /  │  │  Report  │  │ Delivery │  │ Admin  │   │
│  │  RBAC    │  │ Catalog  │  │ (Email/  │  │   UI   │   │
│  │          │  │ + Viewer │  │  Export) │  │        │   │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘  └───┬────┘   │
│       └─────────────┴─────────────┴────────────┘        │
│                          │                              │
│              ┌───────────┴──────────┐                   │
│              │   Portal SQLite DB   │                   │
│              │  (users, roles,      │                   │
│              │   folders, catalog,  │                   │
│              │   subscriptions)     │                   │
│              └──────────────────────┘                   │
└──────────┬──────────────────────────────────────────────┘
           │
           ├── ETL-SQL.ReportBuilder  (script eval + manifest + PdfExporter)
           ├── ETL-SQL.Engine         (query execution + EMAIL SEND handler)
           └── ETL-SQL.Orchestrator   (dataset refresh jobs — separate process)
```

**Technology choices:**

| Concern | Choice | Rationale |
| :--- | :--- | :--- |
| Web framework | ASP.NET Core (Kestrel) | Already used by ReportPlayer; no IIS dependency |
| Portal metadata | SQLite via EF Core | Zero-install, single file, cross-platform |
| Authentication | ASP.NET Core Identity + JWT | Built-in; bcrypt password hashing by default |
| PDF export | `PdfExporter` + `SvgChartRenderer` (QuestPDF) | Already built in `ReportBuilder`; no external runtime |
| CSV export | Manual RFC 4180 writer | Trivial; no additional dependency |
| Email delivery | Engine `EMAIL SEND` statement | Existing `EmailStatementHandler`; portal generates + executes the script |
| Deployment | `dotnet publish --self-contained` | Single-folder xcopy deploy |

---

## Decisions

1. **Script storage**: Filesystem. `.rptsql` files live under `ScriptRootPath` on the
   server; the catalog stores the relative path. Source control is the author's
   workflow — files are checked in/out normally and the portal detects changes
   automatically (see decision 5).

2. **Session isolation**: Per-user. Each user gets their own `DashboardService`
   instance per report so parameter state (slicer selections, date ranges, etc.) is
   independent. Implemented as `ConcurrentDictionary<(reportId, userId), DashboardService>`
   with LRU eviction to bound memory.

3. **PDF rendering**: No Chromium. The existing `PdfExporter` + `SvgChartRenderer`
   in `ETL-SQL.ReportBuilder` renders charts from the `ReportManifest` data directly —
   same pipeline as `etl-sql-report build --format pdf`. All native libs ship
   cross-platform in the QuestPDF NuGet package.

4. **Orchestrator**: Separate process (`ETL-SQL.Orchestrator.Service`). The Orchestrator
   owns all ETL jobs — report refreshes are just one job type among many. The portal
   registers dataset-refresh jobs and polls the shared Orchestrator SQLite
   `ExecutionHistory` table to detect completions and invalidate snapshots. No HTTP
   coupling; the Orchestrator is unaware of the portal.

5. **Snapshot invalidation**: Automatic on script file change. The `Reports` table
   stores `ScriptLastModified`; on each snapshot request the portal checks
   `File.GetLastWriteTimeUtc` and invalidates if it has advanced. A source-control
   push triggers fresh data on next view with no manual portal action.

6. **Email delivery**: The portal generates and executes an ETL-SQL script using the
   existing `EMAIL SEND` syntax. The subscription record stores the SMTP connector
   name and recipient list; the portal builds the script at delivery time and runs it
   through the engine. No separate email library in the portal — `EmailStatementHandler`
   handles everything.

7. **Password security**: Passwords are never stored or logged in plaintext anywhere.
   ASP.NET Core Identity stores bcrypt hashes (PBKDF2). Passwords appearing in
   `CREATE USER` / `ALTER USER SET PASSWORD` scripts are hashed immediately on
   execution and the plaintext is never persisted. SMTP credentials in configuration
   are stored using .NET Data Protection API encryption. The first-run admin account
   requires a password change on first login.

---

## Pre-Work (Lock In Before Phase 1)

### P0-A — Permission Model

```
User → Groups → Folder ACL → [Read | Execute | Manage]

Read     = see the report exists; view cached snapshots and exports
Execute  = run the report with custom parameters; trigger manual refresh
Manage   = edit metadata, schedules, ACL; delete reports and folders
Admin    = implicit Manage everywhere + user/group/role administration
Publisher = Manage in folders they own or are granted Manage on
```

Permissions are granted to **groups**, not individual users. Users belong to one or
more groups. This mirrors the SSRS / Windows file share model and keeps ACL
maintenance tractable at scale.

### P0-B — Portal SQLite Schema

```sql
Users           (Id, Username, Email, PasswordHash, IsActive, MustChangePassword, CreatedAt)
Roles           (Id, Name)                              -- Admin, Publisher, Viewer
UserRoles       (UserId, RoleId)
Groups          (Id, Name, Description)
UserGroups      (UserId, GroupId)
Folders         (Id, ParentId, Name, Path, OwnerId)
FolderAcl       (Id, FolderId, GroupId, Permission)     -- Read/Execute/Manage
Reports         (Id, FolderId, Name, Description, ScriptPath, ScriptLastModified,
                 CreatedBy, CreatedAt, UpdatedAt)
ReportSnapshots (Id, ReportId, ManifestJson, BuiltAt, BuiltBy, ParametersJson)
Subscriptions   (Id, ReportId, UserId, Schedule, DeliverOnRefresh, Format,
                 SmtpConnector, Recipients, LastSentAt, FailCount, IsActive)
AuditLog        (Id, UserId, Action, ResourceType, ResourceId, Timestamp, Detail)
DatasetJobs     (Id, ReportId, OrchestratorJobId, RefreshInterval, LastRefreshedAt)
```

`ReportSnapshots.ManifestJson` is the serialized `ReportManifest` — the same JSON the
browser already knows how to render. The snapshot is rebuilt only when the Orchestrator
signals a dataset refresh or when the script file changes. Parameter interactions
(slicers, etc.) do not invalidate the snapshot; they are applied live against the
cached `DashboardService` instance for that user session.

---

## Phase 1 — Identity, Catalog & Admin Scripting

**Goal:** Users can log in. Reports exist in a folder tree. All user/group/role
management is available both via the web UI and via ETL-SQL scripts.

### 1.1 Auth
- ASP.NET Core Identity backed by the portal SQLite DB
- JWT bearer tokens (stateless; suitable for both browser and API consumers)
- Refresh token rotation stored in SQLite
- First-run bootstraps a default `admin` account; `MustChangePassword = true` forces
  a reset before any other action
- Login endpoint returns `{ token, refreshToken, expiresAt }`
- Rate-limited: 5 failed attempts per IP per 15 minutes before lockout

### 1.2 Folder Management

Report Portal folders are organizational containers in the report catalog — distinct from
file-system directories created by `CREATE DIRECTORY`. Folders exist only in the portal
SQLite DB (`Folders` table) and carry ACL entries. File paths are never exposed to end users.

```sql
-- Create a folder (optionally nested under a parent path)
CREATE FOLDER '/Finance';
CREATE FOLDER '/Finance/Monthly';

-- Rename or move a folder
ALTER FOLDER '/Finance/Monthly' SET NAME = 'Monthly Reports';
ALTER FOLDER '/Finance/Monthly Reports' SET PARENT = '/Shared';

-- Remove a folder (must be empty unless CASCADE is specified)
DROP FOLDER '/Finance/Monthly Reports';
DROP FOLDER '/Finance' CASCADE;        -- removes all child folders and reports

-- Inspect
SHOW FOLDERS;                          -- full tree visible to the calling user
SHOW FOLDERS UNDER '/Finance';         -- subtree only
SHOW FOLDER '/Finance';                -- detail + ACL entries + report count
```

`CREATE DIRECTORY` (file-system operation) is a linter error inside `.rptsql` report
scripts. The linter rule `CreateDirectoryInReport` will flag it with a message directing
the author to use `CREATE FOLDER` instead.

### 1.3 Portal Admin Script Language

All user, group, role, and permission management is scriptable via `.etlsql` scripts
executed against a portal connection. New statement types:

```sql
-- Users
CREATE USER 'john.doe'
  WITH (EMAIL = 'john@company.com', PASSWORD = 'initial-password', ROLE = Viewer);

ALTER USER 'john.doe' SET EMAIL = 'new@company.com';
ALTER USER 'john.doe' SET ROLE = Publisher;
ALTER USER 'john.doe' SET PASSWORD = 'new-password';  -- hashed immediately; plaintext never stored
ALTER USER 'john.doe' ENABLE;
ALTER USER 'john.doe' DISABLE;

DROP USER 'john.doe';

SHOW USERS;                          -- Id, Username, Email, Role, IsActive, CreatedAt
SHOW USER 'john.doe';                -- detail + group memberships

-- Groups
CREATE GROUP 'Finance' WITH (DESCRIPTION = 'Finance department');
ALTER GROUP 'Finance' SET DESCRIPTION = 'Finance and Accounting';
DROP GROUP 'Finance';

ADD USER 'john.doe' TO GROUP 'Finance';
REMOVE USER 'john.doe' FROM GROUP 'Finance';

SHOW GROUPS;
SHOW GROUP 'Finance';                -- members list
SHOW GROUPS FOR USER 'john.doe';

-- Roles
SHOW ROLES;                          -- built-in role descriptions

-- Folder permissions
GRANT READ    ON FOLDER '/Finance'          TO GROUP 'Finance';
GRANT EXECUTE ON FOLDER '/Finance/Monthly'  TO GROUP 'FinanceAnalysts';
GRANT MANAGE  ON FOLDER '/Finance'          TO GROUP 'FinanceAdmins';
REVOKE READ   ON FOLDER '/Finance'          FROM GROUP 'Finance';

SHOW PERMISSIONS ON FOLDER '/Finance';
SHOW PERMISSIONS FOR GROUP 'Finance';

-- Subscriptions
CREATE SUBSCRIPTION FOR REPORT '/Finance/MonthlySales'
  DELIVER TO 'john.doe'
  SCHEDULE '0 8 * * MON'            -- cron: every Monday at 08:00
  FORMAT PDF
  VIA SMTP CONNECTOR 'corporate-smtp';

CREATE SUBSCRIPTION FOR REPORT '/Finance/MonthlySales'
  DELIVER TO GROUP 'Finance'
  ON REFRESH                         -- deliver whenever dataset refreshes
  FORMAT BOTH
  VIA SMTP CONNECTOR 'corporate-smtp';

SHOW SUBSCRIPTIONS;
SHOW SUBSCRIPTIONS FOR REPORT '/Finance/MonthlySales';
ALTER SUBSCRIPTION 5 DISABLE;
DROP SUBSCRIPTION 5;
```

Passwords in scripts are hashed on execution. The plaintext value exists only in
transit and in the script file itself — never in the portal database or any log.
Script files containing `CREATE USER` / `ALTER USER SET PASSWORD` should be treated
as secrets (not committed to source control, or committed with placeholder values and
a separate secrets file).

### 1.4 Folder & Report Catalog API
- `GET /api/folders` — tree visible to the calling user (ACL-filtered)
- `GET /api/folders/{id}/reports` — reports the user can Read
- `POST /api/folders` — create folder (Publisher+ in parent)
- `POST /api/reports` — register a script path into a folder (Publisher+)
- `PUT /api/reports/{id}` — update metadata (Manage)
- `DELETE /api/reports/{id}` — soft-delete (Manage)

### 1.5 Admin REST API
- CRUD for users, roles, groups, group membership (mirrors the script language above)
- CRUD for folder ACL entries
- `GET /api/admin/audit` — paginated audit log

### 1.6 Deliverable
REST API with Swagger UI. Script language parser additions for the portal admin
statements. No frontend yet.

---

## Phase 2 — Report Execution & Snapshot Service

**Goal:** Authorized users can run reports. Snapshots are cached and automatically
invalidated on dataset refresh or script change.

### 2.1 Report Execution
- `POST /api/reports/{id}/execute` — runs the `.rptsql` script via `DashboardService`,
  returns `ReportManifest` JSON. Requires `Execute` permission.
- Parameters passed in request body; validated against declared page parameters.
- Async execution — returns `jobId`; client polls `GET /api/jobs/{jobId}`.
- Result snapshot written to `ReportSnapshots` on completion.

### 2.2 Snapshot Cache
- `GET /api/reports/{id}/snapshot` — returns the most recent `ManifestJson` without
  re-execution. Requires `Read` permission.
- The snapshot is the serialized `ReportManifest`. Its content only changes when the
  Orchestrator completes a dataset refresh (new query results) or the script file is
  modified (new report structure). Slicer/parameter interactions do not touch the
  snapshot — they are applied live against the per-user `DashboardService` session.
- If the snapshot is absent or stale (script file changed since last build), the viewer
  shows a "stale" banner. Users with `Execute` can trigger a refresh; `Read`-only users
  see the last known good snapshot.

### 2.3 Orchestrator Integration
- `DatasetJob` rows link a report to an Orchestrator job ID.
- A portal `BackgroundService` polls the Orchestrator's `ExecutionHistory` SQLite table
  (shared file, same server) every 60 seconds for job completions.
- On completion, the portal invalidates the snapshot and queues a background
  re-execution. The UI shows a "Refreshing…" badge until the new snapshot is ready.

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
| `/reports/{id}` | Report viewer — renders `ReportManifest` via existing `report-runtime.js` |
| `/reports/{id}/export` | Export modal (CSV / PDF download) |
| `/admin/users` | User/group management — mirrors the script language (Admin only) |
| `/admin/folders` | Folder + ACL management |
| `/admin/subscriptions` | Global subscription overview |
| `/profile/subscriptions` | User's own subscription list and preferences |

### 3.3 Report Viewer Reuse
`report-runtime.js` and the ECharts bundle from `ReportPlayer/wwwroot` are copied
verbatim into the portal `wwwroot`. The viewer consumes `ReportManifest` JSON — it
does not care whether the manifest came from a live eval or a cached snapshot.

### 3.4 Parameter Interaction
Slicer and parameter changes POST to the backend, which calls `SetParameterAsync` on
the user's `DashboardService` session and returns an updated manifest. The snapshot
row is not touched — parameter state lives in the session only.

---

## Phase 4 — Export (CSV & PDF)

### 4.1 CSV Export
- `GET /api/reports/{id}/export/csv?visual=SalesTable`
- Reads from the cached snapshot manifest rows. No re-execution.
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
Stored in the `Subscriptions` table (see P0-B schema). Key fields:
- `Schedule` — cron expression (`0 8 * * MON`) or null if `DeliverOnRefresh = true`
- `Format` — `PDF`, `CSV`, or `BOTH`
- `SmtpConnector` — name of a registered SMTP connection in the portal config
- `Recipients` — comma-separated addresses or group name

### 5.2 Email Delivery via ENGINE Script
The portal does **not** implement a separate email library. When a delivery fires, the
portal generates and executes the following ETL-SQL script through the engine:

```sql
-- Generated by SubscriptionDispatcher at delivery time
CREATE CONNECTION _smtp ON SMTP(
  HOST    = 'mail.company.com',
  PORT    = 587,
  USER    = 'reports@company.com',
  PASSWORD = '<resolved from portal config>'
);

EMAIL SEND
  TO      'john.doe@company.com'
  SUBJECT 'Monthly Sales Report — 2026-04-19'
  BODY    '<p>Please find the attached report.</p>'
  ATTACH  'C:\portal\temp\snapshot_42.pdf'
  VIA     _smtp;
```

`EmailStatementHandler` in the engine handles delivery. The portal is responsible only
for building the script, resolving the SMTP credentials from its config, rendering the
attachment, and cleaning up temp files. Delivery failures are logged to `AuditLog`;
retried on next tick up to 3 times before the subscription is disabled.

### 5.3 Scheduler
- `SubscriptionDispatcher` — a `BackgroundService` that wakes every minute.
- Queries `Subscriptions` where `NextRunAt <= now AND IsActive = true`.
- For each due subscription: fetches or builds the snapshot, renders the export,
  executes the email script, updates `LastSentAt`/`NextRunAt`.
- Cron parsing: `Cronos` NuGet package (lightweight, no Quartz needed at this scale).

### 5.4 Orchestrator-Triggered Delivery
Subscriptions with `DeliverOnRefresh = true` fire immediately after the portal detects
a dataset refresh completion (see Phase 2.3), regardless of the cron schedule. This
covers the "send the report whenever the nightly ETL finishes" pattern.

---

## Phase 6 — Admin Hardening & Operations

### 6.1 Configuration

```json
{
  "Portal": {
    "DatabasePath": "./portal.db",
    "ScriptRootPath": "./Reports",
    "SnapshotTtlMinutes": 60,
    "MaxConcurrentExecutions": 4,
    "Smtp": {
      "Connectors": {
        "corporate-smtp": {
          "Host": "", "Port": 587,
          "Username": "", "EncryptedPassword": "",
          "FromAddress": ""
        }
      }
    },
    "Jwt": { "Secret": "", "ExpiryMinutes": 60, "RefreshExpiryDays": 7 },
    "FirstRun": { "AdminUsername": "admin" }
  }
}
```

SMTP passwords are stored encrypted via .NET Data Protection API — never plaintext.
The portal refuses to start if `Jwt:Secret` is empty or fewer than 32 characters.

### 6.2 Security Rules
- Passwords: bcrypt/PBKDF2 via ASP.NET Core Identity. Never logged, never returned
  in API responses, never stored in plaintext anywhere including configuration (SMTP
  uses Data Protection encryption).
- Path traversal: all script paths resolved through `IExecutionContext.ResolvePath()`
  and must remain within `ScriptRootPath`.
- Login: 5 failed attempts per IP per 15 minutes triggers a 15-minute lockout.
- JWT secret: enforced minimum 32 characters; portal refuses to start without it.
- HTTPS: enforced in production (`UseHttpsRedirection` + HSTS); HTTP allowed in
  development only.
- `MustChangePassword`: set on first-run admin and on any admin-reset password; blocks
  all portal actions until changed.
- Audit log: all auth events, report executions, exports, and admin actions are
  recorded. Passwords and parameter values that may contain PII are redacted.

### 6.3 Health Endpoint
`GET /health` returns:
- Portal DB connectivity
- Orchestrator SQLite reachability
- Pending subscription count
- Active execution count
- SMTP connector reachability (optional ping)

### 6.4 Audit Log UI
Admin page: who ran which report, when, with what parameters; export history;
subscription delivery outcomes; failed logins; admin actions. Exportable as CSV.

---

## Development Order

```
P0-A  Permission model (done — this document)
P0-B  EF Core schema + migrations           (2 days)
  │
  ▼
Phase 1  Identity + Catalog + Admin Scripts  (1.5 weeks)
  │      (parser additions for portal admin language)
  ▼
Phase 2  Execution + Snapshots               (1 week)
  │
  ├── Phase 3  Web Frontend                  (2 weeks, parallel with Phase 4)
  │
  └── Phase 4  CSV + PDF Export              (0.5 week — mostly already built)
        │
        ▼
      Phase 5  Email Subscriptions           (1 week)
        │
        ▼
      Phase 6  Admin Hardening               (1 week)
```

**Critical path:** P0-B → Phase 1 → Phase 2 → Phase 5. Frontend and export run in
parallel. Phase 4 is short because `PdfExporter` and CSV generation already exist.

---

## What Can Be Reused Today

| Asset | Location | Reuse |
| :--- | :--- | :--- |
| `DashboardService` | `ReportPlayer` | Per-user instance per report, LRU-pooled |
| `ManifestBuilder` | `ReportBuilder` | Produces the manifest JSON stored in snapshots |
| `PdfExporter` + `SvgChartRenderer` | `ReportBuilder` | Direct call; no changes needed |
| `report-runtime.js` + ECharts | `ReportPlayer/wwwroot` | Copy verbatim into portal wwwroot |
| `EmailStatementHandler` | `Engine` | Portal generates + executes the EMAIL SEND script |
| `IExecutionContext.ResolvePath` | `Engine` | Script path boundary enforcement |
| Orchestrator `ExecutionHistory` | `Orchestrator` | Polled for dataset refresh completions |
| `ReportManifest` JSON schema | `ReportBuilder` | Snapshot column is the serialized manifest |


## Questions to answer
- How to manage the buffer, currently Orchestrator owns the Buffer Manager and will resource manage for all jobs including DATASETS, this web service will manage number of users running reports how to share resource responsibilities between the two?  I feel Orchestrator should still own Buffer Management and the web service should report in on its current resource needs.  A user logs in allocate x automatically so Orchestrator can dial down the number of jobs is run concurrently.  Need some brainstorming on this.