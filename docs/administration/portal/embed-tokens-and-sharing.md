# Embed Tokens, Sharing, and Portal Catalog Operations

> **Applies to:** Team · Enterprise · SaaS

Manage share links, embed tokens, saved views, alerts, effective permissions, usage metrics, report dependencies, and catalog search — all scriptable from ETL-SQL.

> [!TIP]
> See [Publishing Reports](publishing.md) for the overview hub, or [Report Publishing Workflows](report-publishing-workflows.md) for publish procedures.

---

## Share Links

Share links are anonymous bearer capabilities. Keep their URLs secret. Resolution does not require a Portal login, but the Portal rechecks the creator on every request: the creator must still be active and retain read permission on the report (or remain an Admin). Revoked, expired, creator-disabled, and permission-lost capabilities return `404 Not Found`.

New share links expire after seven days unless `ExpiresAt` is supplied.

| Endpoint | Purpose |
| :--- | :--- |
| `POST /api/reports/{id}/share-links` | Create a share link for a report the caller can execute. |
| `GET /api/reports/{id}/share-links` | List share links for a report the caller can manage. |
| `DELETE /api/reports/{id}/share-links/{token}` | Revoke a share link. |
| `GET /api/share/{token}` | Resolve an anonymous share capability after reauthorizing its creator. |
| `GET /api/admin/anonymous-report-access` | Admin inventory of active, expired, revoked, disabled-creator, and permission-lost capabilities. The inventory intentionally excludes the bearer token itself. |

```sql
CREATE SHARE LINK 'External Review' FOR REPORT 'Monthly Sales'
    EXPIRES '2026-12-31T23:59:59Z'
    INTO #share;

SELECT * INTO #shares FROM eng.share_links('Monthly Sales');
REVOKE SHARE LINK 'External Review' FOR REPORT 'Monthly Sales';
```

---

## Embed Tokens

Embed tokens are scoped report tokens intended for trusted internal applications. They are created by users with manage permission on the report, resolve through `GET /api/embed/{token}`, and can be expired or revoked independently. They do not grant Portal administration rights.

```sql
CREATE EMBED TOKEN 'Finance Intranet' FOR REPORT 'Monthly Sales'
    EXPIRES '2026-12-31T23:59:59Z'
    INTO #embed;

SELECT * INTO #embed_tokens FROM eng.embed_tokens('Monthly Sales');
REVOKE EMBED TOKEN 'Finance Intranet' FOR REPORT 'Monthly Sales';
```

---

## Saved Views

Saved views store a user's report parameter/filter state so common slices can be reopened without re-entering parameters. They are per-user by default.

```sql
CREATE SAVED VIEW 'West Coast' FOR REPORT 'Monthly Sales'
    DEFAULT
    PARAMETERS (@region = 'West', @year = '2026')
    INTO #view;

SELECT * INTO #views FROM eng.saved_views('Monthly Sales');
DROP SAVED VIEW 'West Coast' FOR REPORT 'Monthly Sales';
```

---

## Alerts

Alerts store threshold definitions for KPI-style visuals such as cards and gauges. Alert ownership follows the creating user; admins can see all alerts. Alert delivery links to a named Orchestrator notification so delivery policy, secrets, retries, and audit remain centralized.

```sql
CREATE OR REPLACE ALERT RevenueFloor FOR REPORT 'Monthly Sales'
    WHEN VISUAL Revenue >= 1000
    WITH (DESCRIPTION = 'Monthly sales revenue floor');

ALTER ALERT RevenueFloor ADD NOTIFICATION orchestrator.OpsEmail;
DISABLE ALERT RevenueFloor;

SELECT * INTO #alerts FROM eng.alerts('Monthly Sales');
DROP ALERT IF EXISTS RevenueFloor;
```

---

## Effective Permissions

Admins can inspect resolved portal access without mentally joining users, groups, folders, reports, and ACL rows:

| Endpoint | Purpose |
| :--- | :--- |
| `GET /api/admin/permissions/effective/user/{userId}` | Lists the folders and reports a user can access, including the group source for each effective permission. |
| `GET /api/admin/permissions/effective/folder/{folderId}` | Lists users with effective access to a folder. |
| `GET /api/admin/permissions/effective/report/{reportId}` | Lists users with effective access to a report through its folder ACLs. |

Reports inherit folder permissions. If a user belongs to multiple groups, the highest permission wins (`Read < Execute < Manage`) and the response lists the group or groups that supplied that winning level.

```sql
SELECT * INTO #user_effective FROM eng.effective_permissions('USER', 'john.doe');
SELECT * INTO #report_effective FROM eng.effective_permissions('REPORT', 'Monthly Sales');
SELECT * INTO #folder_effective FROM eng.effective_permissions('FOLDER', '/Finance');
```

---

## Usage Metrics

Query usage and operational metrics for monitoring and capacity planning:

```sql
-- 30-day usage summary (views, unique viewers, refresh stats)
SELECT * INTO #usage FROM eng.usage_metrics(30);

-- Live operational snapshot (active executions, queue depth, memory, storage)
SELECT * INTO #ops FROM eng.operational_metrics;
```

Or via REST:
- `GET /api/admin/metrics/usage?days=30`
- `GET /api/admin/metrics/operational`
- `GET /metrics` — Prometheus text format (stable low-cardinality labels)

The Portal also emits `System.Diagnostics.ActivitySource` spans from `ETL-SQL.Portal` and first-class `System.Diagnostics.Metrics` instruments for OpenTelemetry collectors.

---

## Report Dependencies

Inspect a report's dependency graph before making changes to upstream tables, scripts, datasets, or report definitions:

```sql
SELECT * INTO #dependencies FROM eng.report_dependencies('Monthly Sales');
```

Or via REST: `GET /api/reports/{id}/dependencies`

The response is permission-aware and includes: report identity, latest snapshot metadata, datasets found in the snapshot manifest, report-owned registered datasets, dataset refresh jobs, and source table references.

---

## Catalog Search and Favorites

```sql
-- Search visible folders and reports by keyword
SELECT * INTO #catalog FROM eng.catalog_search('sales', 25);

-- Recently viewed reports (permission-aware)
SELECT * INTO #recent FROM eng.recent_reports(20);

-- Manage favorites
FAVORITE REPORT 'Monthly Sales';
FAVORITE REPORT 'Monthly Sales' FOR USER 'john.doe';
UNFAVORITE REPORT 'Monthly Sales' FOR USER 'john.doe';
SELECT * INTO #favorites FROM eng.favorites('john.doe');
```

Via REST: `GET /api/catalog/search?q=<term>` and `GET /api/catalog/recent?limit=20`

Search is permission-aware: admins search the full catalog; other users see only folders granted through group ACLs and reports inside those folders.

---

## Related

- [Publishing Reports](publishing.md) — overview hub
- [Report Publishing Workflows](report-publishing-workflows.md) — publishing procedures and metadata tags
- [Report Versioning and Promotion](report-versioning-and-promotion.md) — dataset key rotation and upgrades
- [Portal Administration](README.md)
