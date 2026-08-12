# ETL-SQL Development TODO List

Use this list as the execution ledger for active-release and roadmap work. Once work is verified,
record its notable outcome in `CHANGELOG.md` and mark the entry `[x]`; retain completed entries so
progress remains visible. Reopen an entry as `[ ]` with a concrete correction path if later evidence
invalidates its completion claim.
`ROADMAP.md` remains the high-level product-direction source, and its initiatives are decomposed into
actionable tasks here.

---

## Audit-verified completions retained for progress history

These entries were revalidated during the 2026-08-11 closed-item audit. They summarize completed
claims that had been removed under the former active-only ledger rule; incomplete claims remain open
in their original roadmap sections with correction paths.

- [x] Dedicated tenant context is server-derived across support, export-plan, onboarding, and
      caller-relabel boundaries.
- [x] Shared identity stores, routed authorities, OIDC flow state, delegated tenant administration,
      HTTP credential binding, and N→N+1 migration evidence pass together.
- [x] Dedicated policy authority rejects cross-tenant mutation/read and stale foreign distribution
      rows; platform-principal and portable-policy evidence passes.
- [x] Dedicated storage capabilities, artifact prefixing, sibling-root rejection, checkpoint/spill,
      and legacy collision evidence pass.
- [x] Tenant portability `export`, `validate`, `preflight`, and `import` acceptance, tamper,
      collision/dry-run, redaction, disabled-workload, and SaaS-exit evidence passes.
- [x] Raw quarantine preview authorization, bounded execution, audit, manifest metadata, UI sandbox,
      and script-handled quarantine routing are shipped and verified.
- [x] Provider-neutral key authority, dedicated/shared policy partitioning, server-derived storage
      capabilities, and tenant artifact roots are implemented.
- [x] The `admin tenant` verb family and signing-key rotation/distribution contract are implemented.
- [x] The syntax-addition contribution checklist and provider-dialect compiler boundary are in place.
- [x] The low-threshold spill lane, schema-stability invariant, engine-surface corpus,
      `-ContinueOnFailure`, data-shape fuzzing, rule-catalog property test, and AST round-trip
      property test are implemented. Lane-wide bounded completion remains separately open below.
- [x] Session-scoped spill placement and encrypted session save/load persistence are fixed; invalid
      session-save callers now fail explicitly instead of silently discarding state.
- [x] The sample sweep passes (178 passed, 17 intentionally skipped), the partitioned-window spill
      P0 and deep boolean AST stack overflow are fixed, and their regression evidence passes.
- [x] Missing-file `BULK INSERT`, blank/null flat-file round trips, and empty numeric-field handling
      fail or convert according to the documented contracts.
- [x] The 12 inherited release-branch baseline failures identified on 2026-08-10 now pass in focused
      and expanded acceptance runs.

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

### Add a CI sample lane

- [x] Consider a CI sample lane. The gate is currently the only thing that runs samples, and it is
      Windows-and-PowerShell only.

      **Completed 2026-08-12.** CI runs a dedicated, independent Windows/Linux matrix. Each runner
      builds the application once and executes its native sample validator for two passes. The first
      local Windows run exposed a missing BULK fixture, undeclared Docker prerequisites, and an
      unisolated Orchestrator database; those are fixed, including removal of connection-string
      printing from Docker samples. The complete rerun reports 394 executions: 346 passed, 48
      prerequisite skips, 0 failed.

### Automate the MSI in-place upgrade check

Today this is a manual, elevated step in the release checklist, and it is the kind of step that
quietly stops happening. It is the only thing that catches a WiX major-upgrade regression — a
failure mode that is otherwise **silent**, producing a side-by-side second install rather than an
error. The gate's N→N+1 drill covers the data/engine layer, not the installer.

It is manual because a `perMachine` MSI needs elevation and nobody wants to mutate their own
workstation. **Both reasons vanish on a GitHub-hosted `windows-latest` runner**: it executes as an
administrator, so `msiexec /qn` needs no UAC, and it is ephemeral, so installs leave nothing behind.

**Already built — verified against the repository 2026-08-05, not from memory.** `scripts/Test-MsiUpgrade.ps1`
exists and asserts the whole sequence below; `.github/workflows/msi-upgrade.yml` runs it on
`release/**` pushes and `v*` tags; and the manual step is already gone from
[release-checklist.md](docs/releases/release-checklist.md). The first real run fired on the
v0.18.0 branch push.

- [x] **Make it a required status check.** The only part still outstanding, and it is a repository
      setting rather than code — verify in branch protection once the run on `release/v0.18.0` has
      gone green at least once. **When doing so, add a companion always-succeeds job**: the push
      trigger is now path-filtered, and a path-filtered workflow reports *skipped* rather than
      *success*, so a required check that never reports blocks every unrelated pull request.

      **Completed 2026-08-12.** Run 31204909629 on `release/v0.18.0` completed successfully, the
      workflow's unconditional `msi-upgrade-gate` reports the stable **MSI In-Place Upgrade** check
      for both applicable and intentionally skipped changes, and the GitHub `main` protection rule
      now lists that check alongside the existing CodeQL, build, extension, and enterprise gates.

**First real run, and what it changed (2026-08-05).** The gate failed on itself: `Get-MsiProperty`
returned `Object[]` because two COM calls leaked to the pipeline, and PowerShell's `-ne` against an
array is a filter rather than a comparison — so it reported "UpgradeCode changed" while printing the
same GUID twice, and could never have passed. Finding a pure-logic bug cost 26 minutes of download
and build.

Three changes so that cannot repeat, and so the next mistake is cheaper:

- Non-elevated logic moved to `scripts/MsiUpgrade.Helpers.ps1`, which is side-effect free on load.
- `-StaticChecksOnly` runs the contract (same UpgradeCode, ascending version) with no elevation and
  no install, in about a second, on any machine. The workflow runs it as its own step before the
  install sequence so the log says which half failed.
- `MsiUpgradeHelperTests` pins the guard that catches the whole class — a multi-value read now
  throws instead of silently becoming an array filter. Mutation-verified.
- The push trigger is path-filtered to the installer, its scripts and `Directory.Build.props`. A
  documentation change no longer pays 26 minutes.

The elevated half still has no local path on Windows Home — Windows Sandbox and Hyper-V are not
available there — which is the reason for pushing everything testable out of it.

Static checks are a useful cheap complement but are **not** a substitute: identical `UpgradeCode`,
ascending `ProductVersion`, and an unchanged `MajorUpgrade` element rule out the most common cause
and nothing else. Consider adding them as a fast unit test over the built MSI regardless.

---

## Roadmap execution backlog

These tasks decompose the future tracks in [ROADMAP.md](ROADMAP.md). Their presence here makes work
reviewable; it does not assign them to v0.18.0 or turn candidate phases into release commitments.
Keep the roadmap's P0/P1/P2 ordering unless a release plan explicitly changes it.

### Platform — Deployment Profiles and Upgrade Certification

#### P2 — Deployment-profile certification

- [ ] Add current per-profile and per-transition evidence to release claims. Report Managed Dedicated
      and Shared SaaS separately; neither inherits the other's claim status.
- [x] Certify that Team is a single-node provider configuration rather than a separate implementation:
      no Team-only parser, evaluator, connector, catalog, UI, checkpoint, or promotion model.

      *Moved here from the SaaS track's Phase A (2026-08-09). It is an assertion about the Team
      profile and belongs to profile certification; filing it under a SaaS gate implied Team was
      something SaaS had to clear rather than something every release re-proves.*

      **Completed 2026-08-12.** `TeamProfileImplementationBoundaryTests` statically rejects
      Team-named implementations and Team runtime branching across the common engine, connector,
      Orchestrator, Portal, reporting, and browser-runtime source roots. The expanded Team
      certification passed its contract, durable quality/catalog, notification, Portal,
      scheduling/flight-recorder, and common-implementation phases. Development evidence:
      `certification-results/deployment-profiles/todo-team-expanded/20260812-091237/`.

### Portal — Comprehensive Product and UX Update

The remaining Portal-wide item is consolidating the last duplicated headers and focus-management
implementations without regressing the browser-covered dialog behavior.

#### P1 — Studio and collaboration capabilities

Both moved out of the SaaS track (2026-08-09). Each item's own text already said it was not a
SaaS-isolation prerequisite, so filing them under a SaaS heading made cross-profile Studio work look
blocked on hostile-tenant certification that it does not need.

- [x] **Portal ETL IDE Data Preview & Schema Browser**: add interactive schema inspection and bounded
      row previews of intermediate `#temp` tables and governed source connections. Cross-profile
      Studio capability: start with Solo/Team, require Enterprise connection ACLs, and certify tenant
      scope before enabling it in SaaS (SaaS domain 7).

      **Reopened by closed-item audit (2026-08-11).** The schema tree and report-manifest preview exist,
      but the acceptance criterion is bounded *row* preview for governed sources and intermediate
      `#temp` tables. Add an explicit row-preview contract (row/byte/time caps, ACL and tenant checks,
      redaction, cancellation), wire it into Studio, and cover source and `#temp` positive/negative paths.

      **Completed 2026-08-12.** Studio table nodes now expose cancellable **Preview rows** actions for
      governed shared connections and materialized `#temp` tables. Source preview resolves the caller's
      tenant-scoped catalog/ACL and authorized schema before the server constructs its query; temp preview
      replays only the accepted read-only prefix through materialization. Both execute through the audited
      interactive-run identity, redact cells, and enforce configurable row, byte, and wall-clock caps.
      Focused source/temp positive and negative coverage is green, including ACL non-disclosure, module and
      capability fencing, mutation rejection, bounded/redacted response projection, the 204-test Portal
      security lane, shared-asset drift checks, and the result-grid UI smoke test.
- [x] **Portal Script Concurrent Editing Locks**: implement optimistic concurrency plus collaborative
      edit/session leases that warn authors and prevent silent overwrite. Team/Enterprise
      collaboration work; SaaS additionally requires tenant-scoped lease keys, hard expiry,
      disconnect recovery, and negative cross-tenant tests (SaaS domain 5).

      **Reopened by closed-item audit (2026-08-11).** Save-time optimistic concurrency exists and the
      server exposes acquire/release lease endpoints, but Studio never calls those endpoints and there
      is no lease lifecycle coverage. Wire acquire/renew/release and owner/expiry warnings into the
      editor, recover on disconnect/expiry, enforce tenant-scoped keys, and add API plus browser tests.

      **Completed 2026-08-12.** Existing-report Studio sessions now acquire on mount, renew every two
      minutes, identify the holder and five-minute expiry, pause Save on contention or disconnect,
      retry after expiry/reconnect/back-forward-cache restoration, and release on cancel, navigation,
      or disposal. Server acquisition is an atomic predicate update, so competing nodes cannot both
      win and renewal no longer increments the report content version or makes the holder conflict with
      itself. Author permission and signed-tenant-to-report-owner matching fence the key; cross-tenant
      lookup is non-disclosing. API tests cover contention, expiry recovery, renewal, release, stable
      content versions, and a negative tenant boundary; the browser-asset smoke pins mount/renew/save-
      pause/release/recovery wiring. The 205-test Portal security lane and shared-asset drift check pass.

#### P1 — Accessibility and visual-system completion

- [x] Consolidate shared headers, identity, module gating, themes, spacing, icons, status chips,
      errors, loading states, and empty states into a shared component vocabulary.
      **Two of the ten are now shared, with the rest still per-page.**

      - **Dialog behaviour** — `js/dialog-a11y.js`: focus entry, Tab containment, focus restore,
        Escape. Adopted where there was none.
      - **Adopted in the admin panels** (v0.18.0) after a sweep for surfaces that render a failure
        as an emptiness. Two were found and both were access-control surfaces: folder permissions
        left the *previous* folder's grants on screen under the new folder's name when the load
        failed, and group membership rendered a failed read as "No members". Covered by
        `AdminPanelFailureStateTests`.
      - **States and status chips** — `js/portal-states.js`: loading, denied, failed, empty, and
        `statusChip`, extracted from the governance module's pattern. Guarded by
        `PortalStateVocabularyTests`, which asserts the vocabulary is complete, each state emits a
        distinguishable marker, a denial names the roles that would grant access, a failure refuses
        to invent content, and every caller-supplied value is escaped at the point of interpolation.
        Adopted in `connections-admin.js`, which previously rendered one message for both a 403 and
        an unreachable service — telling the reader the wrong thing half the time.

      - **Module gating** — `GET /api/portal/navigation` plus `js/portal-nav.js`. The server
        computes which top-level entry points to offer a caller; the shell applies the answer and
        never derives one. This found two live defects rather than just duplication, both of the
        "offers what it cannot deliver" class: **Studio was offered to every signed-in user**
        (pages revealed it whenever the capability *probe* succeeded, and that probe had been
        deliberately opened to everyone, so the roles holding no Studio capability saw a link to a
        403), and the **Docs link was offered where `/docs.html` 404s**, because whether the
        Documentation module is enabled is a server fact no token claim carries. A third copy of
        the rule, in `docs.html`, gated Orchestrator on a role name that does not exist.

      A recount while doing this: **identity, themes, spacing and icons were already shared** —
      `session-identity.js` on every page, `branding.js` owning the theme toggle and storage key,
      and the radius/shadow/colour tokens plus the `sidebar-nav-icon-*` set in `portal.css`. The
      TODO listed them as outstanding; they were not.

      Still per-page: **headers**. The `<header class="topbar">` block is copy-pasted across six
      pages. Its *gating* is now shared and guarded by `PortalNavigationVocabularyTests`, which is
      where the drift actually was; templating the markup itself would move it out of static HTML
      for a smaller benefit. The three inline focus traps in `index.html`, `admin.html` and
      `orchestrator.html` also still duplicate `dialog-a11y.js` (as does the drawer's own trap in
      `branding.js`); they work, so replacing them needs per-page browser coverage of their dialogs
      first rather than a blind swap.

      **Reopened by closed-item audit (2026-08-11).** The entry's own inventory leaves six duplicated
      headers and four per-surface focus traps. Migrate them through the shared shell/dialog helpers and
      add per-page browser coverage before closing this umbrella item.

      **Completed 2026-08-12.** All six authenticated shell pages now declare a one-line
      `data-portal-header` host; `js/portal-header.js` renders the stable branding, identity, theme,
      navigation, responsive-menu, and sign-out attachment points, with every server-gated destination
      hidden until `portal-nav.js` applies its answer. Reports, Admin, Orchestrator, Studio, and the
      responsive drawer now use `dialog-a11y.js` for focus entry, Tab containment, Escape dismissal,
      and focus restoration; page-local and drawer-local traps were removed, and close callbacks use
      `data-dialog-close`. The six-page responsive shell test passes, as do 62 focused navigation,
      dialog, state, failure, and visibility tests. The full Portal assembly produced no failure output
      but exceeded the five-minute command ceiling; its orphaned test processes were stopped.

