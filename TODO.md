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

### Priority 0 — Immediate security boundaries

- [x] **P0.1 Remove plaintext SMTP credentials from generated subscription scripts.**
  `SubscriptionsController.GenerateJobScript` currently decrypts `SmtpConnection.EncryptedPassword` and
  writes `PASSWORD = '...'` into a persistent `.etlsql` file. Replace this with a runtime secret
  reference/resolver so generated job SQL contains only a non-secret SMTP alias or secret identifier.
  Scan generated scripts, job rows, logs, exceptions, and exports for known credential markers.
  *(done — v0.11.0)* Generated Orchestrator scripts are credential-free triggers containing only the
  subscription ID. SMTP credentials are decrypted only inside the portal delivery scope, composed into
  an in-memory script, sanitized from failures, and covered by trigger/history/audit secret-marker tests.
  Startup reconciliation (`SubscriptionScriptMaintenance`) rewrites pre-upgrade scripts that embedded
  decrypted credentials to the trigger form and deletes orphaned generated subscription scripts, so an
  upgraded deployment sheds persisted secrets without waiting for each subscription to be edited.
- [x] **P0.2 Reauthorize every subscription at delivery time.**
  A subscription is permission-checked when created, but its scheduled script can continue after its user
  is disabled, removed from a group, or loses report/folder permission. Route delivery through a trusted
  subscription executor that reloads the subscription owner, active state, report state, and current
  permission immediately before export/send. Denied delivery must be recorded without exposing report
  data and must not be retried as a transient SMTP failure.
  *(done — v0.11.0)* `SubscriptionDeliveryService` reloads owner/report/subscription state and current
  folder permission immediately before delivery. The Orchestrator poller dispatches successful `SUB:`
  triggers, records terminal delivery outcomes, and persists `LastTriggeredAt` so denied or repeated
  completions are not retried as SMTP failures.
- [x] **P0.3 Make privilege reduction effective for issued access tokens.**
  JWT validation currently reloads `User.IsActive` but trusts role claims minted at login. Add a
  security/version stamp or reload current roles during token validation so Admin/Publisher/
  OrchestratorManager removal takes effect immediately. Password reset, explicit token revocation, role
  change, and group/permission changes need clearly defined access-token invalidation semantics.
  Include refresh tokens explicitly: the `RefreshToken` plumbing already exists, so define what logout,
  account disable, password reset, and role change do to outstanding refresh tokens (revocation and
  rotation-on-use), not just to access tokens.
  *(done — v0.11.0)* Access tokens carry the ASP.NET Identity security stamp and validation compares it
  with current user state on every request. Role, group, folder/dataset ACL, active-state, password,
  LDAP mapping, explicit revoke/disconnect, and logout events rotate the stamp and revoke outstanding
  refresh tokens. Refresh tokens are single-use and rotate on refresh; logout invalidates all sessions
  for the current user.
- [x] **P0.4 Govern anonymous share links and embed tokens like delivery (the P0.2 problem in another
  costume).** `ReportShareLink` resolution is anonymous (`ReportsController.ResolveShareLink`,
  `[AllowAnonymous]`), and a link created by a user who is later disabled or loses folder permission
  keeps resolving. Reauthorize against the creator's current state at resolve time, add expiry defaults,
  revoke on creator disable/demotion, assert token entropy, provide an admin inventory of all
  outstanding anonymous links/embed tokens, and audit anonymous views. (P1.8 only excludes these tokens
  from configuration export — nothing covers their runtime semantics.)
  *(done — v0.11.0)* Share and embed capabilities are anonymous but resolve only while the creator is
  active and currently retains report read permission (or Admin). New capabilities default to seven-day
  expiry, use 256-bit random tokens, are explicitly revoked on creator disable/demotion, produce
  token-free anonymous-view audit events, and appear in an admin inventory with active/expired/revoked/
  permission-lost status.
- [x] **P0.5 Decide and secure the alert delivery path.** `ReportAlert` entities exist but have no
  server-side delivery service today (CRUD-only). Either bring alert delivery into the trusted
  executor/reauthorization boundary of P0.2 (alerts that email must never embed SMTP secrets and must
  recheck permission at send time) or explicitly document alerts as browser-side-only and out of P0.2
  scope. An undecided delivery path is how the persisted-SMTP-credential mistake happens twice.
  *(done — v0.11.0)* Alerts are explicitly definition-only/browser-consumed metadata. The portal does
  not evaluate thresholds, schedule alert checks, or send alert email server-side; recipient and SMTP
  fields are reserved metadata. Any future server delivery must use the trusted subscription executor
  pattern with delivery-time reauthorization and runtime-only secret resolution.

### Priority 1 — Multi-user correctness and recoverability

- [x] **P1.1 Add a durable per-job execution lease.**
  Multiple Orchestrator instances can observe the same due job and execute it. Acquire a database-backed
  lease/claim before starting a scheduled run, include owner/expiry/heartbeat fields, and safely reclaim
  abandoned leases. Prove two scheduler processes produce one execution, including crash and clock-skew
  cases. The existing global throttle is capacity control, not duplicate-execution prevention. Note:
  SQLite's single-writer model constrains what a database-backed lease can promise across processes —
  design the lease against that limit and keep it consistent with the P3.1 topology decision.
  *(done — v0.11.0)* `Jobs` gained `LeaseOwner`/`LeaseExpiresAt` (UTC ISO-8601); the claim is a single
  atomic `UPDATE ... WHERE lease free or expired` riding SQLite's single-writer guarantee, so the lease
  coordinates exactly the processes that share one job DB (consistent with P3.1). `ExecuteJobAsync` is
  the single choke point for scheduled and manually triggered runs: claim → heartbeat at lease/3 (losing
  the lease cancels the run) → release after the NextRun update. An expired lease is reclaimable, giving
  at-least-once recovery after a crash; generous lease + heartbeat absorb modest clock skew.
  `SaveJobAsync` became a real upsert — `INSERT OR REPLACE` was deleting and reinserting the row, which
  silently cleared an active lease whenever a definition was re-saved mid-run. `JobExecutionLeaseTests`
  proves: 32 parallel claims → one winner; owner-checked renew/release; expiry reclaim; lease survival
  across a definition re-save; and two scheduler instances over one store executing a due job exactly once.
- [x] **P1.2 Make subscription create/update/delete recoverable across portal DB, job DB, and files.**
  These operations currently commit independent resources in sequence. Introduce an operation record or
  transactional outbox plus idempotent reconciliation so crashes cannot leave orphan rows, plaintext or
  stale scripts, or active jobs for deleted subscriptions. Updates must use atomic file replacement.
  *(done — v0.11.0)* The subscription row is the declared source of truth and now persists `AtTime`
  (migration `SubscriptionAddAtTime`) so a lost job can be rebuilt without losing its delivery time.
  `SubscriptionOrchestration` centralizes job naming/schedule/definition rules for the controller,
  poller, status service, and reconciliation. Create commits row → script → ScriptPath → job in that
  order; update heals a missing job inline; delete removes job → file → row. Script writes are atomic
  (unique same-directory temp + move). `SubscriptionScriptMaintenance` now also converges Orchestrator
  jobs to row state at startup: removes jobs for deleted subscriptions and stale-named duplicates,
  recreates missing jobs from the row (at-least-once recovery), realigns schedule/enablement/script
  drift while preserving run bookkeeping, regenerates missing ScriptPaths, and cleans abandoned
  atomic-write temp files. `SubscriptionLifecycleRecoveryTests` covers each crash artifact plus
  idempotency. (A full transactional outbox was deliberately not introduced: startup reconciliation
  toward row state covers the crash windows with far less machinery.)
