# Data Stewardship and Impact Analysis

> [!TIP]
> This topic has moved to the **[Data Quality & Governance](../data-quality/data-stewardship-and-impact.md)** guide suite. The focused guide there contains additional examples and remediation patterns.

This guide is for administrators, data stewards, report publishers, and CI/CD owners who need to use ETL-SQL lineage metadata before publishing dashboards, changing scripts, or making schema-impacting changes.

> **Applies to:** every deployment profile. Lineage is persisted by whatever runs the script, including the CLI on a workstation — a Portal is not required to use this guide.

## Prerequisites

- **Lineage must be persisted, which happens automatically** — whatever runs the script writes it
  through the configured `ILineageCatalogStore`, including a plain `etl-sql run` on a workstation.
  You do not need to deploy a Portal or an Orchestrator service to use this guide; running scripts
  is enough to populate `eng.protected_data`, `eng.stewardship_score` and the rest.
- Scripts should use durable stewardship tags such as `@owner`, `@steward`, `@contact`, `@domain`, `@classification`, `@quality`, `@pii`, `@phi`, `@pci`, `@sensitive`, and `@freshness`.
- Organization-specific tags should use `org_`, `x_`, or `custom_` prefixes.
- Report publishers need at least read access to the relevant Portal folders to see report and
  subscription impact — **where a Portal is deployed**. The script, dataset and column impact below
  is available without one.

## Script-First Metadata

Stewardship starts in `.etlsql` and `.rptsql` files. Keep ownership and classification close to the transformation or published asset so the metadata is diffable, reviewable, and promoted with the script.

```sql
/* @owner: FinanceOps
   @steward: Maria Chen
   @contact: finance-data@example.com
   @domain: finance
   @classification: restricted
   @quality: gold
   @pii: false
   @freshness: 1d */

SELECT
  order_id,
  customer_id,
  net_amount
INTO #orders_curated
FROM sales.Orders
WHERE order_date >= RELDATE('D-30');
```

Use `INSERT TAG` when metadata is attached to a specific table or column rather than the whole script.

```sql
INSERT TAG FOR TABLE #orders_curated (
  owner = 'FinanceOps',
  steward = 'Maria Chen',
  classification = 'restricted',
  quality = 'gold'
);

INSERT TAG FOR TABLE #orders_curated COLUMN customer_id (
  pii = 'true',
  classification = 'restricted'
);

UPDATE TAG FOR TABLE #orders_curated COLUMN customer_id (
  steward = 'Privacy Review'
);

DELETE TAG FOR TABLE #orders_curated COLUMN customer_id (steward);
INSERT LINEAGE FOR TABLE #orders_curated FROM 'governance/openlineage.json';
DELETE LINEAGE FOR TABLE #orders_curated;
```

`DELETE LINEAGE` removes imported lineage records only. It does not remove lineage captured by
running the script.

## Finding Metadata Gaps

Administrators can query missing stewardship metadata directly from scripts. This is the preferred CI/CD and release-gate pattern because it does not require manual Portal review.

```sql
SELECT * INTO #missing_stewardship
FROM eng.missing_tags
LIMIT 100;

SELECT
  target_table,
  target_column,
  missing_tags,
  job_name,
  script_path
FROM #missing_stewardship;
```

For centralized review against a production Orchestrator or Portal catalog:

```sql
SELECT * INTO #missing_stewardship
FROM prod_orch.eng.missing_tags
LIMIT 500;
```

Treat missing `owner`, `steward`, `contact`, `classification`, or `quality` on published outputs as a release issue unless the asset is intentionally temporary.

## Governance Gates

ETL-SQL enforces tag-driven governance at authoring and publish boundaries:

- `LINT` flags public datasets that are missing `@owner`, `@steward`, `@contact`, `@classification`, or `@quality`.
- `LINT` flags public datasets carrying `@classification=confidential` or `@classification=restricted`.
- `LINT` flags protected `EXPORT DATASET` statements that omit a portable transport credential (`ENCRYPT = PASSWORD` or `ENCRYPT = KEYFILE`).
- Portal `CREATE DATASET` and `PUBLISH DATASET` reject public datasets without complete stewardship metadata.
- Portal `CREATE DATASET` and `PUBLISH DATASET` reject public datasets classified as confidential or restricted.
- `@quality=gold` requires complete stewardship metadata; lint reports this during authoring and Portal runtime rejects incomplete gold dataset publication.

Private datasets can carry protected classifications, but they still need complete stewardship metadata before promotion to `@quality=gold`.

## Finding Protected Data

Query `eng.protected_data` as the first audit step when you need to find where PII, PHI, PCI, sensitive, confidential, or restricted data appears in extracts and reports.

```sql
SELECT * INTO #protected_data
FROM prod_portal.eng.protected_data
LIMIT 500;

SELECT
  target_table,
  target_column,
  protection_tags,
  owner,
  steward,
  job_name,
  source_file
FROM #protected_data;
```

The command reads the lineage catalog and includes rows tagged with truthy `@pii`, `@phi`, `@pci`, or `@sensitive`, plus rows classified as `confidential` or `restricted`.

Use classifier suggestions when you are looking for likely protected data that has not been tagged yet:

