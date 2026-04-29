# ETL-SQL Report Portal: Administrator's Guide

This guide covers everything an administrator needs to deploy, configure, and operate the Report Portal — from first-run setup through day-to-day user and subscription management.

---

## Contents

1. [Deployment](#1-deployment)
2. [Configuration Reference](#2-configuration-reference)
3. [First-Run Setup](#3-first-run-setup)
4. [User Management](#4-user-management)
5. [Groups & Folder Permissions](#5-groups--folder-permissions)
6. [Publishing Reports](#6-publishing-reports)
7. [SMTP Connections](#7-smtp-connections)
8. [Subscriptions](#8-subscriptions)
   - [8.4 Scripted Subscription Management *(Proposed)*](#84-scripted-subscription-management)
9. [Health Monitoring](#9-health-monitoring)
10. [Audit Log](#10-audit-log)
11. [Security Model](#11-security-model)
12. [Quick Start: Required Steps](#12-quick-start-required-steps)

---

## 12. Quick Start: Required Steps

To get the Report Portal running in under 5 minutes:

1. **Standardize Naming**: Ensure you are using the `ETL-SQL-Portal` executable.
2. **Set JWT Secret**: Open `appsettings.json` or set an environment variable `Portal__Jwt__Secret` to a 32-character random string.
3. **Configure Paths**: Verify `ScriptRootPath` points to your `.rptsql` files (defaults to `./Reports`).
4. **Launch**: Run `./ETL-SQL-Portal`.
5. **Admin Login**:
   - URL: `http://localhost:5001`
   - User: `admin`
   - Temp Password: `Admin@12345!`
6. **Secure Account**: Change the admin password immediately upon first login.
7. **Publish**: Go to **Admin -> Folders**, click **Publish Report**, and point to a `.rptsql` file.

---

## 1. Deployment

The Report Portal is an ASP.NET Core 10 web application (`ETL-SQL-Portal`). It uses a local **SQLite** database and serves both the REST API and the static web UI from the same process.

### 1.1 Prerequisites

- .NET 10 Runtime
- Write access to the directories configured for the database, report scripts, and snapshots
- Network access to any data sources the report scripts query
- (Optional) ETL-SQL Orchestrator Service running on the same host or accessible via its SQLite path, for background dataset refresh

### 1.2 Windows (NSSM)

```powershell
nssm install ETL-SQL-Portal "dotnet" "ETL-SQL-Portal.dll"
nssm set ETL-SQL-Portal AppDirectory "C:\ETL-SQL\Portal"
nssm set ETL-SQL-Portal AppStdout    "C:\Logs\portal.log"
nssm set ETL-SQL-Portal AppStderr    "C:\Logs\portal-error.log"
nssm set ETL-SQL-Portal AppEnvironmentExtra "ASPNETCORE_ENVIRONMENT=Production"
nssm start ETL-SQL-Portal
```

### 1.3 Linux (systemd)

```ini
[Unit]
Description=ETL-SQL Report Portal
After=network.target

[Service]
ExecStart=/opt/etlsql/portal/ETL-SQL-Portal
WorkingDirectory=/opt/etlsql/portal
Environment=ASPNETCORE_ENVIRONMENT=Production
Restart=always
User=etlportal

[Install]
WantedBy=multi-user.target
```

### 1.4 Reverse Proxy (Recommended for Production)

The portal listens on HTTP by default. Put it behind **nginx** or **IIS ARR** and terminate TLS at the proxy. Set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` so the app sees the correct client IP in audit logs.

---

## 2. Configuration Reference

All settings live under the `"Portal"` key in `appsettings.json`. Every key can be overridden with an environment variable using the double-underscore separator: `Portal__Jwt__Secret`.

```json
{
  "Portal": {
    "DatabasePath":    "./portal.db",
    "ScriptRootPath":  "./Reports",
    "SnapshotDirectory": "./Snapshots",
    "Resources": {
      "MaxConcurrentReportExecutions": 4,
      "ExecutionTimeoutSeconds":       300,
      "SessionCacheMaxSize":           50,
      "SessionCacheTtlMinutes":        30
    },
    "Jwt": {
      "Secret":            "",
      "ExpiryMinutes":     60,
      "RefreshExpiryDays": 7
    },
    "FirstRun": {
      "AdminUsername": "admin"
    }
  }
}
```

| Key | Default | Description |
| :--- | :--- | :--- |
| `DatabasePath` | `./portal.db` | Path to the SQLite database file. Relative to the app working directory. |
| `ScriptRootPath` | `./Reports` | Root directory for `.rptsql` script files. All script paths are validated to stay within this directory. |
| `SnapshotDirectory` | `./Snapshots` | Where report snapshot files are stored. |
| `Resources.MaxConcurrentReportExecutions` | `4` | How many report execution jobs can run simultaneously. |
| `Resources.ExecutionTimeoutSeconds` | `300` | Per-execution timeout. Jobs exceeding this are cancelled. |
| `Resources.SessionCacheMaxSize` | `50` | Maximum number of in-memory execution sessions cached for result streaming. |
| `Resources.SessionCacheTtlMinutes` | `30` | How long an idle session is kept before eviction. |
| `Jwt.Secret` | *(required)* | HMAC-SHA256 signing secret. **Must be at least 32 characters.** The portal will refuse to start without it. |
| `Jwt.ExpiryMinutes` | `60` | How long an access token is valid. |
| `Jwt.RefreshExpiryDays` | `7` | How long a refresh token is valid. |
| `FirstRun.AdminUsername` | `admin` | Username created on first start if no users exist yet. |

> [!IMPORTANT]
> **`Jwt.Secret` must be set before production use.** Generate a strong random string of at least 32 characters and set it via an environment variable rather than storing it in the checked-in `appsettings.json`:
> ```
> Portal__Jwt__Secret=<your-secret-here>
> ```

---

## 3. First-Run Setup

On first start the portal:

1. Runs any pending EF Core migrations, creating the SQLite schema.
2. Creates the three default roles: `Admin`, `Publisher`, `Viewer`.
3. If no `admin` user exists, creates one with username `admin` and temporary password `Admin@12345!` with `MustChangePassword = true`.

**You must log in as `admin` immediately and change the password.** The portal enforces this — no API calls will succeed until the password has been changed.

---

## 4. User Management

Open **Admin → Users** to manage accounts.

### 4.1 Roles

| Role | What they can do |
| :--- | :--- |
| **Admin** | Everything — full user/group/folder management, SMTP configuration, audit log |
| **Publisher** | Create folders, publish reports, manage subscriptions |
| **Viewer** | Browse accessible folders, run and export reports, manage their own subscriptions |

### 4.2 Creating a User

Click **New User** and fill in:

- **Username** — unique login name
- **Email** — used for subscription delivery
- **Password** — must be at least 8 characters with at least one digit
- **Role** — Admin, Publisher, or Viewer

New users created by an administrator always have `MustChangePassword = true`. They will be prompted to set their own password on first login.

### 4.3 Editing a User

Click a user row to open their profile. You can change their name, email, role, and active status. **Deactivating a user** (`IsActive = false`) prevents login and blocks all API calls using their tokens.

### 4.4 Resetting a Password

Use **Reset Password** on the user's profile to force a new temporary password and set `MustChangePassword = true`. The user will be prompted to change it on their next login.

### 4.5 Revoking Sessions

**Revoke Tokens** immediately invalidates all refresh tokens for that user, ending all active sessions. Use this if an account is believed to be compromised.

---

## 5. Groups & Folder Permissions

Folder visibility is controlled through **groups** and **ACLs** (access control lists).

### 5.1 Groups

A group is a named collection of users. Open **Admin → Groups** to create groups and add members.

### 5.2 Folder ACLs

Each folder can have one or more ACL entries, each granting a group a permission level:

| Permission | What it allows |
| :--- | :--- |
| `Read` | See the folder and its reports; view snapshots |
| `Execute` | Run reports and build new snapshots |
| `Manage` | Publish, update, and delete reports within the folder |

ACLs are not inherited — a group must be explicitly granted access to each folder it needs to see. A folder with no ACLs is visible only to Admins.

> [!TIP]
> Create an **Everyone** group, add all users to it, and grant it `Read` on public folders rather than individually managing each user.

---

## 6. Publishing Reports

Publishing registers a `.rptsql` script file as a named report in a folder.

1. Upload or copy the `.rptsql` file into the portal's `ScriptRootPath` directory.
2. Open **Admin → Folders**, select the destination folder.
3. Click **Publish Report** and fill in:
   - **Name** — the display name shown in the portal
   - **Description** — optional summary
   - **Script path** — path to the `.rptsql` file, relative to `ScriptRootPath`

The portal validates that the path stays within `ScriptRootPath` (path traversal attacks are blocked).

### 6.1 Updating a Report

Edit the `.rptsql` file on disk. The portal detects the modification timestamp and marks the report as **stale** until a new snapshot is built. The snapshot is not rebuilt automatically — a user with Execute permission (or an Orchestrator dataset job) must trigger a refresh.

### 6.2 Deleting a Report

Soft-delete via the report's **Delete** button. The record is marked `IsDeleted = true` and hidden from users; snapshots are retained on disk. Hard deletion requires removing the database record and snapshot files manually.

---

## 7. SMTP Connections

SMTP connections are named credentials used by subscriptions to send email. Open **Admin → SMTP**.

### 7.1 Creating a Connection

| Field | Description |
| :--- | :--- |
| **Alias** | Unique name referenced by subscriptions (e.g. `corporate-smtp`) |
| **Host** | SMTP server hostname |
| **Port** | Typically `587` (STARTTLS) or `465` (SSL) |
| **Username** | Login for the SMTP server |
| **Password** | Stored encrypted via .NET Data Protection API — never stored in plaintext |
| **From Address** | The `From:` address on outgoing emails |
| **Use SSL** | Whether to use SSL/TLS |

### 7.2 Security Note

SMTP passwords are encrypted at rest using the .NET Data Protection API with the machine key. Moving the portal to a new host requires re-entering SMTP passwords because the encrypted values cannot be decrypted on a different machine without transferring the Data Protection key ring.

---

## 8. Subscriptions

Subscriptions are owned by individual users but visible and manageable by Admins in **Admin → Subscriptions**.

### 8.1 Subscription Formats

| Format | What is delivered |
| :--- | :--- |
| `Link` | Email containing a URL linking to the live report in the portal. Fastest; requires no attachment export. SMTP still needed for delivery. |
| `PDF` | Full rendered snapshot of all visuals as a PDF attachment |
| `CSV` | Raw data table as a CSV attachment |
| `Markdown` | Report content as a Markdown text attachment |

### 8.2 Schedules

| Schedule | Behaviour |
| :--- | :--- |
| `Daily` | Runs once per day at the configured `AtTime` |
| `Weekly` | Runs once per week at `AtTime` |
| `Monthly` | Runs on the first day of each month at `AtTime` |

Subscription jobs are handed to the **ETL-SQL Orchestrator** for scheduling. If the Orchestrator is not reachable, subscriptions are created in the database but jobs will not fire until the Orchestrator comes online.

### 8.3 Delivery Failures

Each subscription tracks a `FailCount`. After repeated failures the Orchestrator will stop retrying. Investigate via **Admin → Subscriptions → History** and correct the SMTP configuration or report script before re-enabling.

### 8.4 Scripted Subscription Management

> [!NOTE]
> **Proposed** — This section describes functionality planned for a future release.

Administrators can create and modify subscriptions using ETL-SQL script syntax. This is useful for bulk setup, deployment automation, or version-controlling subscription configuration alongside report scripts.

#### CREATE SUBSCRIPTION

```sql
CREATE SUBSCRIPTION <name>
FOR REPORT '<script-path>'
DELIVER TO '<email>' | GROUP '<group-name>'
SCHEDULE '<cron-expression>'
FORMAT PDF | CSV | BOTH | LINK
AT <smtp-alias>
[ PARAMETERS (
    @param1 = <value>,
    @param2 = <value>,
    ...
) ];
```

The `<name>` is a human-readable label shown in subscription lists. It is optional — if omitted the subscription is identified by its generated ID.

Parameter values use standard ETL-SQL quoting: strings in single quotes, numbers unquoted, `NULL` for no value.

**Examples:**

```sql
-- Daily sales report: always yesterday's data
CREATE SUBSCRIPTION DailySales
FOR REPORT '/Reports/Sales/Daily'
DELIVER TO 'john@example.com'
SCHEDULE '0 6 * * *'
FORMAT PDF
AT corporate-smtp
PARAMETERS (
    @start  = 'D-1',
    @end    = 'D',
    @region = NULL
);

-- Monthly executive summary delivered to a group
CREATE SUBSCRIPTION MonthlyExec
FOR REPORT '/Reports/Executive/MonthlySummary'
DELIVER TO GROUP 'Executives'
SCHEDULE '0 7 1 * *'
FORMAT PDF
AT corporate-smtp
PARAMETERS (
    @period_start = 'M-1',
    @period_end   = 'ME-1'
);

-- Fixed date range for a one-time review
CREATE SUBSCRIPTION Q1Review
FOR REPORT '/Reports/Finance/Quarterly'
DELIVER TO 'cfo@example.com'
SCHEDULE '0 8 * * 1'
FORMAT PDF
AT corporate-smtp
PARAMETERS (
    @start = '2026-01-01',
    @end   = '2026-03-31'
);
```

#### RELDATE parameter values

When a report uses `RELDATE` INPUT parameters, the subscription stores the expression string — not a resolved date. The engine resolves it fresh each time the subscription fires. See [`Docs/Reference/RelativeDate_Parameters.md`](../Reference/RelativeDate_Parameters.md) for the full expression reference.

Common expressions:

| Expression | Resolves to at run time |
| :--- | :--- |
| `'D'` | Today at midnight |
| `'D-1'` | Yesterday at midnight |
| `'W-1'` | Start of last week |
| `'ME-1'` | Last day of last month |
| `'M-1'` | First day of last month |
| `'QE-1'` | Last day of last quarter |
| `'Y-1'` | January 1 of last year |
| `'YE-1'` | December 31 of last year |
| `'N-2H'` | Exactly 2 hours before the run |

A fixed ISO date string (`'2026-01-01'`) can also be used to pin to a specific date.

#### LIST parameter values

Pass `LIST` parameters as a single quoted, comma-separated string. Wrap values containing commas in double quotes:

```sql
PARAMETERS (
    @regions = 'North,South,East',
    @brands  = '"Acme, Inc",Globex'
);
```

#### ALTER SUBSCRIPTION

Modify an existing subscription without recreating it:

```sql
ALTER SUBSCRIPTION <name-or-id>
[ SET SCHEDULE '<cron-expression>' ]
[ SET FORMAT PDF | CSV | BOTH | LINK ]
[ SET ACTIVE | INACTIVE ]
[ PARAMETERS (
    @param1 = <value>,
    ...
) ];
```

The `PARAMETERS(...)` clause **replaces the full parameter set** for the subscription. To clear all parameters use `PARAMETERS ()` (empty). To leave parameters unchanged, omit the clause.

```sql
-- Change schedule only
ALTER SUBSCRIPTION DailySales
SET SCHEDULE '0 8 * * 1-5';

-- Update parameters only
ALTER SUBSCRIPTION DailySales
PARAMETERS (
    @start  = 'W-1',
    @end    = 'W',
    @region = 'North'
);

-- Pause a subscription
ALTER SUBSCRIPTION MonthlyExec SET INACTIVE;
```

#### DROP SUBSCRIPTION

```sql
DROP SUBSCRIPTION <name-or-id>;
```

---

## 9. Health Monitoring

`GET /health` returns a JSON document with the overall portal status and the state of each subsystem.

```json
{
  "status": "Healthy",
  "checks": {
    "db": {
      "status": "Healthy",
      "description": "Database reachable. 3 users registered."
    },
    "orchestrator": {
      "status": "Degraded",
      "description": "Orchestrator DB not found. Scheduled jobs will not run."
    },
    "execution": {
      "status": "Healthy",
      "description": "0/4 slots in use. 2 SMTP connections. 5 active subscriptions."
    }
  }
}
```

| Check | Healthy | Degraded | Unhealthy |
| :--- | :--- | :--- | :--- |
| `db` | SQLite reachable | — | Cannot connect to database |
| `orchestrator` | Orchestrator DB found | Orchestrator DB not found | — |
| `execution` | Capacity available | Slots nearing cap | — |

The overall `status` is the worst of all individual checks: `Unhealthy` > `Degraded` > `Healthy`.

> [!TIP]
> Wire `GET /health` into your uptime monitor or load balancer health check. A `Degraded` response means the portal is functional but subscriptions may not fire. An `Unhealthy` response means the database is down and no API calls will succeed.

---

## 10. Audit Log

Every significant action is written to the audit log. Open **Admin → Audit Log** to browse or search.

### 10.1 Logged Events

| Action | Trigger |
| :--- | :--- |
| `LOGIN` | Successful login |
| `LOGIN_FAILED` | Failed login attempt |
| `LOGOUT` | Explicit logout |
| `PASSWORD_CHANGED` | User changed their own password |
| `CREATE_USER` | Admin created a new user |
| `UPDATE_USER` | Admin edited a user |
| `DELETE_USER` | Admin deleted a user |
| `CREATE_FOLDER` | Folder created |
| `DELETE_FOLDER` | Folder deleted |
| `PUBLISH_REPORT` | Report published |
| `DELETE_REPORT` | Report soft-deleted |
| `EXECUTE_REPORT` | Report execution started |
| `CREATE_SUBSCRIPTION` | Subscription created |
| `DELETE_SUBSCRIPTION` | Subscription deleted |
| `CREATE_SMTP` | SMTP connection added |
| `DELETE_SMTP` | SMTP connection removed |

### 10.2 Exporting the Audit Log

Click **Export CSV** to download up to 10,000 most-recent entries as a UTF-8 CSV file. You can also filter by action type and user before exporting.

---

## 11. Security Model

### 11.1 Authentication

The portal uses **JWT Bearer tokens** with HMAC-SHA256 signing.

- Access tokens expire after `Jwt.ExpiryMinutes` (default 60 min).
- Refresh tokens expire after `Jwt.RefreshExpiryDays` (default 7 days). Each refresh issues a new refresh token (rolling window).
- Refresh tokens are stored in the database and can be individually revoked via **Revoke Tokens**.

### 11.2 Roles

Three roles are enforced at the controller level via `[Authorize(Roles = "...")]` attributes:

- **Admin** — full access
- **Publisher** — can create folders and publish reports
- **Viewer** — read and execute only

Folder-level **ACLs** provide finer control within those role boundaries.

### 11.3 MustChangePassword Enforcement

When a user has `MustChangePassword = true`, a middleware layer (`MustChangePasswordMiddleware`) intercepts all `POST /api/*` calls except `change-password`, `login`, `logout`, and `refresh`. Blocked requests return `403 Forbidden` with a `redirect` field pointing to the change-password page. This applies to all roles including Admin.

### 11.4 Path Traversal Prevention

All script paths submitted to `POST /api/reports` are resolved to absolute paths and validated to remain within `ScriptRootPath`. A path like `../../etc/passwd` is rejected with `400 Bad Request`.

### 11.5 Account Lockout

After **5 consecutive failed login attempts** an account is locked for **15 minutes** (ASP.NET Identity defaults). Lockout applies to all roles. Admins can unlock accounts by resetting the password or waiting for the lockout window to expire.

### 11.6 HTTPS in Production

When `ASPNETCORE_ENVIRONMENT` is `Production`, the portal enables `UseHttpsRedirection()` and HSTS. **Always run behind a TLS-terminating reverse proxy in production.**
