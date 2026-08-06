# ETL-SQL Product Roadmap

This document tracks high-level product tracks and candidate phases. Their actionable work is
decomposed in `TODO.md`. Once an initiative is verified, record its notable outcome in
`CHANGELOG.md` and mark its retained progress entry complete; do not erase the historical progress
signal. Release-specific detail belongs in the release notes under `docs/releases/`.

The enterprise operating model, authority hierarchy, trust boundaries, and progressive deployment
promise are defined in
[`docs/architecture/roadmaps/Enterprise_Platform_Strategy.md`](docs/architecture/roadmaps/Enterprise_Platform_Strategy.md).

---

## Completed roadmap progress

These verified initiatives remain visible here for product-level progress review. Their detailed
implementation tasks and verification state are retained in `TODO.md` and their shipped outcomes in
`CHANGELOG.md`.

- [x] **Workstation-to-Enterprise quality and stewardship:** local policy, CLI quality evidence,
  durable `eng.*` quality/stewardship read models, PII scanning, reproducible scores, operator
  reports, the one-person loop, and cross-profile parity/security fixtures.
- [x] **Deployment profile contract and supported transitions:** Solo/Team/Enterprise/SaaS coverage,
  portable-state rules, secret-safe promotion, direct/progressive SaaS onboarding, and N → N+1
  lifecycle drills.
- [x] **Portal critical-journey foundations:** generated API contracts and Admin casing repair,
  recognizable shared identity, removal of demo governance evidence, parameterized first-run flow,
  and responsive global navigation/content patterns.
- [x] **Portal consumer home and discovery:** favorites, recent, featured, popular, fuzzy catalog
  search, intentional report icons, and concise activity state.
- [x] **First-class Portal Studio foundation:** explicit deployment modes and capabilities,
  catalog-scoped authoring, equal Code/Design entry, internal catalog report creation, and disabled
  or catalog-only route/navigation trust boundaries.
- [x] **Administration Operations hub:** durable fleet/workload signals, report-access decisions,
  service-account lifecycle and audit history, token-safe anonymous access inventory/revocation,
  and native administrative service schedules and run history.
- [x] **Durable Stewardship and Audit routes:** first-class Governance navigation and stable hash
  routes now open the existing lineage, protected-data, stewardship, impact, audit-log, and outbox
  evidence; Audit is administrator-only and no longer masquerades as an ordinary Lineage tab.
- [x] **Terminal quarantine-work tracking:** replay and disposition submissions retain their job
  identity across refreshes, follow the durable execution ledger through terminal status, expose
  sanitized failure evidence, and refresh affected queue/row state only after completion.
- [x] **Portal rule inventory and normalized quality trends:** stewards can inspect every parsed
  rule protecting a job's output columns, including rules that never failed, while trend totals use
  normalized durable failure rows with target, action, and owner instead of reparsing display text.

## Future Candidate Phases

### Platform — Deployment Profiles and Upgrade Certification

Build the profile, portability, and certification program defined in
[`Deployment_Profile_Strategy.md`](docs/architecture/roadmaps/Deployment_Profile_Strategy.md).
Treat **Solo / Workstation**, **Team / SME**, **Enterprise / Corporate**, and
**SaaS / Multi-Organization** as cumulative support profiles rather than editions.

#### P2 — Add deployment-profile certification

1. Retain commit-bound JSON and Markdown evidence under `certification-results/` with topology,
   artifact hashes, mapping decisions, continuity counts, negative isolation results, and
   rollback/restore outcomes.
2. Add the profile/transition matrix to release claims. A capability is not certified for every
   deployment merely because its Solo or Enterprise test passes; each applicable profile and
   transition needs its own current evidence.

**Definition of done.** A user can start with source-controlled artifacts on one workstation,
promote them to a shared team service, add corporate identity/policy/audit/HA, or onboard them
directly or progressively into an isolated SaaS tenant without rewriting pipeline or report logic.
Every supported profile passes N → N+1, every promotion path preserves and reconciles its declared
portable state, and SaaS evidence proves tenant isolation rather than inferring it from
configuration.

### Portal — Comprehensive Product and UX Update

**Review basis (2026-07-26).** Walked the production Portal in both a local development host and
the repository Docker image, at desktop and 390px mobile widths. The review covered login, report
catalog and search, publishing, parameterized execution, subscriptions, the visual designer,
governance, lineage, documentation, Orchestrator status, and all eleven administration areas. It
also cross-checked the browser behavior against the controllers, UI source, and current test
guidance.

