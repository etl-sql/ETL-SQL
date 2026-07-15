# Data Stewardship & Lineage Governance Strategy

**Status:** Backlog strategy
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
- Governance Core: organization policy, named secrets, and durable audit outbox.
- OpenLineage export for external catalog integration.

---

## Obvious Gaps

1. **Stewardship workflow:** Tags identify owners and stewards, but there is no workflow for certification, review, assignment, or stale ownership cleanup.
2. **Tag-driven policy enforcement:** Governance Core does not yet enforce rules directly from lineage tags, such as blocking unrestricted export of `@pii=true` data.
3. **Impact analysis:** The lineage graph exists, but downstream impact needs to be a normal pre-publish and pre-change workflow.
4. **Portal visibility:** Administrators need dashboards for missing owners, sensitive data inventory, stale lineage, uncertified assets, and steward-owned review queues.
5. **Quality integration:** Validation/`EXPECT` results should feed stewardship status, freshness, and quality history.
6. **External catalog lifecycle:** Export exists, but long-running bidirectional catalog synchronization needs stable IDs, conflict rules, and reconciliation reports.

---

## Sprint Candidate: Data Stewardship Core

### Phase 1 - Stewardship Catalog

- Define a governed tag catalog with type, allowed values, aliases, required scopes, and deprecation metadata.
- Add lint/runtime validation for standard stewardship tags while preserving an escape hatch for custom organization tags.
- Add catalog queries for missing owner/steward/contact/classification/quality metadata.
- Document the administrator posture in `Administrators_Guide.md` and the script posture in `Docs/Reference/Lineage.md`.

### Phase 2 - Portal Stewardship Views

- Add searchable tag and lineage inventory views for administrators and stewards.
- Add dashboards for PII/restricted inventory, missing owner/steward, stale lineage, uncertified datasets/reports, and quality/freshness breaches.
- Add per-steward work queues filtered by `@steward`, `@domain`, and group membership.
- Keep all views backed by existing lineage/tag APIs where possible; add APIs only for missing query shapes.

### Phase 3 - Certification & Review Workflow

- Add certification states for datasets, reports, and key lineage targets: draft, reviewed, certified, deprecated, stale.
- Require review for sensitive tag changes, restricted classifications, and promotion to `@quality=gold`.
- Record certification, review comments, and state changes in the Governance audit trail.
- Ensure certification can be exported/imported as script-first metadata for source control and environment promotion.

### Phase 4 - Tag-Driven Policy Enforcement

- Extend Governance Core policy rules to evaluate lineage/tag metadata.
- Support policies such as:
  - block export of `@pii=true` unless the destination is encrypted or approved;
  - require `@owner` and `@steward` on restricted outputs;
  - block publishing uncategorized sensitive datasets;
  - require certified upstream lineage for production reports.
- Evaluate policy at lint/publish time and again at execution time where runtime lineage changes the decision.

### Phase 5 - Impact Analysis

- Add upstream/downstream impact queries by table, column, dataset, report, job, script, and tag.
- Show affected owners, stewards, reports, subscriptions, schedules, and published datasets before destructive or schema-changing actions.
- Add pre-publish impact summaries to Portal and script validation output.
- Add notification hooks for stewards when upstream lineage changes.

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
