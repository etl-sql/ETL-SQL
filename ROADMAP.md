# ETL-SQL Product Roadmap

This document tracks future product tracks and candidate phases. When development begins, the next
actionable phase is moved to `TODO.md`. Shipped work belongs in `CHANGELOG.md`.

The enterprise operating model, authority hierarchy, trust boundaries, and progressive deployment
promise are defined in
[`docs/architecture/roadmaps/Enterprise_Platform_Strategy.md`](docs/architecture/roadmaps/Enterprise_Platform_Strategy.md).

---

## Review Workflow & Data Stewardship

*Combines steward-facing governance, metadata ownership, and impact analysis.*

Strategy: [`docs/architecture/roadmaps/Data_Stewardship_Strategy.md`](docs/architecture/roadmaps/Data_Stewardship_Strategy.md)

### Future Candidate Phases

- [ ] **Phase 1: Stewardship Catalog**
  - Define governed tag metadata, validation, required scopes, aliases, and deprecation rules.
  - Add queries and documentation for missing owner, steward, contact, classification, and quality
    metadata.
- [ ] **Phase 2: Portal Stewardship Views**
  - Add searchable tag catalog, sensitive-data inventory, missing-owner views, stale-lineage views, and
    per-steward queues.
- [ ] **Phase 3: Impact Analysis**
  - Surface upstream and downstream impact for tables, columns, jobs, scripts, datasets, reports,
    subscriptions, owners, and stewards.

---

## Future Candidate Phases

The v0.17.0 implementation work has been promoted to `TODO.md`. Add new long-range candidate phases
here after they are intentionally deferred beyond the active release.