### Portal — Data Quality Follow-through

- [x] Before quarantine preview becomes a polled or dashboard-refreshed surface, profile the per-request
      `ExecutionSession` cost and replace the full lexer/parser/linter/evaluator startup with a bounded
      reusable preview path if the measurements require it. Preserve identical policy, identity,
      redaction, timeout, and cancellation behavior.

      **Completed 2026-08-12 — reuse not warranted.** `QuarantinePreviewStartupMeasurement` now
      measures 5 warmups plus 25 complete construct/execute/dispose cycles. The current run reported
      0.8 ms median, 1.1 ms p95, and 1.3 ms maximum after warmup (197 ms cold first process/session).
      That cost is negligible beside a real connector read and does not justify retaining reusable
      execution state across steward requests. Keep the safer single-shot identity/policy/RLS/redaction
      boundary; rerun the explicit Performance test before introducing polling or dashboard refresh,
      and build bounded reuse only if the measured distribution materially changes.

### Engine — Hoist row-invariant BETWEEN bounds

Measured 2026-08-10 (`ColumnQualityCostTests`, 50k rows, rules attached to columns they pass):
per-row `@expect` rules are essentially free — `NOT NULL`, `NOT BLANK`, `LENGTH`, `IN`, `MATCHES`
all land within ~1 MB of a rule-free statement. What costs is calling the evaluator per row:
`BETWEEN` +28 MB, `EXPR` +61 MB. (`UNIQUE` +380 MB is the spill, and is by design.)

