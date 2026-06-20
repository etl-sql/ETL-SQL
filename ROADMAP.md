# ETL-SQL™ Product Roadmap

This document tracks the product backlog, sprint candidates, and release gates for ETL-SQL. It is intentionally backlog-first: version numbers describe shipped or target release packaging, not the priority order for every future feature.

When development begins on a backlog item, its next actionable phase is moved into `TODO.md` to be tracked as active sprint work.

The enterprise operating model, authority hierarchy, trust boundaries, and progressive deployment promise are defined in
[`Docs/Strategy/Enterprise_Platform_Strategy.md`](Docs/Strategy/Enterprise_Platform_Strategy.md).

---

## Product Direction

ETL-SQL grows through a progressive deployment model, optimized for a single maintainer and small operational teams. The current backlog is organized around product capabilities:

1. **Completed foundations:** multi-user standalone hardening, script-first recovery, operator tooling, Practical HA, Governance Core, and Departmental Isolation.
2. **Current sprint candidate:** none selected.
3. **High-value backlog candidates:** Data Stewardship & Lineage Governance, Debugger & Interactive Troubleshooting, and Enterprise Identity follow-ons.
4. **v1.0.0 release gate:** stabilization, language freeze, distribution trust, and production release verification.

See `CHANGELOG.md` for the exact version where shipped work is packaged.

---

## Architectural Gaps to Address

1. **State Isolation:** *(addressed in v0.12.0)* Portal and Orchestrator state are provider-selectable and support shared PostgreSQL.
2. **Database Hardcoding:** *(addressed in v0.12.0)* Portal database selection supports SQLite/PostgreSQL and SQLite-to-PostgreSQL migration.
3. **Storage Boundary:** *(partially addressed in v0.12.0)* Shared artifact storage, guardrails, and snapshot writes are provider-backed; dataset cache path migration remains a demand-driven hardening follow-on.
4. **Local Limits:** Caches, interactive sessions, and rate-limiting are process-local.
5. **Lease Fencing:** *(addressed in v0.12.0)* Job completion and shared artifact writes use fencing tokens/write epochs.
6. **Tooling Runbooks:** Backup, restore, and upgrade rely on manual runbooks rather than CLI commands.
7. **Secrets Handling:** SMTP, JWT, connectors, and scripts handle secrets in separate, fragmented ways.
8. **Audit Outbox:** Auditing lacks a transactional outbox to guarantee delivery to remote SIEMs.
9. **OIDC Drift:** *(addressed)* OIDC documentation, runtime implementation, diagnostics, and recovery tests are now aligned.
10. **Deployment Schema Updates:** Rolling upgrades lack mixed-version compatibility boundaries.

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

## Shipped Work & Release Gates

### Shipped Phase: Departmental Isolation
*Supports multiple isolated environments (dev/test/prod, or different departments) without the complexity of shared-table multitenancy.*

#### TODOs:
- [x] **Phase 1: Repeatable Deployment Templates**
  - Defined the isolated-environment topology for single-node and HA deployments, including per-environment Portal database, Orchestrator database, artifact root, Data Protection key ring, service identity, network boundary, and encryption keys.
  - Added Docker Compose, Windows Service, and systemd deployment templates with isolated ports, storage roots, service identities, configuration, logs, and key material.
  - Added isolation verification scripts and a runbook to prove environments do not share databases, artifact storage, logs, keys, service accounts, ports, or encryption keys.
- [x] **Phase 2: Environment Portability**
  - Defined the portable environment package format for reports, jobs, folders, permissions, subscriptions, datasets, alerts, and config metadata.
  - Added export/import support with dry-run validation, deterministic idempotency, and logical identity preservation across environments.
  - Strip or externalize environment-specific secrets during export, emitting named-secret requirements instead of credential values.
  - Added promotion tests covering dev-to-prod movement, secret rebinding, script-root rebinding, orchestrator alias rebinding, and idempotent re-promotion.