- [ ] **P1.3 Add optimistic concurrency to administrator-managed resources.**
  Add row versions/ETags to users, groups, folders, ACLs, reports, datasets, jobs, subscriptions, and SMTP
  definitions. Simultaneous edits must return a conflict with current state rather than silently applying
  last-write-wins behavior. Bulk operations need partial-failure and retry semantics.
- [ ] **P1.4 Define durable portal execution state and the supported deployment topology.**
  `ExecutionJobService`, active-refresh suppression, session cache, export rate limits, and execution
  gates are process-local. Either make job/status/refresh coordination durable and shared, or explicitly
  limit the supported portal topology to one instance. Restarts must not report vanished work as success,
  and duplicate refreshes from separate processes must be prevented.
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

### Priority 1 — Security plumbing

- [x] **P1.12 Add HTTP security headers and a Content-Security-Policy.**
  Beyond HSTS and HTTPS redirection the portal sends no CSP, `X-Content-Type-Options`, or
  `X-Frame-Options`/`frame-ancestors`. This matters doubly here: JWTs live in `sessionStorage`
  (XSS-stealable — CSP is the mitigation) and the embed-token feature requires a deliberate
  frame-ancestors policy instead of the default "frameable by anyone." Document the
  sessionStorage-vs-httpOnly-cookie token storage decision alongside the header work.
  *(done — v0.11.0)* Every response now receives CSP, nosniff, referrer, and permissions headers.
  HTML scripts use per-response nonces, inline event handlers are prohibited, and framing defaults
  to same-origin with exact external origins opt-in through `Portal:Security:FrameAncestors`.
- [x] **P1.13 Rate-limit auth endpoints and anonymous token resolution.**
  Identity lockout protects local passwords only. There is no throttle on `/auth` (user enumeration,
  refresh-token guessing) or on the anonymous share-link/embed resolve endpoints (token brute force);
  the only rate limit in the codebase is the PDF export bucket. Use the built-in ASP.NET Core
  `AddRateLimiter` on auth and anonymous endpoints. P2.6 covers workload fairness for authenticated
  users; this item covers abuse of unauthenticated surfaces.
  *(done — v0.11.0)* Built-in fixed-window policies partition by remote IP and endpoint path, reject
  without queuing, and return `429` plus `Retry-After`. Limits are configurable under
  `Portal:RateLimit`; defaults are 20 auth and 60 anonymous-token requests per minute.
- [x] **P1.14 Define rotation and provisioning for the remaining runtime secrets.**
  The dataset at-rest key now has startup validation and resumable rotation, but `Portal:Jwt:Secret`
  and the Orchestrator API keys are plaintext config with no rotation procedure. JWT secret rotation
  invalidates every session — document the procedure (or support a two-key validation ring), define
  Orchestrator key rotation across both sides of the shared secret, and prefer environment/secret-
  provider references for the running portal (P1.8 covers references only inside the export script).
  *(done — v0.11.0)* JWTs are signed only by the current secret and validated against a bounded
  current-plus-previous ring. Orchestrator authentication accepts current and previous keys during a
  coordinated cutover using constant-time digest comparison. Admin-entered Orchestrator keys are
  Data-Protection-encrypted at rest, legacy plaintext sidecars migrate automatically, and deployment
  documentation defines environment/`ENC:` provisioning, rotation order, retirement windows, and
  Data Protection key-ring backup requirements.

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
- [x] **P2.9 Gate CI on dependency vulnerability scanning.**
  THIRD-PARTY-INVENTORY covers license compliance but nothing scans for known-vulnerable packages. Add
  `dotnet list package --vulnerable --include-transitive` (or equivalent) as a CI gate and define the
  response procedure when a hit blocks the build.
  *(done — v0.11.0)* New `scripts/Test-VulnerablePackages.ps1` reuses the pre-release dependency-audit
  helpers (`scripts/lib/DependencyAudit.ps1`) to run the vulnerable-only NuGet audit — direct +
  transitive, per-project fallback, hard failure when no authoritative audit can run — and `ci.yml`
  now gates every run on it after restore. The VS Code job additionally gates on `npm audit` in both
  extension roots, mirroring the pre-release npm phase. The response procedure (advisory triage,
  CPM/version-pin fixes, inventory regeneration, explicit no-suppression risk-acceptance path) is
  documented in `SECURITY.md` §13 and referenced from the gate's failure output.

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
  that email-based self-service reset would depend on the P0.1/P0.2 trusted SMTP path) so it reads as
  a choice rather than an omission.
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

1. **P0.1 and P0.2 together:** remove persisted SMTP secrets and introduce the runtime subscription
   executor/authorization boundary. **P0.5** (alert delivery decision) rides along — it picks the same
   boundary or documents itself out of scope.
2. **P0.3 and P0.4:** immediate privilege-reduction semantics for tokens, refresh tokens, and
   anonymous share/embed links.
3. **P1.12-P1.14:** security headers/CSP, auth-surface rate limiting, and runtime secret rotation —
   small, independent, and cheap to land early.
4. **P1.1 and P1.2:** durable job claims and recoverable subscription lifecycle.
5. **P1.7-P1.11:** script-first configuration export/import and clean-server round-trip.
6. **P1.3-P1.6:** multi-admin conflicts, durable portal state, ownership, and audit guarantees.
7. **P2 lane:** hosted-service, multi-process, delivery, chaos, restore, upgrade, observability,
   dependency-scanning, and workload tests.
8. **P3 decisions:** publish explicit deployment/identity/isolation/recovery boundaries before making
   an enterprise or HA support claim.

## DATASET Hardening + Permutation/Security Verification

> Status: **planned, not started.** Design agreed; pick up Phase 1 first.
> Goal: make every DATASET permutation (machine/portal-at-rest, password/keyfile transport,
> PUBLIC/PRIVATE) work as intended, with the security boundaries proven by tests. This is
> feature-hardening first, verification second — the current code does not yet match the model below.

### Target model (decided)

- **At rest in a portal a dataset is always encrypted with a portal-managed key** ("machine"), bound to
  the portal's **service account** (Windows DPAPI CurrentUser under the service identity; Linux key file
  `chmod 600` owned by the service account) and **backed up deliberately** so it survives host
  move/restore/failover. Consumers never supply a credential.
- **Password / keyfile = a transport credential only**, to make a dataset *movable* between
  machines/portals. Supplied **at export and at publish only — never written to disk / a sidecar**. On
  publish the portal decrypts once and **re-encrypts with its at-rest key**; after publish the portal
  copy is **not movable** — the author keeps the original file. (Surface this warning at publish.)
- **Identity:** datasets get a **stable ID** + a **globally unique name**; `USE DATASET &x` resolves by
  name portal-wide. Folder is *mutable metadata* (datasets can be moved later).
