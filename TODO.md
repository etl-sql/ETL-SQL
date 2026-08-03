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
- [ ] Add an isolation-safe Environments workflow that generates and validates deployment plans.
  - [ ] Keep provisioning in a separately authorized control plane or exported package.
  - [ ] Prove environment switching establishes separate sessions and never merges catalogs,
        datasets, connections, secrets, or authoring history.
- [x] Complete Stewardship and Audit routes using durable evidence.
- [x] Connect disposition/replay submissions to terminal job status.
- [x] Add data-quality rule visibility and structured failure trends.
- [x] Use one sanitized Markdown renderer for Docs and connector Help.
- [x] Replace native browser alerts/prompts/confirms with accessible, auditable feedback and dialogs.
- [x] Improve designer palette discovery, action hierarchy, toolbar labels, empty states, and
      laptop/tablet layouts.

#### Studio authorization and controlled publishing

- [ ] Add an `Author` resource grant that cannot alter ACLs, move/delete reports, or administer a
      folder.
- [ ] Implement deny-by-default, group/service-account-assignable Studio capabilities:
  - [ ] `StudioAccess` for discovery/open.
  - [ ] `ScriptRead` for source access.
  - [ ] `ScriptPreview` for analysis/completion/rendering.
  - [ ] `ScriptRun` plus existing shared-connection ACLs for interactive execution.
  - [ ] `ScriptSave` for drafts without implicit publish/commit/push.
  - [ ] `ReportPublish` for active-version publication.
  - [ ] `ScriptIngress` for external upload/import, disabled in catalog-only SaaS.
  - [ ] `SourceCommit` with actor, revision, diff summary, and correlation id.
  - [ ] `SourcePush` or deployment-service authority, separate and disabled by default.
- [ ] Include Studio capabilities in effective-permission diagnostics and mutation audits.
- [ ] Add a Viewer/Author/Publisher/Approver/Admin authorization matrix test suite.
- [ ] Add draft → review/approval → publish/commit/push with optimistic concurrency, protected
      branches, and separation of duties.

#### Enterprise administration coverage

- [ ] Add identity-provider diagnostics for reachability, claims/groups, sync health, and break-glass
      readiness without exposing client secrets.
- [x] Add a Service Accounts page with scope, expiry, last use, owner, rotation/revocation, one-time
      secret display, and audit history.
- [ ] Extend Policy Authority with fleet impact, approval state, collector consequences, and machine
      links to policy history.
- [ ] Show host enrollment/registration consistency, expiry, certificate posture, and remediation;
      keep enrollment/unenrollment on the host.
- [ ] Integrate secrets/connections with Studio checks, policy findings, rotation dates, and promotion
      plans.
- [ ] Add audit/security collector health, queue metrics, fail-closed state, and redacted test delivery.
- [x] Add native service enablement, schedule, recipient, last/next run, outcome, and history views.
- [ ] Show backup freshness and validation/restore-drill evidence while keeping custody and recovery
      outside the running Portal.
- [ ] Add online-safe diagnostics and an audited, redacted, review-before-download support bundle.
- [ ] Add a read-only Fleet/Operations workspace with compatibility, divergence, drain, migration,
      and upgrade evidence.
- [ ] Add guarded dataset-key inventory, rotation preflight/progress/verification, and rollback
      guidance without displaying key material.
- [ ] Add guided secret-free configuration export, target-plan validation, diff, approval, and audit.
- [ ] Add an access simulator explaining roles, groups, ACLs, connection grants, Studio capability,
      and RLS outcomes without returning protected rows.
- [ ] Verify the Environments workflow preserves separate departmental processes, databases,
      artifacts, key rings, identities, and endpoints.

#### P1 — Accessibility and visual-system completion

- [ ] Consolidate shared headers, identity, module gating, themes, spacing, icons, status chips,
      errors, loading states, and empty states into a shared component vocabulary.
- [ ] Make every dialog/drawer semantic, named, modal where appropriate, focus-trapped, and absent
      from the accessibility tree when closed.
- [ ] Add accessible names and keyboard behavior for search, favorites, script pickers, parameters,
      tabs, trees, palettes, tables, and cards.
- [ ] Verify light/dark, forced contrast, reduced motion, 200% zoom, and narrow viewports without
      clipping or color-only meaning.

#### P2 — Browser quality and delivery guardrails

- [ ] Add automated Chromium desktop and narrow-viewport lanes with seeded Viewer, Publisher,
      Steward, Operator, and Admin journeys. The lane and the Admin journey exist
      (`tests/ETL-SQL.Portal.BrowserTests`); the narrow viewport and the other four roles do not.
- [ ] Add accessibility assertions, critical visual snapshots, and API contract fixtures.
- [ ] Run identical smoke suites against `dotnet run` and the production Docker image.
- [ ] Fail on console errors, unhandled promises, broken Markdown, demo fallback, or horizontal
      overflow.
- [ ] Reuse representative UI sandbox stories as automated fixtures.
- [ ] Exclude generated review/build output from the container context and document a small seeded
      acceptance profile.
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

- [ ] Inventory and remove production demo fallback and browser-memory governance state.
- [ ] Define durable models and authorized APIs for findings, decisions, glossary terms, badges,
      scans, and scoring settings.
- [ ] Enforce resource/role authorization and security audit on every governance mutation.
- [ ] Wire the dashboard exclusively to durable APIs with honest loading, empty, unavailable, and
      failure states.
- [ ] Add API/role tests for mutation boundaries.
- [ ] Add UI tests for live, empty, unauthorized, and API-failure states.
- [ ] Add a guard proving production never presents demo records as governance evidence.

### Portal — Quarantine Row Access

- [ ] Decide and document whether preview requires the caller's connection grant or whether
      `DataQualityStewardAccess` plus a manifest-bound target is sufficient.
- [ ] Add nullable target connection alias, connector type, and catalog-backed provenance fields to
      `QuarantineReplayManifest` at capture time.
- [ ] Preserve backward compatibility by classifying missing provenance as view-only.
- [ ] Make target readability resolve enabled catalog entries with the caller's execution identity
      and the chosen authorization rule.
- [ ] Bootstrap preview only from manifest-owned `SHARED:` connection metadata; never trust request
      connection names or accept arbitrary SQL.
- [ ] Preserve the 15-second timeout, row cap, RLS identity, and redacted errors.
- [ ] Gate connection preview behind `Portal:DataQuality:AllowConnectionPreview`, default off.
- [ ] Audit every raw quarantine preview as a data-access event.
- [ ] Add a positive catalog-backed preview test first.
- [ ] Add tests for catalog miss, disabled entry, switch off, unauthorized identity, legacy manifest,
      row cap/timeout, and failure-path redaction.
- [ ] Document preview eligibility, the kill switch, authorization, and audit behavior.
- [ ] Add readable and view-only data-quality queue sandbox fixtures.

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