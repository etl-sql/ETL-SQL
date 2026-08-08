# ETL-SQL Development TODO List

Use this list as the execution ledger for active-release and roadmap work. Once work is verified,
record its notable outcome in `CHANGELOG.md` and mark the task complete with `- [x]`; do **not**
remove it. `ROADMAP.md` remains the high-level product-direction source, and its initiatives are
decomposed into actionable tasks here. Checked entries are retained so progress remains reviewable.

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

### Portal asset caching — the `?v=` cache-busters were inert, and nothing was cacheable

**The stale-JavaScript hazard originally recorded here does not exist, and the correction matters
more than the original claim.** I asserted that an upgraded Portal would serve `api.js?v=0.17.0`
from the browser's cache and run 0.18.0 against 0.17.0 JavaScript. Checking the response headers
rather than reasoning from the URLs showed the opposite: a middleware appended
`Cache-Control: no-store, no-cache, must-revalidate, max-age=0` to **every** response, static assets
included. A browser forbidden to store a response cannot serve a stale one, so there was never
anything for a cache-buster to bust — the 71 hand-maintained `?v=` strings had no effect at all.

The real finding was the other way round: `no-store` on the asset roots meant **every page
navigation re-downloaded about 3.4 MB**, roughly 1.9 MB of it vendored libraries (`echarts`,
`tabulator`, `arrow`, `chart`) that had not changed since install.

- [x] Split the cache policy by what a response is. Documents and API responses stay `no-store` —
      they carry catalog, identity and report data. The asset roots (`/js/`, `/css/`, `/designer/`,
      `/img/`, `/maps/`) get `no-cache, must-revalidate`, which permits storage and requires
      revalidation, so a request costs a 304 rather than the file. Staleness risk is nil: an
      upgraded Portal returns a new ETag and the browser refetches.
- [x] Remove the 71 inert `?v=` strings. They implied a mechanism that was not there, which is the
      same shape as the other defects this release turned up.
- [x] `StaticAssetCachingTests` pins **both** halves, because each is a mistake someone could make
      in good faith: widening `no-store` back over the assets to be safe, or relaxing the documents
      to make the app feel faster. It asserts a real conditional request returns `304`.

`Set-Version.ps1` therefore does not need to touch `wwwroot`, and no version-agreement test is
needed. If long-lived caching is ever introduced, that decision brings back the need for
fingerprinting — derive it then rather than hand-maintaining copies.

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

- [x] Add `scripts/Test-MsiUpgrade.ps1 -PreviousMsi <path> -CurrentMsi <path>` asserting the full
      sequence, not just the registry:
      1. install previous → exactly **1** uninstall entry at the previous version
      2. write a sentinel file into `InstallLocation`
      3. install current **over** it
      4. **exactly 1 entry, at the new version** — two entries is the side-by-side regression
      5. sentinel survived → config/data preserved
      6. installed `ETL-SQL.exe --version` reports the new version
      7. uninstall → 0 entries
- [x] Steps 5–6 matter: a registry-only assertion passes while files are clobbered or
      `RemoveExistingProducts` is mis-scheduled, which is precisely what "preserves config/data" in
      the checklist is asking about.
- [x] Add a CI job gated to `release/**` pushes and tags (not every PR — the previous release MSI is
      ~900 MB). Resolve the previous tag with `gh release list`, download with
      `gh release download <tag> --pattern '*-x64-Setup.msi'`, and cache it keyed on the tag.
      `.github/workflows/msi-upgrade.yml`, triggered on `release/**` and `v*`.
- [x] Delete the manual step from [release-checklist.md](docs/releases/release-checklist.md) Phase 4.
      Already gone.
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

- [x] **Run scale certification before the long test lanes**, or quiesce the machine first. The
      release gate now orders scale certification ahead of thermally noisy long-running lanes.
- [x] **Add a same-worktree A/B mode** for comparing two commits, so version comparisons cannot be
      contaminated by comparing two directories in different thermal states — the exact error that
      produced the v0.17.0 false alarm. **Already built**: `scripts/Test-ScaleCommitComparison.ps1`
      resolves two refs, alternates detached checkouts in one working directory, rebuilds and
      discards a warm-up per arm, and restores the original checkout in a `finally`. Verified with
      `-PlanOnly`, which reports a properly counterbalanced sequence (`A,B,B,A,A,B`) rather than
      naive alternation — the ordering detail that actually defeats thermal drift.
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

### Completed work retained for progress review

#### Workstation-to-Enterprise quality and stewardship

- [x] Add source-controlled workspace policy with required tags, PII suggestion rules, and quality
      thresholds, including its JSON schema and example.
- [x] Add CLI-native quality summaries, structured JSON evidence, and non-zero quality-gate exits.
- [x] Add local and remote `eng.data_quality_status` and `eng.data_quality_failures` read models over
      structured run history.
- [x] Add the local-Orchestrator historical quality loop with baselines and SMTP/WEBHOOK recovery
      notifications without requiring Portal.
- [x] Add `etlsql scan --pii`, protected-data suggestions, stewardship gaps, and reproducible
      stewardship component scores backed by one shared scoring service.
- [x] Ship source-controlled Data Quality Health and Stewardship Scorecard reports plus the
      one-person quality-loop guide and runnable sample.
- [x] Add workstation, local-Orchestrator, Portal, Enterprise, and SaaS parity/security fixtures for
      quality, policy, scoring, and tenant isolation.

#### Deployment profiles, promotion, and upgrades

- [x] Establish the Solo, Team, Enterprise, and SaaS capability/portability contract and profile
      coverage matrix.
- [x] Add secret-safe promotion inventory, preflight, export/import, target binding, collision
      detection, scheduler fencing, cutover proof, and rollback guidance.
- [x] Implement Solo → Team, Team → Enterprise, Enterprise → SaaS, and direct Solo → SaaS onboarding
      while preserving portable state and proving tenant isolation.
- [x] Add N → N+1 upgrade and transition lifecycle drills with backup, restore, continuity,
      rollback, schema migration, and scheduler fencing evidence.
- [x] Add selectable deployment-profile certification lanes and journey fixtures for Solo, Team,
      Enterprise, SaaS, transitions, quality/stewardship enforcement, and isolation.

#### Portal foundations and coherent workspaces

- [x] Generate and enforce critical browser/API response contracts and fix Admin Users `username`
      casing drift.
- [x] Use one recognizable Portal session identity model across Reports, Admin, Docs, and
      Orchestrator.
- [x] Remove production governance demo evidence and route users only to durable Quarantine and
      Lineage surfaces.
- [x] Make parameterized first-run execution one preflight/Run flow with terminal polling,
      prerequisite-gated actions, and accessible report controls.
- [x] Add a modal responsive global navigation drawer and shared narrow-viewport table, form, tab,
      action, Docs, and Orchestrator patterns.
- [x] Add explicit `Disabled`, `CatalogOnly`, and `SourceControlled` Studio modes with deny-by-default,
      action-specific authoring capabilities and server-side route fences.
- [x] Build the consumer home with favorites, recent, featured, popular, fuzzy global search,
      intentional icons, and one concise report activity status.
- [x] Build a catalog-scoped Studio home with equal Code and Design modes.
- [x] Enforce catalog-only Studio trust boundaries and remove authoring navigation and APIs when
      authoring is disabled.

### Platform — Deployment Profiles and Upgrade Certification

#### P2 — Deployment-profile certification

- [ ] Retain commit-bound JSON and Markdown certification evidence with topology, hashes, mappings,
      continuity counts, negative isolation results, and rollback outcomes.
- [ ] Add current per-profile and per-transition evidence to release claims.

### Portal — Comprehensive Product and UX Update

Follow the roadmap's suggested order: shell/contracts, consumer flow, Studio authorization and
authoring, responsive/accessibility foundations, governance/operations/environments, docs/designer,
documentation reconciliation, then release certification.

#### P0 — Restore trust in critical journeys

- [x] Fix Admin Users casing drift and enforce the generated browser/API contract.
- [x] Add a real login → users → folders → publish/run browser test. `tests/ETL-SQL.Portal.BrowserTests`
      drives Chromium against a Kestrel-hosted Portal (`test-lane.ps1 -Lane browser`, `Category=Browser`,
      opt-in because it downloads Chromium). It found two defects on its first two runs: the forced
      first-run password change signed the user into an already-invalidated session and bounced the
      user back to login with no explanation, and the catalog's view transitions left unhandled
      promise rejections on the page. Both are fixed.

#### P1 — Coherent workspaces

- [x] Build an Administration/Operations hub for service accounts, approvals, share/embed inventory,
      fleet status, metrics, and administrative service runs.
- [x] Add an isolation-safe Environments workflow that generates and validates deployment plans.
      `GET /api/admin/environments/plan?environmentId=&portBase=` derives every isolated resource,
      port and key requirement from the environment id per Departmental_Isolation.md §3–§4 — which is
      what makes a plan checkable rather than a document someone has to follow carefully.
      `POST /api/admin/environments/validate` checks it against this Portal's own environment, the
      environments named for fleet visibility, and the machine registry. Any shared resource is a
      collision, not a warning: sharing one is enough to break isolation.
  - [x] Keep provisioning in a separately authorized control plane or exported package. The Portal
        generates plans and never applies them — an environment able to provision another is not
        isolated from it — and the plan states that boundary in the artifact rather than leaving it
        to the reader. Plans are also secret-free: keys are requirements at named configuration keys,
        never generated and never valued, so a plan is safe to review, store, and hand over.
  - [x] Prove environment switching establishes separate sessions and never merges catalogs,
        datasets, connections, secrets, or authoring history. `EnvironmentIsolationTests` runs two
        deployments and proves catalogs and search do not merge, a resource id from one is meaningless
        in the other, and a token minted in one is refused by the other while still working where it
        was minted.
- [x] Complete Stewardship and Audit routes using durable evidence.
- [x] Connect disposition/replay submissions to terminal job status.
- [x] Add data-quality rule visibility and structured failure trends.
- [x] Use one sanitized Markdown renderer for Docs and connector Help.
- [x] Replace native browser alerts/prompts/confirms with accessible, auditable feedback and dialogs.
- [x] Improve designer palette discovery, action hierarchy, toolbar labels, empty states, and
      laptop/tablet layouts.

#### Studio authorization and controlled publishing

- [x] Add an `Author` resource grant that cannot alter ACLs, move/delete reports, or administer a
      folder. An Author may rewrite a report's script, content and metadata and run it; they may not
      move it, delete it, publish a new report into the folder, create share links or embed tokens,
      or touch any ACL.

      The hazard this had to get past: `FolderPermission` values are stored as integers in every ACL
      row, so `Author` had to be **appended** as 3 rather than inserted between `Execute` and
      `Manage` — inserting would have renumbered `Manage` and silently reinterpreted every grant
      already in force. That makes declaration order a lie about authority, and the ~40
      `>= FolderPermission.Manage` comparisons would each have handed `Author` everything `Manage`
      has. Worse, four integer `Max` operations picking the strongest of several grants would have
      chosen `Author` over `Manage` and *downgraded* anyone holding both.

      Fixed by separating storage value from authority order: `FolderPermissions.Rank()` places
      Author between Execute and Manage, `AtLeast()` replaces every ordinal comparison, and
      `Max()` replaces every integer max. Done in two phases — a behaviour-preserving conversion
      verified against the full suite first, then the deliberate grants one gate at a time.
      `FolderPermissionOrderingTests` pins the stored values and fails the build if any production
      file compares permissions ordinally again, since writing `>=` here is the natural thing to do
      and silently escalates.

- [x] Implement deny-by-default, group/service-account-assignable Studio capabilities. All nine are
      defined, deny-by-default, and enforced by the `RequireStudioCapability` filter on every gated
      route (`SourcePush` is checked inline in `ReportsController`). Capabilities are now assignable
      as well as configurable:
  - `GroupStudioCapabilities` grants them to a group;
      `GET`/`PUT /api/admin/groups/{id}/studio-capabilities` manages the set and rejects an unknown
      name rather than storing a typo that would read as a grant and do nothing.
  - Grants are resolved at sign-in and refresh and carried as `studio_capability` claims, so the
      per-request check stays a claim lookup. Changing a group's capabilities invalidates its
      members' sessions, the same way an ACL change does.
  - Service accounts carry their own set, **capped by the owner's** at token issue — mirroring how
      roles are capped — so an account can never exceed the authority of the person who created it.
  - [x] `StudioAccess` for discovery/open.
  - [x] `ScriptRead` for source access.
  - [x] `ScriptPreview` for analysis/completion/rendering.
  - [x] `ScriptRun` plus existing shared-connection ACLs for interactive execution.
  - [x] `ScriptSave` for drafts without implicit publish/commit/push.
  - [x] `ReportPublish` for active-version publication.
  - [x] `ScriptIngress` for external upload/import, disabled in catalog-only SaaS.
  - [x] `SourceCommit` with actor, revision, diff summary, and correlation id.
  - [x] `SourcePush` or deployment-service authority, separate and disabled by default.
