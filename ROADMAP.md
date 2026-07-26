# ETL-SQL Product Roadmap

This document tracks future product tracks and candidate phases. When development begins, the next
actionable phase is moved to `TODO.md`. Shipped work belongs in `CHANGELOG.md` and the release notes
under `docs/releases/`.

The enterprise operating model, authority hierarchy, trust boundaries, and progressive deployment
promise are defined in
[`docs/architecture/roadmaps/Enterprise_Platform_Strategy.md`](docs/architecture/roadmaps/Enterprise_Platform_Strategy.md).

---

## Future Candidate Phases

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

#### P0 — Restore trust in the critical journeys

1. **Make browser/API contracts explicit.** The Admin Users screen currently fails after a
   successful API response because `UserDto.Username` serializes as `username` while
   `admin.html` reads `userName`. The same class of drift should not remain a runtime discovery:
   publish an OpenAPI or generated TypeScript contract, validate critical responses at the client
   boundary, and cover login → users → folders → report publish/run with a real browser test.
2. **Fix identity presentation everywhere.** The shared header renders JWT `sub` before
   `unique_name`, so the signed-in user appears as an internal numeric ID (for example, `1`) on
   Reports, Admin, Docs, and Orchestrator. Use one session identity model and shared shell component;
   audit rows should display the same recognizable identity.
