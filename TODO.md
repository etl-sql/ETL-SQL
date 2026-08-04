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

- [ ] Add `scripts/Test-MsiUpgrade.ps1 -PreviousMsi <path> -CurrentMsi <path>` asserting the full
      sequence, not just the registry:
      1. install previous → exactly **1** uninstall entry at the previous version
      2. write a sentinel file into `InstallLocation`
      3. install current **over** it
      4. **exactly 1 entry, at the new version** — two entries is the side-by-side regression
      5. sentinel survived → config/data preserved
      6. installed `ETL-SQL.exe --version` reports the new version
      7. uninstall → 0 entries
- [ ] Steps 5–6 matter: a registry-only assertion passes while files are clobbered or
      `RemoveExistingProducts` is mis-scheduled, which is precisely what "preserves config/data" in
      the checklist is asking about.
- [ ] Add a CI job gated to `release/**` pushes and tags (not every PR — the previous release MSI is
      ~900 MB). Resolve the previous tag with `gh release list`, download with
      `gh release download <tag> --pattern '*-x64-Setup.msi'`, and cache it keyed on the tag.
- [ ] Once green, make it a required status check and delete the manual step from
      [release-checklist.md](docs/releases/release-checklist.md) Phase 4.

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
- [ ] **Add a same-worktree A/B mode** for comparing two commits, so version comparisons cannot be
      contaminated by comparing two directories in different thermal states — the exact error that
      produced the v0.17.0 false alarm.
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
- [ ] Add a Viewer/Author/Publisher/Approver/Admin authorization matrix test suite.
      **Viewer/Publisher/Admin and all four ACL levels including Author are covered** by
      `AuthorizationMatrixTests`, written as data so a widened grant fails a `denied` row and a
      narrowed one fails an `allowed` row. Approver rows are pending because the role does not
      exist yet; it lands with the draft → approval → publish workflow below.

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
      **Started, not done.** Dialog behaviour is now shared (`js/dialog-a11y.js`) and the governance
      module's loading/unauthorized/failed/empty states are a reusable pattern, but headers,
      identity, module gating, spacing, icons, and status chips are still per-page. The three
      remaining inline focus traps in `index.html`, `admin.html`, and `orchestrator.html` should
      move onto the shared module too — they work, so replacing them needs per-page browser
      coverage of their dialogs first rather than a blind swap.
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
- [ ] Add accessibility assertions, critical visual snapshots, and API contract fixtures.
      Accessibility assertions are done (`PortalAccessibilityTests`). **API contract fixtures are
      done**: `BrowserApiContractTests` exercises the real endpoints and validates the responses
      against the same `critical-api-contracts.json` the browser validates against, reading the file
      rather than restating it — a C# copy would be a second source of truth that agrees until the
      day it quietly does not. The contract already existed but was only enforced *in the user's
      session*, so a server-side rename reached production and a `TypeError` on somebody's screen was
      the first thing that noticed. **Visual snapshots are the remaining work.**
- [ ] Run identical smoke suites against `dotnet run` and the production Docker image.
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
- [ ] Exclude generated review/build output from the container context and document a small seeded
      acceptance profile. **Exclusion done**: `tests/` (~14 GB of fixtures and corpora) and
      `artifacts/` were being packed and shipped to the Docker daemon on every build although no
      Dockerfile copies them. `ContainerBuildContextTests` guards both directions — nothing a
      Dockerfile copies may be excluded (that breaks the image build, and only for whoever builds a
      container next), and the large directories nothing copies stay excluded. `docs/` and
      `snippets/` are deliberately *not* excluded; both images copy them for the embedded runtime
      help. **The seeded acceptance profile is the remaining work.**
- [ ] Reconcile Portal architecture, isolation, administration, API inventory, policy matrices, HA
      diagrams, threat model, and verification runbooks against final source.
- [ ] Add release acceptance for browser, accessibility, responsive, local/Docker, departmental
      isolation, role/module/capability, and policy journeys.

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

- [ ] Federate a verifiable caller identity from Portal/OIDC; do not trust an identity header.
- [ ] Add per-object ACLs for `JOB`, `SCHEDULE`, and `NOTIFICATION` using the Portal grant vocabulary.
- [ ] Add enforceable ownership for shared names and prevent unauthorized `CREATE OR ALTER` takeover.
- [ ] Attribute every Orchestrator mutation audit record to a real principal rather than a service.
- [ ] Add negative tests proving a reachable Orchestrator does not imply authority over another
      principal's objects.

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

- [ ] Track disposition and replay jobs to a terminal state, or at minimum link each submission to
      durable job history.
- [ ] Replace `ParseRuleFailures` display-string parsing with structured per-column run metrics.
- [x] Add a read-only Portal API and panel showing which rules protect each target/column.
      `GET /api/data-quality/rules?jobName=` plus the rule inventory in the data-quality queue panel.
- [x] Add `eng.data_quality_rules` and make it queryable through Portal `eng.*` access. The engine
      table already existed; `eng.data_quality_rules(job)` now resolves over a `PORTAL` connection to
      the same seven columns, so one SELECT reads the same shape beside the engine or against the
      Portal. The job name is required — rules bind to the statement that declares them, so there is
      no catalog-wide answer.
- [ ] Measure preview-session startup and define an optimization threshold before enabling polling or
      dashboard refresh.
- [ ] If the threshold is exceeded, add a bounded reusable/read-only preview path without weakening
      parsing, linting, policy, RLS, timeout, row-cap, or redaction guarantees.

## Documentation
- [ ] Make sure everything above is documented.  We may want to follow our 4 path process.  How would a solo, team, enterprise, and Saas
      accomplish these items

## Pre-configured reports
- [ ] We have added a lot of standard reports in /samples  Which is great, we should add a way to install them automatically in portal with
      a checkbox.  Include reports, the reports are automatically configured and ready to run after install.