- [x] Include Studio capabilities in effective-permission diagnostics and mutation audits.
      `GET /api/admin/permissions/effective/user/{id}` now reports roles, the Studio deployment mode,
      and the capabilities those roles resolve to — reporting grants without the mode would overstate
      what a user can do when Studio is off. `RequireStudioCapability` stamps the capability that
      authorized the request and `AuditService` records it on the audit row, its outbox message, and
      the outbox payload, so reviewing a Studio mutation does not mean inferring authority from the
      route.
- [x] Add a Viewer/Author/Publisher/Approver/Admin authorization matrix test suite.
      Viewer, Publisher, Admin and all four ACL levels including `Author` are covered by
      `AuthorizationMatrixTests`, written as data so a widened grant fails a `denied` row and a
      narrowed one fails an `allowed` row.

      **Approver is a capability rather than a role**, so its rows live with the workflow they
      govern, in `ReportDraftWorkflowTests`: approving requires `ReportApprove` (asserted both ways —
      the positive row alone would prove approval works without proving anything stops it), and an
      approver cannot publish, because reviewing a change and shipping it are separate authorities
      an organization needs to be able to give to different people.

      Writing it recorded two properties of the model: authorization is **two-dimensional** (a role
      decides the class of operation, an ACL decides the resources), and folder `Manage` is authority
      over the reports in a folder, not over the folder itself — without which the strongest grant
      would be self-propagating.

      Writing it established two things worth recording. Portal authorization is
      **two-dimensional**: a role decides which *class* of operation you may perform, an ACL decides
      which *resources* you may perform it on, and the axes are not interchangeable. And holding
      `Manage` on a folder deliberately does **not** let you read or re-grant its ACL, create a
      subfolder, or delete it — those are Admin-role acts. Without that split the highest ACL grant
      would be self-propagating: whoever held it could hand it out, and the set of people with
      access could only ever grow. That boundary was previously discoverable only by reading ~40
      enum comparisons plus scattered `[Authorize(Roles=…)]` attributes; it is now asserted.
- [x] Add draft → review/approval → publish/commit/push with optimistic concurrency, protected
      branches, and separation of duties.
      **Draft → review → publish is done**, opt-in behind `Portal:Studio:RequireApprovalToPublish`
      (default off, so an upgrade never interposes a review step into a workflow people depend on).
      A draft holds the proposed script in the database rather than the artifact store — a draft is
      not yet a script, and nothing should execute, serve, or list it beside real ones.

      Separation of duties is absolute: an author can never approve their own draft, whatever
      capabilities or roles they hold, **including Admin**. A four-eyes control the most privileged
      account can bypass fails exactly when it is needed, since the account that gets compromised or
      leaned on is the privileged one. Editing a draft revokes any approval or pending review, so a
      reviewer's name can never end up attached to content they did not see, and an approval against
      a base the live script has moved past is refused rather than silently discarding the change in
      between. New `ReportApprove` capability, separate from `ReportPublish` so reviewing and
      shipping can go to different people.

      **Protected branches are done too**, and they are what makes the review worth having.
      `Portal:SourceControl:ProtectedBranches` (empty by default, exact names or a trailing `*`)
      names branches a Portal-originated commit may not land on without an approved draft behind it.
      Protecting a branch without a review path only blocks people; providing a review path without
      protecting anything only asks nicely.

      The reviewer is written into a `Reviewed-by:` commit trailer alongside the script hash, so the
      review outlives the Portal's database — someone auditing the branch later reads it from
      `git log` rather than needing the Portal to answer. Provenance is matched on the *published
      hash*, not on recency, so a draft that was approved but never published cannot lend its
      approval to whatever happens to be on disk. Refused commits are audited as
      `COMMIT_REPORT_SCRIPT_DENIED`: an attempt to put an unreviewed change on a protected branch is
      exactly the event an operator wants to see, and a bare 409 would leave no trace of it.

#### Enterprise administration coverage

- [x] Add identity-provider diagnostics for reachability, claims/groups, sync health, and break-glass
      readiness without exposing client secrets.
      `GET /api/admin/identity/diagnostics` reports OIDC reachability and startup validation findings,
      LDAP configuration, the claim value each provider-managed group expects, how many federated
      users are in no mapped group, and whether any active **local** administrator could sign in with
      the provider unreachable. Configured secrets are presence flags; a test asserts the configured
      secret value appears nowhere in the whole response.
  - `POST /api/admin/identity/diagnostics/group-mapping-test` resolves claim values against the
      mappings without anyone signing in, and names the unmatched ones — sign-in working while
      authorization silently does not is the failure this catches.
- [x] Add a Service Accounts page with scope, expiry, last use, owner, rotation/revocation, one-time
      secret display, and audit history.
- [x] Extend Policy Authority with fleet impact, approval state, collector consequences, and machine
      links to policy history. `GET /api/admin/policy-authority/impact?tenant=&environment=&version=`
      answers the question asked immediately before pressing activate: **what happens when I do?**
      Policy Authority had every verb — validate, publish, activate, canary, roll back — and no
      consequence.
  - **Fleet impact** separates registered from reachable: a machine not seen for over 24h will not
      pick the policy up until it checks in, so a large stale count means the rollout is narrower
      than the fleet count suggests.
  - **Approval state** distinguishes a recorded reviewer from a second pair of eyes — a version whose
      reviewer *is* its author is reported as unreviewed in substance.
  - **Collector consequences**: activating a policy that requires remote audit delivery against an
      unhealthy collector starts refusing mutations with HTTP 503. Both halves were already known
      (the policy states its requirement, `AuditDeliveryGate` states deliverability); this joins them
      so the answer is not discovered by activating.
  - **Machine links**: each machine is listed with the version it actually receives — the canary
      version when it is in the targeted group, the active one otherwise, `none (revoked)` when it
      has been revoked.
- [x] Show host enrollment/registration consistency, expiry, certificate posture, and remediation;
      keep enrollment/unenrollment on the host. In `GET /api/admin/operations/posture`: the host's own
      enrolment compared against the Portal machine registration (tenant and enrollment-id drift, a
      revoked registration, a host enrolled but never registered — each side looks healthy alone,
      which is the point of comparing them), client-certificate thumbprint match and expiry with an
      advance warning, and remediation that names the **host** commands, because enrolment owns an
      OS-protected bootstrap deliberately outside lower-authority Portal configuration.
- [x] Integrate secrets/connections with Studio checks, policy findings, rotation dates, and promotion
      plans. `GET /api/admin/credentials/posture` resolves secrets and shared connections **against
      each other**: which connections reference which secrets, which references do not resolve, when
      each secret was last rotated, which secrets nothing references, and which the target of a
      promotion would have to be supplied.
  - The failure it exists for is invisible on either page alone: a connection referencing a secret
      that was renamed, disabled, or never created looks healthy in both lists and fails the first
      time something runs.
  - No secret value is read to build it — references are matched by name, because resolving them
      would mean decrypting every secret to render a page.
- [x] Add audit/security collector health, queue metrics, fail-closed state, and redacted test delivery.
      `GET /api/admin/audit/collector` reports queue depth, queued bytes, oldest pending age, terminal
      failures, last attempt/success/error, and the thresholds a reading is compared against — the
      signals existed in health/Prometheus/fleet status, which is fine for a dashboard and no use
      mid-incident. Fail-closed state is produced by calling `AuditDeliveryGate` itself, so what is
      reported is what would actually happen to the next mutation.
  - `POST /api/admin/audit/collector/test-delivery` posts a synthetic event through the real
      delivery path (same endpoint resolution, auth, and body shape — a probe with its own path
      proves only the probe). It carries no audit content, echoes the endpoint without its query
      string, redacts failures, and is itself audited.
- [x] Add native service enablement, schedule, recipient, last/next run, outcome, and history views.
- [x] Show backup freshness and validation/restore-drill evidence while keeping custody and recovery
      outside the running Portal. `GET /api/admin/operations/posture` reports the last backup outcome,
      its age against the configured freshness policy, and — new — restore-drill evidence:
      `etl-sql admin restore`/`--validate` now records its outcome under job-state `admin-restore`,
      mirroring what backup already did. A backup nobody has ever restored is a hope rather than a
      recovery plan, so "never proven readable" is reported as a finding instead of a blank. Custody
      and the restore itself stay on the host; only the evidence travels, and every finding names the
      command that fixes it.
- [x] Add online-safe diagnostics and an audited, redacted, review-before-download support bundle.
      `GET /api/admin/support-bundle/review` returns every section as a reviewable document — health,
      deployment identity and versions, migration state, catalog **counts**, audit-outbox state, and
      the redacted Portal configuration — with the redaction note and an explicit list of what it
      leaves out. `GET /api/admin/support-bundle` downloads it. Both are audited.
  - Safe to expose because it collects counts, versions and states rather than content, and all text
      passes through the redactor. Tests assert a report's name and title, the JWT secret, and the
      dataset at-rest key are all absent from the whole response.
  - The redaction rules moved to `ETL_SQL.Core.Common.SupportBundleRedactor` and the CLI builder now
      delegates to them. Two hosts producing support material from two nearly-identical rule sets
      would yield two artifacts that look equally safe and are not.
  - `?acknowledgedContent=<hash>` **409s** when the disclosure changed after review. The hash covers
      the deployment and configuration, not live counters: reviewing audits the review, which moves
      the outbox counts the bundle reports, so hashing everything would make each review stale the
      instant it was made and the check would become noise an operator learns to bypass.
  - The CLI `admin support-bundle` remains the recovery path when the Portal is unavailable — it
      reads host files and configuration the Portal cannot.
- [x] Add a read-only Fleet/Operations workspace with compatibility, divergence, drain, migration,
      and upgrade evidence. `GET /api/fleet/workspace` (FleetReader or Admin) polls every configured
      environment at once and returns the merged report — compatibility metadata, policy/config/version
      divergence findings, migration state, grouping and filtering — plus an upgrade **preflight or
      postflight** report.
  - `FleetHealthAggregator` had been built but had nothing to aggregate: no configuration named the
      environments, so it was machinery with no way in. `Portal:Fleet:Environments` is that way in.
  - Naming an environment grants **visibility, never authority**: one scoped read-only GET per
      environment and nothing else. Per-environment tokens are never echoed, only counted, and an
      unreachable environment is reported as unreachable rather than failing the whole view — a
      partial outage is exactly when the view is needed.
- [x] Add guarded dataset-key inventory, rotation preflight/progress/verification, and rollback
      guidance without displaying key material. `GET /api/admin/datasets/at-rest-key/posture` reports
      the per-version inventory, what rotation would do, what it has done, and what to do if it goes
      wrong. Rotation itself remains `POST datasets/rotate-at-rest-key`.
  - **Preflight is the point.** A cache encrypted under a version whose key is no longer configured
      can be neither rotated nor read, and the only way to discover that was to start the rotation
      and read the failure list. Blocked datasets are now named beforehand, with the reason.
  - Key *versions* are non-secret identifiers and are named; key **material** never appears — a key
      is reported as configured or not, and a test asserts the configured key value is absent from
      the whole response.
  - Progress is the rotation result itself: rotation stamps each cache as it goes, so it is
      resumable rather than transactional, and re-running it retries only what has not moved. That
      property is what the rollback guidance is built on.
- [x] Add guided secret-free configuration export, target-plan validation, diff, approval, and audit.
  - `GET /api/admin/configuration/export/plan` returns what leaves this Portal, what will not, and
      what must be moved separately — **without** the script body. The export endpoint already
      computed all of it and put it in the audit line only, so the only way to learn what an export
      omitted was to read the file.
  - `POST /api/admin/configuration/validate` now returns a per-resource plan of `Create`/`Match`/
      `Collision` alongside its findings. Findings carry only collisions, because that is what needs
      a decision; a plan needs the whole picture, or an operator cannot tell an empty target from an
      identical one.
  - Approval is a plan hash: `export?acknowledgedPlan=<hash>` **409s** when the configuration changed
      after review, so review is binding when used rather than advisory. The hash is derived from the
      plan contents, not the script text, so cosmetic churn does not invalidate a review. The audit
      records the acknowledged plan, or that none was.
- [x] Add an access simulator explaining roles, groups, ACLs, connection grants, Studio capability,
      and RLS outcomes without returning protected rows.
      `GET /api/admin/access-simulator/user/{id}?reportId=&datasetId=` composes every authority into
      one answer **with its sources**, so "why can they still see this?" is answerable from one place
      rather than by checking five surfaces and composing them by hand.
  - Row-level security is explained by naming the identity tokens the script filters on
      (`RowLevelSecurityScan.IdentityReferences`) and the values that would be bound — the report is
      never run, and a test asserts no data from it appears in the response. A tool for auditing who
      can see data must not become a way to see it.
  - The report answer and its explanation both come from `FolderPermissionService`, so the
      diagnostic cannot drift from the enforcement it describes.
  - Reading someone else's effective access is itself audited (`SIMULATE_ACCESS`).