- [x] **Phase 3: Fleet Aggregation**
  - Defined the fleet aggregator trust boundary as read-only scoped access to each environment, with no script execution, no writes, and no raw data blending.
  - Added read-only fleet health aggregation for environment status, queue depth, active executions, failed refreshes, audit outbox health, and storage availability.
  - Proved aggregator credential containment: a `FleetReader` token can read only `GET /api/fleet/status` and is denied admin, identity, publish, and execute paths.

### Shipped Phase: Practical High Availability
*Enables horizontal scaling of Portal and Orchestrator nodes behind a load balancer with PostgreSQL state and shared artifact storage.*

#### TODOs:
- [x] **Phase 1: PostgreSQL State Provider**
  - Extract database provider interfaces for the Portal/Orchestrator state.
  - Create EF Core migrations for PostgreSQL.
  - Implement `etl-sql admin migrate-database --from sqlite --to postgres --dry-run` with row verification and cutover checkpoints.
- [x] **Phase 2: Artifact Storage Abstraction**
  - Create a unified storage provider interface for scripts, snapshots, cached datasets, and keys.
  - Implement Local and SMB/UNC shared storage providers.
  - Enforce path-traversal guardrails and script immutability checks at the storage boundary.
- [x] **Phase 3: Distributed Leases & Fencing**
  - Implement database-backed node heartbeats and execution/job leases.
  - Implement monotonically increasing fencing tokens to reject stale writers during network partitions.
  - Add database-backed leader election for running singletons (e.g. database migrations).
- [x] **Phase 4: Stateless Node Operation**
  - Configure Portal nodes to read state and serve snapshots from PostgreSQL.
  - Support load-balancer session affinity for interactive IDE sessions.
  - Ensure a node partition immediately cancels local running jobs if it loses its database lease.
  - Implement a lightweight `/healthz` HTTP endpoint on Portal nodes to check database, storage, and lease connectivity for load-balancer health checks.
  - Heart-beat node capacity (CPU/memory utilization) to prevent overloaded nodes from claiming new leases, and implement a quarantine policy for failing jobs to prevent cascade failures across the cluster.
- [x] **Phase 5: Rolling Deployment Certification**
  - Verify "expand/migrate/contract" database migrations for rolling upgrades.
  - Run Portal and Orchestrator nodes as separate OS processes against PostgreSQL and shared artifact
    storage; prove simultaneous claims, cancellation, permission changes, restart recovery, conflicting
    administration, failover recovery, and task reclamation without relying on in-process proxies.
  - Add deterministic chaos scenarios for process termination between cross-resource steps, database
    and storage unavailability, network partition, disk exhaustion/pressure, and bounded clock skew.
  - Verify lease loss cancels local work and fencing tokens reject every stale writer after a partition.
  - Certify mixed interactive, scheduled, refresh, and subscription workloads under load, including
    per-user/per-group quotas, queue fairness, administrative overrides, and node-capacity-aware claims.

---

### Shipped Phase: Governance Core
*Enforces centralized security policy, named secret references, and remote audit delivery across all hosts (CLI, IDE, Portal, and Orchestrator).*

#### TODOs:
- [x] **Phase 1: Typed Policy Registry**
  - Create a central registry of settings (Forbidden, Allowed, Constrained, Locked).
  - Enforce policy against parsed AST nodes rather than text matching.
  - Apply policy evaluations at compile/lint time and again at execution time.
- [x] **Phase 2: Organization Policy Documents**
  - Implement versioned policy schemas specifying allowed connector types, filesystem roots, and remote executions.
  - Support fetching policies from local OS-protected configurations or HTTPS endpoints.
  - Configure offline cache periods, failing secure (no access) if a policy expires or cannot be validated.
- [x] **Phase 3: Named Secret References**
  - Implement `ISecretProvider` (Environment, OS Secret Store, HTTPS Vault).
  - Support syntax to reference secrets by logical name (`SECRET:sales_db_password`) in connection strings.
  - Block raw secret values from appearing in logs, diagnostics, or dashboards.