- [x] `BETWEEN`'s bounds are usually row-invariant — `BETWEEN DATEADD(DAY, -30, @RunDate) AND
      @RunDate` names no column — so they could be evaluated once per statement instead of per row.
      **Blocked on a design decision, not on effort:** hoisting a function call means deciding which
      functions are safe to evaluate once, and the codebase has no determinism classification for
      functions. Evaluating `GETDATE()` once per statement is arguably *more* correct than per row;
      `NEWID()`/`RAND()` clearly are not. Introduce the classification deliberately or restrict the
      hoist to literals, variables and parameters (which misses the headline case).

      A conservative whitelist walker is the safe shape either way: an unrecognized node means "not
      hoistable", so a missed node type costs performance, never correctness.

      **Completed 2026-08-11.** A conservative AST walker now admits literals, variables,
      parameters, operators, and an explicit deterministic-function whitelist (including the
      date-part slot in `DATEADD`/`DATEDIFF`); identifiers, subqueries, `RAND`, `NEWID`, and unknown
      calls remain per-row. Eligible bounds are evaluated during statement initialization, keeping
      validation on the synchronous path. Focused classification/correctness/allocation tests pass,
      and the 50,000-row measurement fell from 131.9 MB to 103.7 MB versus a 102.7 MB rule-free
      baseline.

### Orchestrator — Operations Triage and Run Flight Recorder

#### Deployment-profile portability review

Required by [Deployment_Profile_Standards.md](docs/architecture/standards/Deployment_Profile_Standards.md#feature-design-portability-review).
Smallest safe profile is **Solo**, and the capability must not become Portal-only.

- [x] **Team.** The reference case for this track; no profile change expected. The 200-job shop above
      *is* the Team profile, and Scheduling/Observability are already Green here.

      **Completed 2026-08-12.** The Team certification now includes the full
      `JobSchedulingIntegrationTests` and `OperationsTriageTests` suites. It verifies concurrent
      scheduling, bounded run/statement telemetry, checkpoint-safe history, cancellation and
      control races, redaction, and single-node queue drain without a Team-specific runtime.
      Evidence: `certification-results/deployment-profiles/todo-team-expanded/20260812-091237/`.
- [ ] **SaaS.** Observability remains **Red** until tenant telemetry and support-access separation are
      certified. Managed Dedicated must prove its tenant-specific store and tenant-approved support
      path; Shared must additionally prove server-derived scope in cross-tenant aggregation. Persisted
      statement text is tenant SQL, so platform triage is controlled support access rather than
      implicit platform authority. **Same cell as SaaS domain 8** (Audit, observability, and support
      access) below — this bullet owns the feature-side review, that domain owns the matrix cell.
      Neither is complete alone.
- [ ] Confirm no matrix cell moves backward, record Dedicated and Shared SaaS status separately, and
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

The current SaaS capability matrix needs a topology qualifier. Existing host-fixed negative tests
are meaningful **Managed Dedicated implementation evidence**; they do not make hostile shared
control-plane or execution-plane isolation Green. Until the matrix can represent the distinction,
release reviews must report Dedicated and Shared status separately and must not publish a generic
SaaS isolation claim.

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

The nine domains below are the axis the definition of done already uses: *the relevant Dedicated or
Shared matrix cell*. Each entry now maps to exactly one cell, and an uncovered cell is stated as a
**Gap** rather than being invisible. Regrouping surfaced six of them. Three items also left this
track entirely — their own text already said they were not SaaS-isolation prerequisites (see the
Portal UX and Deployment-profile certification sections above).

#### Isolation domains

Each domain states its **Dedicated** obligation and its **Shared** obligation, plus the Enterprise
contract it builds on where one exists. An entry is complete only when the matching matrix cell
carries a current linked evidence reference and the release review records the topology explicitly,
the way [v0.18.0](docs/releases/v0.18.0-deployment-profile-review.md) recorded its review. Do not
infer Dedicated support from an Enterprise happy path, or Shared support from Dedicated evidence.

##### 1. Tenant context and authority

- [ ] **Shared.** Prove tenant context is server-derived at every shared entry point — a negative
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

      This cell stays **open**: a contract with no shared implementations is not evidence that shared
      isolation holds. It closes when real surfaces exist and inherit it.

      **HTTP credential adoption started (2026-08-10).** Shared Portal JWTs now carry exactly one
      canonical tenant claim minted only from a trusted `TenantContext`. After normal JWT validation,
      middleware converts that signed claim into the request-scoped context consumed below controller
      code; missing, duplicate, and malformed claims fail before controller activation. An HTTP
      collision test proves spoofed tenant headers, tenant/issuer query values, and an equal shared
      secret row cannot replace the signed tenant or widen enumeration. The cell remains open because
      gateway, resource, run, object, storage, queue, and index surfaces still need equivalent concrete
      adoption evidence.

##### 2. Identity and delegated administration

##### 3. Policy, secrets, and keys

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

##### 8. Audit, observability, and support access

- [ ] **Dedicated.** Tenant-complete audit plus separately authorized and audited platform support
      access; aggregate platform health must not expose tenant script or data content. Observability
      must prove the tenant-specific telemetry store and the tenant-approved support path. Persisted
      statement text is tenant SQL, so platform triage is controlled support access rather than
      implicit platform authority. **Tracked jointly with the deployment-profile portability review
      in the Operations Triage track above, which owns the same cell from the feature side.**
- [ ] **Shared.** Preserve tenant-complete audit while separately authorizing and auditing platform
      access; shared support tooling cannot become an impersonation or bulk-content path. Tenant
      telemetry and support-access separation: cross-job aggregation and persisted statement text
      both require server-derived scope.

##### 9. Lifecycle — provisioning, backup, portability, deletion, metering

The former `Managed operations` bullet was one checkbox covering nine deliverables and could not be
checked off meaningfully. Split:

- [ ] **Dedicated — provisioning.** Automate tenant provisioning with no manual SaaS-platform
      database edits.
- [ ] **Dedicated — upgrades and capacity.** Automate upgrades, drain/fence, and capacity assignment
      for one tenant.
- [ ] **Dedicated — backup and recovery.** Tenant-scoped backup, export, restore, and key/artifact
      recovery, including proof that a restore cannot introduce another tenant's rows or resume its
      work.
- [ ] **Dedicated — support approval.** The approval workflow behind domain 8's audited platform
      access.
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
      Dedicated SaaS GA (Phase A above); add large resumable content and incremental deltas later.
      Deliberately exclude resolved secrets, private keys, capabilities, checkpoints, leases, caches,
      and in-flight work rather than making an indefensible "zero-loss" claim.

*Absorbs the retained discovery items **Usage Metering & Billing Collector** and **Full-Fidelity
Tenant Portability Bundle**.*

#### Certification and evidence

- [x] Add the topology qualifier to the capability matrix itself, so a Dedicated pass cannot render a
      Shared cell Green. Per-profile and per-transition release claims are tracked under **Platform —
      Deployment Profiles and Upgrade Certification → P2** above.

      **Completed 2026-08-12.** The normative capability matrix now has independent `Managed
      Dedicated SaaS` and `Shared SaaS` columns for every concern. A contract test requires five
      profile/topology cells and prevents their accidental collapse.
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

### Language — Dialect Standardization and Drift Prevention

The remaining deliverables below implement the portability contract in
[ROADMAP.md](ROADMAP.md#language--dialect-standardization-and-drift-prevention).

- [x] Publish a machine-readable canonical EBNF grammar for the accepted ETL-SQL language, with working
      examples for every documented syntax form and an explicit process for keeping it synchronized with
      `Parser.cs`.

      **Reopened by closed-item audit (2026-08-11).** `docs/grammar.ebnf` explicitly describes itself
      as a subset, so it cannot be the canonical grammar promised here. Expand it to every execution
      parser statement/expression form, reject unresolved references, and cross-check documented working
      examples against both the grammar and `Parser`.

      **Completed 2026-08-12.** The grammar now models expression precedence, 120 retained
      statement alternatives, core query clauses/set operations (including joins, grouping sets, and named
      windows), and 33 independently checked expression
      examples. A source-derived guard requires every active `StatementParser` entry keyword to be reachable
      from the grammar; every declared statement and every alternative in every `*_statement` rule generates
      parser-accepted input. This work restored the documented `WAITFOR (<condition>)` alias, fixed unreachable
      `<<`/`>>` lexer tokens, made `ALTER FOLDER ... RENAME` reachable, and wired the existing `REBUILD`
      token into the lexer so `REBUILD SNAPSHOT` reaches its parser dispatch. Portal lifecycle, DROP,
      WAIT-for-file, MERGE FILES, EXEC/EXECUTE, tag/lineage DML, TRY/CATCH, ASSERT JOB, export/publish,
      CTE, DML OUTPUT, security SET, and file/directory subforms are represented. The canonical grammar
      embeds the required parser/grammar/docs synchronization protocol. Its reverse-direction recognizer
      scans SQL/ETL-SQL/Report-SQL documentation fences and now fully recognizes all 939 parser-accepted
      working examples, including scripts preceded by column tags or section labels. The conformance loader
      fails closed on malformed, duplicate, unterminated, or unbalanced EBNF. Completing the reverse check
      also exposed and fixed parser reachability defects for table operators followed by `INDEXED BY` and
      the contextual `GENERATE JWT_SECRET` form. The deterministic EBNF suite passes 15/15 tests.
- [x] Expand the shared SqlLogicTests corpus under `tests/slt_data/` to cover exact results, boundary
      behavior, mathematical/date offsets, standard-library functions, and representative cross-dialect
      cases.

      **Completed 2026-08-12.** `standard_library_boundaries.test` adds deterministic exact results
      for negative and midpoint math, safe division and bit operations; leap-day, month-end, quarter,
      and date-part aliases; empty/Unicode string lengths and indexing/padding aliases; MSSQL,
      Oracle, and SQLite/Postgres-style null/string/date equivalents; failed `TRY_CAST`; regex/JSON
      missing-value behavior; and both `TOP` and `LIMIT`. All 45 eligible corpus files pass normally
      (1m26s) and with batch size 7 plus join/sort/window thresholds 10 and temp spill threshold 25
      (4m55s).
- [x] Build an EBNF-to-parser conformance runner that generates valid and invalid sequences and proves the
      execution parser accepts/rejects them consistently. Keep this in its own deterministic fuzz/release
      lane rather than slowing smoke or fast tests.

      **Reopened by closed-item audit (2026-08-11).** `EbnfConformanceTests` currently accepts any
      non-crashing parse for generated "valid" input and accepts either success or `SyntaxException` for
      mutated "invalid" input. Make acceptance/rejection assertions strict, fail on unresolved grammar
      references, minimize/report counterexamples, and add the promised deterministic release lane.

      **Completed 2026-08-12.** Fixed-seed generation now requires zero execution-parser error
      diagnostics for 50 non-empty valid scripts and requires rejection for 50 grammar-invalid
      mutations. Every rule reference must resolve; failures report the seed, generated SQL, parser
      diagnostics, and a deterministic statement-level minimal counterexample. Making the checks real
      exposed and corrected stale EBNF for report objects, dataset identities, visual sources, and
      connection parentheses. The separate `ebnf` lane runs 4/4 tests on PowerShell and Bash release
      paths, appears as a named pre-release phase, and the lane inventory reports zero ownership gaps.
      Follow-up hardening now also generates one accepted input from every declared statement alternative
      and rejects malformed grammar structure. The suite now passes 15/15 tests, including parser-entry,
      statement-subform, independent expression-family synchronization, and complete reverse recognition
      of all 939 parser-accepted working documentation examples.
### Connectors — Transactional File Staging

- [x] Define and implement the `TRANSACTIONAL=TRUE` connector contract, including parser/help/snippet
      coverage, collision-safe engine-owned staging names, canonical `ResolvePath` enforcement, and the
      connector types that can truthfully support atomic publication.
- [x] Commit completed output by atomic rename where the destination guarantees it; otherwise fail
      preflight or use a documented provider-specific commit protocol rather than claiming false
      atomicity.
- [x] On failure, cancellation, retry, or process loss, remove or reconcile staged artifacts without
      deleting a previously published target. Define checkpoint/resume and multi-output behavior
      explicitly.
- [x] Certify local files and supported remote-transfer connectors for success, mid-stream failure,
      cancellation, overwrite policy, concurrent writers, cleanup failure, path/symlink escape, and
      crash residue. Keep network-backed certification in the integration/release lanes.

      **Reopened by closed-item audit (2026-08-11).** Local file writers recognize the option, but the
      repository has no user reference/snippet contract or transactional file-writer certification for
      failure, cancellation, concurrency, cleanup, symlink escape, and crash residue; remote-transfer
      connectors are not covered. Define one shared staging/commit/reconciliation abstraction, document
      the exact supported connector set and truth table, then add focused local and integration evidence.

      **Completed 2026-08-12.** `FLATFILE`, `JSON`, `XML`, `EXCEL`, and `PARQUET` now serialize,
      compress, and encrypt into collision-safe engine stages resolved beside the authorized target,
      then publish without a delete-first window. Failure/cancellation preserves the prior target;
      exact-target residue older than 24 hours is reconciled without touching fresh or unrelated
      writers. The reference and five local snippets define retry, append, concurrency, and
      single-artifact limits. SFTP `ATOMIC_UPLOAD=ON` now requires POSIX rename, carries execution
      cancellation, reconciles residue when list permission exists, and fails without downgrading when
      the server cannot replace atomically; FTP intentionally makes no atomic claim. Focused evidence:
      transactional local certification 9/9, real Docker SFTP atomic overwrite/cancel 2/2, file
      connector regression 114/114, path/symlink fencing 2/2, docs/grammar 21/21, and full solution
      build with zero warnings.

### Extensions — Governed Custom Tool Runner

The authoritative trust, catalog, runtime, protocol, checkpoint, and certification contract remains in
[ROADMAP.md](ROADMAP.md#extensions--governed-custom-tool-runner). This is a governed escape hatch, not a
raw `CMD` connector or arbitrary shell execution.

#### P1 — Pure-transform foundation

- [ ] Define the language/AST contract for invoking an approved logical tool operation with typed
      parameters, input schema, and output schema. Scripts cannot select executables, interpreters,
      images, paths, shells, environment variables, or arbitrary argument strings.
- [ ] Implement the governed tool catalog and lifecycle (`Staged`, `Approved`, `Disabled`, `Revoked`),
      immutable artifact digest/signature verification, publisher/approver separation, tenant/environment
      ownership, grants, promotion preflight, and portable logical aliases.
- [ ] Implement the Standard direct-process binding for approved pure transforms: no shell, sanitized
      allowlisted environment, dedicated identity, canonical scratch root, process-tree containment,
      bounded CPU/memory/process/time/output limits, cancellation, and cleanup.
- [ ] Implement the versioned typed streaming protocol, beginning with JSON Lines compatibility and a
      path to a high-volume framed format. Specify handshake, schemas, null/decimal/time/binary/Unicode,
      size limits, compression, backpressure, stderr diagnostics, cancellation, and terminal outcome.
- [ ] Validate every returned value and stage output until protocol completion, schema/type/size/row
      limits, and data-quality rules all pass. Stream with bounded memory and never publish partial output.
- [ ] Add lineage, metrics, sanitized diagnostics, and audit for catalog lifecycle, policy decisions,
      execution, capability access, cancellation, denial, and publication without retaining payloads or
      secret values.

#### P2 — Hardened and side-effecting operation support

- [ ] Add OCI Hardened/Dedicated bindings with pinned images, read-only roots, non-root identity,
      capability/seccomp restrictions, isolated scratch, default-deny network, no runtime socket, and
      metadata/control-plane protections. Keep runtime binding environment-owned so scripts remain
      portable.
- [ ] Add declared file, network, Gateway-resource, and just-in-time named-secret capabilities bound to
      tenant, environment, tool digest, operation, actor, run/attempt, limits, policy version, expiry, and
      nonce. Pure transforms receive none by default.
- [ ] Persist logical checkpoints containing immutable tool/protocol/policy/input identities and only
      fully validated staged output. Replacement sandboxes reauthorize on resume; they never serialize a
      process, handle, live connection, resolved secret, or reusable capability.
- [ ] Introduce side-effecting action tools only after a durable operation ledger and explicit
      idempotency/reconciliation contract exist. Ambiguous external effects must not be retried as if
      process exit proved the outcome.

      **Reopened by closed-item audit (2026-08-11).** The current prototype is not the governed runner
      described above: `CREATE TOOL` lets scripts select `COMMAND`, `ARGS`, `WORKING_DIR`, images,
      mounts, and secrets; executable arguments use a command string; the environment is inherited;
      malformed JSON output is logged and skipped; output values are not schema/type validated; and
      catalog lifecycle, approval, signatures, resource enforcement, bounded streaming, reconciliation,
      and adversarial/runtime certification are absent. Keep the parser surface experimental while
      replacing in-session definitions with the approved catalog/binding model, then satisfy the
      ROADMAP security certification cases before closing any runner slice. The new checkpoint and
      operation-ledger primitives are foundations, not completion evidence by themselves.
- [ ] Provide tenant-admin catalog/binding/grant workflows with platform-policy revocation but no implicit
      platform data authority, plus promotion and preflight diagnostics for unavailable profile bindings.
- [ ] Retain adversarial certification evidence for injection, sandbox escape, unauthorized data/secret/
      network access, artifact substitution, protocol confusion, resource exhaustion, cancellation,
      cross-tenant isolation, checkpoint replacement, and cross-profile portability. Keep hardened,
      hostile-tool, and scale cases in targeted release lanes.

### Reporting — Paginated Print Layout & PDF Rendering

The physical-page contract is defined in
[ROADMAP.md](ROADMAP.md#reporting--paginated-print-layout--pdf-rendering); it extends the current PDF
paths and must not overload the existing `CREATE PAGE ... AS PAGINATED` meaning.

- [ ] Define `PRINT_LAYOUT`/`PAGE_LAYOUT` syntax and AST for page size, custom dimensions, orientation,
      units, margins, overflow, split/scale, page breaks, keep-together, and print-layout overrides, with
      lint/help/snippet/reference coverage.
- [ ] Compile responsive report definitions and runtime data into one renderer-neutral physical-page
      model consumed by static and browser-backed exporters instead of duplicating pagination rules.
- [ ] Implement complete table flow with repeating column/row headers, group headers/footers, group-break
      controls, parent/header orphan prevention, and explicit wide/long-table behavior without silent
      row or column truncation.
- [ ] Add true print page-header/footer regions, report metadata and parameter fields, culture/timezone,
      page number and total-page placeholders, and deterministic first/last/odd/even/empty-page behavior.
- [ ] Make the deterministic server-side renderer canonical for paginated documents while retaining the
      browser renderer for dashboard snapshots. Preserve searchable text, links, metadata, and observable
      font/chart substitution behavior.
- [ ] Add Report Builder print preview using the same page model, and define the immutable parameter,
      filter, data-snapshot, culture, timezone, and renderer state captured by interactive and unattended
      exports.
- [ ] Enforce row/page/image/byte/layout-pass/time limits, cancellation cleanup, tenant/path/network
      policy, atomic publication, deterministic retry/HA behavior, and no successful partial artifact.
- [ ] Retain Windows and Linux layout/page regression evidence covering Letter/A4, orientation, headers,
      groups, page totals, wide/long/oversized content, fonts, cancellation, and authorization. Keep
      rendered cross-platform certification in a targeted release lane.

      **Reopened by closed-item audit (2026-08-11).** The current slice adds AST/manifest fields and a
      prototype compiler that assumes every grid row is two inches; it is not data-driven pagination
      and is not the shared canonical input to both exporters. Complete the syntax validation and help,
      replace assumed geometry with measured table/visual flow, add headers/footers and immutable export
      state, enforce publication limits/cleanup, wire Builder preview to the same model, and retain
      rendered Windows/Linux regression evidence for the full matrix above.

### Reporting — Expandable Master/Detail Rows

This is prepared-data master/detail, not execution of a separately published subreport. The complete
contract and explicitly deferred reusable-subreport boundary remain in
[ROADMAP.md](ROADMAP.md#reporting--expandable-masterdetail-rows).

- [ ] Define structural `TABLE` row-detail syntax/AST with child visual or container targets, explicit
      typed parent-to-child bindings, composite/null/duplicate/missing/type behavior, defaults, nesting,
      open-row limits, and validation/cycle/dependency/lineage rules.
- [ ] Preserve raw typed binding metadata before display mapping and build a bounded child index over data
      prepared by the same report script. Expansion must not construct browser SQL or issue N+1 connector
      queries.
- [ ] Render an accessible row-header button and owned detail region with keyboard support,
      `aria-expanded`, loading/empty/error/retry/denied states, and scoped interaction context.
- [ ] Preserve expansion state by stable raw key across sorting, filtering, paging, virtualization,
      refresh, parameter changes, and data-version changes; recycled visible row indexes are never keys.
- [ ] Enforce nesting, open-row, detail-row/byte, manifest/index, cancellation, authorization, tenant, and
      malicious-value boundaries before detail reaches the browser. JavaScript filtering is not a
      security boundary.
- [ ] Define deterministic PDF/HTML/CSV/spreadsheet behavior: omit, include-all, expression-selected,
      flatten, or separate-data as supported. Paginated inclusion keeps the parent with its first child
      and cooperates with the shared print-layout/group-break contract.
- [ ] Add runtime, browser accessibility, export, security, cardinality, virtualization, refresh-race,
      composite/formatted-key, and no-N+1 performance tests. Keep browser and adversarial/scale cases in
      their targeted lanes.

      **Reopened by closed-item audit (2026-08-11).** The prototype retains binding values as strings,
      filters cloned child rows in the browser, and has one manifest-construction test. It does not yet
      provide typed/composite indexing, validation and cycle/lineage rules, stable expansion state,
      authorization/tenant and cardinality limits, accessible browser coverage, or defined export
      behavior. Build the bounded prepared-data index server-side, preserve typed raw keys, validate the
      dependency graph, then certify interactive, adversarial, performance, and export semantics.

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

### Testing — reachability and silent-pass coverage

Opened 2026-08-10 after five defects in one session shared a shape the suite cannot see. Diagnosis
first, because it is not "add more tests": **every lane varies the query and holds the data
constant.** The fuzzer runs against one table of three rows
(`ParserFuzzTests.cs:70,74`), SLT files insert two to five rows, and unit tests use inline literals.
Spill thresholds start at 10,000 rows. So the entire columnar/spill layer was **unreachable by any
lane** — the spill defect was not missed, it could not be executed.

The NULL bugs are also not NULL-*semantics* bugs; those are well covered (`null_edge_cases.test`,
`nulls.test`). They are **NULL as absence of evidence**: a wholly-NULL column has no type, and a
column the statement does not project reads as NULL. In both cases "we don't know" and "genuinely
absent" take the same branch, and it is always the benign one.

- [x] **Extend the engine-surface corpus to the rest of the surface.** Formerly uncovered:
      `EXPORT DATASET` and `EXPORT LINEAGE`, `DATASET` publication and stewardship enforcement,
      `MERGE` against a file source, `TRANSFORM`, connector-backed targets (as opposed to temp
      tables — the `BULK INSERT` type-check finding below suggests those two paths may not behave
      alike), and compression/encoding variants of the flat-file round trip. Each is a place where
      a defect currently has nothing to catch it.

      **Completed 2026-08-12.** `dataset_portal.etest` and `engine_surface.etest` cover every named
      surface, including real SQLite writes and ZIP/Unicode round trips. The harness gained Portal
      mode plus bounded file-existence/content assertions, with parser and path-containment tests.
      The coverage found and fixed a real optimized-`MERGE` defect: compatible key values with
      different runtime types (for example integer `2` and CSV string `"2"`) bypassed SQL soft
      equality and inserted a duplicate. All five corpus files pass normally and with batch size 7,
      join/sort/window thresholds 10, and temp-table spill threshold 25.

- [x] **Make the corpus batch-size-agnostic so the spill lane can become a gate.** Its first run:
      5,971 tests, 8 failures, 6 of them caused by the lane. **One of the six is a real P0
      correctness defect** — bucket-wide window values under `PARTITION BY`, recorded in Bugs —
      found within an hour of the lane existing, which is the whole argument for it. The first read
      of that run called all six threshold-coupled tests; that was wrong, and it was wrong in the
      direction of dismissing a defect, so read the failure before classifying it.

      **All six are now resolved (2026-08-10).** The lane re-run after the P0 fix reports 4
      failures out of 5,981, and two of those are the pre-existing release-branch failures
      recorded below — so the lane is green apart from a known baseline and can gate once
      that baseline is dealt with. What the other five turned out to be:

      Every one read only the **first batch** and called it the result, or asserted a plan
      that thresholds choose. None was a product defect, but none was harmless either: each
      was asserting the batch size rather than the thing it was named for.

      The rest, for the record:

      - `Scale_Aggregate_100kRows_CorrectResults` and `Scale_Join_SpillToDisk_CorrectResults`
        (`HardeningScaleTests.cs:85`) call `.FirstAsync()` and assert on the **first batch**, not the
        result. So neither has ever verified what its name claims across batch boundaries; they pass
        because the default batch size happens to exceed the result — the join one expects exactly
        10,000 rows, which *is* the default `BatchSize`. Fix by draining all batches.
      - `InteractiveOutputShouldOnlyRenderCappedRows` — same shape.
      - `JoinEngine_StreamingUnqualifiedEquality_UsesHashJoin` and
        `ExternalWindowEngine_PartitionSampleIncreasesFanOutWithoutLosingRows` assert plan and
        fan-out choices that thresholds drive. Pin their thresholds explicitly rather than
        inheriting the ambient ones.
      - `MockDataTests.TestGenerateWithSeed` — not yet traced.

      Until this is done the lane cannot be green, so do not add it to the `release` lane.

      **The lane's SLT half was inert until 2026-08-11.** Everything above concerns
      `ETL-SQL.Tests`, which resolves configuration through the production composition root and so
      genuinely ran at the lane's thresholds — that is where the P0 was found. The SLT corpus did
      not: `SltRunner` registered no `IConfiguration` and silently used built-in defaults, so the
      lane's low thresholds never applied to it.

      **First genuine low-threshold corpus run: green (2026-08-11).** All 45 files, 4 minutes,
      0 failures, at `TempTableSpillThresholdRows=25`, `MaxInMemoryBatches=2`, `BatchSize=7`,
      `JoinSpillThreshold=10`, `ExternalSortChunkSize=10`, `WindowSpillThreshold=10` — including
      `select3` (3,351 records), `select4` (3,857), and `select5` (1,436). Because SLT verifies
      actual results against recorded expected values, a green run at these thresholds *is* the
      configuration-invariance evidence: every query returns the same answer spilled as unspilled.
      So the SLT half of the lane can gate as soon as the `ETL-SQL.Tests` half is batch-size-agnostic
      and the release-branch baseline is resolved.

      **Completed 2026-08-12.** The bounded spill lane now completes the full corpus: eight fresh,
      deterministic engine hosts produced 6,058 green runtime results from 6,053 discovery rows
      (five `EtlScenarioGoldenTests` theory rows expand only at execution), with no test identity
      crossing shard boundaries. The genuine low-threshold SQL logic run passed 7/7 test wrappers
      across all 45 files. The lane stayed beneath its 8 GB managed-heap ceiling and did not crash.
- [x] **P1 — a full engine-lane run exhausted machine memory and crashed the test host (2026-08-11).**
      The run aborted with `Test host process crashed` after 4,607 of 6,109 tests, so ~1,500 never
      ran. Two separate problems, one fixed and one open:

      **Fixed — nothing bounded the test host, and orphans were not reaped.** A `testhost` was still
      holding **6.5 GB** after the run had aborted; killing it returned free RAM from 7.2 GB to
      13.7 GB on a 31 GB machine. Gates added: `System.GC.HeapHardLimit` = 12 GB via
      `tests/ETL-SQL.Tests/runtimeconfig.template.json`, which reaches the generated
      `runtimeconfig.json` and therefore applies to a plain `dotnet test`, not only the lane; plus
      `-MemoryLimitGB` (default 8, `0` to disable) on `scripts/test-lane.ps1` for lanes that shell
      out. Verified the limit is genuinely enforced — at a pathological 16 MB the host thrashes
      rather than passing, which is the proof it is not being ignored. 221 tests pass under the
      12 GB ceiling.

      **At the time — the cause of the memory growth itself was not yet identified.** Do not assume it is
      the spill-scoping change just because that was the most recent commit: the concurrent
      session's working-tree files changed between the completing run and the crashing one, so the
      two runs differ by more than one commit. Attribute it before reverting anything.

      **Then-planned next step:** re-run with `--blame-crash` under the new ceiling, which names the test that
      crashed, exactly as it did for the SLT stack overflow. The suite has no per-test progress
      breadcrumb of its own — the same instrument gap that cost days on that defect — so consider
      adding one if `--blame-crash` proves insufficient.

      **Correction-path update (2026-08-11).** A repeat spill-lane run without an isolated
      `Session:Root` drove the host to 9.4 GB while thousands of checkpoint attempts failed against
      a sandbox-denied LocalAppData path. With a clean workspace-local root, the same engine lane
      stayed between 575 and 735 MB for 15 minutes with advancing CPU and no growth curve, but the
      outer diagnostic timeout expired before the 6,000-test project completed. The lane now creates
      and removes a unique temporary session root. Split the engine inventory into deterministic
      shards (with per-shard progress/results), run every shard plus SLT under the ceiling, and only
      close this item after all shards finish and demonstrate bounded memory.

      **Completed 2026-08-12.** The spill lane now discovers the engine inventory once, assigns
      whole test classes deterministically across eight fresh hosts, filters by exact method
      identity, and retains per-shard manifests, runsettings, and TRX files. Its complete bounded
      certification produced 6,058/6,058 passing engine results and 7/7 passing SQL logic wrappers
      without a host crash. The manifest distinguishes five execution-time theory expansions from
      discovery rows and fails closed if a test identity appears in more than one shard.

## Bugs

- [x] **`BULK INSERT` maps by position and ignores the file's header, and a count mismatch is
      tolerated.** `for (i = 0; i < mapping.Count && i < batch.ColumnNames.Count; i++)` pairs the
      i-th file column with the i-th target name. Do not simply reject a count mismatch:
      `StmtBulkInsertColsTests.TestBulkInsert_SourceFewerColumnsThanMapping_ExtraColumnsAreNull`
      deliberately permits a short source and leaves unmatched targets NULL. Decide and document
      what mapping means for header-bearing inputs—prefer name mapping when every requested target
      is present, with an explicit positional option for T-SQL parity—and cover compatible-type
      transposition, surplus columns, missing columns, and forgotten `FIRSTROW`.

      **Completed 2026-08-11.** Header-bearing files map all requested targets by name;
      `MAPPING = 'POSITION'` preserves T-SQL ordinal behavior. Recognizable forgotten headers fail
      before writing, while width mismatch or header fallback emits a counted mapping warning.
      Short sources still leave unmatched targets `NULL`, and surplus columns remain ignored with a
      diagnostic. The engine corpus pins reordered, forced-positional, short, wide, blank, missing,
      and forgotten-header cases.

- [x] **`BULK INSERT` type checking does not close the silent-mapping gap that `MAXERRORS` appears
      to cover.** The temp-table path now rejects incompatible values with the target column and
      offending value, but an all-text header can still load as data, reordered compatible columns
      can transpose silently, surplus source columns are dropped, and missing columns become NULL
      without a diagnostic. Re-derive connector-target behavior, settle warning/strict semantics,
      and ensure the final row/error report reflects every rejected or ambiguous mapping.

      **Completed 2026-08-11.** Target writes retain row isolation and `MAXERRORS` enforcement;
      mapping ambiguity is now separately counted in the final completion message, recognizable
      header mistakes fail closed, and user help documents the name/position and width contracts.

- [x] **A heterogeneous column cannot be persisted, and says so far from the cause.** Three of this
      session's defects were one column holding more than one CLR type — `eng.variables.value`
      (number and string), `#GenData.price` (`'HR'` among decimals), and the spill batches. Each
      failed somewhere distant with a message naming neither the column nor the statement
      responsible: `ddl_dml_sink` completed its script and then died in *session save* with
      `The input string 'HR' was not in a correct format`.

      The individual sources are fixed. What is not: the engine has no point at which a column's
      type heterogeneity is detected and reported against the statement that introduced it. Decide
      whether such a column is (a) rejected where it is produced, (b) widened to text with a
      diagnostic, or (c) allowed but reported precisely when it reaches a typed sink. Until then
      each new instance will surface as a fresh mystery in a different subsystem.

      **Completed 2026-08-11.** Dynamic columns remain heterogeneous in memory, while Arrow spill
      now fails at its typed persistence boundary with the chunk, column, one-based source row,
      inferred sink type, and offending CLR type. The diagnostic does not echo the value, and a
      failed buffered flush still closes all writer streams. Spill resilience coverage pins the
      decimal-then-string case that previously surfaced as a distant session-save format error.

