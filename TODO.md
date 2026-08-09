# ETL-SQL Development TODO List

Use this list as the execution ledger for active-release and roadmap work. Once work is verified,
record its notable outcome in `CHANGELOG.md` and remove the completed task from this file and from
`ROADMAP.md`. `ROADMAP.md` remains the high-level product-direction source, and its initiatives are
decomposed into actionable tasks here.

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
[v0.17.0-code-review.md](docs/architecture/decisions/v0.17.0-code-review.md).

Implementation has been fixed in the canonical shared runtime and synced to host copies; the
remaining work requires the next CodeQL run on `main`.

- [ ] Confirm alert 323 closes on the next `main` scan.

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

- [ ] **Make it a required status check.** The only part still outstanding, and it is a repository
      setting rather than code — verify in branch protection once the run on `release/v0.18.0` has
      gone green at least once. **When doing so, add a companion always-succeeds job**: the push
      trigger is now path-filtered, and a path-filtered workflow reports *skipped* rather than
      *success*, so a required check that never reports blocks every unrelated pull request.

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

### Scale certification — make the harness incapable of false failures

**Resolves a question open since v0.15.0.** There was no engine regression in v0.15.0, v0.16.0, or
v0.17.0. Every "regression" was produced by measuring cold binaries at the end of a long gate. Full
measurements in
[v0.17.0-performance-results.md](docs/architecture/decisions/v0.17.0-performance-results.md).