The Portal has a strong foundation worth preserving: the desktop card/table visual language is
coherent, focus treatment and empty states are generally clear, report publishing and script
validation work end-to-end, and the designer, lineage, connection catalog, secret store, policy
authority, subscriptions, datasets, ACLs, and Orchestrator integration expose substantial product
depth. The next update should make that depth feel like one trustworthy product rather than add
another set of isolated capabilities.

#### P1 — Connect the product into coherent workspaces

1. [x] **Administration and operations hub.** Add visible, role-gated workflows for the backend
   capabilities that currently have no coherent browser home: service accounts and secret rotation,
   pending access approvals, anonymous share/embed inventory, fleet/node status, operational
   metrics, and administrative service runs. Join these with health, audit, outbox, and Orchestrator
   context so an operator can move from a symptom to the responsible job or node.
2. **Surface departmental environments without weakening their isolation.** The shipped
   departmental model is deployment isolation, not shared-table multitenancy: every department owns
   a separate Portal database/login, Orchestrator database/login, artifact root, key ring, signing
   and dataset keys, service identity, and network endpoint. Multiple department instances may run
   on the same physical Portal hosts or HA server pool, but they must not share a Portal process,
   database, artifact namespace, key ring, or service identity. Add an Admin **Environments**
   workflow that can generate and validate a deployment plan from an environment id, show isolation
   verification evidence, and link to the read-only fleet status. Provisioning must go through a
   separately authorized deployment control plane or an exported deployment package—not through a
   `FleetReader` credential and not by granting one department access to another. An optional
   environment chooser may list only environments the signed-in identity is entitled to enter;
   each selection establishes that environment's own session and must never merge report catalogs,
   search results, datasets, connections, secrets, or authoring history.