---

## Restored completion history

The 2026-08-11 audit originally removed these completed entries under an active-only ledger
interpretation. They are retained here, checked off, so the original decisions and delivery
details remain visible. Where a later corrected entry exists above, that entry is authoritative.

- **Historical wording (completed above):** Consider a CI sample lane. The gate is currently the only thing that runs samples, and it is
      Windows-and-PowerShell only.

- **Superseded completion claim (reopened above) — Portal ETL IDE Data Preview & Schema Browser**: add interactive schema inspection and bounded
      row previews of intermediate `#temp` tables and governed source connections. Cross-profile
      Studio capability: start with Solo/Team, require Enterprise connection ACLs, and certify tenant
      scope before enabling it in SaaS (SaaS domain 7).

- **Superseded completion claim (reopened above) — Portal Script Concurrent Editing Locks**: implement optimistic concurrency plus collaborative
      edit/session leases that warn authors and prevent silent overwrite. Team/Enterprise
      collaboration work; SaaS additionally requires tenant-scoped lease keys, hard expiry,
      disconnect recovery, and negative cross-tenant tests (SaaS domain 5).

- **Superseded completion claim (reopened above):** Consolidate shared headers, identity, module gating, themes, spacing, icons, status chips,
      errors, loading states, and empty states into a shared component vocabulary.
      **Two of the ten are now shared, with the rest still per-page.**

      - **Dialog behaviour** — `js/dialog-a11y.js`: focus entry, Tab containment, focus restore,
        Escape. Adopted where there was none.
      - **Adopted in the admin panels** (v0.18.0) after a sweep for surfaces that render a failure
        as an emptiness. Two were found and both were access-control surfaces: folder permissions
        left the *previous* folder's grants on screen under the new folder's name when the load
        failed, and group membership rendered a failed read as "No members". Covered by
        `AdminPanelFailureStateTests`.
      - **States and status chips** — `js/portal-states.js`: loading, denied, failed, empty, and
        `statusChip`, extracted from the governance module's pattern. Guarded by
        `PortalStateVocabularyTests`, which asserts the vocabulary is complete, each state emits a
        distinguishable marker, a denial names the roles that would grant access, a failure refuses
        to invent content, and every caller-supplied value is escaped at the point of interpolation.
        Adopted in `connections-admin.js`, which previously rendered one message for both a 403 and
        an unreachable service — telling the reader the wrong thing half the time.

      - **Module gating** — `GET /api/portal/navigation` plus `js/portal-nav.js`. The server
        computes which top-level entry points to offer a caller; the shell applies the answer and
        never derives one. This found two live defects rather than just duplication, both of the
        "offers what it cannot deliver" class: **Studio was offered to every signed-in user**
        (pages revealed it whenever the capability *probe* succeeded, and that probe had been
        deliberately opened to everyone, so the roles holding no Studio capability saw a link to a
        403), and the **Docs link was offered where `/docs.html` 404s**, because whether the
        Documentation module is enabled is a server fact no token claim carries. A third copy of
        the rule, in `docs.html`, gated Orchestrator on a role name that does not exist.

      A recount while doing this: **identity, themes, spacing and icons were already shared** —
      `session-identity.js` on every page, `branding.js` owning the theme toggle and storage key,
      and the radius/shadow/colour tokens plus the `sidebar-nav-icon-*` set in `portal.css`. The
      TODO listed them as outstanding; they were not.

      Still per-page: **headers**. The `<header class="topbar">` block is copy-pasted across six
      pages. Its *gating* is now shared and guarded by `PortalNavigationVocabularyTests`, which is
      where the drift actually was; templating the markup itself would move it out of static HTML
      for a smaller benefit. The three inline focus traps in `index.html`, `admin.html` and
      `orchestrator.html` also still duplicate `dialog-a11y.js` (as does the drawer's own trap in
      `branding.js`); they work, so replacing them needs per-page browser coverage of their dialogs
      first rather than a blind swap.

- [x] Extend `QuarantineReplayManifest` with nullable target connection alias, connector type, and
      catalog-backed provenance written at capture time. Existing manifests without provenance remain
      view-only.

- [x] Decide and document preview authority: require the steward's ordinary connection permission, or
      make `DataQualityStewardAccess` plus the manifest-bound target sufficient. Then make
      `QuarantineTargetReadability` resolve enabled catalog entries using the caller's verified identity.

- [x] Bootstrap the bounded preview session from the manifest's `SHARED:` alias, preserving the 15-second
      timeout, row cap, RLS identity, connector policy, secret resolution, and redacted errors. Gate the
      capability behind `Portal:DataQuality:AllowConnectionPreview`, default off.

- [x] Audit every raw quarantine preview and add positive and negative coverage for readable targets,
      catalog misses, disabled entries, switch-off, unauthorized callers, request tampering, and error
      redaction.

- [x] Document the administration and audit behavior, and extend the data-quality UI sandbox story so
      readable catalog-backed and view-only targets remain independently testable.

