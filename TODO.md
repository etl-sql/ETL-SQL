# ETL-SQL Development TODO List

Use this list to track and prioritize outstanding roadmap items, architecture modernization tasks, and documentation improvements.

---

## Enterprise Multi-User Hardening + Script-First Recovery

> Status: **planned.**
> Goal: prove that datasets, users, permissions, jobs, and subscriptions remain secure and correct under
> concurrent users, process failure, permission changes, and clean-server recovery. Functional endpoint
> coverage is not enough: enterprise readiness requires durable coordination, authorization at the time
> work executes, recoverable cross-resource operations, and a script-first way to reconstruct the portal.
>
> Priority convention: **P0** security/data-exposure blocker, **P1** correctness/recovery required before
> an enterprise claim, **P2** verification and operational hardening, **P3** larger enterprise capability
> or an explicitly documented deployment boundary.

### Priority 1 — Multi-user correctness and recoverability

- [ ] **P1.5 Fix ownership lifecycle and folder-owner semantics.**
  `Folder.OwnerId` is recorded but does not grant effective permission. Decide whether ownership implies
  Manage or remove the misleading field. Add explicit ownership transfer/reassignment for folders,
  reports, datasets, subscriptions, alerts, and other user-owned objects before deleting a user. Define
  behavior for LDAP users and groups that disappear during synchronization.
- [ ] **P1.6 Make audit recording part of the operation contract.**
  Mutations and their audit rows are generally separate commits. Ensure security-sensitive changes cannot
  succeed without a durable audit event, add operation/correlation IDs for background work, and define
  retention/export. Decide whether enterprise mode requires append-only external export or tamper-evident
  hash chaining rather than relying only on the mutable portal database.

### Priority 1 — Script-first clean-server reconstruction

- [ ] **P1.7 Add `EXPORT PORTAL CONFIGURATION` as an admin-only ETL-SQL operation.**
  Export the current declarative setup as a readable, versioned, idempotent `.etlsql` bootstrap script.
  Emit logical names and paths rather than database IDs, in dependency order:
  portal settings that are safe to export; groups; users; group memberships; folders; folder ACLs;
  reports/publication references; dataset metadata and ACLs; SMTP aliases; jobs; subscriptions; alerts;
  saved administrative definitions; and other scriptable portal resources discovered during inventory.
  Do not silently omit an unsupported resource: produce an export summary listing emitted, skipped,
  runtime-only, and secret-required items.
- [ ] **P1.8 Exclude secrets and ephemeral/security artifacts from configuration export.**
  Never export password hashes, encrypted ciphertext, JWT/dataset-at-rest keys, SMTP passwords,
  Orchestrator API keys, refresh/share/embed tokens, sessions, job history, audit rows, cached dataset
  contents, or snapshots as configuration. Emit explicit placeholders such as
  `${INITIAL_ADMIN_PASSWORD}`, `${SMTP_CORPORATE_PASSWORD}`, and `${ORCHESTRATOR_API_KEY}` with a generated
  requirements header. Prefer environment/secret-provider references so an administrator does not have
  to place plaintext directly in the bootstrap script.
- [ ] **P1.9 Add deterministic import, validation, and dry-run behavior.**
  The exported script must support `SET WHAT_IF ON`, report missing secrets and conflicting objects before
  mutation, and be safe to rerun. Define create/update/skip behavior explicitly; do not depend on source
  integer IDs. Validate referential order and fail closed when a named folder, group, report, SMTP alias,
  or dataset source is unavailable.
- [ ] **P1.10 Separate configuration reconstruction from data/content backup.**
  A bootstrap script reconstructs configuration, not encrypted datasets or generated report output.
  Produce a companion manifest/runbook identifying report scripts/bundles and portable dataset exports
  that must be copied or published separately. Existing portal and Orchestrator database/file backups
  remain the exact-state disaster-recovery path; configuration export is the auditable clean-start path.
- [ ] **P1.11 Prove clean-server round-trip reconstruction.**
  Seed a portal with multiple users/groups, overlapping ACLs, reports, public/private datasets, refresh
  grants, jobs, SMTP aliases, subscriptions, and disabled resources. Export configuration, initialize an
  empty portal, supply test secrets, execute the script twice, and compare normalized effective state.
  Assert that no source secret or security token appears in the export.

### Priority 2 — Verification and operational hardening

- [ ] **P2.1 Add a hosted-service integration lane.**
  `PortalWebFactory` removes every `IHostedService`, so normal portal API tests do not exercise startup
  validation, polling, reconciliation, cleanup, and their interactions. Add a separate fixture that runs
  selected hosted services against isolated databases and controlled clocks.
