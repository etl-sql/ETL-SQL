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

#### Slice 0 — Tenant scoping (sequence this first)

**Why first.** These are primary-key changes, and every later slice inherits the object identity they
establish.

**No production data exists (confirmed 2026-08-15), so this is a greenfield schema change.** Recreate
tables rather than writing data-preserving migrations, and do not build back-compatibility shims for
grants, object identity, or assertion versions. This also settles the open question in A3: bump the
assertion to v2 outright and require both sides current, rather than accepting v1 tokens for a rolling
upgrade window that no deployment needs.

**The state this slice started from — historical, all of it now fixed. Kept because it is the
argument for the design, not a description of the code.** `Jobs` carried `TenantId`, bound from the
signed assertion or fixed host authority, with rebinding by another tenant refused. But `Schedules`
and `Notifications` keyed on `Name` alone; so did the `JobSchedules`/`JobNotifications` join tables,
`JobHistory`, `JobState` (where watermarks live), `Bundle*`, `JobHistoryDaily` — and
`OrchestratorObjectAcls`, whose key was `(ObjectKind, ObjectName, PrincipalKind, PrincipalId)` with
every query filtering on name alone. In a shared Orchestrator two tenants with a schedule named
`nightly` were one row, and a grant on `JOB:daily_load` reached every tenant's `daily_load`. Managed
Dedicated is host-fixed to one tenant so nothing collided in practice; it was a latent gap that Shared
SaaS would have turned into a cross-tenant authorization leak.

**The target design already exists and the schema does not implement it.**
`SharedBackupSurfaceInventory` declares a partition mode and authoritative root for every one of these
surfaces: `Schedules`, `Notifications`, `JobHistory`, `JobColumnMetrics`, `JobDataQualityFailures`,
`JobStatementMetrics`, `JobHistoryDaily`, `JobState`, `BundleVersions`, `BundleFiles`, and
`BundleDependencies` are all declared `DirectTenantColumn` rooted on `TenantId`, while
`OrchestratorObjectAcls` is declared `TenantRootJoin` rooted on `ObjectId` and
`JobSchedules`/`JobNotifications` on `JobId`. None of those columns exist. `SharedBackupSurfaceInventoryTests`
only checks the inventory's internal consistency — duplicates, classification, required surfaces — and
never reconciles it against a real schema, which is why the contradiction has gone unnoticed. Treat the
inventory as the specification for this slice, and close the gap that let it drift.

**Slice 0 status (2026-08-16).** Everything except 0.11 is shipped, and 0.11's answer turned out to be
a gap rather than a confirmation — see its entry; it needs a product decision before it can be built. The identity is a
type (`JobId`/`ScheduleId`/`NotificationId`), not a string, because a job has both a name and an id,
both are text, and passing one where the other belongs fails silently: the write matches zero rows and
nothing throws. That one change surfaced 97 such faults the compiler had been accepting, plus six live
defects that no test had caught — positional `SELECT *` reads shifted by the new columns, a
`GetRecentColumnMetricsAsync` overload that no longer implemented its interface member so the empty
default won, `JobStatementMetrics`/history/state reads joining tenants on name, a rollup writer that
never populated the id its reader filtered on, and a tenant-deletion path that would have deleted
another tenant's history once two tenants shared a job name.

- [x] **0.1 Surrogate object identity — do this before anything else.** *Decided 2026-08-15: surrogate
      IDs, matching the inventory's declared `TenantRootJoin` design and the `portal-datasetacls` /
      `portal-reportacls` precedent.* `JOB`, `SCHEDULE`, and `NOTIFICATION` gain a stable surrogate ID;
      name becomes unique *per tenant* rather than globally, and ACLs, join tables, history, state, and
      metrics reference the ID rather than the name. This is also what makes rename and re-creation safe,
      and it is what closes the shared-name takeover hazard at the root instead of by ownership check.
      Every item below references this identity.
- [x] **0.2 Tenant-key the ACL store.** Every read, write, and delete takes the request's verified
      `TenantContext`; no lookup resolves on object name alone. Follow the composite tenant/kind/logical
      identity proven by `SharedTenantResourceRegistry`.
- [x] **0.3 Inherit the shared-surface contract.** The ACL store's test class inherits
      `SharedTenantSurfaceContractTests` (`tests/ETL-SQL.Tests/Multitenancy/`), so all six cases —
      including the `acme`/`acme-evil` prefix trap and cross-tenant enumeration — are answered by a
      contract rather than by reviewer judgement. `OrchestratorObjectAclSharedSurfaceTests` presents
      the grant store as a shared surface — a logical id is a job name, the value is the principal it
      is granted to — and routes every access through the store's own tenant-qualified resolution
      rather than scoping anything itself, so what the contract judges is the store.