- **Access:** **PUBLIC = any authenticated user with read permission on the dataset's folder** (reuse
  `FolderPermissionService`); **PRIVATE = owner + explicit dataset grants only** (ignores folder read).
- **Refresh:** transparent stale-cache refresh serves **stale-with-warning** to readers and never
  re-materializes under a consumer's identity. A forced `REFRESH DATASET` requires
  **refresh/editor/owner**; editing the source query requires **editor/owner**. This lets a user group
  operate refreshes without receiving metadata or query-edit rights. **Scheduled/system refresh jobs
  keep admin rights**.
- Threat model: at-rest encryption + compression (already SNAPPY parquet) protects moved files and other
  local users; an attacker with code-exec **as the service account** is out of scope.

### Current state vs target (gaps to close, with file:line)

- DSL/parse + crypto primitives are solid: `MachineBoundCrypto.cs`, `CryptoUtils.cs` (PBKDF2-SHA256 600k
  for PASSWORD; RSA-OAEP+AES hybrid for KEYFILE), `EncryptionOptions.cs`. Parse-level coverage in
  `tests/ETL-SQL.Tests/Reporting/DatasetPhase{2,3,4}Tests.cs`.
- **Engine bypasses ACL:** all four handlers pass a literal `"IsAdmin=true"` —
  `UseDatasetStatementHandler.cs:51`, `CreateDatasetStatementHandler.cs:59`,
  `RefreshDatasetStatementHandler.cs:37`, `ShowDatasetsStatementHandler.cs:41`. `SHOW` lists everything,
  `REFRESH` is unrestricted, PRIVATE is only folder-matched.
- **Cross-folder consumption is broken today:** `UseDatasetStatementHandler` looks up by
  `(name, consumer's folder)` (lines 35, 51) and `DatasetRegistryService.Lookup` filters
  `Name == name && FolderPath == folderPath` (line 69) — a dataset created in folder A can't be consumed
  from folder B at all, PUBLIC or not. Global-unique-name resolution fixes this.
- **Consume/refresh hard-codes `ENCRYPT=MACHINE`** (`UseDatasetStatementHandler.cs:120,168`,
  `RefreshDatasetStatementHandler.cs:81`) with no transport/publish step. Under the target model the
  at-rest read is correct; the transport concern moves to an explicit export/publish.
- **Sidecar leaks the password in cleartext:** `CreateDatasetStatementHandler.WriteSidecarScript`
  (lines 260-275). Target: never write the credential to disk.
- **Folder linkage mismatch:** `Dataset.FolderPath` is a *string* (`PortalEntities.cs:292`) but
  `FolderPermissionService` keys on `FolderId` (`FolderAcls.FolderId`). PUBLIC-via-folder-permission
  needs datasets linked to a folder by ID. Engine can reuse the identity-agnostic overload
  `FolderPermissionService.GetEffectivePermissionAsync(folderId, ISet<int> groupIds)` (line 41) with the
  threaded caller's group IDs.

### Phase 1 — Core model correctness & security (default path; independently shippable)

- [x] **1a. Stable ID + globally unique name.** *(done — branch v0.11.0)* `Name` now carries the unique
  index (`PortalDbContext.cs`; migration `20260610143113_DatasetGlobalUniqueName`). Registry
  `Lookup`/`Exists`/`SetStale`/`Delete` are **by name**; `RegisterOrUpdate` returns the stable Id;
  `BuildDatasetFilePath(int datasetId, string name)` keys the parquet filename on the Id so a folder
  move/rename never rewrites the file. `CreateDataset` registers-first to allocate the Id. The four
  handlers + `DatasetController` + tests updated; new cross-folder regression in `PortalIntegrationTests`
  (`DatasetRegistry_ResolvesByGlobalNameRegardlessOfFolder`). CREATE rejecting a duplicate name now
  surfaces as the DB unique-constraint error — a friendly pre-check is deferred to 1b/1c. EF migration
  drops `(FolderPath, Name)`; note: a catalog with the same name in two folders must be de-duped first.
- [x] **1b. Link datasets to a folder by ID + folder-permission access (PUBLIC).** *(done — branch v0.11.0)*
  Added `Dataset.FolderId` (nullable FK, migration `DatasetAddFolderId`). The dataset→folder link is
  derived from the **executing report**: the report id is threaded into the engine
  (`Evaluator.DatasetOwningReportId`, set by `DashboardService`/`SessionCache`/`ExecutionJobService`
  like the 1c caller context), `CreateDataset` stamps `OwningReportId`, and `RegisterOrUpdate` resolves
  `FolderId = Report.FolderId`. `CanReadAsync` PUBLIC branch now requires Read on `FolderId` via
  `FolderPermissionService.GetEffectivePermissionAsync`; PUBLIC with no folder → any authenticated
  caller (unauthenticated/unset denied). This also **revived the PRIVATE owner check** (`OwningReportId`
  is now populated). `Folder.Path` is logical, not the script dir, so the link could not come from
  `FolderPath`. Tests: `DatasetRegistry_PublicGatedByFolderReadPermission` + updated
  `DatasetRegistry_FiltersPrivateDatasetsByOwnerAclAndAdmin` (no-folder PUBLIC requires auth).
- [x] **1c. Thread caller identity into the engine (close the ACL bypass).** *(done — branch v0.11.0)*
  Added `Evaluator.DatasetCallerContext` beside `DatasetRegistry`; the four handlers now forward it to
  `Lookup`/`ListAll` instead of the literal `"IsAdmin=true"`, so `DatasetRegistryService.CanReadAsync`
  (owner + `DatasetAcl` grants) is the access authority for PRIVATE. The **1a interim folder guard is
  removed**. Portal wiring: `DashboardService` takes a caller-context ctor arg and sets it where it
  assigns the registry; `SessionCache` passes `"UserId={userId}"` (interactive viewing as the real user);
  `ExecutionJobService` snapshot path passes `"IsAdmin=true"` (trusted server-side refresh — the HTTP
  trigger is already permission-gated, so the user-vs-scheduled refresh *write* split stays 1d). Unset =
  fail-closed (PRIVATE denied, PUBLIC allowed); non-portal standalone unchanged (registry null). Tests:
  `DatasetPhase4Tests.UseDataset_PrivateWithoutAccess_Denied` + `ShowDatasets_ForwardsCallerContextToRegistry`.
  (PUBLIC is still an unconditional allow in `CanReadAsync` — the folder-permission gate is **1b**.)
- [x] **1d. Refresh split + serve-stale-with-warning (option a).** *(done — branch v0.11.0)* `USE DATASET`
  is now read-only: a stale cache is served with a yellow staleness warning and **never re-materialized
  under the consumer's identity** (`RematerialiseAndRefresh` deleted from `UseDatasetStatementHandler`);
  a never-materialized dataset errors instead of re-running the source. `REFRESH DATASET` requires the
  independent Refresh/Editor/Owner capability via `IDatasetRegistry.CanRefreshAsync`;
  `CREATE OR ALTER DATASET` (over an existing dataset) remains Editor/Owner-only via
  `IDatasetRegistry.CanEditAsync`. The four-level ACL hierarchy is Viewer < Refresh < Editor < Owner,
  with a migration preserving existing Editor/Owner grants.
  `SHOW DATASETS` already caller-filtered (1c). Re-materialization now happens only via the producing
  report's `CREATE` (owner or scheduled/admin job). Tests: `DatasetPhase4Tests` refresh/create-or-alter
  denial + serve-stale + never-materialized; `PortalIntegrationTests.DatasetRegistry_CanEdit_OnlyOwnerEditorAndAdmin`.
