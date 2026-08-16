# ETL-SQL Development TODO List

Use this list as the execution ledger for open active-release and roadmap work. Once work is
verified, record its notable outcome in `CHANGELOG.md`.
`ROADMAP.md` remains the high-level product-direction source, and its initiatives are decomposed into
actionable tasks here.  Items will be double-checked to ensure they met all the previous goals and 
then they should be removed from the `TODO.md` and `ROADMAP.md`. Git and the
changelog retain completion history. If later evidence invalidates a completion claim, add a new
open entry with a concrete correction path.

---

## v0.18.0 Release — target 2026-08-24

First release on the monthly cadence (v0.7.0–v0.17.0 were weekly). Rationale in
[Release_Workflows.md](docs/architecture/roadmaps/Release_Workflows.md#release-cadence).
The date is a target, not a commitment — ship when the gate is green and the evidence is collected.

### Release evidence gates

Carried forward from
[Enterprise_Release_Evidence_Checklist.md](docs/architecture/decisions/Enterprise_Release_Evidence_Checklist.md).
Release evidence is per-release and must be collected against the v0.18.0 candidate; it cannot be
inherited from v0.17.0.

- [ ] Full pre-release lane — `scripts/Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration`
- [ ] Cross-platform test lane — `scripts/test-lane.ps1`
- [ ] Documentation/security-boundary suite — `SecurityBoundaryDocTests` and the broader docs tests
- [ ] Enterprise hardening certification — `scripts/Test-EnterpriseHardeningCertification.ps1`,
      Windows **and** Linux
- [ ] Recovery drill — `etl-sql admin restore --validate --report`
- [ ] HA fault injection — `etl-sql admin ha-soak validate` (run `fault-plan` before `fault-run`,
      and `evidence` before `validate` — see the RCI item below)

**Sequencing.** The release-process RCI items are scheduled **last**, deliberately. The RCI changes
touch the validation gate and CI itself, so landing them
mid-release would mean debugging the measuring instrument and the product at the same time. Doing
them at the end also means they are exercised for the first time on the *next* release rather than
destabilising this one.

### Release-process RCI — issues found cutting v0.17.0 (scheduled last)

Thirteen process problems surfaced during this release. Remaining items are listed in rough value
order. The theme: **the gate's failures were mostly not product
defects**, they were the gate measuring the wrong thing, hiding things, or being impossible to run.

#### Process observation worth keeping

The **authorship-permission regression** (five sites, including unauthenticated share links
surviving revocation) was found by two pre-existing tests during the gate. It had been reviewed by
hand in Phase 2 and cleared. Meanwhile the one finding raised purely from reading the diff turned
out to be wrong on both premises, and its proposed fix measured as a no-op. For permission and
revocation logic, a red test is far stronger evidence than a careful read.

### Close CodeQL alert 323 — unescaped telemetry in the lineage tree

Open High `js/xss` accepted for v0.17.0 and left **open** rather than dismissed, because it is a real
latent gap. Full triage in
[v0.17.0-code-review.md](docs/releases/v0.17.0-code-review.md).

Implementation has been fixed in the canonical shared runtime and synced to host copies; the
remaining work requires the next CodeQL run on `main`.

- [ ] Confirm alert 323 closes on the next `main` scan.

## Roadmap execution backlog

These tasks decompose the future tracks in [ROADMAP.md](ROADMAP.md). Their presence here makes work
reviewable; it does not assign them to v0.18.0 or turn candidate phases into release commitments.
Keep the roadmap's P0/P1/P2 ordering unless a release plan explicitly changes it.

### Orchestrator — Per-Object Authorization

Decomposes the [ROADMAP.md](ROADMAP.md) item of the same name. **Read this preamble before picking up
a slice** — most of the roadmap item is already built, and the open work is narrower than the roadmap
text implies.

**Verified shipped (2026-08-15), not open work.** Federated identity
(`OrchestratorIdentityAssertion` — HMAC, issuer/audience-bound, 2-minute lifetime, nonce; the
caller-controlled `X-Orchestrator-Actor` header is retired and carries no authority; required by
default when the service binds non-loopback, with a startup failure when the secret is missing).
Per-object ACLs over `JOB`/`SCHEDULE`/`NOTIFICATION` × `READ`/`EXECUTE`/`OVERRIDE`/`MANAGE` ×
`USER`/`GROUP`/`SERVICE`, enforced across the scheduled-job, schedule, notification, history, and
data-quality endpoints including list filtering. Ownership checks that stop `CREATE OR ALTER` taking
over another principal's name, in both the HTTP and engine-handler paths. Grant deletion on object
drop. `OrchestratorPerObjectAuthorizationIntegrationTests` proves the definition-of-done scenarios and
`scripts/Test-DeploymentProfileCertification.ps1` gates on it as the `per-object-authorization` hosted
prerequisite. Do not re-plan or re-implement any of the above; extend it.

**Design decisions taken (2026-08-15) — do not re-litigate.** Identity federates to the **Portal**,
which is the single control plane for user provisioning, groups, and audit. The Orchestrator never
grows its own principal registry; that was considered and rejected because it would be the second
permission model the roadmap item exists to prevent, and because it would have no path to
Active Directory at all. The Portal already syncs OIDC/AD group claims into Portal groups on every
login (`OidcUserProvisioningService`), and grants resolve `Group` principals from the assertion, so a
grant made against a Portal group inherits AD membership changes with no ETL-SQL action — that
property is the point of the design. **Team and above require a Portal.** Solo may run without one
via `RequireFederatedIdentity=false`, where no principals and no grants exist at all; the admin CLI
manages that box directly. The integration shape is **exchange**: a client authenticates to the
Portal and receives a short-lived Orchestrator assertion, then calls the Orchestrator directly. The
proxy shape (Portal forwards every call) was rejected because it needs a Portal twin for every
orchestrator endpoint, forever; presenting a Portal browser JWT to the Orchestrator was rejected
because it undoes the deliberate audience separation between the two tokens.

#### Slice F — Audit parity, documentation, and evidence

- [ ] **F1 Audit parity.** ACL mutations emit `SecurityEventContract` naming the real principal;
      confirm every other mutation verb (create/alter/drop/enable/disable/trigger/kill/resume) does
      too, in both the HTTP and engine-handler paths, and add the missing ones.
- [ ] **F2 Documentation.** Permission matrix (verb → required permission → required scope) in
      `docs/administration/orchestration/orchestrator-portal.md`; the Portal-as-control-plane model and
      the Solo exception in `docs/guides/administration.md`; new settings in the appsettings reference.
- [ ] **F3 Retire stale text.** `ROADMAP.md` and `docs/architecture/decisions/job_schedule_notification.md`
      (three places) still describe this as deferred and describe v0.18.0 as shipping "attribution,
      not authorization". That is no longer true and misleads anyone reading it as current.
- [ ] **F4 Evidence.** `per-object-authorization` and `verifiable-caller-identity` prerequisites green
      on the release candidate; record the topology explicitly rather than inheriting a prior claim.

### Orchestrator — Job Administration UI

Split out of Per-Object Authorization (2026-08-15) so the security boundary is not held hostage to a
much larger UI build. Depends on that item for ownership and grant surfacing, but nothing else.

**Scoped against what already exists (reviewed 2026-08-15).** `src/ETL-SQL.Portal/wwwroot/orchestrator.html`
is not a stub — it has the service status chip with restart/stop, four clickable stat chips that filter
the table, a triage board, a 24-hour Gantt timeline, the jobs table with run/enable-disable/kill/delete,
a detail panel with Details and Script Flow (DAG) tabs, an inline script editor, a duration sparkline,
run history with resume-from-named-checkpoint behind an impact-confirm dialog, a create-job modal, and
a run-with-variable-overrides modal. This item extends that to the objects and metrics added since it
was written; it is not a rebuild.

- [x] **Schedule and notification objects.** `/api/schedules` and `/api/notifications` — the unified
      catalog — have full UI catalog tables with create/edit/delete/toggle and dispatch tests.
- [x] **Job metrics.** The sparkline toggles between duration and rows processed. Data quality failure
      breakdowns, quarantine counters, and stewardship coverage scores are surfaced in the job details panel.
- [x] **Bundles.** `jobType`/`targetPath` (`bundle://`) selector and pinning surfaced in create modal and details.
- [x] **`DisplayName`, `Description`, and `Options`** — including `SandboxProfile` admission options in job forms,
      table badges, and search filtering.
- [x] **Watermark state.** High-water mark inspector with key editing and 1-click backfill reset.
- [x] **Definition change log** — Chronological audit trail showing mutations, triggers, and access changes.
- [x] **Table ergonomics** — Instant search, status filters, configurable pagination, and 7-day multi-day run calendar.
- [x] **Job-to-job dependency view.** Interactive force-directed cross-job DAG showing upstream providers and downstream consumers.

### Installer — component rework

Raised 2026-08-15 while scoping the orchestrator/admin Portal deployment. The MSI already splits into
`Feature_SDK`, `Feature_Orchestrator`, and `Feature_Portal` (`src/ETL-SQL.Installer/Installer.wxs`), so
component-level installation works; the naming and the groupings are what need work.

- [ ] **Rename `Feature_SDK`** — it does not describe what it installs. It is workstation authoring:
      the TUI, the editor, or both.
- [ ] **Orchestrator feature** installs the job runner with the admin/orchestrator Portal as a
      default-on sub-feature, since Team and above require a Portal for identity.
- [ ] **Report Portal feature** with the data-steward and orchestrator surfaces as independent
      options, so a team can put stewardship on its own server, separate from reporting.
- [ ] **Portal surface flag.** A Portal installed for orchestrator administration still shows the full
      report catalog, designer, and navigation. Decide the feature flag here rather than in the
      authorization item — the installer feature and the runtime flag should be one decision.
- [ ] **Deployment-profile install templates** (Solo, Team, Enterprise, SaaS) as presets over the
      component choices.

### Platform — Progressive SaaS Delivery and Red Cells

SaaS remains one deployment profile, delivered through two topologies in sequence:

1. **Managed Dedicated SaaS** — automated tenant-specific Enterprise-style deployments with
   disjoint database, artifact, key, cache, queue, identity, and worker/hypervisor boundaries.
2. **Shared SaaS** — shared tenant-aware control planes and hardened per-run execution after demand
   justifies the additional density, security, and operational complexity.

These are delivery stages, not editions. Team remains a lightweight configuration of common
providers, and Enterprise is the operational/self-hosting foundation rather than a code fork.

The SaaS capability matrix distinguishes Managed Dedicated from Shared topology evidence. Existing
host-fixed negative tests are meaningful **Managed Dedicated implementation evidence**; they do not
make hostile shared control-plane or execution-plane isolation Green. Release reviews must report
Dedicated and Shared status separately and must not publish a generic SaaS isolation claim.

The existing Portal/Orchestrator Enterprise tracks supply the identity, authorization, durable state,
artifacts, secrets, policy, audit, HA, recovery, and promotion foundation. Do not rebuild those
capabilities in SaaS-specific services.

**Reorganized by isolation domain, one entry per matrix cell (2026-08-09).** This track previously
carried the same work on three axes: Phase B bullets (Dedicated), Phase C bullets (Shared), and a
parallel *Cross-Cutting SaaS Follow-through* list whose eleven entries each restated "Dedicated does
X, Shared adds Y". Every one of those was therefore a duplicate spanning two phase bullets,
maintained in two places. Worse, B and C did not use the same domain list — B had managed operations
and no observability or HA, C had observability and HA and no managed operations — so a domain
nobody had written down looked the same as a domain deliberately marked N/A.

The nine-domain model is the axis the definition of done uses: *the relevant Dedicated or Shared
matrix cell*. Each remaining entry maps to exactly one open cell, and an uncovered cell is stated as
a **Gap** rather than being invisible. Completed cells have been retired from this open-work ledger.

#### Isolation domains

The remaining domains state the still-open Dedicated or Shared obligation and the Enterprise contract
they build on where one exists. An entry is complete only when the matching matrix cell carries a
current linked evidence reference and the release review records the topology explicitly, the way
[v0.18.0](docs/releases/v0.18.0-deployment-profile-review.md) recorded its review. Do not infer
Dedicated support from an Enterprise happy path, or Shared support from Dedicated evidence.

##### 4. Storage, paths, and artifacts

**Current certification-environment constraint (2026-08-13).** The available development host is
Docker Desktop on Linux/x86-64 with `runc` and NVIDIA runtimes only; it has no registered gVisor
(`runsc`) or Kata runtime. ETL-SQL Portal and Orchestrator images and PostgreSQL/Testcontainers are
available, so provider-neutral behavior, real Docker Desktop execution, multi-process coordination,
forced `runc` termination, cleanup, recovery, and residue checks can be implemented and exercised
here. Evidence produced on this host must be labeled **Docker Desktop / `runc`** and must not be
represented as a hostile-tenant Hardened-runtime result. For the current development cycle, complete
every testable portion on this host and retain the gVisor/Kata mount-isolation, forced-termination,
and cross-sandbox checkpoint run as an explicit external certification gap until a suitable Linux
host or CI runner becomes available. This constraint applies to the Shared storage cell below and
the Dedicated/Shared Hardened execution cells in domain 5; it does not block their remaining
provider-neutral scheduler and lifecycle work.

- [ ] **Shared.** Server-derived storage identifiers with a negative test that a caller-supplied
      object, prefix, or path identifier cannot widen scope, and no reuse of volumes, directories,
      object prefixes, or encryption data keys across tenants or sandbox assignments.
      **Gap — no phase bullet covered shared storage scope.**

      **Control-plane and run-capability slice completed (2026-08-10).** Shared Portal artifact
      operations now require the request's verified `TenantContext`; scripts, maps, snapshots, and
      key artifacts are mapped below provider-neutral tenant prefixes, while background snapshot
      work uses its persisted server-owned tenant binding. Dataset files and decrypted-preview
      scratch use tenant-specific directories, and published/generated script paths are resolved
      against the same tenant root. Report and ad-hoc execution receive tenant/run-specific scratch,
      spill, checkpoint, script/map, dataset, and snapshot grants. Equal logical keys coexist, tenant
      enumeration strips and filters physical prefixes, another tenant's absolute path is refused,
      and a caller spelling another tenant in a relative object name remains nested below its own
      prefix. Snapshot packages are separated by both Artifact encryption-key scope and storage
      prefix. This cell remains open until the Hardened execution slice in domain 5 proves worker
      volume/mount non-reuse and forced-termination cleanup. The provider-neutral workspace layer
      now allocates a cryptographically identified, single-use tenant/run/attempt root, verifies a
      tamper-evident ownership marker before destructive teardown, refuses path-shaping identifiers,
      and deletes without following reparse points. Tests prove ordinary teardown removes nested and
      read-only residue and that a successive assignment cannot observe or reuse the prior root. The
      cell remains open until a certified Hardened provider consumes this contract and proves its
      actual mounts and abnormal-exit cleanup obey the same lifecycle.

*Absorbs the retained discovery item **Tenant-Scoped Virtual Filesystem and Object Storage**.*

##### 5. Scheduling, execution, and capacity

- [ ] **Dedicated.** Provision tenant-dedicated queues, schedules, leases, quotas, session roots, and
      VM/worker boundaries; run disposable OCI tasks without treating a shared-kernel container as
      the boundary between customers. Prove reserved placement.

      **Production hardened-provider slice completed (2026-08-13).** Scheduled jobs now prefer the
      configured `ISandboxScheduledJobExecutor`, which binds the job's immutable tenant, attempt,
      session/checkpoint, server-resolved profile, durable admission, and content-addressed script to
      `SandboxExecutionCoordinator`. `DockerSandboxExecutionProvider` accepts only digest-pinned
      images and registered gVisor/Kata runtimes; ordinary `runc`/`crun` cannot claim Hardened
      evidence. It creates tenant code stopped, then uses a read-only root, non-root user, no network,
      all capabilities dropped, no-new-privileges, bounded memory/PIDs/tmpfs, exact assignment mounts,
      and a tenant-specific persistent session/key mount. Dedicated hosts are fixed to exactly one
      tenant and capacity pool and refuse foreign placement before invoking Docker. Runtime removal
      is verified before workspace/capacity release; retained reconciliation locates containers by
      durable admission ID and releases only absent or proven-removed work. Tests pin scheduler
      routing, immutable input/tamper refusal, command construction, reserved placement, session-key
      separation, prepare cleanup, terminal teardown, and stopped/running/absent reconciliation.
      This cell remains open because the current certification host exposes only ordinary `runc` and
      NVIDIA runtimes and has no pinned runnable ETL-SQL engine image for gVisor/Kata. A real hardened
      runtime run, forced termination, different-sandbox checkpoint resume, and residue proof are
      still required; mocked command evidence is not substituted for that gate.
- [ ] **Shared.** Implement the provider-neutral scheduler and Hardened per-run sandbox boundary with
      tenant-scoped queues, leases, capabilities, checkpoints, quotas, fair admission,
      ambiguous-outcome handling, and destructive cleanup. Tenant-partitioned queues and
      weighted/fair admission so one tenant cannot cause head-of-line blocking or starvation; enforce
      reservations, maximums, backpressure, and Dedicated placement without silently borrowing across
      an isolation or service-tier boundary.

      **Workspace-lifecycle slice completed (2026-08-10).** `ISandboxWorkspaceProvider` now gives a
      provider one fresh, server-owned tenant/run/attempt writable root per assignment. Assignment
      roots are unique even when logical IDs repeat, carry a cryptographic ownership marker, reject
      caller path shaping, and fail closed rather than deleting on marker mismatch. Verified teardown
      removes nested/read-only content without following reparse points, and residue tests pin
      non-reuse across successive and cross-tenant assignments. `SandboxExecutionCoordinator` now
      requires providers to prepare a non-executing attempt, validates complete runtime evidence and
      the requested isolation tier before running tenant code, and destroys the runtime before its
      workspace on success, failure, cancellation, or ambiguous outcome. If runtime detach cannot be
      proven, it retains writable state for fenced reconciliation instead of deleting a potentially
      live mount. `FairShareSandboxAdmissionController` now enforces disjoint provider capacity pools,
      per-tenant concurrent/queued maximums, queue backpressure, and bounded weighted round-robin
      admission. Shared and Dedicated pools cannot borrow from one another; an uncertain teardown
      retains its admission reservation until an external provider reconciler releases the fenced ID.
      `RelationalSandboxAdmissionLedger` now persists tenant/pool policy, FIFO queue sequence, active
      ownership, expiry, monotonic fence token, cancellation, completion, and retained reconciliation
      state through the existing provider-neutral SQLite/PostgreSQL dialect. Competing nodes cannot
      activate the same queued admission; owner/fence mismatches cannot renew or complete it; lease
      expiry becomes `Retained` rather than silently freeing capacity; queue order and reservations
      survive store recreation. Pool and tenant capacity counters are now reserved and released in
      the same relational transaction as admission activation/completion, so competing nodes claiming
      different work cannot oversubscribe either boundary. Retained attempts continue consuming both
      counters until fenced reconciliation. A real PostgreSQL/Testcontainers test proves the same
      two-node final-slot contention, retained-capacity behavior, and fresh-instance recovery on the HA
      provider. `LedgerBackedSandboxAdmissionController` now persists queue intent before entering the
      local weighted dispatcher, requires a fenced relational activation before returning authority,
      renews the lease while work runs, and cancels the coordinator's execution token if renewal loses
      ownership. Normal release commits the durable terminal state before returning local capacity;
      reconciliation releases both ledgers. `SandboxAdmissionReconciliationService` now sweeps expired
      active leases into retained state and calls an environment-owned runtime probe; only an explicit
      `Detached` result releases fenced capacity. `Running`, `Unknown`, probe failures, and fence races
      remain retained for a later pass. Restart recovery can now call `ResumeQueuedAsync` to adopt the
      exact persisted admission ID after revalidating tenant, queue sequence, pool, weight, and limits;
      it refuses active/retained/terminal authority, and cancellation leaves the durable row queued for
      another node. `SandboxWorkloadPolicyResolver` now consumes the existing durable job `Options`
      metadata, but permits only a named `SandboxProfile` request. The verified tenant's server-owned
      catalog entry controls profile entitlement, physical pool, required isolation, runtime limits,
      weight, and queue/concurrency ceilings; unknown tenants, profiles, malformed or ambiguous JSON,
      and cross-tenant entitlement attempts fail closed. `AddSandboxAdmissionHosting` now binds the
      ledger to the configured SQLite/PostgreSQL Orchestrator authority and, when explicitly enabled,
      registers the durable controller plus a retained-capacity reconciliation background loop.
      Enabled hosts require positive pool/interval configuration and an environment-owned
      `ISandboxRuntimeReconciler`; a missing provider binding fails startup instead of guessing that a
      runtime detached. Scheduled jobs now persist an immutable canonical `TenantId` derived from the
      Portal's signed tenant assertion or fixed Orchestrator host authority. REST creation, script-first
      `CREATE JOB`, and tenant-scoped subscription generation carry the binding; conflicting host and
      signed identities are denied, replacement cannot change it, and legacy/unbound jobs cannot enter
      sandbox policy resolution. Global/durable weighted selection, scheduler-execution/restart
      dispatch remain open. The production Docker OCI provider and scheduler dispatch seam described
      in the Dedicated slice above now exist, but Shared cannot claim them until a real Hardened
      runtime lane proves the boundary and cluster-global fair queued-work recovery is complete.
- [ ] **Both topologies.** Admission and runtime limits for CPU, memory, processes, scratch/spill,
      IOPS, network, rows, duration, connector concurrency, queue depth, and interactive sessions.
      Ordinary cgroups and containers are useful controls but are not the hostile-tenant security
      boundary.
- [ ] **High availability, Dedicated.** Fleet rollout, compatibility, and drain behavior across a
      population of per-tenant deployments — upgrading a hundred dedicated stacks is the operational
      problem the topology creates. **Gap — Phase C carried HA alone.**
- [ ] **High availability, Shared.** Tenant-aware fleet rollout, compatibility/drain behavior, and
      noisy-neighbour containment without silently falling back from Dedicated placement or Hardened
      isolation.

*Absorbs the retained discovery items **Noisy-Neighbor CPU/Memory/I/O Containment** and
**Tenant-Aware Fair-Share Scheduling**.*

##### 6. Network egress and the Gateway

- [ ] **Dedicated.** Enroll a tenant-owned outbound Gateway, register resources locally, map them
      through tenant-admin `SHARED:` aliases, and prove revocation, local credential custody, typed
      operations, and SaaS-to-on-premises connectivity before introducing a shared broker registry.
      Follow the
      [SaaS Tenant Isolation Architecture](docs/architecture/SaaSTenantIsolation.md#11-secure-outbound-data-gateway).
- [ ] **Shared.** Add the shared tenant/gateway session registry, typed stream routing, metering,
      backpressure, and negative cross-tenant tests without weakening gateway-local resource policy.
- [ ] **Both topologies.** Execute tenant workloads with default-deny networking, blocked cloud
      metadata/control-plane/internal hosting ranges, and only capability-authorized connector,
      storage, telemetry, or Gateway Broker destinations. Test DNS rebinding, redirects, alternate
      address forms, port scanning, and policy changes during a run. **Gap — egress fencing sat only
      in the discovery list and in neither phase, though a dedicated tenant's own worker still must
      not reach the cloud metadata service.**

*Absorbs the retained discovery item **Internal Network Egress Fencing**.*

##### 9. Lifecycle — provisioning, backup, portability, deletion, metering

The former `Managed operations` bullet was one checkbox covering nine deliverables and could not be
checked off meaningfully. Split:

- [ ] **Shared — backup and recovery.** Tenant-scoped export/restore from shared stores, including
      proof that point-in-time recovery, retry, or cache rebuild cannot introduce another tenant's
      rows.
- [ ] **Shared — metering.** Shared-fleet attribution for rows/bytes, connector class, sandbox
      CPU/memory/I/O, Gateway traffic, storage, and concurrency. Metering keeps its own durable,
      tenant-partitioned ledger; it cannot read payload content or become execution authorization.

      **Counts-only ledger and scheduler adoption completed (2026-08-13).**
      `RelationalTenantMeteringLedger` is a separate SQLite/PostgreSQL event ledger whose append and
      query APIs require a host-fixed or verified-credential `TenantContext`; events contain no
      tenant selector. Composite tenant/source/event idempotency lets equal event IDs coexist across
      tenants, and reads cannot omit the tenant predicate. Its fixed enum/count schema covers rows,
      read/write bytes, connector class, sandbox CPU/peak-memory/I/O, Gateway ingress/egress, storage,
      concurrency, duration, and status, with no script, parameter, target, resource/object name, row
      sample, secret, or authorization result. It exposes evidence append/query only and execution
      policy has no dependency on it. The scheduler now emits one idempotent event per tenant-bound
      attempt, including sandbox CPU/memory/spill I/O when the sandbox path ran; engine JSON completion
      envelopes carry process peak-memory and CPU measurements across the OCI boundary. Ledger failure
      cannot change a completed workload. SQLite restart/collision tests and a real PostgreSQL 64-bit
      round trip prove durable partitioning. This cell remains open until the Shared Gateway and
      tenant storage providers feed their actual byte/storage/connector-class measures and hostile
      fleet evidence proves those producers cannot misattribute a tenant.

*Absorbs the retained discovery items **Usage Metering & Billing Collector** and **Full-Fidelity
Tenant Portability Bundle**.*

#### Certification and evidence

- [ ] Relabel the current Tenant-isolation implementation-Green evidence as **Managed Dedicated
      only**, attach clean commit-bound topology evidence, and prevent it from satisfying Shared SaaS
      cells.

      **Correction path updated 2026-08-12.** The implementation-Green tenant-isolation cell is
      now explicitly Managed Dedicated; Shared remains Red and says Dedicated evidence cannot
      satisfy it. `CapabilityMatrix_SeparatesManagedDedicatedFromSharedSaasClaims` pins that
      asymmetry. Keep this item open until the final candidate is committed and the topology lane
      attaches clean commit-bound Managed Dedicated evidence; development-worktree evidence cannot
      satisfy that final clause.
- [ ] Move Shared Tenant isolation from Red to claim-Green only with clean commit-bound hostile
      cross-tenant evidence across database, artifact, cache, queue, audit, PII, lineage/quality,
      path, key, checkpoint, Gateway, sandbox, telemetry, support, restore, and resource-exhaustion
      surfaces.

### Triage rule — a wrong answer outranks a crash

Standing rule for this ledger, set 2026-08-10. **A defect that returns a wrong answer is more
serious than one that fails**, and is filed at least P0 regardless of how narrow the trigger looks.
A crash is self-reporting: someone sees it, and nothing downstream consumes it. A wrong number is
consumed — written to a table, put in a report, acted on — and there is no moment at which anyone
learns it was wrong.

This also settles fix trade-offs: **never trade a wrong answer for a crash**. The first attempt at
the window partition-spill P0 did exactly that — it returned correct results by declining a spill
path, which would have made large partitioned queries exhaust memory instead. Correct-but-crashing
is not a fix, it is a different defect.

When one is found: file it as P0 with a reproducer that fails on the *behavior*, not on a plan or
threshold choice, so it cannot be mistaken for a configuration artifact later.

## Bugs
### VS Code
- [ ] **ETL-SQL Results window stays open**  The ETL-SQL Results window is always shown can it be hidden unless the active file is an etlsql or rptsql file?