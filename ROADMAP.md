# ETL-SQL™ Product Roadmap

This document tracks the product backlog, sprint candidates, and release gates for ETL-SQL. It is intentionally backlog-first: version numbers describe shipped or target release packaging, not the priority order for every future feature.

When development begins on a backlog item, its next actionable phase is moved into `TODO.md` to be tracked as active sprint work.

The enterprise operating model, authority hierarchy, trust boundaries, and progressive deployment promise are defined in
[`Docs/Strategy/Enterprise_Platform_Strategy.md`](Docs/Strategy/Enterprise_Platform_Strategy.md).

---

## Product Direction

ETL-SQL grows through a progressive deployment model, optimized for a single maintainer and small operational teams. The current backlog is organized around product capabilities:


See `CHANGELOG.md` for the exact version where shipped work is packaged.

---

## Architectural Gaps to Address

---

## Backlog Operating Model

- `TODO.md` is the active sprint board.
- `ROADMAP.md` is the product backlog and release-gate tracker.
- Strategy documents under `Docs/Strategy/` hold the deeper rationale, scope, non-goals, and acceptance criteria for larger backlog items.
- A backlog item can be promoted into the current sprint when its acceptance criteria are clear enough to implement and test.
- A version can include multiple backlog items, part of a backlog item, or mostly stabilization work.

---

## Current Sprint Candidate

No active sprint is selected. Promote the next actionable backlog phase into `TODO.md` when ready.

---

## Product Backlog

### Enterprise Identity Follow-ons
*Builds on the shipped certified OIDC authentication path with non-interactive identities and approval workflows.*

#### Candidate phases:
- [ ] **Phase 2: Service Accounts**
  - Implement non-interactive service account identities for scheduled CLI jobs and API access.
  - Assign explicit OAuth scopes and rotation patterns.
- [ ] **Phase 3: Approval Workflows (Four-Eyes)**
  - Implement approval requests for critical actions (publishing reports, modifying production scheduled jobs).
  - Enforce segregation of duties (a user cannot approve their own changes).
  - Automatically re-evaluate and cancel pending/approved items if permissions or user status changes.
  - Record all approval requests, comments, grants, and rejections in the Governance audit trail.

### Data Stewardship & Lineage Governance
*Turns captured lineage and tags into steward-facing workflow, certification, impact analysis, and tag-driven policy enforcement.*

Strategy: [`Docs/Strategy/Data_Stewardship_Strategy.md`](Docs/Strategy/Data_Stewardship_Strategy.md)

#### Candidate phases:
- [ ] **Phase 1: Stewardship Catalog**
  - Define governed tag metadata, validation, required scopes, aliases, and deprecation rules.
  - Add queries and documentation for missing owner/steward/contact/classification/quality metadata.
- [ ] **Phase 2: Portal Stewardship Views**
  - Add searchable tag catalog, sensitive-data inventory, missing-owner views, stale lineage views, and per-steward queues.
- [ ] **Phase 3: Certification & Review Workflow**
  - Add review/certification state for datasets, reports, and key lineage targets.
  - Audit certification decisions and keep export/import script-first.
- [ ] **Phase 4: Tag-Driven Policy Enforcement**
  - Extend Governance Core to block or warn based on lineage tags and classification metadata.
- [ ] **Phase 5: Impact Analysis**
  - Surface upstream/downstream impact for tables, columns, jobs, scripts, datasets, reports, subscriptions, owners, and stewards.
- [ ] **Phase 6: Quality & Freshness Stewardship**
  - Tie `EXPECT`/validation outcomes, freshness, SLA, and quality trends to lineage targets.
- [ ] **Phase 7: External Catalog Sync**
  - Add stable external IDs, conflict rules, and reconciliation reports for external catalog integration.

### Debugger & Interactive Troubleshooting
*Adds a script debugger for ETL-SQL pipelines without compromising script-first execution or zero-trust safety.*

#### Candidate phases:
- [ ] **Phase 1: Debug Protocol & Execution Hooks**
  - Define breakpoints, step over/into/out, pause/resume, cancellation, and variable/temp-table inspection contracts.
  - Ensure debug hooks do not change normal execution semantics.
- [ ] **Phase 2: CLI/TUI Debug Experience**
  - Add a local debugging flow for scripts with breakpoints, current statement context, variables, temp tables, and recent lineage entries.
- [ ] **Phase 3: VS Code Debug Adapter**
  - Add a VS Code debug adapter configuration that uses the same engine debug protocol.
- [ ] **Phase 4: Portal/Orchestrator Guardrails**
  - Define whether scheduled/Portal jobs can be debugged, who can attach, what is redacted, and how sessions are audited.

---

## Demand-Driven Extensions (Unscheduled)

These features are technically feasible but will only be scheduled based on customer demand post-v1.0.0:
* **MSSQL State Provider:** Microsoft SQL Server support for Portal/Orchestrator state.
* **S3-Compatible Artifact Storage:** Support for AWS S3, Google Cloud Storage, or MinIO object storage.
* **Shared Multitenancy:** Tenant columns in database tables (only if departmental isolation is insufficient).
* **Advanced Key Management:** Envelope encryption, HSM integrations, or AWS KMS / Azure Key Vault adapters.
