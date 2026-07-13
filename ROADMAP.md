# ETL-SQL Product Roadmap

This document tracks future product tracks and candidate phases. When development begins, the next actionable phase is moved to `TODO.md`. Shipped work belongs in `CHANGELOG.md`.

The enterprise operating model, authority hierarchy, trust boundaries, and progressive deployment promise are defined in [`Docs/Strategy/Enterprise_Platform_Strategy.md`](Docs/Strategy/Enterprise_Platform_Strategy.md).

---

## Enterprise Policy Enforcement & Monitoring

*Completes the enterprise controls for protected enrollment and authoritative client runtime. Standalone installations remain unenrolled, unrestricted by organization policy, and independent of network services.*

### Shipped Scope
- **Machine Enrollment:** Machine-level enrollment, protected bootstrap, trust key, machine identity, and enroll/status/unenroll CLI.
- **Signed Policy Retrieval:** Tenant-bound RSA-PSS signed policy retrieval, protected cache, rollback/expiry checks, final configuration precedence, diagnostics, dynamic reload, and fail-closed host refresh.
- **Policy Authority & Operation-Boundary Enforcement:** Administrator policy-authority API and Portal workflow for validating, versioning, publishing, superseding, activating, and rolling back signed organization policies.
- **Shared Runtime Enforcement:** Filesystem path traversal checks, network/connector destination rules, and process resource boundaries.

### Future Candidate Phases

#### Phase 1: Policy Hardening
- [x] ~~Complete handle-based or equivalent race-resistant `DELETE`, `MOVE`, and `RENAME` operations on supported platforms; add link/junction substitution tests at each mutation boundary.~~ Shipped (`FileHandleFinalPath` + `FileSystemPolicyAuthorizer` handle-based re-check; substitution tests). Residual close-out tracked in `TODO.md`.
- [x] ~~Extend connect-time DNS re-pin, redirect re-authorization, and proxy-bypass controls beyond the REST connector to every policy-governed outbound HTTP/network client, including SharePoint, Report Portal, Orchestrator, remote policy/vault access, discovery, and probe paths.~~ Shipped (`PolicyBoundHttp` adopted across those clients). Remaining client audit tracked in `TODO.md`.
- [x] ~~Add a Portal administrator UI for policy validation, version history, staged publication, activation, rollback, machine revocation, and signing-key status on top of the shipped authority API.~~ Shipped (`policy-authority-admin.js` + `PolicyAuthorityController`).
- [x] **Canary Policy Rollout:** Shipped in v0.16.0 (`feat/canary-policy-rollout`). Percentage-of-fleet or named-group cohorts served alongside the fleet active version, with promote/halt (halt re-issues the active document so the cohort reverts), admin API + Portal UI, and audit. Progressive rollouts let administrators validate new filesystem-path and connection restrictions on a subset before deploying fleet-wide.

#### Phase 3: Certification & Operations

##### Deployment and recovery
- [ ] Document policy-authority deployment, signing-key custody/rotation, machine enrollment/revocation, service-identity permissions, staged rollout, emergency policy publication, and unenrollment governance.
- [ ] Document cache and outbox backup/restore rules; restored machines must not duplicate machine identity or silently reuse credentials in another environment.
- [ ] Define upgrade ordering and compatibility across bootstrap, envelope, policy, event, and collector schema versions.
- [ ] Provide outage runbooks for policy authority, certificate expiry, invalid publication, SIEM outage, disk exhaustion, and fail-closed fleet recovery.
- [ ] Add support-bundle diagnostics that expose versions, hashes, timestamps, and health without policy payload values, trust material, credentials, or sensitive event targets.

#### Phase 4: Operations Control Plane

##### 4.1 Central fleet management
- [ ] Expand fleet inventory beyond current health aggregation to include node/environment identity, installed version, schema version, enrollment and policy compliance, last policy refresh, signing/client-certificate expiry, configuration drift, storage provider, database provider, and upgrade readiness.
- [ ] Add fleet search, filtering, grouping, and drill-down without granting the aggregator mutation authority over departmental environments.
- [ ] Define explicit machine/node registration, retirement, duplicate identity, stale heartbeat, and revoked-node behavior.
- [ ] Surface unsupported version combinations, missing required capabilities, unhealthy dependencies, and policy divergence as actionable findings.

