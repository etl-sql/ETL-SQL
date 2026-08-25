# ETL-SQL Product Roadmap

This document describes future product outcomes and their sequencing. Detailed architecture belongs
in the linked decisions and architecture documents, executable release work belongs in `TODO.md`,
and shipped outcomes belong in `CHANGELOG.md` and the release notes under `docs/releases/`.

The stable deployment-profile topology is defined in
[`docs/architecture/DeploymentProfiles.md`](docs/architecture/DeploymentProfiles.md). The Enterprise
operating model and trust hierarchy are defined in
[`docs/architecture/roadmaps/Enterprise_Platform_Strategy.md`](docs/architecture/roadmaps/Enterprise_Platform_Strategy.md).

## Roadmap Authoring Contract

Before adding or revising an entry:

1. **Verify current reality.** Search source, tests, `TODO.md`, `CHANGELOG.md`, release notes, and
   authoritative architecture documents. Do not describe an implemented capability as future work.
2. **Separate outcome from mechanism.** State the user or operator outcome first. Treat libraries,
   providers, protocols, performance figures, and implementation shapes as candidates unless an ADR
   has accepted them.
3. **Declare maturity and horizon.** Every entry must use one status and one horizon from the
   vocabularies below.
4. **Keep one coherent initiative per entry.** Split work when parts have different dependencies,
   security boundaries, delivery horizons, or independent user value.
5. **Name boundaries and dependencies.** State what the initiative intentionally excludes and which
   contracts or earlier initiatives it requires.
6. **Deliver vertical slices.** Stages must produce independently testable product capability, not
   merely layers of internal plumbing or an exhaustive all-at-once migration.
7. **Make claims testable.** Exact latency, size, parity, coverage, or visual-count claims require a
   defined fixture and verification method. Otherwise describe them as targets to measure.
8. **Preserve sources of truth.** Detailed design belongs in an ADR or architecture document;
   executable tasks belong in `TODO.md`; shipped outcomes belong in `CHANGELOG.md` and release notes.
9. **Retire completed work.** When the promised outcome ships and is verified, remove the roadmap
   entry or rewrite it to describe only the genuinely remaining increment.
10. **Respect product boundaries.** Roadmap work must preserve script-first authoring, transparent
    transformation, portability, lineage, Zero-Trust execution, deployment profiles, and the
    third-party dependency policy.

### Status vocabulary

- **Exploring** — The problem is plausible, but product or architecture decisions remain open.
- **Accepted** — The outcome and governing architecture are accepted, but work is not scheduled.
- **Planned** — Delivery is selected for an upcoming milestone and decomposed in `TODO.md`.
- **In Progress** — Implementation is active; the entry states only remaining scope and risks.
- **Incremental** — A useful baseline has shipped; the entry describes the next independently
  valuable expansion.

### Horizon vocabulary

- **Foundation** — Required before dependent product work can safely proceed.
- **Next** — A leading candidate for the next planning horizon.
- **Later** — Valuable but not currently a dependency or launch gate.
- **Launch Gate** — Required before a named deployment profile or hosted service can make its
  production claim.

### Required entry shape

Each entry states its status, horizon, authoritative design, problem and intended outcome, why the
horizon is appropriate, boundaries, dependencies, vertical delivery slices, and acceptance evidence.
Keep entries concise and link to detailed designs rather than copying them here.

---

## Foundation and Next Horizon

### Tooling & Authoring — Lossless Visual Report Builder Editing

**Status:** In Progress  
**Horizon:** Foundation  
**Authoritative design:** [`docs/guides/tooling/report-builder.md`](docs/guides/tooling/report-builder.md)

An edit to one presentation statement must not alter data-preparation SQL, unrelated presentation
statements, comments, whitespace, or line endings. This is Gate Zero for expanding the report grammar:
a visual editor that can overwrite script content breaks ETL-SQL's script-first contract.

**Why now:** GoG will add nested presentation syntax. Its authoring and mutation contracts must be
safe before that grammar broadens.

**Boundaries:** This work preserves source text and usable canvas state; it does not redesign the GoG
grammar or silently normalize an author's script.

**Dependencies:** Parser span/trivia support, `DesignerScriptPatcher`, and parity between embedded and
LSP-hosted designer paths.

