# ETL-SQL Product Roadmap

This document tracks future product tracks and candidate phases. When development begins, the next actionable phase is moved to `TODO.md`. Shipped work belongs in `CHANGELOG.md`.

The enterprise operating model, authority hierarchy, trust boundaries, and progressive deployment promise are defined in [`Docs/Strategy/Enterprise_Platform_Strategy.md`](Docs/Strategy/Enterprise_Platform_Strategy.md).

---

## Enterprise Policy Enforcement & Monitoring

*Completes the enterprise controls for protected enrollment and authoritative client runtime. Standalone installations remain unenrolled, unrestricted by organization policy, and independent of network services.*

### Shipped Scope
- **Policy Authority & Operation-Boundary Enforcement:** Administrator policy-authority API and Portal workflow for validating, versioning, publishing, superseding, activating, and rolling back signed organization policies.
- **Shared Runtime Enforcement:** Filesystem path traversal checks, network/connector destination rules, and process resource boundaries.

### Future Candidate Phases

#### Phase 1: Central Security Events
- [ ] **Event contract and emission:** Define structured security-event schema with actor, host, tenant, script/job/correlation IDs, policy hash, decision, and reason.
- [ ] **Durable delivery and monitoring:** Provide a durable local security-event outbox with bounded storage, delivering events to a SIEM collector using machine identity.

#### Phase 2: Operations Control Plane
- [ ] **Central fleet management:** Expand fleet inventory to include node/environment identity, versions, heartbeats, and compliance.
- [ ] **Upgrade orchestration and compatibility:** Define and automate rolling-upgrade sequences, readiness checks, and compatibility gates.
- [ ] **Standard observability export:** Add first-class OpenTelemetry metrics and traces.
- [ ] **Searchable Portal Documentation Hub:** Compile markdown manuals into a searchable static website hosted natively inside the Report Portal.

---

## Adaptive Execution & Extended Large-Data Certification

*Improves streaming scan, filter, projection, low-cardinality aggregation, and spill-backed `#temp` staging efficiency and concurrency under bounded-memory behavior.*

### Shipped Scope
- **Allocation Budgets:** Budgeting memory and garbage collection targets at scale (10M / 50M rows and 1B scale certification).
- **Adaptive Execution Controller:** Adaptive worker admission, concurrency caps, batch/memory grant setpoints, and spill writes.

---

## Shared Connection & Secret Governance

*Features per-connection use ACLs, connection/secret impact inventories, and sensitive metadata classification.*

### Shipped Scope
- **Connection Governance:** Shipped per-connection use ACLs to authorize which users/processes can request a connection from the catalog.
- **Impact Inventory:** Added dependency/impact inventories for shared connections and secrets to trace usages before deletion or rotation.
- **Sensitive Metadata:** Added organization-designated sensitive metadata controls.

### Future Candidate Phases
- [ ] **Catalog approval workflow (optional):** Propose-and-approve workflow on shared connection creation/update/deletion for organizations that need four-eyes control.

---

## Review Workflow & Data Stewardship

*Combines steward-facing governance with four-eyes review, certification, impact analysis, and tag-driven policy enforcement.*

Strategy: [`Docs/Strategy/Data_Stewardship_Strategy.md`](Docs/Strategy/Data_Stewardship_Strategy.md)

### Future Candidate Phases

- [ ] **Phase 1: Stewardship Catalog**
  - Define governed tag metadata, validation, required scopes, aliases, and deprecation rules.
- [ ] **Phase 2: Portal Stewardship Views**
  - Add searchable tag catalog, sensitive-data inventory, missing-owner views, and stale-lineage views.
- [ ] **Phase 3: Review, Approval & Certification Workflow**
  - Add review and certification state for datasets, reports, jobs, and key lineage targets.
  - Require four-eyes approval for configured critical actions.
- [ ] **Phase 4: Tag-Driven Policy Enforcement**
  - Extend Governance Core to block or warn based on lineage tags and classification metadata.
- [ ] **Phase 5: Impact Analysis**
  - Surface upstream and downstream impact for tables, columns, jobs, scripts, datasets, reports, and owners.

---

## Debugger & Interactive Troubleshooting

*Adds a script debugger for ETL-SQL pipelines without compromising script-first execution or zero-trust safety.*

### Future Candidate Phases

- [ ] **Phase 1: Debug Protocol & Execution Hooks**
  - Define breakpoints, step over, step into, step out, pause, resume, and variable/temp-table inspection contracts.
- [ ] **Phase 2: CLI/TUI Debug Experience**
  - Add local debugging with breakpoints, current-statement context, variables, temp tables, and recent lineage entries.
- [ ] **Phase 3: VS Code Debug Adapter**
  - Add a VS Code debug adapter configuration that uses the same engine debug protocol.
- [ ] **Phase 4: Portal/Orchestrator Guardrails**
  - Define whether scheduled or Portal jobs can be debugged, attachment rules, and audits.
