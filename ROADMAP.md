# ETL-SQL™ Product Roadmap

This document outlines the future release sequence, feature specifications, and implementation phases for the ETL-SQL project.

When development begins on a release, its next actionable phase is moved into `TODO.md` to be tracked as active tasks.

The enterprise operating model, authority hierarchy, trust boundaries, and progressive deployment promise are defined in
[`Docs/Strategy/Enterprise_Platform_Strategy.md`](Docs/Strategy/Enterprise_Platform_Strategy.md).

---

## Product Direction

ETL-SQL grows through a progressive deployment model, optimized for a single maintainer and small operational teams:

1. **v0.11.0 (Active):** Hardening multi-user standalone correctness and script-first recovery.
2. ** Operator Tooling (doctor, backup, restore, support-bundle).
3. ** Practical High Availability (PostgreSQL state and shared storage).
4. ** Introducing Governance Core (Org policy and secure audit outbox).
5. ** Integrating Enterprise Identity (OIDC and approval workflows).
6. ** Enforcing Departmental Isolation (Repeatable isolated container deployments).
7. **v1.0.0 (Target):** Stable Production Release & Release Gates (Stabilization, language freeze, distribution trust).

---

## Architectural Gaps to Address

1. **State Isolation:** Portal uses EF Core/SQLite while Orchestrator uses a separate hand-written SQLite store.
2. **Database Hardcoding:** Portal database selection is hardcoded to SQLite and lacks a migration strategy.
3. **Storage Boundary:** Scripts, cached datasets, and snapshots use direct filesystem paths rather than an abstraction.
4. **Local Limits:** Caches, interactive sessions, and rate-limiting are process-local.
5. **Lease Fencing:** Orchestrator leases lack fencing tokens to reject stale writers.
6. **Tooling Runbooks:** Backup, restore, and upgrade rely on manual runbooks rather than CLI commands.
7. **Secrets Handling:** SMTP, JWT, connectors, and scripts handle secrets in separate, fragmented ways.
8. **Audit Outbox:** Auditing lacks a transactional outbox to guarantee delivery to remote SIEMs.
9. **OIDC Drift:** OIDC documentation and runtime implementation are out of sync.
10. **Deployment Schema Updates:** Rolling upgrades lack mixed-version compatibility boundaries.

---

## Release Sequence & Incremental TODOs

### Active Phase: v0.11.0 — Standalone Hardening & Recovery
*Focuses on multi-user correctness, script-first recovery, and establishing a stable single-server benchmark.*

#### TODOs:
- [x] **Phase 1: Multi-User Correctness**
  - Fix folder and asset ownership lifecycle (resolve `Folder.OwnerId` permission gaps).
  - Make audit recording a transactional operation contract to prevent un-audited state mutations.
- [x] **Phase 2: Script-First Reconstruction**
  - Implement `EXPORT PORTAL CONFIGURATION` to export all users, folders, ACLs, and jobs as a versioned, idempotent `.etlsql` bootstrap script.
  - Exclude all credentials, keys, and cached dataset values from the configuration export (use env/secret placeholders).
  - Implement dry-run/validation behavior (`SET WHAT_IF ON`) during configuration imports.
  - Verify full portal round-trip reconstruction on clean database initialization, including
    reports, dataset metadata/grants, target-aliased refresh jobs, subscriptions, and alerts.
  - Isolate subscription delivery and durable deduplication per normalized recipient.
- [x] **Phase 3: Verification & Observability**
  - Implement independent-service/shared-connection tests to verify SQLite job and delivery
    coordination for the supported single-Portal topology.
  - Add subscription delivery outbox and idempotency tests.
  - Automate a complete backup and restore validation drill.
  - Expose operational metrics (queue depth, active executions, failure rates, dataset/snapshot
    disk usage) via an admin metrics endpoint. **OpenTelemetry export was deliberately deferred** to
    the Practical High Availability release — it was judged not yet warranted for the single-instance
    topology and is better designed alongside multi-node aggregation.

> **v0.11.0 also delivered beyond the minimum scope above:** a hosted-service integration lane,
> fault-injection/recovery tests, workload-fairness (per-user concurrency) caps, and an in-place
> N→N+1 versioned-upgrade drill (which pulls forward the first part of Operator Tooling → Phase 4;
> the `Test-PreRelease.ps1` wiring for that drill remains with Operator Tooling).

---

### Operator Tooling
*Exposes supported operator workflows via CLI commands to move away from manual runbooks.*

#### TODOs:
- [x] **Phase 1: System Diagnostics**
  - Implement `etl-sql admin doctor` for local environment configuration and smoke verification.
  - Implement `etl-sql admin support-bundle` to redact credentials and export system config, health, logs, and database metrics.
  - Implement `etl-sql init` to scaffold a starter configuration, generate a first `.etlsql` script, and print a pointer to the User Manual for new users who prefer CLI onboarding over reading documentation first.
- [x] **Phase 2: Backup and Disaster Recovery**
  - Implement `etl-sql admin backup` to package configuration, database state, and files.
  - Enforce split-custody recovery by ensuring decryption and Data Protection keys are backed up separately from database state.
  - Implement `etl-sql admin restore --validate` to verify catalog and key versions before restoring.
- [x] **Phase 3: Database Migrations**
  - Implement automatic database schema migrations for SQLite upon engine/portal startup or upgrade.