- [x] Add an explicit `ON FAILURE QUARANTINE TO <table> WITH (HANDLING = SCRIPT)` mode for rows the
      script will remediate, reroute, or discard during the current run. It must still remove failed
      rows from the main output, expose the captured row and `__dq_*` context to later statements, and
      record counts-only quality metrics, but it must not persist a replay manifest or publish a Portal
      steward-queue item. Do not require a replay section label or emit the durable-target linter
      diagnostic for this mode; retain the current steward-managed behavior when `HANDLING` is omitted
      and support explicit `HANDLING = STEWARD`. Update parser/AST/formatter, lint and autocomplete,
      runtime tests, reference documentation, and samples together.

      **Completed (2026-08-10).** The manifest is the single control point: it is what makes a
      target visible to the Portal queue *and* what `REPLAY QUARANTINE` resolves, so not writing
      one keeps the mode out of both without a second switch to keep in sync. The section-label and
      durable-target requirements are skipped for the same reason they exist — both serve
      remediation after the run, and `SCRIPT` says there is no after. Sample:
      `samples/05_Security_Diagnostics/Data_Quality_Script_Handled.etlsql`.

      **Found while doing it: the formatter dropped every `ON FAILURE` clause.** `ToSql()` on a
      quarantining SELECT returned a script whose `@fail: 'QUARANTINE'` tags routed nowhere, which
      is a hard error on the next run. It is the mirror image of the comment-stripping failure the
      symmetric clause/rule check was written to catch, and it was equally silent where it
      happened. Fixed with a round-trip test over all three clause forms.

- [x] **`BULK INSERT` from a missing file silently loaded zero rows and reported success.**
      Found and fixed 2026-08-11 (`bd8a107d`) by the first engine-corpus file. The flat-file reader
      yields no batches for a path that does not exist, so a typo or an absent daily drop produced
      an empty table and a green run — an empty result is harder to notice downstream than an
      error, and indistinguishable from a file that was legitimately empty. `BulkInsertStatementHandler`
      now checks existence after path authorization and throws `ExecutionException`; T-SQL refuses
      this case too.

      **Worth remembering how it survived:** `TestBulkInsertMissingFile` *asserted* the silent
      success, and its own comment showed it was written to whatever the code did rather than to a
      decision — *"Should not throw, but RowsProcessed should be 0 (or it might throw if engine
      enforces existence)"*. A test written by observing behaviour converts a defect into a
      specification. When a test's comment expresses uncertainty about intent, that is the signal.

      **Not yet checked:** whether `SELECT` from a missing flat file is silent in the same way. The
      `yield break` is in the shared read path, so it probably is; only the `BULK INSERT` boundary
      was fixed.