- [ ] **P2.2 Add genuine multi-process tests.**
  Start two portal and/or Orchestrator processes against the supported shared state and test simultaneous
  refresh, due-job claims, job cancellation, permission changes, subscription delivery, restart recovery,
  and conflicting administration. In-process `Task.WhenAll` tests do not prove process coordination.
- [ ] **P2.3 Add subscription delivery idempotency and failure tests.**
  Cover SMTP timeout after server acceptance, retry after unknown outcome, duplicate scheduler trigger,
  attachment generation failure, invalid recipient isolation, disabled SMTP alias, and partial recipient
  failure. Choose and document at-most-once or at-least-once semantics; use delivery IDs and a durable
  ledger/outbox to make duplicates observable and controllable.
- [ ] **P2.4 Add fault-injection and recovery tests.**
  Kill processes between every cross-resource step; simulate SQLite busy/locked, disk full, corrupt files,
  unavailable Orchestrator/SMTP, network partition, and clock skew. Verify reconciliation is idempotent and
  preserves the last known-good report/dataset/subscription state.
- [ ] **P2.5 Automate a complete backup/restore drill.**
  Restore portal DB, Orchestrator DB, report scripts/bundles, subscription definitions, snapshots,
  datasets, Data Protection/key material, and configuration into a clean location. Verify permissions,
  key-version reads, jobs, subscriptions, and audit continuity after restore.
- [ ] **P2.6 Add workload fairness and abuse tests.**
  Global concurrency caps allow one user or group to consume all capacity. Define per-user/group limits,
  queue fairness, cancellation, timeouts, export quotas, and administrative overrides; test mixed short,
  long, interactive, scheduled, refresh, and subscription workloads.
- [ ] **P2.7 Test the versioned upgrade path, not just backup/restore.**
  P2.5 drills restore into a clean location; nothing proves an N→N+1 upgrade: EF migrations applying
  over a live catalog, Orchestrator SQLite schema changes, parquet/key-version compatibility, and what
  rollback means after a partial migration. Seed a portal on release N, upgrade in place to N+1, and
  verify permissions, jobs, subscriptions, datasets, and audit continuity. Define and document the
  supported rollback procedure (restore-from-backup vs. down-migration).
- [ ] **P2.8 Add operational observability for a multi-user deployment.**
  P1.6 adds correlation IDs for audit only. An administrator running this for real users needs: active
  executions, queue depth, job/SMTP failure rates, and disk usage of dataset/snapshot storage — via
  expanded health checks or a metrics endpoint (OpenTelemetry if warranted). Extend the
  no-credentials-in-logs guarantee from a dataset-test-only scan to a portal-wide log-hygiene rule.

### Priority 3 — Enterprise capability decisions

- [ ] **P3.1 Decide the HA/cluster roadmap (Multi-Path Practical HA).**
  SQLite and local/process state are suitable for a single-node deployment but not an unqualified HA
  claim. Provide three supported topologies to satisfy different enterprise deployment sizes:
  - **Path A (Relational DB):** Switch EF Core providers to support PostgreSQL or Microsoft SQL Server (easily supported in domain-managed SMB infrastructures).
  - **Path B (Zero-DBA Distributed SQLite):** Integrate `rqlite` (SQLite replicated via Raft) running as a local sidecar process, allowing nodes to cluster directly via command-line flags without external DBMS administration.
  - **Path C (Shared Storage Abstraction):** Abstract disk-writes behind `IStorageProvider` and support:
    - On-Premise SMB: Windows UNC Shares (`\\fileserver\etl-sql\storage`) running under Domain Service Accounts.
    - Cloud-Native: S3-compatible object storage (MinIO, AWS S3, or Backblaze B2).
  - **Path D (Infrastructure-Free Coordination):** Replace process-local semaphores and memory caches:
    - Use database-backed leases (`ExpiresAt` and `LockedByNode` records) for scheduled run claims (no Redis).
    - Use a database-driven invalidation table (`SystemEvents` polling) to synchronize cache evictions across nodes.
- [ ] **P3.2 Add or explicitly defer OIDC/SAML and MFA.**
  Local Identity and LDAP do not cover common enterprise SSO, conditional-access, and MFA requirements.
  Document the supported identity boundary until federated authentication is implemented.
- [ ] **P3.3 Review the authorization model for inheritance, deny, and direct grants.**
  Current group-based highest-permission-wins ACLs may be sufficient, but enterprise deployments often
  require inherited folder permissions, explicit deny, direct user/service-account grants, nested groups,
  and a permission-change impact preview. Decide intentionally and add effective-permission tests.