- [x] Verify the Environments workflow preserves separate departmental processes, databases,
      artifacts, key rings, identities, and endpoints. `GET /api/admin/environments/current` reports
      this environment against the isolation contract and links to the read-only fleet workspace.
      Resources the process **cannot** observe from inside — a shared database login, two environments
      under one OS account, whether a key is unique across environments — are reported as *unknown*
      rather than assumed isolated. A verification that quietly assumes the answer is worse than one
      that admits the gap.

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
- [x] Make every dialog/drawer semantic, named, modal where appropriate, focus-trapped, and absent
      from the accessibility tree when closed. `PortalDialogAccessibilityTests` enforces
      `role="dialog"`, `aria-modal`, and an accessible name on every overlay across every page and
      JS module, and that no page presents a dialog without focus management; the browser lane
      asserts closed dialogs are not tab-reachable. The detector matches by class *pattern* rather
      than a list of known names — its first version passed with 31 green assertions while three
      unmarked dialogs sat behind a prefixed class the list did not contain. Fixed on the way:
      three governance dialogs had no semantics or focus trap, and `studio.html` had no focus
      management at all.
- [x] Add accessible names and keyboard behavior for search, favorites, script pickers, parameters,
      tabs, trees, palettes, tables, and cards. `PortalAccessibilityTests` computes the accessible
      name of every visible interactive control on every page the way the accessibility tree does,
      and fails with the offending selectors. It found four unlabelled search boxes — admin users,
      docs dictionary, governance filter, quarantine queue — all relying on a placeholder, which is
      not a name and disappears as soon as the user types. Keyboard behaviour for dialogs (focus
      entry, Tab containment, focus restore, Escape) is covered by `dialog-a11y.js`.
- [x] Verify light/dark, forced contrast, reduced motion, 200% zoom, and narrow viewports without
      clipping or color-only meaning. All six axes are asserted in the browser lane at 390px and
      1440px. Two of the probes were wrong before they were right, and both corrections matter:
      reduced motion must treat the standard `.001ms` collapse as honoured rather than as animation,
      and a translucent background must be composited rather than read as opaque — otherwise white
      text on a `rgba(255,255,255,.12)` overlay reports as invisible.

#### P2 — Browser quality and delivery guardrails

- [x] Add automated Chromium desktop and narrow-viewport lanes with seeded Viewer, Publisher,
      Steward, Operator, and Admin journeys. Desktop (1440px) and narrow (390px) lanes both exist
      and every accessibility/responsive check runs at both; `RoleJourneyTests` now covers Viewer,
      Publisher, DataSteward and OrchestratorManager (the Operator journey) alongside the existing
      Admin journey, asserting in **both** directions — the surfaces a role can use are offered, and
      the ones it cannot are absent rather than merely guarded. A navigation that offers what it
      cannot deliver reads as the product being broken rather than as a permission the user lacks.

      Two findings. The Governance nav is shown to every role and that is **correct** — lineage and
      stewardship are open to any authenticated user, and the gated pieces are gated inside the
      section; my expectation was wrong, not the product. And every non-admin role saw 403s on the
      report library, half of them because the Studio *capability probe* was itself role-gated, so
      asking "what may I do?" was an error for anyone outside two roles. Fixed. The remaining three
      are recorded as an explicitly skipped test rather than deleted.
- [x] Add accessibility assertions, critical visual snapshots, and API contract fixtures.
      **Accessibility assertions** — `PortalAccessibilityTests`. **API contract fixtures** —
      `BrowserApiContractTests` exercises the real endpoints and validates responses against the same
      `critical-api-contracts.json` the browser uses, reading the file rather than restating it; the
      contract existed but was only enforced in the user's session, so a rename reached production
      and a `TypeError` on somebody's screen was the first thing that noticed. **Visual snapshots** —
      `CriticalSurfaceSnapshotTests` captures **accessibility trees**, not pixels: no churn on fonts
      or GPU, a text diff reviewable in the PR that causes it, and failures for the changes that
      matter rather than a handful of grey pixels nobody investigates. Baselines live beside the
      tests; `ETLSQL_UPDATE_SNAPSHOTS=1` regenerates them, and updating one is a review decision.

      It found a defect in the governance dashboard on its first run: the five KPI tiles collapsed
      into a single undifferentiated run of text, so a screen reader read the numbers with no label
      attached to any of them, and the state banners were anonymous bold runs rather than headings.
      Both fixed.
- [x] Run identical smoke suites against `dotnet run` and the production Docker image.
      **Verified: 7 local checks vs 7 container checks, same checks, same outcomes**, including a
      report that actually executes on both. `scripts/Invoke-SmokeParity.ps1` starts a local Portal, builds and starts
      the production image, runs the *same* `Invoke-AcceptanceProfile.ps1` against both, and compares
      per-check JSON results.

      The comparison is the point: parity is a **comparison, not two green runs**. A container run
      that silently skips checks the local run performed would otherwise report success while proving
      less, so any check present in one side and absent from the other — or with a different
      outcome — fails even when both runs exit zero. Both targets get identical configuration and a
      bind-mounted script root, and the local side is pinned to `ASPNETCORE_ENVIRONMENT=Production`
      because `appsettings.Development.json` overrides environment variables.

      The image build takes several minutes, so budget for that when running it.
- [x] Fail on console errors, unhandled promises, broken Markdown, demo fallback, or horizontal
      overflow. `BrowserSession` now records `console.error` alongside thrown exceptions — the two
      catch different failures, since a console error usually stops nothing, which is exactly why it
      survives review. Horizontal overflow is asserted at 390px and at 200% text, ignoring content
      that scrolls inside its own container. Demo fallback is guarded at the source by
      `GovernanceNoDemoDataTests`.
- [x] Reuse representative UI sandbox stories as automated fixtures. `SandboxStoryTests` serves the
      repository root and drives **every** story and fixture through Chromium, asserting each mounts
      without throwing, logs nothing, and renders something. The sandbox already imports the
      canonical component sources, so this exercises the files the Portal ships without a Portal, a
      database, or a login — and it had only ever been run by a person clicking through, which meant
      a broken fixture stayed broken until someone happened to open it.

      It found one immediately: the VS Code designer webview imported `renderDesigner`, which the
      module does not export (it is `createDesigner`), so that fixture threw on import and rendered
      nothing. Fixed.

      Assertions are deliberately shallow. A narrow-viewport check was tried and removed — the
      sandbox stage is `overflow: auto`, so a component wider than it scrolls inside its own
      container, which is correct; the check flagged six components for doing the right thing.
      Page-level overflow is asserted where it reaches users, on the shipped pages.
- [x] Exclude generated review/build output from the container context and document a small seeded
      acceptance profile. **Exclusion**: `tests/` (~14 GB of fixtures and corpora) and
      `artifacts/` were being packed and shipped to the Docker daemon on every build although no
      Dockerfile copies them. `ContainerBuildContextTests` guards both directions — nothing a
      Dockerfile copies may be excluded (that breaks the image build, and only for whoever builds a
      container next), and the large directories nothing copies stay excluded. `docs/` and
      `snippets/` are deliberately *not* excluded; both images copy them for the embedded runtime
      help.

      **The seeded acceptance profile is done**: `scripts/Invoke-AcceptanceProfile.ps1` seeds a
      folder, a self-contained report, and one user per role entirely over the public HTTP API, then
      runs smoke checks. Going through the API is what makes `dotnet run` and the container image
      comparable — the same checks against both, rather than two scripts that happen to share a
      name. It is idempotent, handles the forced first-run password change, and exits 0/1/2 for
      pass/fail/unreachable. Documented at `docs/administration/portal/acceptance-profile.md`.

      Verified live against a running Portal. The one step it cannot do over HTTP is put the
      `.rptsql` file under the Portal's script root, so that is written directly when
      `-ScriptRootPath` is reachable and **skipped rather than failed** when it is not — a check that
      fails for something the script said it could not set up is noise, and noise is what stops
      people reading output.
- [x] Reconcile Portal architecture, isolation, administration, API inventory, policy matrices, HA
      diagrams, threat model, and verification runbooks against final source.
      **Portal architecture, API inventory and policy matrices are reconciled and now guarded.**
      `ArchitectureDocReconciliationTests` checks the mechanically checkable claims against source:
      every seeded Identity role, every persisted entity, every named authorization policy, and every
      API area is documented. Only checkable claims are asserted — a test that pretended to verify
      prose about intent would be vacuous or would block every honest rewording.

      It found more than a reading pass had: `Portal.md` claimed **three** seeded roles when there
      are **eight**, five of them security-relevant including every governance role; 11 API areas
      were entirely absent (branding, OIDC, service accounts and tokens, both policy-authority
      surfaces, configuration promotion, Studio, designer, docs, fleet); and 3 entities were missing.
      Also corrected: the two-axis authorization model, that folder `Manage` is authority over the
      reports in a folder rather than over the folder, and that `FolderPermission` must never be
      compared ordinally.

      **Isolation reconciled, and it found a real hole rather than just stale prose.** The
      departmental isolation contract listed the Portal database, Orchestrator database, artifact
      root and Data Protection key ring as resources that must never be shared — but not the
      **security-event outbox**, whose default is a machine-wide path under `LocalApplicationData`
      shared by every ETL-SQL process on the host. Two environments on one machine were writing
      security events into a single queue: a cross-environment leak of exactly the records isolation
      exists to keep apart, and the one resource whose default is *wrong* rather than merely unset.

      It is now a planned isolated resource in `GET /api/admin/environments/plan`, reported in the
      current-environment evidence, documented in both `Departmental_Isolation.md` and
      `security-events.md`, and pinned by a test. Found empirically: it was what made the browser
      lane fail whenever two test processes started back to back.

      **Administration reconciled.** Four settings this release added existed only in guides and
      architecture prose, and were absent from the configuration reference — which is the document
      an operator actually opens when configuring: `Studio.RequireApprovalToPublish`,
      `SourceControl.ProtectedBranches`, `DataQuality.AllowConnectionPreview`, and the tenth Studio
      capability `ReportApprove`. All four are now there, and
      `EveryStudioCapability_AppearsInTheConfigurationReference` guards the class of drift:
      capabilities are granted by typing their name, and the filter rejects an unknown name rather
      than storing a typo, so one missing from the reference is one nobody can grant deliberately
      and nothing reports.

      **HA reconciled, and it found an operational trap rather than stale prose.** Three of the six
      `/healthz` finding codes were undocumented, and `Portal:Topology:*` — the five settings that
      decide whether `/healthz` returns 200 — was absent from the configuration reference entirely.
      The trap: `ExpectedMode: Auto` infers `HighAvailability` from PostgreSQL *or* a configured
      `Portal:Storage:KeyRingPath` and never infers `Departmental`, so a single-node or departmental
      deployment that merely moved its key ring is classified HA, `RequirePostgresForHa` applies, and
      the load balancer stops routing to a node that is otherwise working. Asserted against the real
      endpoint rather than argued. A topology diagram now carries the ETL-SQL/infrastructure
      responsibility split the document previously described only in prose.

      **Threat model reconciled.** The security review packet's scope and trust boundaries predated
      this release's Portal authority surfaces — Studio capabilities, the draft review path and
      protected branches, and the disclosure surfaces. All are now in the boundary table with the
      evidence that constrains them, and the packet's read-only fleet non-approval is **enforced**:
      `FleetAggregation_ExposesNoMutatingRoutes` fails the build if a mutating route appears. A
      boundary stated only in a document lasts until the first convenient `POST`.

      **Verification runbooks reconciled and guarded.** `HaAndSecurityDocReconciliationTests` checks
      that every test named in the Automated Coverage Map still exists and every `ha-soak`
      subcommand a runbook says to type is defined by the CLI — a coverage map naming a deleted test
      claims a certification nobody performed. The production-readiness checklist gained the
      `ExpectedMode` step as **Required**, since that trap fires precisely at go-live.
- [x] Add release acceptance for browser, accessibility, responsive, local/Docker, departmental
      isolation, role/module/capability, and policy journeys. All seven are now gate phases in
      `Test-PreRelease.ps1`, and each phase's stated reason names what it actually covers rather
      than describing the lane in the abstract — a gate whose phases you have to go and infer from
      test filters is one nobody can review.

      - **Browser, accessibility, responsive** — the browser lane: the critical journey, four
        non-Admin role journeys, accessibility and responsive checks at 1440px and 390px,
        accessibility-tree snapshots, and every sandbox story mounting.
      - **Departmental isolation, role/module/capability, policy** — the Portal lane, which already
        carried `EnvironmentIsolationTests`, `AuthorizationMatrixTests`, policy authority and
        distribution, module gating and Studio capabilities; the phase now says so.
      - **Local/Docker** — a new `Invoke-SmokeParity.ps1` phase under `-IncludeDockerIntegration`,
        since it needs a container runtime.

### Portal — Authorship Is Not Permission

- [x] Decide and document whether dataset authorship upgrades an existing ACL grant but never
      substitutes for one. **Decided: datasets get a real grant instead of an upgrade.** `DatasetAcl`
      is group-only, so there was no per-user grant for authorship to upgrade and removing the check
      alone would have hidden a new private dataset from its own author. A creator (and the author of
      the report that owns the dataset) is now granted `Owner` in the new `DatasetUserAcls` table at
      registration time, and permission resolution reads grants only. Recorded in
      [AuthorshipIsNotPermission.md](docs/architecture/decisions/AuthorshipIsNotPermission.md).
  - The table is a sibling of `DatasetAcl` rather than a nullable `UserId` on it because relaxing
    `DatasetAcl.GroupId` to nullable is an `AlterColumn`, which the rolling-expand migration contract
    rejects and SQLite implements as a table rebuild.