**Delivery slices:** Route the LSP host through surgical patching; add multi-page and CTE coverage;
exercise repeated CRLF/LF mutations; preserve statement-body trivia; and retain canvas state during
transient syntax errors. GoG syntax work must extend these same mutation primitives.

**Acceptance evidence:** Regression and fuzz tests prove that presentation-only mutations leave all
out-of-scope bytes unchanged and that invalid intermediate edits do not reset the canvas.

### Reporting & Presentation — Native Grammar-of-Graphics Spine

**Status:** Shipped
**Horizon:** Delivered
**Authoritative design:** [`docs/architecture/decisions/GrammarOfGraphicsSpecIR.md`](docs/architecture/decisions/GrammarOfGraphicsSpecIR.md)

ETL-SQL needs its own typed, versioned visual contract so report meaning is not defined by an ECharts
option object. Named `.rptsql` visual types remain the easy path and lower into a semantic `ChartSpec`;
a deterministic `PlotPlan` resolves domains, ordering, ticks, palettes, null handling, and legends
once for every renderer.

**Outcome:** This is the common spine for advanced authoring, native static export,
micro-charts, terminal reporting, renderer independence, and the completed ECharts/ClearScript retirement.

**Boundaries:** Data transformation remains visible in ETL-SQL and `#temp` tables. ETL-SQL learns
from Vega-Lite but does not embed Vega-Lite JSON or accept a second runtime reporting language.
No vendor chart schema or external chart runtime is stored or used by the reporting path.

**Dependencies:** Lossless Report Builder editing and a dependency-light Core contract; renderer and
pixel-emission dependencies remain in the reporting layer.

**Delivered:** Every graphical catalog visual lowers through typed `ChartSpec`/`PlotPlan` semantics or
an approved focused native layout module. Native `CUSTOM` authoring covers layers, scales,
Cartesian/transposed/polar coordinates, conditions, inheritance, datum/value bindings, explicit
stack/offset/interval placement, deterministic jitter/nudge, continuous color ranges, wrapped facets,
fixed aspect, and `TICK`. Browser and static output use native SVG; terminal and accessible fallbacks
consume the same resolved contract. The capability matrix proves that no graphical visual requires an
external chart runtime.

**Acceptance evidence:** Cross-backend golden and semantic conformance tests, a capability matrix,
typed-value and null tests, and measured bundle, cold-start, export-time, and output-size baselines.

**Closure evidence:** [`reporting-phase13-closure.md`](docs/benchmarks/reporting-phase13-closure.md)
indexes version compatibility, cross-backend conformance, visual goldens, accessibility, bundle,
cold-start, memory, export-time, output-size, parser/sample, and capability/source parity gates. The
production composite sample and Vega-Lite/ggplot2 conversion guides keep transformations visible in
source-controlled SQL.

### Reporting & Presentation — Native Card and Table Micro-Charts

**Status:** Shipped
**Horizon:** Current
**Authoritative design:** [`docs/architecture/decisions/MicroChartsAndHtmlEmbedding.md`](docs/architecture/decisions/MicroChartsAndHtmlEmbedding.md), subject to the GoG boundary above

Cards and tables should support native sparklines and progress indicators that remain meaningful in
browser, export, email, and terminal surfaces. Their small geometry surface makes them an early proving
ground for the native SVG compiler.

**Why now:** They deliver visible user value while exercising typed data, scale resolution, static SVG,
and semantic fallback without waiting for the complete visual catalog.

**Boundaries:** This entry excludes arbitrary HTML/CSS/SVG templates and recursive template macros.
The semantic spec remains dependency-light; rendering and SkiaSharp code do not move into Core.

**Dependencies:** The GoG vertical slice and the existing `CARD`/`TABLE` contracts.

**Delivery slices:** Add one card sparkline and one table-cell progress/sparkline path, then export and
terminal fallbacks, followed by additional native presets justified by repeated use.

**Current delivery:** `CARD` source-bound sparklines plus `TABLE` wide sparklines and progress
indicators lower through typed GoG contracts into deterministic `PlotPlan` geometry. Browser, native
PDF, Markdown/email-image, terminal, screen-reader, and plain-text surfaces consume the resulting
native SVG or semantic fallback without server-side V8. Representative geometry/export goldens,
per-row payload budgets, render-cost gates, and JSON-columnar/Arrow crossover evidence are checked in.