- [x] **1e. Portal-managed at-rest key.** *(done — branch v0.11.0)* Dataset parquet is now encrypted at
  rest with a portal-managed key — `Portal:Dataset:AtRestKey` (base64 config secret, like
  `Portal:Jwt:Secret`), threaded into the engine as `Evaluator.DatasetAtRestKey` (set by
  `DashboardService`/`SessionCache`/`ExecutionJobService`). The three implicit-MACHINE sites
  (`Use`/`Refresh`/`Create` handlers) route through a shared `DatasetAtRestOptions.Apply`: a configured
  key → `ENCRYPT=PASSWORD` with that key (reuses the existing AES-256/PBKDF2 `CryptoUtils` path — no new
  primitive); **unset → falls back to host `ENCRYPT=MACHINE`** (dev/standalone unchanged). The cache is
  portal-bound and portable: back the key up with config and move it with the portal; losing it makes
  caches unreadable (re-materialise to recover). Tests: `DatasetPhase4Tests` at-rest round-trip +
  wrong-key-fails. NOTE: explicit `ENCRYPT=PASSWORD|KEYFILE` on `CREATE` (transport) and the
  scheduled-refresh-job key embedding are revisited in **Phase 2**.

### Phase 2 — Portable move (the "movable" story)

> **Phase 2 COMPLETE (2a-2d) on branch v0.11.0.** Commits: 2d 8796ffb8, 2a 588220c0, 2b 9aa6594d, 2c <this>.

- [x] **2a. EXPORT DATASET** *(done)* `&x TO '<file>' ENCRYPT = PASSWORD|KEYFILE [PASSWORD=… | KEYFILE=…]` —
  decrypts the at-rest cache, re-encrypts to the target with the transport credential (supplied at export,
  never persisted). AST `ExportDatasetStatement` + EXPORT dispatch → `ReportParser.ParseExportDataset` +
  `ExportDatasetStatementHandler` (reuses `EncryptionOptions`/`CryptoUtils`).
- [x] **2b. PUBLISH/IMPORT** *(done)* `PUBLISH DATASET FROM '<file>' AS &x [INTO '<folder>'] [ACCESS …]
  ENCRYPT = …` — decrypt once with the credential, re-encrypt with the portal at-rest key, register.
  Published copy is at-rest-bound (not movable); keep-your-original warning. New `Dataset.CreatedBy`
  (publisher owner; migration `DatasetAddCreatedBy`); `CanReadAsync`/`CanWriteAsync` fall back to
  `CreatedBy`; folder resolved from target logical path.
- [x] **2c. Repurpose `ENCRYPT=PASSWORD|KEYFILE` on `CREATE DATASET` to transport-only** *(done)* — in a
  portal the at-rest cache always uses the portal key (`BuildParquetOptions` ignores the statement's
  ENCRYPT clause when an at-rest key is set); the CREATE credential throw now only applies in non-portal
  mode. Lint realigned: `DatasetEncryptionModeRule` reworded (transport-only/ignored-at-rest/use EXPORT),
  `DatasetEncryptWithoutKeyRule` repointed to EXPORT/PUBLISH (where the credential is required).
- [x] **2d. Remove the cleartext-credential sidecar** *(done)* — deleted `WriteSidecarScript` +
  `EncryptLabel` from `CreateDatasetStatementHandler`.

### Phase 2 follow-up — Security, metadata, and lifecycle correctness

> The portable EXPORT/PUBLISH flow is implemented, but the items below are required before the target
> model can be considered production-hardened.

- [x] **2e. Keep the portal at-rest key out of persisted scheduled-job SQL.** *(done — v0.11.0)*
  Scheduled dataset jobs now persist only a no-secret trigger, so neither the portal key nor source
  credentials enter job SQL. The runtime **`ENCRYPT = PORTAL`** mechanism remains available for other
  persisted connector definitions: `EncryptionOptions` resolves the key from
  **`ETLSQL_DATASET_ATREST_KEY`**, which the portal exports from `Portal:Dataset:AtRestKey`.
  `DatasetRefreshJobSecurityTests` covers PORTAL round-trip/env failure and the no-secret trigger.
- [x] **2L. Make scheduled dataset refresh functional.** *(done — v0.11.0)*
  Replaced the serialized `BEGIN … END` placeholder with a parseable, credential-free orchestrator
  trigger. `IDatasetRegistry.RegisterRefreshJobAsync` maps that trigger to the owning report in
  `DatasetJobs`; `OrchestratorPollerService` observes successful completion and queues the report through
  the portal's keyed `ExecutionJobService`, preserving connection setup, report identity, registry access,
  and at-rest encryption context. Repeated report runs upsert the mapping. A dataset without an owning
  report logs that durable `REFRESH EVERY` scheduling is unavailable instead of creating a false job.
- [x] **2f. Centralize portal/engine dataset authorization.** *(done — v0.11.0)*
  Added `DatasetPermissionService` as the shared authority used by `DatasetRegistryService` and every
  `DatasetController` endpoint. PUBLIC datasets now require folder Read when linked to a portal folder
  (with the documented authenticated-user fallback for legacy rows without a folder); PRIVATE datasets
  require owner/publisher status or an explicit grant; admins remain owners; and ACLs consistently elevate
  eligible readers to Editor/Owner. Added controller/registry regressions for folder-gated PUBLIC access
  and `Dataset.CreatedBy` publisher ownership; the existing seeded dataset permission matrix also passes.