##### 4.2 Upgrade orchestration and compatibility
- [ ] Define and automate the supported rolling-upgrade sequence: readiness check, node drain, binary deployment, database migration ownership, compatibility window, health verification, traffic restoration, and rollback decision.
- [ ] Publish machine-readable compatibility metadata for Portal, Orchestrator, engine, database schema, policy/envelope schema, snapshots, plugins/connectors, and collectors.
- [ ] Prevent two nodes from racing to run incompatible migrations; expose migration leader, progress, failure, and recovery state.
- [ ] Add fleet-wide preflight and postflight reports while leaving package deployment to established tools such as Intune, SCCM, Ansible, Kubernetes, or equivalent infrastructure.
- [ ] Certify N-1 rolling compatibility where promised and fail clearly when a deployment exceeds the supported compatibility window.

##### 4.3 Standard observability export
- [ ] Add first-class OpenTelemetry metrics and traces, plus a Prometheus-compatible metrics endpoint where appropriate.
- [ ] Standardize dimensions for environment, node, job, report, dataset, connector, execution mode, status, policy version, and workload class while controlling high-cardinality labels.
- [ ] Export queue depth/age, active and throttled work, execution latency, rows, CPU, memory, GC, spill, connector latency, retries, failures, storage growth, database pool health, policy refresh, audit/security backlog, and delivery health.
- [ ] Correlate metrics and traces with structured logs, audit events, security events, job IDs, script hashes, and request correlation IDs.
- [ ] Keep observability exporters optional and ensure disabled exporters impose negligible standalone overhead.

##### 4.4 Historical capacity planning and sizing
- [ ] Retain or export capacity history sufficient to calculate peak and percentile CPU, memory, queue wait, execution duration, concurrency, spill frequency/bytes, connector latency, retry/failure rates, and dataset/snapshot growth.
- [ ] Add workload breakdowns by environment, node, job, report, dataset, connector, owner, and workload class without exposing sensitive row data.
- [ ] Produce sizing and trend reports that distinguish CPU, memory, storage, connector, database, and concurrency bottlenecks.
- [ ] Add saturation indicators and forecast thresholds so administrators can identify when to scale up, scale out, repartition workloads, or adjust schedules.
- [ ] Document benchmark-to-production sizing guidance and clearly state where measured workload history is required instead of synthetic estimates.

##### 4.5 Alerting and service objectives
- [ ] Define recommended SLIs/SLOs for availability, queue wait, execution success/latency, freshness, policy availability, audit/security delivery, database health, and recovery.
- [ ] Add configurable alerts for queue age/depth, sustained CPU or memory pressure, repeated spills, failed/retried jobs, stale snapshots/datasets, policy/signature failures, certificate expiry, outbox backlog, disk pressure, storage growth, database connectivity/pool exhaustion, and unhealthy fleet nodes.
- [ ] Support alert routing through standard observability systems rather than building a proprietary pager; include deduplication, severity, recovery notifications, and runbook links in emitted signals.
- [ ] Provide baseline thresholds but require administrators to tune them from measured workload and business criticality.

##### 4.6 HA topology and failure certification
- [ ] Publish supported standalone, departmental, and HA reference topologies with exact requirements for PostgreSQL, load balancing, shared artifact storage, certificates, DNS, service supervision, and network trust boundaries.
- [ ] Certify node loss, process crash, network partition, PostgreSQL failover, shared-storage outage, duplicate scheduler leadership, orphaned work, and recovery without duplicate or lost mutations.
- [ ] Document which components ETL-SQL coordinates and which remain responsibilities of PostgreSQL, load balancers, object/file storage, Kubernetes, Windows Services/systemd, or other infrastructure.
- [ ] Add topology-aware health and readiness checks so load balancers remove unsafe nodes without hiding whole-environment failures.