```sql
SELECT * INTO #protected_review
FROM prod_portal.eng.protected_data_suggestions
LIMIT 500;

SELECT
  target_table,
  target_column,
  suggested_tag,
  suggested_value,
  confidence,
  evidence_kind,
  evidence,
  reason
FROM #protected_review;
```

Suggestions are review findings only. They are derived from column names, source-column names, catalog metadata hints, and supported sampled-value callers, and they never set `@pii`, `@phi`, `@pci`, `@sensitive`, or `@classification` automatically.

The packaged starter report at `samples/08_Reporting/protected_data_audit.rptsql` builds a dashboard from `eng.protected_data`, `eng.missing_tags`, and optional Portal steward-impact audit events.

## Portal Stewardship Review

Open **Governance → Stewardship** (the durable route `#governance/stewardship`) for the focused
metadata queue. Administrators can open **Governance → Audit Evidence**
(`#governance/audit`) for the combined steward workflow; the Audit route is role-gated because it
joins catalog evidence with Portal audit and operational outbox records.

Audit mode combines:

- **Protected inventory** - lineage tagged as PII, PHI, PCI, sensitive, confidential, or restricted.
- **Classifier suggestions** - likely protected data that needs steward review before tags are added to scripts.
- **Steward queue** - missing metadata, stale assets, and sensitive assets assigned to stewards.
- **Affected reports and datasets** - inferred report, dataset, and subscription dependencies from protected lineage.
- **Recent steward-impact events** - `STEWARD_LINEAGE_IMPACT` audit rows.
- **Audit outbox health** - pending and failed durable audit outbox counts from operational metrics.

Use the available views for:

- **All** - searchable lineage and tag inventory.
- **Missing** - assets missing required stewardship metadata.
- **Sensitive** - assets tagged as PII, PHI, PCI, sensitive, confidential, or restricted.
- **Stale** - assets outside their `@freshness` window or the selected stale-after-days threshold.
- **Queue** - assets assigned to a steward.

The backing API is:

```text
GET /api/catalog/stewardship?view=missing&q=orders&staleAfterDays=30
GET /api/catalog/stewardship?view=queue&steward=Maria%20Chen
GET /api/catalog/stewardship?view=sensitive
GET /api/catalog/protected-data/suggestions?limit=100
```

Use these endpoints for automation when a workflow needs the same posture data shown in Portal.

## Running Impact Analysis

Open the Portal Lineage catalog and switch to Impact mode. Enter a target, select a direction, and run the analysis before changing upstream tables, scripts, datasets, or report definitions.

Supported target types:

- **Table** - a physical, connector-qualified, or temp table name.
- **Column** - a table or table-column target; use the column field when the table name is separate.
- **Job** - an Orchestrator or report refresh job name.
- **Script** - a script path recorded in lineage history.
- **Dataset** - a published dataset name.
- **Report** - a report name or folder-qualified report path.
- **Subscription** - a subscription id or `Subscription #id`.
- **Owner** or **Steward** - tag values assigned in lineage metadata.

The backing API is:

```text
GET /api/catalog/impact?kind=table&name=sales.Orders&direction=downstream&depth=4
GET /api/catalog/impact?kind=report&name=/Finance/Margin%20Dashboard&direction=both&depth=4
GET /api/catalog/impact?kind=steward&name=Maria%20Chen&direction=both&depth=2
```

The response includes summary counts and affected tables, columns, reports, datasets, subscriptions, jobs, owners, and stewards.

## Steward Notification Hooks

When Portal report execution or persisted ad hoc interaction lineage changes affect steward-owned assets, Portal writes `STEWARD_LINEAGE_IMPACT` rows to the durable audit log and audit outbox. The row resource is the steward name, and the detail payload includes the report id, job name, script path, impacted targets, and lineage entry count.

Use the audit log for human review and the audit outbox for integration with external notification delivery, SIEM, ticketing, or governance workflow systems.

Portal administrators can query recent steward-impact audit events from script:

```sql
EXECUTE prod_portal BEGIN
  SELECT * INTO #steward_events FROM eng.audit(100, 'STEWARD_LINEAGE_IMPACT');
END;
```

## Pre-Publish Validation

Report validation includes impact data for valid `.rptsql` files when source tables can be statically read from the script.

```text
POST /api/reports/validate
Content-Type: application/json

{ "scriptPath": "reports/finance/margin-dashboard.rptsql" }
```

For each source table, the response `impact` object summarizes downstream reports, datasets, subscriptions, and jobs. Publishers should review this before accepting a report change that modifies source tables, filters, joins, visual mappings, or published datasets.

## Release Gate Checklist

- Required stewardship tags are present on published outputs.
- Sensitive and restricted assets have an owner, steward, contact, classification, and quality.
- Stale assets have an accepted freshness reason or an active remediation task.
- Impact analysis has been reviewed for changed tables, scripts, datasets, reports, and steward-owned assets.
- High-impact changes have a documented owner or steward acknowledgement in the release record or a `STEWARD_LINEAGE_IMPACT` audit trail.

## References

- [Lineage](../../reference/statements/session-control/lineage.md)
- [Report-SQL Guide](report-sql.md)
- [Portal User Guide](../tooling/portal-user.md)
- [Governance Core](../../administration/platform/governance.md)
- [Data Stewardship Strategy](../../architecture/roadmaps/Data_Stewardship_Strategy.md)