- [x] **2g. Fix portal-key dataset viewing — decrypt by config, not the stored mode.** *(done — v0.11.0)*
  `DatasetViewerService.LoadCachedAsync` no longer decides decryption from `Dataset.EncryptionMode` (which
  records the CREATE transport clause and is unreliable at rest). New `ResolveAtRestDecryptOptions`: when
  `Portal:Dataset:AtRestKey` is set every cache decrypts with the portal key (ENCRYPT=PASSWORD); else the
  stored mode applies (MachineBound→MACHINE, None→plaintext); a legacy Password/KeyFile record with no key
  surfaces a clear error. New `DatasetViewerServiceTests` (portal-key/wrong-key/MACHINE/plaintext/publish-
  shape) over a direct temp-SQLite `PortalDbContext`; existing `DatasetControllerTests` unchanged.
  Remaining bit of the original item — explicit at-rest **version** metadata + migrating legacy
  Password/KeyFile rows + key **rotation** — folded into **2i**. (`ColumnSchema`/`RowCount` not populated
  for PUBLISH is a small separate follow-up; rows still read from the parquet's own schema.)
- [x] **2h. Make CREATE/REFRESH/PUBLISH/EXPORT failure-atomic.** *(done — v0.11.0)*
  Added a shared same-directory dataset file transaction. All four write paths now produce a uniquely
  named `.parquet` staging file, reject missing/empty output, and atomically replace the destination only
  after the write succeeds. A same-directory backup remains until the registry update commits, so failed
  CREATE/REFRESH metadata updates restore the previous readable cache; EXPORT failures preserve an
  existing target. Failed PUBLISH removes its allocated row and partial files so the global name is
  immediately retryable. Dataset deletion and report deletion remove only path-guarded managed files
  inside `DatasetRootPath`. Portal startup reconciliation removes abandoned staging/backup files,
  missing-file catalog rows, and unreferenced managed `<name>_<id>.parquet` files while leaving unrelated
  exports untouched. Failure-injection tests cover refresh rollback, publish credential failure, direct
  and report-owned deletion, and orphan reconciliation.
- [x] **2i (core). Fail closed on a missing/weak at-rest key.** *(done — v0.11.0)* The portal no longer
  silently falls back to host MACHINE encryption. New `DatasetAtRestKeyValidationService` (an
  `IHostedService` mirroring `JwtSecretValidationService`) validates `Portal:Dataset:AtRestKey` at startup
  via the pure `DatasetAtRestKeyValidator`: a set key must be base64 and decode to ≥ 32 bytes; an unset key
  is **Fatal** (the app `StopApplication()`s) unless the new `Portal:Dataset:AllowMachineFallback=true` dev
  opt-in is set (then a Warn). `PortalWebFactory` strips hosted services, so tests are unaffected. Tests:
  `DatasetAtRestKeyValidatorTests`.
- [x] **2i (follow-up). Version + rotate the at-rest key.** *(done — v0.11.0)* Added nullable
  `Dataset.AtRestKeyVersion` with an EF migration; portal writes stamp the configured non-secret
  `AtRestKeyVersion`, and version-aware registry/viewer reads resolve either the current key or a
  configured `PreviousAtRestKeys` entry. `LegacyAtRestKeyVersion` explicitly identifies unversioned rows
  during the first rotation; leaving it unset stamps existing current-key rows without rewriting them.
  Admin-only `POST /api/admin/datasets/rotate-at-rest-key` re-encrypts one guarded managed file at a time,
  atomically updates its version, continues past failures, and is safe to rerun. Rotation also normalizes
  stale Password/KeyFile metadata to portal-managed at-rest semantics. Startup validation checks current,
  previous, and legacy version mappings. The administrator guide now defines first-run provisioning,
  coordinated backup/restore, rotation, resume, verification, and old-key retirement. Tests cover
  validation, previous-version reads, successful re-encryption, legacy stamping, and resumability.
- [x] **2j. Authorize PUBLISH target folders and define system ownership.** *(done — v0.11.0)*
  Added a registry publish preflight that resolves the target folder and requires folder `Manage` before
  `PUBLISH DATASET` allocates a row. Interactive publications set `CreatedBy` to the caller; trusted
  admin/system publication falls back to the target folder owner, so ownership is never null. Successful,
  denied, and post-authorization failed attempts write sanitized `PUBLISH_DATASET` audit events without
  credentials. Tests cover missing/Read-only/Manage targets, system ownership, denial before allocation,
  audit sanitization, and the cross-portal export/publish round-trip.
- [x] **2k. Implement dataset move semantics.** *(done — v0.11.0)*
  Added `POST /api/datasets/{id}/move`. The caller must have folder `Manage` on both the current and
  destination folders (admins may recover legacy rows with no folder); the destination must exist.
  The operation updates `FolderId` and `FolderPath` together, preserves the stable dataset Id and Parquet
  path, invalidates the owning report's sessions, and writes a `MOVE_DATASET` audit event. Regression
  coverage proves denial without destination rights and verifies the successful move after permission is
  granted.

### Phase 3 — Verification deck (scripts + xUnit)

- [x] **Runnable example deck** `samples/08_Reporting/datasets/` + `README.md` (tiny inline/CSV seed; no
  external deps; reuse keyfile at `samples/10_Kitchen_Sinks/test_key/`). Datasets deployed **separately**
  from the reports that consume them:
  - `01_deploy_datasets.etlsql` — CREATE `&sales_public` + `&sales_private`; ends with `SHOW DATASETS`.
  - `02_report_public_consumer.etlsql` — different folder; `USE DATASET &sales_public` → succeeds.
  - `03_report_private_allowed.etlsql` — owner/granted → succeeds.
  - `04_report_private_denied.etlsql` — non-owner, no grant → PRIVATE error.
  - `05_export_then_publish.etlsql` (+ runbook) — EXPORT w/ password/keyfile, then PUBLISH → consume by
    ACL only; shows "not movable after publish."
  - `README.md` — manual portal walkthrough: 2nd user sees PUBLIC (folder read), 403 on PRIVATE, grant
    flips it, refresh permission, "copy the portal .parquet elsewhere → fails."
  *(done — v0.11.0)* Added five parser-verified scripts with inline seed rows, separate producer and
  consumer deployment instructions, an expected PRIVATE-denial case, and PASSWORD plus RSA KEYFILE
  export/publish round trips. The portal runbook covers folder/read/grant behavior, independent Refresh
  permission, at-rest-file non-portability, wrong-credential cleanup, secret-persistence inspection, and
  the distinction between local syntax checks and identity-aware portal execution.
- [x] **Automated xUnit** — new `tests/ETL-SQL.Tests/Reporting/DatasetSecurityMatrixTests.cs` + extend
  `tests/ETL-SQL.ReportPortal.Tests/DatasetControllerTests.cs`. Build on `PortalIntegrationTests.cs`
  (real registry, ~920-1006) and crypto round-trips in `DatasetPhase2Tests.cs`:
  *(done — v0.11.0)* `DatasetSecurityMatrixTests` provides deterministic portal-key, PASSWORD, and
  generated RSA KEYFILE portability/failure assertions. `DatasetPhase4Tests` covers global-name
  round trips, stale reads, refresh/edit separation, export/publish, rollback, and plaintext-secret
  scanning across metadata and persisted files; `DatasetRefreshJobSecurityTests` verifies the
  SQLite-backed scheduled definition is parseable and contains neither source SQL nor the at-rest key.
  `DatasetControllerTests` now compares real HTTP and registry decisions for public folder readers,
  private owners/grants/outsiders, and administrators, in addition to endpoint-specific list, data,
  export, refresh, edit, move, delete, ACL, orphan-owner, and status coverage. Portal integration,
  viewer, storage-maintenance, validator, rotation, execution-trust, and concurrent-snapshot tests cover
  the remaining metadata, key lifecycle, failure atomicity, folder lifecycle, and trusted-scheduler rows.
  1. **Crypto portability (in-process — no 2nd machine):** at-rest key decrypts locally, swapped key
     throws; transport PASSWORD right/wrong; transport KEYFILE right/missing/wrong; ciphertext ≠
     plaintext. (Deterministic CI assertion on the Linux/keyfile path; Windows binds via DPAPI.)
  2. **Default round-trip:** CREATE folder A → `USE` from folder B by global name → rows match (red today).
  3. **Access model (1b/1c):** PUBLIC consumable with folder read, denied without; PRIVATE denied to
     non-owner, allowed to owner + explicit `DatasetAcl` grant; non-admin `SHOW` lists only visible.
  4. **Refresh split (1d):** non-owner stale → cached + warning, no re-run; `REFRESH` denied to viewer
     and allowed to refresh/editor/owner; query edits remain editor/owner-only; scheduled/system
     (admin) refreshes.
  5. **Export→Publish (Phase 2):** export w/ password/keyfile, re-import, decrypt once, consume by ACL
     with no credential; assert published copy is at-rest-encrypted and credential never sidecar'd.
  6. **Portal/engine parity:** matrix every HTTP dataset endpoint against registry/`USE DATASET` for
     PUBLIC folder-read/no-read, PRIVATE owner/publisher/grants/no-grant, and admin. The same identity
     must receive the same decision through both paths.
  7. **Negatives:** duplicate global name rejected; orphaned `OwningReportId` → PRIVATE inaccessible to
     former owner; export missing credential → clear error.
  8. **Secret non-persistence:** scheduled job definitions, SQLite job rows, logs, exceptions, snapshots,
     and generated scripts never contain the portal at-rest key or transport credentials.
  9. **Metadata/viewer parity:** CREATE with MACHINE/PASSWORD/KEYFILE and PUBLISH with PASSWORD/KEYFILE
     all produce portal-managed at-rest files that the web viewer/API can read without transport creds;
     migrate a legacy metadata row and prove it remains readable.
  10. **Failure atomicity:** wrong publish password, invalid keyfile, encryption failure, registry failure,
      and cancelled refresh leave no blocking row/partial export/orphan plaintext; failed refresh keeps
      the previous cache readable; concurrent readers see either the old or new complete snapshot.
  11. **Key lifecycle:** missing/invalid/weak production key fails startup; backup/restore with the same
      key works; wrong key fails cleanly; key rotation re-encrypts resumably and records the new version.
  12. **Folder lifecycle:** publish to missing/unauthorized folder denied before row allocation; dataset
      move requires source/destination rights, updates both folder fields, and does not rename/rewrite the
      parquet file; delete/report cleanup removes only managed files inside `DatasetRootPath`.
  - Run: `dotnet test ETL-SQL.slnx --filter "Category!=Integration&Category!=Performance&Category!=SLT"`
    (Portal tests use WebApplicationFactory — no Docker).

### Phase 4 — Docs / residual decisions

- [x] Update `Docs/Architecture/Reporting.md` (already stale) + user-facing portal docs: at-rest-vs-
  transport model, "not movable after publish / keep your original," PUBLIC=folder-read /
  PRIVATE=grant, at-rest key backup requirement. *(done — v0.11.0)* Replaced the obsolete temp-only
  dataset and unsafe-snapshot descriptions with current registry, caller-context, ACL, atomic file,
  key-version, rotation, export/publish, and reconciliation architecture. The Report-SQL guide now
  includes `ACCESS PUBLIC|PRIVATE` in the CREATE signature/reference and accurately describes
  host-bound standalone encryption.
- [x] Document `EXPORT DATASET` and `PUBLISH DATASET` in `Docs/Reference/Grammar.md`,
  `Docs/Report_SQL_Guide.md`, keyword help, language-server/VS Code completion and syntax surfaces.
  *(done — v0.11.0)* Added complete PASSWORD and KEYFILE signatures/examples, read and destination
  `Manage` authorization requirements, global-name behavior, atomic failure/retry semantics, and explicit
  transport-credential non-persistence. Corrected the stale Report-SQL claim that portal CREATE
  PASSWORD/KEYFILE modes make the managed cache portable: portal storage always uses the portal at-rest
  key, and portability is EXPORT→PUBLISH only. Added `$export-dataset` and `$publish-dataset` snippets,
  expanded VS Code highlighting for dataset transport clauses, regenerated the syntax index, and updated
  snippet-library coverage.
- [x] Document portal at-rest key provisioning, validation, backup/restore, key-version metadata,
  rotation/recovery, and the explicitly supported development fallback. Add an operator runbook for
  orphan reconciliation and interrupted rotation. *(done — v0.11.0)* The administrator guide now
  defines fatal startup validation, coordinated key/database/file backup and restore, the development
  machine fallback, legacy stamping, resumable per-dataset rotation and key retirement, interrupted
  rotation recovery, and the exact startup reconciliation deletion boundary and operator procedure.
- [x] Confirm scheduled-refresh-as-admin is the only standing "trusted" path. *(done — v0.11.0)*
  Removed the implicit administrator context from ordinary `EnqueueExecution` and user-triggered
  `EnqueueRefresh` jobs. Execution jobs now carry an explicit trust flag and derive either
  `UserId=<caller>` or `IsAdmin=true`; only `OrchestratorPollerService` opts into the trusted form.
  Regression tests prove that `UserId=0` alone does not grant trust. Remote orchestrator workers do not
  host the portal dataset registry, so no portal dataset trust context is delegated to them.
- [x] Decide and document ownership/audit semantics for datasets published by admin, scheduled, or
  system identities, and the required permission level for publishing/moving into a folder.
  *(done — v0.11.0)* Report-created datasets remain owned through their owning report; interactive
  PUBLISH is owned and audited as the actual caller, including administrators; userless trusted system
  PUBLISH falls back to the destination folder owner; refresh and move preserve ownership. Publishing
  requires destination `Manage`, and moving requires `Manage` on source and destination, with the normal
  administrator bypass. Execution/session caller contexts now preserve both `UserId` and the Admin role
  so authorization does not erase accountability.

### Files to modify / add (representative)

- Schema/registry: `src/ETL-SQL.ReportPortal/Data/PortalEntities.cs` (add `FolderId`, unique `Name`),
  new EF migration, `src/ETL-SQL.ReportPortal/Services/DatasetRegistryService.cs` (lookup-by-name,
  centralized access rule), `src/ETL-SQL.Core/Data/IDatasetRegistry.cs` (signatures).
- Engine: `src/ETL-SQL.Engine/Evaluator.cs` (caller-context field), the four
  `Handlers/{Use,Create,Refresh,ShowDatasets}StatementHandler.cs` (threaded caller, refresh split,
  at-rest read), `src/ETL-SQL.ReportHosting/DashboardService.cs` + portal `ExecutionJobService` (set
  caller). Remove sidecar secret.
- At-rest key + transport: `CryptoUtils`/`EncryptionOptions`; new EXPORT/PUBLISH AST + parser
  (`ReportAst.cs` / `ReportParser.cs` / `SystemParser.cs`) + handler(s).
- Security/lifecycle follow-up: central dataset permission service shared by registry/controller;
  `DatasetViewerService` at-rest metadata support; portal key validation/version/rotation service;
  atomic dataset file writer and orphan reconciliation; publish/move authorization and audit.
- Lint: `Analysis/Linting/Rules/DatasetEncrypt*Rule.cs` realign to transport-only.
- Examples: `samples/08_Reporting/datasets/*`. Tests: new `DatasetSecurityMatrixTests.cs`, extend
  `DatasetControllerTests.cs` + `DatasetPhase3/4Tests.cs`.

### Verification

1. `dotnet build ETL-SQL.slnx` — clean.
2. `dotnet test … --filter "Category!=Integration&Category!=Performance&Category!=SLT"` — matrix green;
   the cross-folder global-name `USE`, the PRIVATE cross-user denial, and the export→publish round-trip
   (all red before Phase 1–2) pass.
3. Headless deck:
   `dotnet run --project src/ETL-SQL.App -- run samples/08_Reporting/datasets/01_deploy_datasets.etlsql`
   then `02_`–`05_`.
4. Optional manual portal pass via the deck README checklist.
5. Inspect persisted job definitions and portal logs for known test credentials/key markers — zero
   matches. Force publish/refresh failures and cancellation; verify no plaintext temp files, partial
   ciphertext, orphan registry rows, or lost last-good cache remain.
6. Start portal with missing/invalid production key (must fail), restore with the backed-up key (datasets
   readable), then execute the rotation runbook and verify every dataset records the new key version.

> Convention: INT/TINYINT/BIGINT all materialize as `decimal` at runtime — dataset row assertions use
> `m` suffixes / `Convert.ToDecimal`, never int/long literals.

---

## Fresh-Eyes Review Findings (2026-06-11)

> Source: full-repo review against AGENTS.md / GOALS.md. Items below are **new** — they do not
> duplicate the Enterprise Hardening P0–P3 lanes above (cross-references noted where adjacent).
> Same priority convention as above.

### Security

- [x] **R1 (P0). LDAP login silently re-activates disabled accounts.** *(done — v0.11.0)*
  `AuthController.Login` now rejects any portal-disabled account up front (generic 401 + audit event)
  regardless of identity provider, and the LDAP sync no longer touches `IsActive` — only an
  administrator re-enables an account. Regression:
  `LdapAuthTests.Login_DisabledLdapUser_IsRejectedAndStaysDisabled`.
- [x] **R2 (P1). Hash refresh tokens at rest.**
  Refresh tokens are stored as plaintext in `RefreshTokens.Token` (`AuthController.cs:204-210`) and
  looked up by raw value. A portal DB file/backup leak lets an attacker mint sessions for any user for
  up to `RefreshExpiryDays`. Store `SHA256(token)` and hash the presented value on lookup — no schema
  semantics change. (Rotation/invalidation semantics remain P0.3; this is at-rest protection.)
  *(done — v0.11.0)* Login and refresh return the raw bearer value once, while SQLite stores only its
  SHA-256 digest. Refresh hashes presented values before lookup, and regression tests assert the raw
  value never equals the persisted token.
- [x] **R3 (P1). Replace the hardcoded first-run admin password.** *(done — v0.11.0)*
  New `Portal:FirstRun:AdminPassword` provisions the seed password; when unset, a random password is
  generated and logged once under the `Portal.FirstRun` category — no well-known default remains.
  Seeding now reads the DI-resolved `PortalConfig` so test hosts can pin the password; the
  WebApplicationFactory fixtures, the Docker portal fixture (`Portal__FirstRun__AdminPassword` env),
  the administrators guide, and QUICKSTART were updated accordingly.
- [x] **R4 (P1). Dataset access-level change should require Manage, not Edit.**
  `PATCH /api/datasets/{id}` lets a dataset **Editor** flip `AccessLevel` Private→Public
  (`DatasetController.cs:325-339`), widening exposure to every folder reader. ACL grant/revoke
  correctly requires Manage; the access-level flip is the same class of operation and should be gated
  the same. Keep TTL/metadata edits at Editor.
  *(done — v0.11.0)* An actual access-level change now requires Manage; re-stating the current level
  and TTL/metadata edits remain Editor-level. Regression:
  `DatasetControllerTests.Update_AccessLevelChangeRequiresManage_NotEdit`. Rode along: the scripted
  `ALTER DATASET` connector path sent `PUT api/datasets/{id}` but the portal only exposes PATCH
  (every scripted alter 405'd); the connector now sends PATCH, so it shares the same gate.
- [x] **R5 (P2). Purge expired/revoked refresh tokens and detect reuse.**
  Nothing deletes `RefreshTokens` rows — the table grows forever, and presenting an already-revoked
  token is not treated as a theft signal (standard response: revoke the user's whole token family).
  Add a periodic cleanup and reuse detection alongside the P0.3 invalidation work.
  *(done — v0.11.0)* Replaying a revoked-but-live refresh token now invalidates every session and
  refresh token for the user (via `SecuritySessionService`) and writes a `REFRESH_TOKEN_REUSE` audit
  event; the response stays a generic 401. `RefreshTokenMaintenanceService` purges expired rows
  hourly; revoked-but-unexpired rows are retained deliberately as the reuse-detection evidence.
  Regression: `AuthSessionInvalidationTests.RefreshTokens_AreHashedAtRestAndRotateOnUse` (family
  revocation on replay) + `RefreshTokenMaintenanceTests.PurgeExpired_DeletesOnlyExpiredRows`.

### Bugs / correctness

- [x] **R6 (P1). `ExecutionJobService.RunJobAsync` can leak the concurrency gate and strand refreshes.**
  *(done — v0.11.0)* A timeout while queued now lands in a dedicated catch that marks the job
  Cancelled, clears the `_activeRefreshes` debounce entry, and records the status — without releasing
  a gate it never acquired. The Running-status update moved inside the guarded region, and
  `UpdateReportRefreshStatusAsync` swallows-and-logs DB failures so a transient SQLite busy error can
  no longer leak the gate or strand the job. Regression:
  `ExecutionJobServiceTests.RefreshTimedOutWhileQueued_ReachesTerminalStateAndFreesGateAndDebounce`.
- [x] **R7 (P2). `EnqueueRefresh` debounce race can throw.**
  `if (!_activeRefreshes.TryAdd(...)) return _activeRefreshes[reportId];`
  (`ExecutionJobService.cs:97-99`) — if the in-flight refresh completes between the failed `TryAdd`
  and the indexer read, the indexer throws `KeyNotFoundException` → 500 to the caller. Use
  `TryGetValue` with a retry/fall-through to enqueue.
  *(done — v0.11.0)* The claim is now a `TryAdd`/`TryGetValue` loop: if the in-flight refresh
  completes between the two, the enqueue retries the claim instead of throwing.
- [x] **R8 (P2). `_jobs` dictionary is never evicted.**
  Completed/failed `ExecutionJob` entries live forever in `ExecutionJobService._jobs` — unbounded
  memory growth on a long-running portal. Evict completed jobs after a retention window (keep the
  status queryable for, say, 24h). Related to P1.4 (durable job state) but worth fixing in-process now.
  *(done — v0.11.0)* Terminal jobs older than `CompletedJobRetention` (24h) are evicted on each
  enqueue; running/pending jobs are never evicted. Regression:
  `ExecutionJobServiceTests.Enqueue_EvictsTerminalJobsPastRetention_KeepsRecentOnes`.
- [x] **R9 (P2). `SessionCache.GetOrCreate` races leak DashboardServices.**
  Two concurrent requests for the same (report, user) both construct a `DashboardService`; the loser
  is overwritten in `_sessions[key] = entry` and **never disposed** (`SessionCache.cs:43-66`). The
  script-path-change branch also overwrites without disposing the old entry. Use `GetOrAdd`-with-lazy
  or dispose the displaced entry. Also note: the session key is (reportId, userId) but the caller
  context now embeds `IsAdmin` — an admin-elevated session created before role removal keeps serving
  with admin dataset context until eviction (P0.3-adjacent; document or key on the role too).
  *(done — v0.11.0)* `GetOrCreate` is now an optimistic `TryAdd`/`TryUpdate` loop: every displaced or
  race-losing service is disposed, and the caller context is part of the session identity, so a role
  flip (admin↔user) replaces the session instead of serving stale admin dataset context. Regression:
  `SessionCacheTests` (same-instance reuse, role/script replacement, concurrent convergence).
- [x] **R10 (P2). `OrchestratorPollerService` watermark can skip completions.**
  `_lastPollTime = DateTime.UtcNow` is set after processing (`OrchestratorPollerService.cs:115`), so
  any job that completed between the query and the assignment is never observed; a mid-loop exception
  still advances the watermark and silently drops the remaining completions. Advance the watermark to
  the max `EndTime` actually processed. Also parse `EndTime` with
  `CultureInfo.InvariantCulture` + `DateTimeStyles.RoundtripKind` (`:141`) — the current
  `DateTime.TryParse` is culture/kind-sensitive against the "o"-format value the query compares.
  *(done — v0.11.0)* Polls use a bounded absolute-time window via SQLite `julianday`, parse timestamps
  with invariant round-trip semantics, and advance only through each successfully handled completion.
- [x] **R11 (P2). Dead write in `DatasetRegistryService.RegisterOrUpdate`.**
  Line 57 sets `existing.EncryptionMode = MachineBound` when a portal key is configured, then line 68
  unconditionally overwrites it with `metadata.EncryptionMode`. Harmless today only because the viewer
  decrypts by config (2g), but the stored mode stays misleading and the 2i rotation normalization
  relies on accurate metadata. Reorder or delete one of the writes.
  *(done — v0.11.0)* The stored mode now describes the cache at rest: with a portal key configured a
  cache-bearing row is stamped `MachineBound` (matching rotation normalization) and the statement's
  transport clause cannot overwrite it; without a portal key the statement's mode applies. Regression:
  `DatasetRegistryMetadataTests`.
- [x] **R12 (P3). `DatasetDto.IsEncrypted` reports the wrong fact.**
  `IsEncrypted: !string.IsNullOrWhiteSpace(d.ParquetFilePath)` (`DatasetController.cs:519`) means
  "has a cache file," not "encrypted." Derive from encryption mode/at-rest key state or rename the
  DTO field.
  *(done — v0.11.0)* `IsEncrypted` now additionally requires `EncryptionMode != None`, which is
  accurate once R11 keeps the stored mode truthful.

### Performance / operational

- [x] **R13 (P1). Report snapshots accumulate without bound.**
  Every execution writes a unique `report_{id}_{jobId}.snapshot.json` and a `ReportSnapshots` row;
  nothing deletes old manifests or rows. A report refreshed every 5 minutes produces ~105k files/year.
  Add retention (keep last N per report) to complement `DatasetStorageMaintenance`, and surface disk
  usage via the P2.8 observability item.
  *(done — v0.11.0)* After each successful execution the portal keeps the newest
  `Portal:Resources:SnapshotRetentionPerReport` snapshots (default 20) and removes older rows plus
  their path-guarded manifest files; pruning is best-effort and never fails a completed run.
  Documented in the administrators guide. Disk-usage surfacing remains under P2.8. Regression:
  `ExecutionJobServiceTests.PruneSnapshots_KeepsNewestPerReport_DeletesRowsAndFiles`.
- [x] **R14 (P2). Per-request DB hit in `OnTokenValidated`.**
  Every authenticated request runs a `Users.AnyAsync` query (`Program.cs:123-135`); the P0.3 role/
  security-stamp reload will make this heavier. Add a short (~30s) memory-cache of the user's active/
  stamp state so revocation latency stays bounded without a DB roundtrip per request.
  *(done — v0.11.0)* New `UserSecurityStateCache` (30s TTL over `IMemoryCache`) serves the
  active-flag/stamp check; `SecuritySessionService` evicts on stamp rotation, so in-process
  revocation is immediate and cross-process staleness is bounded by the TTL. Regression:
  `RefreshTokenMaintenanceTests.UserSecurityStateCache_CachesUntilEvicted`.
- [x] **R15 (P3). Dataset listing is N+1.**
  `DatasetController.GetAll` and `DatasetRegistryService.ListAll` load all datasets then await a
  permission check per dataset (each potentially hitting folder ACLs). Fine at dozens of datasets;
  precompute group→folder permissions once per request before scaling the catalog.
  *(done — v0.11.0)* `DatasetPermissionService.GetEffectivePermissionsAsync` resolves the caller's
  group ids once and all referenced folder permissions in a single grouped ACL query
  (`FolderPermissionService.GetEffectivePermissionsAsync`), then evaluates each dataset in memory via
  the same `Evaluate` core the single-dataset path uses — decisions are identical by construction.
  Both listing callers use the batch path; the existing portal/registry permission-matrix tests are
  the behavioral regression.

### Tooling / standards / docs

- [x] **R16 (P2). No `.editorconfig`, analyzer config, or format gate.**
  User config claims "standard EditorConfig settings" but the repo has none at root, no
  `TreatWarningsAsErrors`/`AnalysisLevel` in `Directory.Build.props`, and CI has no
  `dotnet format --verify-no-changes` step. The build is currently 0-warning — cheap to lock that in
  now (warnings-as-errors + an `.editorconfig`) before drift accumulates.
  *(done — v0.11.0)* Added a root `.editorconfig` setting standard C# layout/naming rules, enabled
  `TreatWarningsAsErrors` and `AnalysisLevel=latest` in `Directory.Build.props`, formatted the entire
  codebase, and added a `dotnet format --verify-no-changes` step to the Github Actions CI workflow.
- [x] **R17 (P3). `SharpCompress` is referenced globally.**
  `Directory.Build.props` injects `<PackageReference Include="SharpCompress" />` into **every**
  project, including Core and shells that don't compress anything. Scope it to the projects that use
  it (transitive surface, audit noise, P2.9 scan scope).
  *(done — v0.11.0)* Removed the global `SharpCompress` package reference. The package was completely
  unused in C# files and has been removed from all projects, resulting in a cleaner dependency surface.
- [x] **R18 (P2). AGENTS.md connector list is stale.**
  §2 lists ~20 connector tokens; the code ships 29 (missing from the list: `MYSQL`, `SQLITE`,
  `MONGODB`, `KAFKA`, `NEO4J`, `S3`, `SHAREPOINT`, `ACTIVE_DIRECTORY` — all of which
  `Data_Connectors.md` documents). CLAUDE.md's "14 connector types" is also stale. Agents writing
  scripts from AGENTS.md will wrongly avoid supported connectors. Sync both files (and note
  CLAUDE.md says `sync-assets.ps1` while AGENTS.md/CI use `node scripts/sync-assets.js` — pick one
  as canonical).
  *(done — v0.11.0)* Updated the connector list in `AGENTS.md` and the architecture overview table in
  `CLAUDE.md` to reflect all 29 supported connector types, and standardized the asset synchronization
  instructions on `node .\scripts\sync-assets.js` across both files.
