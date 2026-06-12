# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; move a roadmap phase here only when work on that
release begins.

---

## Enterprise Multi-User Hardening + Script-First Recovery

> Status: **planned.**
> Goal: prove that datasets, users, permissions, jobs, and subscriptions remain secure and correct under
> concurrent users, process failure, permission changes, and clean-server recovery. Functional endpoint
> coverage is not enough: enterprise readiness requires durable coordination, authorization at the time
> work executes, recoverable cross-resource operations, and a script-first way to reconstruct the portal.
>
> Priority convention: **P0** security/data-exposure blocker, **P1** correctness/recovery required before
> an enterprise claim, and **P2** verification and operational hardening.

### Priority 1 — Multi-user correctness and recoverability

- [x] **P1.5 Fix ownership lifecycle and folder-owner semantics.**
  `Folder.OwnerId` is recorded but does not grant effective permission. Decide whether ownership implies
  Manage or remove the misleading field. Add explicit ownership transfer/reassignment for folders,
  reports, datasets, subscriptions, alerts, and other user-owned objects before deleting a user. Define
  behavior for LDAP users and groups that disappear during synchronization.
  *(done — v0.11.0)* **Decision: folder ownership implies Manage** (the field already anchors the
  system-publish dataset ownership fallback, so removal was off the table). The rule lives centrally
  in `FolderPermissionService` (userId threaded through the group overloads, including the batch
  path), so controllers, dataset permission evaluation, and subscription delivery reauthorization
  all agree. **Deletion lifecycle:** `DELETE /api/admin/users/{id}` now 409s with an owned-resource
  inventory when the user owns folders/reports/datasets; `?reassignTo=<userId>` transfers all three
  (version-bumped, audited as `TRANSFER_OWNERSHIP`). Personal artifacts die with the user, and the
  subscriptions' Orchestrator jobs + trigger scripts are removed inline rather than waiting for
  startup reconciliation. **LDAP boundary documented:** sync is login-time-only convergence of
  LDAP-provider group memberships; vanished directory users keep their rows (they just can't bind)
  until an admin acts, and vanished groups drain per-login — nothing is auto-deleted. Tests:
  `OwnershipLifecycleTests` (owner-Manage without ACL, 409→transfer→audit, invalid targets,
  job/script/capability cleanup); the delivery-security fixture now separates folder owner from
  subscription owner since owners can no longer lose access via ACL revocation. Docs: admin guide
  §4.7/§4.8/§5.2 + architecture ownership-semantics note.
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

- [x] **P2.1 Add a hosted-service integration lane.**
  `PortalWebFactory` removes every `IHostedService`, so normal portal API tests do not exercise startup
  validation, polling, reconciliation, cleanup, and their interactions. Add a separate fixture that runs
  selected hosted services against isolated databases and controlled clocks.
  *(done — v0.11.0)* `HostedPortalFactory` keeps the full hosted-service pipeline (session cache,
  execution job service, Orchestrator poller, JWT/dataset-key validators, refresh-token maintenance)
  over the standard isolated temp-DB fixture, with an injectable `TimeProvider` (now registered in DI)
  and new config knobs `Portal:Orchestrator:PollIntervalSeconds` and
  `Portal:Jwt:RefreshTokenPurgeIntervalSeconds` so loops are observable in tests.
  `HostedServiceLaneTests` proves: full startup + instance-lock acquisition + loop survival, both
  fatal startup validators stopping the host, the machine-fallback opt-in, and the in-host token
  purge honoring a pinned clock. The lane immediately caught and fixed a real shutdown race:
  `ExecutionJobService.ReleaseInstanceLocks` mutated the lock list unsynchronized, throwing when a
  self-initiated fatal-validator shutdown overlapped host disposal. Startup *reconciliation*
  (`DatasetStorageMaintenance`/`SubscriptionScriptMaintenance`) runs inline in `Program.cs` and is
  already exercised by every portal test; multi-process coordination remains P2.2.
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