3. **Never present demo governance records as evidence.** A fresh installation currently reports
   a governance score, active bypasses, named glossary stewards, badges, and settings sourced from
   the prototype's demo/browser state. Ship real authorized APIs and explicit unavailable/empty
   states, or hide unfinished routes. The detailed durability work remains in
   [Portal — Governance Dashboard](#portal--governance-dashboard).
4. **Make parameterized report execution one understandable flow.** Before a snapshot exists, keep
   the report name as the page heading, collect required parameters before submitting work, use one
   unambiguous Run action, and show the resulting job through a terminal state. The current flow
   first runs a preparation step, labels the embedded parameter form `Ready`, then asks the user to
   run again. Disable export/subscription actions until their prerequisites exist and give every
   embedded input an accessible name.
5. **Make the primary shell responsive.** At 390px the global navigation and Admin workspace clip
   beyond the viewport; the Reports hamburger only controls the folder sidebar and leaves the page
   underneath interactive. Collapse the global nav, use a modal drawer with overlay/focus
   containment, and provide responsive table, form, tab, and action patterns for Reports, Admin,
   Governance, Docs, and Orchestrator.

#### P1 — Connect the product into coherent workspaces

1. **Consumer home and global discovery.** Surface the existing consumer-home and fuzzy catalog
   APIs as a useful landing page: favorites, recent, featured, popular, and one global report
   search. Report cards should use intentional thumbnails/icons and one concise last-run/last-viewed
   status instead of repeating `Not run`, `Never run`, and `Awaiting first run`.
2. **Promote the script editor to a first-class Studio.** Add a top-level **Studio** destination for
   authorized Admins and Publishers when the Designer module is enabled. It should open a
   catalog-scoped authoring home for creating or editing `.rptsql` reports, with the existing script
   editor and visual designer as equal Code and Design modes. This is especially important for a
   closed SaaS deployment where Portal authoring is the approved path and outside files, raw upload,
   or source-control write-back are disabled. Keep the existing interactive trust boundary:
   ACL-filtered `SHARED:` connections, read-only queries plus `#temp` staging, server-enforced
   limits, and no script-supplied credentials or arbitrary connection creation. Make the navigation
   and every authoring API disappear when authoring is disabled; do not rely on hiding the menu.
   Define an explicit deployment policy such as `Disabled`, `CatalogOnly`, or `SourceControlled`
   rather than treating the current Designer and upload/source-control switches as unrelated
   settings.
3. **Administration and operations hub.** Add visible, role-gated workflows for the backend
   capabilities that currently have no coherent browser home: service accounts and secret rotation,
   pending access approvals, anonymous share/embed inventory, fleet/node status, operational
   metrics, and administrative service runs. Join these with health, audit, outbox, and Orchestrator
   context so an operator can move from a symptom to the responsible job or node.
4. **Surface departmental environments without weakening their isolation.** The shipped
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
5. **Finish the data-steward journey.** Keep the real lineage and quarantine views, make
   Stewardship and Audit genuine routes, and connect disposition/replay submissions to job status.
   Add rule visibility and structured failure trends. Governed quarantine row access is specified
   separately in [Portal — Quarantine Row Access](#portal--quarantine-row-access).
6. **Use one documentation renderer.** Docs and connector Help currently expose raw Markdown table
   pipes, admonition markers, and code fences. Use a shared, sanitized renderer with consistent
   headings, tables, admonitions, code blocks, links, topic search, and copy actions.
7. **Use one feedback and dialog system.** Replace native `alert`, `prompt`, and `confirm` calls
   across Reports, Admin, Governance, Designer, Orchestrator, and report runtime with accessible
   toasts and purpose-built dialogs. Password reset, destructive changes, policy rollout, and
   source-control commits need structured validation, clear impact text, and auditable outcomes.
8. **Polish the visual designer without reducing its power.** Group or search the long visual
   palette, replace the rainbow of equally weighted buttons with clearer hierarchy, label the
   icon-only toolbar, improve dataset/on-page empty states, and make the canvas/inspector useful at
   laptop and tablet widths.

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

1. Add an automated browser lane; the current testing guide explicitly records that Portal and
   report-runtime JavaScript have none. Cover Chromium desktop and a narrow viewport, at minimum,
   with seeded Viewer, Publisher, Steward, Operator, and Admin journeys.
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
2. **Report consumer flow:** consumer home/search, report-card cleanup, parameter preflight,
   execution status, prerequisites, and accessible report runtime controls.
3. **Studio authoring:** top-level role/module-gated Studio, catalog-only SaaS policy, Code/Design
   modes, authoring home, and end-to-end create/edit/validate/run/publish coverage.
4. **Responsive and accessible foundations:** mobile shell, responsive Admin patterns, semantic
   dialogs/drawers, keyboard/focus work, and shared feedback components.
5. **Governance, operations, and environments:** remove demo evidence, finish steward/audit routes,
   connect job status, expose the missing role-gated operations surfaces, and add the isolation-safe
   departmental environment workflow.
6. **Docs and designer polish:** shared Markdown renderer, designer hierarchy/discoverability, and
   final visual consistency pass.
7. **Architecture and administration documentation:** after the implementation and contracts have
   stabilized, reconcile `Docs/Architecture/Portal.md`,
   `docs/architecture/decisions/Departmental_Isolation.md`, the Portal administration guides, API
   inventory, module/authoring policy matrix, HA diagrams, isolation threat model, and deployment
   verification runbook with the shipped behavior. Architecture diagrams and interface contracts
   must be checked against the final C# source rather than copied from this roadmap.
8. **Release gate:** browser, accessibility, responsive, local/Docker parity, departmental
   isolation, and role/module/authoring-policy acceptance runs.

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

1. **Submitted jobs disappear.** Replay and disposition actions report
   `Disposition job {id} submitted` and stop there — no status, no link into job history. A steward
   cannot tell whether the release they just made actually applied. The queue should follow the job
   to a terminal state, or at minimum link to it.
2. **The trend panel re-parses a display string.** `ParseRuleFailures` reconstructs per-rule
   failures by splitting the `DataQualityFailures` history payload on `;`, `:`, and `=`. That format
   exists for humans reading run history; it already needed careful handling because rule text
   contains both `:` and `=` (a `MATCHES` regex). v2 records per-column run metrics — the trend
   should read those instead of parsing prose.
3. **No rule visibility in the Portal.** `SHOW DATA QUALITY RULES` is an engine statement only, so a
   steward who lives in the Portal cannot see which rules protect which columns — the thing they
   most need when a quarantine rate jumps. Wants a read-only endpoint plus a panel beside the trend.
4. **Every preview spins a full engine.** Each request lexes, parses, lints, and evaluates through a
   new `ExecutionSession`. Acceptable at current volume; worth revisiting before any endpoint like
   this becomes a polled or dashboard-refreshed surface.