**Acceptance evidence:** Geometry goldens, browser/export snapshots, accessible text fallbacks, and
measured payload/render costs across representative small and tabular datasets.

### Reporting & Presentation — Semantic Terminal Chart Compilation

**Status:** Shipped
**Horizon:** Current
**Authoritative design:** [`docs/architecture/decisions/GrammarOfGraphicsSpecIR.md`](docs/architecture/decisions/GrammarOfGraphicsSpecIR.md)

Reports should remain useful over SSH, in air-gapped environments, and in terminal workflows by
lowering chart semantics to blocks, Braille cells, text, tables, and proportional components.

**Why now:** An early terminal backend proves that `ChartSpec` and `PlotPlan` are renderer-neutral
rather than merely a new browser chart schema.

**Boundaries:** The target is semantic parity, not pixel parity. Rich form controls, full keyboard
navigation, and elaborate TUI interaction are separate later UI work and do not block this compiler.

**Dependencies:** The representative GoG vertical slice and a shared semantic fallback contract.

**Delivered:** The first GoG visual set compiles to fractional blocks, Braille, point glyphs, labeled
rules, and proportional components, with coordinated width-aware facets. Maps, flows, hierarchies,
and networks expose useful ordered fallbacks through the same serialized contract used by terminal,
screen-reader, and Markdown/plain-text delivery surfaces.

**Acceptance evidence:** Terminal snapshots at supported widths, semantic assertions for values and
ordering, fallback tests, and screen-reader/plain-text review fixtures.

### Reporting & Interaction — Cascading Slicers

**Status:** Delivered
**Horizon:** Delivered
**Authoritative design:** [Cascading Slicers and Atomic Parameter State](docs/architecture/decisions/CascadingSlicersAndAtomicParameters.md)

Parent filter selections should constrain descendant option sets and update invalid selections
atomically across offline snapshots, local vectors, and live queries. Filter bindings should declare
the relationship directly so the compiler can infer a multi-parent dependency graph and detect cycles.

**Why now:** Cascading slicers are a high-value report interaction and establish the parameter-state
semantics required by bookmarks.

**Boundaries:** The design must define null, All, multi-select, invalid-selection, and offline/live
behavior before syntax is accepted. It must not hide data transformations or introduce an independent
control-only query language.

**Dependencies:** Stable typed report data, atomic parameter updates, dependency-graph compilation,
and Report Builder round-trip support for the accepted syntax.

**Delivery slices:** One parent/child local-vector flow; multiple parents and cycle diagnostics;
equivalent live-query ordering; offline snapshot conformance.

**Acceptance evidence:** State-transition tests cover invalid descendants, null/All semantics,
multi-select values, cycles, concurrent changes, and equivalent offline/live results.

**Delivered:** Report-SQL now has explicit LOCAL/LIVE `CASCADE` policies, retained offline option
vectors, stable multi-parent graph compilation, author-time cycle diagnostics, topologically ordered
LIVE queries, and atomic manifest publication with CLEAR/FIRST/ERROR rollback semantics. Parser,
formatter, Analysis, LSP help/snippets, Report Builder round trips, browser JSON multi-select state,
documentation, Gemini baselines, and production conformance tests use the same contract.

---

## Later Reporting and Presentation Work

### Reporting & Interaction — Author Bookmarks

**Status:** Complete  
**Horizon:** Delivered  
**Authoritative design:** [`docs/architecture/decisions/AuthorBookmarks.md`](docs/architecture/decisions/AuthorBookmarks.md)

Authors need source-controlled report states that atomically apply parameters, active page, and
supported presentation state. A bookmark is distinct from a user-created Portal saved view and from
transient URL state.

**Why now:** The feature becomes coherent after cascading parameters establish reliable atomic state.

**Boundaries:** Initial URLs carry only a bookmark identifier; arbitrary parameter values are not
placed in the hash. Offline snapshots receive a serializable resolved state. References to report
objects are statically linted and rename-aware.

**Dependencies:** Atomic cascading parameter behavior, manifest serialization, and authoring support.

**Delivery slices:** Parameter/page bookmarks; supported presentation state; Portal shell and button
invocation; offline serialization and identifier-only deep links.

**Acceptance evidence:** Atomic-application tests, stale-reference diagnostics, rename tests,
offline replay, and disclosure tests for generated URLs.