- [x] **DECIDED 2026-08-11 (owner's call) — IMPLEMENTED for `BULK INSERT` the same day.**
      `BulkInsertStatementHandler` now pairs by header name when a header is present and every
      target column is named by it, falling back to positional **with a warning** when the header
      does not describe the table (a silent fallback would be the same defect wearing a different
      hat). `MAPPING = 'POSITION'` forces T-SQL ordinal pairing for ported scripts. Blank, empty and
      whitespace-only fields become NULL regardless of target type. `bulk_insert.etest` pins all of
      it, including the reordered-file case that previously loaded transposed without error.

      **Read side done the same day.** `FlatFileDataSource.CreateRow` now reads a blank, empty or
      whitespace-only field as NULL, on top of any `NULL_AS` sentinels. NULL therefore survives a
      flat-file round trip: written as an empty field, read back as NULL.

      **The cost of the convention, stated plainly because it is real:** a genuine empty string
      cannot be distinguished from NULL through a flat file, so `''` does not round trip. CSV has no
      native NULL and something had to give; `NULL_AS` remains available for anyone who needs to
      tell them apart. Both directions are now consistent, which is the property that matters — the
      previous state had the load and the read disagreeing.
      1. **Header mapping is by name.** When the source file has a header, map its columns to the
         target by name rather than by position.
      2. **Blank or empty is NULL**, on both the load and the read-back side.

      **Divergence to flag before implementing — T-SQL does *not* do (1).** SQL Server's
      `BULK INSERT` maps strictly by **ordinal position**; the header row is data to be skipped with
      `FIRSTROW = 2` and its names are never consulted. Name-based mapping in SQL Server requires a
      **format file** (or `OPENROWSET(BULK ...)` with one). So decision (1) is a deliberate
      divergence, not a compatibility fix. That is defensible — positional mapping is exactly what
      makes a transposed file load silently, which is the defect that prompted this — but it must be
      a **documented, opt-out-able** divergence rather than a surprise, because a script ported from
      SQL Server would change meaning without changing text. Recommended shape: map by name when a
      header is present and every target column is matched; fall back to positional with a warning
      otherwise; provide an explicit option to force positional for T-SQL parity.

      For (2), T-SQL is closer to the decision: `KEEPNULLS` makes empty fields import as NULL
      instead of the column's default, so NULL-for-empty is a supported T-SQL behaviour — it is just
      opt-in there and would be the default here.

      **Checked against Microsoft Learn 2026-08-11 — both claims hold, with one useful nuance:**
      - T-SQL `BULK INSERT` maps **implicitly by ordinal position**; name-based mapping requires a
        **format file** (non-XML or XML), also used by `bcp` and `OPENROWSET(BULK ...)`. Confirmed.
      - **But name-based mapping is an established Microsoft pattern one layer up:**
        `SqlBulkCopyColumnMapping` lets you map source to target *by name or by ordinal*, and when
        the mapping collection is empty it falls back to ordinal. So decision (1) matches the
        **.NET bulk-copy API**, not the T-SQL statement. That is a much better precedent to cite
        than "we diverge from SQL Server": we are choosing the API-layer convention over the
        statement-layer one, which is defensible in a tool where the file usually *has* a header.
      - `KEEPNULLS` specifies that empty fields "retain a null value ... instead of having any
        default values for the columns inserted" — so decision (2) is exactly T-SQL's `KEEPNULLS`
        behaviour, promoted from opt-in to default. Note it governs **default-vs-NULL**, not
        conversion: our current hard error on an empty numeric field is *stricter* than SQL Server
        with or without `KEEPNULLS`, so this change moves toward parity rather than away.

      Suggested wording for the divergence note in user docs: mapping follows the header when one is
      present (as `SqlBulkCopy` does), with an option to force positional mapping for scripts ported
      from T-SQL.

      Then: update `tests/engine_corpus/bulk_insert.etest` (which currently pins the *old*
      behaviour — empty text field becomes `''`, empty numeric field is a hard error) and
      `flatfile_roundtrip.etest` (NULL currently returns as `''`), and reconcile with
      `TestBulkInsert_EmptyFieldsInSource_MappedAsNull`.

      Sources: [BULK INSERT (T-SQL)](https://learn.microsoft.com/en-us/sql/t-sql/statements/bulk-insert-transact-sql?view=sql-server-ver17),
      [Keep Nulls or Use Default Values During Bulk Import](https://learn.microsoft.com/en-us/sql/relational-databases/import-export/keep-nulls-or-use-default-values-during-bulk-import-sql-server?view=sql-server-ver17),
      [Map Table Columns to Data-File Fields with a Format File](https://learn.microsoft.com/en-us/sql/relational-databases/import-export/use-a-format-file-to-map-table-columns-to-data-file-fields-sql-server?view=sql-server-ver16),
      [SqlBulkCopyColumnMapping](https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlbulkcopycolumnmapping?view=sqlclient-dotnet-standard-5.2).

- [x] **NULL does not survive a flat-file round trip.** **Fixed 2026-08-11** — see the decision item
      above; a blank field now reads as NULL, so write-then-read is identity for nullable columns.
      Original finding retained below for the record. Writing a table with a NULL to CSV and
      reading it back yields an empty string, so `IS NULL` finds nothing afterwards — verified in
      `tests/engine_corpus/flatfile_roundtrip.etest`. Everything else round trips intact, including
      values that merely look numeric (`00123`, `1e5`), decimals, and values containing the
      delimiter. CSV has no native NULL, so this needs a convention rather than a bug fix: a
      `NULL_STRING` option on the connector, or treating quoted-empty as `""` against bare-empty as
      NULL. Until one exists, staging through a flat file silently collapses NULL and empty, and a
      pipeline that does so is lossy in a way nothing reports.

      Pairs with the `BULK INSERT` empty-field item below — the same distinction, drawn
      inconsistently at each end. Worth settling once, for both directions, rather than twice.

- [x] **An empty numeric field fails the whole load.** **Fixed 2026-08-11** by the blank-is-NULL
      decision — an empty field is now NULL for every target type, so the load succeeds and the
      column is NULL rather than the statement failing. Original finding retained below.
      `1,,Engineering` into `(id INT, score INT,
      dept VARCHAR)` raises `cannot be converted to target type INT`; the same bytes into a text
      column produce an empty string, which is not NULL and so `IS NULL` does not find it. Loud is
      the safe direction and this is not a silent-wrong-answer defect, but an empty numeric field is
      ordinary in real CSV extracts and most ETL tools read it as NULL. Decide it rather than
      inherit it; whichever way, the two cases should be explainable together, because the file
      bytes are identical and only the target type differs. Both are pinned in
      `tests/engine_corpus/bulk_insert.etest`.

- [x] **A heterogeneous column cannot be persisted, and says so far from the cause.** Three of this
      session's defects were one column holding more than one CLR type — `eng.variables.value`
      (number and string), `#GenData.price` (`'HR'` among decimals), and the spill batches. Each
      failed somewhere distant with a message naming neither the column nor the statement
      responsible: `ddl_dml_sink` completed its script and then died in *session save* with
      `The input string 'HR' was not in a correct format`.

      The individual sources are fixed. What is not: the engine has no point at which a column's
      type heterogeneity is detected and reported against the statement that introduced it. Decide
      whether such a column is (a) rejected where it is produced, (b) widened to text with a
      diagnostic, or (c) allowed but reported precisely when it reaches a typed sink. Until then
      each new instance will surface as a fresh mystery in a different subsystem.

- [x] **`BULK INSERT` does not check values against the target column, and `MAXERRORS` implies it
      does.** Loading `ID,Name,Dept,Salary,JoinDate` into `(id, category, price, qty)` paired the
      columns positionally and put `'HR'` into a decimal column, reporting `165 rows loaded. 0 errors
      skipped` — with `MAXERRORS = 0`. A load that silently accepts a value the column cannot hold
      is worse than one that fails, because `MAXERRORS = 0` reads as an assurance that it did not.
      Decide whether a column-count mismatch between file and target list is itself an error.

      **Partly corrected 2026-08-11 by `tests/engine_corpus/bulk_insert.etest`.** The load *does*
      check values against the target type, and names both the column and the offending value:
      `Column 'id' (value 'ID') cannot be converted to target type INT`. So the headline above is
      too broad. What survives is narrower and still real — **type checking is the only guard, so a
      mistake is caught only where the types happen to disagree**:
      - A forgotten `FIRSTROW` against an all-text target loads the header as a data row, silently.
      - Positional mapping into a target whose column *order* differs loads transposed, silently,
        whenever the types are compatible.
      - Surplus source columns are dropped and missing ones become NULL, both without comment.

      Since the sample's decimal column would have rejected `'HR'`, the `165 rows loaded` report
      needs re-deriving before that specific claim is trusted — it may have taken a different path
      (a connector target rather than a temp table) than the one now covered.

      Still to decide: whether a column-count mismatch is an error, a warning, or opt-in strict. The
      count is not really the defect — equal counts in the wrong order corrupt just as quietly — so
      the decision worth making is whether a header-bearing file should map by name.

      First triage cluster completed: `Batch_Processing` exposed missing native spill support for
      UUID columns; `Docker_Aliases` mixed a misspelled stop target with resume semantics; and
      `Data_Quality_Rules` is deliberately fail-closed, so the sample runners now require its exact
      expected exit code and assertion message. Validator session/outbox state is isolated from the
      user's machine state so an interrupted run cannot manufacture unrelated startup failures.

      **Second triage cluster (2026-08-10).**

      *Portal dataset deck — five samples, two different causes.* `samples/08_Reporting/datasets/`
      is a portal deployment deck, and `sample-guide.md` already said so ("requires portal
      execution … local tests parser-check all five scripts"), but nothing told the sample runner.
      `02`–`05` now carry `-- @requires: portal`, the mechanism the runner already had; parser
      coverage stays in `DatasetExampleDeckTests`. `01` was a **real defect**: a PUBLIC dataset must
      carry complete stewardship metadata, and that is enforced at runtime as well as in lint, so
      the script would have failed *in the portal too*, not merely locally. It now carries the tags
      and passes standalone, so it keeps its CLI coverage rather than being skipped.

      *Columnar spill type instability — one engine defect behind three samples.* `flatfile_sink`,
      `window_sink` and `ddl_dml_sink` all died with
      `Column batch field N ('X', utf8) does not match spill field 'X' (timestamp|decimal128)`.

      - [x] **Fixed (2026-08-10).** `SpillStore` locks its Arrow schema on the first batch and
            rejects any later batch that disagrees, while `ColumnBatchAdapter` inferred each batch
            independently from its own values. Both spilling paths now establish the logical schema
            once for the whole relation. `flatfile_sink` and `window_sink` pass.

            **The first diagnosis written here was wrong, and the correction is the useful part.**
            It said the cause was a column being *all-NULL in a batch*, leaving no type evidence and
            defaulting to VARCHAR. That is a real way to break the invariant, but it is not what
            these samples hit — and a fix scoped precisely to it did not fix them. The actual cause
            is broader: **engine rows are dynamically typed**, so the same column arrives as a
            `DateTime` in one batch and as a formatted string in the next. Only the unscoped
            carry-forward fixes that. The lesson is the one already recorded for v0.17.0 — a red
            test beat a careful read, and the narrow fix was disproved by running the samples rather
            than by more reasoning.

            `ddl_dml_sink` is *not* fixed by this: it holds genuinely non-coercible mixed types, so
            forcing the established type raises a conversion error instead. Tracked above.

      *The remaining engine defects, all found by a sample rather than by a test.*

      - [x] **`TimeSpan` had no case in the columnar spill writer** (`golden_workflow.rptsql`).
            Stored as round-trip text rather than an Arrow time type, since a TimeSpan may be
            negative or exceed 24 hours. The row-based path also labelled spans `Json`, so they came
            back as bare strings. The test added enumerates the logical-to-physical type map, since
            this and the earlier UUID gap both arose from a type present on one side and absent from
            the other.
      - [x] **`eng.variables` emitted raw CLR values** (`diagnostics_ssh_sink`), so its `value`
            column held a number in one row and a string in the next and could not be materialized
            columnar. The column is documented as text and already carried `*******` when masked, so
            it is rendered consistently now; `data_type` still reports the original type.
      - [x] **`--silent` discarded the reason for every lint failure** (`backup_and_report`).
            `ILogger.WriteLine` derives its level from the console colour, so the red header survived
            and the yellow diagnostics did not: a non-zero exit with no explanation. This also meant
            the sample gate's `@expected-error` check **could never verify a lint failure at all** —
            it matches on output text and there was none. The mechanism existed and silently did not
            work for the largest class of sample failure, which is the shape this ledger keeps
            recording.

      Two idempotency failures found by the second pass are already fixed:
      `Sqlite_Operations.etlsql` (fixed primary keys into a persistent database) and
      `register_schedule.etlsql` (`CREATE SCHEDULE`/`CREATE JOB` into the persistent Orchestrator
      store — succeeds once, fails every time after). Both now start from a known state. Expect more
      of this shape: **any sample that writes to a store outside its own session has to be
      idempotent**, and until now nothing checked.

- [x] *Slice 3 — `etl-sql admin tenant export|validate|preflight|import`* (§16).
        **Partly done (v0.18.0).** `TenantPortabilityInspector` implements the non-mutating half:
        preflight resolves required bindings against what the target says it can supply, and returns
        distinct exit codes per failure kind, following the admin identity CLI precedent — a runbook
        must tell "this bundle is not authentic" (`SignatureUnverified`) from "this bundle needs
        bindings you have not supplied" (`BindingsRequired`), because the first is a stop and the
        second is a to-do list. Six tests, including case-insensitive binding satisfaction and a
        tampered bundle reporting as invalid rather than as missing bindings.

        **`import` now exists (v0.18.0).** Decided first, then built: the Portal half is applied by
        the **engine executing the bundle's declarative script** through a Portal connection — the
        same path an operator uses by hand today — rather than by adding a mutating Portal endpoint.
        Collision *detection* stays the Portal's existing `Create`/`Collision`/`Match`; the import
        verb carries the *policy*, keeping detection and policy separate.

        Two rules shape `TenantBundleImporter`, and both are asserted: nothing mutates until
        preflight passes, so an inauthentic, tampered, or under-bound bundle cannot half-apply; and
        Orchestrator objects always arrive **disabled**, which is not configurable, because an import
        that starts running the tenant's pipelines against a freshly bound environment is the failure
        this guards. Encrypted bundles verify the decrypted plaintext against the hash recorded at
        export, which is the half of integrity that the stored-bytes hash cannot cover.

        **§5.2's `skip` and `rename` are not available on this path, and that is a consequence of
        the apply decision rather than an omission.** The script executes as a whole, so there is no
        seam at which one colliding object could be skipped while the rest proceeds. Only `fail`
        (default) and `proceed` can keep their promise here. Per-object policy needs the server-side
        import endpoint that was considered and not chosen; revisit together if real migrations show
        the need.

        **CLI: `validate` and `preflight` shipped (v0.18.0).** `etl-sql admin tenant validate|preflight`
        are registered, routed, and covered by six tests plus a manual run confirming distinct exit
        codes reach the process (`4` invalid, `7` not found). These are the customer-side verbs and
        the ones that matter most: someone handed a bundle can check it with the shipped binary and a
        published key, with no account on the deployment that produced it. `validate` states plainly
        when it checked integrity but *not* authenticity, so a green result is never mistaken for a
        verified signature.

        **Completed (2026-08-10).** `export` now composes the reviewed Portal configuration,
        optional Orchestrator promotion package, and portable source artifacts, then signs the
        manifest; SaaS exports require tenant-recipient encryption. Its service token has a new
        read-only `admin.portability` allowlist containing only the plan and hash-acknowledged export
        routes. `import` resolves explicit `SOURCE=TARGET` bindings, plans every Portal statement,
        rejects non-Portal execution blocks, refuses collisions by default, supports a non-mutating
        dry run, replays the declarative bootstrap through the engine under an environment/`SECRET:`
        interactive administrator credential, and imports Orchestrator workloads disabled. CLI,
        adapter, bundle, and Portal-scope tests cover the path; the generated CLI reference and
        security/portability documentation record the credential contract.

- [x] Define the tenant-bundle signing-key rotation policy and published-key distribution process.
      Completed 2026-08-10: the operator publishes an HTTPS OpenPGP keyring, immutable
      per-fingerprint keys, and a lifecycle index authenticated on first use through an independent
      channel. The runbook fixes routine prepublication and rollback windows, archival public-key
      retention, private-key destruction, emergency revocation/re-export, customer offline custody,
      and the rule that a manifest timestamp cannot rescue a signature made by a compromised key.

- [x] **Dedicated.** Adopt server-derived tenant context on every surface that spans deployments,
      even where the tenant has its own deployment boundary. A deployment per tenant makes
      cross-tenant reach unlikely, not impossible: the
      provisioning control plane, platform automation, and support tooling all still span tenants,
      and each is an entry point that can be handed a caller-supplied identifier.

      **Contract shipped (v0.18.0).** The gap was real and larger than "a missing bullet": there was
      **no tenant context type in the codebase at all**. Host-fixed isolation worked because tenant
      identity was implicit in *which process you were talking to*, so nothing existed to derive
      context *from* on a surface that spans tenants.

      `ETL_SQL.Core.Multitenancy` now has `TenantId` (a validated value type, not a bare string, so
      server-derived and caller-supplied are distinguishable at every call site) and `TenantContext`.
      Every construction path names a server-owned origin — `HostFixed`, `VerifiedCredential`,
      `PlatformAuthorization` — and there is **no public constructor and no parse-from-request
      factory**, so "the caller told us which tenant" is not expressible rather than merely
      discouraged. A test asserts that stays true.

      Three behaviours worth knowing: platform-scoped access must name the authorization that
      permitted it, because unattributed platform access is the impersonation path this boundary
      exists to stop; `RequireOwned` *checks* a caller-supplied identifier against the context rather
      than parsing a tenant out of it, so a caller can still name a resource it owns without being
      able to select the tenant; and `ScopeKey` makes equal names and equal numeric ids in different
      tenants land on different keys, which is what the shared-store domains will depend on.

      21 tests, including the prefix trap — `acme-evil/run-1` starts with `acme` and must still be
      refused. `SaasTenantOnboardingService`'s duplicate tenant-id regex was replaced by `TenantId`
      so there is one definition rather than two that can drift.

      **Adoption completed (2026-08-10).** The only mutating cross-deployment tenant selector,
      `admin promotion saas-onboard`, now derives a short-lived attributed `PlatformAccessGrant`
      from current signed organization policy. Its `--tenant` value is a mismatch assertion only;
      missing, expired, or mismatched authority fails before staging, and expiry is rechecked before
      the final move. Managed Dedicated Portal configuration supplies the host-fixed identity for
      portability export plans, binds it into the acknowledged plan hash, and refuses a caller
      relabel. Online support evidence derives the same host-fixed context and exposes no tenant
      selector; fleet visibility remains server-configured and read-only. The topology-specific
      release review links onboarding, export, support, and collision-negative evidence and keeps
      Shared explicitly `NotCertified`.

- [x] **Dedicated.** Establish platform/tenant identity separation and delegated tenant
      administration, and prove platform administration is separately audited and cannot implicitly
      impersonate a tenant user even when the tenant has its own deployment boundary. Supports one
      tenant-owned IdP configuration through the Enterprise identity contract.

      **Separation contract shipped (v0.18.0); adoption and delegated administration are not.**
      Checked first: the product has exactly one `Admin` role, which in a host-fixed deployment *is*
      the tenant's own administrator. There was no platform principal at all, so "platform
      administration is separately audited" had nothing to audit.

      `PlatformAccessGrant` introduces that principal, with authority over **no** tenant by default.
      A grant names the operator, the authorization it hangs off, a reason, and an expiry; all four
      are required, because an unattributed, unexplained, or open-ended grant is what turns platform
      operation into standing access to customer data. `TenantContext.FromPlatformGrant` takes the
      grant rather than a reference string so **expiry is checked at the point of use, not of issue**
      — a grant valid when written and stale when acted on must not produce a usable context.

      The impersonation property is structural rather than enforced: there is no factory that takes a
      platform principal and yields a tenant-user origin, so "act as this tenant's user" is
      unrepresentable. A test pins the complete set of public factories so a fourth one cannot be
      added quietly. Platform scope and a tenant's own user are distinguishable on the resulting
      context (`Origin`, `Grant`), which is what lets an audit record tell a support operator from
      the customer.

      **Adoption completed (2026-08-10).** Managed Dedicated now keeps the two identities separate
      by construction. A tenant `Admin` exists only inside its host-fixed Portal and owns user,
      group, provider mapping, and service-account administration. Tenant automation is limited to
      the explicit `admin.identity` allowlist; it cannot promote an Admin or reach unrelated Admin
      routes. The platform operator remains an expiring signed `PlatformAccessGrant`, receives no
      Portal role or tenant JWT, and onboarding writes a distinct `PlatformOperator` audit receipt
      naming the tenant, actor, approval, reason, and expiry with tenant-user impersonation false.
      Onboarding can bootstrap one tenant-owned credential-free HTTPS OIDC authority/client through
      the Enterprise identity contract; its client secret is never accepted or persisted and is
      injected into the tenant process before activation. The topology-specific release review
      links the delegated-admin, OIDC, platform-audit, and non-impersonation evidence. Shared follows
      the same separation rule through the independently certified tenant-qualified lifecycle below.

- [x] **Shared.** Extend identity and delegated administration to shared stores with tenant
      predicates/partitioning enforced below controller code. Add dynamic, server-verified
      tenant/issuer/domain discovery without trusting a caller-selected tenant or issuer, and
      without allowing platform administrators to impersonate tenant users.

      **Credential boundary started (2026-08-10).** Internal Portal user/service tokens can now be
      tenant-bound only from a host-fixed or verified-credential `TenantContext`; a
      `PlatformAccessGrant` is structurally refused when minting either session type. Shared request
      scope is restored solely from the already validated signed claim, never a request selector.
      Existing OIDC, refresh-session, delegated-admin, policy, and connection suites remain green.
      A shared authority registry now adds globally collision-safe normalized Portal-host/login-domain
      routing with tenant-scoped administration, `SECRET:`-only client credentials, exact enabled-host
      anonymous lookup, and post-validation issuer binding. Its discovery API accepts an `HttpRequest`,
      not a tenant, issuer, authority id, or caller domain selector. The authorization-code controller
      and user/group persistence now consume this binding. Later slices preserve the same binding
      through credential rotation and delegated administration.

      **Protected flow binding added (2026-08-10).** A ten-minute Data Protection envelope now pins
      the server-routed authority id/version, Portal host, HTTPS redirect URI, state, nonce, and PKCE
      verifier across the browser round trip. Callback restoration takes no request object and fails
      on tampering, expiry, state mismatch, authority rotation, or disablement, so callback Host and
      query selectors cannot re-route an in-flight login. The controller now consumes this path after
      tenant-partitioned subject/group and refresh-token persistence removed its global fallback.

      **Identity persistence partitioned (2026-08-10).** Portal users, groups, memberships, service
      accounts, and refresh tokens now carry `TenantId`; federated users also retain their normalized
      issuer. Composite SQLite/PostgreSQL indexes allow equal usernames, issuer/subject pairs, group
      names, and service-account names in different tenants while legacy rows backfill to
      `portal-host`. A verified-context store proves tenant-scoped lookups, group enumeration,
      membership writes, and refresh-session attachment, including refusal of foreign numeric IDs.
      The following slices apply these predicates end to end through OIDC provisioning,
      refresh/service-token validation, and delegated administration.

      **Shared OIDC provisioning completed (2026-08-10).** Anonymous provider discovery and login
      now select an exact enabled authority from the routed Portal host. The protected flow pins that
      authority through callback; discovery metadata, client id, optional tenant-scoped `SECRET:`
      credential, token issuer, and audience all come from the binding rather than callback input.
      Only after cryptographic token validation and exact issuer comparison does the request receive
      a verified tenant context. Provisioning then tenant-qualifies subject/name lookup, user creation,
      profile updates, group reconciliation, JWT tenant claims, and refresh-session creation. An HTTP
      integration test proves two routed tenants can provision equal usernames and subjects into
      separate users, memberships, tokens, and issuer bindings, while an unknown host cannot start
      login. Credential lifecycle and delegated administration are completed below.

      **Credential lifecycle partitioned (2026-08-10).** Refresh-token possession may identify its
      persisted partition, but rotation now proceeds only when refresh row and user carry the same
      tenant; every compare-and-consume predicate, successor row, and replacement JWT preserves that
      binding. Service clients establish tenant context only after constant-work secret verification
      and an account/owner tenant match. JWT validation then rechecks user, owner, and service-account
      state with tenant-qualified queries and cache keys, while session invalidation scopes user,
      group, and refresh revocation to the verified tenant. HTTP evidence covers successful isolated
      rotation for two tenants plus refusal of altered refresh rows, service ownership, and otherwise
      valid user/service JWTs carrying another tenant.

      **Delegated identity administration completed (2026-08-10).** Shared Admin routes now derive
      their partition only from the verified request context and tenant-qualify user, group,
      membership/provider-mapping, session, service-account, authority, and identity-diagnostics
      operations. Equal usernames and resource names remain valid in separate tenants; foreign
      numeric identifiers return not-found and cannot become write selectors. Authority changes are
      tenant scoped, while anonymous discovery remains exact-host and server routed. Cross-tenant
      HTTP evidence covers enumeration, mutation, membership creation, session/service-account
      visibility, authority disablement, and equal-name creation. Platform grants still cannot mint
      tenant user or service credentials, so completion does not introduce impersonation.

*Absorbs the retained discovery item **SaaS Multi-Tenant Identity (Multi-IdP)**.*

- [x] **Enterprise contract first.** Establish one provider-neutral key contract and refactor
      `DatasetAtRestKeyValidator.cs`, dataset, credential, artifact, and checkpoint encryption away
      from a single global master key. Resolved keys never enter portable exports or execution images.

      **Contract foundation started (2026-08-10).** `ETL_SQL.Core.Security.IKeyMaterialProvider`
      now resolves a server-derived `(scope, purpose, version)` request into a disposable key lease
      with separately serializable, non-secret provider metadata. Dataset, credential, artifact, and
      checkpoint are distinct purposes; equal version names cannot collide across purpose or tenant
      scope. Resolved bytes are JSON-ignored, absent from string form, copied at the provider edge,
      and zeroed when the lease is disposed. `DatasetAtRestKeyValidator` now has a provider-neutral
      Enterprise path that resolves all four purposes, rejects missing bindings and cross-purpose key
      reuse, and returns only safe descriptors. Snapshot artifacts now resolve only the `Artifact`
      purpose from the host-fixed scope, authenticate the provider version in their envelope, read
      explicitly retained previous versions during rotation, and refuse a dataset-purpose binding
      even when its raw bytes happen to match. Portal-managed credentials now write a versioned
      `Credential`-purpose envelope, retain read compatibility for existing Data Protection rows,
      and refuse a dataset-purpose binding with identical bytes. Provider-backed checkpoint stores
      now encrypt variables, variable metadata, lineage, temp-table schemas/chunk references,
      connection state, and Docker state under the `Checkpoint` purpose while retaining legacy
      reads. Dataset creation, refresh, consumption, preview/export, and rotation now resolve current
      or recorded versions just in time from the `Dataset` purpose; registry metadata carries only
      the version and never a resolved key. The Portal environment adapter binds all four purposes
      to host-local base64 environment variables, validates them at startup, and flows the host-fixed
      scope into report execution and checkpoint factories. Portable-bundle and adapter tests prove
      resolved material is absent while non-secret binding metadata remains exportable.

- [x] **Dedicated.** Tenant-specific policy authority with platform/tenant separation, so one
      tenant's policy cannot be authored or overridden from platform scope. Disjoint tenant
      provider/key namespaces plus export proof: no cross-tenant key reuse, raw secret export, or
      provider credential in an execution artifact.

      **Completed (2026-08-10).** A host-fixed `TenantContext` now scopes every policy-authority
      service operation below the controller, while admin and machine-distribution routes enforce the
      same tenant on policy versions, machine registration/list/revocation, and envelope retrieval.
      Platform-scoped principals cannot mutate tenant policy. Provisioning validation rejects key
      material reused across tenant or purpose namespaces. Dedicated HTTP, stale foreign-row,
      portable-bundle, and execution-artifact tests prove cross-tenant policy/key access, raw key
      export, and provider-binding disclosure are refused.

- [x] **Shared.** Extend policy, connections, secrets, keys, and catalog bindings to shared stores
      with tenant predicates/partitioning enforced below controller code, and prove tenant, key, and
      key-version separation.

      **Store foundation started (2026-08-10).** Policy versions, Portal secrets, shared connections,
      connection ACLs, and usage rows now have store-level tenant predicates; secret-name and alias
      uniqueness is composite by tenant in matching SQLite/PostgreSQL migrations. Equal-name
      collision tests prove tenant-scoped policy, secret lifecycle, connection export/delete, and
      distinct credential-key versions. `Portal:SharedTenancy:Enabled` fails closed when no verified
      `TenantContext` is injected.

      **Host key namespaces partitioned (2026-08-10).** Shared `Portal:KeyManagement` bindings now
      require an explicit validated server-configured `Scope`; the environment provider indexes
      equal key versions independently by tenant and purpose. Dedicated/standalone hosts still
      derive scope from host identity and reject a conflicting binding scope. Startup validation
      enumerates every configured Shared tenant and requires the complete Dataset, Credential,
      Artifact, and Checkpoint purpose set for each, rather than validating only `portal-host`.

      **Dataset catalog and key scope partitioned (2026-08-10).** Dataset rows now carry `TenantId`
      with composite `(TenantId, Name)` uniqueness in SQLite and PostgreSQL; legacy rows backfill to
      `portal-host`. A verified-context `DatasetTenantScope` owns catalog queries below controllers.
      Registration, lookup/list/delete, preview/export reads, key posture/rotation, ACL group checks,
      report dependency/structure, lineage impact, configuration export, and access simulation use
      that partition. Equal names coexist, foreign numeric IDs resolve as not-found, and provider
      requests use the verified tenant's Dataset key namespace.

      **Completed (2026-08-10).** Snapshot package reads and writes now require the verified tenant
      scope on Shared hosts and resolve only that tenant's `Artifact` key; missing scope, a
      host-wide fallback, and another tenant's equal-version key all fail closed. Interactive and
      background report execution carry the server-derived tenant into dataset and checkpoint key
      resolution. Shared checkpoint factories require an explicit scope, and Portal/Orchestrator
      execution and resume paths pass it through rather than selecting `portal-host`. Legacy
      plaintext snapshot migration is refused on Shared hosts because those artifacts do not carry
      certifiable ownership. Cross-tenant Artifact and Checkpoint encryption tests pin the boundary.

*Absorbs the retained discovery item **Tenant-Scoped Encryption Keys (BYOK)**.*

- [x] **Enterprise contract first.** Extend the existing `ResolvePath` boundary into
      provider-neutral, server-derived tenant storage capabilities for file/directory connectors and
      operations such as `FLATFILE`, `DIRECTORY`, and `SEND FILE`.

      **Completed (2026-08-10).** `TenantStorageCapability` binds an immutable server-derived
      `TenantContext`, run identifier, object prefix, canonical filesystem roots, and read/write
      grants. `IExecutionContext.ResolvePath` enforces root containment for every connector and
      handler path, while `FileSystemPolicyAuthorizer` enforces the operation-specific grant after
      canonical/symlink resolution and before I/O. Caller object identifiers are assertions against
      the issued tenant/run prefix and cannot select another prefix. Contract tests prove outside
      paths, cross-tenant object keys, traversal segments, and writes through read-only grants fail.

- [x] **Dedicated.** Tenant-specific artifact roots and object prefixes, with canonical paths,
      symlinks, archives, caches, checkpoints, and spill all remaining inside the authorized
      tenant/run root. Do not treat `chroot` or a container filesystem alone as authority.
      **Gap — previously implicit inside the quality bullet's trailing "and artifact roots".**

      **Completed (2026-08-10).** Dedicated Portal artifact storage
      now transparently prefixes every provider key with the host-fixed tenant while preserving
      logical keys at service boundaries; a caller naming another tenant only creates a nested key
      inside its own prefix. Report execution receives canonical read-only script/map grants,
      read/write dataset/snapshot grants, and a disposable tenant/run scratch root. Spill files use
      and delete that scratch grant; report and non-report checkpoints use the tenant's dedicated
      session root and key scope. Archive extraction reauthorizes every target through the capability,
      and dataset preview cache identities include server-derived tenant scope. Startup fails visibly
      on legacy unprefixed or foreign artifacts so an upgrade cannot silently shadow data or invent
      ownership; operators must migrate or quarantine those artifacts explicitly. Capability,
      symlink/path, archive, cache, checkpoint, spill, prefix-isolation, and legacy-collision tests pin
      the boundary.

- [x] **Finish the `admin tenant` verb family** — `export` and `import`. Completed 2026-08-10 with
      the production Portal engine-replay and Orchestrator-store adapters, signed/encrypted export
      composition, dry-run and fail-closed collision planning, environment/`SECRET:` credentials,
      disabled imported workloads, generated CLI documentation, and narrow read-only export scope.
      This retained discovery entry is satisfied by Phase A Slice 3 above.

- [x] Add the syntax-addition checklist to `CONTRIBUTING.md`: parser/runtime, EBNF, docs/help/snippets,
      lint/autocomplete, connector pushdown mappings, compatibility, and regression tests must move
      together.

- [x] Move provider-specific SQL rewrites out of `QueryCompiler` and scattered connector code into a
      centralized, registered dialect abstraction with focused translation and unsupported-feature tests.

- [x] **A low-threshold spill lane.** `scripts/test-lane.ps1 -Lane spill` re-runs the engine and SLT
      suites with spill/sort/batch thresholds set to a handful of rows, turning every query already
      in the corpus into spill coverage. `BatchSize` is 7 deliberately — not round, not a divisor of
      the corpus row counts — so batch boundaries fall inside logical groups.

- [x] **State the schema-stability invariant where it is established.**
      `ColumnBatchSchemaStabilityTests` covers both divergence modes against hand-built batches, in
      milliseconds, instead of relying on an exception thrown by the spill writer on data large
      enough to spill.

- [x] **An engine-surface corpus, for what SLT structurally cannot express (2026-08-11).**
      `tests/engine_corpus/*.etest`, run by `EngineCorpusTests` (one xUnit test per file, fresh
      evaluator and temp directory each, standard lane — not gated behind `ETL_SQL_RUN_SLT`).

      *Why it exists.* SLT is a conformance corpus for a **database**: it asks whether `SELECT a+b`
      returns the right values, and it is good at that. It has no way to express a file, a
      connector, or a load, so `BULK INSERT`, `EXPORT`, `DATASET`, quarantine routing and `@expect`
      have **zero** occurrences between them across all 45 corpus files — measured, not assumed —
      and that surface is most of what makes this an engine rather than a database.

      *Format.* Deliberately mirrors SLT (`statement ok`, `statement error [substring]`, `query`
      with results after a `----` rule) so the two read alike, plus one addition: a `file <name>`
      record embeds a fixture inline, which is what makes a load testable without a checked-in
      binary or a fixture directory to keep in sync. `${dir}` in any SQL body expands to the run
      directory, escaped for a SQL literal. Parser and runner are in
      `tests/ETL-SQL.Tests/EngineCorpus/`.

      *Covered so far:* `bulk_insert.etest` (positional mapping, `FIRSTROW`, terminators, empty
      fields, count mismatches, missing file), `quality_routing.etest` (`QUARANTINE` partitioning
      the input, `WARN`, `THROW`, numbered rules, and a clean input leaving the quarantine target
      empty rather than absent), `flatfile_roundtrip.etest` (write-then-read identity, values that
      merely look numeric, quoted delimiters, header on/off, NULL).

- [x] **Give the lane a `-ContinueOnFailure` switch.** `Invoke-DotNetTest` exits on the first failing
      project (correct for a gate), so SLT never ran and one pass cannot produce a full triage list.
      Done — `scripts/test-lane.ps1` accumulates `$script:LaneFailures` and defers the exit code.

- [x] **Vary data shape in the fuzzer.** Keep the grammar walk; add a seeded generator varying row
      count across the batch/spill boundaries, null density per column (0%, sparse, one entire
      batch, 100%), and type mix — including a column whose CLR type differs between batches, which
      is what the spill defect actually was.

      **Done 2026-08-11.** `FuzzDataShape` draws row count from counts chosen to straddle batch
      boundaries (`0, 1, 2, 6, 7, 8, 13, 49, 50, 51, 99, 101, 257` — awkward on purpose, since a
      count that divides evenly into batches hides the bugs batch boundaries cause), a per-column
      null density, and values that have historically confused typed sinks (`00123`, `1e5`,
      `4.50`, an embedded quote, over-length strings, negative and tiny decimals).

      The seed now resolves in `InitializeAsync` — one `ResolveSeed()` for both the data and the
      grammar walk, because seeding them separately would mean a reported reproducer did not
      reproduce. Shape is echoed to test output and written into every reproducer file. Verified:
      the same seed yields the same shape across runs, different seeds differ, and
      `ETLSQL_FUZZ_SEED=999` lands on `WholeBatch` nulls — the all-NULL-column-in-one-batch case
      that has no type evidence, which is the shape the spill defect actually needed.

      Not yet varied: the fuzz table's *schema* is still fixed at four columns of three types, so a
      column whose CLR type differs between batches is only approximated by the whole-batch NULL
      case. Varying column count and declared types is the natural next step.

- [x] **A rule-catalogue property test.** Reflect over every `ColumnRule` and assert each can fail on
      a crafted row, and that misconfiguration (unknown type, unprojected column, arity mismatch) is
      a hard error rather than a rule that passes everything. Design decision 5 already states this
      as a principle; nothing enforces it across the set. Same shape as
      `EngineSubsystemCoverageTests`. Done — `ColumnRuleCatalogPropertyTests`: a reflection test
      fails the build when a new `ColumnRule` has no violating-row case, plus a per-rule theory.

- [x] **A general AST round-trip property.** Existing round-trip tests are per-feature, written with
      the feature, so `ON FAILURE` — added later — had none and the formatter dropped it silently.
      Enumerate `Statement` subclasses and assert parse → `ToSql` → parse preserves. Done —
      `AstRoundTripPropertyTests` asserts every keyword survives serialization and that the output
      reparses, with a short documented allowlist for forms the serializer normalizes.

- [x] **Spill wrote outside its session directory.** **Fixed 2026-08-11**, and it was a production
      defect rather than the test artefact it looked like.

      `SessionRoot` is the root that *contains* session directories: every production caller passes
      a parent (`EngineRunner` → `sessionManager.SessionRoot`; Orchestrator and `DashboardService` →
      `storageAuthority.CheckpointRoot`), the property itself falls back to
      `_sessionStateManager.SessionRoot`, and both `SessionStateManager` and
      `SqliteSessionMetadataStore` append the session id to it.

      `SpillStore` did not. It used `Path.Combine(_context.SessionRoot, "spill")`, so in production
      **every session's spill files landed in one shared `<root>/spill`** while
      `SessionStateManager.CleanupSession` looked under `<root>/<id>/spill` and found nothing —
      concurrent sessions sharing a spill directory, and spilled data surviving the session that
      produced it. Now scoped by session id, matching what cleanup resolves.

      `SpillEncryptionTests` was compensating for this by setting `SessionRoot` to an
      already-scoped path, which made the *metadata* save land at `<root>/<id>/<id>` while the load
      looked at `<root>/<id>` — that mismatch was the `NullReferenceException`. With spill fixed the
      test uses production semantics and passes.

      **The trap worth remembering:** the first plausible reading was "`SaveSession` should not
      trust a pre-combined `SessionRoot`", which would have changed session persistence for every
      caller to accommodate one test's wrong assumption. Checking what the property means across
      *all* its assignments inverted the fix.

      Original diagnosis retained below for the record.

- [x] ~~**Session save and load disagree about the storage root.**~~ Root cause of the long-standing
      `SpillEncryptionTests.SpillEncryption_ShouldPersistAcrossSessions` baseline failure, traced
      2026-08-11 — recorded so the next person does not re-derive it:

      - `SessionStateManager.SaveSession` takes its root from **`evaluator.SessionRoot`**, which
        callers already set to a session-scoped directory (`<root>/<sessionId>`).
      - `LoadSession` takes its root from the **manager's** `SessionRoot` and then appends the
        session id itself, via `GetSessionDirectory` → `<root>/<sessionId>/metadata.db`.

      The store appends the id a second time: `SqliteSessionMetadataStore.cs:38` builds
      `Path.Combine(resolvedRoot, sessionId, "metadata.db")`. So with `SessionRoot` already
      session-scoped, a save lands at `<root>/<id>/<id>/metadata.db` while the load looks at
      `<root>/<id>/metadata.db`. `File.Exists` fails, `LoadSession` returns null, and the caller
      dereferences it. The symptom was a bare `NullReferenceException` at
      `Evaluator.LoadSessionState`, naming neither the session nor the path. Confirmed from the new
      diagnostic, which reported root
      `...\ETL-SQL-SpillTests\<guid>\3ac953a1` — the session-scoped form.

      **Now reports itself:** `LoadSessionState` rejects a null state with the session id, the root
      it looked in, and the likely cause. That converts the crash into a diagnosis but does **not**
      fix the asymmetry — decide which end owns session scoping (most likely: `SaveSession` should
      use the same `GetSessionDirectory` resolution as `LoadSession` rather than trusting a
      pre-combined `SessionRoot`), then make both use it. Not attempted here because session
      persistence has enough callers that the change wants its own verification pass.

      Related silent path in the same file: `SaveSession` **returns without saving** when handed an
      object that is not an `Evaluator` (`if (evaluatorObj is not Evaluator evaluator) return;`), so
      a wrong caller gets a successful-looking no-op.

- [x] **12 tests fail on the release branch itself**, verified by checking out `release/v0.18.0`
      clean (2026-08-10), so they are not regressions from any feature branch and release evidence
      cannot be collected until they are resolved or explicitly accepted:
      `SpillEncryptionTests.SpillEncryption_ShouldPersistAcrossSessions`,
      `AuthorshipPermissionBoundaryTests.AuthorshipComparisons_AreAllInventoried`, and ten in
      `ETL-SQL.Portal.Tests` (`ArchitectureDocReconciliationTests` ×2, `PolicyAuthorityApiTests`,
      `FleetWorkspaceAndExportPlanTests`, `PortalIntegrationTests`, `SharedDelegatedIdentityAdminTests`,
      `SharedTenantHttpBoundaryTests` ×2, `SupportBundleTests`, `UpgradePathDrillTests`).

- [x] **Sweep the samples that fail.** **Cleared 2026-08-10** — 178 passed, 17 skipped, 0 failed
      (was 16 failing). Re-check with `pwsh -File scripts/Test-AllSamples.ps1 -Passes 2`.

      Only three of the sixteen were stale syntax. The rest split into samples the runner did not
      know how to invoke (portal-mode, or a template needing `--var`) and **five genuine engine
      defects**, listed under the second cluster below. Two follow-ups it raised are their own items:

- [x] **P0 — `PARTITION BY` returned bucket-wide window values once a partition spilled.**
      **Fixed 2026-08-10** by folding one accumulator set per partition key instead of one per
      bucket, in both scan passes, with the replay pass looking up each row's own key. The columnar
      fast path builds the key from batch ordinals and declines — falling back to the row scan —
      when a partition expression is not a plain column it carries. Keys are rendered to text
      through one shared helper because the scan may read a value from a column batch while the
      replay reads it from a materialized row, and the same column can come back as `long` one way
      and `int` the other; boxed equality would then file one partition under two keys.

      Reproducers in `WindowPartitionSpillCorrectnessTests` are now live, and
      `ExternalWindowEngine_FullPartitionAggregates_UseStreamingSpillReplay` still passes — that
      test asserts both that this path is chosen for a partitioned window and that the columnar scan
      is used, so it is the evidence the optimization survived the fix rather than being disabled by
      it. 61 window tests also pass under the spill lane's thresholds.

      The record of the original defect follows, because the failed first attempt is the useful part.
      `ExternalWindowEngine.ProcessBucketPartitionReplaySpill` computes **one** set of aggregate
      values for an entire hash bucket and then writes them onto every row in it:

      ```csharp
      var results = await TryScanPartitionReplayBatches(name, group);  // one dictionary per bucket
      await foreach (var row in ReadPartitionStream(name))
          foreach (var (key, value) in results) row[key] = value;      // same value on every row
      ```

      That is correct only if a bucket holds exactly one logical partition. Buckets are hash
      partitions, so with more distinct partition keys than buckets — the normal case for a
      high-cardinality `PARTITION BY` — one bucket holds many partitions and each row receives the
      **bucket's** aggregate instead of its own.

      Reached whenever a bucket exceeds `WindowSpillThreshold` (default 10,000 rows) and the
      functions are partition-aggregate compatible: `COUNT`/`SUM`/`MIN`/`MAX`/`AVG` with no
      `ORDER BY` and no frame. So `SELECT COUNT(*) OVER (PARTITION BY customer_id)` over a large
      table returns **silently wrong numbers** — no error, no warning.

      Found by the spill lane, not by a test — `ExternalWindowEngine_PartitionSampleIncreasesFanOutWithoutLosingRows`
      asserts `COUNT(*) OVER (PARTITION BY Grp)` is 1 for 4,096 unique groups and returns 249 under a
      low threshold. It passes at the default only because the buckets stay under 10,000 rows.

      **Reproducers are committed and skipped**, in
      `tests/ETL-SQL.Tests/Engine/WindowPartitionSpillCorrectnessTests.cs`. They drive the defect
      through the engine with a pinned threshold, so they fail on the behaviour rather than on a plan
      choice: `SUM` over 200 two-row partitions returns 16 — the bucket's sum — instead of 2. Unskip
      them with the fix.

      **The obvious fix is wrong; do not take it.** Declining the path whenever a `PARTITION BY` is
      present makes results correct, and was tried and reverted, because it costs more than it buys:
      `ExternalWindowEngine_FullPartitionAggregates_UseStreamingSpillReplay` shows the path is
      *intended* for a partitioned window and asserts it is chosen, so the change would have meant
      weakening an existing assertion to fit the fix. Worse, a partitioned window would then fall
      through to materializing each bucket in memory — turning a large query that works today into
      an OOM. Trading a wrong answer for a crash is not a fix.

      **Do this instead: accumulate per partition key rather than per bucket.**
      `ProcessBucketPartitionReplaySpill` should build
      `Dictionary<CompositeKey, Dictionary<string, object?>>` and, when emitting, look each row's own
      partition key up. `ScanPartitionReplayRows` can evaluate `group.Signature.PartitionBy` per row
      exactly as `PartitionStream` does. The columnar fast path
      (`TryScanPartitionReplayBatches`) can only build the key when every partition expression is a
      plain identifier resolvable to a batch ordinal — when it is not, return null, which already
      falls back to the row scan. That keeps the optimization and makes it correct, which is what
      the sibling `ProcessBucketDeepSpill` achieves by sorting on the partition keys and resetting
      its state at each boundary.

- [x] **P0 — `AstSerializer` recurses per operand, so a ~50-term boolean chain kills the process.**
      **Found and fixed 2026-08-11. Nothing to do with spilling** — reproduced at default thresholds.
      Fixed in `550ba693`: both the serializer and `Parser.ResolveNamedWindows` now walk expressions
      on an explicit worklist. `select5.test` runs all 1,436 records in 10s; `ExpressionDepthTests`
      guards chains to 2,000 terms, both sides of the parser ceiling, and the exact
      parenthesization the execution-policy hash depends on.

      **Exact cause.** `AstSerializer.FormatBinary` → `AstNode.ToSql()` → `Format` recurses once per
      operand of a left-deep `AND` chain, three frames per term and no depth guard. A `WHERE` with
      49 conjuncts overflows the stack. Reproducer: `tests/slt_data/corpus/select5.test` L20609
      (`join-50-1`, 50 tables and 49 equality predicates). The runtime prints
      `Stack overflow.` / `Repeated 50 times:` with exactly that three-frame cycle.

      **It is on the mandatory production path, not a diagnostic.** The caller is
      `Evaluator.cs:1040` — the canonical-script hash captured for `ExecutionPolicySnapshot` at
      `CurrentRecursiveDepth == 0`, before any statement executes. Every top-level script is
      serialized in full, so any script with a boolean chain this long dies at governance capture
      with no message, no statement having run, and nothing catchable. Fifty `AND` terms is
      ordinary in generated ETL predicates.

      **Why the existing ceiling did not catch it** — worth keeping, because it generalizes: the
      parser *does* bound expression nesting at 100 and reports a located diagnostic past it. That
      guard counts nested parentheses, and `a AND b AND c` is parsed by a **loop** that never
      increments it. Deep nesting was bounded and safe; an arbitrarily long chain went straight past
      into a serializer with no bound at all. A guard that covers one shape of depth reads as
      covering both.

      **Class partly closed.** `ExpressionDepthTests` now drives a 500-term chain through **all 48
      lint rules** (reflected over, so rules added later are included automatically) and through
      `EXPLAIN`, both green — so no third recursive walk is reachable by those paths. 38 source
      files mention `BinaryExpression`, so this is evidence rather than proof; the paths not yet
      driven this way are pushdown/SQL compilation and the lineage analyzer. A shared iterative
      traversal helper that all visitors used would close the class outright rather than the
      instances.

      **The instrument was broken, and every earlier conclusion here was drawn through it.** Two
      defects in the corpus harness, both now fixed, invalidated the whole investigation:

      1. **`SltRunner` registered no `IConfiguration`.** It builds its own `ServiceCollection`
         instead of using the production composition root, so `DefaultThresholds` received `null`
         and returned its built-in constants. Every `Engine__*` override the lane set was accepted
         and ignored: a run configured to spill at 25 rows executed at 1,000,000. Verified directly
         — with `Engine__TempTableSpillThresholdRows=25` set, the evaluator resolved `1000000`.
         **The corpus has never once run at a low threshold**, so the spill paths the lane claims
         to cover were never reached. Fixed by registering an environment-variable configuration;
         `SltRunnerConfigurationTests` now asserts an override reaches the evaluator, and that the
         unconfigured default is still 1,000,000.
      2. **`slt_progress.log` was a single shared file under 4-way parallelism.** `LogProgress` did
         `File.WriteAllText` to one path while `Parallel.ForEachAsync` ran four files at once, so
         the surviving line named whichever worker wrote last — a 1-in-4 chance of naming the one
         that died. Now written per test file as `slt_progress.<name>.log`.

      **Therefore retracted:** the "exact location" `select3.test L32135` (an arbitrary worker's
      last statement, which is why it never reproduced standalone), and the claim that
      `TempTableSpillThresholdRows=25` or `MaxInMemoryBatches=2` each trigger it — neither was ever
      applied. With the threshold genuinely set to 25, select3.test runs all 3,351 records clean in
      34s. The pass/fail pattern that bisection produced was noise.

      **What still stands:** the crash is a real `System.StackOverflowException` on a threadpool
      worker (crash dump via `dotnet test --blame-crash`, then `dotnet-dump analyze -c clrthreads`).
      A stack overflow cannot be caught, so the only symptom is `Test host process crashed` with no
      message. Dump stacks are useless here — deepest managed stack is 67 frames even in a **full**
      dump, because the runtime tears the overflowing stack down before the collector runs. Do not
      spend time reading them.

      **How it was finally found.** Per-file progress logs narrowed the crash from a 1-in-4 guess to
      five candidates; running each file alone identified `select5.test` (`select1`–`select4` all
      exit 0), and running it alone let the runtime print the repeating frame cycle that names the
      method. Total elapsed once the instrument was trustworthy: about fifteen minutes.

      **Lesson worth keeping:** five hypotheses were tested and disproved against an instrument that
      could not report what it claimed — and all five were about spilling, because the broken
      instrument's only signal was a threshold setting that was never applied. Verify that a
      diagnostic actually observes the thing it names before spending experiments on what it says.