- [x] **Phase 4: N→N+1 Upgrade Validation**
  - Seed a portal on the prior release version, upgrade in place to the current release, and verify that permissions, jobs, subscriptions, datasets, and audit history are intact.
  - Distinguish this from a clean restore drill — the upgrade path must apply EF migrations on top of a live production-shaped database, not a fresh empty one.
  - Add this drill as a named phase in `Test-PreRelease.ps1` so it runs before every release tag.

---

### Practical High Availability
*Enables horizontal scaling of Portal and Orchestrator nodes behind a load balancer with PostgreSQL state and shared artifact storage.*

#### TODOs:
- [ ] **Phase 1: PostgreSQL State Provider**
  - Extract database provider interfaces for the Portal/Orchestrator state.
  - Create EF Core migrations for PostgreSQL.
  - Implement `etl-sql admin migrate-database --from sqlite --to postgres --dry-run` with row verification and cutover checkpoints.
- [ ] **Phase 2: Artifact Storage Abstraction**
  - Create a unified storage provider interface for scripts, snapshots, cached datasets, and keys.
  - Implement Local and SMB/UNC shared storage providers.
  - Enforce path-traversal guardrails and script immutability checks at the storage boundary.
- [ ] **Phase 3: Distributed Leases & Fencing**
  - Implement database-backed node heartbeats and execution/job leases.
  - Implement monotonically increasing fencing tokens to reject stale writers during network partitions.
  - Add database-backed leader election for running singletons (e.g. database migrations).
- [ ] **Phase 4: Stateless Node Operation**
  - Configure Portal nodes to read state and serve snapshots from PostgreSQL.
  - Support load-balancer session affinity for interactive IDE sessions.
  - Ensure a node partition immediately cancels local running jobs if it loses its database lease.
  - Implement a lightweight `/healthz` HTTP endpoint on Portal nodes to check database, storage, and lease connectivity for load-balancer health checks.
  - Heart-beat node capacity (CPU/memory utilization) to prevent overloaded nodes from claiming new leases, and implement a quarantine policy for failing jobs to prevent cascade failures across the cluster.
- [ ] **Phase 5: Rolling Deployment Certification**
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

### Governance Core
*Enforces centralized security policy, named secret references, and remote audit delivery across all hosts (CLI, IDE, Portal).*

#### TODOs:
- [ ] **Phase 1: Typed Policy Registry**
  - Create a central registry of settings (Forbidden, Allowed, Constrained, Locked).
  - Enforce policy against parsed AST nodes rather than text matching.
  - Apply policy evaluations at compile/lint time and again at execution time.
- [ ] **Phase 2: Organization Policy Documents**
  - Implement versioned policy schemas specifying allowed connector types, filesystem roots, and remote executions.
  - Support fetching policies from local OS-protected configurations or HTTPS endpoints.
  - Configure offline cache periods, failing secure (no access) if a policy expires or cannot be validated.
- [ ] **Phase 3: Named Secret References**
  - Implement `ISecretProvider` (Environment, OS Secret Store, HTTPS Vault).
  - Support syntax to reference secrets by logical name (`SECRET:sales_db_password`) in connection strings.
  - Block raw secret values from appearing in logs, diagnostics, or dashboards.
- [ ] **Phase 4: Durable Audit Outbox**
  - Implement a transactional outbox table for audit events.
  - Create an HTTPS audit transporter supporting batching, retry, deduplication, and backpressure limits.
  - Define policy to fail closed (block mutations) when remote audit delivery is unavailable.
  - Implement disk-size safeguards (rotation/retention thresholds) for the local SQLite outbox queue during extended collector outages.

---

### Enterprise Identity & Approvals
*Integrates with enterprise OpenID Connect (OIDC) providers and adds human-in-the-loop approval workflows.*

#### TODOs:
- [ ] **Phase 1: Certified OIDC Authentication**
  - Reconcile and certify OIDC login, logout, token refresh, and claim validation.
  - Map OIDC group claims dynamically to Portal user groups.
  - Support MFA and conditional access by delegating authentication entirely to the identity provider.
- [ ] **Phase 2: Service Accounts**
  - Implement non-interactive service account identities for scheduled CLI jobs and API access.
  - Assign explicit OAuth scopes and rotation patterns.
- [ ] **Phase 3: Approval Workflows (Four-Eyes)**
  - Implement approval requests for critical actions (publishing reports, modifying production scheduled jobs).
  - Enforce segregation of duties (a user cannot approve their own changes).
  - Automatically re-evaluate and cancel pending/approved items if permissions or user status changes.
  - Record all approval requests, comments, grants, and rejections in the Governance audit trail.

---

### Departmental Isolation
*Supports multiple isolated environments (dev/test/prod, or different departments) without the complexity of shared-table multitenancy.*

#### TODOs:
- [ ] **Phase 1: Repeatable Deployment Templates**
  - Build systemd, Windows Service, and Docker templates for deploying isolated environments.
  - Restrict access so that one department's service identity has no access to another's DB, storage, or keys.
- [ ] **Phase 2: Environment Portability**
  - Build CLI commands to export and import reports, jobs, and configs between environments (e.g., Dev to Prod).
  - Ensure export/import routines strip out all environmental secrets and connection strings.
- [ ] **Phase 3: Fleet Aggregation**
  - Define the security boundary before implementation: the aggregator must be read-only, authenticate to each department Portal via a dedicated scoped service account, and have no ability to write data or execute scripts in any department environment.
  - Build a central dashboard to aggregate environment health, active executions, and audit records without blending raw department data or permissions.
  - Prove that a compromised fleet aggregator credential cannot be used to pivot into any department's database, artifact storage, or encryption keys.

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