- [x] Add dataset revocation tests before implementation.
  - [x] Prove a creator removed from every group loses dataset access. `DatasetAuthorshipRevocationTests`
        revokes the grant and asserts both the single-dataset and batch permission paths deny.
  - [x] Prove a creator retaining a lesser grant receives only the documented upgrade. A creator left
        with only a group `Viewer` grant can read and cannot manage ACLs.
- [x] Apply the rule to both `DatasetPermissionService` paths and `ReportDependencyService`.
- [x] Show and revoke per-user dataset grants in the Admin dataset permissions panel.
      `GET /api/datasets/{id}/acl` now returns group and user grants with a `principalKind`, and
      `DELETE /api/datasets/{id}/acl/user/{userId}` revokes a direct grant (invalidating that user's
      sessions). The table rendering is extracted to `dataset-acl-ui.js` with node unit tests, so the
      group/user distinction and the two revoke routes cannot silently cross over.
- [x] Add an architecture test rejecting unconditional `CreatedBy`/`OwnerId` permission
      short-circuits that do not consult an ACL. `AuthorshipPermissionBoundaryTests` inventories
      every `CreatedBy`/`OwnerId` comparison in the Portal with the reason it is safe and asserts set
      equality, so a new short-circuit fails the build until someone justifies it and a removed one
      forces its entry out. It earned itself immediately: the three dataset short-circuits it pinned
      as open are now gone, and it caught a new comparison added while fixing them.
- [x] Audit and test revocation for connections, subscriptions, alerts, and saved views. One real gap
      found, in alerts:
  - **Alerts — was leaking, now fixed.** `PortalAlertEvaluationService` dispatched without
    re-authorizing the alert's owner, so an alert outlived its author's access and kept pushing the
    value that crossed the threshold into the channel they had chosen. It now applies the same
    delivery-time check subscriptions do (owner active, still holds folder read, or is an admin) and
    skips unauthorized alerts whole rather than recording a transition nobody was told about.
  - **Subscriptions — already correct.** `SubscriptionDeliveryService.AuthorizeAsync` re-authorizes
    at delivery, covered by `SubscriptionDeliverySecurityTests`.
  - **Saved views — already correct.** Every route resolves report permission before narrowing to
    the caller's own rows, so losing report access removes the views with it.
  - **Connections — no authorship path exists.** `PortalConnectionCatalogService` resolves admin,
    then group ACLs; `CreatedByUserId` is recorded but never consulted for authorization. (An
    unrestricted connection — one with no ACL rows at all — is usable by everyone; that is a
    separate default, not authorship persistence.)
- [x] Prove directory/group removal revokes reports, datasets, connections, subscriptions, alerts,
      saved views, and anonymous links created by that identity.
      `DirectoryRemovalRevocationTests` runs it as one scenario in two phases, because they revoke
      different things: **group removal** takes the report, its saved views, and the anonymous
      share/embed links (and flips the admin anonymous-access inventory off `Active`), while a
      **direct** dataset grant deliberately survives — losing a group must not revoke a grant made to
      you personally. **Directory removal** then cascades the direct grant away and proves ownership
      transfer carries a grant to the new owner rather than leaving the dataset administrator-only.
      Subscription and alert delivery are re-authorized on the same rule and proven against it in
      `SubscriptionDeliverySecurityTests` and `PortalAlertEvaluationServiceTests`, which drive those
      delivery paths directly. Connections have no authorship path to revoke.

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
- [ ] Preserve the clicking principal across the Portal→Orchestrator proxy boundary. The Portal
      proxies with the shared key, so a bulk re-run of 27 jobs from the triage inbox currently audits
      as the service rather than the human who clicked — the Portal knows the actor and drops it.
      Worth capturing at the Portal edge even before federated identity exists.
- [ ] Add enforceable ownership for shared names and prevent unauthorized `CREATE OR ALTER` takeover.
- [ ] Attribute every Orchestrator mutation audit record to a real principal rather than a service.
- [ ] Add negative tests proving a reachable Orchestrator does not imply authority over another
      principal's objects.

### Orchestrator — Operations Triage and Run Flight Recorder

**Target operator:** a BI team running ~200 scheduled jobs across the day, whose reference points are
SQL Agent's Job Activity Monitor (triage and re-run) and the SSISDB execution reports (post-mortem).
Today they can answer *"what is running"* but not *"what broke, why, and can I safely re-run it"*
without one drill-down per job.

**The finding that sizes this track: almost none of it is new instrumentation.** The engine already
measures per statement — `ExecutionMetrics` in `src/ETL-SQL.Core/Profiling.cs` carries statement
text, duration, rows, CPU ms, spill read/write bytes, extent and partition-pass counts, aggregate
cardinality, and per-statement data-quality cost — profiling defaults to on
(`EvaluatorOptions.cs:70`), and `eng.profile` already projects it. It is in-memory only, so it dies
with the job process. Checkpoint/resume also already ships: top-level section labels save session
state (`SectionLabelStatementHandler.cs:27-41`), the evaluator resumes from
`@_LAST_CHECKPOINT_LABEL` and validates the label still exists (`Evaluator.cs:1062-1084`), and the
CLI exposes `--session`, `--var`, and `--resume` (`CliOrchestrator.cs:87-101`). The Orchestrator
simply never passes any of it: `TriggerJobAsync` takes a bare name (`SchedulerService.cs:277`) and
`BuildArguments` emits `run <script> --json [--session <id>]` and nothing else
(`ProcessJobExecutor.cs:435-441`). This track is persistence, plumbing, and rendering over
capabilities that already exist.

Two claims that look true and are not, recorded so they are not re-derived: job history is *not*
limited to status/error/rows — `JobHistoryEntry` (`IJobHistoryStore.cs:109`) also carries peak
memory, CPU seconds, script hash at run time, hash-matched, quarantined/warned rows, and a compact
failure summary, with normalized `JobDataQualityFailure` rows and a `JobDataQualityStatus`
projection already served at `/api/data-quality/{status,failures}`. And a cross-job history feed
already exists: `/api/history` returns all jobs when `jobName` is omitted
(`JobApiEndpoints.cs:347`), so the triage inbox needs no new backend read.

#### P0 — Triage inbox — SHIPPED on `feat/orchestrator-triage-inbox` (2026-08-05)

All items below are complete. The board reads shared state directly, groups failures into
incidents, surfaces missed runs, offers bulk re-run, and links each job to its downstream blast
radius. Covered by `OperationsTriageTests` (10) and `RunFailureSignatureTests` (12), plus a
headless render check for escaping and a UI-sandbox story (`triage-board`) with fixtures for the
busy morning, the quiet one, and a clipped history read.

**Authorization handoff.** The inbox inherits the Portal's existing coarse gate — the whole
orchestrator surface sits behind one `OrchestratorAccess` policy (`RequireRole("Admin",
"OrchestratorManager")`, `Program.cs:298`) applied at the controller (`OrchestratorController.cs:11`),
so read and mutate share a grant. This track creates no new hole, but it makes that single grant
mean considerably more. See **Orchestrator — Per-Object Authorization** below for the read/manage
split this needs; the two tracks do not block each other, but the grant vocabulary must be decided
before the flight recorder lands.

- [x] Add the cross-job read. **Changed from the original plan: reads the shared job-history store
      directly rather than proxying `/api/history`.** That is how every other Portal job-history read
      already works (failure digest, capacity report, operations posture, lineage impact), and it
      means the board still answers when the Orchestrator *service* is unreachable — which is
      precisely when someone is triaging. Mutations still go through the proxy.
      `OperationsTriageService` + `GET /api/orchestrator/triage`.
- [x] Add a cross-job triage view to `orchestrator.html`, placed above the schedule because "what
      broke" outranks "what is scheduled". Rendering is a canonical pure-function module
      (`wwwroot/js/triage-ui.js`) so the sandbox can drive it from fixtures.
- [x] Group failures by normalized error signature. `RunFailureSignature` strips ids, timestamps,
      quoted values, paths, and numbers. It deliberately errs toward **under**-grouping: merging two
      distinct outages hides the second one, and that is the only failure mode an operator cannot
      recover from by reading more closely.
- [x] Surface missed runs, with a grace window so ordinary lateness under load is not reported —
      a list that cries wolf gets ignored. Disabled jobs are excluded.
- [x] Add multi-select bulk re-run, reporting per-job outcomes rather than one opaque result,
      capped at 50 so a mis-click cannot enqueue the estate, and audited per job.
