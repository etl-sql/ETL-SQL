# Data Stewardship & Lineage Governance Strategy

**Status:** v0.17.0 core shipped; later review, quality, and external-catalog lifecycle phases remain candidate work
**Date:** 2026-06-19
**Scope:** Product work that turns ETL-SQL lineage and tags from captured metadata into governed, visible, policy-aware stewardship workflows.

---

## Why This Matters

Lineage and tags are already a major ETL-SQL differentiator: scripts can carry ownership, classification, quality, source, and transformation context directly with the work being executed. The remaining gap is operational stewardship. Administrators and data owners need a first-class way to find unowned data, certify trusted assets, review sensitive tag changes, understand impact before publishing or changing pipelines, and enforce policy from the metadata ETL-SQL already captures.

This strategy keeps the core promise script-first and source-control friendly. Governance metadata should remain expressible in `.etlsql` / `.rptsql`, reviewable in Git, queryable in scripts, and visible in the Portal. Portal workflows should operationalize stewardship, not replace scripts as the system of record.

---

## Current Foundation

Already available:

- Column-level and table-level lineage capture during execution.
- Script, table, and column tags with metadata inheritance through transformations.
- Standard stewardship tags such as `@owner`, `@steward`, `@contact`, `@domain`, `@quality`, `@classification`, `@pii`, `@phi`, `@pci`, and `@sensitive`.
- Cross-run lineage history through `ILineageCatalogStore`, backed by SQLite for single-node deployments and PostgreSQL in HA deployments.
- Portal lineage/dependency surfaces and catalog APIs.
- Portal stewardship and impact-analysis views backed by `/api/catalog/stewardship` and
  `/api/catalog/impact`.
- Governance Core: organization policy, named secrets, and durable audit outbox.
- OpenLineage export for external catalog integration.

---

## Remaining Gaps

1. **Certification workflow:** Tags identify owners and stewards, and v0.17.0 surfaces inventory,
   impact, and policy gates, but explicit certification/review state transitions for datasets,
   reports, and key lineage targets remain future work.
2. **Impact workflow hardening:** Impact analysis is available, but workflow-specific approval gates
   and long-running catalog drift workflows remain.
3. **Quality integration:** Validation/`EXPECT` results should feed stewardship status, freshness,
   and quality history.
4. **External catalog lifecycle:** Export exists, but long-running bidirectional catalog
   synchronization needs stable IDs, conflict rules, and reconciliation reports.

---

## Shipped Core: Data Stewardship

### Phase 1 - Stewardship Catalog

Shipped in v0.17.0:

- A governed tag catalog defines type, allowed values, aliases, required scopes, and deprecation metadata.
- Lint and `INSERT/UPDATE TAG` runtime validation use the standard catalog while preserving `org_`, `x_`, and
  `custom_` organization tags.
- `SHOW LINEAGE HISTORY FOR MISSING TAGS` queries missing owner/steward/contact/classification/quality metadata.
- Administrator and script-first usage is documented in the platform and lineage references.

### Phase 2 - Portal Stewardship Views

Shipped in v0.17.0:

- The Portal Lineage catalog includes a Stewardship mode with searchable lineage/tag inventory.
- Sensitive and restricted inventory, missing metadata, stale lineage, and steward queue views are
  backed by `/api/catalog/stewardship`.
- The stewardship API summarizes total, sensitive, missing-metadata, stale, and queue assets and
  returns steward/domain/classification/quality facets.
- Stale-lineage posture uses `@freshness` when present and otherwise falls back to a configurable
  stale-after-days window.

### Phase 3 - Impact Analysis

Shipped in v0.17.0:

- `/api/catalog/impact` supports upstream, downstream, and bidirectional impact analysis for tables,
  columns, jobs, scripts, datasets, reports, subscriptions, owners, and stewards.
- Portal Lineage catalog includes an Impact mode that summarizes affected tables, columns, reports,
  datasets, subscriptions, jobs, owners, and stewards.
- Report validation includes pre-publish impact summaries for source tables detected in valid
  `.rptsql` scripts.
- Report execution and persisted ad hoc interaction lineage emit `STEWARD_LINEAGE_IMPACT` audit and
  audit-outbox events for affected stewards.
- Tests cover Portal/API impact analysis, report and dataset joins, private dataset authorization,
  cycle-safe traversal, pre-publish validation summaries, steward audit hooks, and the UI sandbox
  Impact fixture.
- Publisher and administrator usage is documented in
  [Data Stewardship and Impact Analysis](../../guides/data-stewardship-impact.md).

### Phase 4 - Certification & Review Workflow

- Add certification states for datasets, reports, and key lineage targets: draft, reviewed, certified, deprecated, stale.
- Require review for sensitive tag changes, restricted classifications, and promotion to `@quality=gold`.
- Record certification, review comments, and state changes in the Governance audit trail.
- Ensure certification can be exported/imported as script-first metadata for source control and environment promotion.

### Phase 5 - Tag-Driven Policy Enforcement

- Shipped in v0.17.0: Governance Core policy rules evaluate lineage/tag metadata for lint, publish,
  and execution gates.
- Supported policies include:
  - block export of `@pii=true` unless the destination is encrypted or approved;
  - require `@owner` and `@steward` on restricted outputs;
  - block publishing uncategorized sensitive datasets;
  - require certified upstream lineage for production reports.
- Policies are evaluated at lint/publish time and again at execution time where runtime lineage
  changes the decision.

### Phase 6 - Quality & Freshness Stewardship

- Persist validation and `EXPECT` outcomes as quality evidence tied to lineage targets.
- Track freshness/SLA status using `@freshness`, `@sla`, run history, and subscription/job results.
- Surface quality trend, last certified run, last failed validation, and stale status in Portal.

### Phase 7 - External Catalog Sync

- Define stable external IDs for lineage targets, reports, datasets, and stewardship states.
- Add reconciliation reports for DataHub/Collibra/Alation/OpenLineage-compatible catalogs.
- Define conflict rules for external edits versus script-first metadata.
- Keep sync failures visible and auditable; do not silently rewrite local source-controlled metadata.

---

## Acceptance Criteria

- An administrator can answer: "Which restricted or PII-bearing assets are missing an owner, steward, contact, certification, or quality classification?"
- A steward can answer: "What assets am I responsible for, what changed upstream, and what needs review?"
- A publisher can see downstream impact before publishing or changing a script/report/dataset.
- Governance policy can block or warn based on tags and lineage, with audit records explaining the decision.
- Source-controlled scripts remain the canonical way to define durable pipeline metadata.

---

## Non-Goals

- Building a full replacement for Collibra, Alation, DataHub, or other enterprise catalogs.
- Making Portal-only metadata the source of truth for pipelines.
- Introducing shared-table multitenancy as part of stewardship.
- Blocking all custom tags; organizations must still be able to define local metadata where the standard catalog is insufficient.