- [x] **Phase 4: Durable Audit Outbox**
  - Implement a transactional outbox table for audit events.
  - Create an HTTPS audit transporter supporting batching, retry, deduplication, and backpressure limits.
  - Define policy to fail closed (block mutations) when remote audit delivery is unavailable.
  - Implement disk-size safeguards (rotation/retention thresholds) for the local SQLite outbox queue during extended collector outages.

---

### Shipped Phase: Certified OIDC Authentication
*Aligns OIDC runtime behavior, administrator documentation, diagnostics, claim/group handling, and recovery certification.*

#### TODOs:
- [x] **Phase 1: Certified OIDC Authentication**
  - Reconcile and certify OIDC login, logout, token refresh, and claim validation.
  - Map OIDC group claims dynamically to Portal user groups.
  - Support MFA and conditional access by delegating authentication entirely to the identity provider.
  - Add redacted diagnostics and recovery coverage for unavailable providers, JWKS rotation,
    group-claim changes, disabled accounts, and session revocation.

---

### Shipped Phase: Engine Workflow Enhancements
*Improves incremental loads, staged extract performance, cross-connection joins, and schema contract checks.*

#### TODOs:
- [x] **Job-scoped state persistence / incremental watermarking**
  - Implement `GET_JOB_STATE()` and `SET_JOB_STATE()` with successful-run commit semantics.
  - Persist scheduled-job state in the Orchestrator store and provide a local CLI fallback for
    development runs.
- [x] **Pushdown aggregation for staged extracts**
  - Push eligible grouped/aggregate `SELECT ... INTO #temp` extracts to SQL connectors and stream
    grouped results back into the engine.
- [x] **Cross-connection semi-join pushdown**
  - Push bounded, parameterized key filters for eligible local-temp-to-remote joins with clear
    `EXPLAIN` visibility and safe fallback.
- [x] **JSON/spec-backed schema contract checks**
  - Extend `EXPECT SCHEMA` to load reviewed JSON contracts with `ON DRIFT WARN` support.

---

### Milestone: v1.0.0 — Stable Production Release
*Establishes a stable language contract, unified open-source licensing, distribution trust, and final release gates. Focuses strictly on stabilization and security; no new features.*

#### TODOs:
- [ ] **Phase 1: Language & Manifest Freeze**
  - Publish the canonical language grammar, connector options reference, and standard library docs.
  - Define a strict deprecation policy for syntax and options.
  - Implement script compatibility test corpus and a migration-linter.
  - Implement `SHOW VERSION` and machine-readable compatibility diagnostics.
- [ ] **Phase 2: Licensing & Contribution Policies**
  - Apply the **Apache-2.0 License** consistently across all projects, extension manifests, and installers.
  - Establish the **Developer Certificate of Origin (DCO)** for external code contributions.
- [ ] **Phase 3: Distribution Trust**
  - Automate build workflows to generate SHA-256 checksums and an SBOM (Software Bill of Materials).
  - Retain test and certification reports in public release assets.
  - Implement cache-busting asset fingerprinting (inject hashes into JS/CSS URLs) in the Report Portal to prevent outdated client-side assets after upgrades.
- [ ] **Phase 4: Release Gates**
  - Verify that a clean script-to-scheduled-production workflow completes successfully without manual intervention.
  - Ensure zero credentials leak in logs, bundles, or debug dumps.
  - Reconcile OIDC/LDAP configurations with standard documentation libraries.
  - Implement automatic diagnostic redaction in `etl-sql admin support-bundle` to automatically strip query parameters, private table data, and personal data (PII) before export.

---

## Demand-Driven Extensions (Unscheduled)

These features are technically feasible but will only be scheduled based on customer demand post-v1.0.0:
* **MSSQL State Provider:** Microsoft SQL Server support for Portal/Orchestrator state.
* **S3-Compatible Artifact Storage:** Support for AWS S3, Google Cloud Storage, or MinIO object storage.
* **Shared Multitenancy:** Tenant columns in database tables (only if departmental isolation is insufficient).
* **Advanced Key Management:** Envelope encryption, HSM integrations, or AWS KMS / Azure Key Vault adapters.
