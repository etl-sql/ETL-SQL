# ETL-SQL Development TODO List

Use this list as the execution ledger for open active-release and roadmap work. Once work is
verified, record its notable outcome in `CHANGELOG.md` and remove the completed entry; Git and the
changelog retain completion history. If later evidence invalidates a completion claim, add a new
open entry with a concrete correction path.
`ROADMAP.md` remains the high-level product-direction source, and its initiatives are decomposed into
actionable tasks here.

**Closed-item review — 2026-08-13.** All 67 checked entries were revalidated against their
implementation, focused regression evidence, and commit history, then removed. The remaining
34 checklist entries are open work; partial implementation does not close a remaining
certification or topology obligation.

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
- [ ] Evidence indexed under `artifacts/release-evidence/0.18.0/`, recording what was **not**
      covered as well as what was

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

### Platform — Deployment Profiles and Upgrade Certification

#### P2 — Deployment-profile certification

- [x] Add current per-profile and per-transition evidence to release claims. Report Managed Dedicated
      and Shared SaaS separately; neither inherits the other's claim status.

### Orchestrator — Operations Triage and Run Flight Recorder

#### Deployment-profile portability review

Required by [Deployment_Profile_Standards.md](docs/architecture/standards/Deployment_Profile_Standards.md#feature-design-portability-review).
Smallest safe profile is **Solo**, and the capability must not become Portal-only.

- [x] Confirm no matrix cell moves backward, record Dedicated and Shared SaaS status separately, and
      record the review outcome the way
      [v0.18.0](docs/releases/v0.18.0-deployment-profile-review.md) did.

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

##### 1. Tenant context and authority

- [x] **Shared.** Prove tenant context is server-derived at every shared entry point — a negative
      test per surface that a caller-supplied tenant, alias, gateway, resource, run, object, or
      storage identifier cannot widen scope, plus collision tests for equal numeric/logical IDs
      across tenants.

      **The guard exists; the surfaces do not (v0.18.0).** Checked before writing anything: the
      product is host-fixed today — the only tenant runtime isolation test is
      `HostFixedPortalInstances_IsolateAuditOutboxesAndSecurityCaches` — so there is no shared
      control plane, store, queue, or index to point a negative test at. Writing "a negative test per
      shared surface" would have meant inventing the surfaces first.

      What ships instead is the thing that makes those tests unavoidable later:
      `SharedTenantSurfaceContractTests`, an abstract contract following the
      `ArtifactStorageContractTests` pattern already used here. The first shared surface's test class
      inherits it and cannot ship without answering all six cases — a caller naming another tenant's
      scoped id, an unscoped name resolving across tenants, equal logical ids colliding, a write
      overwriting another tenant's row of the same name, enumeration leaking, and the
      `acme`/`acme-evil` prefix trap. A reference in-memory implementation keeps the contract
      executable rather than aspirational. Writing the guard *after* the first shared surface is how
      a boundary ends up certified by whoever was in a hurry.

      **The contract immediately earned its keep.** Its enumeration case failed on the first run
      against `TenantContext` as shipped the day before: `ScopeKey` correctly rejects an empty
      logical id, so there was no way to derive a tenant's key prefix for a scan — something every
      shared store needs. `ScopePrefix` was added in response, delimited, with a test showing why it
      is not `ScopeKey("")`: a range scan on the bare name `acme` also matches every `acme-evil/…`
      key.

      **Closed with durable adoption (2026-08-13).** `SharedTenantResourceRegistry` is a real
      SQLite/PostgreSQL-backed Shared control-plane namespace for aliases, gateways, resources, runs,
      objects, storage, queues, and indexes. It accepts only a verified request `TenantContext`,
      persists composite tenant/kind/logical identity, and scopes lookup, enumeration, deletion, and
      collision handling below that tenant. Provider and authenticated HTTP tests prove equal logical
      IDs coexist, foreign numeric and scoped IDs are absent, caller tenant headers/query values do
      not select scope, `acme` cannot enumerate `acme-evil`, and state remains partitioned after a
      store restart. This closes tenant-context derivation only; it does not certify the separate
      storage, scheduler, Gateway transport, data-evidence, or hardened-execution cells below.

      **HTTP credential adoption started (2026-08-10).** Shared Portal JWTs now carry exactly one
      canonical tenant claim minted only from a trusted `TenantContext`. After normal JWT validation,
      middleware converts that signed claim into the request-scoped context consumed below controller
      code; missing, duplicate, and malformed claims fail before controller activation. An HTTP
      collision test proves spoofed tenant headers, tenant/issuer query values, and an equal shared
      secret row cannot replace the signed tenant or widen enumeration. The durable registry above
      now supplies equivalent concrete adoption evidence for the remaining identifier namespaces.

##### 4. Storage, paths, and artifacts

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
      dispatch, and an actual Hardened OCI/microVM provider remain open.
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

##### 7. Data assets and evidence

Lineage, quality, quarantine, catalogs, datasets, reports, and authoring ingress.

- [ ] **Dedicated.** Prove disjoint lineage, scans, quality evidence, caches, outboxes, and
      quarantine data using tenant-specific stores and artifact roots. Deliver controlled tenant
      ingress and a certified tenant-admin/author boundary within the dedicated deployment. Reports
      (currently Yellow): certify tenant catalog, dataset, snapshot, share/embed, export, and
      subscription isolation.
- [ ] **Shared.** Prove tenant-isolated lineage/graph indexes, scans, quality evidence, quarantine,
      caches, searches, and outboxes in shared services — partitioning metadata search, graph
      traversal, exports, and support diagnostics so table names, schemas, tags, edges, and evidence
      cannot leak across tenants. Re-certify tenant ingress, catalogs, datasets, embeds, snapshots,
      exports, subscriptions, and interactive sessions against shared stores and worker fleets.
      Dedicated-store evidence is explicitly not sufficient here.

*Absorbs the retained discovery item **Tenant-Isolated Lineage Graphs**.*

##### 9. Lifecycle — provisioning, backup, portability, deletion, metering

The former `Managed operations` bullet was one checkbox covering nine deliverables and could not be
checked off meaningfully. Split:

- [x] **Dedicated — provisioning.** Automate tenant provisioning with no manual SaaS-platform
      database edits.
- [ ] **Dedicated — upgrades and capacity.** Automate upgrades, drain/fence, and capacity assignment
      for one tenant.
- [ ] **Dedicated — backup and recovery.** Tenant-scoped backup, export, restore, and key/artifact
      recovery, including proof that a restore cannot introduce another tenant's rows or resume its
      work.
- [x] **Dedicated — support approval.** Add the approval workflow behind the shipped audited platform
      support-access model.
- [ ] **Dedicated — metering.** Tenant-specific usage records for dedicated operations.
- [ ] **Dedicated — deletion.** Legal/retention-aware tenant deletion with a completion record.
- [ ] **Shared — backup and recovery.** Tenant-scoped export/restore from shared stores, including
      proof that point-in-time recovery, retry, or cache rebuild cannot introduce another tenant's
      rows.
- [ ] **Shared — metering.** Shared-fleet attribution for rows/bytes, connector class, sandbox
      CPU/memory/I/O, Gateway traffic, storage, and concurrency. Metering keeps its own durable,
      tenant-partitioned ledger; it cannot read payload content or become execution authorization.
- [ ] **Shared — provisioning, upgrade, and deletion** against shared control planes.
      **Gap — Phase C carried no managed-operations bullet at all.**
- [ ] **Portability bundle (both).** Unify the existing Portal configuration export, Orchestrator
      promotion package, source artifacts, and optional evidence/content into the one open,
      versioned, signed, tenant-encrypted format defined in
      [`TenantPortability.md`](docs/architecture/TenantPortability.md). Deliver the minimum
      configuration/artifact bundle and the SaaS → self-hosted Enterprise proof before Managed
      Dedicated SaaS GA; add large resumable content and incremental deltas later.
      Deliberately exclude resolved secrets, private keys, capabilities, checkpoints, leases, caches,
      and in-flight work rather than making an indefensible "zero-loss" claim.

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

When one is found: file it as P0 with a reproducer that fails on the *behaviour*, not on a plan or
threshold choice, so it cannot be mistaken for a configuration artifact later.