3. **Finish the data-steward journey.** Keep the real lineage and quarantine views, make
   Stewardship and Audit genuine routes, and connect disposition/replay submissions to job status.
   Add rule visibility and structured failure trends. Governed quarantine row access is specified
   separately in [Portal — Quarantine Row Access](#portal--quarantine-row-access).
4. [x] **Use one documentation renderer.** Docs and connector Help now use the same sanitized
   renderer for headings, tables, admonitions, code blocks, allow-listed links, and copy actions;
   Docs retains its topic search and section filters.
5. [x] **Use one feedback and dialog system.** Reports, Admin, Governance, Designer,
   Orchestrator, ReportPlayer, Workstation, and VS Code report surfaces now share accessible toasts
   and focus-trapped dialogs. Password reset, destructive changes, policy rollout, file naming,
   dataset naming, and source-control actions include validation, impact text, and non-secret
   audit-action events.
6. [x] **Polish the visual designer without reducing its power.** The full visual library is now
   searchable and grouped, neutral palette actions retain small type-color markers, primary
   toolbar actions have visible labels, empty datasets/pages/canvases explain the next action, and
   laptop/tablet layouts preserve usable canvas and inspector space.

#### Studio authorization model

Studio permissions should overlay resource ACLs; they should not turn `Publisher` or folder
`Manage` into an all-purpose development credential.

| Action | Required authority |
| :--- | :--- |
| Discover/open Studio | Designer module + deployment authoring policy + `StudioAccess` |
| Read report source | `ScriptRead` + report/folder `Author` or `Manage` |
| Analyze, complete, and render a preview | `ScriptPreview` + source-read authority |
| Run an interactive selection | `ScriptRun` + source-read authority + existing shared-connection ACLs; retain the read-only/`#temp` execution policy |
| Save a draft | `ScriptSave` + report/folder `Author` or `Manage`; saving must not publish, commit, or push implicitly |
| Publish or replace the active report version | `ReportPublish` + target-folder authority |
| Upload/import outside source | `ScriptIngress`; disabled in the catalog-only SaaS profile |
| Commit to repository | `SourceCommit` + source-save authority; record actor, revision, diff summary, and correlation id |
| Push or promote a branch | `SourcePush` or a deployment service identity; separate from commit and disabled by default |

Add an `Author` resource grant so report editing does not require permission to change ACLs, move or
delete reports, or administer the entire folder. Capabilities should be assignable to groups and
service accounts, included in effective-permission diagnostics, auditable, deny-by-default for new
tenants/environments, and tested as a matrix across Viewer, Author, Publisher, Approver, and Admin.
For controlled production, support a draft → review/approval → publish/commit/push workflow with
optimistic concurrency, protected branches, and separation of duties.

#### Enterprise administration coverage audit

The enterprise guides are strong on command-line and configuration runbooks, but several shipped
Portal APIs and health contracts have no corresponding browser workflow. Portal coverage should be
added where the operation is safe while the Portal is online; host-bootstrap, recovery, and
containment operations must remain out-of-process.

| Enterprise area | Current Portal coverage | Portal update and boundary |
| :--- | :--- | :--- |
| OIDC and LDAP identity | Federated login, provisioning, and group sync are implemented; OIDC diagnostics is an API, while provider configuration is file/environment based. | Add identity-provider status, callback/issuer reachability, claim and group-mapping test results, sync health, and break-glass readiness. Never return client secrets; stage high-risk configuration changes and show restart impact. |
| Service accounts | CRUD, secret rotation, revoke, scoped-token issuance, middleware enforcement, and the Admin Operations inventory are implemented. | The page shows scope, expiry, last use, owner, rotate/revoke, one-time secret display, and resource-filtered audit history. |
| Policy authority and machine registry | The Admin Policy Authority page covers validation, publication, activation, canaries, rollback, and machine registration/revocation. | Preserve this as the model enterprise surface; add fleet impact, approval/separation-of-duty state, collector consequences, and links from affected machines to policy history. |
| Host enrollment | `etl-sql enterprise enroll/status/unenroll` is an elevated host command; the Portal registers the corresponding machine identity. | Show enrollment and registration consistency, expiry, certificate posture, and remediation instructions. Keep enrollment/unenrollment on the host because it owns an OS-protected bootstrap and is intentionally outside lower-authority Portal configuration. |
| Secrets and shared connections | Strong Admin pages already support write-only secrets, masked connections, verify, enable/disable, impact, ACLs, and metadata promotion. | Retain and integrate them with Studio capability checks, policy findings, rotation due dates, and cross-environment promotion plans. |
| Audit outbox and security-event delivery | Audit rows are visible; outbox and security-event diagnostics are emitted through health, Prometheus, and fleet status, but have no operator workspace. | Add collector status, pending/failed counts and bytes, oldest age, last attempt/success, fail-closed threshold state, and a redacted test-delivery workflow. Security-event collector configuration remains signed organization policy. |
| Native failure, backup, and capacity services | The Operations page shows enablement, schedule, recipients/SMTP alias, last/calculated-next run, outcomes, and durable history. Configuration remains file based. | Add staged configuration with validation and an explicit apply/restart contract only if live configuration becomes a supported product capability. |
| Backups and restore drills | Split-custody backup/restore and validation are CLI operations; the native backup service records age/failure evidence. | Show last successful backup, freshness policy, archive/manifest identifiers, validation and restore-drill evidence, and alerts. Keep backup custody, restore, and destructive recovery outside the running Portal. |
| Doctor and support bundles | `etl-sql doctor` and redacted support-bundle generation are CLI-only. | Add an Admin Diagnostics page for the online-safe checks and an audited, redacted support bundle with an explicit review-before-download step. Keep the CLI path as the recovery option when Portal is unavailable. |
| HA, fleet, migrations, and upgrades | Readiness, node heartbeats, fleet status/aggregation, compatibility metadata, migration ownership, and upgrade reports exist in backend contracts; no unified Portal view exists. | Add the read-only Fleet/Operations workspace, upgrade preflight/postflight evidence, node divergence, drain guidance, and migration owner/status. Package deployment, database migration, HA soak, and traffic control remain external operator actions. |
| Dataset at-rest keys | Rotation and recovery behavior are documented and an Admin rotation endpoint exists; no Admin workflow exists. | Add key-version inventory, preflight, impact count, guarded rotation, progress/failures, post-rotation verification, and rollback instructions without ever displaying key material. |
| Configuration export and promotion | A secret-free configuration export API and script-first replay path exist; no guided Portal workflow exists. | Add export, target-plan validation, diff, unsupported-resource summary, approval, and audit. Do not turn it into database backup or move secrets/datasets between isolated environments. |
| RLS and effective access | Report/folder effective permissions are queryable and row filtering is enforced at execution; the Portal does not provide one complete access explanation. | Add an identity access simulator that explains role, group, folder/report ACL, connection grant, Studio capability, and RLS outcome without returning protected rows. |
| Departmental isolation | Deployment templates and verification enforce separate environments; the Portal exposes only read-only fleet status. | Implement the isolation-safe Environments workflow described above, while keeping cross-environment provisioning in a separately authorized deployment plane. |

#### P1 — Accessibility and visual-system completion

- Consolidate the duplicated page headers, identity display, module gating, theme control, spacing,
  icons, status chips, errors, loading states, and empty states into a shared Portal shell and
  component vocabulary. Avoid mixing product icons, CSS glyphs, and emoji as primary controls.
- Give dialogs `role="dialog"`, `aria-modal`, an accessible name, focus trap/restoration, and
  correct hidden-state behavior. Closed Governance modals and the Orchestrator detail drawer must
  not remain in the accessibility tree.
- Name report search, favorite actions, script-picker rows, and report-parameter controls; support
  keyboard activation and arrow-key behavior for tabs, trees, palettes, tables, and card actions.
- Verify light, dark, forced/high-contrast, reduced-motion, 200% zoom, and narrow-viewport behavior
  without horizontal page clipping or information conveyed by color alone.

#### P2 — Browser quality and delivery guardrails

1. Broaden the automated browser lane. The lane itself now exists (`test-lane.ps1 -Lane browser`,
   Chromium against a Kestrel-hosted Portal) and carries the one critical admin journey. It still
   needs a narrow viewport and seeded Viewer, Publisher, Steward, and Operator journeys alongside
   the Admin one.
2. Add accessibility assertions (including no hidden modal content), visual snapshots for the
   shared shell and critical empty/error/data states, and request/response contract fixtures.
3. Run the same smoke suite against `dotnet run` and the production Docker image. Treat console
   errors, unhandled promise rejections, broken Markdown, demo-data fallback, and horizontal page
   overflow as failures.
4. Keep the manual UI sandbox for fast component development, but make its representative stories
   fixtures for the automated lane rather than a separate source of truth.
5. Tighten container build hygiene so generated Portal review data and repository build outputs do
   not inflate the Docker context, and document a small seeded review profile for repeatable product
   acceptance.

#### Suggested sprint sequence

1. **Shell and contracts:** shared identity/navigation/theme shell, Admin Users fix, generated API
   contract, and first end-to-end login/admin smoke.
2. **Studio authorization and publishing:** complete group/service-account capability assignment
   and `Author` resource grants, then add review/promotion flow and end-to-end
   create/edit/validate/run/save/publish/commit coverage.
3. **Responsive and accessible foundations:** semantic
   dialogs/drawers, keyboard/focus work, and shared feedback components.
4. **Governance, enterprise operations, and environments:** remove demo evidence, finish
   steward/audit routes, connect job status, implement the enterprise coverage matrix above, and add
   the isolation-safe departmental environment workflow.
5. **Docs and designer polish:** shared Markdown renderer, designer hierarchy/discoverability, and
   final visual consistency pass.
6. **Architecture and administration documentation:** after the implementation and contracts have
   stabilized, reconcile `Docs/Architecture/Portal.md`,
   `docs/architecture/decisions/Departmental_Isolation.md`, the Portal administration guides, API
   inventory, module/authoring policy matrix, HA diagrams, isolation threat model, and deployment
   verification runbook with the shipped behavior. Architecture diagrams and interface contracts
   must be checked against the final C# source rather than copied from this roadmap.
7. **Release gate:** browser, accessibility, responsive, local/Docker parity, departmental
   isolation, and role/module/authoring-capability/policy acceptance runs.

**Definition of done.** A first-time Viewer can find and run a parameterized report without
instruction; a Publisher can validate, publish, design, and diagnose it; a Steward sees only real,
durable governance evidence and can follow remediation work to completion; and an Admin/Operator
can identify users, services, nodes, access requests, and failures without dropping to direct API
calls. Those journeys pass with keyboard-only use at desktop and 390px widths, under both the local
host and production container, with no native browser dialogs, hidden interactive content, demo
fallback, uncaught client errors, or horizontal page overflow. In a catalog-only SaaS profile,
authorized authors can work entirely in Studio while outside script ingress is rejected; in a
departmental topology, a cross-environment identity cannot discover or access another environment's
reports, datasets, connections, secrets, artifacts, or authoring state.

### Portal — Authorship Is Not Permission (permission-model consistency)

**Origin (2026-07-26).** During the v0.17.0 release gate, two pre-existing security tests caught
report authorship being treated as *standing permission*: a user removed from every group kept full
access to reports they had authored, kept seeing them in the catalog, kept the ability to approve
other people's access requests, and the anonymous share/embed links they had issued kept resolving.
Four sites were fixed in v0.17.0 (`GetEffectiveReportPermissionAsync`, `CreatorCanResolveAsync`,
`CatalogController.VisibleReportsQuery`, and the access-request approve/deny endpoints), and the rule
is now pinned by `ReportAuthorshipRevocationTests`.

The **same pattern remains in shipped code for datasets**, where it was not introduced by v0.17.0 and
so was deliberately left alone rather than changed mid-release without test coverage:

- `DatasetPermissionService.GetEffectivePermissionAsync` and its `Evaluate` helper both return
  `DatasetPermission.Owner` whenever `dataset.CreatedBy == userId` or
  `dataset.OwningReport?.CreatedBy == userId`, before consulting any ACL or folder permission.
- `ReportDependencyService` (~line 94) short-circuits on `dataset.OwningReport?.CreatedBy`.

The work:

1. **Decide the intended rule explicitly and write it down.** The report model now says authorship
   *upgrades* an existing grant but never substitutes for one. Datasets should either adopt that rule
   or document why they differ — the current inconsistency is undocumented and looks accidental.
2. **Write the dataset revocation tests first.** Dataset authorship is load-bearing for publishing and
   registry flows that currently have no coverage for the revocation case, which is exactly why this
   was not changed during the release. Mirror `ReportAuthorshipRevocationTests`: a creator removed
   from every group loses dataset access, and one retaining a lesser grant keeps the upgrade.
3. **Apply the rule** to both `DatasetPermissionService` paths and `ReportDependencyService`.
4. **Add an architecture test that fails on new unconditional authorship short-circuits** — a rule
   over the permission services and controllers flagging `CreatedBy ==` / `OwnerId ==` comparisons
   that return a permission or `true` without also consulting an ACL. This is the class-level guard;
   all four v0.17.0 sites would have tripped it at the commit that introduced them, instead of being
   found by two unrelated tests during a release gate.
5. **Audit the remaining ownership surfaces** for the same shape — connection ACLs, subscriptions,
   alerts (`ReportAlerts` filters on `OwnerId == CurrentUserId`), and saved views.

**Definition of done.** Removing a user from every group, or from the directory, demonstrably revokes
every report, dataset, connection, subscription, alert, saved view, and anonymous link they created,
with a test per surface; and a newly-introduced authorship short-circuit fails the build rather than
a release gate.

### Orchestrator — Per-Object Authorization

**Origin (2026-07-27).** Surfaced while designing the unified job/schedule/notification model
([job_schedule_notification.md](docs/architecture/decisions/job_schedule_notification.md)). Making the
Orchestrator the system of record for `JOB`, `SCHEDULE`, and `NOTIFICATION` moves durable, mutable,
operationally significant objects into a store whose API authenticates with a **single shared key**
(`X-Orchestrator-Key`). It has no user or group model at all.

The consequence: anyone who can reach the orchestrator connection can create, alter, disable, or drop
**anyone's** job. The only boundary is the use-ACL on the orchestrator connection in the Portal's
governed catalog, which is connection-level, not per-object. That is a real asymmetry with the
Portal, which enforces per-object RBAC — and it is a deliberate deferral, not an oversight.

**Why it is acceptable for now:** the Portal is the only client, and it authenticates as a single
principal. Per-object ACLs against one subject would be authorization theatre.

**What ships in v0.18.0 instead — attribution, not authorization.** The Portal passes the acting
user's identity through on every mutation, and the Orchestrator records `CreatedBy` / `ModifiedBy` on
the job, schedule, and notification rows. One column each, purely additive, no identity model
required. It makes "who scheduled this?" answerable — the question that will come up first — and it
makes a silent takeover (see below) visible after the fact.

**The trigger to build real authorization** is a second client, or one Orchestrator shared across
teams or tenants. At that point the Orchestrator needs an identity model, which realistically means
federating to the Portal's or directly to OIDC rather than inventing a third one. Sequence it with
the enterprise identity work in `docs/guides/administration.md`.

The work, when triggered:

1. **Federate identity** rather than duplicating it — the caller's identity arrives as a verifiable
   token, not a trusted header, which is the difference between authorization and attribution.
2. **Per-object ACLs** on `JOB`, `SCHEDULE`, `NOTIFICATION`, reusing the Portal's grant vocabulary so
   there is one permission model to reason about, not two.
3. **Ownership on the shared-name hazard.** Names are unique per orchestrator and `CREATE OR ALTER`
   is supported, so a second script importing an existing name silently takes the object over rather
   than erroring. Until ACLs exist this is mitigated socially — naming conventions, a category in
   `OPTIONS`, and the attribution columns above. Ownership makes it enforceable.
4. **Audit parity** with the Portal: every mutation attributable to a real principal, not to "the
   Portal".

**Definition of done.** A user who can reach an orchestrator cannot mutate a job they do not own, the
Orchestrator's audit records name a person rather than a service, and the permission vocabulary is
the Portal's rather than a second one.

### Portal — Governance Dashboard

Finish the data-steward-first dashboard described in
[`Governance_Dashboard_Strategy.md`](docs/architecture/roadmaps/Governance_Dashboard_Strategy.md).
The current production module is a visual prototype: it substitutes demo assets when the
stewardship API fails and keeps findings, decisions, glossary terms, badges, scans, and scoring
settings only in browser memory.

Replace those placeholders with authorized, audited, durable Portal APIs. The work is complete only
when role and API tests cover the mutation boundaries, UI tests cover live and failure states, and
the production surface never presents demo records as governance evidence.

### Portal — Quarantine Row Access

**Problem.** `DataQualityController.GetQuarantineRows` runs `SELECT * FROM {target}` inside a fresh
in-process `ExecutionSession`. That session is constructed with an empty connection dictionary and
never calls `Evaluator.LoadSessionState`, so it restores nothing from the producing run: no
connections, no temp tables, no session variables. Every real capture target therefore fails —
a connection-qualified target (`warehouse.dbo.quarantine_users`) raises `Unknown source: warehouse`,
and a `#temp` target is silently auto-created as an empty in-memory table, which is worse: the
steward reads "no rows" as "nothing was quarantined". Pre-projection capture plus in-Portal editing
is the strongest part of the remediation workflow, and it is unavailable exactly where quarantine
data actually lives.

The current queue marks these targets **View only**, explains why, and provides review SQL to run
where the connection exists. The remaining product gap is governed, in-Portal access to durable
catalog-backed targets.

**Chosen direction: catalog-backed preview.** Resolve the target through the shared connection
catalog rather than widening the Portal's reach generally.

| Option | Verdict |
| :--- | :--- |
| Rehydrate the producing job's `SessionState` into the preview session | Rejected — restores *every* connection an arbitrary job held, with no bound tied to the manifest, and the state may no longer exist. |
| Resolve the target's connection from the catalog as `SHARED:alias` | **Chosen** — governed path; flows through `SharedConnectionExpander` → `ConnectionSecretResolver` → `ConnectorPolicyAuthorizer`, so policy, secret resolution, and redaction all apply unchanged. |
| Round-trip the read through the orchestrator as a job and return its result set | Deferred fallback — covers ad-hoc script connections the catalog does not know, but needs a result-returning job path and turns an interactive read into an async one. |

Slices:

1. **Manifest provenance.** Add nullable `TargetConnectionAlias`, `TargetConnectorType`, and
   `TargetIsCatalogBacked` to `QuarantineReplayManifest`, written at capture time. Backward
   compatible in the same way the replay-mode fields were: absent means "unknown", which classifies
   as view-only.
2. **Readability consults the catalog.** `QuarantineTargetReadability` gains an
   `IConnectionCatalogProvider` and the caller's `ExecutionIdentity`, and reports readable only when
   the alias resolves, is enabled, and the caller is authorized for it. Every other case keeps its
   existing reason string, so the interim UI needs no change.
3. **Preview session bootstrap.** Prepend
   `CREATE CONNECTION {alias} AS {type}('SHARED:{alias}');` to the preview script. The alias comes
   from the manifest, never from the request, and the statement is still only
   `SELECT * FROM {manifest target}` — not arbitrary SQL. Keep the 15s timeout,
   `MAX_LAST_RESULT_ROWS`, the RLS execution identity, and `SecretRedactor` on the error path.
4. **Kill switch and audit.** Gate the whole path behind `Portal:DataQuality:AllowConnectionPreview`
   (default **off**, so an upgrade does not silently start opening production connections from the
   web tier), and audit each preview read the way dispositions are audited today — reading raw
   quarantined source rows is an access event, not a page view.
5. **Tests.** A **happy-path** read is the first requirement, not the last: every existing
   `quarantine/rows` test asserts a rejection, so the catalog-backed path needs positive coverage
   before it can be considered functional. Then: catalog miss, disabled entry, feature switch off,
   unauthorized identity, and a redaction assertion on the failure path.
6. **Docs + sandbox.** Administration guide: which connections become previewable, what the switch
   does, and what is audited. Flip the sandbox's view-only fixture to a readable catalog-backed
   target so both states stay developable
   (`tools/ui-sandbox/stories/data-quality-queue.story.js`).

Open decision for the sprint: whether a steward reviewing rows through a catalog connection should
be limited to connections their own role can already reach, or whether `DataQualityStewardAccess`
plus a manifest-bound target is authority enough. This changes slice 2's authorization check.

### Portal — Data Quality Follow-through

These lower-level data-quality findings support the comprehensive update above. Ordered by how much
each affects day-to-day use.

1. [x] **Submitted jobs stay connected.** Replay and disposition jobs persist in the browser session
   and follow the durable execution ledger to Completed, Failed, or Cancelled.
2. [x] **Trends use normalized failure rows.** New runs read structured target/column/rule/action/
   owner/count records; the compact display string remains only as compatibility fallback for older
   history.
3. [x] **Rules are visible in the Portal.** The read-only rules endpoint parses the governed job
   script and the trend panel lists every protection, including rules that have not failed.
4. **Every preview spins a full engine.** Each request lexes, parses, lints, and evaluates through a
   new `ExecutionSession`. Acceptable at current volume; worth revisiting before any endpoint like
   this becomes a polled or dashboard-refreshed surface.

### SaaS Multi-Tenancy — Secure Outbound Data Gateway

To transition the ETL-SQL platform from a logical Enterprise farm into a hardened, multi-tenant SaaS service, we must introduce secure, outbound-only connectivity for on-premises client networks. This eliminates the need for complex, slow-to-configure site-to-site VPNs or insecure inbound firewall exceptions.

#### Core Architecture & Components:
1. **The Outbound Gateway Daemon (`etl-sql-gateway.exe`)**:
   - A dedicated, lightweight Windows Service and Linux systemd daemon that installs inside the client's private network.
   - It maintains a persistent outbound-only WebSocket or gRPC tunnel over standard HTTPS (Port 443) back to the SaaS Orchestrator.
   - It performs no local script evaluation or compilation (highly restricted security footprint), acting purely as a query and file-stream conduit.

2. **The `ROUTE='GATEWAY:name'` Parameter**:
   - Introduces the `ROUTE` parameter to all connection definitions (e.g. `FLATFILE`, `MSSQL`, `POSTGRES`).
   - *Local Dev (Solo Profile)*: The local engine detects the gateway route but bypasses it to resolve UNC paths and database addresses natively, ensuring zero-friction local development.
   - *SaaS Profile (Cloud Container)*: The engine query planner intercepts the connection, matches the gateway name to the active tenant's open tunnel, and streams all read/write database queries or file blocks through the secure WebSocket conduit.

3. **Hybrid SaaS Execution Model**:
   - The ETL-SQL script compilation, scheduling, and orchestrator coordinates inside the SaaS cloud container.
   - Queries targeting local files or private databases are dynamically proxied down to the local gateway daemon, allowing combined cloud and on-premises execution within a single script.

### SaaS Multi-Tenancy — Containerized Data Plane (Compute Isolation)

To secure CPU, memory, and disk IO resources in a shared multi-tenant SaaS fleet, the core script execution engine is decoupled from the web control plane via containerized sandboxing.

#### Core Architecture & Components:
1. **Execution Provider Abstraction (`IEngineExecutionProvider`)**:
   - Introduces a pluggable query runner abstraction.
   - *Solo / Team Profiles*: Defaults to the `InProcessEngineExecutionProvider`, running directly within the local process with zero dependencies.
   - *SaaS / Enterprise Profiles*: Leverages the `ContainerizedEngineExecutionProvider` to execute queries inside isolated OS container tasks.

2. **Pre-Warmed Keep-Alive Container Pools (Interactive Runs)**:
   - To avoid the 1-3 second container startup latency ("cold start") when a developer runs a query in the Portal Web Editor, the Portal manages a pool of pre-warmed tenant-assigned containers.
   - Interactive queries execute instantly. The assigned container automatically spins down after 15 minutes of inactivity to conserve host resources.

3. **Ephemeral Job Containers (Scheduled Tasks)**:
   - For scheduled nightly pipelines run by the Orchestrator, the system provisions a fresh, isolated container task, executes the script to completion, and immediately tears it down. This ensures complete state isolation and guarantees that a memory or disk-spill leak from one tenant cannot impact another.

### SaaS Multi-Tenancy — Tenant Portability & Migration (Export/Import)

To guarantee customer agency, facilitate seamless onboarding, and eliminate SaaS vendor lock-in, the platform includes a zero-loss tenant migration utility.

#### Core Architecture & Components:
1. **The Portal Tenant Packaging Engine**:
   - An administrative service that gathers all tenant-owned metadata, definitions, and assets, including:
     - All `.etlsql` and `.rptsql` scripts.
     - Scheduled job definitions, frequencies, and dependency rules.
     - Connector schemas, connection options, and parameters (excluding raw secrets or passwords, which are encrypted or parameterized).
     - Execution lineage data, history graphs, and quarantine schema definitions.
2. **`etl-sql admin tenant export` / `import`**:
   - A command-line utility command that compresses the packaged tenant assets into a single encrypted, signed zip bundle.
   - This bundle can be imported directly into:
     - Another tenant workspace in a different SaaS cluster.
     - A private corporate on-premises **Enterprise** environment.
     - A developer's local workstation running in **Solo** mode.
3. **Data Upgrades during Migration**:
   - The import processor verifies the version manifest of the import file and automatically transforms the metadata database schemas if importing into a newer version of the platform.

### Language — Dialect Standardization and Open-Source Governance

To secure the portability guarantee across diverse runtime environments and enable compliant third-party implementations (e.g., in Rust or Go), the ETL-SQL language dialect is formalized into a tool-driven, open-source standard.

#### Pillars of Standardization:
1. **Canonical Grammar Specification (EBNF)**:
   - Define and publish a machine-readable EBNF (Extended Backus-Naur Form) or ANTLR grammar file in the repository.
   - This file serves as the single source of truth for lexicographical parsing, preventing implementation drift and simplifying parser generator integration for other programming languages.
2. **Conformance Test Suite (SqlLogicTests)**:
   - Establish and expand a shared suite of SqlLogicTests (SLT) asserting exact execution, mathematical offsets, standard library function results, and query boundaries.
   - A runtime parser/compiler is deemed compliant only when it successfully passes the complete SLT suite.
3. **Change-Control Governance (RFC Process)**:
   - Introduce a structured RFC (Request for Comments) process for language syntax extensions.
   - Proposals for new keywords, functions, or connection options must demonstrate cross-dialect compatibility, translation mappings for remote SQL pushdown, and EBNF syntax updates before approval, protecting the language core from syntax bloat.

### Connectors — Transactional File Staging

To prevent downstream systems from consuming half-written or dirty data on execution failure, file-based and network transfer connectors (e.g. `FLATFILE`, `SFTP`) will support native transactional staging boundaries.

#### Core Design & Parameters:
1. **`TRANSACTIONAL=TRUE` Configuration Option**:
   - Enable transactional staging on connection creation.
   - The engine writes target data blocks to temporary `.tmp` files (or in a hidden `.staging/` directory at the destination) during the active execution stream.
2. **Atomic Commits & Automatic Cleanups**:
   - If the script execution completes successfully, the engine issues a fast atomic rename (e.g. `file.csv.tmp` -> `file.csv`) to expose the complete file.
   - If the script fails during any phase (e.g., in a `load:` block), the engine automatically cleans up and deletes the staged files, leaving the production directory in its original clean state.

### Connectors — External Command Pipe (CMD Connector)

To address the "last mile" problem where custom scripts (Python, PowerShell, Bash, binary executables) are required for legacy data formats or proprietary processing, we introduce the `CMD` connector.

#### Core Design & Parameters:
1. **The `CMD` Connection Schema**:
   - Create an external handler: `CREATE CONNECTION py_cleaner AS CMD(EXEC='python sanitize.py', FORMAT='JSON')`.
   - The process communicates via streaming standard inputs (`stdin`) and standard outputs (`stdout`).
2. **Streaming Execution Flow**:
   - Developers can pipe tabular data directly into the external process: `SELECT * FROM py_cleaner.Execute(SELECT raw_payload FROM #data)`.
   - The engine serializes query rows to JSON lines, feeds them to the script's `stdin`, and reads the script's `stdout` JSON lines as a live SQL data stream.
3. **Multi-Tenant Sandbox Containment**:
   - *Solo/Enterprise Profiles*: Spawns the process locally under the permissions of the host service runner.
   - **SaaS Profile**: Spawns the process strictly within the tenant's container sandbox, leveraging OS limits (cgroups) and network namespaces to prevent remote execution leaks to the SaaS host.

### Reporting — Paginated PDF Export Engine

To support traditional enterprise reporting requirements (similar to SSRS or Crystal Reports), the visualization system needs a layout-aware PDF generation engine.

#### Core Design & Parameters:
1. **Physical Page-Breaking and Layout Rules**:
   - Translate responsive 12-column grid CSS layouts (`STRUCTURE`) into fixed A4/Letter pages on PDF export.
   - Introduce card properties like `PAGE_BREAK = BEFORE | AFTER` to control printable pagination boundaries.
2. **Repeating Table Headers & Footers**:
   - The PDF exporter must automatically repeat `TABLE` headers at the top of every physical page during multiline grid overflow.
   - Support system placeholders in report footers (e.g. `Page X of Y`, runtime timestamp).

### Reporting — Inline Row Detail Subreports

To enable hierarchical and nested data visualization inside tables without forcing users to navigate to separate pages or visuals.

#### Core Design & Parameters:
1. **The `ROW_DETAIL` Mapping Clause**:
   - Expand the `TABLE` mapping syntax to support a collapsible child container:
     ```sql
     CREATE VISUAL CustomerTable AS TABLE (
       SOURCE = #customers,
       MAPPINGS (
         CustomerID, Name, Email,
         ROW_DETAIL (
           TARGET = OrderSubTable,
           KEY = CustomerID
         )
       )
     );
     ```
2. **Interactive Row Expansion**:
   - The Table UI renders a toggle icon (`▸`) at the start of each row. Clicking it expands the row vertically to embed the `TARGET` visual, pre-filtered by the row's `KEY` context.
