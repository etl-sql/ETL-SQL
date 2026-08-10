# ETL-SQL Development TODO List

Use this list as the execution ledger for active-release and roadmap work. Once work is verified,
check it off in place and record its notable outcome in `CHANGELOG.md`; completed entries remain as
decision and delivery history. `ROADMAP.md` remains the high-level product-direction source, and its
initiatives are decomposed into actionable tasks here.

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

### The sample-scripts gate could never fail

**Fixed (v0.18.0), and it invalidates every previous release's sample evidence.**
`Test-AllSamples.ps1` printed `FAILED` per script and a red summary count, then fell off the end of
the script and exited 0. `Invoke-LoggedPhase` judges a phase by `$LASTEXITCODE`, so the "Sample
scripts" phase reported **Passed** no matter how many samples failed. The POSIX twin
`test-all-samples.sh` has always had `exit 1`; only the Windows script — the one the gate actually
runs — was missing it. CI runs no sample lane at all, so nothing else covered this.

Found by running the shipped `samples/03_SQL_Engines/Sqlite_Operations.etlsql` by hand while
building the lineage cookbook: it failed on a defect dating to the connector's introduction
(2026-05-29), meaning it shipped broken in v0.14.0 through v0.17.0.

- [x] `Test-AllSamples.ps1` exits non-zero on failure.
- [x] `-Passes <n>` runs the suite more than once. Sample output is gitignored, so a sample that
      writes to a persistent store passes on a clean checkout and fails for everyone who runs it
      twice — one pass structurally cannot see that class of defect. `Test-PreRelease.ps1` now runs
      the phase with `-Passes 2`; `test-all-samples.sh` takes the same count as its first argument.
- [x] `Sqlite_Operations.etlsql` made idempotent, and its staging corrected — three
      `SELECT … INTO #stage` statements read as accumulating but each replaces the table, so the
      sample had been demonstrating one row of the three it appeared to load.
- [x] `register_schedule.etlsql` made idempotent. Found by the second pass on its first real run:
      it registers a schedule and a job in the persistent Orchestrator store with plain `CREATE`,
      so it succeeded exactly once per machine and failed forever after.
- [ ] Consider a CI sample lane. The gate is currently the only thing that runs samples, and it is
      Windows-and-PowerShell only.

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
[v0.17.0-performance-results.md](docs/releases/v0.17.0-performance-results.md).