**Delivered:** One versioned `ResolvedReportState` envelope shared by author bookmarks and Portal
saved views; server-side atomic application through the cascading-parameter engine that publishes one
manifest and rolls back entirely on failure; strict parser/formatter/lint plus `DROP BOOKMARK`;
LSP completion, hover, and rename across every bookmark reference; Report Builder parse/edit/patch
round-trip with a bookmarks panel; a Portal Views menu covering author bookmarks and per-user saved
views with the full WAI-ARIA menu-button keyboard model; per-user ownership re-checks, revision-drift
warnings, and identifier-only URLs. One boundary is deliberate and recorded in `TODO.md`: offline
bookmark replay is implemented and tested against the runtime's `__ETLSNAP__` contract, but no
self-contained offline HTML snapshot *host* exists yet — that is a separate feature.

### Reporting & Presentation — Constrained HTML Visuals

**Status:** Accepted direction — implementation not started

**Horizon:** Unscheduled

**Authoritative design:** The product direction below is accepted. A dedicated threat model and ADR
must define the exact grammar, sanitizer, template evaluator, interaction projection, and portable
fallback contract before parser implementation. The earlier HTML examples in
`MicroChartsAndHtmlEmbedding.md` are design input, not parser contracts.

`CREATE VISUAL ... AS HTML` is the presentation counterpart to `CREATE VISUAL ... AS CUSTOM`.
`CUSTOM` gives script authors a renderer-neutral grammar for composing charts. `HTML` gives them a
constrained, renderer-owned way to compose bespoke non-chart presentation components such as
operational tiles, narrative metric panels, status cards, and row repeaters. Built-in `CARD`, `TABLE`,
`TEXT`, `IMAGE`, slicers, and other named visuals remain the concise path for common cases.

The visual keeps the normal Report-SQL statement shape for consistency:

```sql
CREATE VISUAL NodeClusterStatus AS HTML (
  SOURCE = #cluster_nodes,
  MODE = REPEATER,
  TEMPLATE = '<article class="node-card">{{HostName}}</article>',
  STYLE (
    CSS = '.node-card { padding: 1rem; }'
  ),
  ACTIONS (...)
);
```

`SOURCE` is optional so the same visual can render static or parameter-driven presentation content.
The initial template model supports escaped field and parameter substitution, a small typed
conditional form, and `SINGLE` or `REPEATER` rendering. All calculation, aggregation, lookup,
filtering, and other data transformation remains visible SQL; the template language does not become
a second expression or execution engine.

HTML visuals may embed declared report visuals through a bounded, statically resolved visual helper.
Embedded references must participate in missing-target, cycle, nesting, query-work, node, byte, and
aggregate-report budgets. They reuse existing visual manifests and actions; they do not load an
alternate chart runtime. Author interactions reuse declarative Report-SQL actions such as parameter
updates, navigation, bookmarks, drill, and cross-filtering instead of DOM scripting.

The security boundary permits sanitized semantic HTML and visual-scoped CSS. It rejects JavaScript,
`<script>`, inline event handlers, unsafe URL schemes, iframes, objects, embeds, global document
mutation, external form submission, and arbitrary raw-HTML substitution. Field and parameter values
are escaped by default with no initial raw-output escape hatch. Allowed links, images, buttons,
standard controls, and inline SVG must pass an explicit element/attribute/URL policy; generated
micro-chart SVG remains server-owned. CSS is isolated to the visual while retaining approved report
theme variables, so a component cannot restyle the report shell or neighboring visuals.

Browser, print, and PDF output render the sanitized component. Every HTML visual must also declare or
resolve a concise semantic summary for email, Markdown, terminal, plain text, screen readers, and any
surface that cannot preserve the component faithfully. Static output must never silently drop data or
imply unavailable interaction.

The first delivery is script-first with preview, formatter, lint, LSP, documentation, and lossless
Report Builder preservation. A dedicated WYSIWYG HTML editor is deferred, matching the accepted
`CUSTOM` authoring boundary.

**Delivery slices:** Threat model and ADR; parser/immutable AST/formatter; typed template and
sanitization contracts; optional-source `SINGLE` and `REPEATER` rendering; visual embedding and
declarative actions; scoped styling and theme tokens; browser/print/PDF plus semantic fallbacks; LSP,
preview, documentation, samples, capability inventory, and lossless designer round trips.

