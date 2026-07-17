# Publishing Reports

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
the Portal database, and the dataset directory together. Restoring only the database/files without the
matching key makes the caches unreadable.

`Portal:Dataset:AllowMachineFallback=true` is supported only for deliberate development/standalone use.
It creates host-bound caches that cannot be restored on another host.

At startup, the portal validates the current key, every `PreviousAtRestKeys` entry, and
`LegacyAtRestKeyVersion`. Startup is fatal when a required key is missing, is not valid base64, decodes
to fewer than 32 bytes, reuses the current version as a previous version, or names a legacy version that
cannot be resolved. The only exception is an empty current key with
`AllowMachineFallback=true`, which starts with a warning and host-bound encryption.

For backup and restore:

1. Stop writes or take a coordinated snapshot.
2. Back up the Portal database, `Portal:DatasetRootPath`, the current key/version, all configured
   previous key/version pairs, and `LegacyAtRestKeyVersion`.
3. Restore those items as one set. Do not start the portal with only the database or dataset directory.
4. Start the portal and verify dataset reads before retiring the backup.
5. A restore with the wrong key must fail cleanly; restore the matching secret rather than changing
   metadata or attempting to regenerate the key.

A **complete** portal backup is one coordinated set: the Portal database, the Orchestrator database,
`Portal:ScriptRootPath`, `Portal:SnapshotDirectory`, `Portal:DatasetRootPath`, the Data Protection key
ring, and the configuration (JWT secret, dataset at-rest key/versions, Orchestrator API key). In
single-node deployments those databases are usually `portal.db` and `etlsql.db`; in HA deployments
they are PostgreSQL backups taken with the shared artifact roots. Restored together with the matching
secrets, a clean-location restore preserves authentication, folder permissions, Orchestrator jobs,
subscriptions, audit history, and dataset metadata — verified by the automated backup/restore drill.

> **Dataset cache files are referenced by absolute path in the catalog.** Restore
> `Portal:DatasetRootPath` to its **original absolute path** (or rewrite the catalog paths) — a
> dataset whose cache moves to a different directory will not be found, and the portal's startup
> storage reconciliation will treat the moved file as an orphan. Everything else restores to a clean
> location without path constraints.

#### Versioned Upgrades and Rollback

A backup/restore drill proves recovery into a *clean* location; upgrading a *live* deployment to a new
release is a separate operation. On startup the portal runs any pending EF Core schema migrations
against the configured Portal database (§2 startup sequence), and the Orchestrator store adds any
missing columns in place when it initializes. Both are forward-only: an in-place upgrade preserves
authentication, folder permissions, durable execution jobs, subscriptions, datasets and their at-rest
key version, and audit history. New columns are added nullable/with defaults, so pre-upgrade rows
remain valid (for example, audit rows written before correlation-id support read back with an empty
correlation id). This is covered by an automated upgrade-path drill that seeds the previous release's
schema, migrates forward over populated data, and asserts continuity.

Additionally, to prevent browsers from serving stale cached assets after an upgrade, the portal automatically applies cache-busting asset fingerprinting on startup. It scans HTML files in the web root and appends the active `EngineVersion` as a query parameter (e.g., `?v=0.12.0`) to all referenced JS, CSS, and ES module imports.

Rolling upgrade migrations follow an expand/migrate/contract discipline. The automated
`PortalMigrations_UpOperationsFollowRollingExpandContract` test rejects destructive `Up` operations
such as table/column drops, table/column renames, existing-column alterations, or required columns
without defaults. Destructive contract cleanup must be a later, explicit migration after all nodes
are running binaries that no longer depend on the old shape.

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