- [x] Link a failed run to its downstream blast radius ("this job failed → these tables are now
      stale") — the core of the "better, not equal" claim, since SSISDB structurally cannot answer
      it. Needed a new entry point first: the impact view held its target in local state and read no
      URL parameters, so `createLineageCatalog` now exposes `showImpact({kind, name, ...})` and
      `index.html` applies a deep link from the query string. **Query string, not the hash** — the
      governance hash router lowercases the whole route and would corrupt a case-sensitive target
      name. The link is applied once and stripped, so later mode changes do not snap back to it.
      Present on failed runs and on missed runs, because a job that never ran leaves downstream
      tables just as stale as one that failed.

#### P1 — Flight recorder (persist what is already measured)

**Transport decided: extend the `--json` envelope. Do not let the child write to the store.**
The envelope already carries structured, unbounded-cardinality metric arrays —
`dataQualityColumnMetrics` and `dataQualityRuleFailures` (`ProcessJobExecutor.cs:322-340`,
`WarmRunnerResponse` at `:727-740`) — parsed with explicit version tolerance ("absent on older
runners → defaults"). Statement metrics are the same pattern, only larger, so this is the
established contract rather than a new one. A direct store write was rejected on two grounds: it
would put state-store credentials (Postgres, at Enterprise) into every spawned job process, widening
the credential surface considerably; and it would couple the child to the *schema* rather than to a
message, which is the worse coupling during a rolling upgrade because schema changes are exactly
what migrations gate. Today the scheduler is the only writer of job history, and that is worth
keeping. The envelope's failure mode is also benign: no envelope means the flight recorder is
missing for that run, while the run's own result stays correct.

Known long-term costs of that choice, recorded so they are not rediscovered:

- [ ] Persist per-run statement metrics to a child table keyed on the job-history id, written by the
      scheduler alongside `LogJobEndAsync`.
- [ ] **Define the statement payload once, as a shared contract type consumed by both the one-shot
      envelope and `WarmRunnerResponse`.** This is the recurring tax and the main thing to get right:
      there are three execution paths — in-process `ScriptExecutorAdapter` (the default, since
      `UseProcessSpawning` is `false`), the one-shot `--json` process, and the warm runner with its
      own stdin/stdout line protocol — and the latter two carry the payload separately today. Every
      field added to the envelope currently has to be written twice; one shared type makes that once.
- [ ] Respect the single-line envelope constraint: `ParseResult` scans stdout backwards for one line
      beginning with `{` and parses that line as a complete document (`ProcessJobExecutor.cs:256-265`).
      There is no chunking or streaming, so a 500-statement script serializes onto one very long
      line. Cap the payload — all failed statements plus the top N by duration — rather than
      shipping every statement.
- [ ] Account for scheduler memory: the child's entire stdout accumulates in a `StringBuilder` inside
      the *Orchestrator* process (`ProcessJobExecutor.cs:158-162`), per concurrent job, and the
      scheduler is the one process that must not run out of memory. Statement text is the bulk of the
      payload, so the normalization required for redaction below also largely removes this problem —
      do both in the same change.
- [ ] Guard the `ArgumentsTemplate` escape hatch. An operator template that omits `--json` produces
      no envelope, and the fallback path returns success with zero rows — so the flight recorder
      would go silently missing on exactly the customised deployments least likely to notice. Warn at
      startup when a template omits it.
- [ ] Redact or normalize statement text before persisting it. **This is a security requirement, not
      a nicety.** `ExecutionMetrics.Sql` is raw statement text and may contain inline literals and
      credentials. The data-quality design deliberately committed to counts-only, never sample values
      (`IJobHistoryStore.cs:125`, `JobDataQualityFailure`); persisting raw SQL into a shared store
      breaks that invariant for a *different* principal than the one who ran the script. `eng.profile`
      exposing the same text in-process is not precedent — that is the author reading their own run.
- [ ] Name the persisted table's columns to match `eng.profile` so the same query shape works whether
      an operator is reading the live session or durable history.
- [ ] Bound growth before shipping: 200 jobs/day × dozens of statements compounds quickly. Follow the
      existing pattern (`PruneHistoryAsync` / `RollUpJobHistoryAsync`, `IJobHistoryStore.cs:230-252`)
      and prefer retaining statement detail for failed runs plus a sampled slice of successes — that
      removes most of the volume while still answering every triage question.
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

- [ ] **Solo.** The smallest safe form already exists as `eng.profile` (live, in-session) — so the
      durable table must be reachable as an `eng.*` read model, not only through the Portal inbox,
      or Solo silently loses a capability that Team gains. Precedent: `eng.job_history`,
      `eng.data_quality_status`, and `eng.data_quality_failures` are all already exposed this way
      (`EngineCatalogDataSources.cs`).

**Decided: a bare CLI run stays live-only and records nothing.** That is the point of the profile —
developing against real data should not accumulate a run history, and only production execution is
worth retaining. `eng.profile` remains the Solo answer.

#### Recording an unattended run without adopting the Orchestrator service

The gap this leaves is the operator who runs on a schedule (Windows Task Scheduler / cron) but does
not want the Orchestrator service. **The mechanism already exists and should not be rebuilt:**
`Engine:AuditAdHocRuns` (`src/appsettings.json:67`, default `false`) makes a standalone CLI run write
through `IJobHistoryStore.LogJobStartAsync` / `LogJobEndAsync` (`EngineRunner.cs:342-385, 711-844`)
and also populates the lineage catalog, covered by `AdHocAuditingTests`. What is wrong with it is
granularity and identity, both of which the triage inbox depends on:

- [x] Add a per-invocation override (`--record` / `--no-record`) for the recording decision.
      `Engine:AuditAdHocRuns` is a machine-wide boolean in `appsettings.json`, but a single install
      serves both interactive development and the scheduled invocation. Today an operator who wants
      their 02:00 task recorded also silently starts recording every exploratory run they make — the
      exact outcome the Solo decision above rejects.
- [x] Add `--job-name` so an unattended run has a stable identity. The ad-hoc path derives the job
      name from `Path.GetFileName(scriptFile)` (`EngineRunner.cs:377`), so the same script under two
      schedules, or same-named scripts in different folders, collapse into one history identity and
      the inbox cannot tell them apart. Default to the current behaviour when the flag is absent.
- [x] Fix `docs/administration/platform/appsettings-reference.md:85`, which describes
      `Engine:AuditAdHocRuns` as sending runs "to the audit server". It writes to the local job
      history store and lineage catalog; no server is involved.
**Shipped (v0.18.0).** `--record` / `--no-record` override `Engine:AuditAdHocRuns` per invocation;
absent means the configured setting still decides, so existing behaviour is unchanged. `--no-record`
wins over `--record`, because the safe reading of a contradictory command line is to record less.
`--job-name` gives an unattended run a stable identity, defaulting to the script file name.

One thing the item did not mention: the lineage catalog derived the job name *independently* of the
job history (`EngineRunner.cs`, two separate `Path.GetFileName` calls), so `--job-name` alone would
have filed a run's lineage and its history entry under two different identities. Both now use the
same name, covered by a test asserting the catalog and the inbox agree.

- [ ] **Team.** The reference case for this track; no profile change expected. The 200-job shop above
      *is* the Team profile, and Scheduling/Observability are already Green here.
- [ ] **Enterprise.** Statement metrics must be written to the shared store, not node-local, or the
      inbox returns different results per node behind the load balancer; the new table needs both
      `SqliteOrchestratorDialect` and `NpgsqlOrchestratorDialect`. Retention/roll-up must be
      leader-elected via the existing lease/`ClusterLock` machinery rather than running concurrently
      on every node. Parameter-override triggers must reach the audit outbox.
- [ ] **SaaS.** Observability is **Red** — tenant telemetry and support-access separation are not
      certified — and this track makes that cell *harder*, because a cross-job triage inbox is by
      definition a cross-scope aggregation, exactly the shape that leaks when scope is not
      server-derived. Persisted statement text compounds it: a platform operator triaging a tenant's
      failure would be reading tenant SQL, which is the controlled-support-access overlay. Do not
      claim this feature for SaaS until the inbox scope is server-derived and a negative
      cross-tenant test lands in the SaaS certification lane.
- [ ] Confirm no matrix cell moves backward, and record the review outcome the way
      [v0.18.0](docs/architecture/decisions/v0.18.0-deployment-profile-review.md) did.

### Platform — Admin CLI for Identity and Access

**Target operator:** an administrator on a headless server with SSH and no browser, or a runbook /
CI pipeline that must provision users and groups reproducibly. Today neither can. Secrets and
connections have a full CLI surface (`admin set-secret`, `admin list-connections`,
`admin verify-connection`, …, `CliOrchestrator.cs:881-954`); **users, groups, and group membership
have none at all** — they exist only in `AdminController.cs` (~60 endpoints) behind the Portal web
UI. This track closes that gap in the CLI, where the work is scriptable and reviewable in a diff.
An admin TUI was considered and deliberately deferred: see the note at the end.

**The prerequisite that sizes this track: the admin API is closed to non-interactive clients by
design, and opening it is the security-sensitive part.**
`ServiceAccountScopeMiddleware.cs:29-31` returns a `null` required scope for `/api/admin`,
`/api/auth`, and `/api/oidc`, and line 17 turns `null` into a flat `403`. Service accounts are
therefore categorically barred from every admin route — that is a deliberate posture, not an
oversight. The vocabulary in `ServiceAccountSecurity.cs:10-16` has only `portal.read`,
`reports.execute`, and `orchestrator.execute`; there is no admin/write scope to grant. So this is
not "add CLI verbs over an existing API" — it is *carving a narrow, tested hole in an intentional
deny*, and the verb surface is the easy half. Under OIDC there is no interactive-password fallback
either, so a scoped service identity is the only workable path.

#### Prerequisite — a scoped non-interactive admin identity

- [x] Add an `admin.identity` scope (users, groups, membership, sessions) distinct from any broader
      admin capability. Do **not** add a blanket `admin.*`. Backup/restore, migration, promotion,
      service restart/shutdown, and at-rest key rotation stay unreachable by token.
- [x] Replace the blanket `/api/admin` deny with a route-level allowlist so that *only* the
      identity routes become reachable, and every other `/api/admin/**` path continues to return
      `403` for service identities. Default-deny must survive: a new admin controller added later
      must be unreachable until someone opts it in.
- [x] Require **both** the `Admin` role and the `admin.identity` scope. A scope must never
      substitute for the role, and holding the role must not imply the scope.
- [x] Negative tests are the deliverable here, not the positive ones: a token without the scope,
      a token with the scope against a non-identity admin route, a revoked/expired/disabled
      account, and a token whose owner lost the `Admin` role after issue.
- [x] Audit every mutation with the service identity as actor, distinguishable from a human of the
      same name in the audit log.
- [x] Decide and document whether a service account may create or elevate *another* admin — the
      privilege-escalation question. Recommendation: deny role elevation to `Admin` by token, and
      require an interactive human for that one operation.

**Prerequisite shipped (v0.18.0).** The scope, the allowlist, the role coupling, the escalation
denial, and the negative tests are in. The verb surface below is now unblocked.

**The item as written was unsatisfiable, and finding out why was the work.**
`ServiceAccountsController.Validate` already refused the `Admin` role outright ("Service accounts
cannot receive the Admin role"), while `AdminController` is `[Authorize(Roles = "Admin")]` — so no
token could reach any admin route whatever scope it held, and "require **both** the role and the
scope" could never both hold. Resolved by coupling them: the `Admin` role is now grantable to a
service account **only** alongside `admin.identity`, which is safe precisely because the allowlist
confines such a token to identity routes. Granting `Admin` without that scope is still refused.

**`AdminIdentityRoutes` is an enumerated allowlist, not a prefix rule.** A prefix would have silently
admitted the next endpoint hung off `users/` or `groups/`; tests assert that
`users/{id}/favorites` and a hypothetical `groups/{id}/some-future-endpoint` are denied, so
default-deny survives the next person to add a controller. The method is part of the grant.

**A window nobody had written down.** Role claims are stamped at token issue and a service JWT lives
up to 15 minutes, so demoting an administrator left their automation able to create users for the
rest of that window — on the one route family that grants access. The owner's `Admin` assignment is
now re-read from the store on every identity-route request. Ordinary Portal routes keep the cheaper
claim-only posture. Proven by mutation: disabling the check fails exactly the two tests that cover
it, and no others.

**Escalation: denied, as recommended.** No service account can create or promote an `Admin`,
whatever its scopes; the guard runs before any mutation, and a test asserts the user is not created.
Demotion stays allowed so revoking an administrator during an incident does not need a browser.
Group membership cannot confer `Admin` — groups carry ACLs and Studio capabilities, not roles — so
the two `AddToRoleAsync` call sites are the whole escalation surface.

**Audit attribution needed no code.** `AuditService` already resolves `ActorType=ServiceAccount`,
the service-account id, and effective scopes from the principal. Verified end to end rather than
assumed.

#### Verb surface

Nested under `admin` (`admin user list`), following the `admin ha-soak <verb>` precedent rather
than the flat `admin set-secret` style — the identity family is ~25 verbs and flat naming stops
scanning cleanly. Record the inconsistency in the CLI reference so it reads as a decision.

- [x] **Auth/bootstrap.** `admin portal-whoami` — resolve credentials, print the identity, roles,
      and scopes, print no secret. Mirrors the `verify-secret` "prove it resolves without echoing
      it" idiom and is the first thing to run when a runbook fails. Credentials come from
      `--portal-url` plus env or a `SECRET:name` reference resolved through the existing machine
      secret store; **never** from argv.
- [ ] **Users.** `user list` (`--filter`, `--role`, `--include-inactive`), `user show`,
      `user create` (`--username --email --role`, optional `--first-name --last-name --provider`,
      `--password-stdin`), `user update`, `user enable` / `user disable`, `user delete`,
      `user reset-password`, `user revoke-tokens`.
- [x] **Effective permissions.** `user permissions --username` over
      `permissions/effective/user/{id}`, and `access-simulate --username --report` over the access
      simulator. Read-only, no new API needed, and the highest-value verb in the set: it answers
      "why can this person see this" without a browser.
- [ ] **Groups.** `group list`, `group show`, `group create`, `group update`, `group delete`
      (`--cascade`), `group members`, `group add-member`, `group remove-member` (repeatable
      `--username` maps to the bulk endpoints), `group capabilities` / `group set-capabilities`
      over `groups/{id}/studio-capabilities`.
- [x] **Sessions.** `session list`, `session disconnect --username`.
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

Still open: `user update` beyond enable/disable, `user reset-password`, `group update`,
`group capabilities` / `set-capabilities`, `service-account …`, and `access-simulate`.
`user permissions` already covers the common access question.

#### Cross-cutting behaviour the verbs must get right

- [x] **Name→ID resolution.** The API is ID-keyed; operators and runbooks have names. Resolve
      `--username`/`--name` via the catalog endpoints, and give not-found and ambiguous-match
      distinct, documented exit codes rather than a generic failure.
- [x] **Optimistic concurrency.** `UserDto`/`GroupDto` carry `Version` and the bulk endpoints take
      `VersionedResourceRequest` (`AdminModels.cs:35`). Read-then-write must carry the version
      through; add `--if-version` for callers that want to fail on drift. Last-writer-wins is the
      wrong default for an admin tool.
- [x] **Idempotence for runbooks.** `--if-not-exists` on create and `--if-exists` on delete, so a
      re-run is a no-op rather than an error. This is the property that makes the CLI worth having
      over the web UI; without it the tool is just a slower browser.
- [x] **`--json` on every read verb**, with a shape stable enough to pipe. Human-readable table by
      default, matching `admin list-connections`.
- [x] **Documented exit codes** — distinct values for auth failure, scope denied, not found,
      conflict/version drift, and validation error. Scripts branch on these.
- [x] **No secrets on argv, ever.** `--password-stdin` only, consistent with `SecretAdminService`'s
      never-echo discipline.
- [x] **No `ETL-SQL.Portal` project reference from `ETL-SQL.App`.** HTTP only, via a client in the
      App tier modelled on `src/ETL-SQL.TUI/UI/PortalClient.cs`. Keeping it over the wire is what
      makes the CLI work against a *remote* Portal from a jump box, which is the whole point. Add
      an architecture-boundary test so the reference cannot be added later by accident.

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

### Platform — SaaS Profile Red Cells

Every SaaS cell in the
[capability matrix](docs/architecture/standards/Deployment_Profile_Standards.md#capability-matrix)
is **Red** except Reports (**Yellow**) and Tenant isolation (**Green — implementation**, negative
tests in the certification lane, not a release claim). This section makes those cells plannable
instead of a single undifferentiated "SaaS is not certified".

**Read the Red honestly.** It does not mean the code is absent — much of the implementation exists
and the isolation lane already carries negative database, artifact, cache, queue, audit, PII,
lineage/quality, path, and quota tests. Red means *no current commit-bound evidence that tenant
identity is enforced end to end for that concern*. The work is therefore mostly proof and boundary
closure, not greenfield build. Per
[§8](docs/architecture/roadmaps/Deployment_Profile_Strategy.md#8-saas-is-a-distinct-trust-boundary),
tenant context must be **server-derived from authenticated authority** and never trusted from a
caller's unverified resource identifier — most of these items reduce to auditing one code path
against that single rule.

Ordering below is by dependency, not by value: identity carries tenant context, so nothing else can
be proven before it.

#### P0 — The boundary everything else depends on

- [ ] Identity: establish platform/tenant identity separation and delegated administration, and prove
      platform administration is separately audited and cannot implicitly impersonate a tenant user.
- [ ] Prove tenant context is server-derived at every entry point — a negative test per surface that
      a caller-supplied tenant/resource identifier cannot widen scope.
- [ ] Policy: tenant-specific policy authority with platform/tenant separation, so one tenant's
      policy cannot be authored or overridden from platform scope.

#### P1 — Data-plane isolation

- [ ] Connections and secrets: tenant/provider/key separation plus export proof (no cross-tenant key
      reuse, no raw secret export across the boundary).
- [ ] Scheduling and Execution: tenant-scoped queues, schedules, leases, quotas, and failure
      containment, including noisy-neighbour behaviour under load.
- [ ] Quality and stewardship: tenant-isolated lineage, scan, quality evidence, cache, and outbox.
- [ ] Audit: tenant-complete audit plus separately audited platform access.
- [ ] Backup and recovery: tenant-scoped backup, export, and restore isolation — including that a
      restore cannot reintroduce another tenant's rows.

#### P2 — Operations and topology

- [ ] Observability: tenant telemetry and support-access separation. **This is the cell the
      Operations Triage track above collides with** — a cross-job triage inbox is a cross-scope
      aggregation, and persisted statement text means a platform operator triaging a tenant failure
      is reading tenant SQL. Sequence the two together rather than independently.
- [ ] High availability: tenant-aware fleet rollout and noisy-neighbour containment.
- [ ] Authoring: controlled tenant ingress and a certified tenant authoring boundary.
- [ ] Reports (currently Yellow): tenant catalog and embed isolation.
- [ ] Move Tenant isolation from implementation-Green to claim-Green by attaching clean commit-bound
      evidence from the SaaS certification lane.

Each item is complete only when the matrix cell is updated with a **current linked evidence**
reference and the release review records the change, the way
[v0.18.0](docs/architecture/decisions/v0.18.0-deployment-profile-review.md) did. Do not infer SaaS
support from an Enterprise happy path.

### Portal — Governance Dashboard

- [x] Inventory and remove production demo fallback and browser-memory governance state.
- [x] Define durable models and authorized APIs for findings, decisions, glossary terms, badges,
      scans, and scoring settings.
- [x] Enforce resource/role authorization and security audit on every governance mutation.
- [x] Wire the dashboard exclusively to durable APIs with honest loading, empty, unavailable, and
      failure states.
- [x] Add API/role tests for mutation boundaries.
- [x] Add UI tests for live, empty, unauthorized, and API-failure states.
- [x] Add a guard proving production never presents demo records as governance evidence.

### Portal — Quarantine Row Access

- [x] Decide and document whether preview requires the caller's connection grant or whether
      `DataQualityStewardAccess` plus a manifest-bound target is sufficient.
- [x] Add nullable target connection alias, connector type, and catalog-backed provenance fields to
      `QuarantineReplayManifest` at capture time.
- [x] Preserve backward compatibility by classifying missing provenance as view-only.
- [x] Make target readability resolve enabled catalog entries with the caller's execution identity
      and the chosen authorization rule.
- [x] Bootstrap preview only from manifest-owned `SHARED:` connection metadata; never trust request
      connection names or accept arbitrary SQL.
- [x] Preserve the 15-second timeout, row cap, RLS identity, and redacted errors.
- [x] Gate connection preview behind `Portal:DataQuality:AllowConnectionPreview`, default off.
- [x] Audit every raw quarantine preview as a data-access event.
- [x] Add a positive catalog-backed preview test first.
- [x] Add tests for catalog miss, disabled entry, switch off, unauthorized identity, legacy manifest,
      row cap/timeout, and failure-path redaction.
- [x] Document preview eligibility, the kill switch, authorization, and audit behavior.
- [x] Add readable and view-only data-quality queue sandbox fixtures.

### Portal — Data Quality Follow-through

- [x] Track disposition and replay jobs to a terminal state, or at minimum link each submission to
      durable job history. **The tracking existed and had never worked.** The client polled
      `GET /api/jobs/{id}` — the *report-execution* namespace, backed by `PortalExecutionJobs` —
      with an id from `IJobChannel`, which was never in that table. Every poll answered 404, the
      client treated it as a transient outage, and it retried once a second for the life of the
      tab. No disposition or replay ever reached a terminal state on screen.

      Fixed with `GET /api/data-quality/jobs/{jobId}` on the right namespace, and both submissions
      are now recorded durably in job state (`dq:quarantine-submission:<kind>:<target>`) rather than
      only in the submitting browser's storage. The record is what makes a submission visible to a
      second steward looking at the same target — who previously could not tell a replay was
      already in flight, and whose obvious next move was to replay the same production load again.

      A forgotten job reports **`Unknown`, never `Failed`**. The in-process channel keeps job state
      in memory and answers "Job not found." after a restart; passing that through would tell a
      steward their replay failed when it may have completed, and the natural response to a failed
      replay is to run it again. `Unknown` is terminal — more polling cannot produce an answer, and
      a spinner that never resolves is a worse report than saying we cannot tell.
- [x] Replace `ParseRuleFailures` display-string parsing with structured per-column run metrics.
      The structured path was already built and already primary — the engine writes per-rule rows
      from all three execution paths (`EngineRunner`, `SchedulerService` for both the spawned and
      warm runners) and the trend endpoint reads them. What remained was that the Portal **threw
      the structured fields away at the last step**: `TargetTable`, `Action` and `Owner` were
      queried, grouped and serialised, and the browser rendered only column, rule and count.

      That was not merely missing detail. Two columns with the same name in different target
      tables — `Email` in `warehouse.Customers` and in `warehouse.Leads` — rendered as two
      identical-looking rows with different numbers and no way to tell which was which.

      `ParseRuleFailures` stays, because history recorded before per-rule capture has only the
      compact string and pruning is not automatic. It is now marked: those rows carry `CountsOnly`,
      are never merged with structured rows (summing them would attribute a legacy run's failures
      to a target table it never named), and render as *unavailable* rather than blank — an empty
      Owner cell otherwise reads as "nobody owns this rule", which is a different and more alarming
      claim than "this run did not record it".
- [x] Add a read-only Portal API and panel showing which rules protect each target/column.
      `GET /api/data-quality/rules?jobName=` plus the rule inventory in the data-quality queue panel.
- [x] Add `eng.data_quality_rules` and make it queryable through Portal `eng.*` access. The engine
      table already existed; `eng.data_quality_rules(job)` now resolves over a `PORTAL` connection to
      the same seven columns, so one SELECT reads the same shape beside the engine or against the
      Portal. The job name is required — rules bind to the statement that declares them, so there is
      no catalog-wide answer.
- [x] Measure preview-session startup and define an optimization threshold before enabling polling or
      dashboard refresh. **Measured: ~0.8 ms median, ~1.2 ms p95** (`QuarantinePreviewStartupMeasurement`,
      25 timed iterations after warm-up, three consecutive runs agreeing to 0.1 ms). Threshold set at
      a **250 ms median / 500 ms p95** — the point at which per-poll overhead becomes a visible
      fraction of a one-second poll interval. Recorded in
      [DataQualityRules.md](docs/architecture/decisions/DataQualityRules.md).

      The measurement is deliberately scoped to session construct → execute → dispose and excludes
      the target's connector read, because that read is what a preview mostly costs and is *not*
      what a reusable session would change. The harness reports rather than gates: scale
      certification here has produced a 56% warm/cold spread on one commit, wide enough to swamp any
      threshold worth setting, so it asserts only an order-of-magnitude structural ceiling.
- [x] If the threshold is exceeded, add a bounded reusable/read-only preview path without weakening
      parsing, linting, policy, RLS, timeout, row-cap, or redaction guarantees. **Not exceeded —
      roughly 300× under it — so this is deliberately not built.** It would buy about a millisecond
      per request while requiring every one of those guarantees to be re-established across a shared
      session; that is a large correctness surface for a negligible gain, and those guarantees are
      the whole reason the preview may read raw quarantined rows at all. Polling and dashboard
      refresh are not blocked by session cost; if either is slow the cause is the target read or the
      row cap.

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

## Architecture documentation staleness audit (2026-08-05)

Checked mechanically rather than by reading: every `src/…` path and every backticked type name in
`docs/architecture/*.md` was resolved against the tree, then each doc was checked for coverage of
the subsystems added in v0.14–v0.18.

**The wrong-statement rate is low.** Of ~16 flagged type references, all but three were false
positives — role names, framework types, TypeScript classes, and test-only types. The three real
ones are fixed: `ICryptoService`/`ISecurityService` in `Engine.md` and `ICodeActionHandler` in
`LanguageServer.md` named interfaces that no longer exist. Every cited source path resolves.

**The staleness is omission, not error** — and it is concentrated in `Engine.md`, which documents
the v0.10-era engine accurately and has not grown with it. Mention counts:

| Subsystem | `Engine.md` | Status |
| :--- | ---: | :--- |
| External spill engines | 69 | Well covered |
| Lineage capture | 6 | Covered |
| Adaptive execution / memory grants | 1 | Barely mentioned |
| **Data quality rules** (`@expect`, quarantine) | **0** | v0.17.0 engine subsystem, absent |
| **Columnar fast-path plans** (`Columnar*Plan`) | **0** | A whole execution-strategy family, absent |
| **Row-level security** (identity vars, `HAS_GROUP`) | **0** | v0.14.0 engine feature, absent |
| **`SECRET:` resolution / organization policy** | **0** | Engine-boundary enforcement, absent |

Each was confirmed engine-level, not Portal-only: `src/ETL-SQL.Core/Quality/`,
`src/ETL-SQL.Engine/Engines/Columnar*Plan.cs`, RLS functions in `StandardFunctions.System.cs` and
the declare/set handlers.

Why it matters more than a wrong type name: the data-quality rules **pin execution to the local row
pipeline** — the columnar fast-path gates deliberately exclude rule-carrying statements — so a
developer reading `Engine.md` to understand dispatch and fast paths cannot see a constraint that
governs both.

- [x] Add the four missing subsystems to `Engine.md`, or split them into their own architecture
      pages and link them. **Extended rather than split**, because the document is organised by
      mechanism and these are mechanisms — and because splitting would put the fast-path
      *disqualifiers* in a different file from the fast paths, which is the specific confusion that
      prompted this. The new sections explain how the pieces fit and link out to
      `DataQualityRules.md` and `RowLevelSecurity.md` for detail rather than restating it.

      **Checking the source corrected the claim that started this.** I had recorded that "the
      columnar fast-path gates exclude rule-carrying statements". They do not. Three
      `!HasDataQualityRules(...)` guards protect **SQL pushdown** — work sent to a remote database
      never reaches `ColumnQualityValidator` — while the native columnar `SELECT … INTO` is guarded
      separately on `!DataQuality.TracksNullCounts`, because a columnar batch copy does not visit
      the values null-counting needs. Same principle, two different mechanisms, and the imprecise
      version would have been repeated into the document had it not been checked.

      Also documented while there: the `RecordPlanDecision` / `PlanDecisionReasonCodes` telemetry
      that records *why* a fast path was declined, and that administrators bypass `HAS_GROUP` /
      `HAS_ROLE` by default.
- [x] Consider extending the reconciliation tests with a coverage check for engine subsystems.
      `EngineSubsystemCoverageTests` inventories every code-bearing directory under
      `ETL-SQL.Engine` and `ETL-SQL.Core` and asserts set equality against a declared inventory, so
      a **new subsystem fails the build until someone says where it is documented or why it needs
      no page**. Where coverage is claimed, the named page must still contain a marker for it.

      **Not a text search, deliberately.** Matching directory names against the prose was tried and
      is useless both ways: `Data`, `Common` and `Services` match incidental English everywhere,
      while `Planning` reads as undocumented even though its types are described by name. So the
      test does not infer coverage — it forces a decision, the same shape as
      `AuthorshipPermissionBoundaryTests`.

      **It found two real gaps while being written, and both are now closed** — by writing the
      pages, not by relaxing the inventory. The known-gap list is empty, and the test that pins it
      stays, so a future gap has to be added deliberately:
  - [x] `ETL-SQL.Core/Storage` — `IArtifactStorage`, the seam every host writes scripts, snapshots,
        datasets and key rings through. Now documented with the `ArtifactArea` set (including that
        `Keys` is treated as secret: owner-only writes, no local-copy leasing), the provider list,
        and the two decorators — `GuardedArtifactStorage` for the security guardrails and
        `FencedArtifactStorage` for database-backed write-epoch fencing. The last is why HA needs
        artifact roots genuinely *shared* rather than merely identical: fencing is coordinated
        through the database, so two nodes writing to separate directories never contend for the
        same epoch.
  - [x] `ETL-SQL.Core/Observability` — `ObservabilityConventions` and the instrumenting decorators.
        Documented with the reason the constants exist: keeping free-form names, paths, SQL text,
        parameter values and connection strings out of telemetry. That is a cost control *and* a
        disclosure control — a label travels wherever telemetry goes and is not covered by the
        redaction applied to logs and support bundles.

Docs verified current and needing no action: `Orchestrator.md` (HA, leases, fencing and heartbeats
all covered), `Lineage.md`, `Connectors.md`, `Reporting.md`, `Portal.md` (reconciled and guarded
this release).

## Documentation

- [x] Make sure everything above is documented. We may want to follow our 4 path process. How would
      a solo, team, enterprise, and SaaS accomplish these items.
      [v0.18.0-deployment-profile-review.md](docs/architecture/decisions/v0.18.0-deployment-profile-review.md)
      is the release review `Deployment_Profile_Standards.md` already prescribed but that no release
      had produced. Driven from the 47 changelog fragments — the authoritative list of what shipped —
      grouped into six capability areas, each answering how every profile accomplishes it and which
      cells are genuinely **N/A** rather than an unstated Portal prerequisite.

      **The finding is the summary: v0.18.0 is a Portal and Enterprise release.** Most of what it
      added has no Solo form because Solo has no Portal — which is a legitimate answer on one
      condition, that the underlying evidence stays reachable without the Portal. It does: every
      governance and quality surface reads `eng.*`, served identically by the CLI, Report Player and
      Orchestrator. The review says so where that condition holds and says the opposite where it
      does not.

      **No cell moved to Green.** The release strengthens evidence behind existing Green cells and
      adds acceptance lanes that make them re-testable; the SaaS column is unchanged and still Red
      for every concern touched. Three things the review records that were not written down
      anywhere: the Portal governance score and `eng.stewardship_score` use different models and
      will not agree; recovery custody stays on the host in every profile; and the `Auto` topology
      mode classifies a Team-on-PostgreSQL deployment as HA and holds it out of rotation.

## Pre-configured reports

- [x] ~~Install the `/samples` reports into the Portal automatically via a checkbox.~~
      **Investigated and deliberately not built.** Kept rather than deleted so it is not re-proposed.

      Scoping it down to the reports actually worth installing dissolved the item. The candidates
      were the three admin/steward reports added in v0.17.0–v0.18.0 — `data_quality_health`,
      `stewardship_scorecard`, `protected_data_audit` — and every one duplicates a Portal surface
      that already exists and is better at the job:

      - **Protected Data Audit** reads `eng.protected_data` / `protected_data_suggestions` /
        `missing_tags`. `CatalogController.ProtectedData` calls the same
        `LineageProtectedData.FromHistory`, and `lineage-catalog.js` already renders both, plus
        missing tags in the stewardship inventory.
      - **Data Quality Health** duplicates the Data Quality dashboard and quarantine queue shipped
        this release.
      - **Stewardship Scorecard** is worse than duplication: Governance Overview scores assets with
        a Portal-configurable *deduction* model (`GovernanceScoringSettings`), while
        `eng.stewardship_score` is a policy-driven weighted *component coverage* from Core
        `StewardshipScoring`. Two surfaces both labelled "stewardship score", computing different
        numbers from different rules, with nothing on screen explaining the disagreement.

      The three `samples/admin_operations/*.etlsql` files are not reports at all — zero
      `CREATE VISUAL`/`CREATE PAGE`/`SET REPORT` — and already ship as running services
      (`FailureDigestAdminService`, `BackupReportAdminService`, `CapacityReportAdminService`,
      registered in `Program.cs`) with schedule and history in Admin → Operations.

      **What these reports are actually for is portability**, not Portal novelty: they are written
      against `eng.*` so the same report answers the same question from the CLI, the Report Player,
      the Orchestrator and the Portal. That belongs in the four-path documentation below, not in an
      installer. Two ACL facts found while scoping it are worth keeping: folder ACLs bind to
      **groups**, not Identity roles, and Admins bypass folder ACLs entirely
      (`FolderPermissionService`), so an "Administrator" folder ACL would have been a no-op.

## Engine & Orchestrator bugs

- [x] **Session TTL Conflict**: Inactive sessions from interactive development (VS Code, TUI, Workstation Editor) should be reaped in 24 hours, but persistent sessions for Orchestrator jobs must remain for 7 days (governed by `Session:StaleSessionRetentionDays`). Currently, the engine's startup sweep (`SessionStateManager.cs`) reaps all sessions older than 24 hours (governed by `Session:PersistentSessionTTLHours`), overriding the 7-day retention period for Orchestrator jobs. Fix the cleanup sweep to identify and preserve Orchestrator session state for the full 7-day period.
- [x] **Missing Resume ID in Alerts**: When an Orchestrator job fails, the Email (SMTP) and Teams (Webhook) alerts sent by `NotificationDispatchService.cs` contain the error message but miss the resume identifier (`SessionId`). Update `DispatchJobNotificationsAsync` to format the `SessionId` (available on the execution result) into the alert body/text, giving operators the exact ID needed to resume the run.

## Engine & Pushdown Optimization

- [x] **Dialect-Aware Function Rewriting**: Develop a function translation layer inside `PushdownEngine.cs` (during `CompileQuery`) to rewrite engine functions (e.g., `TRUNC`, `SYSDATE`) into native target dialect equivalents (e.g., `CAST(... AS DATE)`, `GETDATE()` on SQL Server). This preserves whole-query pushdown and avoids falling back to expensive local network/CPU evaluation for simple dialect mismatches. Note: Sargability remains the script author's responsibility; however, a linter check could be added to warn about non-sargable expression filters on indexed columns.
- [x] **Asynchronous Ingestion Pipelining**: Decouple source reading and destination writing in the streaming pipeline (e.g., `INSERT INTO ... SELECT ...` direct streams) using buffered async channels (`System.Threading.Channels`). Running the producer (read network I/O) and consumer (write network I/O) concurrently will maximize network pipeline throughput and reduce job runtime on large transfers.
- [x] **Dialect Translation Matrix**: Develop a lightweight, regression-preventing dialect test matrix. Instead of running the entire SQL Logic Test (SLT) suite against live remote databases, define a test matrix of ~100-200 standard and rewritten SQL functions and assert that their compiled SQL string outputs match Postgres, MSSQL, and Oracle dialect expectations, verifying pushdown correctness.
- [x] **Add Core Function Transpilation Rewrites**: Implement and verify standard and dialect-specific rewrites for:
  - Null Handling: `ISNULL` -> `COALESCE` on Postgres/Oracle.
  - Date Extractors: `YEAR`, `MONTH`, `DAY` -> `EXTRACT(...)` on Postgres/Oracle.
  - String Length: `LEN` -> `LENGTH` on Postgres/Oracle, and `LENGTH` -> `LEN` on MSSQL.
  - Substrings: `SUBSTRING` -> `SUBSTR` on Oracle.
  - Bare primitives: `SYSDATE` (function call) -> `SYSDATE` (bare keyword) on Oracle.

## Portal bugs
- [x] Why the casing differences in documents?  Also All filter does not look like it has all documents.  Seems like some are missing.  See screenshot: "C:\Users\chuck\OneDrive\Pictures\Screenshots\Screenshot 2026-08-04 134045.png"
- [x] Studio has a great front page but once you click into code editor or the report visual designer the main toolbar goes away and you can't navigate out.  Can these two pages fit better within the overall page so you have the exiting navigation buttons.
- [x] Governance sidebar has Overview, Quarantine Queue, Lineage Search, Stewardship, Audit Evidence.  Lineage Search, Stewardship, Audit Evidence all point to the same place just different top selector.  These are redundant simplify it down to just Lineage Search.
- [x] Governance Overview includes Overview, Workqueue, Exceptions, Glossary, and Settings.  These should not all exist under the Overview.  I feel like we have two menu's going that should be combined into one.  The sidebar should have Overview, Workqueue, Exceptions, Glossary, Quarantine Queue, Lineage Search, and Settings.  The Overview page should not have a separate menu options.
- [x] We have the Quarantine Queue but there is supposed to be a lot more available so the user can see metrics on failure rate of data quality they also should be able to look up jobs and what rules (@expect tags) are applied to each job.  All of that is missing.  See this document: C:\Users\chuck\scratch\ETL-SQL\docs\architecture\decisions\DataQualityRules.md

## Profile

- [x] Basic performance is automatically captured but the more detailed performance metrics captured
      with SET PROFILE ON; needs some enhancements. We need to make sure that the Data Quality work
      is being captured so the user knows the cost of the rule.

      **The profile had drifted well behind the engine, not only on data quality.** Comparing every
      counter on `ITelemetryContext` against the columns `eng.profile` actually exposes found seven
      live counters that never reached it — all from the spill/partition work:

      | Added | Why it matters |
      | :--- | :--- |
      | `spill_read_bytes` | `spilled_bytes` said the engine wrote to disk; nothing said it had to read it back, which is the half that costs time on the critical path |
      | `spill_extents` | High against modest bytes is fragmentation, not volume — points at batch sizing rather than memory |
      | `partition_passes` | Above 1 means the data did not fit the budget; the single most useful "raise a threshold" signal |
      | `aggregate_groups` | The cardinality that drives aggregate memory |
      | `aggregate_expansion_ratio` | Output rows over input rows |
      | `sort_spills` | Sorts that went to disk |
      | `cpu_time_ms` | Separates *slow because it was working* from *slow because it was waiting* — high duration with low CPU is I/O, a lock or a remote database, and no engine tuning will help it |

      Plus the four data-quality columns that prompted this: `dq_rows_validated`,
      `dq_rows_quarantined`, `dq_rows_warned`, `dq_validation_ms`. Cost is attributed to the
      statement carrying the rules; every other statement reports zero, so the overhead is read
      directly rather than inferred from a run total.

      Two things worth knowing about the timing: it is gated on `IsProfiling`, which **defaults to
      true**, so the two timestamp reads per row are the normal case — `SET PROFILE OFF` is the
      lever that removes them. And `SHOW PROFILE` is retired in favour of `SELECT * FROM
      eng.profile`; its handler still exists but the parser rejects the statement, so it is
      unreachable.

- [x] **`SubquerySpillCount` is declared, reset and exported — and never incremented.** It sits on
      `ITelemetryContext`, is cleared in `ExecutionTelemetryManager.Clear()`, and is written into
      the report manifest by `ManifestBuilder` as `subquerySpillCount`, where it is therefore always
      `0`. Either wire it where subquery spilling happens or remove it; a manifest field that always
      reads zero is worse than an absent one, because it looks like an answer.

## SaaS Multi-Tenancy & Portal ETL IDE (Round 1 Gaps)

- [ ] **Tenant-Scoped Encryption Keys (BYOK)**: Refactor `DatasetAtRestKeyValidator.cs` and credential decryption in the engine to support tenant-isolated encryption keys backed by external KMS provider secrets, replacing the single global master key.
- [ ] **Chrooted Virtual Filesystem Isolation**: Build a secure path abstraction layer for all file/directory connectors and operations (e.g. `FLATFILE`, `DIRECTORY`, `SEND FILE`) to enforce tenant-scoped root directories (chroot) and prevent directory traversal or access outside the tenant container.
- [ ] **Noisy-Neighbor CPU/Memory Containment**: Implement CPU/memory/IO limits per tenant session, leveraging cgroups or containerized execution runners for ad-hoc and scheduled query execution, preventing a single query from starving shared Portal or Orchestrator nodes.
- [ ] **Portal ETL IDE Data Preview & Schema Browser**: Add support for interactive schema inspection and row previews of intermediate `#temp` tables and source connections in the Portal Web Editor, allowing developers to debug ETL scripts in real time.
- [ ] **SaaS Multi-Tenant Identity (Multi-IdP)**: Support registration of tenant-specific OIDC Identity Providers (e.g., Okta, Azure AD, Ping Identity) dynamically resolved by tenant domain or issuer claims, rather than using a single platform-wide OIDC configuration.
- [ ] **Usage Metering & Billing Collector**: Instrument the engine's telemetry manager to log row-transit, data size, connector type, and execution CPU usage per tenant id, writing these to a durable billing log for billing ingestion.

## SaaS Multi-Tenancy & Portal ETL IDE (Round 2 Gaps)

- [ ] **Tenant-Aware Fair-Share Scheduling**: Implement tenant-partitioned execution queues or weighted round-robin scheduling in the Orchestrator to prevent a single tenant's massive job load from causing head-of-line blocking or starvation for other tenants.
- [ ] **Internal Network Egress Fencing**: Secure network connections established by tenant scripts by executing remote connector queries in isolated sandbox networks (e.g. secure VPC boundaries, dynamic proxy routing) to block port scans or connections to the internal SaaS hosting subnet.
- [ ] **Tenant-Isolated Lineage Graphs**: Partition the metadata search and lineage graph indexing engine to ensure that lineage tracking data, table names, and database schemas cannot be leaked or queried across tenant boundary boundaries.
- [ ] **Zero-Loss Tenant Migration Utility**: Build an administrative command-line utility to export and import a tenant's complete configuration, active jobs, reports, history, and workspace files as a single encrypted bundle for easy onboarding or migration to on-premises deployments.
- [ ] **Portal Script Concurrent Editing Locks**: Implement collaborative file mutexes and session-lease locking in the Portal script editor to warn other workspace developers when a script is actively being edited, preventing silent code overwrites on save.

## Bugs
- [x] **Lineage is not working correctly**  Using this query: 
```sql
  DROP CONNECTION IF EXISTS hospital;
  CREATE CONNECTION hospital AS
  MSSQL('ENC:ArFsBrabRZQUUiaV/Aw6a1XNHcXrNolQfWGGxr3kACamZ5c8Qros/oHIUSpysb/f2NlhnpUvb6zpfwrP/0ObQiktuIVG0yElAygnEkAJwnUSjYkcOppeHIkAFffLMprq3jm4YSOszSP02BZWHgbzqueW8QPX4QKR/eYoAJ7
  l7+mR3vI16g2EFN/wpND220nYXGfNFA==');

  EXECUTE hospital
  BEGIN
      DROP TABLE IF EXISTS Patient;
      CREATE TABLE Patient (
          patient_id bigint IDENTITY(1,1) PRIMARY KEY NOT NULL
          ,name varchar(100) NOT NULL
          ,date_of_birth date NULL
          ,date_of_death date NULL
          ,gender varchar(10) NULL
          ,created_at datetime NOT NULL DEFAULT GETDATE()
          ,updated_at datetime NOT NULL DEFAULT GETDATE()
      );
  END

  DROP CONNECTION IF EXISTS pats;
  CREATE CONNECTION pats AS FLATFILE(PATH="C:\tmp\patients.csv", TEXT_QUALIFIER='"', DELIMITER=',', HEADER=TRUE, NULL_AS=EMPTY);
  INSERT INTO hospital.dbo.Patient (name, date_of_birth, date_of_death, gender)
  SELECT name, CAST(date_of_birth AS date), CAST(date_of_death AS date), gender FROM pats.FILE;

  SELECT
  patient_id, name, date_of_birth, date_of_death, gender, created_at, updated_at
  FROM hospital.dbo.Patient;  

  EXPORT LINEAGE FOR hospital.dbo.Patient AS OPENLINEAGE TO 'C:\tmp\hospital.Patient.openlineage.jsonl';

  ```
  When I hover over any column in the last select other than patient_id (which is correct) I should get the source being the csv file.  
  Example 
  date_of_birth: pats.name - C:\tmp\patients.csv -> Cast to date -> MSSQL EDW.dbo.Patient.date_of_birth  
  name: pats.name - C:\tmp\patients.csv -> MSSQL EDW.dbo.Patient.Name
  I don't want to reveal any credentials but the database, file, etc should be identifiable
  enough that we know where they came from.  The output should start at the end and work backwards.

  We saved this lineage above and later pulled in EDW.dbo.Patient.name into another script.  So step 1 import lineage, step 2 write Patient.name out to a different csv file.  C:\tmp\output.csv  The lineage should show name: output.name - C:\tmp\patients.csv -> MSSQL EDW.dbo.Patient.Name -> output.name C:\tmp\output.csv

  Let's write out the second script to test a full lineage I can't seem to find the syntax to IMPORT LINEAGE
  ```sql
  IMPORT LINEAGE FOR hospital.dbo.Patient AS OPENLINEAGE FROM 'C:\tmp\hospital.Patient.openlineage.jsonl';
  ```
  - [x] **Lineage syntax needs help**  I see in the syntax index that LINEAGE_NAMESPACE and LINEAGE_IMPORT_CATALOG exist but there is no documentation on how to use them.  There should be multiple ways to export lineage one being OPENLINEAGE, then markdown as a mermaid chart, and maybe more its been a while since we worked on this.
  - [x] **Import lineage** As listed above we should be able to IMPORT LINEAGE from a file.  But we also need a way to import from a database and if I remember correctly we also save lineage to the ETL-SQL database so that may be a third area we could import it from.
  - [x] **VS Code METADATA EXPLORER not showing flatfile columns**  See screenshot: "C:\Users\chuck\OneDrive\Pictures\Screenshots\Screenshot 2026-08-07 153915.png"  Shows both FILE and DUAL.  FILE has nothing below it, DUAL and dummy?  I would expect to see these column: "name","date_of_birth","date_of_death","gender" without " surrounding them.
  - [x] **VS Code METADATA EXPLORER not show column types**  For databases where column type is accessible it should show that information.  Currently just shows column names.  See screenshot: "C:\Users\chuck\OneDrive\Pictures\Screenshots\Screenshot 2026-08-07 154200.png"  I would expect: date_of_birth::date   But when the column is dragged and dropped into a script (which works great) it should only show the column name and not the type.
  - [x] **VS Code setting need to be grouped**  We have AI, formatting, and paths.  It would be easier for a user to read if these were grouped together.
  - [x] **Hide report preview, launch, report designer unless the script is an rptsql**  We have two extension for a reason so an etlsql is not expected to contain reporting elements.  We'll need to add an option to VS Code for that extension currently only ETL-SQL points to .etlsql we'll need ETL-SQL Report.  Then on our welcome screen when they click create etlsql it opens as etlsql.  Notebook is the same etlnb, and Report (the change) should open up as an rptsql.
  - [x] **Add a format button** I used to be able to use shortcut keys to format but vs code must have overwritten them.  Either way let's add a format button next to the run button so users can click to format their code or if a selection is highlighted it only formats the highlighted selection.
  - [x] **On save when plaintext password VS Code asks to add the security feature, then sets the password but does not save**  It encrypts the plaintext connection and shows it as encrypted but doesn't save the file.  If you exit without saving it saves it as a plaintext.  The workflow of adding the password and switching to the encrypted connection string should save the file with that encrypted connection string automatically.

### VS Code resolution (v0.18.0)

**Two of these six needed no code.** The format button already existed in `editor/title` beside Run
(`navigation@11.5`) and already formatted the selection when one was active. Report preview, the
designer, and the launch submenu were already gated to `resourceLangId == rptsql`; `rptsql` was
already registered for `.rptsql` under the alias "ETL-SQL Report", and the welcome screen already
created `.etlsql` / `.rptsql` / `.etlnb`. Verified against the contributed menus and `WelcomeView`,
not assumed.

**Flatfile columns.** The root cause was not in the explorer. `CREATE CONNECTION x AS
FLATFILE(PATH='...', ...)` carries no target expression — everything is in the option bag — and the
language server built its connection string from `TargetExpression` only. The string came out empty,
the file was never opened, and the table showed with nothing under it. The server now renders the
option bag the way the engine does (`ConnectionStringBuilder`), using literal values only; an option
built from a variable is skipped rather than guessed at. Column names were never actually quoted —
they only looked that way because no columns were read at all.

**DUAL.** Injected into every connection so `SELECT 1 FROM DUAL` completes. It is not a browsable
object, so `etlsql/getTables` — which only feeds the explorer and sidebar — now filters it.
Completions read the metadata manager directly and still see it.

**Column types.** `etlsql/getColumns` returns `columnDetails` (name + type) beside the existing
`columns`. The type is rendered as a dimmed `::type` suffix through the row's existing `detail`
field rather than being formatted into the label, because the label is what a drag inserts — so
drag-and-drop still yields just the column name. Sources that cannot report types show none.

**Encrypt-on-save.** `etlsql.secureConnection` rewrote the buffer and never saved, so the editor
showed `ENC:...` while the file on disk still held the plaintext password. It now saves, and warns
explicitly if the save fails. Saving re-fires `onDidSaveTextDocument`, and the conditions that
triggered the prompt (`NO_SAVE_SENSITIVE`, `CONNECTION_ENCRYPTION`) are still true on that pass, so
a naive fix prompts forever — hence `saveGuard.ts`, a consume-once mark with a TTL so a failed save
cannot suppress the policy for the rest of the session. Extracted rather than inlined so the loop
behaviour is covered by tests.
 - [x] **Tags not being passed along to the final table**  Using this query
 ```sql
 DROP CONNECTION IF EXISTS hospital;
CREATE CONNECTION hospital AS MSSQL('ENC:Aic23Mtsl64L0DIRFyErg99XTCE9ULhB+601tKD9wTSKFmFoNgvswkqZ73T7Txz9fZSwRncM0nIyqbfapQ2E24vKOOJjcgGDHJP9FQec8ten2RaDQEygHj8LHLr17J0lOqYritLgiBaGTs4pRXGnkSpNBxfDJfk+pZe4IeuRxpQet16HVzvn3J/qNHhE0JVCICIeRQ==');

EXECUTE hospital
BEGIN
    DROP TABLE IF EXISTS Patient;
    CREATE TABLE Patient (
        patient_id bigint IDENTITY(1,1) PRIMARY KEY NOT NULL
        ,name varchar(100) NOT NULL
        ,date_of_birth date NULL
        ,date_of_death date NULL
        ,gender varchar(10) NULL
        ,created_at datetime NOT NULL DEFAULT GETDATE()
        ,updated_at datetime NOT NULL DEFAULT GETDATE()
    );
END 

DROP CONNECTION IF EXISTS pats;
CREATE CONNECTION pats AS FLATFILE(PATH="C:\tmp\patients.csv", TEXT_QUALIFIER='"', DELIMITER=',', HEADER=TRUE, NULL_AS=EMPTY);

import:
INSERT INTO hospital.dbo.Patient (name, date_of_birth, date_of_death, gender)
SELECT 
    name /* @d: patient name formatted as last name, first name; @pii: true; */
    ,CAST(date_of_birth AS date) AS date_of_birth /* @d: patient date of birth; @pii: true; */
    ,CAST(date_of_death AS date)
    , gender /* @d: patient gender MALE/FEMALE/OTHER;  @expect: "IN ('MALE','FEMALE','OTHER')"; @fail: 'QUARANTINE'; */
FROM pats.FILE
ON FAILURE QUARANTINE  TO quarantine_gender WITH (RETENTION = '30 DAYS')
;

check:
SELECT 
    patient_id /* @d:internal id to the patient table, idenitity column; */
    ,name      
    ,date_of_birth 
    ,date_of_death
    ,gender 
    ,created_at
    ,updated_at
FROM hospital.dbo.Patient
;

-- export lineage
EXPORT LINEAGE FOR hospital.dbo.Patient AS OPENLINEAGE TO 'C:\tmp\hospital.Patient.openlineage.jsonl';
```

I would expect hovering over name in the SELECT...FROM hospital.dbo.Patient would should patient name formatted as last name, first name; PII but it shows nothing.  Likely same as lineage where the INSERT is breaking the association.

### Resolution (v0.18.0)

**Physical identifiers.** Lineage now carries a credential-free physical descriptor beside every
logical name: `FLATFILE C:\tmp\patients.csv`, `localhost:EDW.dbo.Patient`, and `EDW.dbo.Patient`
under `SET NO_SAVE_CONNECTION = ON`. The IDE hover path analyses text statically and never opens a
connection, so `LineageAnalyzer` builds its resolver from the script's own `CREATE CONNECTION`
statements; the engine supplies live connections at runtime. Connectors expose their own
server/database through `IDataSource.GetLineageLocation()` (implemented for MSSQL, Postgres, MySQL;
others fall back to the database name). An `ENC:` connection string resolves to *nothing* rather
than to a guess.

**Design call — the logical name stays the key.** An earlier pass rewrote the stored lineage key
with the enriched name. That made every lookup re-enrich to match, and it broke export/import and
cross-script chaining, where no connection map exists. Physical descriptors are now a separate
display field (`target_physical` / `source_physical`); `target_table` remains the logical key.

**Ordering.** `eng.lineage` gained a `step` column — distance from a raw source — and returns rows
origin-first. Timestamp order could not work: static analysis and execution record the same flow at
different moments.

**Design call — transformations do not get their own row.** A `CAST` is never a standalone event; it
happens as part of a write into some target, so it rides on that row. A transformation that really
is its own step (staged through a `#temp`) still gets its own row, because it has its own target.
This is the answer to "maybe 2 and 3 can be combined".

**Duplicate rows.** One movement was recorded twice — once by static analysis, once at execution —
and surfaced as two steps that never happened. `INSERT ... SELECT` column rows are now labelled
`INSERT` (and `SELECT ... INTO` as `SELECT INTO`) to match what the engine records, and `eng.lineage`
collapses same-hop duplicates at the projection layer, keeping the better-described entry. Both
entries survive in the tracker because hover locates the cursor by source position.

**IMPORT LINEAGE.** The feature existed as `INSERT LINEAGE FOR TABLE ... FROM ...` and was
undocumented. Added `IMPORT LINEAGE FOR <table> [AS OPENLINEAGE] FROM <file|json>` as the spelling
that mirrors `EXPORT LINEAGE`; `INSERT LINEAGE` still works. `IMPORT` is a **soft keyword**,
recognized only before `LINEAGE` — reserving it would have taken away `import:` as a section label,
which appears in the very script in this bug report.

Three round-trip defects had to be fixed for import to be worth anything:
- Export wrote a file source as the name `FILE`, losing the path. It now writes the full path, per
  OpenLineage convention.
- Export strips the connection alias (an alias is script-local), so imported rows never chained.
  Import now re-attaches the *importing* script's alias by matching the OpenLineage namespace, so
  two scripts can call the same database different things and lineage still connects.
- `LoadState` dropped transformation detail, so re-imported lineage lost its `CAST`s.

**Latent bug found.** When two records collided on the tracker's dedup key, merged metadata never
reached the column-metadata indexes — so a tag applied by a second observation of the same statement
was invisible to `GetColumnMetadata` and to tag inheritance. Fixed in `LineageTracker.Record`.

**Casing.** All `eng.*` columns are snake_case, including the `LINEAGE(...) INTO` projection, which
was still PascalCase. Reference docs updated to match.

Not addressed here (still open above): the VS Code metadata-explorer items, settings grouping,
report-surface gating, format button, and the plaintext-password save flow.

- [ ] **Create a working cookbook recipe for Lineage**  It should be two parts, part one should be data
  from a flat file into a database with some transformations in the middle.  This is an EDW load.  This
  should also include an export of the Lineage.  Part two would be importing the lineage and taking the 
  table from EDW and making a report from it.  Everything should work and the lineage on the report should
  show the flat file source to the EDW to the report and all transformations that happen between.