##### 4.7 Disaster recovery objectives
- [ ] Define supported RPO/RTO targets for each reference topology and identify the state, artifact, key, certificate, policy, outbox, and external dependency included in each target.
- [ ] Add scheduled restore drills that verify database consistency, artifact references, encrypted data/key availability, policy enrollment, service accounts, audit/security continuity, and orchestrator recovery.
- [ ] Prevent cloned restores from silently reusing machine identity or client credentials in another environment; require deliberate re-enrollment and credential rotation.
- [ ] Produce a machine-readable recovery report with achieved RPO/RTO, missing dependencies, data loss window, and operator actions.
- [ ] Document regional/site failure, split custody, backup retention, immutable/offline backup, and emergency access procedures.

##### 4.8 Searchable Portal Documentation Hub
- [ ] Compile the repository's markdown document library (cookbooks, reference guides, manuals) into a unified, searchable static website using a static site generator (e.g., MkDocs or Docusaurus).
- [ ] Host the compiled documentation site natively inside the Report Portal (e.g. under a `/docs` route) to allow administrators, analysts, and business users to search and navigate documentation in their web browser.
- [ ] Reconcile the static site's theme and search indices with the Portal's user interface, ensuring sensitive configurations remain excluded from the compiled index.

##### Prioritization gates
- [ ] Rank each workstream using measured administrative pain, customer deployment scale, security impact, and dependency on external infrastructure.
- [ ] Preserve scoped read-only fleet aggregation by default; any future remote mutation or upgrade command requires a separate threat model, authorization design, approval workflow, and audit contract.
- [ ] Complete threat-model and senior security review with all high-severity findings resolved.
- [ ] Pass full functional, performance, migration, recovery, enterprise certification, and standalone regression suites.
- [ ] Confirm documentation never claims OS-level containment against administrators or arbitrary alternate executables; mandate WDAC/AppLocker or equivalent controls where that boundary is required.

#### Phase 5: Report Portal Modularization (Bites)
- [ ] **Module Feature-Flag Configuration:** Implement dynamic configuration toggles (e.g. `Portal:Modules:Reporting = true/false`) in `appsettings.json` to selectively enable or disable functional layers of the Portal binary.
- [ ] **Conditional Route Registration:** Dynamically unregister API controllers and frontend routes for disabled modules (e.g. disabling Visual Reporting completely hides reporting menus and returns 404/403 for `/api/reports` and `/api/designer` endpoints).
- [ ] **Fenced Background Services:** Conditionally disable background workers, schedulers, and node-heartbeat capacities associated with disabled modules to reduce memory footprints and security surface areas.
- [ ] **Multi-Topology Certification:** Certify varied deployment profiles (e.g., a pure "Secret Store & Connection Catalog" gateway node vs. a pure "BI Report Player" viewing node) sharing a single code executable.

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
  - **Quality Gate Attestation:** Expose quality-gate expectation run results directly on visual report cards and designer views in the Report Portal as dynamic "Verified Data" attestation badges.
- [ ] **Phase 7: External Catalog Sync**
  - Add stable external IDs, conflict rules, and reconciliation reports for external catalog integration.

---

## Debugger & Interactive Troubleshooting

*Adds a script debugger for ETL-SQL pipelines without compromising script-first execution or zero-trust safety.*

### Future Candidate Phases

- [ ] **Phase 1: Debug Protocol & Execution Hooks**
  - Define breakpoints, step over, step into, step out, pause, resume, cancellation, and variable or temp-table inspection contracts.
  - Ensure debug hooks do not change normal execution semantics.
- [ ] **Phase 2: CLI/TUI Debug Experience**
  - Add local debugging with breakpoints, current-statement context, variables, temp tables, and recent lineage entries.
- [ ] **Phase 3: VS Code Debug Adapter**
  - Add a VS Code debug adapter configuration that uses the same engine debug protocol.
- [ ] **Phase 4: Portal/Orchestrator Guardrails**
  - Define whether scheduled or Portal jobs can be debugged, who can attach, what is redacted, and how sessions are audited.