For live operational health (as opposed to longer-term usage), use
`SHOW PORTAL OPERATIONAL METRICS INTO #ops` or `GET /api/admin/metrics/operational`. The response
is a point-in-time snapshot for a multi-user deployment: `activeExecutions` and
`queuedExecutions` (queue depth), the configured `executionCap`/`perUserExecutionCap`, recent
execution and subscription-delivery counts and failure counts over the last 24 hours (the failure
rate denominators), `averageExecutionDurationMs`, `averageQueuedExecutionAgeSeconds`, and
`datasetStorageBytes`/`snapshotStorageBytes` for disk-usage monitoring. The response also includes
`hourlyExecutionLoad`, a last-24-hours UTC bucket list with `hourUtc`, `executions`, `failures`,
`rowsProcessed`, and `peakMemoryBytes`, so operators can identify busy hours, high-failure windows,
and executions that are moving unusually large result sets. The execution and delivery figures come
from the durable `PortalExecutionJobs` and subscription-delivery ledgers, so they survive a restart.
It also reports database **schema migration status** — `appliedMigrations`, `pendingMigrations`,
`lastAppliedMigration`, and `schemaUpToDate` — so after an in-place upgrade an operator can confirm
the catalog migrated fully (`pendingMigrations: 0`) without shell access. The `/health` endpoint's
`execution` check also reports the single-instance topology and active execution count for liveness
probes.

For a single report execution, poll `GET /api/jobs/{jobId}` with the job id returned by a refresh or
execute request. The job response includes `rowsProcessed`, `peakMemoryBytes`, and `cpuTimeSeconds`
when the execution path can measure them. Use this endpoint for per-job troubleshooting; use
`SHOW PORTAL OPERATIONAL METRICS` or `GET /api/admin/metrics/operational` for aggregate
administrator load monitoring. The current portal UI does not surface all of these fields yet, so
the documented script and REST endpoints are the discovery path.

For infrastructure scrapers, the Portal also exposes `GET /metrics` in Prometheus text format. It
uses the same non-secret operational snapshot as `GET /api/admin/metrics/operational` and emits
stable low-cardinality labels: `environment`, `node`, and `component="portal"`. The scrape includes
active and queued executions, execution caps, recent execution/delivery totals and failures, average
execution duration, average queue age, dataset/snapshot storage bytes, active subscriptions, SMTP
connection count, Portal schema migration status, audit outbox pending/failed counts, audit outbox
pending bytes and oldest-pending age, security-event pending/failed counts, stored bytes, dropped
count, oldest-pending age, and collector configured/reachable state. It does not emit script paths,
report names, usernames, connection strings, credentials, local filesystem paths, policy payload
values, collector URLs, collector errors, or secret configuration values. Treat `/metrics` as an
operations endpoint and expose it only on a trusted management network or behind your standard
monitoring ingress controls.
The scrape also includes component-labeled runtime gauges for process working set, private memory,
managed heap bytes, generation 0/1/2 GC collection counts, and a database-reachable gauge for the
Portal state store used to compose the scrape.

Portal execution jobs also emit `System.Diagnostics.ActivitySource` spans from
`ETL-SQL.ReportPortal`. The `portal.execution_job` span uses bounded dimensions for environment,
component, job id, report id, user id, workload kind, execution mode, terminal status, row count,
peak memory, CPU time, script hash, and request correlation id. These dimensions are intended for
OpenTelemetry collectors or .NET listeners; avoid adding report names, usernames, local paths,
parameter values, SQL text, or connection metadata as tags.

The same service exposes first-class `System.Diagnostics.Metrics` instruments from meter
`ETL-SQL.ReportPortal` for terminal execution count, duration, rows processed, peak memory, and CPU
time. Metric tags intentionally stay low-cardinality: environment, node, component, workload kind,
execution mode, and terminal status. Job id, report id, user id, and script hash remain trace-only
correlation fields.

Metric labels and trace tags use the shared `ETL_SQL.Core.Observability.ObservabilityConventions`
names. Prometheus labels are the same names without the `etlsql.` prefix and with dots converted to
underscores, for example `etlsql.environment` becomes `environment` and `etlsql.workload.kind`
becomes `workload_kind`.

Every Portal HTTP response includes `X-Correlation-ID`, matching ASP.NET Core's request trace
identifier. Portal request logs are scoped with that correlation id and the active trace id, and audit
rows use the same value when a controller does not pass a more specific operation id.

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
    AT smtp
    DISABLE;

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

