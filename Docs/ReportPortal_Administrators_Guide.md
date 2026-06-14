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
   - [8.4 Scripted Subscription Management](#84-scripted-subscription-management)
9. [Health Monitoring](#9-health-monitoring)
10. [Audit Log](#10-audit-log)
11. [Security Model](#11-security-model)
12. [Quick Start: Required Steps](#12-quick-start-required-steps)
13. [Orchestrator Management](#13-orchestrator-management)
14. [Production Readiness Checklist](#14-production-readiness-checklist)

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
   - Temp Password: the value of `Portal__FirstRun__AdminPassword` if you configured one; otherwise a
     randomly generated password printed once to the startup log (look for the `Portal.FirstRun` category).
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
      "MaxConcurrentExecutionsPerUser": 2,
      "ExecutionTimeoutSeconds":       300,
      "SessionCacheMaxSize":           50,
      "SessionCacheTtlMinutes":        30,
      "SnapshotRetentionPerReport":    20
    },
    "Jwt": {
      "Secret":            "",
      "ExpiryMinutes":     60,
      "RefreshExpiryDays": 7
    },
    "Identity": {
      "Provider": "Local",
      "Oidc": {
        "Authority": "",
        "ClientId": "",
        "TenantId": "",
        "GroupClaimTypes": [ "groups", "roles" ]
      },
      "Ldap": {
        "Enabled": false,
        "Server": "localhost",
        "Port": 389,
        "UseSsl": false,
        "Domain": "",
        "BaseDn": "",
        "ServiceUser": "",
        "ServicePassword": "",
        "RoleMappings": {}
      }
    },
    "FirstRun": {
      "AdminUsername": "admin"
    },
    "Engine": {
      "StartOfWeek": "Monday"
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
| `Resources.MaxConcurrentExecutionsPerUser` | `2` | Workload fairness: the most of the shared execution slots a single non-administrator may hold at once, so one user flooding the queue cannot starve everyone else. Keep it below `MaxConcurrentReportExecutions`; administrators are exempt. |
| `Resources.ExecutionTimeoutSeconds` | `300` | Per-execution timeout. Jobs exceeding this are cancelled. |
| `Resources.SessionCacheMaxSize` | `50` | Maximum number of in-memory execution sessions cached for result streaming. |
| `Resources.SessionCacheTtlMinutes` | `30` | How long an idle session is kept before eviction. |
| `Resources.SnapshotRetentionPerReport` | `20` | Newest snapshots kept per report. After each successful execution, older snapshot rows and their manifest files are pruned (minimum effective value is 1). |
| `Jwt.Secret` | *(required)* | HMAC-SHA256 signing secret. **Must be at least 32 characters.** The portal will refuse to start without it. |
| `Jwt.ExpiryMinutes` | `60` | How long an access token is valid. |
| `Jwt.RefreshExpiryDays` | `7` | How long a refresh token is valid. |
| `Identity.Provider` | `Local` | Main authentication provider model (`Local` or `Oidc`). If LDAP is enabled, directory logins are supported alongside the selected main provider. |
| `Identity.Oidc.Authority` | *(empty)* | OIDC authority URL, for example `https://login.microsoftonline.com/<tenant-id>/v2.0`. |
| `Identity.Oidc.ClientId` | *(empty)* | OIDC client/application id. |
| `Identity.Oidc.GroupClaimTypes` | `groups`, `roles` | Claims the portal will map into portal groups for folder and dataset ACLs. |
| `Identity.Ldap.Enabled` | `false` | Set to `true` to enable LDAP and Active Directory integration. |
| `Identity.Ldap.Server` | `localhost` | The hostname or IP address of the LDAP/AD server. |
| `Identity.Ldap.Port` | `389` | The server connection port (usually 389 for plain/STARTTLS, 636 for LDAPS/SSL). |
| `Identity.Ldap.UseSsl` | `false` | Set to `true` to establish connections via SSL/TLS (LDAPS). |
| `Identity.Ldap.Domain` | *(empty)* | Default DNS or NetBIOS domain suffix used to qualify logins (e.g. `corp.local`). |
| `Identity.Ldap.BaseDn` | *(empty)* | LDAP directory base search path (e.g. `OU=Users,DC=corp,DC=local`). |
| `Identity.Ldap.ServiceUser` | *(empty)* | Optional service account distinguished name or UPN for searching. |
| `Identity.Ldap.ServicePassword` | *(empty)* | Optional password for the service account. |
| `Identity.Ldap.RoleMappings` | *(empty)* | Key-value pairs mapping Active Directory groups (full DNs or short CNs) to Portal Roles (`Admin`, `Publisher`, `Viewer`). |
| `FirstRun.AdminUsername` | `admin` | Username created on first start if no users exist yet. |
| `Engine.StartOfWeek` | `Monday` | Day used as the start of week when resolving `RELDATE` week-boundary expressions (`W`, `W-1`, etc.). |

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
3. If no `admin` user exists, creates one with username `admin` and `MustChangePassword = true`. The
   temporary password comes from `Portal:FirstRun:AdminPassword` (or the `Portal__FirstRun__AdminPassword`
   environment variable); when unset, a random password is generated and written once to the startup log
   under the `Portal.FirstRun` category — there is no well-known default password.

**You must log in as `admin` immediately and change the password.** The portal enforces this — no API calls will succeed until the password has been changed.

---

## 4. User Management

Open **Admin → Users** to manage accounts.

The user catalog is server-paged. Use the search box and status filter to narrow large account lists, then select rows on the current page to enable or disable multiple users. Selection is page-local and is cleared when the filter or page changes.

### 4.1 Enterprise Identity Path

The portal supports integration with enterprise identity providers via two primary paths: **OpenID Connect (OIDC)** and **LDAP / Active Directory (AD)**.

#### OpenID Connect (OIDC)
Microsoft Entra ID is the reference provider. OIDC users will be provisioned into the portal identity store on first login, and configured OIDC group claims (`groups` or `roles`) will map to portal groups for ACL resolution.

To configure OIDC, update `appsettings.json` with the following configuration:
```json
{
  "Portal": {
    "Identity": {
      "Provider": "Oidc",
      "Oidc": {
        "Authority": "https://login.microsoftonline.com/<tenant-id>/v2.0",
        "ClientId": "<application-client-id>",
        "TenantId": "<tenant-id>",
        "GroupClaimTypes": [ "groups", "roles" ]
      }
    }
  }
}
```

#### LDAP / Active Directory (AD)
LDAP bind authentication enables directory verification for user logins, auto-provisioning of user metadata (email, display name), automatic role assignments based on security groups, and dynamic synchronization of portal group memberships.

To enable and configure LDAP, update `appsettings.json` under `"Identity"`:
```json
{
  "Portal": {
    "Identity": {
      "Provider": "Local",
      "Ldap": {
        "Enabled": true,
        "Server": "domaincontroller.corp.local",
        "Port": 389,
        "UseSsl": false,
        "Domain": "corp.local",
        "BaseDn": "OU=Users,DC=corp,DC=local",
        "ServiceUser": "",
        "ServicePassword": "",
        "RoleMappings": {
          "CN=GG-Portal-Admins,OU=Groups,DC=corp,DC=local": "Admin",
          "GG-Portal-Publishers": "Publisher"
        }
      }
    }
  }
}
```

##### Key LDAP Integration Details:
1. **Login Bind & Username Formats**: When `Ldap.Enabled` is `true`, users can log in using either their simple username, a domain-qualified format (`CORP\username`), or a User Principal Name (UPN) format (`username@corp.local`). If the user does not exist in the database, the portal authenticates them against the directory, maps roles/groups, and auto-provisions a `PortalUser` record with their directory metadata (`displayName`, `mail`, `givenName`, `sn`).
2. **Local User Fallback**: Local portal accounts (users configured with `Provider == "Local"`, such as the default `admin` account) bypass LDAP authentication entirely and authenticate against local hashes. This ensures that administrators can always log in using a local emergency account even if active directory is down or unreachable.
3. **Password Changes**: Password change requests via the `/api/auth/change-password` endpoint are strictly blocked for accounts authenticated via LDAP. All password policy enforcement and resets must be handled on the directory level.
4. **Role Mappings**: Active Directory group memberships (retrieved via the standard `memberOf` user attribute) are matched against the configured `RoleMappings`. Users will automatically be assigned portal roles (`Admin`, `Publisher`, `Viewer`) corresponding to their active directory security groups.
5. **Group Synchronization**: Portal groups created with `Provider = "LDAP"` automatically synchronize their member lists against Active Directory security groups during login:
   - The user is added to matching LDAP portal groups they belong to in AD.
   - The user is removed from any LDAP portal groups they no longer belong to in AD.
   - **Safety Boundary**: Local portal groups (`Provider == "Local"`) are completely ignored during this synchronization, allowing manual group assignments to be preserved.
6. **Removed Directory Users**: Removing or disabling a user in the directory prevents their next LDAP login, but the Portal does not poll the directory for account lifecycle changes. Disable the corresponding Portal account in **Admin → Users** as part of the offboarding workflow. Disabling the Portal account revokes refresh tokens and causes already-issued access tokens to be rejected on their next request.
7. **Recovery Administration**: Keep at least one tested local Admin account. Local accounts bypass LDAP authentication, allowing an operator to disable stale LDAP accounts or correct mappings when the directory is unavailable.

##### Scripted LDAP Administration:
Administrators can script-manage LDAP users and groups inside `EXECUTE portal BEGIN...END` blocks:
```sql
-- Creating an AD / LDAP user (password is optional/ignored)
CREATE USER 'john' WITH (
  EMAIL    = 'john@corp.local',
  ROLE     = 'Publisher',
  PROVIDER = 'LDAP'
);

-- Creating a group mapped to a specific Active Directory security group
CREATE GROUP 'Finance Viewers' WITH (
  DESCRIPTION = 'Portal representation of AD Readers group',
  PROVIDER    = 'LDAP',
  AD_GROUP    = 'CN=GG-Finance-Readers,OU=Groups,DC=corp,DC=local'
);

-- Match by Name (Default when PROVIDER = 'LDAP' and AD_GROUP is omitted)
CREATE GROUP 'GG-Finance-Readers' WITH (
  DESCRIPTION = 'Finance Report Viewers AD Group',
  PROVIDER    = 'LDAP'
);
```

### 4.2 Roles

| Role | What they can do |
| :--- | :--- |
| **Admin** | Everything — full user/group/folder management, SMTP configuration, audit log, Orchestrator management |
| **Publisher** | Create folders, publish reports, manage subscriptions |
| **Viewer** | Browse accessible folders, run and export reports, manage their own subscriptions |
| **OrchestratorManager** | Orchestrator tab only — create/edit/delete/trigger/kill scheduled jobs, view execution history. Cannot access the Admin panel. |

<a name="orchestrator-manager-role"></a>
Assign `OrchestratorManager` to operations staff who need to manage the ETL-SQL Orchestrator from the web UI without needing full admin rights. A user with only this role can see and use the Orchestrator tab but has no access to user management, groups, folders, audit logs, or report publishing.

### 4.3 Creating a User

Click **New User** and fill in:

- **Username** — unique login name
- **Email** — used for subscription delivery
- **Password** — must be at least 8 characters with at least one digit
- **Role** — Admin, Publisher, or Viewer

New users created by an administrator always have `MustChangePassword = true`. They will be prompted to set their own password on first login.

### 4.4 Editing a User

Click a user row to open their profile. You can change their name, email, role, and active status. **Deactivating a user** (`IsActive = false`) prevents login and blocks all API calls using their tokens.

### 4.5 Resetting a Password

Use **Reset Password** on the user's profile to force a new temporary password and set `MustChangePassword = true`. The user will be prompted to change it on their next login.

### 4.6 Revoking Sessions

**Revoke Tokens** immediately invalidates all refresh tokens for that user, ending all active sessions. Use this if an account is believed to be compromised.

### 4.7 Deleting a User — Ownership Lifecycle

Deleting a user distinguishes durable shared resources from personal artifacts:

- **Durable resources must be reassigned first.** If the user owns folders, published reports, or
  datasets, the delete returns `409 Conflict` with a count of each. Retry with
  `DELETE /api/admin/users/{id}?reassignTo=<userId>` naming a different, active user; ownership of
  all three transfers in one operation and a `TRANSFER_OWNERSHIP` audit event records the counts
  and the target.
- **Personal artifacts die with the user.** Subscriptions (including their Orchestrator jobs and
  generated trigger scripts, which are removed immediately), alerts, saved views, favorites,
  share links, embed tokens, and refresh tokens are deleted — they are personal capabilities, not
  shared state. Active subscriptions still require the explicit `?cascade=true` acknowledgement.

### 4.8 LDAP Account Lifecycle Boundary

LDAP synchronization happens **at login only** — there is no background directory sweep:

- A user removed from the directory simply can no longer authenticate (the LDAP bind fails). Their
  portal account, ownerships, and grants remain until an administrator deactivates or deletes the
  account using the lifecycle above. Synchronization never deactivates or deletes accounts.
- On each successful LDAP login the user's memberships in `Provider = 'LDAP'` groups converge to
  the directory's group list (additions and removals), and mapped roles are applied. Local groups
  are never touched by synchronization.
- An LDAP-mapped group removed from the directory keeps its portal row and ACLs; it loses members
  one at a time as they log in. Delete the group in the portal when it is no longer wanted.

---

## 5. Groups & Folder Permissions

Folder visibility is controlled through **groups** and **ACLs** (access control lists).

### 5.1 Groups

A group is a named collection of users. Open **Admin → Groups** to create groups and add members.

Use the group search box to locate groups by name, description, or directory mapping. The member panel is also server-paged: search active users when adding members, select multiple matches to add them together, or select current members to remove them together. **Delete Selected** rejects groups that still have members or ACL entries; remove those references first or use the administrative API with an explicit cascade decision.

### 5.2 Folder ACLs

Each folder can have one or more ACL entries, each granting a group a permission level:

| Permission | What it allows |
| :--- | :--- |
| `Read` | See the folder and its reports; view snapshots |
| `Execute` | Run reports and build new snapshots |
| `Manage` | Publish, update, and delete reports within the folder |

ACLs are not inherited — a group must be explicitly granted access to each folder it needs to see. A folder with no ACLs is visible only to Admins **and its owner**: the user who created a folder (or received it through ownership transfer) always holds effective `Manage` on it, without an ACL entry. Ownership moves only through the explicit transfer on user deletion (§4.7); revoking a group ACL never locks an owner out of their own folder.

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

Report catalog metadata can come directly from the script header. The portal recognizes these canonical tags:

| Tag | Portal field |
| :--- | :--- |
| `@owner` | Report owner/team |
| `@contact` | Support contact |
| `@tags` | Search/category tags, comma-separated |
| `@category` | Primary catalog category |
| `@domain` | Business/data domain |
| `@steward` | Data/report steward |
| `@certification` or `@trusted` | Trust/certification marker |
| `@description` or `@d` | Report description when no publish description is supplied |

```sql
/* @owner: Finance BI
   @contact: finance-bi@example.com
   @tags: revenue,monthly,kpi
   @category: Finance
   @certification: trusted */
SET REPORT TITLE = 'Monthly Sales';
```

Publish request fields override script tags when both are supplied. On republish with a new script path, the portal refreshes the stored metadata from the new script while preserving explicit request values.

### 6.1 Script hash pinning

When a report is published, the portal computes a SHA-256 hash of the `.rptsql` file and stores it as `PublishedScriptHash` in the database. This hash is the "known-good" fingerprint for that version of the report.

At every execution (snapshot build), the portal computes a fresh hash of the file and records it as `ScriptHashAtRunTime` in the `ReportSnapshots` table, along with a `HashMatched` flag. If the file has changed since publishing, `HashMatched = false` and the portal logs a warning. The `GET /api/reports/{id}` response includes a `scriptChanged` field that is `true` when the current file hash differs from the published hash.

> **Note:** the hash is advisory — execution is not blocked by a mismatch in the Report Portal (unlike the Orchestrator's `BLOCK` policy). Use `scriptChanged = true` as a signal to re-publish the report after intentional changes or to investigate unexpected modifications.

Use `SHOW REPORT HISTORY 'Report Name'` or `GET /api/reports/{id}/history` to inspect the lifecycle metadata behind the History button in the viewer. The response includes the pinned publish hash, the current script hash when the script is still available under `ScriptRootPath`, a `scriptChanged` flag, snapshot build rows with runtime hashes, and report audit entries such as publish, update, favorite, and delete activity.

```sql
SHOW REPORT HISTORY 'Monthly Sales' INTO #report_history;
```

### 6.2 Updating a Report

Edit the `.rptsql` file on disk. The portal detects the modification timestamp and marks the report as **stale** until a new snapshot is built. The snapshot is not rebuilt automatically — a user with Execute permission (or an Orchestrator dataset job) must trigger a refresh. If you intentionally changed the script, re-publish the report (via `PUT /api/reports/{id}` or by deleting and re-publishing) to reset the pinned hash.

Before publishing or replacing a report script, the portal validates that the file exists under `ScriptRootPath`, has a `.rptsql` extension, and parses successfully. Use `VALIDATE REPORT SCRIPT 'sales/daily.rptsql'` or `POST /api/reports/validate` with `{ "scriptPath": "sales/daily.rptsql" }` to run the same validation used by `POST /api/reports` and `PUT /api/reports/{id}`. The response includes the script hash, last modified time, script metadata tags, input parameters, and parse errors when validation fails. The Admin publish form runs this validation before saving.

```sql
VALIDATE REPORT SCRIPT 'sales/daily.rptsql' INTO #validation;
```

### 6.3 Deleting a Report

Soft-delete via the report's **Delete** button. The record is marked `IsDeleted = true` and hidden from users; snapshots are retained on disk. Hard deletion requires removing the database record and snapshot files manually.

### 6.4 Dataset Permissions

Cross-report shared datasets allow reports to consume cached, shared data with automated background refreshes. Dataset permissions are independent of folder ACLs.

| Dataset state | Who can see or use it |
| :--- | :--- |
| `Public` | Authenticated callers with `Read` or higher on the linked folder; legacy datasets without a folder allow any authenticated caller. |
| `Private` with owning report | Admins and the user who published the owning report. |
| `Private` with dataset ACL | Admins and members of groups granted `Viewer`, `Refresh`, `Editor`, or `Owner` on that dataset. |
| `Private` with no owner or ACL | Admins only. |

Dataset permissions are independent of folder ACLs. Folder permissions control report browsing and execution; dataset ACLs control cross-report dataset reuse. A user who can run a report does not automatically gain access to every private dataset in the portal.

Dataset permissions are hierarchical: `Viewer < Refresh < Editor < Owner`. `Refresh` can read and
trigger materialization but cannot alter dataset metadata or source definitions. Interactive report
execution and user-triggered refresh retain the real user's dataset identity. The orchestrator poller is
the only non-user execution path that explicitly runs a scheduled dataset refresh with administrator
dataset rights.

Dataset ownership and folder mutation use these rules:

| Operation | Owner recorded | Required folder permission |
| :--- | :--- | :--- |
| `CREATE DATASET` in a report | The owning report; the report publisher has owner rights | Report execution permission; updates still require dataset Editor/Owner rights |
| Interactive `PUBLISH DATASET` | The calling user, including an administrator | `Manage` on the destination folder; administrators satisfy this automatically |
| Userless trusted system `PUBLISH DATASET` | The destination folder owner | Trusted scheduled execution only |
| Scheduled refresh | Ownership is unchanged | Trusted poller execution |
| Move dataset | Ownership is unchanged | `Manage` on both source and destination folders; administrators satisfy this automatically |

Publish and move audits record the initiating user when one exists. Userless scheduled activity is
recorded without a fabricated user identity. Failed publish audits contain the target and a sanitized
reason, never transport credentials.

All dataset file paths are also constrained to `Portal:DatasetRootPath`. ACLs cannot grant access to a dataset record whose backing file is outside that configured root.

Dataset registry administration is scriptable with the same catalog name and folder values shown in the portal UI:

```sql
REFRESH DATASET 'Sales Summary' IN FOLDER '/Finance';

ALTER DATASET 'Sales Summary' IN FOLDER '/Finance'
    SET ACCESS = PUBLIC, TTL = '2h';

GRANT VIEWER ON DATASET 'Sales Summary' IN FOLDER '/Finance' TO GROUP 'Finance';
GRANT REFRESH ON DATASET 'Sales Summary' IN FOLDER '/Finance' TO GROUP 'DataOperations';
GRANT EDITOR ON DATASET 'Sales Summary' IN FOLDER '/Finance' TO GROUP 'FinanceAnalysts';
GRANT OWNER  ON DATASET 'Sales Summary' IN FOLDER '/Finance' TO GROUP 'FinanceAdmins';
REVOKE EDITOR ON DATASET 'Sales Summary' IN FOLDER '/Finance' FROM GROUP 'FinanceAnalysts';

DROP DATASET 'Sales Summary' IN FOLDER '/Finance';
```

Use `&dataset` only for report-owned dataset definitions inside `.rptsql` files. Portal registry commands use string-literal catalog names plus `IN FOLDER` so they cannot be confused with engine `#temp` tables or report dataset declarations.

### 6.5 Dataset At-Rest Key Lifecycle

Production portals require `Portal:Dataset:AtRestKey`, a base64 value decoding to at least 32 bytes.
Generate it with a cryptographically secure random generator, store it in the portal's secret manager,
and set a non-secret `Portal:Dataset:AtRestKeyVersion` such as `2026-01`. Back up the key, its version,
`portal.db`, and the dataset directory together. Restoring only the database/files without the matching
key makes the caches unreadable.

`Portal:Dataset:AllowMachineFallback=true` is supported only for deliberate development/standalone use.
It creates host-bound caches that cannot be restored on another host.

At startup, the portal validates the current key, every `PreviousAtRestKeys` entry, and
`LegacyAtRestKeyVersion`. Startup is fatal when a required key is missing, is not valid base64, decodes
to fewer than 32 bytes, reuses the current version as a previous version, or names a legacy version that
cannot be resolved. The only exception is an empty current key with
`AllowMachineFallback=true`, which starts with a warning and host-bound encryption.

For backup and restore:

1. Stop writes or take a coordinated snapshot.
2. Back up `portal.db`, `Portal:DatasetRootPath`, the current key/version, all configured previous
   key/version pairs, and `LegacyAtRestKeyVersion`.
3. Restore those items as one set. Do not start the portal with only the database or dataset directory.
4. Start the portal and verify dataset reads before retiring the backup.
5. A restore with the wrong key must fail cleanly; restore the matching secret rather than changing
   metadata or attempting to regenerate the key.

A **complete** portal backup is one coordinated set: `portal.db`, the Orchestrator database
(`etlsql.db`), `Portal:ScriptRootPath`, `Portal:SnapshotDirectory`, `Portal:DatasetRootPath`, the
Data Protection key ring, and the configuration (JWT secret, dataset at-rest key/versions,
Orchestrator API key). Restored together with the matching secrets, a clean-location restore
preserves authentication, folder permissions, Orchestrator jobs, subscriptions, audit history, and
dataset metadata — verified by the automated backup/restore drill.

> **Dataset cache files are referenced by absolute path in the catalog.** Restore
> `Portal:DatasetRootPath` to its **original absolute path** (or rewrite the catalog paths) — a
> dataset whose cache moves to a different directory will not be found, and the portal's startup
> storage reconciliation will treat the moved file as an orphan. Everything else restores to a clean
> location without path constraints.

#### Versioned Upgrades and Rollback

A backup/restore drill proves recovery into a *clean* location; upgrading a *live* deployment to a new
release is a separate operation. On startup the portal runs any pending EF Core schema migrations
against the existing `portal.db` (§2 startup sequence), and the Orchestrator store adds any missing
`etlsql.db` columns in place when it initializes. Both are forward-only: an in-place upgrade preserves
authentication, folder permissions, durable execution jobs, subscriptions, datasets and their at-rest
key version, and audit history. New columns are added nullable/with defaults, so pre-upgrade rows
remain valid (for example, audit rows written before correlation-id support read back with an empty
correlation id). This is covered by an automated upgrade-path drill that seeds the previous release's
schema, migrates forward over populated data, and asserts continuity.

Procedure for an in-place upgrade:

1. **Take a complete coordinated backup first** (the full set listed above, with matching secrets).
   This backup *is* your rollback path.
2. Stop the portal (and Orchestrator service) so no writes are in flight during migration.
3. Deploy the new binaries and start the portal. Pending migrations apply automatically before it
   serves requests; watch the startup log for the migration entries and any validation failure.
4. Verify after startup: admin login, a representative protected report, a dataset read (confirms the
   at-rest key still decrypts caches), and that scheduled subscriptions/jobs are still present.

**Rollback is restore-from-backup, not a down-migration.** EF migrations ship `Down` methods, but
reverting a partially-applied or completed upgrade by running them against production data is **not a
supported recovery path** — a newer binary may already have written data shaped for the new schema. If
an upgrade fails or must be reverted, redeploy the previous binaries and restore the pre-upgrade
coordinated backup as one set. Because cache files are referenced by absolute path, restore
`DatasetRootPath` to its original location (see the note above). Keep the pre-upgrade backup until the
new release has been verified in production.

To stamp existing unversioned datasets without changing the key:

1. Configure the existing key and `AtRestKeyVersion`.
2. Leave `LegacyAtRestKeyVersion` unset.
3. Call `POST /api/admin/datasets/rotate-at-rest-key` as an administrator.

To rotate from `v1` to `v2`:

```json
{
  "Dataset": {
    "AtRestKey": "<new-v2-base64-key>",
    "AtRestKeyVersion": "v2",
    "PreviousAtRestKeys": {
      "v1": "<old-v1-base64-key>"
    },
    "LegacyAtRestKeyVersion": "v1",
    "AllowMachineFallback": false
  }
}
```

Restart the portal, then call `POST /api/admin/datasets/rotate-at-rest-key`. Rotation processes datasets
in stable ID order and commits each file and version independently. A failed dataset keeps its old file
and version; rerun the same endpoint to resume. Readers and engine scripts can use both current and
configured previous versions during this window.

After the response reports no failures and every dataset row records `v2`, take a new backup, remove
`LegacyAtRestKeyVersion`, and remove `v1` from `PreviousAtRestKeys`. Do not retire the old key until old
backups have expired or their recovery procedure retains that key separately. Rotation audit entries
record versions and counts only, never key material.

#### Interrupted Rotation

Rotation is resumable per dataset. If the request is cancelled, the process stops, or one dataset fails:

1. Keep the current and previous key mappings unchanged.
2. Restart the portal. Startup reconciliation removes abandoned `.rotate-*`, `.tmp-*`, and `.bak-*`
   staging files under `DatasetRootPath`.
3. Review the rotation response and portal logs for failed dataset names. Keys and credentials are not
   logged.
4. Correct missing files, permissions, or key-version mappings.
5. Call `POST /api/admin/datasets/rotate-at-rest-key` again. Datasets already at the target version are
   skipped; incomplete datasets are retried.
6. Retire the previous key only after every catalog row reports the target version and reads succeed.

#### Dataset Orphan Reconciliation

The portal runs dataset storage reconciliation automatically during startup, before serving requests.
It is intentionally limited to the top level of `DatasetRootPath`:

- abandoned transaction and rotation staging files are deleted;
- catalog rows with an empty path or a missing managed cache file are deleted;
- unreferenced files matching the managed `<safe-name>_<id>.parquet` naming pattern are deleted;
- files outside `DatasetRootPath`, nested files, and files that do not match the managed naming pattern
  are not adopted or deleted.

Operator procedure:

1. Back up `portal.db` and `DatasetRootPath` before manually repairing catalog or filesystem state.
2. Stop the portal and inspect both sides together. Do not rename managed files to make them appear
   referenced; their stable dataset ID is part of the filename contract.
3. Restore a missing referenced cache from the coordinated backup before startup. If no valid cache
   exists, allow reconciliation to remove the stale row, then republish or rerun the producing report.
4. Move suspected unmanaged files outside `DatasetRootPath` before startup if they need investigation.
5. Start the portal and inspect `DatasetStorageMaintenance` log entries for each removed row or file.
6. Run `SHOW DATASETS` and exercise representative reads after reconciliation.

### 6.6 Effective Permissions

Admins can inspect resolved portal access without mentally joining users, groups, folders, reports, and ACL rows:

| Endpoint | Purpose |
| :--- | :--- |
| `GET /api/admin/permissions/effective/user/{userId}` | Lists the folders and reports a user can access, including the group source for each effective permission. |
| `GET /api/admin/permissions/effective/folder/{folderId}` | Lists users with effective access to a folder. |
| `GET /api/admin/permissions/effective/report/{reportId}` | Lists users with effective access to a report through its folder ACLs. |

Reports inherit folder permissions. If a user belongs to multiple groups, the highest permission wins (`Read < Execute < Manage`) and the response lists the group or groups that supplied that winning level.

```sql
SHOW EFFECTIVE PERMISSIONS FOR USER 'john.doe' INTO #effective;
SHOW EFFECTIVE PERMISSIONS FOR REPORT 'Monthly Sales' INTO #effective;
SHOW EFFECTIVE PERMISSIONS FOR FOLDER '/Finance' INTO #effective;
```

### 6.7 Usage Metrics

Admins can inspect operational usage with `SHOW PORTAL USAGE METRICS FOR 30 DAYS` or `GET /api/admin/metrics/usage?days=30`. The response includes total report views, unique viewers, reports viewed, refresh failure count, average refresh duration, subscription delivery failures, and per-report rows with view counts, unique viewers, last view time, refresh status/error/duration, and subscription failure counts.

```sql
SHOW PORTAL USAGE METRICS FOR 30 DAYS INTO #usage;
```

For live operational health (as opposed to longer-term usage), `GET /api/admin/metrics/operational`
returns a point-in-time snapshot for a multi-user deployment: `activeExecutions` and
`queuedExecutions` (queue depth), the configured `executionCap`/`perUserExecutionCap`, recent
execution and subscription-delivery counts and failure counts over the last 24 hours (the failure
rate denominators), and `datasetStorageBytes`/`snapshotStorageBytes` for disk-usage monitoring. The
execution and delivery figures come from the durable `PortalExecutionJobs` and subscription-delivery
ledgers, so they survive a restart. The `/health` endpoint's `execution` check also reports the
single-instance topology and active execution count for liveness probes.

### 6.8 Report Dependencies

Use `SHOW REPORT DEPENDENCIES 'Report Name'` or `GET /api/reports/{id}/dependencies` to inspect the dependency view available from the report viewer. The response is permission-aware and includes the report identity, latest snapshot metadata, datasets found in the snapshot manifest, report-owned registered datasets, dataset refresh jobs, and source table references that can be parsed from the report script or dataset source queries.

```sql
SHOW REPORT DEPENDENCIES 'Monthly Sales' INTO #dependencies;
```

Source connection values are derived from two-part object names such as `sales.Orders`: `sales` is reported as the connection and `Orders` as the object. Raw column-level lineage remains available through engine lineage commands such as `SHOW LINEAGE`; the portal dependency endpoint only reports lineage details that are already present in portal metadata or parseable script text.

### 6.9 Catalog Search

Use `SHOW CATALOG SEARCH '<term>'` or `GET /api/catalog/search?q=<term>` to search visible folders and reports. Search is permission-aware: admins search the full catalog, while other users only see folders granted through group ACLs and reports inside those folders.

The search matches folder name/path and report name, description, owner, contact, tags, category, domain, steward, and certification fields. Results include a `type` of `Folder` or `Report`, the catalog `path`, report metadata, and status fields such as `snapshotBuiltAt`, `lastViewedAt`, `lastRefreshStatus`, `lastRefreshError`, and `lastRefreshDurationMs` where applicable.

Use `SHOW RECENT REPORTS LIMIT 20` or `GET /api/catalog/recent?limit=20` to list the caller's recently viewed reports. This endpoint is also permission-aware and uses the same catalog result shape as search, including snapshot, stale, script-changed, and refresh status fields. A report enters the recent list when the caller opens a snapshot through `GET /api/reports/{id}/snapshot`.

Use `FAVORITE REPORT`, `UNFAVORITE REPORT`, `SHOW FAVORITES`, or the REST endpoints to manage and list favorite reports. Favorite catalog results use the same shape as search and include `isFavorite = true`.

```sql
SHOW CATALOG SEARCH 'sales' LIMIT 25 INTO #catalog;
SHOW RECENT REPORTS LIMIT 20 INTO #recent;

FAVORITE REPORT 'Monthly Sales';
FAVORITE REPORT 'Monthly Sales' FOR USER 'john.doe';
UNFAVORITE REPORT 'Monthly Sales' FOR USER 'john.doe';
SHOW FAVORITES FOR USER 'john.doe' LIMIT 50 INTO #favorites;
```

### 6.10 Share Links

Share links and embed tokens are anonymous bearer capabilities. Keep their URLs secret. Resolution does
not require a portal login, but the portal rechecks the creator on every request: the creator must still
be active and retain read permission on the report (or remain an Admin). Revoked, expired,
creator-disabled, and permission-lost capabilities return `404 Not Found`.

New share links and embed tokens expire after seven days unless `ExpiresAt` is supplied. Role demotion or
account disablement explicitly revokes all capabilities created by that user. Successful anonymous views
are audited without recording the token. Administrators can inventory all capabilities through
`GET /api/admin/anonymous-report-access`; the inventory intentionally excludes the bearer token itself.

Use `CREATE SHARE LINK FOR REPORT`, `SHOW SHARE LINKS`, and `REVOKE SHARE LINK` for script-first administration, or the backing REST endpoints:

| Endpoint | Purpose |
| :--- | :--- |
| `POST /api/reports/{id}/share-links` | Create a share link for a report the caller can execute. |
| `GET /api/reports/{id}/share-links` | List share links for a report the caller can manage. |
| `DELETE /api/reports/{id}/share-links/{token}` | Revoke a share link. |
| `GET /api/share/{token}` | Resolve an anonymous share capability after reauthorizing its creator. |
| `GET /api/embed/{token}` | Resolve an anonymous embed capability after reauthorizing its creator. |
| `GET /api/admin/anonymous-report-access` | Admin inventory of active, expired, revoked, disabled-creator, and permission-lost capabilities. |

```sql
CREATE SHARE LINK FOR REPORT 'Monthly Sales'
    EXPIRES '2026-12-31T23:59:59Z'
    INTO #share;

SHOW SHARE LINKS FOR REPORT 'Monthly Sales' INTO #shares;
REVOKE SHARE LINK 'share-token';
```

### 6.11 Embed Tokens

Embed tokens are scoped report tokens intended for trusted internal applications. They are created by users with manage permission on the report and resolve through `GET /api/embed/{token}`. They do not grant portal administration rights and can be expired or revoked independently.

```sql
CREATE EMBED TOKEN FOR REPORT 'Monthly Sales'
    NAME 'Finance Intranet'
    EXPIRES '2026-12-31T23:59:59Z'
    INTO #embed;

SHOW EMBED TOKENS FOR REPORT 'Monthly Sales' INTO #embed_tokens;
REVOKE EMBED TOKEN 'embed-token';
```

### 6.12 Saved Views

Saved views store a user's report parameter/filter state so common slices can be reopened without re-entering parameters. They are per-user by default; admins should treat shared curated variants as separate reports or publish-time defaults rather than hidden shared state.

```sql
CREATE SAVED VIEW 'West Coast' FOR REPORT 'Monthly Sales'
    DEFAULT
    PARAMETERS (@region = 'West', @year = '2026')
    INTO #view;

SHOW SAVED VIEWS FOR REPORT 'Monthly Sales' INTO #views;
DROP SAVED VIEW 'West Coast' FOR REPORT 'Monthly Sales';
```

### 6.13 Alerts

Alerts store threshold definitions for KPI-style visuals such as cards and gauges. Alert ownership follows
the creating user; admins can see all alerts. In v0.11.0 alerts are definition-only/browser-consumed
metadata: the portal does not evaluate thresholds, schedule checks, or deliver email server-side.
`Recipient` and `SmtpAlias` are reserved metadata for a future trusted delivery implementation.

Any future server-side alert delivery must use the same security boundary as subscriptions: reload the
owner and current report permission immediately before evaluation/send, resolve SMTP secrets only at
runtime, and never persist credentials in jobs or generated scripts.

```sql
CREATE ALERT 'Revenue Floor' FOR REPORT 'Monthly Sales'
    WHEN VISUAL 'Revenue' >= 1000
    DELIVER TO 'ops@example.com'
    AT smtp;

SHOW ALERTS FOR REPORT 'Monthly Sales' INTO #alerts;
DROP ALERT 'Revenue Floor' FOR REPORT 'Monthly Sales';
```

### 6.14 Environment Promotion Pattern

Use ETL-SQL environment sets as the deployment boundary. Do not create a separate portal deployment language for dev/test/prod. Scripts should define or load the environment values first, activate the target set, then use the same portal admin commands for folders, grants, publishing, subscriptions, and refresh jobs.

```sql
CREATE SETS !DEV
BEGIN
    @PortalEnvironment = 'DEV'
END

CREATE SETS !PROD
BEGIN
    @PortalEnvironment = 'PROD';
    SET WITH_PROMPT ON;
END

USE SETS !PROD;

IF @PortalEnvironment = 'PROD'
BEGIN
    CREATE FOLDER '/Finance';

    PUBLISH REPORT 'Monthly Sales'
        FROM 'C:\Reports\Prod\monthly_sales.rptsql'
        IN FOLDER '/Finance'
        WITH (
            DESCRIPTION = 'Monthly revenue by region',
            TAGS = 'finance,monthly,certified'
        );

    GRANT EXECUTE ON FOLDER '/Finance' TO GROUP 'FinanceAnalysts';
    CREATE REFRESH JOB FOR REPORT 'Monthly Sales' SCHEDULE '0 6 * * *' AT orch;
END
```

Promotion is a normal script replay with a different active set and explicit portal literals for the target environment. Use `PUBLISH REPORT ...` for first publish or the portal's report update flow when replacing the script behind an existing catalog entry; follow with `REFRESH REPORT` after the publish step succeeds.

The copy-pasteable sample lives at `samples/report_portal_deployment/portal_promotion.etlsql`. Keep promotion scripts in source control next to the report scripts they publish so folder grants, refresh jobs, and publish paths are reviewed together.

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

### 7.3 Scripted Management

SMTP connections can also be managed from an ETL-SQL script inside an `EXECUTE portal` block (Admin role required), which keeps mail configuration reproducible alongside the rest of a portal bootstrap script:

```sql
EXECUTE portal BEGIN
    CREATE SMTP CONNECTION 'corporate' WITH (
        HOST         = 'smtp.corp.example',
        PORT         = 587,
        USERNAME     = 'mailer',
        PASSWORD     = ENC:...,            -- expression position: ENC:/variables accepted
        FROM_ADDRESS = 'reports@corp.example',
        USE_SSL      = TRUE
    );
    SHOW SMTP CONNECTIONS;                 -- never returns passwords
    DROP SMTP CONNECTION 'corporate';
END;
```

The password travels once over the authenticated HTTPS channel and is stored encrypted exactly as if entered in **Admin → SMTP**; no SMTP secret is persisted in the script's execution history or portal audit log.

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

The scheduled job itself is a **credential-free trigger**: the generated `.etlsql` script contains only the subscription ID — no SMTP credentials, recipients, or report parameters. When the trigger completes, the portal's trusted delivery executor re-checks the subscription owner's active state and current report permission, exports the report, and sends the email in-process. The SMTP credential is decrypted only for the duration of that delivery and is never written to disk. On startup the portal also rewrites any pre-upgrade subscription script that embedded credentials to the trigger form and removes generated scripts whose subscription no longer exists.

Because delivery happens in the portal, the **portal process must be running** for subscription email to be sent — the Orchestrator alone only fires the trigger.

### 8.3 Delivery Semantics

Subscription delivery is **at-most-once per scheduler trigger**. Every delivery is claimed in a durable delivery ledger keyed on `(subscription, trigger)` — the trigger being the Orchestrator completion's timestamp — so a completion observed twice (a poller re-read or a scheduler double-fire) is suppressed without re-sending. Each attempt carries a `delivery-<id>` that matches its audit correlation id, and the ledger records the terminal outcome (`Delivered`, `Failed`, `Denied`, `Skipped`).

The portal never records `Delivered` unless the in-process delivery run reports success, so it errs toward recording a failure rather than a false success. The one boundary it cannot control is SMTP itself: if the SMTP server accepts a message but the connection then times out, the recipient may receive a copy that the portal records as `Failed` — at the wire that single case is at-least-once. The ledger makes every attempt and outcome observable so such cases are visible rather than silent.

> Per-recipient delivery is currently whole-subscription: one message is composed for all recipients and the outcome applies to the subscription. Splitting delivery per recipient (so one bad address does not fail the rest) is a planned refinement.

### 8.4 Delivery Failures

Each subscription tracks a `FailCount`, incremented by the portal's delivery executor when an export or send fails (with sanitized error detail in the audit log and the delivery ledger). A delivery that is **denied** — the owner was disabled or lost read permission on the report's folder — is recorded as `SUBSCRIPTION_DELIVERY_DENIED` in the audit log and is *not* counted or retried as a transient failure. Investigate via **Admin → Subscriptions → History** and correct the SMTP configuration, permissions, or report script before re-enabling.

The Admin subscription table shows active/paused state, the last successful delivery time or failure count, and provides:

- **History** — recent delivery attempts with status, attempt time, duration, rows processed, and sanitized error text.
- **Pause / Resume** — stop or restart future deliveries without deleting the subscription.
- **Delete** — retire the subscription and remove its generated Orchestrator job.

Use the search box and status filter to isolate subscriptions by report, name, recipient, active/paused state, or delivery failure. Select rows on the current page to pause or resume multiple subscriptions together. Selection is page-local and is cleared when the filter or page changes.

### 8.5 Scripted Subscription Management

Administrators can create and modify subscriptions using ETL-SQL script syntax. This is useful for bulk setup, deployment automation, or version-controlling subscription configuration alongside report scripts.

#### CREATE SUBSCRIPTION

```sql
CREATE SUBSCRIPTION ['<name>']
FOR REPORT '<script-path>'
DELIVER TO '<email>' | GROUP '<group-name>'
SCHEDULE '<cron-expression>'
FORMAT PDF | CSV | BOTH
AT <smtp-alias>
[ PARAMETERS (
    @param1 = '<value>',
    @param2 = '<value>',
    ...
) ];
```

The optional `'<name>'` is a human-readable label shown in subscription lists. It is optional — if omitted the subscription is identified by its generated ID.

Parameter values are stored as strings and must be single-quoted. Use the report script's defaults when you want an unset parameter.

When these statements are executed remotely through a `REPORTPORTAL` connection, `FORMAT PDF` and `FORMAT CSV` are supported. `FORMAT BOTH` and `DELIVER TO GROUP` are valid ETL-SQL syntax but are not yet supported by the portal connector — the remote call will fail at runtime. Use a single format and a named recipient address until portal support for multi-format delivery and group expansion ships.

**Examples:**

```sql
-- Daily sales report: always yesterday's data
CREATE SUBSCRIPTION 'DailySales'
FOR REPORT '/Reports/Sales/Daily'
DELIVER TO 'john@example.com'
SCHEDULE '0 6 * * *'
FORMAT PDF
AT corporate-smtp
PARAMETERS (
    @start  = 'D-1',
    @end    = 'D',
    @region = 'All'
);

-- Monthly executive summary delivered to a group
CREATE SUBSCRIPTION 'MonthlyExec'
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
CREATE SUBSCRIPTION 'Q1Review'
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

When a report uses `RELDATE` INPUT parameters, the subscription stores the expression string — not a resolved date. The engine resolves it fresh each time the subscription fires. See [`Docs/Reference/RelativeDate_Parameters.md`](Reference/RelativeDate_Parameters.md) for the full expression reference.

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
ALTER SUBSCRIPTION <id> SET
    SCHEDULE = '<cron-expression>' |
    FORMAT = PDF | CSV | BOTH |
    SMTP = '<smtp-alias>' |
    ENABLE |
    DISABLE |
    PARAMETERS (
        @param1 = '<value>',
        ...
    );
```

The `PARAMETERS(...)` clause **replaces the full parameter set** for the subscription. To clear all parameters use `PARAMETERS ()` (empty). To leave parameters unchanged, omit the clause.

```sql
-- Change schedule only
ALTER SUBSCRIPTION 5 SET SCHEDULE = '0 8 * * 1-5';

-- Update parameters only
ALTER SUBSCRIPTION 5 SET
PARAMETERS (
    @start  = 'W-1',
    @end    = 'W',
    @region = 'North'
);

-- Pause a subscription
ALTER SUBSCRIPTION 6 SET DISABLE;
```

#### DROP SUBSCRIPTION

```sql
DROP SUBSCRIPTION <id>;
```

---

## 9. Extended Admin Scripting

The Report Portal connector supports script-first administration inside a remote block:

```sql
CREATE CONNECTION portal AS REPORTPORTAL (
    HOST = 'http://localhost:5000',
    USERNAME = 'admin',
    PASSWORD = ENC:...
);

EXECUTE portal BEGIN
    SHOW USERS;
    SHOW REPORTS;
END;
```

Result-producing commands can write to a temp table with `INTO #table` and also update `@@RESULT` / `@@RESULTSETS`.

### 9.0 Configuration Export (Script-First Reconstruction)

```sql
EXECUTE portal BEGIN
    EXPORT PORTAL CONFIGURATION TO 'portal_bootstrap.txt';
END;
```

Admin-only. Writes the portal's declarative configuration as a replayable bootstrap script in
dependency order: groups, users, group memberships, folders, folder ACLs, SMTP connections, report
publications, dataset metadata and grants, subscriptions, and alerts — by logical name, never
database id. Secrets are **never** exported: password-bearing statements carry `${...}`
placeholders collected in a `REQUIRED SECRETS` header, and a trailing summary lists every emitted,
skipped, and runtime-only item so nothing is omitted silently. The same script is available from
`GET /api/admin/configuration/export`, and each export writes an `EXPORT_PORTAL_CONFIGURATION`
audit event.

No real secret or security material ever appears in the export — not password hashes, encrypted SMTP
credentials, JWT/dataset-at-rest keys, Orchestrator API keys, refresh tokens, or share/embed
capability tokens. Each credential is a `${...}` placeholder you replace before import, ideally
**without putting plaintext in the file**:

- `ENV('NAME')` — resolve from an environment variable at import (preferred; nothing sensitive in the script).
- `ENC:...` — an encrypted literal, unlocked by `USE PASSWORD = ...` at import.
- `'...'` — a plaintext literal (least preferred; avoid committing).

An unsubstituted `${...}` placeholder is rejected at import before it reaches the portal (see §9.0
import behavior), so a forgotten secret fails closed rather than provisioning an empty credential.

Notes:

- The engine write-blocks script extensions (`.etlsql`, `.sql`) as control-plane protection, so
  export to a data extension such as `.txt` and rename after review when committing to source control.
- The script reconstructs **configuration only** — report `.rptsql` files, dataset caches, and
  snapshots are content and travel separately. The export ends with a **companion content manifest
  and recovery runbook** naming the three recovery paths and listing every report script to copy
  into the target script root and every dataset to re-materialize or re-publish. The three paths:
  (1) configuration — this script, the auditable clean-start path; (2) content — the manifest's
  report scripts and datasets, copied/published separately; (3) exact-state disaster recovery —
  restoring the portal and Orchestrator database/file backups, which this export does not replace.
- Replay against a fresh portal requires substituting every `${...}` placeholder first; scheduled
  refresh jobs are listed in the summary for manual re-creation because they need an Orchestrator
  connection alias.

#### Importing (replaying the bootstrap)

The bootstrap is replayed by running it as a normal script through an admin `REPORTPORTAL`
connection — substitute the `${...}` placeholders, then:

```sql
CREATE CONNECTION portal AS REPORTPORTAL (HOST = '...', USERNAME = 'admin', PASSWORD = ENC:...);
-- Preview first — no mutations, validates references and secrets:
SET WHAT_IF ON;
-- run the EXECUTE portal BEGIN … END block; the portal reports a create/skip plan per statement
SET WHAT_IF OFF;
-- run it again to apply
```

Import behavior:

- **Idempotent (safe to rerun).** Provisioning the identity/permission graph — users, groups,
  group memberships, folders, folder grants, SMTP connections, report publications — is
  create-or-skip: an object that already exists is left untouched and logged as skipped, so the
  same bootstrap can be replayed without `409 Conflict` errors. (Subscriptions and alerts are not
  yet name-keyed and would be re-created on a rerun — drop them first or run them once.)
- **Fail-closed before mutation.** A missing referenced folder, group, user, or report stops the
  statement with a clear error instead of a generic portal failure, and an unsubstituted `${...}`
  secret placeholder is rejected before it is ever sent to the portal.
- **`SET WHAT_IF ON` is a validating dry-run.** Each statement reports what it *would* do
  (create / skip) and performs the same reference and secret validation as a real apply — without
  writing anything — so you can confirm a clean import before committing to it.

### 9.1 Report Operations

```sql
EXECUTE portal BEGIN
    SHOW REPORT 'Daily Sales' INTO #report;
    SHOW REPORT HISTORY 'Daily Sales' INTO #history;
    SHOW REPORT DEPENDENCIES 'Daily Sales' INTO #deps;
    VALIDATE REPORT SCRIPT 'C:\Reports\daily_sales.rptsql' INTO #validation;

    FAVORITE REPORT 'Daily Sales';
    FAVORITE REPORT 'Daily Sales' FOR USER 'alice';
    SHOW FAVORITES LIMIT 25 INTO #favorites;
    SHOW FAVORITES FOR USER 'alice' LIMIT 25;
    UNFAVORITE REPORT 'Daily Sales' FOR USER 'alice';
END;
```

Name lookups are case-insensitive. If multiple reports share the same name, the connector raises an ambiguity error instead of choosing one.

### 9.2 Sharing, Embedding, Saved Views, and Alerts

```sql
EXECUTE portal BEGIN
    CREATE SHARE LINK FOR REPORT 'Daily Sales' EXPIRES '2026-12-31T23:59:59Z' INTO #share;
    SHOW SHARE LINKS FOR REPORT 'Daily Sales';
    REVOKE SHARE LINK '<token>';

    CREATE EMBED TOKEN FOR REPORT 'Daily Sales' NAME 'Finance Wallboard' INTO #embed;
    SHOW EMBED TOKENS FOR REPORT 'Daily Sales';
    REVOKE EMBED TOKEN '<token>';

    CREATE SAVED VIEW 'EMEA' FOR REPORT 'Daily Sales'
        PARAMETERS (@region = 'EMEA', @start = 'D-1');
    SHOW SAVED VIEWS FOR REPORT 'Daily Sales';
    DROP SAVED VIEW 'EMEA' FOR REPORT 'Daily Sales';

    CREATE ALERT 'HighFailures' FOR REPORT 'Ops'
        WHEN VISUAL 'FailureCard' > 10
        DELIVER TO 'ops@example.com'
        AT corporate-smtp;
    SHOW ALERTS FOR REPORT 'Ops';
    DROP ALERT 'HighFailures' FOR REPORT 'Ops';
END;
```

### 9.3 Catalog, Permissions, Metrics, and Sessions

```sql
EXECUTE portal BEGIN
    SHOW RECENT REPORTS LIMIT 20 INTO #recent;
    SHOW CATALOG SEARCH 'finance' LIMIT 50 INTO #catalog;
    SHOW EFFECTIVE PERMISSIONS FOR USER 'alice' INTO #perms;
    SHOW EFFECTIVE PERMISSIONS FOR REPORT 'Daily Sales';
    SHOW EFFECTIVE PERMISSIONS FOR FOLDER '/Finance';
    SHOW PORTAL USAGE METRICS FOR 30 DAYS INTO #metrics;
    SHOW ACTIVE SESSIONS INTO #sessions;

    DISCONNECT USER 'alice';
    REVOKE TOKENS FOR USER 'alice';
END;
```

`SHOW ACTIVE SESSIONS` reports unrevoked, unexpired refresh tokens. `DISCONNECT USER` and
`REVOKE TOKENS` revoke refresh tokens and rotate the user's security stamp, so already-issued access
tokens are rejected on their next request.

### 9.4 Service Control

```sql
EXECUTE portal BEGIN
    RESTART PORTAL;
    SHUTDOWN PORTAL;
END;
```

Service-control commands require an Admin user and are disabled by default. Enable them only for trusted automation:

```json
{
  "Portal": {
    "AllowServiceControl": true
  }
}
```

`RESTART PORTAL` requests process shutdown so Docker, systemd, Windows Service, or another supervisor can start it again. The portal does not self-spawn a replacement process.

---

## 10. Health Monitoring

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
| `REFRESH_TOKEN_REUSE` | A revoked refresh token was replayed (theft signal); all of the user's sessions were invalidated |
| `UPDATE_ORCHESTRATOR_SETTINGS` | Admin changed the Orchestrator URL or API key via the Settings tab |

### 10.2 Exporting the Audit Log

Click **Export CSV** to download up to 10,000 most-recent entries as a UTF-8 CSV file. You can also filter by action type and user before exporting. The export includes each row's **correlation id** — the HTTP request trace identifier or the background operation id (e.g. `delivery-<id>` for subscription deliveries) — so every event can be tied back to the operation that produced it.

### 10.3 Audit Guarantees, Retention, and the Tamper-Evidence Boundary

- **Mutations and their audit rows commit together.** Security-sensitive changes (user role/active/password/token changes, user deletion and ownership transfer, group membership, folder and dataset ACLs, dataset metadata/move/delete, SMTP definitions, share-link/embed-token revocation, subscription delivery outcomes) write their audit row in the same database transaction as the change itself: the operation cannot succeed without its durable audit event, and a rejected or conflicted operation leaves no audit row behind. Informational events (views, exports, logins, denials) remain independent best-effort records.
- **Retention is opt-in.** By default every audit row is kept forever. Set `Portal:Audit:RetentionDays` to enable a daily sweep that deletes rows older than the window (`Portal:Audit:PurgeIntervalSeconds` tunes the cadence). Export or forward rows you must keep **before** enabling retention.
- **The audit table is not tamper-proof — by design.** It lives in the writable portal SQLite database, so an attacker (or administrator) with file access can alter it. The supported enterprise posture is to **export or forward audit data to external append-only storage on a schedule** (the CSV endpoint, or log forwarding per the security guide) and treat the in-portal table as the operational view. Tamper-evident hash chaining inside the portal database is a deliberate non-goal for this release (see `ROADMAP.md`).

---

## 11. Security Model

### 11.1 Authentication

The portal uses **JWT Bearer tokens** with HMAC-SHA256 signing.

- Access tokens expire after `Jwt.ExpiryMinutes` (default 60 min).
- Every access token contains the user's Identity security stamp. Validation checks current account
  state (active flag + stamp) through a 30-second in-memory cache, so revocation takes effect
  immediately in-process and within 30 seconds across processes, without a database read per request.
- Refresh tokens expire after `Jwt.RefreshExpiryDays` (default 7 days), are stored only as SHA-256
  digests, and are single-use. Each successful refresh revokes the old token and returns a replacement.
- Replaying an already-rotated refresh token is treated as a theft signal: the request is rejected,
  every session and refresh token for that user is invalidated, and a `REFRESH_TOKEN_REUSE` audit
  event is written.
- Expired refresh-token rows are purged hourly. Revoked-but-unexpired rows are retained on purpose —
  they are the evidence reuse detection needs.
- Role, group, folder/dataset ACL, active-state, password, and LDAP mapping changes rotate the stamp and
  revoke outstanding refresh tokens for affected users.
- **Logout**, **Disconnect User**, and **Revoke Tokens** invalidate all current sessions for that user,
  including already-issued access tokens.
- Browser clients store access and refresh tokens in `sessionStorage`, not cookies. This avoids a
  cookie/CSRF authentication surface and keeps API clients on the same bearer-token model, but
  JavaScript running in the page can read the tokens. The portal therefore applies a nonce-based
  Content Security Policy, blocks inline event handlers, and does not permit arbitrary script origins.
  Do not weaken `script-src` or add `unsafe-inline`.

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

### 11.7 Browser Security Headers and Embedding

The portal sends `Content-Security-Policy`, `X-Content-Type-Options: nosniff`,
`Referrer-Policy: no-referrer`, and a restrictive `Permissions-Policy` on every response. Portal HTML
uses a fresh script nonce per response. Same-origin framing is allowed by default; external framing is
denied.

To allow a trusted application to frame portal content, list each exact origin. Paths, wildcards, user
information, and non-HTTP schemes are rejected:

```json
"Portal": {
  "Security": {
    "FrameAncestors": [
      "https://analytics.example.com",
      "https://intranet.example.com:8443"
    ]
  }
}
```

When no external origin is configured, the portal also sends `X-Frame-Options: SAMEORIGIN`. When
external origins are configured, CSP `frame-ancestors` is authoritative and the legacy header is
omitted because it cannot express an allowlist.

### 11.8 Unauthenticated Request Rate Limits

The portal applies fixed-window limits by remote IP address and endpoint path. Requests over the limit
are rejected immediately with `429 Too Many Requests` and `Retry-After: 60`; excess requests are not
queued.

```json
"Portal": {
  "RateLimit": {
    "AuthPermitLimit": 20,
    "AuthWindowSeconds": 60,
    "AnonymousTokenPermitLimit": 60,
    "AnonymousTokenWindowSeconds": 60
  }
}
```

The auth policy covers every `/api/auth/*` action. The anonymous-token policy covers share-link and
embed-token resolution. When the portal runs behind a reverse proxy, configure ASP.NET Core forwarded
headers at the host boundary so `RemoteIpAddress` is the trusted client address; do not accept forwarded
addresses from arbitrary direct clients.

### 11.9 Runtime Secret Provisioning and Rotation

Provision `Portal:Jwt:Secret`, `Portal:Orchestrator:ApiKey`, and `Orchestrator:ApiKey` through
environment variables or the deployment secret provider. The shared `AddSecureConfiguration` layer
also accepts machine-bound `ENC:` values. Do not commit plaintext production values to
`appsettings.json`.

The portal persists its ASP.NET Data Protection key ring in `.portal-keys` beside `portal.db`.
Admin-entered Orchestrator API keys in `portal-orchestrator.json` are protected by that ring. Back up
`.portal-keys` with the portal database; losing it makes protected SMTP and Orchestrator values
unreadable. Legacy sidecars containing plaintext `ApiKey` are automatically rewritten with
`ProtectedApiKey` when first loaded.

#### Rotate the JWT signing secret

1. Generate a new 256-bit-or-stronger secret.
2. Set the new value as `Portal__Jwt__Secret`.
3. Put the old value in `Portal__Jwt__PreviousSecrets__0`.
4. Restart all portal instances together. New tokens use only the new key; existing access tokens
   signed by the old key remain valid.
5. After at least `Jwt.ExpiryMinutes` plus clock skew has elapsed, remove the old value from
   `PreviousSecrets` and restart all instances.

Removing the old key immediately is an emergency revocation procedure and invalidates access tokens
signed with it. Refresh tokens are not JWT-signed and can still obtain a new access token unless the
user sessions are separately revoked.

#### Rotate the Orchestrator API key without downtime

1. Generate a new random key.
2. Add it to `Orchestrator__PreviousApiKeys__0` while leaving the old key in
   `Orchestrator__ApiKey`; restart the Orchestrator. It now accepts both.
3. Change the portal to send the new key through `Portal__Orchestrator__ApiKey` or Admin Settings and
   verify an authenticated management request.
4. Set the new key as `Orchestrator__ApiKey`, retain the old key temporarily in
   `Orchestrator__PreviousApiKeys__0`, and restart the Orchestrator.
5. After every caller has moved to the new key, remove the old key from `PreviousApiKeys` and restart.

Keep the overlap short and record the cutover. `PreviousSecrets` and `PreviousApiKeys` are validation
rings, not permanent secret archives.

---

## 13. Orchestrator Management

The portal includes a built-in **Orchestrator** tab that provides a web interface for managing ETL-SQL scheduled jobs. Access is controlled by the `OrchestratorAccess` policy: **Admin** or **OrchestratorManager** role.

### 13.1 Connecting to the Orchestrator Service

The portal communicates with the Orchestrator Service over HTTP. Configure the connection in one of two ways:

**Via environment variable / `appsettings.json`** (takes effect at startup):

```json
"Portal": {
  "Orchestrator": {
    "ApiUrl": "http://orchestrator-host:5001",
    "ApiKey": "your-shared-secret",
    "SameHost": false
  }
}
```

**Via the Admin UI** (takes effect immediately — no restart required):

1. Log in as Admin.
2. Navigate to **Admin → Settings → Orchestrator Connection**.
3. Enter the **Orchestrator API URL** (e.g., `http://orchestrator-host:5001`).
4. Enter the **API Key** if one is configured on the Orchestrator side.
5. Click **Save**.

The portal writes a `portal-orchestrator.json` sidecar file next to the portal database. Values saved here override environment variables on the next request.

To verify the connection, click **Test Connection** — the button calls the `/api/orchestrator/status` endpoint using the currently saved settings and displays an Online or Offline chip.

> [!TIP]
> If you change the URL or key without saving, **Test Connection** still tests the previously saved settings. Save first, then test.

#### API Key

The API key is sent as an `X-Orchestrator-Key` header on every request the portal makes to the Orchestrator. The Orchestrator must be configured with the same key:

```
Orchestrator__ApiKey=your-shared-secret
```

Remote report execution returns the completed report manifest through the authenticated Orchestrator
job-status API. The Portal then persists the manifest under its own `SnapshotDirectory`, so a shared
snapshot folder is not required when the two services run on separate hosts. Configure the same
non-empty API key on both services; the Orchestrator never includes report manifest data in an
unauthenticated status response. Verify the connection by executing a small report and confirming both
the snapshot manifest and CSV export are available.

The portal never echoes the stored API key back to the browser — the **Admin → Settings** page shows only whether a key is set (`HasApiKey: true/false`). To change the key, type a new value and save. To clear it, check **Clear API key** and save.

### 13.2 What the Orchestrator Tab Shows

After connecting, the Orchestrator tab displays:

| Section | Description |
| :--- | :--- |
| **Stats bar** | Service status chip (Online/Offline), Active Jobs, Queued, Completed Today, Failed Today. Refreshes every 10 seconds. |
| **24-hour Gantt chart** | All jobs plotted on a timeline from 00:00 to 23:59. Each bar is positioned at the job's scheduled fire time and sized by historical average duration. Blue = enabled, grey = disabled. Click a bar to open the job detail panel. |
| **Jobs table** | All registered jobs including disabled ones. Columns: Name, Schedule, Status, Last Run, Next Run, Actions. |
| **Job detail panel** | Slides in from the right when you click a job or Gantt bar: schedule info, script content (read-only), duration trend sparkline, and a history table showing the last 20 executions. |

### 13.3 Job Actions

| Action | What it does |
| :--- | :--- |
| **Run / Trigger** | Fires the job immediately, outside its normal schedule. The job still runs at its next scheduled time afterwards. |
| **Disable** | Sets `IsEnabled = false`. The job is still visible (dimmed) and its history is preserved. Re-enable at any time. |
| **Enable** | Sets `IsEnabled = true`. The scheduler picks the job up at its next fire time. |
| **Kill** | Cancels a currently-running execution. Only available when the job has a `RUNNING` history entry. |
| **Delete** | Permanently removes the job definition and all its history. This is equivalent to `DROP JOB` and cannot be undone. |

> [!CAUTION]
> Use **Disable** to pause a job temporarily. Use **Delete** only to retire a job permanently.

### 13.4 Creating a Job

Click **New Job** to open the Create Job modal.

| Field | Description |
| :--- | :--- |
| **Job Name** | Unique identifier — no spaces, use underscores |
| **Script** | Pick from the Orchestrator's script browser (files in `Orchestrator:ScriptRoot`) or enter a path manually |
| **Every / Unit** | Schedule interval: a number and `SECONDS`, `MINUTES`, `HOURS`, or `DAYS` |
| **At Time** | Optional `HH:MM` wall-clock time, used with `DAYS` to pin to a specific time of day |
| **Max Retries** | How many times to retry on failure (0 = no retries) |
| **Retry Delay** | Initial delay in seconds between retries (doubles on each subsequent attempt) |
| **Hash Policy** | `Warn` (log a warning if the script changed since creation), `Block` (refuse to run if the script changed), or `Off` |

> [!NOTE]
> The job stores the script content at creation time. If the `.etlsql` file changes on disk later, the stored copy is not updated automatically. Re-create or re-save the job to pick up the change.

### 13.5 Service Control

When the Orchestrator is online, two buttons appear next to the status chip:

- **Stop** — gracefully shuts down the Orchestrator process. If it is registered as a Windows Service or systemd unit, the OS supervisor restarts it automatically. The portal polls the health endpoint every 3 seconds and updates the status chip when the service comes back.
- **Restart** — functionally identical to Stop; the portal waits for the service to come back online and shows a polling indicator.

When the Orchestrator is offline:
- An **Offline** banner is shown across the top of the page.
- If `Portal:Orchestrator:SameHost = true` is configured, a **Start** button appears that uses the Windows `ServiceController` API to start the local service.
- On separate-server deployments the portal displays: *"Orchestrator is offline — start the service on its host machine."*

### 13.6 Performance Metrics

The job detail panel's history table includes per-execution performance data:

| Column | Source |
| :--- | :--- |
| **Duration** | Wall-clock time from `StartTime` to `EndTime` |
| **Rows Processed** | Row count reported by the script |
| **Peak RAM** | Peak memory in bytes during execution (recorded at job completion) |
| **CPU Time** | Cumulative CPU seconds (recorded at job completion) |

> [!NOTE]
> RAM and CPU columns are only populated for completed runs. A currently-running job shows elapsed wall-clock time only — live resource counters are not available.

### 13.7 Configuration Reference

| Key | Location | Description |
| :--- | :--- | :--- |
| `Portal:Orchestrator:ApiUrl` | Portal `appsettings.json` / env var | Base URL of the Orchestrator Service HTTP API |
| `Portal:Orchestrator:ApiKey` | Portal `appsettings.json` / env var | Shared secret sent as `X-Orchestrator-Key` header |
| `Portal:Orchestrator:SameHost` | Portal `appsettings.json` / env var | `true` enables the **Start** button using Windows `ServiceController` |
| `portal-orchestrator.json` | Sidecar file next to portal database | Overrides for URL/key saved via the Admin UI; takes precedence over env vars |
| `Orchestrator:ApiKey` | Orchestrator `appsettings.json` / env var | Key the Orchestrator validates against incoming `X-Orchestrator-Key` headers |
| `Orchestrator:ScriptRoot` | Orchestrator `appsettings.json` / env var | Root directory for the script file browser exposed to the portal |

---

## 14. Production Readiness Checklist

Use this checklist before promoting the Report Portal to a production or customer-facing environment. Items marked **Required** will cause data loss, security exposure, or service failure if skipped. Items marked **Recommended** reduce operational risk.

### Security

- [ ] **Required** — Change the initial `admin` password after first login. If it was provisioned via `Portal__FirstRun__AdminPassword`, remove that value from configuration afterwards; if it was generated, treat the startup log line that printed it as sensitive.
- [ ] **Required** — Replace the default JWT secret. Set `Portal__Jwt__Secret` in environment variables or `appsettings.json` to a randomly generated 256-bit value. Run `etl-sql config setup-jwt --update` to generate one.
- [ ] **Required** — Set `Portal__Jwt__Issuer` and `Portal__Jwt__Audience` to values that match your deployment. Default `ETL-SQL-Portal` values are acceptable but should be documented.
- [ ] **Required** — Enable HTTPS in production. Configure a reverse proxy (nginx, Caddy, IIS) or supply a TLS certificate via Kestrel. Do not run the portal over plain HTTP with real user data.
- [ ] **Recommended** — Restrict `Security:AuthorizedHosts` in `appsettings.json` to the actual hostnames the portal will accept requests from.
- [ ] **Recommended** — Verify that connector secrets in report scripts use `ENC:` encryption with a master password, not plaintext connection strings.
- [ ] **Recommended** — Review folder-level permissions. Users should not have access to reports or datasets outside their role.

### Data and Storage

- [ ] **Required** — Confirm `Portal:DatabasePath` points to a persistent location that survives service restarts and OS reboots (not a temp directory or container ephemeral layer).
- [ ] **Required** — Confirm `Portal:SnapshotRoot` and `Portal:ReportDataRoot` are writable and on a volume with sufficient capacity for report snapshots and dataset exports.
- [ ] **Recommended** — Schedule regular backups of the portal SQLite database and the snapshot/data directories.
- [ ] **Recommended** — Set `Portal:MaxSnapshotAgeDays` to automatically clean up expired snapshots.

### Reliability

- [ ] **Required** — Run exactly one active Report Portal process for each portal SQLite database and script/snapshot/dataset storage root. The portal enforces this with `portal.instance.lock` beside the database and refuses a second instance. Horizontal portal replicas are not a supported topology.
- [ ] **Required** — Run the portal as a managed service (Windows Service or systemd unit) so it restarts automatically on host reboot or crash.
- [ ] **Required** — Treat a restart as cancellation of in-flight portal executions. Polling remains durable through `PortalExecutionJobs`; abandoned `Pending`/`Running` jobs return `Cancelled` with an interruption reason and must be submitted again.
- [ ] **Recommended** — Verify the `/health` endpoint returns `Healthy` before directing user traffic. Wire this endpoint into your load balancer or monitoring system.
- [ ] **Recommended** — If the Orchestrator is deployed separately, confirm `Portal:Orchestrator:ApiUrl` and both `ApiKey` values match. Verify the connection via the Admin → Orchestrator page.
- [ ] **Recommended** — Configure SMTP for subscriptions. Test an outbound email from Admin → Connections before creating live subscriptions.

### Observability

- [ ] **Recommended** — Enable structured logging (`Logging:LogLevel:Default` = `Information` minimum). Direct logs to a persistent file or log aggregator.
- [ ] **Recommended** — Enable the audit log (`Portal:EnableAuditLog = true`) so report view, export, and subscription events are recorded.
- [ ] **Recommended** — Set up a monitoring alert on the `/health` endpoint with a recovery window of ≤ 5 minutes.
- [ ] **Recommended** — Review the Report History page after first production use to confirm snapshot refresh and subscription delivery are completing without errors.

### Operational Handoff

- [ ] **Recommended** — Document the deployment: service name, host, port, backup schedule, and escalation path.
- [ ] **Recommended** — Identify who holds the admin credentials and the JWT secret, and ensure they are stored in a secrets manager (not in a shared document).
- [ ] **Recommended** — Run `etl-sql doctor` from the host machine to confirm write access, ODBC drivers, and configuration are correct before go-live.
