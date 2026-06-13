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