- [x] **0.4 Tenant-key `Schedules` and `Notifications`** to match `Jobs`, with the same signed-tenant
      binding and refuse-to-rebind behaviour, plus the `JobSchedules`/`JobNotifications` join tables per
      the fork in 0.1.
- [x] **0.5 Defence in depth in the decision path.** `OrchestratorObjectAuthorizationService.CanAsync`
      compares the caller's tenant to the object's tenant itself, rather than relying on endpoint-level
      checks alone — a grant must never be evaluated across a tenant boundary even if a future endpoint
      forgets its filter.
- [x] **0.6 Run history and metrics.** `JobHistory` gains `TenantId`; `JobColumnMetrics`,
      `JobDataQualityFailures`, and `JobStatementMetrics` follow it as the inventory already declares.
      They key on `JobHistoryId` today, which is globally unique, so they are not currently *wrong* —
      but the declared contract is a direct tenant column, and the `/api/history` and
      `/api/data-quality/*` endpoints must filter on tenant, not only on the ACL decision.
- [x] **0.7 Job state and watermarks.** `JobState` keys on `(JobName, StateKey)`. Two tenants running a
      job of the same name would share one high-water mark, which is silent data corruption rather than
      a disclosure — the worse failure of the two, since nothing would report it.
- [x] **0.8 Bundles.** `BundleVersions`, `BundleFiles`, and `BundleDependencies` key on bundle name and
      version. Include `bundle://` URI resolution and the latest-version pinning path, so one tenant's
      publish cannot become another tenant's pinned dependency.
- [x] **0.9 Rollups and tenant deletion.** `JobHistoryDaily` keys on `(Day, JobName)`. The rollup writer
      in `SchedulerService` and the deletion path in `SharedTenantLifecycleStore` — which currently
      resolves a tenant's rows via `WHERE JobName IN (SELECT Name FROM Jobs WHERE TenantId = @tenant)` —
      both break once names repeat across tenants: deletion would remove another tenant's history. Both
      paths move to the surrogate ID from 0.1.
- [x] **0.10 Host metrics — bind the node, do not fake per-tenant gauges.** `HostMetrics` samples
      `MemoryLoadPercent`, `ProcessCpuPercent`, `HostCpuPercent`, and free disk per node. On a Shared node
      running several tenants' work those gauges do not decompose by tenant, so a `TenantId` column there
      would be a number that looks meterable and is not. Instead give `HostMetrics` and `HostMetricsDaily`
      the node's **tenant and capacity-pool binding** — the sandbox provider already fixes a Dedicated host
      to exactly one tenant and pool and refuses foreign placement. Dedicated capacity then attributes
      cleanly to a tenant, and Shared capacity is honestly marked shared rather than silently misattributed.
- [ ] **0.11 Confirm the billing signal is the usage ledger, not the host gauges.** `TenantUsageRecords`
      already carries what metering needs per run — `TenantId`, `JobHistoryId`, `WorkloadKind`, `Status`,
      `RowsProcessed`, `PeakMemoryBytes`, `CpuTimeSeconds`, `DurationMs`, `RecordedAtUtc`, unique per
      tenant/run and indexed by tenant and time. Verify it is written for **every** run path — scheduled,
      ad-hoc, sandboxed, and failed — since a metering table with gaps is worse than none. Any future
      billing work reads this, with 0.10 supplying Dedicated capacity attribution alongside it.
      **Checked 2026-08-15 and the answer is a gap, not a confirmation.** The only writer is
      `SchedulerService`, in the per-attempt `finally`, guarded by `job.TenantId` being non-empty — so
      scheduled runs and failed attempts are covered, and **ad-hoc runs are not metered at all**. An
      ad-hoc run now records history through `LogAdHocRunStartAsync`, which has no job and therefore no
      tenant binding to meter against, so closing this needs the ad-hoc path to carry a tenant before it
      can write a usage row. Decide whether unattended ad-hoc execution is billable before building it —
      if it is not, say so here and the gap becomes the specification.
- [x] **0.12 Close the drift gap.** Add a test that reconciles `SharedBackupSurfaceInventory` against the
      actual orchestrator and Portal schemas, so a declared `DirectTenantColumn` must exist and a declared
      `TenantRootJoin` must have a resolvable root. Without it the inventory can drift back the moment a
      table changes, and backup, restore, portability, and tenant-deletion evidence all rest on it.

#### Slice A — Portal-issued Orchestrator assertions