The same commit measures 5013 ms warmed and 8977 ms cold — a **56% spread**, far wider than any
threshold the gate compares against. v0.15.0 reached the right conclusion ("environmental, not
code") but had no mechanism to prove it, so it was deferred twice more and cost v0.17.0 most of a
release day plus a false regression alarm.

Remaining work:

- [x] Investigate performance improvements when data-quality allocation is active. Focus on reducing
      per-row allocation and GC pause time without weakening `@expect`/`@fail` behavior, quarantine
      routing, or lineage/tag capture.

      **Done (v0.18.0).** The passing synchronous-rule path (`NOT NULL`, comparison, `IN`,
      `MATCHES`, `EXISTS IN`, and prepared `UNIQUE`) no longer enters nested async `Task` state
      machines or boxes interface enumerators per row. A focused 100,000-row measurement fell from
      43.2 MB (432 bytes/row) to the test's ≤4 KB total noise budget. `EXPR` rules and actual
      quarantine/warn target writes retain their asynchronous paths. The full 91-test quality
      runtime/parser suite pins THROW/WARN/QUARANTINE, dry-run, PII, metrics, replay, lineage/tag,
      expression, and routing behavior alongside the new allocation budget.

Do **not** re-bless the baselines. `baseline-smoke.json` and `baseline-standard.json` both pass when
measured correctly; an earlier bless of cold readings was correctly reverted in `e3fa80af`.

---

## Roadmap execution backlog

These tasks decompose the future tracks in [ROADMAP.md](ROADMAP.md). Their presence here makes work
reviewable; it does not assign them to v0.18.0 or turn candidate phases into release commitments.
Keep the roadmap's P0/P1/P2 ordering unless a release plan explicitly changes it.

### Platform — Deployment Profiles and Upgrade Certification

#### P2 — Deployment-profile certification

- [x] Retain commit-bound JSON and Markdown certification evidence with topology, hashes, mappings,
      continuity counts, negative isolation results, and rollback outcomes. SaaS evidence must name
      Managed Dedicated or Shared topology explicitly.

      **Done (v0.18.0).** The certification runner now enables scenario evidence in its child test
      processes, validates required scenario ids and schemas, and aggregates concrete hashes,
      mapping decisions, continuity, negative proof, and rollback results into the commit-bound JSON
      and Markdown bundle. Dirty runs remain useful development evidence but are never release
      eligible. Managed Dedicated is named explicitly and Shared SaaS remains `NotCertified`.
- [ ] Add current per-profile and per-transition evidence to release claims. Report Managed Dedicated
      and Shared SaaS separately; neither inherits the other's claim status.
- [ ] Certify that Team is a single-node provider configuration rather than a separate implementation:
      no Team-only parser, evaluator, connector, catalog, UI, checkpoint, or promotion model.

      *Moved here from the SaaS track's Phase A (2026-08-09). It is an assertion about the Team
      profile and belongs to profile certification; filing it under a SaaS gate implied Team was
      something SaaS had to clear rather than something every release re-proves.*

### Portal — Comprehensive Product and UX Update

The remaining Portal-wide item is consolidating the last duplicated headers and focus-management
implementations without regressing the browser-covered dialog behavior.

#### P1 — Studio and collaboration capabilities

Both moved out of the SaaS track (2026-08-09). Each item's own text already said it was not a
SaaS-isolation prerequisite, so filing them under a SaaS heading made cross-profile Studio work look
blocked on hostile-tenant certification that it does not need.

- [ ] **Portal ETL IDE Data Preview & Schema Browser**: add interactive schema inspection and bounded
      row previews of intermediate `#temp` tables and governed source connections. Cross-profile
      Studio capability: start with Solo/Team, require Enterprise connection ACLs, and certify tenant
      scope before enabling it in SaaS (SaaS domain 7).
- [ ] **Portal Script Concurrent Editing Locks**: implement optimistic concurrency plus collaborative
      edit/session leases that warn authors and prevent silent overwrite. Team/Enterprise
      collaboration work; SaaS additionally requires tenant-scoped lease keys, hard expiry,
      disconnect recovery, and negative cross-tenant tests (SaaS domain 5).

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

### Portal — Quarantine Row Access

The authoritative design and rejected alternatives remain in
[ROADMAP.md](ROADMAP.md#portal--quarantine-row-access). The first usable slice is catalog-backed,
manifest-bound preview; it must not rehydrate every connection from the producing session or accept
an arbitrary connection/target from the browser.

- [ ] Extend `QuarantineReplayManifest` with nullable target connection alias, connector type, and
      catalog-backed provenance written at capture time. Existing manifests without provenance remain
      view-only.
- [ ] Decide and document preview authority: require the steward's ordinary connection permission, or
      make `DataQualityStewardAccess` plus the manifest-bound target sufficient. Then make
      `QuarantineTargetReadability` resolve enabled catalog entries using the caller's verified identity.
- [ ] Bootstrap the bounded preview session from the manifest's `SHARED:` alias, preserving the 15-second
      timeout, row cap, RLS identity, connector policy, secret resolution, and redacted errors. Gate the
      capability behind `Portal:DataQuality:AllowConnectionPreview`, default off.
- [ ] Audit every raw quarantine preview and add positive and negative coverage for readable targets,
      catalog misses, disabled entries, switch-off, unauthorized callers, request tampering, and error
      redaction.
- [ ] Document the administration and audit behavior, and extend the data-quality UI sandbox story so
      readable catalog-backed and view-only targets remain independently testable.

### Portal — Data Quality Follow-through

- [ ] Before quarantine preview becomes a polled or dashboard-refreshed surface, profile the per-request
      `ExecutionSession` cost and replace the full lexer/parser/linter/evaluator startup with a bounded
      reusable preview path if the measurements require it. Preserve identical policy, identity,
      redaction, timeout, and cancellation behavior.

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

- [x] Federate a verifiable caller identity from Portal/OIDC; do not trust an identity header.
- [x] Add per-object ACLs for `JOB`, `SCHEDULE`, and `NOTIFICATION` using the Portal grant
      vocabulary, with **read** and **manage** as separate grants (see the interaction note above).
- [x] Decide authority for a parameter-overridden trigger — an override can widen a data scope, so
      "may this principal trigger job X" and "may they override its variables" are two questions.
      Triage P2 is safe under `OrchestratorAccess` for a single-team Team deployment; a shared or
      multi-team Orchestrator needs this first.
- [x] Add enforceable ownership for shared names and prevent unauthorized `CREATE OR ALTER` takeover.
- [x] Attribute every Orchestrator mutation audit record to a real principal rather than a service.
- [x] Add negative tests proving a reachable Orchestrator does not imply authority over another
      principal's objects.

Done: Portal/OIDC callers now cross the service boundary in short-lived HMAC-signed assertions;
unsigned actor headers have no authority. Durable owner plus user/group/service ACLs distinguish
`READ`, `EXECUTE`, variable `OVERRIDE`, and `MANAGE` for jobs, schedules, and notifications. The
same checks run in HTTP endpoints and engine catalog handlers, so ad-hoc ETL-SQL cannot take over a
shared name. History, quality, triage, and ad-hoc status reads are filtered, and mutation security
events use the verified principal. Negative integration tests cover forged/missing identity,
cross-principal reads, endpoint and script `CREATE OR ALTER`, trigger versus override, and all three
catalog object kinds.

### Orchestrator — Operations Triage and Run Flight Recorder

Statement timelines are now durable across in-process, one-shot, and warm-runner execution. The
remaining work is the joined operator drill-down, recovery controls, and cross-profile evidence.
#### P1 — Flight recorder (persist what is already measured)
- [x] Join the run drill-down across all three sources now available: statement timeline, the
      normalized data-quality failures, and `ScriptHashAtRunTime`/`HashMatched` — the last of which
      tells an operator *the script changed between the good run and the bad one*, which SSISDB
      cannot do.

      **Done (v0.18.0).** The Portal triage board now opens a run-level evidence row that joins the
      registered/runtime hash decision, the normalized counts-only quality failures, and the
      normalized/capped statement timeline. Run reads are direct and bounded by durable run id;
      loading, missing, empty, and failed-read states are explicit, and the endpoint retains the
      existing `OrchestratorAccess` authorization boundary.

#### P2 — Recovery controls

- [x] Thread variable overrides through `/api/scheduled-jobs/{name}/trigger` → `TriggerJobAsync` →
      `BuildArguments` as `--var`, turning a backfill from "edit the job, run it, remember to edit it
      back" into a form. Overrides must also apply on the `ArgumentsTemplate` branch, which currently
      bypasses the default argument builder.
- [x] Treat a parameter-overridden trigger as a privileged, audited mutation — an override can widen
      a data scope — and redact override values that resolve to secrets before they reach history.

      **Done (v0.18.0).** Portal operators can open a one-run form and supply up to 32 validated
      input overrides without editing the saved job. The values reach in-process execution,
      one-shot processes, warm runners, retries, and custom `ArgumentsTemplate` launches; scripts
      retain their declared scheduled defaults when no override is supplied. Portal audit and the
      Orchestrator security-event outbox record normalized names and counts only, while process and
      operational logs never render values. A same-job concurrency race returns `409 Conflict`
      instead of accepting and discarding an override set. API, execution-path, redaction, dynamic
      form, and responsive UI coverage pin the contract.
- [x] Expose resume as **"Resume from checkpoint `<label>`"**, passing `--resume` with the run's
      session id, disabled with a stated reason when the run was not a persistent session or never
      reached a label. Be explicit in the UI that this is opt-in on script authoring and will not
      retroactively cover existing jobs.
- [x] **Do not implement resume-at-statement-index.** It is unsound here: statements share the
      evaluator's variable scope, derived/temp result sets, connection state, and open transactions,
      so restarting at an arbitrary index either fails on an unbound variable or silently runs
      against a half-built intermediate. SQL Agent can start at step 3 because its steps are
      independent processes. The author-declared checkpoint label is the only safe unit, and it is
      the one the engine already implements.

      **Done (v0.18.0).** Failed or cancelled persistent runs now retain an opaque session handle
      and the last reached top-level label without exposing the handle through history APIs. The
      Portal offers an audited `Resume · <label>` action only while that saved state and label are
      still valid; otherwise it shows the specific reason recovery is unavailable. Resume loads the
      saved evaluator state and passes `--resume` plus the recorded session through in-process,
      one-shot, warm-runner, retry, and custom-argument paths. The scheduler also verifies that the
      current saved script still contains the label. No API, executor, or UI accepts a statement
      index. Engine, store, scheduler, API, Portal, static UI, and responsive visual coverage pin
      the named-checkpoint-only contract and its replay/idempotency warning.

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

      Parameter-override triggers now emit a sanitized `OverrideAttempt` security event into the
      configured durable outbox, carrying variable names and count but never values.
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

### Platform — Admin CLI for Identity and Access

User, group, membership, session, and access verbs are complete. The remaining identity verb family
is service-account lifecycle, including safe handling of its one-time secret.
#### Verb surface

Nested under `admin` (`admin user list`), following the `admin ha-soak <verb>` precedent rather
than the flat `admin set-secret` style — the identity family is ~25 verbs and flat naming stops
scanning cleanly. Record the inconsistency in the CLI reference so it reads as a decision.

- [x] **Service accounts.** `service-account list|create|update|revoke` over
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

**Service-account lifecycle shipped (v0.18.0).** `service-account list|create|update|rotate-secret|revoke`
uses the same remote Portal client, idempotence, and version-guarded mutation model. Create and
rotation require `--secret-out`; the CLI reserves a new file, never overwrites, removes an unused
reservation on failure, and never emits the one-time secret to terminal or JSON. The first account
remains a tenant-admin Portal bootstrap. Later service identities use constrained delegation: same
human owner, and no scope, role, or Studio capability broader than the caller's current grant.

#### Disambiguate the two secret and connection stores (do this first)

- [x] `admin set-secret` writes the machine-local `Governance:Secrets` provider
      (`SecretAdminService.cs`); the Portal Admin tab writes `PortalSecretStoreService`
      (`SecretsAdminController.cs`) — an encrypted, audited, RBAC'd store in the catalog DB. They
      are different stores with overlapping names. An operator who runs `admin set-secret`
      expecting to change what the Admin tab shows silently edits the wrong one. The `set-secret`
      help text says "(machine scope)"; nothing else does, and no `list` verb shows which store it
      read.
- [x] Make the scope explicit and symmetric across the secret and connection verbs — a `--scope
      machine|portal` selector or separate verb families, decided once and applied to both. Do this
      **before** the identity verbs land, so the new surface inherits a coherent model instead of
      the ambiguity.
- [x] Same audit for shared connections: `ConnectionAdminService` vs `ConnectionsAdminController`.

**Store scope is explicit (v0.18.0).** Machine-local lifecycle is now nested symmetrically under
`admin machine secret set|list|verify|rotate|disable|enable|delete` and
`admin machine connection set|list|verify|disable|enable|delete`. The ambiguous flat verbs are
removed rather than retained as aliases. Both list surfaces name their configured machine provider;
the Portal Admin stores remain the encrypted, audited, tenant-admin surfaces in the catalog DB.

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

#### Phase A — Enterprise and portability prerequisites

Gates every domain below; nothing in Managed Dedicated ships before these.

- [x] Complete the Enterprise prerequisites used by hosted deployments: verifiable caller identity,
      per-object authorization, shared PostgreSQL/artifact providers, scoped secret/policy authority,
      durable audit, HA, backup/restore, and upgrade/promotion evidence. Track implementation in the
      existing Enterprise and Orchestrator sections; this item proves the joined hosted prerequisite.

      **Done (v0.18.0).** All eight were already implemented — the gap was that nothing proved them
      *together*. The Enterprise profile certification lane covered three (policy authority, OIDC,
      HA fencing); the other five had passing tests wired into no lane, and upgrade/promotion was
      proven only inside the `Upgrade` and `TeamToEnterprise` *transition* lanes. A hosted claim
      therefore required correlating three lanes by hand, which is the inferred claim this framework
      refuses everywhere else.

      `Test-DeploymentProfileCertification.ps1 -Profile Enterprise` now runs twelve phases tagged
      with the eight prerequisites, emits a `hostedPrerequisites` array in `certification.json` and a
      table in `certification.md`, and **fails the lane naming any prerequisite that is unproven** —
      including one whose phases never ran because an earlier phase broke the loop. It also now
      requires the concrete `EnterpriseUpgrade` scenario evidence rather than accepting a green
      phase. `DeploymentProfileCertificationScriptContractTests` pins the list, the enforcement, and
      that every declared prerequisite is actually attached to a phase, so the lane cannot quietly
      narrow what "Enterprise certified" means.

      Verified by running the real lane (2026-08-09, commit `bfcae8ac`): 13/13 phases passed and all
      eight prerequisites reported `True`. That run was against a dirty worktree, so it correctly
      reported `releaseEligible = False` — it is development evidence, not a release claim. The lane
      now needs **Docker** because it exercises shared PostgreSQL providers, matching the
      Team-to-Enterprise precedent; the release checklist says so.

      Two side findings: `certification-results/deployment-profiles/` was not gitignored, unlike its
      sibling evidence directories, so every local run left a commitable pile — now ignored. And
      `OrchestratorPerObjectAuthorizationIntegrationTests` is tagged `Category=Integration` despite
      using only an in-process `OrchestratorWebFactory`, so this security boundary is excluded from
      the standard CI run. Not changed here — retagging it is a test-lane decision, not a
      certification one — but it is why the prerequisite needed an explicit lane phase.
- [ ] Deliver the minimum tenant portability bundle and SaaS → self-hosted Enterprise journey before
      Managed Dedicated SaaS general availability. The
      [Tenant Portability Architecture](docs/architecture/TenantPortability.md) owns the bundle and
      migration contract; this gate owns its release sequencing, and domain 9 owns its delivery.

#### Isolation domains

Each domain states its **Dedicated** obligation and its **Shared** obligation, plus the Enterprise
contract it builds on where one exists. An entry is complete only when the matching matrix cell
carries a current linked evidence reference and the release review records the topology explicitly,
the way [v0.18.0](docs/releases/v0.18.0-deployment-profile-review.md) recorded its review. Do not
infer Dedicated support from an Enterprise happy path, or Shared support from Dedicated evidence.

##### 1. Tenant context and authority

- [ ] **Dedicated.** Derive tenant context server-side even where the tenant has its own deployment
      boundary. A deployment per tenant makes cross-tenant reach unlikely, not impossible: the
      provisioning control plane, platform automation, and support tooling all still span tenants,
      and each is an entry point that can be handed a caller-supplied identifier.
      **Gap — Phase C carried this alone, as though a dedicated boundary settled the question.**
- [ ] **Shared.** Prove tenant context is server-derived at every shared entry point — a negative
      test per surface that a caller-supplied tenant, alias, gateway, resource, run, object, or
      storage identifier cannot widen scope, plus collision tests for equal numeric/logical IDs
      across tenants.

##### 2. Identity and delegated administration

- [ ] **Dedicated.** Establish platform/tenant identity separation and delegated tenant
      administration, and prove platform administration is separately audited and cannot implicitly
      impersonate a tenant user even when the tenant has its own deployment boundary. Supports one
      tenant-owned IdP configuration through the Enterprise identity contract.
- [ ] **Shared.** Extend identity and delegated administration to shared stores with tenant
      predicates/partitioning enforced below controller code. Add dynamic, server-verified
      tenant/issuer/domain discovery without trusting a caller-selected tenant or issuer, and
      without allowing platform administrators to impersonate tenant users.

*Absorbs the retained discovery item **SaaS Multi-Tenant Identity (Multi-IdP)**.*

##### 3. Policy, secrets, and keys

- [ ] **Enterprise contract first.** Establish one provider-neutral key contract and refactor
      `DatasetAtRestKeyValidator.cs`, dataset, credential, artifact, and checkpoint encryption away
      from a single global master key. Resolved keys never enter portable exports or execution images.
- [ ] **Dedicated.** Tenant-specific policy authority with platform/tenant separation, so one
      tenant's policy cannot be authored or overridden from platform scope. Disjoint tenant
      provider/key namespaces plus export proof: no cross-tenant key reuse, raw secret export, or
      provider credential in an execution artifact.
- [ ] **Shared.** Extend policy, connections, secrets, keys, and catalog bindings to shared stores
      with tenant predicates/partitioning enforced below controller code, and prove tenant, key, and
      key-version separation.

*Absorbs the retained discovery item **Tenant-Scoped Encryption Keys (BYOK)**.*

##### 4. Storage, paths, and artifacts

- [ ] **Enterprise contract first.** Extend the existing `ResolvePath` boundary into
      provider-neutral, server-derived tenant storage capabilities for file/directory connectors and
      operations such as `FLATFILE`, `DIRECTORY`, and `SEND FILE`.
- [ ] **Dedicated.** Tenant-specific artifact roots and object prefixes, with canonical paths,
      symlinks, archives, caches, checkpoints, and spill all remaining inside the authorized
      tenant/run root. Do not treat `chroot` or a container filesystem alone as authority.
      **Gap — previously implicit inside the quality bullet's trailing "and artifact roots".**
- [ ] **Shared.** Server-derived storage identifiers with a negative test that a caller-supplied
      object, prefix, or path identifier cannot widen scope, and no reuse of volumes, directories,
      object prefixes, or encryption data keys across tenants or sandbox assignments.
      **Gap — no phase bullet covered shared storage scope.**

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

- [ ] Add the topology qualifier to the capability matrix itself, so a Dedicated pass cannot render a
      Shared cell Green. Per-profile and per-transition release claims are tracked under **Platform —
      Deployment Profiles and Upgrade Certification → P2** above.
- [ ] Relabel the current Tenant-isolation implementation-Green evidence as **Managed Dedicated
      only**, attach clean commit-bound topology evidence, and prevent it from satisfying Shared SaaS
      cells.
- [ ] Move Shared Tenant isolation from Red to claim-Green only with clean commit-bound hostile
      cross-tenant evidence across database, artifact, cache, queue, audit, PII, lineage/quality,
      path, key, checkpoint, Gateway, sandbox, telemetry, support, restore, and resource-exhaustion
      surfaces.

### Language — Dialect Standardization and Drift Prevention

The five deliverables below implement the portability contract in
[ROADMAP.md](ROADMAP.md#language--dialect-standardization-and-drift-prevention).

- [ ] Publish a machine-readable canonical EBNF grammar for the accepted ETL-SQL language, with working
      examples for every documented syntax form and an explicit process for keeping it synchronized with
      `Parser.cs`.
- [ ] Expand the shared SqlLogicTests corpus under `tests/slt_data/` to cover exact results, boundary
      behavior, mathematical/date offsets, standard-library functions, and representative cross-dialect
      cases.
- [ ] Add the syntax-addition checklist to `CONTRIBUTING.md`: parser/runtime, EBNF, docs/help/snippets,
      lint/autocomplete, connector pushdown mappings, compatibility, and regression tests must move
      together.
- [ ] Build an EBNF-to-parser conformance runner that generates valid and invalid sequences and proves the
      execution parser accepts/rejects them consistently. Keep this in its own deterministic fuzz/release
      lane rather than slowing smoke or fast tests.
- [ ] Move provider-specific SQL rewrites out of `QueryCompiler` and scattered connector code into a
      centralized, registered dialect abstraction with focused translation and unsupported-feature tests.

### Connectors — Transactional File Staging

- [ ] Define and implement the `TRANSACTIONAL=TRUE` connector contract, including parser/help/snippet
      coverage, collision-safe engine-owned staging names, canonical `ResolvePath` enforcement, and the
      connector types that can truthfully support atomic publication.
- [ ] Commit completed output by atomic rename where the destination guarantees it; otherwise fail
      preflight or use a documented provider-specific commit protocol rather than claiming false
      atomicity.
- [ ] On failure, cancellation, retry, or process loss, remove or reconcile staged artifacts without
      deleting a previously published target. Define checkpoint/resume and multi-output behavior
      explicitly.
- [ ] Certify local files and supported remote-transfer connectors for success, mid-stream failure,
      cancellation, overwrite policy, concurrent writers, cleanup failure, path/symlink escape, and
      crash residue. Keep network-backed certification in the integration/release lanes.

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

- [x] Establish the real distribution before changing any number. Run the Portal and Orchestrator
      lanes N times under deliberate load and record, per waiting test, the observed time-to-satisfy
      against its configured budget. Set budgets from that data rather than by doubling until green.
- [x] Replace bare deadlines with a shared load-aware helper — the `WaitUntilAsync` idea extended
      with a budget that scales from a baseline measured once per run, so a saturated agent and a
      developer laptop do not need the same constant.
- [x] Make a budget expiry **report what it was waiting for**. Every one of these cost an
      investigation mostly because the failure said "expected true, got false" rather than "waited
      15s for the host to stop; last observed state X". Diagnostics on timeout are the highest-value
      part of this item.
- [x] Extend the guardrail to flag a *new* bare wall-clock budget in a waiting helper, so the class
      cannot quietly regrow once retired. Same annotation escape hatch as the existing check.
- [x] Decide whether `HostedServiceLaneTests` should stay in the shared lane at all. It is the one
      Portal suite that starts the full `IHostedService` pipeline, making it both the slowest to
      reach a decision and the most sensitive to surrounding load — a separate lane may be a more
      honest fix than a larger number.
- [x] Retire the three tracking documents into one when the class is closed, and record the outcome
      the way the [stability record](docs/releases/flaky-test-stability.md) records its fix, so the
      reference pattern stays findable.

**Done (v0.19.0):** `LoadAwareWait` now owns bounded observable waits, diagnostics, bounded
per-process load calibration, and optional JSONL evidence across the known engine, Orchestrator,
Portal, Docker, and language-server offenders. Three loaded repetitions of both sensitive lanes
passed with the worst 15-second condition completing in 1.05 seconds; the baselines were retained.
The static guard rejects new bare deadline helpers, scheduler tests use isolated throttle stores,
signed subscription triggers exercise the authorization boundary, and the full Portal hosted-service
pipeline runs in its own `portal-hosted` process. The consolidated policy and evidence are in
[Flaky Test Stability](docs/releases/flaky-test-stability.md).

**Do not** simply add retries. A retry hides a genuine intermittent product defect exactly as well
as it hides a test-harness one, and the shared-SQLite finding in the v0.18.0 notes — where the
browser lane's failure turned out to describe a real production sharing hazard — is the reason to
keep failures visible.

## Bugs


- [x] **Create a working cookbook recipe for Lineage**  It should be two parts, part one should be data
  from a flat file into a database with some transformations in the middle.  This is an EDW load.  This
  should also include an export of the Lineage.  Part two would be importing the lineage and taking the 
  table from EDW and making a report from it.  Everything should work and the lineage on the report should
  show the flat file source to the EDW to the report and all transformations that happen between.

      **Done (v0.18.0).** [End-to-End Lineage](docs/cookbooks/etl/end-to-end-lineage.md), backed by two
      runnable samples under `samples/04_Orchestration/` that the sample suite exercises. SQLite, so
      it runs with no infrastructure.

      Writing it as a *runnable* recipe rather than a written one found five defects that reading
      could not have: the documented tag syntax `expr /* @d: … */ AS alias` was a parse error in
      every form; `eng.lineage` dropped the transformation on every renamed column (the display
      dedup preferred the observation carrying the physical path and discarded the one carrying the
      classification — so the columns most likely to *have* a transformation were exactly the ones
      that lost it); a `+` chain containing a string literal was classified as arithmetic because
      the heuristic only inspected the immediate operands of a left-associative parse; the
      OpenLineage round trip returned a different `transformation_kind` than it was written with,
      because our 12 kinds map many-to-one onto OpenLineage's subtype vocabulary; and re-recording a
      hop never filled in the physical identifier the first (pre-connection) observation lacked.

- [ ] **Sweep the samples that fail.** 16 of 195 remain after the first triage cluster — see the
      sample-runner item under the release-process RCI section for why this was invisible until now.
      Each needs triaging individually: some will be stale syntax, some may be real engine defects.
      Run `pwsh -File scripts/Test-AllSamples.ps1 -Passes 2` for the current list.

      As of 2026-08-09: `01_deploy_datasets`, `02_report_public_consumer`,
      `03_report_private_allowed`, `04_report_private_denied`, `05_export_then_publish`,
      `append_to_parquet`, `backup_and_report`, `capacity_report`, `daily_failure_digest`, `ddl_dml_sink`,
      `diagnostics_ssh_sink`, `flatfile_sink`, `golden_workflow.rptsql`, `parameterized_exec_test`,
      `variables_config_sink`, `window_sink`.

      First triage cluster completed: `Batch_Processing` exposed missing native spill support for
      UUID columns; `Docker_Aliases` mixed a misspelled stop target with resume semantics; and
      `Data_Quality_Rules` is deliberately fail-closed, so the sample runners now require its exact
      expected exit code and assertion message. Validator session/outbox state is isolated from the
      user's machine state so an interrupted run cannot manufacture unrelated startup failures.

      Two idempotency failures found by the second pass are already fixed:
      `Sqlite_Operations.etlsql` (fixed primary keys into a persistent database) and
      `register_schedule.etlsql` (`CREATE SCHEDULE`/`CREATE JOB` into the persistent Orchestrator
      store — succeeds once, fails every time after). Both now start from a known state. Expect more
      of this shape: **any sample that writes to a store outside its own session has to be
      idempotent**, and until now nothing checked.
