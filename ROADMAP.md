# ETL-SQL Product Roadmap

This document tracks work that remains outside the active sprint. When development begins, move the
next actionable phase into `TODO.md`. Shipped work belongs in `CHANGELOG.md`.

The enterprise operating model, authority hierarchy, trust boundaries, and progressive deployment
promise are defined in
[`Docs/Strategy/Enterprise_Platform_Strategy.md`](Docs/Strategy/Enterprise_Platform_Strategy.md).

---

## Review Workflow & Data Stewardship

*Combines steward-facing governance with four-eyes review, certification, impact analysis, and
tag-driven policy enforcement. These capabilities are expected to be developed in the same sprint.*

Strategy: [`Docs/Strategy/Data_Stewardship_Strategy.md`](Docs/Strategy/Data_Stewardship_Strategy.md)

### Candidate phases

- [ ] **Phase 1: Stewardship Catalog**
  - Define governed tag metadata, validation, required scopes, aliases, and deprecation rules.
  - Add queries and documentation for missing owner, steward, contact, classification, and quality metadata.
- [ ] **Phase 2: Portal Stewardship Views**
  - Add searchable tag catalog, sensitive-data inventory, missing-owner views, stale-lineage views, and per-steward queues.
- [ ] **Phase 3: Review, Approval & Certification Workflow**
  - Add review and certification state for datasets, reports, jobs, and key lineage targets.
  - Require four-eyes approval for configured critical actions, including report publication and production job changes.
  - Enforce segregation of duties so users cannot approve their own changes.
  - Re-evaluate pending and approved requests when permissions, ownership, or user status changes.
  - Audit requests, comments, decisions, certification changes, and rejections while keeping export/import script-first.
- [ ] **Phase 4: Tag-Driven Policy Enforcement**
  - Extend Governance Core to block or warn based on lineage tags and classification metadata.
- [ ] **Phase 5: Impact Analysis**
  - Surface upstream and downstream impact for tables, columns, jobs, scripts, datasets, reports, subscriptions, owners, and stewards.
- [ ] **Phase 6: Quality & Freshness Stewardship**
  - Tie `EXPECT` and validation outcomes, freshness, SLAs, and quality trends to lineage targets.
- [ ] **Phase 7: External Catalog Sync**
  - Add stable external IDs, conflict rules, and reconciliation reports for external catalog integration.

---

## Debugger & Interactive Troubleshooting

*Adds a script debugger for ETL-SQL pipelines without compromising script-first execution or
zero-trust safety.*

### Candidate phases

- [ ] **Phase 1: Debug Protocol & Execution Hooks**
  - Define breakpoints, step over, step into, step out, pause, resume, cancellation, and variable or temp-table inspection contracts.
  - Ensure debug hooks do not change normal execution semantics.
- [ ] **Phase 2: CLI/TUI Debug Experience**
  - Add local debugging with breakpoints, current-statement context, variables, temp tables, and recent lineage entries.
- [ ] **Phase 3: VS Code Debug Adapter**
  - Add a VS Code debug adapter configuration that uses the same engine debug protocol.
- [ ] **Phase 4: Portal/Orchestrator Guardrails**
  - Define whether scheduled or Portal jobs can be debugged, who can attach, what is redacted, and how sessions are audited.