**Acceptance evidence:** Hostile markup, URL, CSS, disclosure, and nesting tests; deterministic
escaping and typed-binding tests; source-free, single-row, and repeated-row fixtures; embedded-visual
cycle and budget tests; action and parameter-refresh parity; accessibility checks; browser, print,
PDF, email, Markdown, terminal, and snapshot conformance; payload/render budgets; and proof that no
script-authored JavaScript executes on any host.

### Reporting & Interaction — Visual Detail Popovers

**Status:** Shipped in v0.19.0

A `TOOLTIP` clause attaches one of two detail surfaces. A transient tooltip carries text, is never
focusable, and holds nothing interactive. A detail popover carries formatted content and whole
visuals — including a chart driven by the row the reader activated — and is opened by hover preview,
click, tap, or keyboard, then pinned until dismissed.

**What shipped:** One shared browser controller across the supported chart adapters, with anchor-based
flip/shift placement, `Escape`/outside-click/toggle dismissal with focus return, generation-fenced
refreshes, and correct `role="tooltip"` versus labelled-dialog semantics. Static resolution of every
tooltip target with `RPT2101`–`RPT2114` diagnostics covering missing references, cycles, nesting,
depth, node, refresh-work, payload, and per-report budgets. A row-context contract that admits only
an explicitly mapped, non-secret column into `@hover_value`. One semantic fallback wording shared by
PDF, print, Markdown, email, terminal, plain text, and snapshots, which never implies hover is
available.

**Boundaries held:** A detail surface cannot open another detail surface or recursively embed a
dashboard, and every limit fails the build rather than rendering a partial surface.

**Evidence:** 36 browser tests (behaviour, geometry, performance) driving the canonical runtime,
plus parser/formatter round trips, static-safety and budget boundaries at limit-1/limit/limit+1,
manifest version compatibility, rename, and designer round trips. Measured on the kitchen-sink
fixture: transient open 50.9 ms, refresh completion 65.3 ms, reposition 26.8 ms, dismissal 3.5 ms.

**Reference:** [`TOOLTIP`](docs/reference/visuals-reporting/report/tooltip.md).

---

## Platform, SaaS, and Governance

### Platform — Native Object Storage for Shared Artifacts

**Status:** Accepted  
**Horizon:** Launch Gate — Shared SaaS  
**Authoritative design:** Provider contract not yet decided; build on the existing artifact-storage abstraction

Shared SaaS needs horizontally scalable artifact storage without assuming SMB or POSIX atomic rename.

**Why now:** Object-native storage must precede broad Shared SaaS scale and large-content portability.

**Boundaries:** Do not emulate atomic rename with an unreliable copy/delete sequence. Shared mutation
continues to use database-backed leases and fencing.

**Dependencies:** Immutable or content-addressed object naming, conditional-write/version semantics,
an authoritative commit record, and garbage collection for abandoned staging objects.

**Delivery slices:** Provider-neutral contract and fault model; one object-store provider; second-provider
conformance; integration with large-content portability and Shared-source isolation.

**Acceptance evidence:** Provider contract certification covers concurrent writers, stale fences,
partial writes, lost responses, retries, storage outages, reconciliation, and garbage collection.

### Identity — Workload Identity and M2M Hardening

**Status:** Incremental  
**Horizon:** Later  
**Authoritative design:** Not yet decided

Service-account administration, client credentials, scopes, ownership, rotation, revocation, audit,
and Portal/Orchestrator authorization have shipped. The next increment reduces dependence on long-lived
shared secrets and hardens automated publication and execution.

**Why now:** CI/CD and scheduled workloads should converge on short-lived, audience-bound,
secretless credentials as hosted and enterprise automation expands.

**Boundaries:** Long-lived API credentials remain a compatibility baseline, not the desired final
architecture. Workload identity cannot exceed the owning principal, tenant, resource, or approved
operation.

**Dependencies:** A threat model and policy for federation, token exchange, audiences, credential
binding, sensitive-operation approvals, and anomaly signals.

**Delivery slices:** One CI OIDC exchange; additional GitHub/GitLab/Azure DevOps federation;
certificate or `private_key_jwt` authentication; secretless scheduled workloads; approval and
credential-use anomaly policies.

**Acceptance evidence:** Cross-tenant/audience replay failures, short-lived credential tests,
revocation and rotation journeys, approval bypass tests, and attributed audit evidence.

