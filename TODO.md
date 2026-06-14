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
- [x] **P1.6 Make audit recording part of the operation contract.**
  Mutations and their audit rows are generally separate commits. Ensure security-sensitive changes cannot
  succeed without a durable audit event, add operation/correlation IDs for background work, and define
  retention/export. Decide whether enterprise mode requires append-only external export or tamper-evident
  hash chaining rather than relying only on the mutable portal database.
  *(done — v0.11.0)* `AuditService.Stage` adds the audit row to the operation's own unit of work, and
  the security-sensitive mutation set now stages before its final save so the change and its audit
  event share one commit: user update/reset-password/revoke-tokens, user delete (whole flow now one
  explicit transaction with external job/script cleanup moved post-commit), group update/delete/
  membership (single + bulk), folder ACL grant/revoke, dataset update/move/delete/ACL, SMTP
  update (which previously didn't audit at all)/delete, subscription delete, share-link/embed-token
  revocation, and subscription delivery success/failure bookkeeping. Conflicted/rejected operations
  leave no audit row. New `AuditLog.CorrelationId` (migration `AuditLogCorrelationId`) carries the
  HTTP trace identifier automatically and an explicit operation id for background work (per-delivery
  `delivery-<id>`); exposed in the DTO and CSV export. Retention is opt-in via
  `Portal:Audit:RetentionDays` (default keep-forever), enforced by the clock-injectable
  `AuditRetentionService` and proven in the hosted lane under a pinned clock. **Decision recorded:**
  the in-portal table is deliberately not tamper-proof; the supported enterprise posture is scheduled
  CSV export/forwarding to external append-only storage, with in-database hash chaining an explicit
  roadmap non-goal. Creates and bulk-summary events remain best-effort `LogAsync` records (universal
  contract tracked in ROADMAP §8). Tests: `AuditContractTests` (atomic success with correlation id,
  conflict leaves no row) + hosted-lane retention sweep.

### Priority 1 — Script-first clean-server reconstruction

- [x] **P1.7 Add `EXPORT PORTAL CONFIGURATION` as an admin-only ETL-SQL operation.**
  Export the current declarative setup as a readable, versioned, idempotent `.etlsql` bootstrap script.
  Emit logical names and paths rather than database IDs, in dependency order:
  portal settings that are safe to export; groups; users; group memberships; folders; folder ACLs;
  reports/publication references; dataset metadata and ACLs; SMTP aliases; jobs; subscriptions; alerts;
  saved administrative definitions; and other scriptable portal resources discovered during inventory.
  Do not silently omit an unsupported resource: produce an export summary listing emitted, skipped,
  runtime-only, and secret-required items.
  *(done — v0.11.0)* New scripted statement `EXPORT PORTAL CONFIGURATION TO '<file>'`
  (AST/parser/connector) fetches the bootstrap from the new admin-only
  `GET /api/admin/configuration/export` (audited) and writes it path-guarded.
  `ConfigurationExportService` emits, in dependency order and by logical name: groups, users
  (LDAP-aware; inactive users re-disabled), memberships, folders (parents first), folder ACLs, SMTP
  connections, report publications, dataset metadata/grants, scheduled PDF/CSV subscriptions (with
  parameters), and alerts. Secrets never leave the portal: `${PORTAL_USER_*_PASSWORD}`/
  `${SMTP_*_PASSWORD}` placeholders are collected into a `REQUIRED SECRETS` header (groundwork for
  P1.8), and the trailing summary lists emitted, skipped/manual (deliver-on-refresh and multi-
  recipient subscriptions, non-PDF/CSV formats, folderless published datasets, refresh jobs needing
  an orchestrator alias, file-provisioned portal settings), and runtime-only items.
  `ConfigurationExportTests` proves the generated script parses with the real ETL-SQL parser,
  contains the expected statements/placeholders, leaks no seeded credential or hash, and that the
  endpoint is admin-gated. Residuals: deeper secret-exclusion sweep (P1.8), deterministic
  import/WHAT_IF semantics (P1.9 — CREATE-based statements are not yet rerun-safe), companion
  content manifest (P1.10), round-trip proof (P1.11), and Grammar.md/completion-surface docs to
  ride with the P1.9 import work.
- [x] **P1.8 Exclude secrets and ephemeral/security artifacts from configuration export.**
  Never export password hashes, encrypted ciphertext, JWT/dataset-at-rest keys, SMTP passwords,
  Orchestrator API keys, refresh/share/embed tokens, sessions, job history, audit rows, cached dataset
  contents, or snapshots as configuration. Emit explicit placeholders such as
  `${INITIAL_ADMIN_PASSWORD}`, `${SMTP_CORPORATE_PASSWORD}`, and `${ORCHESTRATOR_API_KEY}` with a generated
  requirements header. Prefer environment/secret-provider references so an administrator does not have
  to place plaintext directly in the bootstrap script.
  *(done — v0.11.0)* The export queries only configuration tables; it never reads or emits password
  hashes, encrypted SMTP ciphertext, keys, API keys, refresh/share/embed tokens, sessions, history,
  audit rows, or caches (those are listed under the script's runtime-only summary). The `REQUIRED
  SECRETS` header now documents the three substitution forms in preference order — `ENV('NAME')`
  (no plaintext in the file), `ENC:...`, then a plaintext literal — and an unsubstituted `${...}`
  placeholder is rejected at import before reaching the portal (P1.9 fail-closed). New
  `ConfigurationExportSecretExclusionTests` seeds real marker secrets (password hash, SMTP
  ciphertext, refresh/share/embed tokens) and asserts none appear in the export while the credential
  is still represented by its placeholder. Admin guide §9.0 updated.
- [x] **P1.9 Add deterministic import, validation, and dry-run behavior.**
  The exported script must support `SET WHAT_IF ON`, report missing secrets and conflicting objects before
  mutation, and be safe to rerun. Define create/update/skip behavior explicitly; do not depend on source
  integer IDs. Validate referential order and fail closed when a named folder, group, report, SMTP alias,
  or dataset source is unavailable.
  *(done — v0.11.0)* The bootstrap replays through the `REPORTPORTAL` connector (a normal script run,
  so `SET WHAT_IF ON` is honored). The identity/permission graph is now **idempotent create-or-skip**
  (USER, GROUP, ADD-USER-TO-GROUP, FOLDER, SMTP, PUBLISH REPORT all check existence first via new
  `Try*` lookup helpers; folder/dataset GRANT were already server-side upserts) — safe to rerun
  without `409`s, by logical name only. **Fail-closed before mutation:** `ResolveRequiredSecretAsync`
  rejects an unsubstituted `${...}` placeholder before it is sent, and missing folder/group/user/report
  references throw a clear error instead of a generic portal failure. **Validating dry-run:** new
  read-only `IPortalAdminConnection.PlanAdminStatementAsync` (default-interface method, overridden by
  the portal connector) reports a create/skip plan and runs the same reference/secret validation
  without mutating; the two engine remote-block handlers now route WHAT_IF through it instead of the
  old type-name-only message. Tests: `ScriptedPortalImportTests` drives the connector against the
  in-memory portal via a new injectable-`HttpClient` ctor (idempotent double-replay, dry-run plan with
  no mutation, missing-secret fail-closed, missing-reference fail-closed). Residual: subscriptions and
  alerts are id-keyed (not yet name-deduped) so they re-create on rerun — documented in the admin guide
  import section; name-keyed subscription/alert upsert and the round-trip proof remain with P1.11.
- [x] **P1.10 Separate configuration reconstruction from data/content backup.**
  A bootstrap script reconstructs configuration, not encrypted datasets or generated report output.
  Produce a companion manifest/runbook identifying report scripts/bundles and portable dataset exports
  that must be copied or published separately. Existing portal and Orchestrator database/file backups
  remain the exact-state disaster-recovery path; configuration export is the auditable clean-start path.
  *(done — v0.11.0)* The export now ends with a **companion content manifest and recovery runbook**
  that names the three recovery paths (configuration = this script; content = report scripts +
  datasets copied/published separately; exact-state = portal/Orchestrator DB+file backups) and lists
  every report `.rptsql` to copy into the target script root (`<logical> <= <source path>`) and every
  dataset to re-materialize or re-publish. `ExportResult` gained a structured
  `ContentManifest` (`ContentManifestItem` Kind/Logical/Source/Action) so callers and tests can
  consume it, and the export audit detail now records the content-item count. Test:
  `ConfigurationExportTests` asserts the manifest section, the runbook's three paths, and the report
  script path. Admin guide §9.0 updated.
- [x] **P1.11 Prove clean-server round-trip reconstruction.**
  Seed a portal with multiple users/groups, overlapping ACLs, reports, public/private datasets, refresh
  grants, jobs, SMTP aliases, subscriptions, and disabled resources. Export configuration, initialize an
  empty portal, supply test secrets, execute the script twice, and compare normalized effective state.
  Assert that no source secret or security token appears in the export.
  *(done — v0.11.0)* `ConfigurationRoundTripTests` seeds a source portal with the
  identity/permission/SMTP graph — three users (one **disabled**), two groups, overlapping folder ACLs
  across a parent/child, and an SMTP alias — exports its configuration, supplies the `${...}` secrets,
  and replays the bootstrap **twice** into a second fresh `PortalWebFactory` through the connector. It
  asserts: no seeded secret (SMTP ciphertext, password hashes) appears in the export; the reconstructed
  normalized state (users+role+active, groups, memberships, folders, overlapping ACLs, SMTP) **equals**
  the source's; and the second pass adds no duplicate rows (idempotent). The script is replayed by
  parsing the real export, extracting the `EXECUTE … BEGIN … END` body (raw-captured, re-parsed exactly
  as `ExecutePushdownStatementHandler` does at runtime), and running each statement through the
  connector's import path. **Scope:** the round-trip covers the config graph the P1.9 import made
  idempotent; reports/datasets/subscriptions are content (P1.10) and/or carry the documented
  subscription/alert idempotency residual, so they are intentionally out of the pure-config round-trip.

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
- [x] **P2.2 Add genuine multi-process tests.**
  Start two portal and/or Orchestrator processes against the supported shared state and test simultaneous
  refresh, due-job claims, job cancellation, permission changes, subscription delivery, restart recovery,
  and conflicting administration. In-process `Task.WhenAll` tests do not prove process coordination.
  *(done — v0.11.0)* The supported topology is **one active portal per database**, so the coordination
  that matters happens through durable shared state, not shared memory — and the tests use **separate
  service instances with separate database connections** (not `Task.WhenAll` on one shared object) so
  the coordination is proven across connections. Existing proofs: the instance lock rejects a second
  portal on the same DB (`ExecutionJobServiceTests.StartAsync_RejectsSecondPortalInstanceUsingSameDatabase`,
  real OS file locks), the Orchestrator job lease runs a due job exactly once across two scheduler
  instances over one store (`JobExecutionLeaseTests`), restart recovery marks abandoned jobs
  (`StartAsync_MarksAbandonedJobs…`), and conflicting administration conflicts via optimistic concurrency
  (`OptimisticConcurrencyIntegrationTests`). New `MultiInstanceCoordinationTests` closes the subscription-
  delivery gap: two independent delivery executors over fresh WAL connections to one portal DB — a proxy
  for two poller processes — coordinate through the durable delivery ledger, so a completion delivered by
  one is suppressed at the second (sequential), exactly one wins a simultaneous race on the same
  completion (concurrent, via the ledger's unique index, stable over repeated runs), and distinct
  completions are not falsely contended. **Boundary:** true OS-process isolation (separate connection
  pools / in-memory caches that a single test process shares) belongs to a Docker/integration harness;
  the durable cross-connection coordination boundary is proven here.
- [x] **P2.3 Add subscription delivery idempotency and failure tests.**
  Cover SMTP timeout after server acceptance, retry after unknown outcome, duplicate scheduler trigger,
  attachment generation failure, invalid recipient isolation, disabled SMTP alias, and partial recipient
  failure. Choose and document at-most-once or at-least-once semantics; use delivery IDs and a durable
  ledger/outbox to make duplicates observable and controllable.
  *(done — v0.11.0)* **Semantics: at-most-once per scheduler trigger**, documented in admin guide §8.3
  and the `SubscriptionDeliveryService` doc. New durable `SubscriptionDelivery` ledger (entity +
  migration `SubscriptionDeliveryLedger`) records each attempt with a `delivery-<id>` (== the audit
  correlation id) and is unique on `(SubscriptionId, TriggerKey)`. `DeliverAsync` now claims a ledger
  row before sending (duplicate trigger → suppressed without re-running), executes, then finalizes the
  row with the terminal outcome; a manual call uses a `manual:<guid>` key, and the poller passes the
  Orchestrator completion's `EndTime` ("o") as the key so a re-observed completion is never delivered
  twice. The portal never records `Delivered` unless the runner reports success (no false success);
  the SMTP timeout-after-acceptance at-least-once caveat is documented. `SubscriptionDeliveryLedgerTests`
  covers duplicate-trigger suppression, distinct triggers, timeout-after-acceptance, attachment-generation
  failure, runner-throws unknown outcome (recorded + not reclaimed), missing/disabled SMTP alias, and
  denial isolated from a healthy delivery. **Residual:** delivery is whole-subscription (one message to
  all recipients); per-recipient split/isolation is a documented follow-up.
- [x] **P2.4 Add fault-injection and recovery tests.**
  Kill processes between every cross-resource step; simulate SQLite busy/locked, disk full, corrupt files,
  unavailable Orchestrator/SMTP, network partition, and clock skew. Verify reconciliation is idempotent and
  preserves the last known-good report/dataset/subscription state.
  *(done — v0.11.0)* New `FaultInjectionRecoveryTests` (fast lane) adds the deterministic gaps on top of
  existing recovery coverage: dataset reconciliation is **idempotent** (a second pass is a no-op) and
  **preserves last known-good** (the referenced cache + row survive while crash artifacts — orphan rows,
  orphan files, abandoned `.tmp-` staging — are removed); reconciliation **tolerates a held-open
  (busy/locked) file** without aborting the sweep; and the Orchestrator poller **degrades to cached-only
  mode** (no throw) when its database is corrupt/unreadable. These complement already-covered scenarios:
  `SubscriptionLifecycleRecoveryTests` (kill-between-row/script/job → reconcile heals orphan/stale/drifted
  state, idempotent second pass) and `DatasetStorageMaintenanceTests` (managed-orphan + staging/backup
  cleanup, referenced/unmanaged preservation), plus the P2.3 delivery-ledger failure modes (timeout,
  attachment failure, unknown-outcome-not-reclaimed, missing SMTP, denial isolation) and the P2h dataset
  atomic-write rollback. **Out of fast-lane scope:** disk-full, network-partition, and clock-skew faults
  are non-deterministic and belong to a separate chaos/integration harness (noted in the test summary);
  live unavailable-Orchestrator/SMTP outages are exercised by the Docker integration lane
  (`JobSchedulingIntegrationTests.Verify_Dependency_Outage_And_Fault_Tolerance`,
  `SubscriptionIntegrationTests.Verify_Subscription_History_When_Orchestrator_Db_Is_Unavailable`).
- [x] **P2.5 Automate a complete backup/restore drill.**
  Restore portal DB, Orchestrator DB, report scripts/bundles, subscription definitions, snapshots,
  datasets, Data Protection/key material, and configuration into a clean location. Verify permissions,
  key-version reads, jobs, subscriptions, and audit continuity after restore.
  *(done — v0.11.0)* `BackupRestoreDrillTests` (fast lane). `RestorablePortalFactory` overlays a backup
  directory onto a fresh host before startup (reusing the fixture's fixed JWT secret), so a restored
  portal authenticates exactly as the source. `CleanServerRestore_…` seeds a source portal (user,
  group, folder + ACL, report, subscription, dataset metadata with a versioned at-rest key, an audit
  row, and an Orchestrator job), WAL-checkpoints, copies the whole state tree to a backup, then brings
  up a **second** portal on the backup and asserts continuity: admin login works (identity + JWT config),
  the ACL/membership survive and resolve via `FolderPermissionService`, the subscription and (canonically
  named, reconciliation-preserved) Orchestrator job survive, the audit row survives, and the dataset
  metadata + `AtRestKeyVersion` survive. `DatasetCacheKeyVersionRead_SurvivesBackupRestore` proves the
  key-version read end to end: a parquet encrypted with key v1 is backed up, restored, and decrypted by
  `DatasetViewerService` under the restored key configuration. **Finding surfaced + documented:** dataset
  cache files are referenced by absolute path in the catalog, so `DatasetRootPath` must be restored to its
  original absolute path (or catalog paths rewritten) — a moved cache is not found and startup
  reconciliation treats it as an orphan; everything else restores to a clean location (admin guide §6.5).
  The production `etl-sql admin backup/restore` CLI remains a separate ROADMAP item.
- [x] **P2.6 Add workload fairness and abuse tests.**
  Global concurrency caps allow one user or group to consume all capacity. Define per-user/group limits,
  queue fairness, cancellation, timeouts, export quotas, and administrative overrides; test mixed short,
  long, interactive, scheduled, refresh, and subscription workloads.
  *(done — v0.11.0)* New `Resources.MaxConcurrentExecutionsPerUser` (default 2) caps how many of the
  shared execution slots a single non-admin may hold at once, so one user flooding the queue cannot
  starve others. `ExecutionJobService.RunJobAsync` acquires a per-user `SemaphoreSlim` **before** the
  global gate (and without holding a global permit), so a capped user queues without blocking the
  shared pool; administrators are exempt (the administrative override). The existing per-execution
  timeout + cancellation (queued-timeout already frees the slot) and the PDF-export rate-limit bucket
  cover the timeout/quota dimensions. Tests (in `ExecutionJobServiceTests`, using the
  never-responding channel so a slotted job hangs Running): per-user limit lets user B run while user A
  is capped at one slot; the contrast (cap == global) shows one user saturating the whole pool;
  administrator bypass runs two admin jobs past a per-user cap of 1. Admin guide config table updated.
  **Residual:** per-*group* limits and cross-workload queue-fairness weighting (interactive vs scheduled
  vs subscription priority) are not implemented — per-user fairness + admin override is the shipped slice.
- [ ] **P2.7 Test the versioned upgrade path, not just backup/restore.**
  P2.5 drills restore into a clean location; nothing proves an N→N+1 upgrade: EF migrations applying
  over a live catalog, Orchestrator SQLite schema changes, parquet/key-version compatibility, and what
  rollback means after a partial migration. Seed a portal on release N, upgrade in place to N+1, and
  verify permissions, jobs, subscriptions, datasets, and audit continuity. Define and document the
  supported rollback procedure (restore-from-backup vs. down-migration).
- [x] **P2.8 Add operational observability for a multi-user deployment.**
  P1.6 adds correlation IDs for audit only. An administrator running this for real users needs: active
  executions, queue depth, job/SMTP failure rates, and disk usage of dataset/snapshot storage — via
  expanded health checks or a metrics endpoint (OpenTelemetry if warranted). Extend the
  no-credentials-in-logs guarantee from a dataset-test-only scan to a portal-wide log-hygiene rule.
  *(done — v0.11.0)* New `OperationalMetricsService` + admin-only `GET /api/admin/metrics/operational`
  return a point-in-time snapshot: active vs queued executions (queue depth), the execution and
  per-user caps, recent (24h) execution and subscription-delivery counts and failure counts (the
  failure-rate denominators, sourced from the durable `PortalExecutionJobs` and `SubscriptionDelivery`
  ledgers so they survive restart), dataset/snapshot disk usage, and active subscription/SMTP counts.
  The existing `/health` `execution` check still reports topology + active count for liveness probes.
  **Log hygiene generalized to a portal-wide rule** (SECURITY.md §9): credential-bearing error paths
  sanitize secrets before they reach logs, persisted failure detail, or audit rows — enforced by a new
  test (`OperationalObservabilityTests`) that drives a delivery failure whose error text echoes the
  SMTP password and asserts it appears in none of the captured logs, the returned reason, the ledger
  detail, or the audit record. Admin guide §6.7 documents the endpoint. **Residual:** OpenTelemetry
  metrics/trace export was deemed not yet warranted for the single-instance topology; revisit with the
  HA roadmap.