- [ ] **P3.4 Decide departmental/tenant isolation and naming boundaries (Multi-Tenancy Options).**
  Dataset names are globally unique and portal state is shared. Provide isolated namespaces, storage, encryption keys, and scheduler coordination:
  - **Path A (Soft Multi-Tenancy):** Add `TenantId` columns to all tables (users, groups, reports, datasets, audit logs) and apply EF Core Global Query Filters. Change unique database constraints from `Name` to `(TenantId, Name)`.
  - **Path B (Hard Multi-Tenancy - Recommended for SQLite):** Deploy a database-per-tenant model. Since SQLite databases are simple local files, the portal dynamically resolves and connects to a separate `<tenant>.db` file based on subdomain/URL prefix/header context (zero DBMS administration overhead).
  - **Path C (Storage Directory Isolation):** Partition the filesystem storage roots to include the tenant ID (`ScriptRootPath/tenant_A/`, `DatasetRootPath/tenant_A/`) and update `PortalPathGuard` to enforce tenant boundary checks.
  - **Path D (Scheduler Tenant Context):** Ensure the background Orchestrator scheduler propagates tenant contexts (loading the correct DB connection, storage path, and keys) during scheduled job runs.
- [ ] **P3.5 Add change approval where production governance requires it (Four-Eyes Approval).**
  Consider optional four-eyes approval for report publication, job changes, subscription recipient
  changes, SMTP/secret changes, and high-impact ACL grants. Preserve script-first operation by making
  approval state and promotion scriptable and auditable:
  - **Path A (Propose-Review-Promote Schema):** Store proposed alterations in an `ApprovalRequest` outbox table containing the serialized target states (`PendingStateJson`) rather than modifying active tables immediately.
  - **Path B (Scriptable Approvals):** Add new declarative ETL-SQL syntax to configuration scripts (`PROPOSE UPDATE...` and `APPROVE PROPOSAL... BY '<reviewer>' WITH SIGNATURE = '...'`) so that approval histories and promotions remain reproducible and auditable in reconstruction bootstraps.
  - **Path C (API Interception & Validation):** Configure ASP.NET Core controllers to intercept mutations on gated resources, return a `202 Accepted` with a proposal ID, and enforce the dual-control constraint **`ProposerId != ReviewerId`** upon approval.
- [ ] **P3.6 Decide the self-service password recovery boundary.**
  Only administrator password reset exists today. That may be the right answer for an LDAP-centric
  deployment, but document it as an explicit boundary alongside the P3.2 SSO/MFA decision (and note
  that email-based self-service reset would depend on the trusted SMTP path) so it reads as a choice
  rather than an omission.
- [ ] **P3.7 Centralized Policy Enforcement & Remote Auditing.**
  Workstation local executions can bypass config policies, override guards via `SET` commands, and alter
  local logs. Enable enterprise-locked parameters and remote audit telemetry:
  - **Path A (Immutable Central Config):** Allow the CLI to resolve configuration via environment variables, a secured network mount path, or a cryptographically signed HTTPS policy endpoint, failing closed if the server is unreachable or the signature validation fails.
  - **Path B (Locked Guardrails & Blocked SET Commands):** Reject compilation/linting of scripts containing explicitly blacklisted `SET` commands (e.g. `DisabledSetCommands`), and block runtime execution if a `SET` command attempts to modify a `LockedSettings` parameter.
  - **Path C (Tamper-Proof Audit Sinks):** Stream telemetry events (invocations, rule bypasses, failures) in real time over HTTPS/Syslog using cross-platform Serilog remote sinks to write-only SIEM/centralized loggers (e.g., Elasticsearch, Splunk).
- [ ] **P3.8 Asymmetric & KMS-Based Script Encryption.**
  Symmetric password-based script encryption requires sharing and rotating passwords. Move to certificate or vault-backed options to protect scripts in collaborative environments:
  - **Path A (Asymmetric Key/Certificate Decryption):** Allow scripts (or script-specific secrets) to be encrypted with an enterprise public key. The local CLI decrypts them at runtime using a private key or certificate installed in the workstation's local Certificate Store or secure Keychain.
  - **Path B (KMS / Envelope Decryption):** Integrate the CLI with corporate Key Management Services (e.g., HashiCorp Vault, AWS KMS). The data key is wrapped by the KMS, allowing central auditing of decryption operations and instant revocation of a user's decryption rights.

### Recommended execution order

1. **P1.7-P1.11:** script-first configuration export/import and clean-server round-trip.
2. **P1.3-P1.6:** multi-admin conflicts, durable portal state, ownership, and audit guarantees.
3. **P2 lane:** hosted-service, multi-process, delivery, fault-injection, restore, upgrade,
   observability, and workload tests.
4. **P3 decisions:** publish explicit deployment, identity, isolation, and recovery boundaries before
   making an enterprise or HA support claim.