### Execution — Measured Lean Worker Profile

**Status:** Exploring  
**Horizon:** Later  
**Authoritative design:** Not yet decided

Ephemeral execution workers may benefit from a smaller dependency boundary, faster cold starts, and
lower working set than the unified executable.

**Why now:** This is an optimization track to pursue only after measurements identify meaningful cost
or isolation gains.

**Boundaries:** Feature flags alone do not remove assemblies. Trimming must not break reflection, DI,
dynamic connector discovery, or deployment-profile behavior.

**Dependencies:** Baselines for startup working set, cold-start latency, artifact size, loaded
assemblies, connector closure, sandbox lifetime, and cost sensitivity.

**Delivery slices:** Measurement report; explicit engine-only entry project and connector/profile
manifest; trimming experiment; opt-in certified worker artifact if justified.

**Acceptance evidence:** Reproducible before/after measurements and connector, governance,
cancellation, sandbox, and deployment-profile certification.

### SaaS Reliability — Provider-Neutral Fault Certification

**Status:** Accepted  
**Horizon:** Launch Gate — Hosted production  
**Authoritative design:** Not yet decided

Hosted operation must prove safe behavior through process loss, lease races, database and storage
outages, network partitions, duplicate delivery, clock skew, and disk exhaustion.

**Why now:** Failure certification is required before production claims depend on distributed leases,
shared storage, and remote workers.

**Boundaries:** Define reusable scenarios before selecting Chaos Mesh or another hosting-specific
tool. Only workloads with explicit checkpoint semantics may claim checkpoint resume; other work fails
safely and remains eligible for deliberate retry.

**Dependencies:** Lease fencing, provider fault hooks, durable audit/evidence capture, and documented
retry/checkpoint contracts.

**Delivery slices:** Local deterministic scenarios; Docker/cloud adapters; lease/storage/database
fault suites; deployment-profile certification integration.

**Acceptance evidence:** Repeatable fault matrices prove no split-brain mutation, authority reuse,
silent loss, or false checkpoint-resume claim.

### SaaS Reliability — Production Canaries

**Status:** Accepted  
**Horizon:** Launch Gate — Hosted production  
**Authoritative design:** Not yet decided

A hosted fleet needs isolated synthetic journeys that detect correctness and latency regressions in
reports, jobs, Gateway, export, and notification paths before customers do.

**Why now:** Canaries become operationally necessary when a production SaaS fleet exists; they are not
an immediate product sprint before that environment exists.

**Boundaries:** Synthetic resources cannot access customer systems. Cost, quota, identity, and failure
domains remain isolated, and alerts distinguish ETL-SQL failures from synthetic dependency failures.

**Dependencies:** Named SLOs, fleet regions/failure domains, external probe capability, isolated
synthetic tenants/resources, and credential rotation.

**Delivery slices:** One external health journey; report and job journeys; Gateway/export/notification
coverage; regional and failure-domain rollout.

**Acceptance evidence:** Fault-injection drills trigger the expected SLO alerts without customer data
access, cross-tenant effects, or ambiguous dependency attribution.

### Governance & Gateway — Verified Viewer Context Propagation

**Status:** Exploring  
**Horizon:** Later  
**Authoritative design:** Threat-model ADR required before connector work

Interactive Gateway queries may need verified viewer attributes for downstream row filtering while
the database connection continues to use a service credential. This asserted application context is
not equivalent to OAuth on-behalf-of delegation or Kerberos constrained delegation.

**Why now:** The business requirement is important, but the assurance model must be separated before
connector-specific behavior is promised.

**Boundaries:** The first stage cannot claim that the database authenticated the viewer. PostgreSQL
role changes are not derived directly from OIDC roles/groups. Context is parameterized,
transaction-scoped, cleared before pool reuse, resource-allowlisted, tenant-bound, and fail-closed.

**Dependencies:** Cryptographic Portal-to-Gateway identity, an ADR separating asserted context,
OAuth delegation, and Kerberos delegation, plus connector session-cleanup contracts.

**Delivery slices:** Threat model and neutral envelope; one asserted-context connector; pool-cleanup
certification; additional connectors; separately designed delegated-authentication tracks if justified.

**Acceptance evidence:** Forgery, replay, cross-tenant/resource, reserved-key, injection, transaction
lifetime, and pool-reuse tests; audit records both viewer and executing credential.