- [x] **A1 Exchange endpoint.** Add `POST /api/auth/orchestrator-assertion` to the Portal: an
      authenticated caller (local/LDAP session, OIDC session, or service account) receives a
      short-lived audience-bound assertion plus its expiry. The Orchestrator needs **no new trust
      code** — it already validates exactly this token, which is why this shape was chosen. Reuse
      `OrchestratorProxyService.CurrentIdentityAssertionAsync` for principal/group/tenant resolution
      rather than duplicating it.
- [x] **A2 Split the scope ladder.** Replace the single `orchestrator.execute` scope in
      `ServiceAccountScopes` with four scopes mirroring the permission vocabulary that already
      exists, so there is one ladder and not two: `orchestrator.read` (view jobs, history, metrics,
      DQ status, stewardship) → `READ`; `orchestrator.execute` (trigger, kill, resume, variable
      overrides) → `EXECUTE`/`OVERRIDE`; `orchestrator.publish` (create objects, `MANAGE` what you
      own) → create + owned `MANAGE`; `orchestrator.admin` (manage anyone's grants). Migrate existing
      accounts: today's `orchestrator.execute` becomes `read`+`execute`, never `publish`. Update
      `ServiceAccountScopeMiddleware` and the scope checkboxes in `operations-admin.js`.
- [x] **A3 Enforce the ceiling.** Carry scopes in the assertion payload (bump
      `OrchestratorIdentityAssertion.CurrentVersion` to 2 and reject v1 outright — see Slice 0) and cap
      the ACL decision in `OrchestratorObjectAuthorizationService.CanAsync` by the token's scope. A
      `publish` account still cannot touch a job it was not granted; a `read` account cannot trigger
      one however broad its ACL.
- [x] **A4 `ORCHESTRATOR` connector authentication.** Mirror `PortalDataSource.EnsureAuthenticatedAsync`
      (`api/auth/login`, cached token, re-auth 5 minutes before expiry) — that connector is the
      precedent and the shapes should match. Accept **both** credential forms, because an OIDC-federated
      user has no Portal password to put in a connection: `USER`/`PASSWORD` for local and LDAP
      accounts, `CLIENT_ID`/`CLIENT_SECRET` for a Portal service account. Passwords use the canonical
      quoted `'SECRET:name'` form. Emit the assertion header on every request in
      `OrchestratorDataSource`, which today sends only `X-Orchestrator-Key`.
- [x] **A5 Negative tests.** A connector with no identity is denied against a federated Orchestrator;
      a token cannot claim a role its owner does not currently hold; a token cannot assert a foreign
      tenant; an expired assertion is refused and re-exchanged; scope ceiling is enforced independently
      of the ACL.

#### Slice B — Stable principal keys

**Shipped 2026-08-16.** `PortalUser` and `Group` carry an immutable `PrincipalKey`, minted once and
never reissued, and grants resolve against it. Closing this also fixed a defect in the same code path:
the assertion carried numeric group ids where row-level security expects group *names*, so
`HAS_GROUP('Finance')` matched in the Portal's own execution path and could never match in a
scheduled job. The assertion now carries both — keys for grants, names for RLS — because they answer
different questions and carrying one silently breaks the other.

- [x] **B1 Portal migration.** Add an immutable per-row key to Portal users and groups. Grants store
      numeric IDs today, which dangle on rename, OIDC re-provisioning, or a rebuilt Portal database.
      Portal migrations are dual-provider and reject `AlterColumn` — add sibling columns rather than
      widening, and review the scaffolded Postgres migration for snapshot drift.
- [x] **B2 Carry and resolve stable keys.** Assertion carries them; `OrchestratorObjectAuthorizationService.Matches`
      resolves against them.
- [x] **B3 Stable keys are the only form.** No production data exists, so there is no numeric-ID
      migration to write — ship the schema keyed on stable identifiers from the start. Still define the
      unresolvable-key behaviour, since re-provisioning and restore can produce one at runtime: fail
      closed and report the orphan, never silently widen.
- [x] **B4 Tests.** Renaming a group preserves its grants; an OIDC user re-provisioned under the same
      `sub` keeps theirs; a rebuilt Portal database does not silently transfer a grant to whoever now
      holds the old numeric ID.

#### Slice C — Grant administration surface

**Closed 2026-08-16.** Testing the surface end to end — Portal RBAC, the minted assertion, the
Orchestrator's decision, and its answer coming back — found two defects that reading the code did not:
the Portal staged its grant audit row without ever saving it, so no accepted change was recorded; and
the grant API returned its enums as ordinals, so the Access panel rendered an empty chip for a grant
that existed and the CLI printed `1:key = 2`. Both were surfaces that looked implemented and were
never asserted. The panel's own rules are now covered by `scripts/test-orchestrator-acl-ui.mjs` in the
JS lane, alongside the sandbox story.

- [x] **C1 Portal ACL API.** Proxy routes for orchestrator object grants, gated by `orchestrator.admin`
      plus Portal RBAC. Follow the `{alias}/acl` pattern already in `ConnectionsAdminController`.
- [x] **C2 ACL panel** in the Orchestrator tab's detail panel, following `dataset-acl-ui.js`. Grants
      are unmanageable through the product today — setting one requires hand-crafting a signed
      assertion with the shared secret.
- [x] **C3 CLI.** `etl-sql admin orchestrator grant|revoke|show` against the HTTP API, for headless
      and scripted provisioning.
- [x] **C4 Surface attribution.** `CreatedBy`/`ModifiedBy` are persisted and currently invisible; show
      owner in the jobs table and detail panel.
- [x] **C5 Tests.** Only an authorized administrator can change grants; the panel reflects the
      Orchestrator's state rather than a Portal-side copy.

#### Slice D — Ownership lifecycle and solo → team promotion

**Closed 2026-08-16.** Ownership is now writable exactly once by creation and thereafter only by an
administrator, through `PUT /api/authorization/{kind}/{name}/owner` and `POST /api/authorization/adopt`
(`GET /api/authorization/unowned` lists what needs one). Reassignment is administrator-only rather than
owner-only because an owner may manage their own object: an owner who could hand ownership on could
widen access to it without anyone administering it. Two silent-adoption paths were closed on the way —
`TrySaveJobAsync` and both catalog upserts filled in a missing `CreatedBy` with whoever saved next, so
an edit decided accountability quietly. Closing the slice also found a shipped defect that had killed
the whole Orchestrator page: an unescaped apostrophe in `orchestrator.html`'s inline module, which is
a parse error, so none of the page's JavaScript ran. `scripts/test-portal-inline-scripts.mjs` now
parses every inline script in the Portal and Player pages in the JS lane.

- [x] **D1 Owner reassignment.** Admin-only endpoint plus audit. `CreatedBy` is immutable by design,
      so today an owner who leaves makes the object `Admin`-only forever.
- [x] **D2 Unowned objects fail closed.** A pre-existing object with no owner is `Admin`-only until
      adopted. Fail-open was rejected: it is an authorization hole that ages badly, and attaching a
      Portal is an administered event, not a surprise.
- [x] **D3 Bulk adoption** in both CLI and UI, so a solo box that attaches a Portal can assign owners
      to everything it already has.
- [x] **D4 Promotion preflight rule.** `DeploymentPromotionPreflightService` is artifact-oriented today
      (`DP001`–`DP008` cover backward moves, inventory, reparse points, portable artifacts, and raw
      credentials) and says nothing about orchestrator objects. Add a finding that reports unowned
      objects before promotion completes.
- [x] **D5 Grant-resurrection test.** Dropping an object deletes its grants; recreating the same name
      must not inherit them.

#### Slice E — Legacy mode and the solo boundary

- [ ] **E1 Make the mode visible.** `RequireFederatedIdentity` defaults to "is the bind address
      non-loopback", so a shared Orchestrator behind a reverse proxy on loopback silently runs with
      one root key and no warning. Report the authorization mode on the health endpoint and warn at
      startup when a multi-user deployment is running in legacy mode.
- [ ] **E2 Hold the boundary.** The solo admin CLI path must never create orchestrator-local
      principals or grants — that is the second identity model this design rejected, wearing a CLI.
- [ ] **E3 Document the escape hatch** as Solo-only, with the promotion path in D3.

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

- [ ] **Schedule and notification objects.** `/api/schedules` and `/api/notifications` — the unified
      catalog — have no UI at all. Create-job offers only interval/unit/at-time: no cron, no named
      shared schedules, no notification wiring or dispatch.
- [ ] **Job metrics.** The sparkline is duration-only. Rows processed, `/api/data-quality/status`,
      `/api/data-quality/failures`, and `/api/stewardship/*` are live endpoints with no surface here.
- [ ] **Bundles.** `jobType`/`targetPath` (`bundle://`) is supported by the API but the modal only
      accepts script paths. No version pinning, dependency, or deployment view.
- [ ] **`DisplayName`, `Description`, and `Options`** — including the `SandboxProfile` that admission
      control now reads out of `Options`.
- [ ] **Watermark state.** Declarative incremental watermarking shipped without any way to inspect or
      reset a high-water mark, which is what an operator needs at 2am.
- [ ] **Definition change log** — attribution columns exist; there is no view of who changed what.
- [ ] **Table ergonomics** — search, pagination, and a calendar beyond the 24-hour Gantt.
- [ ] **Job-to-job dependency view.** Script flow is per-job; there is no chain view.

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