The same commit measures 5013 ms warmed and 8977 ms cold — a **56% spread**, far wider than any
threshold the gate compares against. v0.15.0 reached the right conclusion ("environmental, not
code") but had no mechanism to prove it, so it was deferred twice more and cost v0.17.0 most of a
release day plus a false regression alarm.

Remaining work:

- [ ] Investigate performance improvements when data-quality allocation is active. Focus on reducing
      per-row allocation and GC pause time without weakening `@expect`/`@fail` behavior, quarantine
      routing, or lineage/tag capture.

Do **not** re-bless the baselines. `baseline-smoke.json` and `baseline-standard.json` both pass when
measured correctly; an earlier bless of cold readings was correctly reverted in `e3fa80af`.

---

## Roadmap execution backlog

These tasks decompose the future tracks in [ROADMAP.md](ROADMAP.md). Their presence here makes work
reviewable; it does not assign them to v0.18.0 or turn candidate phases into release commitments.
Keep the roadmap's P0/P1/P2 ordering unless a release plan explicitly changes it.

### Platform — Deployment Profiles and Upgrade Certification

#### P2 — Deployment-profile certification

- [ ] Retain commit-bound JSON and Markdown certification evidence with topology, hashes, mappings,
      continuity counts, negative isolation results, and rollback outcomes. SaaS evidence must name
      Managed Dedicated or Shared topology explicitly.
- [ ] Add current per-profile and per-transition evidence to release claims. Report Managed Dedicated
      and Shared SaaS separately; neither inherits the other's claim status.

### Portal — Comprehensive Product and UX Update

The remaining Portal-wide item is consolidating the last duplicated headers and focus-management
implementations without regressing the browser-covered dialog behavior.
#### P1 — Accessibility and visual-system completion

- [ ] Consolidate shared headers, identity, module gating, themes, spacing, icons, status chips,
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

### Orchestrator — Per-Object Authorization

Trigger this track when a second client is introduced or an Orchestrator is shared across teams or
tenants. Until then, retain v0.18.0 actor attribution as attribution—not authorization.

**Interaction with Operations Triage and Run Flight Recorder (above).** Neither track blocks the
other, but the flight recorder changes what a single coarse grant is worth, so one decision here has
ordering pressure: **define the ACL vocabulary with a read grant distinct from a manage grant.**
Today `OrchestratorAccess` (`Program.cs:298`) means see-the-tab *and* trigger/kill/stop-service in
one role. Once statement-level history is persisted, that same grant also means "read the statement
text of every job in the estate" — an analyst who should see why the nightly load failed does not
therefore need kill and service-stop. If per-object ACLs ship with manage semantics only, adding a
read grant afterwards is a migration rather than a definition.

- [ ] Federate a verifiable caller identity from Portal/OIDC; do not trust an identity header.
- [ ] Add per-object ACLs for `JOB`, `SCHEDULE`, and `NOTIFICATION` using the Portal grant
      vocabulary, with **read** and **manage** as separate grants (see the interaction note above).
- [ ] Decide authority for a parameter-overridden trigger — an override can widen a data scope, so
      "may this principal trigger job X" and "may they override its variables" are two questions.
      Triage P2 is safe under `OrchestratorAccess` for a single-team Team deployment; a shared or
      multi-team Orchestrator needs this first.
- [ ] Add enforceable ownership for shared names and prevent unauthorized `CREATE OR ALTER` takeover.
- [ ] Attribute every Orchestrator mutation audit record to a real principal rather than a service.
- [ ] Add negative tests proving a reachable Orchestrator does not imply authority over another
      principal's objects.

### Orchestrator — Operations Triage and Run Flight Recorder

Statement timelines are now durable across in-process, one-shot, and warm-runner execution. The
remaining work is the joined operator drill-down, recovery controls, and cross-profile evidence.
#### P1 — Flight recorder (persist what is already measured)
- [ ] Join the run drill-down across all three sources now available: statement timeline, the
      normalized data-quality failures, and `ScriptHashAtRunTime`/`HashMatched` — the last of which
      tells an operator *the script changed between the good run and the bad one*, which SSISDB
      cannot do.

#### P2 — Recovery controls

- [ ] Thread variable overrides through `/api/scheduled-jobs/{name}/trigger` → `TriggerJobAsync` →
      `BuildArguments` as `--var`, turning a backfill from "edit the job, run it, remember to edit it
      back" into a form. Overrides must also apply on the `ArgumentsTemplate` branch, which currently
      bypasses the default argument builder.
- [ ] Treat a parameter-overridden trigger as a privileged, audited mutation — an override can widen
      a data scope — and redact override values that resolve to secrets before they reach history.
- [ ] Expose resume as **"Resume from checkpoint `<label>`"**, passing `--resume` with the run's
      session id, disabled with a stated reason when the run was not a persistent session or never
      reached a label. Be explicit in the UI that this is opt-in on script authoring and will not
      retroactively cover existing jobs.
- [ ] **Do not implement resume-at-statement-index.** It is unsound here: statements share the
      evaluator's variable scope, derived/temp result sets, connection state, and open transactions,
      so restarting at an arbitrary index either fails on an unbound variable or silently runs
      against a half-built intermediate. SQL Agent can start at step 3 because its steps are
      independent processes. The author-declared checkpoint label is the only safe unit, and it is
      the one the engine already implements.

#### Deployment-profile portability review

Required by [Deployment_Profile_Standards.md](docs/architecture/standards/Deployment_Profile_Standards.md#feature-design-portability-review).
Smallest safe profile is **Solo**, and the capability must not become Portal-only.

- [x] **Solo.** The smallest safe form already exists as `eng.profile` (live, in-session) — so the
      durable table must be reachable as an `eng.*` read model, not only through the Portal inbox,
      or Solo silently loses a capability that Team gains. Precedent: `eng.job_history`,
      `eng.data_quality_status`, and `eng.data_quality_failures` are all already exposed this way
      (`EngineCatalogDataSources.cs`).

      **Done (v0.18.0).** `eng.job_statement_metrics` returns the live session as `CURRENT_RUN`
      followed by persisted rows as `HISTORY`, with column names matching `eng.profile` so one query
      shape reads either. Documented at `docs/reference/eng/job-statement-metrics.md`.

**Decided: a bare CLI run stays live-only and records nothing.** That is the point of the profile —
developing against real data should not accumulate a run history, and only production execution is
worth retaining. `eng.profile` remains the Solo answer.

- [ ] **Team.** The reference case for this track; no profile change expected. The 200-job shop above
      *is* the Team profile, and Scheduling/Observability are already Green here.
- [x] **Enterprise.** Statement metrics must be written to the shared store, not node-local, or the
      inbox returns different results per node behind the load balancer; the new table needs both
      `SqliteOrchestratorDialect` and `NpgsqlOrchestratorDialect`. Retention/roll-up must be
      leader-elected via the existing lease/`ClusterLock` machinery rather than running concurrently
      on every node. Parameter-override triggers must reach the audit outbox.

      **Partly done (v0.18.0).** The table already goes through the shared store and both dialects
      (`Int64Type` rewrites cover the duration and byte counters, since PostgreSQL `INTEGER` is
      32-bit). **Maintenance is now leased**: roll-up and both prunes sat behind no lock at all, so
      behind a load balancer every node ran them concurrently against one database — duplicating
      roll-up work and racing each other's deletes. A node that loses the race now *skips the cycle*
      rather than waiting, because blocking a scheduler loop to redo maintenance is worse than
      deferring it a few minutes; the lease is released rather than left to expire.
      `Scheduler:MaintenanceLeaseMinutes` is configurable.

      **Still open:** parameter-override triggers reaching the audit outbox — that belongs with the
      P2 recovery-controls item above, which has not been started.
- [ ] **SaaS.** Observability remains **Red** until tenant telemetry and support-access separation are
      certified. Managed Dedicated must prove its tenant-specific store and tenant-approved support
      path; Shared must additionally prove server-derived scope in cross-tenant aggregation. Persisted
      statement text is tenant SQL, so platform triage is controlled support access rather than
      implicit platform authority.
- [ ] Confirm no matrix cell moves backward, record Dedicated and Shared SaaS status separately, and
      record the review outcome the way
      [v0.18.0](docs/architecture/decisions/v0.18.0-deployment-profile-review.md) did.

### Platform — Admin CLI for Identity and Access

User, group, membership, session, and access verbs are complete. The remaining identity verb family
is service-account lifecycle, including safe handling of its one-time secret.
#### Verb surface

Nested under `admin` (`admin user list`), following the `admin ha-soak <verb>` precedent rather
than the flat `admin set-secret` style — the identity family is ~25 verbs and flat naming stops
scanning cleanly. Record the inconsistency in the CLI reference so it reads as a decision.

- [ ] **Service accounts.** `service-account list|create|update|revoke` over
      `api/admin/service-accounts`. Sequencing trap: this is how the CLI's *own* credential is
      minted, so the first one must be creatable interactively in the Portal — do not build a
      bootstrap that requires a token to mint the first token.

**Read verbs and the cross-cutting foundation shipped (v0.18.0).** `admin portal-whoami`,
`admin user list|show|permissions`, `admin group list|members`, and `admin session list`, over
`PortalAdminClient` (HTTP, no Portal project reference, asserted by an architecture test).
Credentials come from the environment or a `SECRET:` reference and **never** from argv; the client
id may be a flag because it is an identifier, not a credential. Exit codes are distinct per failure
kind and documented in `docs/reference/portal-admin/admin-identity-cli.md`; not-found and
ambiguous-match are kept apart so a runbook can create the first but must stop on the second.

**Mutating verbs shipped too (v0.18.0).** `user create|delete|enable|disable|revoke-tokens`,
`group create|delete|add-member|remove-member`, and `session disconnect`, with both properties that
make the tool worth having over a browser:

- **Idempotence** — `--if-not-exists` on create, `--if-exists` on delete. Membership changes are
  idempotent unconditionally, because adding an existing member is exactly what a re-run does.
- **Optimistic concurrency** — every guarded write sends the version in `If-Match`. The default
  carries through the version just read, so a concurrent edit is a detectable conflict rather than a
  silent overwrite; `--if-version` pins an expected value. There is no way to ask for
  last-writer-wins. A 428 (version omitted) is reported as a conflict, since a script should react
  the same way: re-read and retry.

Passwords come only from `--password-stdin`; there is no `--password` flag, and a test asserts that
neither it nor `--client-secret` can be added without failing.

**The user, group, membership, session, and access verbs are now complete.** Added on top of the
above: `user update`, `user reset-password`, `group update`, `group capabilities` /
`set-capabilities`, and `access-simulate`.

Two behaviours worth knowing rather than discovering: `user update` and `group update` send only the
fields actually supplied, so changing an email cannot silently blank a name; and `set-capabilities`
**replaces** the grant wholesale rather than adding to it, with no `--capability` meaning "revoke
everything". Both are documented, and the second is stated plainly because "set" read as "add" is
the kind of misunderstanding that quietly removes someone's access.

Still open: the **`service-account`** verbs (`list|create|update|revoke`). Left deliberately for
their own pass — `create` returns a one-time secret, so it needs a decision about how a CLI hands
that back without it landing in a scrollback buffer or a CI log, and the bootstrap trap noted above
(the first account must be mintable from the Portal UI) belongs in the same discussion.

#### Disambiguate the two secret and connection stores (do this first)

- [ ] `admin set-secret` writes the machine-local `Governance:Secrets` provider
      (`SecretAdminService.cs`); the Portal Admin tab writes `PortalSecretStoreService`
      (`SecretsAdminController.cs`) — an encrypted, audited, RBAC'd store in the catalog DB. They
      are different stores with overlapping names. An operator who runs `admin set-secret`
      expecting to change what the Admin tab shows silently edits the wrong one. The `set-secret`
      help text says "(machine scope)"; nothing else does, and no `list` verb shows which store it
      read.
- [ ] Make the scope explicit and symmetric across the secret and connection verbs — a `--scope
      machine|portal` selector or separate verb families, decided once and applied to both. Do this
      **before** the identity verbs land, so the new surface inherits a coherent model instead of
      the ambiguity.
- [ ] Same audit for shared connections: `ConnectionAdminService` vs `ConnectionsAdminController`.

#### Admin TUI — considered, deferred, and the condition for revisiting

A terminal admin UI mirroring the Portal's Admin and Orchestrator tabs was evaluated and set aside.
Mirroring ~60 endpoints makes every future admin feature a three-place change, and this repo already
has a recurring defect shape where a Portal control exists, looks implemented, and is never asserted
end to end — a TUI mirror is fertile ground for exactly that. The deciding argument, though, is that
a TUI is not scriptable, and the operators asking for this want runbooks and CI, not keystrokes.

Revisit only as a thin read-mostly browser over the verbs above, once they exist and are stable —
scoped to discovery (`user permissions`, `access-simulate`, `group members`), where not knowing the
ID to type makes the CLI genuinely awkward. Mutations stay in the CLI. Note that a TUI speaking HTTP
is exactly as unavailable as the browser when the Portal is down, so it is a convenience tool, not a
break-glass one; anything claiming break-glass needs direct store access and is a different,
more dangerous design that must be justified separately.

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

Ordering below is by dependency. The existing Portal/Orchestrator Enterprise tracks supply the
identity, authorization, durable state, artifacts, secrets, policy, audit, HA, recovery, and
promotion foundation. Do not rebuild those capabilities in SaaS-specific services.

#### Phase A — Enterprise and portability prerequisites

- [ ] Complete the Enterprise prerequisites used by hosted deployments: verifiable caller identity,
      per-object authorization, shared PostgreSQL/artifact providers, scoped secret/policy authority,
      durable audit, HA, backup/restore, and upgrade/promotion evidence. Track implementation in the
      existing Enterprise and Orchestrator sections; this item proves the joined hosted prerequisite.
- [ ] Certify that Team is a single-node provider configuration rather than a separate implementation:
      no Team-only parser, evaluator, connector, catalog, UI, checkpoint, or promotion model.
- [ ] Deliver the minimum tenant portability bundle and SaaS → self-hosted Enterprise journey before
      Managed Dedicated SaaS general availability. The
      [Tenant Portability Architecture](docs/architecture/TenantPortability.md) owns the bundle and
      migration contract; this gate owns its release sequencing.

#### Phase B — Managed Dedicated SaaS

- [ ] Identity: establish platform/tenant identity separation and delegated tenant administration,
      and prove platform administration is separately audited and cannot implicitly impersonate a
      tenant user even when the tenant has its own deployment boundary.
- [ ] Policy: tenant-specific policy authority with platform/tenant separation, so one tenant's policy
      cannot be authored or overridden from platform scope.
- [ ] Connections and secrets: disjoint tenant provider/key namespaces plus export proof; no
      cross-tenant key reuse, raw secret export, or provider credential in an execution artifact.
- [ ] Scheduling and Execution: provision tenant-dedicated queues, schedules, leases, quotas, session
      roots, and VM/worker boundaries; run disposable OCI tasks without treating a shared-kernel
      container as the boundary between customers.
- [ ] Quality and stewardship: prove disjoint lineage, scans, quality evidence, caches, outboxes, and
      quarantine data using tenant-specific stores and artifact roots.
- [ ] Audit: provide tenant-complete audit plus separately authorized and audited platform support
      access; aggregate platform health must not expose tenant script or data content.
- [ ] Backup and recovery: tenant-scoped backup, export, restore, and key/artifact recovery, including
      proof that a restore cannot introduce another tenant's rows or resume its work.
- [ ] Gateway: enroll a tenant-owned outbound Gateway, register resources locally, map them through
      tenant-admin `SHARED:` aliases, and prove revocation, local credential custody, typed operations,
      and SaaS-to-on-premises connectivity before introducing a shared broker registry. Follow the
      [SaaS Tenant Isolation Architecture](docs/architecture/SaaSTenantIsolation.md#11-secure-outbound-data-gateway).
- [ ] Authoring: controlled tenant ingress and a certified tenant-admin/author boundary within the
      dedicated deployment.
- [ ] Reports (currently Yellow): certify tenant catalog, dataset, snapshot, share/embed, export, and
      subscription isolation within the dedicated deployment.
- [ ] Managed operations: automate provisioning, upgrades, drain/fence, capacity assignment, basic
      metering, support approval, portability export, legal/retention-aware deletion, and recovery for
      one tenant without manual SaaS-platform database edits.
- [ ] Relabel the current Tenant-isolation implementation-Green evidence as **Managed Dedicated only**,
      attach clean commit-bound topology evidence, and prevent it from satisfying Shared SaaS cells.

#### Phase C — Shared SaaS

- [ ] Prove tenant context is server-derived at every shared entry point — a negative test per surface
      that a caller-supplied tenant, alias, gateway, resource, run, object, or storage identifier cannot
      widen scope.
- [ ] Extend identity, delegated administration, policy, connections, secrets, keys, and catalog
      bindings to shared stores with tenant predicates/partitioning enforced below controller code and
      negative collision tests for equal numeric/logical IDs across tenants.
- [ ] Scheduling and Execution: implement the provider-neutral scheduler and Hardened per-run sandbox
      boundary with tenant-scoped queues, leases, capabilities, checkpoints, quotas, fair admission,
      ambiguous-outcome handling, and destructive cleanup.
- [ ] Quality and stewardship: prove tenant-isolated lineage/graph indexes, scans, quality evidence,
      quarantine, caches, searches, and outboxes in shared services.
- [ ] Audit: preserve tenant-complete audit while separately authorizing and auditing platform access;
      shared support tooling cannot become an impersonation or bulk-content path.
- [ ] Backup and recovery: tenant-scoped export/restore from shared stores, including proof that
      point-in-time recovery, retry, or cache rebuild cannot introduce another tenant's rows.
- [ ] Observability: tenant telemetry and support-access separation. **This is the cell the Operations
      Triage track above collides with** — cross-job aggregation and persisted statement text require
      server-derived scope and tenant-approved support access.
- [ ] High availability: tenant-aware fleet rollout, compatibility/drain behavior, and noisy-neighbour
      containment without falling back from Dedicated placement or Hardened isolation silently.
- [ ] Authoring and Reports: re-certify tenant ingress, catalogs, datasets, embeds, snapshots, exports,
      subscriptions, and interactive sessions against shared stores and worker fleets.
- [ ] Gateway Broker: add the shared tenant/gateway session registry, typed stream routing, metering,
      backpressure, and negative cross-tenant tests without weakening gateway-local resource policy.
- [ ] Move Shared Tenant isolation from Red to claim-Green only with clean commit-bound hostile
      cross-tenant evidence across database, artifact, cache, queue, audit, PII, lineage/quality, path,
      key, checkpoint, Gateway, sandbox, telemetry, support, restore, and resource-exhaustion surfaces.

Each item is complete only when the relevant **Dedicated or Shared** matrix cell has a current linked
evidence reference and the release review records the topology explicitly, the way
[v0.18.0](docs/architecture/decisions/v0.18.0-deployment-profile-review.md) recorded its review. Do
not infer Dedicated SaaS support from an Enterprise happy path, or Shared SaaS support from Dedicated
topology evidence.

### Testing — retire the wall-clock flake class (scheduled for v0.19.0)

Three release cycles have now recorded flaky tests with the **same shape**, and the standing policy
of "stabilize the minimum to ship and record it" has kept them out of the way without retiring them:

| Cycle | Test | Shape |
| :--- | :--- | :--- |
| v0.15.0 | `SchedulerServiceTests` (8), `ClusterLockTests.RunExclusive_SerializesConcurrentCriticalSections` | fixed delay as sole synchronization — **fixed**, `WaitUntilAsync` is the reference pattern |
| v0.17.0 | `ProcessJobExecutorChaosTests` (2), `PortalIntegrationTests.Snapshot_ConcurrentRefreshReadsAndExports_…` | wall-clock budget on process kill / concurrent refresh |
| v0.18.0 | `HostedServiceLaneTests.MissingDatasetAtRestKey_WithoutFallback_StopsApplication`, `SubscriptionIntegrationTests.Verify_Subscription_Failure_Scenario` | wall-clock budget exceeded under full-suite load |

**Why the existing guardrail does not catch these.** `scripts/check-flaky-test-delays.mjs` (CI,
`ci.yml:45`) flags a literal `Task.Delay` used as the *sole* synchronization before a positive
assertion, and unreviewed elapsed-time upper bounds. It deliberately **excludes** delays inside a
polling loop or behind a deadline — which is exactly the remaining shape. These tests poll
correctly; their *budget* is simply too small for a machine running the other ~900 tests. So the
next pass is not more of the same lint.

The failure mode is also asymmetric and worth stating: a too-small budget produces a **false
failure**, which costs a maintainer an investigation and teaches them to ignore red. A too-large
budget only costs wall-clock time on a run that was going to fail anyway.

- [ ] Establish the real distribution before changing any number. Run the Portal and Orchestrator
      lanes N times under deliberate load and record, per waiting test, the observed time-to-satisfy
      against its configured budget. Set budgets from that data rather than by doubling until green.
- [ ] Replace bare deadlines with a shared load-aware helper — the `WaitUntilAsync` idea extended
      with a budget that scales from a baseline measured once per run, so a saturated agent and a
      developer laptop do not need the same constant.
- [ ] Make a budget expiry **report what it was waiting for**. Every one of these cost an
      investigation mostly because the failure said "expected true, got false" rather than "waited
      15s for the host to stop; last observed state X". Diagnostics on timeout are the highest-value
      part of this item.
- [ ] Extend the guardrail to flag a *new* bare wall-clock budget in a waiting helper, so the class
      cannot quietly regrow once retired. Same annotation escape hatch as the existing check.
- [ ] Decide whether `HostedServiceLaneTests` should stay in the shared lane at all. It is the one
      Portal suite that starts the full `IHostedService` pipeline, making it both the slowest to
      reach a decision and the most sensitive to surrounding load — a separate lane may be a more
      honest fix than a larger number.
- [ ] Retire the three tracking documents into one when the class is closed, and record the outcome
      the way [v0.15.0](docs/architecture/decisions/v0.15.0-flaky-tests.md) recorded its fix, so the
      reference pattern stays findable.

**Do not** simply add retries. A retry hides a genuine intermittent product defect exactly as well
as it hides a test-harness one, and the shared-SQLite finding in the v0.18.0 notes — where the
browser lane's failure turned out to describe a real production sharing hazard — is the reason to
keep failures visible.

## Cross-Cutting SaaS and Portal Follow-through (Retained Discovery Items)

These items originated in the earlier SaaS/Portal gap rounds and remain here so their history is not
erased. The Progressive SaaS phases above own their delivery order and certification. Completion of
an item here must satisfy the corresponding canonical phase; it must not create a second provider,
catalog, execution path, or migration format.

### Security, identity, operations, and authoring

- [ ] **Tenant-Scoped Encryption Keys (BYOK)**: first establish one provider-neutral Enterprise key
      contract, then use tenant-isolated key namespaces for Managed Dedicated SaaS and prove
      tenant/key/version separation in Shared SaaS. Refactor `DatasetAtRestKeyValidator.cs`, dataset,
      credential, artifact, and checkpoint encryption away from a single global master key. Resolved
      keys never enter portable exports or execution images.
- [ ] **Tenant-Scoped Virtual Filesystem and Object Storage**: extend the existing `ResolvePath`
      boundary into provider-neutral, server-derived tenant storage capabilities for file/directory
      connectors and operations such as `FLATFILE`, `DIRECTORY`, and `SEND FILE`. Canonical paths,
      object prefixes, symlinks, archives, caches, checkpoints, and spill must remain inside the
      authorized tenant/run root. Do not treat `chroot` or a container filesystem alone as authority.
- [ ] **Noisy-Neighbor CPU/Memory/I/O Containment**: implement admission and runtime limits for CPU,
      memory, processes, scratch/spill, IOPS, network, rows, duration, connector concurrency, queue
      depth, and interactive sessions. Dedicated SaaS proves reserved placement; Shared SaaS adds
      Hardened per-run sandboxes and fair-share scheduling. Ordinary cgroups/containers are useful
      controls but are not the hostile-tenant security boundary.
- [ ] **Portal ETL IDE Data Preview & Schema Browser**: add interactive schema inspection and bounded
      row previews of intermediate `#temp` tables and governed source connections. This is a
      cross-profile Studio capability, not a SaaS-isolation prerequisite; start with Solo/Team,
      require Enterprise connection ACLs, and certify tenant scope before enabling it in SaaS.
- [ ] **SaaS Multi-Tenant Identity (Multi-IdP)**: Managed Dedicated SaaS supports one tenant-owned IdP
      configuration through the Enterprise identity contract. Shared SaaS later supports dynamic,
      server-verified tenant/issuer/domain discovery without trusting a caller-selected tenant or
      issuer and without allowing platform administrators to impersonate tenant users.
- [ ] **Usage Metering & Billing Collector**: begin with tenant-specific usage records for Managed
      Dedicated operations, then add shared-fleet attribution for rows/bytes, connector class,
      sandbox CPU/memory/I/O, Gateway traffic, storage, and concurrency. Metering has its own durable,
      tenant-partitioned ledger and cannot read payload content or become execution authorization.

### Shared-fleet isolation and portability

- [ ] **Tenant-Aware Fair-Share Scheduling**: implement tenant-partitioned queues and weighted/fair
      admission in the provider-neutral Execution Scheduler so one tenant cannot cause head-of-line
      blocking or starvation. Enforce reservations, maximums, backpressure, and Dedicated placement;
      do not silently borrow across an isolation or service-tier boundary.
- [ ] **Internal Network Egress Fencing**: execute tenant workloads with default-deny networking,
      blocked cloud metadata/control-plane/internal hosting ranges, and only capability-authorized
      connector, storage, telemetry, or Gateway Broker destinations. Test DNS rebinding, redirects,
      alternate address forms, port scanning, and policy changes during a run.
- [ ] **Tenant-Isolated Lineage Graphs**: partition shared metadata search and lineage/quality indexes,
      caches, graph traversal, exports, and support diagnostics so table names, schemas, tags, edges,
      and evidence cannot leak across tenants. Dedicated-store evidence is not sufficient for this
      shared-service item.
- [ ] **Full-Fidelity Tenant Portability Bundle**: unify the existing Portal configuration export,
      Orchestrator promotion package, source artifacts, and optional evidence/content into the one
      open, versioned, signed, tenant-encrypted format defined in
      [`TenantPortability.md`](docs/architecture/TenantPortability.md). Deliver the minimum
      configuration/artifact bundle and SaaS → self-hosted Enterprise proof before Managed Dedicated
      SaaS GA; add large resumable content and incremental deltas later. Deliberately exclude resolved
      secrets, private keys, capabilities, checkpoints, leases, caches, and in-flight work rather
      than making an indefensible "zero-loss" claim.
- [ ] **Portal Script Concurrent Editing Locks**: implement optimistic concurrency plus collaborative
      edit/session leases that warn authors and prevent silent overwrite. This is Team/Enterprise
      collaboration work, not a SaaS security prerequisite; SaaS additionally requires tenant-scoped
      lease keys, hard expiry, disconnect recovery, and negative cross-tenant tests.

## Bugs

- [ ] **SQLite `INSERT INTO <conn>.<table> SELECT` inserts each row twice.** Found while making the
      lineage cookbook runnable, after fixing the two defects that were masking it (below). On a
      **fresh** database, `samples/03_SQL_Engines/Sqlite_Operations.etlsql` stages three rows with
      distinct ids into `#stage`, inserts them once, and fails with
      `UNIQUE constraint failed: inventory.item_id`.

      Not yet root-caused. The leading hypothesis is that `SqliteDataSource.WriteBatches` enumerates
      its `IAsyncEnumerable<DataTable>` argument more than once — a re-enumerable source would then
      be written twice — but that was not confirmed, and it may instead be the INSERT handler
      invoking the write path twice for a pushdown-capable target. Worth checking whether other
      database connectors share the shape before fixing it in SQLite alone.

      Reproduce: `rm -f samples/output/local_store.db` then
      `dotnet run --project src/ETL-SQL.App -- run samples/03_SQL_Engines/Sqlite_Operations.etlsql`.


- [ ] **Create a working cookbook recipe for Lineage**  It should be two parts, part one should be data
  from a flat file into a database with some transformations in the middle.  This is an EDW load.  This
  should also include an export of the Lineage.  Part two would be importing the lineage and taking the 
  table from EDW and making a report from it.  Everything should work and the lineage on the report should
  show the flat file source to the EDW to the report and all transformations that happen between.
