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

### Add a CI sample lane

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

---

## Roadmap execution backlog

These tasks decompose the future tracks in [ROADMAP.md](ROADMAP.md). Their presence here makes work
reviewable; it does not assign them to v0.18.0 or turn candidate phases into release commitments.
Keep the roadmap's P0/P1/P2 ordering unless a release plan explicitly changes it.

### Platform — Deployment Profiles and Upgrade Certification

#### P2 — Deployment-profile certification

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

- [x] **Portal ETL IDE Data Preview & Schema Browser**: add interactive schema inspection and bounded
      row previews of intermediate `#temp` tables and governed source connections. Cross-profile
      Studio capability: start with Solo/Team, require Enterprise connection ACLs, and certify tenant
      scope before enabling it in SaaS (SaaS domain 7).
- [x] **Portal Script Concurrent Editing Locks**: implement optimistic concurrency plus collaborative
      edit/session leases that warn authors and prevent silent overwrite. Team/Enterprise
      collaboration work; SaaS additionally requires tenant-scoped lease keys, hard expiry,
      disconnect recovery, and negative cross-tenant tests (SaaS domain 5).

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

### Engine — Script-Handled Data Quality Quarantine

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

### Engine — Additional Data Quality Rules

The current handler covers the fundamentals well. It already exceeds dbt’s four built-ins—not_null, unique, accepted_values, and relationships—with regex, numeric comparisons, deduplication modes, and arbitrary EXPR predicates. dbt’s official data-test documentation (https://docs.getdbt.com/docs/build/data-tests) confirms those four as its core set.

**All six shipped (2026-08-10), in the priority order below.** Notes worth keeping, because each was
found by building the thing rather than by reading:

- **Three silent-pass holes closed on the way through.** A composite rule naming an unprojected
  column read as NULL and skipped every row, so a typo in `UNIQUE WITH` disabled the rule instead of
  failing it. `TypeConverter.Cast` returns the value unchanged for a type it does not know, so
  `CASTABLE AS BANANA` would have accepted everything. And both per-row rule switches ended in a
  `default` that returned "passed", so a new `ColumnRule` record without its runtime arm would have
  reported clean data. All three now fail loudly. This is the same shape as the Portal defect
  pattern: the control exists, looks implemented, and is never asserted end to end.
- **`EXISTS IN` probed its key set with `Enumerable.Contains` and an explicit comparer**, which
  bypasses the `HashSet` and scans linearly — O(rows × keys) per statement. Fixed while adding the
  composite form.
- **`CASTABLE AS DECIMAL(18,2)` had to enforce the width itself.** The shared converter ignores
  precision, scale and string length, so sharing `TRY_CAST` semantics verbatim would have shipped a
  declaration that reads as a constraint and checks only "is a number".
- **The two rule switches were collapsed into one predicate** plus a thin async wrapper for the two
  forms that need the evaluator (`EXPR`, `BETWEEN`), rather than adding six rules to each of two
  copies.

The original recommendation, kept for the reasoning:

1.  **Composite referential integrity**
    ```sql
    TenantId /* @expect: 'EXISTS WITH (TenantId, CustomerId)
                           IN dim_customer(TenantId, CustomerId)'; */
    ```
    The current `EXISTS IN table(column)` implementation is single-column in `src/ETL-SQL.Core/Quality/ColumnRule.cs:76`. That can incorrectly accept CustomerId when it exists under a different tenant. This is the most important correctness and isolation gap.

2.  **NOT BLANK**
    ```sql
    Name /* @expect: 'NOT NULL, NOT BLANK'; */
    ```
    It should reject empty and whitespace-only strings while retaining the existing rule that NULL skips everything except NOT NULL. This is common enough that users should not need a regex or repeat the column name in EXPR.

3.  **String-length rules**
    ```sql
    PostalCode /* @expect: 'LENGTH BETWEEN 5 AND 10'; */
    ```
    Length checks are a standard validity category in both Great Expectations (https://greatexpectations.io/expectations/expect_column_value_lengths_to_be_between/) and Soda (https://docs.soda.io/sodacl-reference/validity-metrics). They can currently be written with `EXPR LEN(...)`, but a native rule gives clearer diagnostics and autocomplete.

4.  **Semantic type/castability**
    ```sql
    RawDate   /* @expect: 'CASTABLE AS DATE'; */
    RawAmount /* @expect: 'CASTABLE AS DECIMAL(18,2)'; */
    ```
    This is particularly useful during ingestion, where everything may arrive as text. It should share behavior with the existing `docs/reference/functions/conversion/try_cast.md:8`, not independently invent conversion semantics.

5.  **Negative membership and matching**
    ```sql
    Status /* @expect: "NOT IN ('UNKNOWN', 'N/A')"; */
    Notes  /* @expect: 'NOT MATCHES <script[^>]*>'; */
    ```
    These are expressible with EXPR, but explicit rules produce better lineage, diagnostics, and policy review. Great Expectations similarly provides both positive and negative set membership (https://greatexpectations.io/expectations/expect_column_values_to_not_be_in_set/).

6.  **Typed and relative ranges**
    ```sql
    EventDate /* @expect: 'BETWEEN DATEADD(DAY, -30, @RunDate) AND @RunDate'; */
    ```
    Current comparisons only accept decimal literals, as documented in `docs/reference/statements/dml/data-quality-rules.md:44`. A general BETWEEN rule could support numbers, dates, variables, and expressions.

### Orchestrator — Operations Triage and Run Flight Recorder

#### Deployment-profile portability review

Required by [Deployment_Profile_Standards.md](docs/architecture/standards/Deployment_Profile_Standards.md#feature-design-portability-review).
Smallest safe profile is **Solo**, and the capability must not become Portal-only.

- [ ] **Team.** The reference case for this track; no profile change expected. The 200-job shop above
      *is* the Team profile, and Scheduling/Observability are already Green here.
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

#### Phase A — Remaining portability CLI work

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

#### Isolation domains

Each domain states its **Dedicated** obligation and its **Shared** obligation, plus the Enterprise
contract it builds on where one exists. An entry is complete only when the matching matrix cell
carries a current linked evidence reference and the release review records the topology explicitly,
the way [v0.18.0](docs/releases/v0.18.0-deployment-profile-review.md) recorded its review. Do not
infer Dedicated support from an Enterprise happy path, or Shared support from Dedicated evidence.

##### 1. Tenant context and authority

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

##### 3. Policy, secrets, and keys

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

##### 4. Storage, paths, and artifacts

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
- [x] **Finish the `admin tenant` verb family** — `export` and `import`. Completed 2026-08-10 with
      the production Portal engine-replay and Orchestrator-store adapters, signed/encrypted export
      composition, dry-run and fail-closed collision planning, environment/`SECRET:` credentials,
      disabled imported workloads, generated CLI documentation, and narrow read-only export scope.
      This retained discovery entry is satisfied by Phase A Slice 3 above.
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
- [x] Add the syntax-addition checklist to `CONTRIBUTING.md`: parser/runtime, EBNF, docs/help/snippets,
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

- [x] **A low-threshold spill lane.** `scripts/test-lane.ps1 -Lane spill` re-runs the engine and SLT
      suites with spill/sort/batch thresholds set to a handful of rows, turning every query already
      in the corpus into spill coverage. `BatchSize` is 7 deliberately — not round, not a divisor of
      the corpus row counts — so batch boundaries fall inside logical groups.
- [x] **State the schema-stability invariant where it is established.**
      `ColumnBatchSchemaStabilityTests` covers both divergence modes against hand-built batches, in
      milliseconds, instead of relying on an exception thrown by the spill writer on data large
      enough to spill.
- [ ] **Make the corpus batch-size-agnostic so the spill lane can become a gate.** Its first run:
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
- [ ] **Give the lane a `-ContinueOnFailure` switch.** `Invoke-DotNetTest` exits on the first failing
      project (correct for a gate), so SLT never ran and one pass cannot produce a full triage list.
- [ ] **Vary data shape in the fuzzer.** Keep the grammar walk; add a seeded generator varying row
      count across the batch/spill boundaries, null density per column (0%, sparse, one entire
      batch, 100%), and type mix — including a column whose CLR type differs between batches, which
      is what the spill defect actually was.
- [ ] **A rule-catalogue property test.** Reflect over every `ColumnRule` and assert each can fail on
      a crafted row, and that misconfiguration (unknown type, unprojected column, arity mismatch) is
      a hard error rather than a rule that passes everything. Design decision 5 already states this
      as a principle; nothing enforces it across the set. Same shape as
      `EngineSubsystemCoverageTests`.
- [ ] **A general AST round-trip property.** Existing round-trip tests are per-feature, written with
      the feature, so `ON FAILURE` — added later — had none and the formatter dropped it silently.
      Enumerate `Statement` subclasses and assert parse → `ToSql` → parse preserves.

### Test baseline — pre-existing failures on `release/v0.18.0`

- [ ] **12 tests fail on the release branch itself**, verified by checking out `release/v0.18.0`
      clean (2026-08-10), so they are not regressions from any feature branch and release evidence
      cannot be collected until they are resolved or explicitly accepted:
      `SpillEncryptionTests.SpillEncryption_ShouldPersistAcrossSessions`,
      `AuthorshipPermissionBoundaryTests.AuthorshipComparisons_AreAllInventoried`, and ten in
      `ETL-SQL.Portal.Tests` (`ArchitectureDocReconciliationTests` ×2, `PolicyAuthorityApiTests`,
      `FleetWorkspaceAndExportPlanTests`, `PortalIntegrationTests`, `SharedDelegatedIdentityAdminTests`,
      `SharedTenantHttpBoundaryTests` ×2, `SupportBundleTests`, `UpgradePathDrillTests`).

## Bugs

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

- [ ] **A heterogeneous column cannot be persisted, and says so far from the cause.** Three of this
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

- [ ] **`BULK INSERT` does not check values against the target column, and `MAXERRORS` implies it
      does.** Loading `ID,Name,Dept,Salary,JoinDate` into `(id, category, price, qty)` paired the
      columns positionally and put `'HR'` into a decimal column, reporting `165 rows loaded. 0 errors
      skipped` — with `MAXERRORS = 0`. A load that silently accepts a value the column cannot hold
      is worse than one that fails, because `MAXERRORS = 0` reads as an assurance that it did not.
      Decide whether a column-count mismatch between file and target list is itself an error.

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
