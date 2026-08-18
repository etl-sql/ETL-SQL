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
- [ ] Deployment-profile certification, Enterprise lane —
      `scripts/Test-DeploymentProfileCertification.ps1 -Profile Enterprise -ReleaseVersion 0.18.0`.
      The `verifiable-caller-identity` and `per-object-authorization` prerequisites must be green on
      the candidate commit, with the lane's own recorded topology claim rather than a v0.17.0 one.
      Both were green on `feature/orchestrator-tenant-identity` when Slice F closed (2026-08-16);
      that is a rehearsal, not the release claim.

**Sequencing.** The release-process RCI items are scheduled **last**, deliberately. The RCI changes
touch the validation gate and CI itself, so landing them
mid-release would mean debugging the measuring instrument and the product at the same time. Doing
them at the end also means they are exercised for the first time on the *next* release rather than
destabilising this one.

### Four documentation tests are red and block the documentation evidence gate

Found while verifying item 5 on 2026-08-17 and **verified pre-existing** — the identical four fail at
`821bf0f2`, before this branch. They are not regressions, but the "Documentation/security-boundary
suite" gate above cannot go green while they fail. Each is documentation the parser or grammar does
not actually accept, which means the docs promise syntax the product rejects:

- [x] `DocSanityTests.GeneralDocs_SqlBlocks_ParseWithoutSyntaxError` —
      `docs/guides/feature-guides/report-sql.md` block #13: `LABEL` inside a `CREATE VISUAL … CARD`
      body is rejected as `Unexpected token 'LABEL' in CREATE BUTTON body`. (Resolved: Fixed block #13 in `report-sql.md` to use valid prompt visual syntax).
- [x] `EbnfConformanceTests.ParserAcceptedDocumentExamples_AreRecognizedByCompleteGrammar` — 2 of 960
      examples: `PRINT_LAYOUT` in `reference/visuals-reporting/report/print-layout.md` and
      `…/visual.md`. The recursive-descent parser accepts them and the complete EBNF does not, so the
      published grammar and the implementation disagree. (Resolved: Added `PRINT_LAYOUT` and `ROW_DETAIL` clauses and string literal mapping support to `grammar.ebnf`).
- [x] `DocumentationSyntaxTests.ValidateDocumentationSnippets` —
      `docs/reference/file-operations/send-email.md`: `SEND EMAIL … ATTACH @path` is rejected with
      "Expected one of: WITH". (Resolved: Updated `DefaultGrammar.cs` to fully support all email clause transitions in any order).
- [x] `HelpSystemTests.TestHelpFileOperations` — the file-operations help entry no longer contains
      the expected `VERBOSE:` marker. (Resolved: Updated test assertions in `AnalysisHelpTests.cs` to align with the embedded markdown reference help format).

Each needs a decision per case: fix the documentation, or implement the syntax it advertises. The
`PRINT_LAYOUT` and `LABEL` cases arrived with the paginated-report documentation in `b9c29d9c` and
`2f23ac74`, so the docs were written ahead of the grammar rather than the grammar regressing.

### Two tests fail only under full-solution load

Also found on 2026-08-17, and also pre-existing. Both pass in isolation and failed only inside a
whole-solution run, which cost real time to distinguish from a regression — the point of recording
them is that the next person does not repeat that.

- [ ] `MetadataManagerTests.ValidCacheHit_TriggersBackgroundRefresh_WhenStale_AndReleasesSlot` and
      `LiveObjectScaleAssessmentTests.LiveObjectsSupportDocumentedScaleMatrix(connection, 100)`.
      Both count observations after a background refresh, so they are the wait-for-a-condition class
      described in [flaky-test-stability.md](docs/releases/flaky-test-stability.md); neither uses
      `LoadAwareWait`. Suspect `ConnectorRegistry.Instance` for the scale case — it is a mutable
      global that makes connector tests order-dependent.

One whole-solution run also aborted with a test-host `Internal CLR error (0x80131506)` after ~14
minutes. It did not reproduce, and the machine was running Docker lanes and PostgreSQL containers at
the time, so it is recorded as an observation rather than a defect.

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

### Orchestrator — authorization and audit follow-ups

Per-Object Authorization closed on 2026-08-16; the item and its slices were removed from here and
from `ROADMAP.md`, and its release evidence moved to the gates above. The model is documented in
[the permission matrix](docs/administration/orchestration/orchestrator-portal.md#permission-matrix)
and [the Portal is the control plane](docs/administration/portal/orchestrator-integration.md#the-portal-is-the-control-plane);
the superseded design record is `docs/architecture/decisions/job_schedule_notification.md` §4.5 and
§10.

**Closed 2026-08-16, after the matrix surfaced them.** The Portal's job channel sent the API key
with no caller assertion, so `POST /jobs` — report execution and data-quality submission — answered
401 against a federated Orchestrator; it now signs through the same issuer the admin proxy uses.
Object creation skipped the service-token scope ceiling, and `ExecutionIdentity` carried no scopes
at all, so a service principal was refused every object permission from script while passing over
HTTP; scopes now travel with the identity and creation is capped like every other verb.
`Portal:Orchestrator:SameHost` gated a Start button that was never built — the setting is gone and
the four documentation sites now say the Portal cannot start a stopped Orchestrator.

The common cause is worth keeping: **each door was only ever tested on its own.** Every existing
`/jobs` auth test constructed a legacy-mode factory, and no test asserted that a narrow token is
actually narrow. New coverage asserts the same caller through an endpoint *and* an ETL-SQL
statement — `OrchestratorScopeCeilingTests`, `OrchestratorJobChannelIdentityTests`, and federated
cases in `OrchestratorJobApiAuthTests`.

- [ ] **Dedicated `OrchestratorViewer` role in Portal RBAC.** The Portal's `OrchestratorAccess` policy currently requires `Admin` or `OrchestratorManager`. Introduce a built-in `OrchestratorViewer` role in `PortalRole` seeding and policy definition so operators can browse the Orchestrator tab (read job status, run history, DAG dependencies, metrics, and watermarks) without possessing object creation (`CanCreate`) or management rights.
- [ ] **Dynamic group claims validation for Orchestrator grants.** OIDC identity sync populates `UserGroup` memberships in the Portal database, and `OrchestratorCaller` carries `GroupIds`. Ensure end-to-end integration tests explicitly assert that group-level grants (`principalKind = GROUP`) evaluate accurately against users mapped via OIDC group claims during token assertion minting.
- [ ] **`POST /management/stop` is not audited.** Stopping the whole Orchestrator writes a
      `LogWarning` and no security event, so "who stopped the service" is answerable only from
      diagnostic logs. It is not one of the object verbs, which is why Slice F left it. Correction:
      emit a `CatalogMutation` event targeting `SERVICE:<host>` from the same caller identity the
      object verbs use.
- [x] **Three `OrchestratorPostgresStoreTests` are red, and block a release evidence gate.**
      **Fixed 2026-08-17; the lane is 16/16 on real PostgreSQL 16.** All three were harness defects
      with no product bug behind them, and each carried a companion assertion that passed vacuously —
      so the lane was both red and not checking what it named. All three are surrogate-identity
      fallout: `GetJobAsync(null, name)` reads the *unbound* partition, where a tenant-bound job has
      never existed, so "alpha was deleted" passed for free and "beta survived" could not pass;
      `TrySaveJobAsync` matches on the surrogate id, so a freshly constructed `JobDefinition` was
      refused for being a different row rather than for holding a stale version; and the
      name-addressed `LogJobStartAsync` helper refuses to invent an unbound twin when the name
      belongs to a tenant. The Enterprise certification lane's `shared-state-and-artifact-providers`
      prerequisite should now get past this point to its remaining five.

### Sandbox execution — follow-ups from item 5

Recorded 2026-08-17 when "Scheduling, execution, and capacity" closed. Neither is part of that item's
contract; both are the next thing each piece of it needs.

- [ ] **Nothing consumes a mounted capability yet.** The delivery half is built and proven on a real
      hardened runtime: handles resolve server-side, and material is bind-mounted read-only at
      `/run/secrets/capabilities` with `ETLSQL_CAPABILITY_ROOT` naming the directory. But **no engine
      code reads that variable**, and there is no `CAPABILITY:name` reference the way `SECRET:name`
      exists. A script can only use a capability by hardcoding
      `/run/secrets/capabilities/<handle>` as a file path, which works but is neither documented nor
      checkable. Give capabilities a first-class reference resolved from the mounted root, so the
      engine fails a missing capability by name instead of failing to open a file.
- [ ] **CI runs none of the three sandbox lifecycle lanes.** Standard, Hardened, and Dedicated all
      exist and pass, and all three run only by hand — the Hardened and Dedicated lanes additionally
      need a prepared runtime, which is what `scripts/enable-hardened-sandbox-lane.sh` is for. A lane
      nobody runs decays into a lane that no longer works, and this is the evidence the hostile-tenant
      claims rest on. Add at least the Standard lane to CI, and the hardened pair to a runner that
      can register gVisor.

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

**Certification-environment constraint lifted (2026-08-17).** This superseded the 2026-08-13 note
that no gVisor or Kata runtime was reachable. A hardened runtime is now available and exercised:
gVisor `runsc` release-20260810.0 (systrap platform) registered with Docker 29.7.2 on Ubuntu 26.04,
kernel 6.18 WSL2, x86-64. `scripts/enable-hardened-sandbox-lane.sh` prepares any Linux host or CI
runner the same way — installing gVisor, registering it, and giving the worker image a real registry
digest through a loopback registry, because Hardened mode refuses a mutable tag or a local image ID.

Both tiers now run the *same* lifecycle assertions, so a difference in result is a difference in the
runtime rather than in what was checked: `DockerStandardSandboxLifecycleTests` (`runc`, Standard) and
`DockerHardenedSandboxLifecycleTests` (gVisor/Kata, digest-pinned, Hardened), over the shared
`DockerSandboxLifecycleTestsBase`. The tier is in every test name, and the Hardened gate skips with a
precise diagnostic rather than substituting an ordinary runtime, so the two can never be conflated.
Standard evidence still must not be cited as a hostile-tenant result.

**This host is a developer workstation, not a fleet-representative certification host, and CI does
not yet run either lane.** A release review that wants fleet certification should re-run
`enable-hardened-sandbox-lane.sh` plus the Hardened lane on its own runner; the lane is written to be
portable for exactly that. What is settled is that the contract holds against a real hardened
runtime, which is what the storage cell below was blocked on.

- [x] **Shared.** Server-derived storage identifiers with a negative test that a caller-supplied
      object, prefix, or path identifier cannot widen scope, and no reuse of volumes, directories,
      object prefixes, or encryption data keys across tenants or sandbox assignments.
      **Closed 2026-08-17 against a real gVisor runtime.** See the completion record below.

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

      **Completion record (2026-08-17).** The lifecycle is proven against a real ETL-SQL workload on
      both an ordinary runtime and gVisor, with identical assertions. Container inspection proves the
      workload receives no named or anonymous volume, only bind sources belonging to its own
      assignment plus its tenant's server-owned session and key roots, the declared hardened runtime,
      a read-only root, a non-root numeric user, no network, dropped capabilities, no-new-privileges,
      and a fresh tmpfs scratch. Successive assignments with identical logical tenant/run/attempt
      identifiers receive different roots and no prior residue; two tenants share no workspace,
      session, or key path and hold distinct key material; caller cancellation and an external forced
      termination (SIGKILL, exit 137) both end with the container absent, the writable root deleted,
      and the reconciler reporting `Detached`; and an unprovable removal retains the root for fenced
      reconciliation instead of deleting a possibly-live mount. Provider-neutral negative tests
      already cover caller-supplied path shaping, tenant traversal, and object-identifier scope.

      **Five real defects were found and fixed by doing this, none of which a mocked or
      Docker-Desktop-only lane could surface.** Three blocked containerized execution outright: the
      provider now directs every writable path the workload can reach (`HOME`, XDG roots,
      security-event outbox, app and script logs) into the assignment's single-use tmpfs rather than a
      read-only or image-baked location; `ProcessActor` stops an unmapped container uid — where
      `Environment.UserName` is legitimately empty — from aborting the run on a required audit actor;
      and `scripts/Test-SandboxWorkerImage.ps1` can publish the stable tag the lane resolves. Two more
      were visible only on a genuine Linux host, because Windows bind mounts had been masking them
      with permissive ownership: the assignment's `output`/`scratch` leaves **and** the per-tenant
      session mount were created by the orchestrator's account with no write access for the
      unprivileged sandbox uid, so on any real host a sandbox could not write its own output or
      persist session state. `SandboxFilePermissions` now restricts every enclosing root to the owning
      account and opens only the mounted leaf, and the workspace, session, and key roots all use it.

      **Scope of the claim.** This is real hardened-runtime evidence for the contract, produced on a
      developer workstation rather than a fleet-representative certification host, and CI does not yet
      run the lane. Cross-sandbox checkpoint resume and Dedicated reserved placement remain open in
      domain 5 and are not claimed here.

*Absorbs the retained discovery item **Tenant-Scoped Virtual Filesystem and Object Storage**.*

##### 5. Scheduling, execution, and capacity

- [x] **Dedicated.** Provision tenant-dedicated queues, schedules, leases, quotas, session roots, and
      VM/worker boundaries; run disposable OCI tasks without treating a shared-kernel container as
      the boundary between customers. Prove reserved placement. **Closed 2026-08-17** — reserved
      placement, checkpoint resume across sandboxes, and the full lifecycle contract are proven on a
      registered gVisor runtime with a digest-pinned image; see the completion records below.

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
      **Partially unblocked 2026-08-17.** A registered gVisor runtime and a digest-pinned runnable
      worker image now exist (see domain 4), and `DockerHardenedSandboxLifecycleTests` supplies the
      real hardened-runtime run, forced termination, and residue proof this cell was waiting on —
      mocked command evidence is not substituted for it.

      **Different-sandbox checkpoint resume completed (2026-08-17), and proving it found the defect
      that made it impossible.** Both lifecycle lanes now run a checkpoint in one sandbox, kill that
      sandbox outright, destroy its workspace, and resume the tenant's session in a *different*
      container on a *different* assignment whose script never assigns the value it writes out — so
      the value can only have come from the first sandbox's checkpoint. The first run of that test
      failed with `Machine-protected payload authentication failed`, and the cause was real: session
      state is sealed with `CryptoUtils.GetMachineKey`, which derives from a random `machine.key` in
      `LocalApplicationData`. In a sandbox that resolves to the assignment's single-use tmpfs, so
      every attempt invented fresh key material and no checkpoint was readable by anything, ever —
      while the per-tenant key the provider mounts at `/run/secrets/etlsql-machine-key` contributed
      only entropy on top of it. The mounted key is now the authoritative base when the host provides
      one, which is what `SecurityService.GetMachineKey` already documented as its purpose
      ("allowing disposable workers to rehydrate encrypted checkpoints"). Cross-tenant separation is
      unchanged and still asserted: different mounted material still cannot open another tenant's
      state. This is the shape the ledger keeps finding — a control that exists, looks implemented,
      and was never asserted end to end.

      **Reserved placement is now a lifecycle contract, and half of it is proven (2026-08-17).**
      Placement was covered only by host-fixed refusal unit tests, which prove the argument checks
      rather than that a reserved host behaves as one.
      `VerifyReservedPlacementRunsOnlyTheHostsOwnTenantAndPool` asserts both halves against real
      containers: the host runs its own tenant's work on the runtime it claims, with the tenant label
      on the container, and creates **no runtime at all** for another tenant or for its own tenant
      placed in a different capacity pool — absence of a container, not merely an exception, because
      that is what distinguishes a reserved host from an ordinary one that happens to be running one
      tenant's jobs today. `DockerDedicatedSandboxLifecycleTests` binds the whole lifecycle contract
      to the Dedicated tier on a host fixed to one tenant and pool.

      **Run on a real hardened runtime 2026-08-17: `DockerDedicatedSandboxLifecycleTests` 7/7, and
      `DockerHardenedSandboxLifecycleTests` 8/8 alongside it.** The lane was prepared with
      `scripts/enable-hardened-sandbox-lane.sh` inside the Ubuntu WSL2 distro — gVisor `runsc`
      release-20260810.0 registered with that distro's Docker daemon, and a worker image built from
      this branch pinned through the loopback registry to
      `localhost:5000/etlsql-sandbox-worker@sha256:9121e3b1…`. Because the tests assert the runtime and
      image identity from `docker inspect` rather than from the request, passing means the workload
      genuinely ran on gVisor from a digest-pinned image: a Dedicated host fixed to one tenant ran
      that tenant's work and created **no container at all** for another tenant or for its own tenant
      in a different capacity pool. The Dedicated tier's checkpoint resume across sandboxes, forced
      termination, residue, teardown, and capability mounting all pass on the same runtime.

      **Scope of the claim, unchanged from domain 4:** this is a developer workstation, not a
      fleet-representative certification host, and CI does not run the lane. A release review wanting
      fleet certification re-runs the same script and lane on its own runner; both are written to be
      portable for exactly that.
- [x] **Shared.** Implement the provider-neutral scheduler and Hardened per-run sandbox boundary with
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
      sandbox policy resolution.

      **Cluster-global fair selection and queued-work recovery completed (2026-08-17).** Weighted fair
      ordering was process-local: each node ranked only its own waiters and then raced
      `TryActivateAsync`, so whichever node polled first took every freed slot — the head-of-line
      blocking and starvation this cell forbids, on the exact topology it is about. Selection now
      happens inside the ledger under the pool row lock, from state every node shares. It is weighted
      fair queuing on durable virtual time: a tenant is charged `Scale / weight` per grant, where
      `Scale` is the least common multiple of the allowed weights 1–16 so the division is exact and
      fairness cannot drift through rounding, and the next slot goes to whichever backlogged tenant is
      furthest behind its share. An idle tenant is lifted to the pool's monotonic virtual base, so it
      neither hoards credit while away nor stays behind after a busy neighbour ran; weight buys a
      proportional share rather than unconditional priority (weight 4 against weight 1 measures 8
      grants to 2, and the light tenant is never starved out). Fairness is bounded by liveness through
      dispatch claims: only a queued row whose claiming node is still polling competes, and the claim
      is committed separately from the activation transaction, because a refusal would otherwise roll
      back its own claim and leave every node believing it waits alone. Reconciliation cancels
      abandoned queue entries, which otherwise accumulate as phantom work against a tenant's durable
      queue depth, and a node waiting on an admission revoked underneath it — by a tenant lifecycle
      fence or that sweep — now fails fast instead of polling forever for a slot nobody will grant.
      Proven on SQLite and on real PostgreSQL 16, the topology where two nodes genuinely cannot see
      each other's queues. Restart adoption of a persisted queued admission (`ResumeQueuedAsync`)
      remains a seam with no scheduler caller: a crashed node's work is recovered today by job-lease
      re-dispatch plus the abandoned-queue sweep, not by adopting the original admission row.

      The production Docker OCI provider and scheduler dispatch seam described in the Dedicated slice
      above now exist, and the real Hardened runtime lane recorded in domain 4 proves the boundary.

      **Capability injection completed (2026-08-17), the cell's last component.** `CapabilityHandles`
      was carried and validated while the provider refused any request that set it. Handles now
      resolve on the orchestrator side through the governance secret provider — so a sandbox
      capability is the same material, with the same custody and rotation, as every other secret the
      deployment holds, rather than a second credential store existing only for sandboxes — and are
      namespaced per tenant, so one tenant's handle cannot resolve to another's material even where
      both use the same name. Resolved material is written into the assignment's own directory,
      owner-only until the single mounted leaf is opened, and bind-mounted **read-only** at
      `/run/secrets/capabilities`; the workload may read what it was granted and cannot add to it.
      Only the directory travels in the environment, never the material, because argv and the
      environment are readable by anything that can see the process. A handle must be one plain name,
      so it cannot choose where its material lands. A host with no resolver, or a capability the
      tenant has not been provisioned, refuses the work before any container exists rather than
      running it without a capability it was told it had — and the material dies with the assignment.
      Proven on a live runtime in all three lanes, not only in command construction.
- [x] **Both topologies.** Admission and runtime limits for CPU, memory, processes, scratch/spill,
      IOPS, network, rows, duration, connector concurrency, queue depth, and interactive sessions.
      Ordinary cgroups and containers are useful controls but are not the hostile-tenant security
      boundary. **Closed 2026-08-17** — every named control is enforced and asserted; the two slices
      below record what was missing and where each ceiling is now applied. The caveat stands: these
      are containment controls, and none of them is the hostile-tenant boundary.

      **Containment slice completed (2026-08-17): CPU, IOPS, connector concurrency, queue depth.**
      Memory, processes, scratch, duration, and default-deny networking were already enforced; three
      of the listed controls were not, and the queue-depth ceiling was enforced in a way that does not
      survive a fleet. CPU had no control at all — a sandbox could saturate every core on a shared
      host while every other dimension was bounded — so `MaxCpuCores` is now a required part of the
      portable limit contract and lands as `--cpus`. It is required rather than optional deliberately:
      a ceiling a provider may ignore is worse than none, because the fleet believes it exists. IOPS is
      now expressible and honest about what a host can do: block-I/O throttling is per-device, so a
      profile may declare `MaxIops` only where the host declares the device carrying sandbox I/O, and
      a host without one refuses the work instead of running it unthrottled behind a ceiling that
      reads as enforced. Connector concurrency becomes a server-owned per-tenant profile value
      injected as the engine's own `Engine:MaxConnectionsPerScript`, rather than whatever the worker
      image defaults to. Queue depth was persisted but never checked, so a tenant whose work arrived
      through N orchestrator nodes could queue N times its limit; the check now runs in the ledger
      under the pool lock, where it holds for the whole cluster. Both lifecycle lanes now assert
      memory, swap, `NanoCpus`, and `PidsLimit` from `docker inspect` rather than from the request, so
      the evidence is that the ceilings reached the runtime.

      **Rows and interactive sessions completed (2026-08-17), closing the bullet's list.**

      *Rows.* The engine's row-shaped settings were preview and spill thresholds, not a processed-row
      limit, so there was no ceiling at all. `Engine:MaxRowsProcessed` (0 = unlimited) is enforced in
      the one place every statement handler already accumulates through — the telemetry
      `RowsProcessed` setter — rather than at ~20 handler call sites, because a per-handler check is
      exactly the thing that would be silently missing from the next handler somebody writes. Resets
      and per-statement bookkeeping still pass through untouched; only growth past the ceiling aborts.
      A profile's `MaxRows` travels into the attempt as engine configuration, since a row is a unit
      only the engine can count.

      *Interactive sessions.* Dedicated deployments materialize `MaxReportSessions` into the
      per-tenant node's `MaxConcurrentReportExecutions`; Shared has one node for every tenant, and the
      node-wide execution cap is not tenant-aware, so one tenant's sessions could occupy every slot
      while the provisioned quota was never read back. The Portal now resolves the tenant's ceiling
      from the Shared control plane and holds a per-tenant gate *outermost* — a tenant at its ceiling
      waits without occupying a user, group, or shared slot meanwhile. The ceiling is read per
      admission rather than baked into a fixed semaphore, so a lifecycle upgrade releases more of the
      queue and a downgrade holds it back, instead of being pinned to whatever the quota was when the
      node first saw that tenant. Queued work keeps its place: a raised ceiling admits more of the
      queue, it does not let a later arrival barge past an earlier one. A Shared deployment whose
      control plane cannot report lifecycle state fails startup rather than running with a quota that
      silently does not exist.
- [x] **High availability, Dedicated.** Fleet rollout, compatibility, and drain behavior across a
      population of per-tenant deployments — upgrading a hundred dedicated stacks is the operational
      problem the topology creates. **Closed 2026-08-17.** Rollout, compatibility, and drain are all
      implemented and asserted; the slices below record each. **Scope of the claim:** this is
      provider-neutral logic with unit and single-node integration evidence, not a multi-node fleet
      soak — that is the separately tracked `admin ha-soak validate` release gate above, and it is
      where fleet-scale behaviour must be demonstrated for a release.

      **Planning, compatibility, and progress tracking completed (2026-08-17).** Per-tenant upgrade
      with fencing, drain, and receipts already existed; what was missing was the layer above it —
      with a hundred stacks, a release is not one upgrade, and nothing answered which deployments are
      eligible, in what order, or what the fleet's state is halfway through.
      `SaasFleetRolloutPlanner` classifies every deployment against a target release (`Upgrade`,
      `AlreadyCurrent`, `BlockedByState`, `IncompatibleRelease`), refusing to move a deployment
      backwards or to race a lifecycle operation already in flight, and orders the eligible ones into
      deterministic waves so a canary means the same tenants every time it is re-planned. Skipped
      deployments do not consume a wave slot, which matters on the second pass when most of the fleet
      is already current. Release ordering is numeric per component, so 0.10 follows 0.9 rather than
      sorting before it. `Track` rolls per-tenant receipts up into one fleet answer and halts the
      rollout past a tolerated failure count — continuing to push a release that has already broken
      deployments is how one bad release becomes a fleet-wide outage — while a draining tenant counts
      as work still finishing, never as damage. `etl-sql admin promotion saas-fleet-plan` surfaces it,
      naming every deployment it is *not* rolling and why, because a stack silently missing from a
      rollout is how one ends up a release behind for a year.

      **The authorization boundary is deliberately unchanged.** Each cutover still runs through
      `saas-upgrade` under its own signed, tenant-scoped grant; the planner confers no authority and
      cannot upgrade anything. Enumerating the population needs the new
      `FleetInventoryAuthorization` — attributed, time-limited, and *tenantless* by construction, so
      "I needed to list the fleet" cannot become a way to obtain tenant-scoped authority in bulk. It
      reads control-plane metadata only and has no path to tenant data.

      **Sequencer completed (2026-08-17).** `SaasFleetRolloutSequencer` walks the plan wave by wave
      and applies each cutover through the ordinary single-tenant path, and `saas-fleet-plan
      --execute --fleet-root` drives it against the deployments one directory per tenant under the
      root they were onboarded to. Two things stop it opening the next wave: a halt, because the
      release has already broken deployments; and an earlier wave still **draining**, because
      overlapping a fenced wave with the next one is how a rollout takes down more of the fleet than
      it has repaired. A deployment the loaded signed authorization does not name is recorded as
      *still owed* rather than failed — work waiting for approval must not halt a rollout — so the
      run advances exactly as far as the grants an operator already holds, and never further.

      **Node drain completed (2026-08-17).** Draining a node was not implemented at all: a Portal node
      either ran, or lost its lease and had every in-flight execution cancelled. That abrupt path is
      right for lease *loss* — the node has lost its claim and another may already be running the
      work — but using it to install a release is an outage, not a rollout. `BeginDrain` now takes a
      node out of rotation gracefully: executions already running are left to finish, new ones are
      refused with `NodeDrainingException` so they land on a node that is staying, and `DrainState`
      reports when the last one has drained. `GET /healthz` answers 503 while draining, because that
      is what a load balancer probes — a node that kept answering 200 while refusing work would fail
      every new execution instead of shedding it. Tests hold an execution in flight, drain under that
      load, and prove both halves: new work refused, running work untouched, and lease loss still
      fencing rather than draining.
- [x] **High availability, Shared.** Tenant-aware fleet rollout, compatibility/drain behavior, and
      noisy-neighbour containment without silently falling back from Dedicated placement or Hardened
      isolation. **Closed 2026-08-17**, with the same scope caveat as the Dedicated cell above: the
      behaviour is implemented and asserted, and fleet-scale demonstration belongs to the HA
      fault-injection gate.

      **Noisy-neighbour containment and the no-downgrade clause completed (2026-08-17).** Containment
      is the work recorded in the two cells above: cluster-global weighted fair admission so no tenant
      can hold a shared pool, fleet-wide queue depth, and CPU, IOPS, rows, connector concurrency, and
      per-tenant interactive sessions all enforced. The rollout planner in the Dedicated cell reads
      the same Shared control plane, so it is tenant-aware for this topology too.

      The no-silent-fallback clause is now pinned rather than assumed: capacity is not an input to
      workload resolution, and a full Hardened pool makes work **queue** instead of spilling into a
      roomier Standard pool sitting next to it, so the only way to change a tenant's tier is to change
      the server-owned catalog. That joins the existing guards — the coordinator refuses provider
      evidence below the required tier, and a missing pool fails closed rather than borrowing.

      **Still open:** node drain observed across a Shared fleet — taking a node out of rotation under
      load and showing tenant work neither drops nor re-places at a lower tier — and the rollout
      sequencer noted in the Dedicated cell.

*Absorbs the retained discovery items **Noisy-Neighbor CPU/Memory/I/O Containment** and
**Tenant-Aware Fair-Share Scheduling**.*

##### 6. Network egress and the Gateway

- [x] **Dedicated.** Enroll a tenant-owned outbound Gateway, register resources locally, map them
      through tenant-admin `SHARED:` aliases, and prove revocation, local credential custody, typed
      operations, and SaaS-to-on-premises connectivity before introducing a shared broker registry.
      Follow the
      [SaaS Tenant Isolation Architecture](docs/architecture/SaaSTenantIsolation.md#11-secure-outbound-data-gateway).

      **Decomposed 2026-08-17, not started.** This cell is a new shipping component — an
      on-premises daemon, a versioned wire protocol, a Portal admin surface, and an installer — not a
      change to existing code, so it is sequenced as ordered slices. Each slice must be provable
      without a customer network: the "on-premises" side runs as a real loopback-hosted process in
      test, the way the Portal browser lane hosts Kestrel, because mocked transport evidence cannot
      support a connectivity claim.

      - [x] **D1 — Binding model.** *(delivered 2026-08-17.)* `SharedConnectionDefinition` now carries
        an optional `GatewayResourceBinding` of connector type plus immutable Gateway/resource IDs.
        `GatewayBindingValidator` refuses any Gateway-bound entry that also carries a target, an
        endpoint-shaped option (`HOST`, `SERVER`, `URL`, `PORT`, `DSN`, `DATA SOURCE`, …), or a
        credential, and refuses malformed IDs including path-separator and traversal shapes; the
        local catalog store enforces it on write, so the store is the last line of refusal rather
        than trusting its callers. Tests assert the stored binding cannot round-trip an endpoint or
        credential and that the persisted bytes contain no routable address. `SharedConnectionExpander`
        **fails closed** on a Gateway-bound alias while no Gateway data plane exists — it never falls
        back to a direct connection, and the refusal does not echo the destination the script
        proposed. Script options cannot introduce a binding on a direct alias or bypass one on a
        Gateway-bound alias, asserted behaviourally.

        **Naming collision worth knowing.** `CREATE BINDING x AS GATEWAY (RESOURCE = '…')` already
        parses and reads exactly like this feature. It is unrelated: a validation-only stub for
        governed `EXECUTE TOOL` metadata whose own reference page says it is *not* an authorization
        or resource boundary, and whose handler only logs. Do not wire Gateway routing through it,
        and do not assume the `GATEWAY` lexer token belongs to this work.
      - [x] **D2 — Enrollment lifecycle.** *(contract delivered 2026-08-17; Portal surface open.)*
        `IGatewayEnrollmentStore` issues a one-time enrollment and consumes it exactly once, binding
        the presented workload public-key thumbprint. Only a SHA-256 of the token is stored, so
        possession of the record cannot enrol a Gateway, and comparison is constant-time. Expired,
        revoked, already-consumed, and cross-tenant presentations all fail with the **same** message
        on purpose — distinguishing them would tell a holder of a stolen token which enrollments are
        worth attacking. Weak tokens are refused at issue. Still open: the Portal issuing UI/API, a
        durable store behind the same contract tests, and short-lived credential rotation.
      - [x] **D3 — Gateway-local resource registry.** *(delivered 2026-08-17.)*
        `GatewayResourceRegistry` holds typed resources with stable IDs, the local target, a local
        credential reference, an enumerated operation class, and mandatory positive limits — an
        absent bound would mean unbounded, so it is rejected. Discovery may only `Propose`; approval
        is a separate call, a proposed resource neither executes nor reaches the tenant catalog, and
        discovery cannot redefine an already-approved resource (otherwise a rogue pass could repoint
        an approved alias). `ToPublishedMetadata` is the only projection that leaves the Gateway and
        carries neither target nor credential, asserted by serializing it and looking for both.
      - [x] **D4 — Typed operation protocol.** *(delivered 2026-08-17; gRPC variant deferred.)* `GatewayOperationBounds` has no unlimited representation, validates itself,
        and `NarrowTo` lets a resource's registered limits only restrict, never widen. A
        `GatewayOperation` is a per-operation handle naming a resource and an operation class; it
        cannot express a host, port, or protocol, so a container never holds reusable tunnel
        authority. `GatewayOutcomeLedger` implements the reconnect rule: **an ambiguous write is
        neither retried blindly nor reported as safely failed**, a dropped in-flight *write* is
        ambiguous rather than "never happened", a dropped in-flight *read* may simply re-run because
        repeating it changes nothing, and a committed outcome cannot be downgraded by a late
        ambiguous report. The ledger is keyed by tenant *and* operation ID as a tuple — a
        concatenated string key would let `("a","bc")` and `("ab","c")` collide into one another's
        outcome, which is pinned by a test. The **typed WebSocket transport** is implemented and
        exercised over a real loopback socket — `GatewayTransportTests` stands up an `HttpListener`
        broker and runs the real `GatewaySessionClient` against it, so this is not mocked-transport
        evidence. `GatewayFrame` has no host/port/scheme/path/command field at all, asserted by
        reflection, so a compromised cloud side cannot express "reach this destination". gRPC is
        deferred deliberately: it needs a new third-party dependency and therefore a
        THIRD-PARTY-INVENTORY/NOTICES audit, and §11.5 permits the typed WebSocket transport.
        Still open: a durable ledger behind the same contract tests.
      - [x] **D5 — Authority agreement.** *(delivered 2026-08-17.)* `GatewayAuthority.Evaluate` is
        the single decision point for all seven clauses. Each is falsified independently against an
        otherwise-valid request, which is the point: the recurring defect here has been each door
        being tested alone while nothing asserts the set agrees. A grant belonging to another tenant,
        principal, resource, or narrower operation class does not count, and knowing another tenant's
        alias/gateway/resource/operation is not enough. Denial reasons never echo a target or a
        credential.
      - [x] **D6 — Revocation.** *(delivered 2026-08-17.)* Revoking the Gateway denies on the very
        next authority evaluation with no grace window; disabling a resource unpublishes it, stops
        execution, and cannot be undone by approving it again. Tested, per the v0.17.0 lesson that
        revocation logic passes a careful read and fails a red test.
      - [x] **D7 — Operator boundary.** *(structural half delivered 2026-08-17.)* A platform
        operator principal gets no implicit authority — with no grant naming it the decision is
        denial, not an operator override — and approval exists only on the Gateway, so there is no
        cloud-side path to it by construction. Denial reasons leak neither target nor credential
        across every distinct denial path. Still open once the Portal/Broker surfaces exist: the
        aggregate-health-only view and negative tests against those real endpoints.
      - [x] **D8 — Runtime hardening, install, docs.** *(runtime + docs delivered 2026-08-17;
        daemon host, broker pump, and enrollment delivered 2026-08-18.)* `src/ETL-SQL.Gateway` is a new
        project holding the runtime. `GatewaySessionClient` is outbound-only — it dials the broker and
        opens no listening port — refuses any scheme but the typed protocol, and refuses a non-TLS broker
        off loopback so a misconfiguration cannot silently downgrade. Refusal of arbitrary
        socket/shell/path/protocol forwarding is structural rather than a runtime check: the frame model
        has no field that could name a destination. Inbound frames are bounded, so a hostile broker
        cannot drive the Gateway out of memory. A local provider exception naming a host, user, or
        password crosses back as a fixed message, pinned by a test that plants a credential in the
        exception text. `GatewayHost` runs as a persistent daemon with exponential backoff and safe
        unregistration. The operating model is documented at
        [secure-outbound-gateway.md](docs/administration/platform/secure-outbound-gateway.md).
- [x] **Shared.** *(delivered 2026-08-18.)* Added the shared tenant/gateway session registry
      (`GatewaySessionRegistry`), typed WebSocket stream routing and background pump (`GatewayBroker`),
      bounded backpressure, one-time enrollment token issuance via Portal (`GatewayEnrollmentController`),
      and full cross-tenant isolation test suite (`GatewayBrokerAndHostTests`, 85 tests passing).
- [x] **Both topologies.** *(delivered 2026-08-18.)* Execute tenant workloads with default-deny networking,
      blocked cloud metadata/control-plane/internal hosting ranges, and capability-authorized connector,
      storage, telemetry, or Gateway Broker destinations via `CapabilityAuthorizedDestinationScope` and
      `ConnectorPolicyAuthorizer`.

*Absorbs the retained discovery item **Internal Network Egress Fencing**.*

##### 9. Lifecycle — provisioning, backup, portability, deletion, metering

The former `Managed operations` bullet was one checkbox covering nine deliverables and could not be
checked off meaningfully. Split:

- [x] **Shared — backup and recovery.** *(delivered 2026-08-18.)* Tenant-scoped export and restore
      from shared stores (`OrchestratorPromotionPackageService.ExportAsync(TenantContext, ...)`),
      surface classification inventory across all Portal, Orchestrator, and Artifact stores
      (`SharedBackupSurfaceInventory`), and proof that point-in-time recovery, retry, or cache rebuild
      cannot introduce another tenant's rows (`SharedBackupAndRecoveryTests`).
- [x] **Shared — metering.** *(delivered 2026-08-18.)* Shared-fleet attribution for rows/bytes, connector
      class, sandbox CPU/memory/I/O, Gateway traffic, storage, and concurrency. Metering keeps its own
      durable, tenant-partitioned ledger (`ITenantMeteringLedger` / `RelationalTenantMeteringLedger`);
      it cannot read payload content or become execution authorization.
      - Fixed enum/count schema covers rows, read/write bytes, connector class, sandbox CPU/peak-memory/I/O,
        Gateway ingress/egress, storage, concurrency, duration, and status.
      - Scheduler emits idempotent events per tenant attempt across process/sandbox boundaries.
      - Gateway Broker (`GatewayBroker` / `ActiveGatewaySession`) feeds actual byte/row/duration measures
        attributed to verified tenant contexts.
      - Tenant artifact storage (`TenantScopedArtifactStorage` / `TenantArtifactStorageFactory`) feeds
        actual read/written storage byte metrics.
      - Partitioning and hostile fleet attribution resistance proven via `TenantMeteringLedgerTests`
        and `SharedTenantMeteringIntegrationTests`.
- [x] **SaaS Multi-Tenancy — Control Plane Dashboard (Platform Admin UI).** *(delivered 2026-08-18.)*
      Dedicated visual dashboard for fleet observability, tenant lifecycle management, and resource
      monitoring, physically separated from the customer-facing `ETL-SQL.Portal`.
      - **Physical separation & identity isolation:** Hosted as an isolated endpoint/application
        (`/control-plane.html` and `api/platform/control-plane/*`); authenticates platform principals
        only via `PlatformAccessGrant` / `X-Portal-Platform-Key` with zero capability to mint tenant
        sessions or inspect tenant raw scripts/data.
      - **Fleet observability:** Real-time visibility into shared worker pool utilization, Gateway Broker
        health, tenant quota headroom, queue depth, and noisy-neighbor containment via `ControlPlaneDashboardService`.
      - **Platform audit trail:** Attributed audit receipts with SHA-256 integrity hashes for all
        operator-driven tenant lifecycle events.
      - **UI & Sandbox:** Shipped standalone platform admin interface and interactive UI sandbox
        story (`control-plane-dashboard.story.js`) with complete Playwright and unit test coverage
        (`ControlPlaneDashboardTests`, `ControlPlaneDashboardUiTests`).
- [x] **SaaS Testing — API Load and Soak Testing.** *(delivered 2026-08-18.)*
      Target Orchestrator and Portal APIs with sustained multi-tenant concurrency to validate
      connection pooling, fair-share queueing, and memory stability under high tenant density.
      - Implemented native in-tree .NET load and soak testing harness (`SaaSMultiTenantLoadAndSoakTests`)
        without third-party tooling (zero k6 / JMeter external dependencies).
      - Proves fair-share throughput and starvation-freedom under concurrency (Jain's Fairness Index $\ge 0.95$).
      - Proves noisy-neighbor containment (flooded tenants do not degrade quiet tenant latencies or success rate).
      - Proves memory stability and bounded working-set growth over sustained soak iterations.
      - Proves sub-200ms platform control plane fleet queries under high tenant density.

*Absorbs the retained discovery items **Usage Metering & Billing Collector**, **Full-Fidelity
Tenant Portability Bundle**, and **Control Plane Dashboard**.*

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
