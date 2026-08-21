# ETL-SQL Development TODO List

Use this list as the execution ledger for open active-release and roadmap work. Once work is
verified, record its notable outcome in `CHANGELOG.md` and remove it from this file and, when
applicable, `ROADMAP.md`. Git and the changelog retain completion history. If evidence invalidates a
completion claim, add a new open entry with a concrete correction path.

---

## v0.19.0 Release Execution Ledger

Target Release: **v0.19.0**
Authoritative Policy: [`docs/releases/release-checklist.md`](docs/releases/release-checklist.md) & [`docs/architecture/decisions/Enterprise_Release_Evidence_Checklist.md`](docs/architecture/decisions/Enterprise_Release_Evidence_Checklist.md)

### Release Evidence Gates
- [ ] Enterprise Release Evidence Checklist: Verify all gates from `Enterprise_Release_Evidence_Checklist.md`, `test-lane.ps1`, `Test-PreRelease.ps1`, `Test-EnterpriseHardeningCertification.ps1`, `admin restore --validate`, `ha-soak validate`, and `SecurityBoundaryDocTests`.

## Release Gate Follow-ups

These failures were observed during the post-Phase 2 full-solution test run and are unrelated to the
GoG contract changes.

- [x] Restore or generate `src/etl-sql-vscode/ui/dist/index.html` before browser-story tests, or make
  the VS Code webview stories self-contained, so the results, preview, preview-sink, designer, and
  visual-flow fixtures do not fail with HTTP 404 responses.
- [x] Document the `/api/gateway/enrollment` area exposed by `GatewayBootstrapController.cs` in
  `docs/architecture/Portal.md` so the API architecture reconciliation test passes.
- [x] Add `GatewayEnrollmentEntity` to the persisted-entity summary in
  `docs/architecture/Portal.md` so the data-model reconciliation test passes.
- [x] Give the `gw-enrollModal` overlay in `gateways-admin.js` a semantic, accessible dialog role and
  name so `PortalDialogAccessibilityTests` passes.

## Reporting and Authoring Critical Path

### Phase 1 — Lossless Report Builder Editing (Gate Zero)

Authoritative guide: [`docs/guides/tooling/report-builder.md`](docs/guides/tooling/report-builder.md)

- [x] Route every LSP-hosted designer mutation through `DesignerScriptPatcher`; remove the legacy
  full-script regeneration path from `DesignerLspHandler.cs`.
- [x] Add multi-page mutation coverage proving an edit on a later page cannot shift or rewrite an
  earlier page.
- [x] Add CTE-chain coverage proving presentation edits preserve all data-preparation statements.
- [x] Add repeated-mutation fuzz coverage for at least 50 moves/property changes under CRLF and LF.
- [x] Preserve comments and trivia inside visual bodies, including `MAPPINGS`, `OPTIONS`, actions,
  styles, and other nested clauses.
- [x] Keep the WYSIWYG canvas usable during transient syntax errors and surface localized diagnostics
  without resetting the last valid state.
- [x] Define reusable round-trip fixtures for future nested GoG syntax so new grammar uses surgical
  patching from its first implementation.

**Exit gate:** Embedded and LSP designer paths pass byte-preservation tests showing that a
presentation-only edit changes only the intended statement span; multi-page, CTE, trivia, line-ending,
sequential-mutation, and transient-error suites are green.

**Completed:** Shared Portal/LSP surgical patching, reusable nested-clause fixtures, 50-cycle LF/CRLF
coverage, transient-error canvas retention, and host-parity regression suites are green.

### Phase 2 — GoG Semantic Contracts and Baselines

Authoritative design:
[`docs/architecture/decisions/GrammarOfGraphicsSpecIR.md`](docs/architecture/decisions/GrammarOfGraphicsSpecIR.md)

- [x] Define a versioned, immutable, dependency-light `ChartSpec` contract for typed bindings, mark
  layers, coordinate and scale intent, formatting, interactions, themes, and accessibility metadata.
- [x] Define typed columnar chart data preserving decimals, temporal values and offsets, booleans,
  nominal/ordinal intent, nulls, and raw values separately from formatted display values.
- [x] Define the deterministic `PlotPlan` contract for domains, category order, ticks, palettes,
  series, legends, null policy, resolved layers, and semantic summaries.
- [x] Keep renderer and pixel-emission dependencies out of Core; document the Core/reporting project
  boundary and enforce it with source-boundary tests.
- [x] Add stable serialization/version tests for `ChartSpec`, typed chart data, and `PlotPlan`.
- [x] Build the cross-backend conformance harness and initial capability matrix before migrating a
  visual type.
- [x] Capture reproducible pre-migration baselines for browser bundle size, fixture build/export time,
  output size, and memory using named fixtures.

**Exit gate:** Contracts serialize deterministically, typed-value tests pass, dependency boundaries are
enforced, the conformance harness can compare renderer results, and baseline evidence is checked in.

**Completed:** Versioned BCL-only semantic contracts, typed columnar values, deterministic golden
serialization, source-boundary enforcement, cross-backend semantic projection comparisons, a
source-backed 36-visual capability matrix, and reproducible named-fixture baseline evidence are green.

### Phase 3 — Representative GoG Vertical Slice

- [x] Lower existing named syntax directly to `ChartSpec` for `BAR`, `LINE`, `SCATTER`, `PIE` or
  `DONUT`, and `COMBO`, plus `RULE` annotations.
- [x] Resolve the representative set through one `PlotPlan` path with stable domains, ordering,
  palettes, legends, ticks, dual axes, gaps, and null behavior.
- [x] Generate ECharts options transiently from `PlotPlan`; remove ECharts-shaped state from the
  semantic contract for migrated visuals.
- [x] Implement native SVG output for the representative Cartesian, polar, layered, and annotation
  cases without adding rendering dependencies to Core.
- [x] Implement terminal output for the same representative plans to prove the contract is not a
  renamed browser chart schema.
- [x] Route at least one PDF or email export path for the representative set without server-side V8.
- [x] Preserve current named visual syntax and add parser, LSP, Report Builder, runtime, and export
  regression coverage for every migrated type.

**Exit gate:** The representative set passes shared semantic assertions and backend-specific goldens
across ECharts, native SVG, terminal, and at least one V8-free export path; the capability matrix
accurately records remaining ECharts-only behavior.

**Completed:** Named `BAR`, `LINE`, `SCATTER`, `PIE`, `DONUT`, and `COMBO` syntax now lowers to
typed data and one deterministic `PlotPlan`, including `RULE` overlays. ECharts is a transient browser
adapter for these visuals; native SVG, semantic terminal output, and static PDF use the same plan,
with representative parser, LSP, Report Builder, runtime, export, and cross-backend conformance
coverage checked in.

### Phase 4 — Native Static Export and Micro-Charts

Authoritative design:
[`docs/architecture/decisions/MicroChartsAndHtmlEmbedding.md`](docs/architecture/decisions/MicroChartsAndHtmlEmbedding.md),
constrained by the GoG ADR.

- [x] Harden native SVG scale, tick, path, label, theme, and accessibility behavior for the
  representative chart set.
- [x] Add one native `CARD` sparkline backed by the GoG contracts.
- [x] Add one native `TABLE` cell sparkline and one progress indicator backed by the same contracts.
- [x] Add browser, PDF, email image, terminal, screen-reader, and plain-text fallbacks for each
  micro-chart.
- [x] Measure the JSON-columnar/Arrow crossover using representative payload, parse, memory, and
  interaction fixtures; do not encode an arbitrary permanent row threshold.
- [x] Add geometry goldens, export snapshots, typed-data tests, payload budgets, and render-cost
  measurements.

**Exit gate:** Card/table micro-charts render from the same semantic plan on every supported surface,
export without V8 for supported cases, have accessible fallbacks, and meet measured payload/render
budgets.

### Phase 5 — Semantic Terminal and Accessibility Fallbacks

- [x] Lower rectangles/bars to fractional blocks, lines/areas to Braille or block canvases, points to
  distinguishable glyphs, rules to labeled references, and arcs to proportional terminal components.
- [x] Render coordinated facets at supported terminal widths without changing shared data ordering or
  scale meaning.
- [x] Add semantic fallbacks: maps to ranked regional breakdowns, Sankey to transition/drop-off
  tables, treemap/sunburst to proportional hierarchies, and networks to degree/connection summaries.
- [x] Reuse one summary/fallback contract for terminal, screen-reader output, and plain-text email.
- [x] Add terminal snapshots at supported widths plus semantic assertions for values, series order,
  palette identity where color exists, null handling, and truncation.
- [x] Keep rich TUI form controls and full keyboard-navigation redesign out of this phase unless they
  are separately approved; they must not delay semantic chart portability.

**Exit gate:** The capability matrix identifies a useful native or semantic fallback for every current
graphical type, and terminal/accessibility fixtures preserve the analytical meaning of each case.

**Completed:** `PlotPlan` marks now lower to fractional blocks, Braille canvases, distinct point
glyphs, labeled rules, and proportional components. Facets retain resolved row/category ordering at
40-, 80-, and 120-column targets. One serialized `SemanticFallback` contract supplies terminal,
screen-reader/manifest, and Markdown/plain-text delivery for native charts plus ranked maps,
transition/drop-off flows, proportional hierarchies, and network degree/connection summaries.

### Phase 6 — Cascading Slicers and Atomic Parameter State

- [x] Write and accept the interaction design for filter bindings, inferred dependencies, multiple
  parents, null/All behavior, multi-select values, invalid descendant selections, and atomic updates.
- [x] Add parser and AST support only after the syntax has minimal parser-accepted examples and
  Report Builder round-trip fixtures.
- [x] Implement one parent/child local-vector flow for offline and snapshot use.
- [x] Implement multiple-parent dependency graph compilation and author-time cycle diagnostics.
- [x] Implement equivalent topologically ordered live-query refresh behavior.
- [x] Define and implement invalid-selection policies without observable intermediate parameter states.
- [x] Add conformance tests proving equivalent local-vector, offline snapshot, and live-query results.

**Exit gate:** State-transition tests cover null/All, multi-select, invalid descendants, cycles,
concurrent changes, and atomic offline/live behavior; accepted syntax is supported by parser, LSP,
documentation, and lossless designer editing.

**Completed:** Accepted `CASCADE` syntax now compiles LOCAL and LIVE filter DAGs, retains snapshot
option vectors, applies stable topological refreshes, and commits parameter/visual state through one
manifest reference swap. CLEAR/FIRST/ERROR, null/All, JSON and legacy multi-select values, multiple
parents, cycles, rollback, and concurrent requests have production state-transition coverage. Parser,
formatter, Analysis lint, LSP snippet/hover, Report Builder round trips, runtime assets, and reference
documentation share the same contract. Gemini's Phase 6 fixture/baseline suite remains as independent
regression evidence alongside production conformance tests.

### Phase 7 — Native Advanced Chart Authoring

- [x] Write the language proposal for native mark layers, scales, coordinates, conditions, and facets;
  do not add embedded Vega-Lite runtime syntax.
- [x] Review the proposal against existing parser behavior, language standards, transformation
  transparency, lineage, actions/interactions, themes, and accessibility.
- [x] Implement parser, immutable AST, formatter, Analysis-tier lint, LSP completion/hover, and rename
  support together for each accepted grammar slice.
- [x] Extend Report Builder parsing and surgical mutation support with trivia-preserving fixtures for
  every accepted nested form.
- [x] Add layering and dual-axis support, then conditions and one-dimensional faceting, then
  two-dimensional composition and shared/independent scale policies.
- [x] Keep aggregation, lookup, filtering, calculation, windowing, and statistical preparation in
  visible ETL-SQL/`#temp` operations rather than adding hidden visual transforms.

**Exit gate:** Every new grammar form has a minimal working example, parser/LSP/formatter/designer
coverage, lineage behavior, cross-backend conformance, and no undocumented renderer-specific state.

**Completed:** Accepted renderer-neutral `CUSTOM`/`CHART` syntax now covers ordered mark layers,
named scales, Cartesian/transposed/polar coordinates, dual axes, conditional encodings, one- and
two-dimensional facets, and shared/independent scale resolution without embedded Vega-Lite or
hidden data transforms. Immutable AST and contracts, canonical formatting, Analysis lint and
lineage, LSP completion/hover/scoped rename, lossless Report Builder preservation, native SVG,
terminal, transient ECharts compilation, capability inventory, documentation, and parser-tested
examples share the same contract. Gemini's Phase 7 semantic-readiness suite remains as independent
regression evidence alongside the production conformance tests.

### Phase 8 — Standard Catalog Migration and ECharts Retirement

- [ ] Group remaining standard visuals by shared semantics and migrate them in independently testable
  batches rather than one catalog-wide rewrite.
- [ ] Expand native Cartesian, polar, statistical, hierarchical, and annotation coverage with shared
  `PlotPlan` conformance and visual goldens.
- [ ] Evaluate `GANTT` as a native time/band/rect/rule/text/dependency-path composition before
  classifying it as a specialized layout.
- [ ] Evaluate focused layout modules for maps, force networks, Sankey, treemap, and sunburst only
  after the native contract is stable.
- [ ] Before adding any layout dependency, complete license, maintenance, transitive-dependency,
  necessity, notices, and inventory checks required by the third-party policy.
- [ ] Conditionally omit ECharts from reports only when the capability matrix proves every contained
  visual and interaction is native.
- [ ] Remove `EChartsSsrRenderer`, ClearScript/V8 packages, and ECharts runtime assets only after no
  certified browser, export, interaction, or regression path requires them.
- [ ] Re-measure bundled/compressed size, cold start, memory, export time, and output size; report
  actual results rather than preserving a speculative size promise.

**Exit gate:** All graphical types are native or deliberately use an approved specialized module,
cross-surface evidence is green, ECharts/ClearScript have no remaining consumers, and dependency
inventories and runtime assets are synchronized.

### Phase 9 — Author Bookmarks

- [ ] Accept a design that distinguishes source-controlled bookmarks, user-created Portal saved
  views, and transient URL state.
- [ ] Define a serializable resolved bookmark state for parameters, active page, and the initially
  supported presentation state.
- [ ] Add parser, AST, manifest, lint, LSP, rename, documentation, and Report Builder support for the
  accepted bookmark syntax.
- [ ] Apply bookmark state atomically through the parameter engine established by cascading slicers.
- [ ] Add Report Player/Portal shell and in-canvas action invocation.
- [ ] Support offline replay and identifier-only URL hashes; do not disclose arbitrary parameter
  values in browser history or referrers.
- [ ] Add stale-reference, rename, atomic-application, offline replay, and URL disclosure tests.

**Exit gate:** Bookmarks apply atomically and portably, object references are statically safe and
rename-aware, and URLs reveal only the bookmark identifier.

### Phase 10 — Constrained HTML/SVG Presentation Templates

- [ ] Revise `MicroChartsAndHtmlEmbedding.md` to separate native micro-charts from arbitrary template
  rendering and record the accepted Zero-Trust boundary.
- [ ] Threat-model HTML, CSS, SVG, URLs, actions, data binding, template expansion, and export/fallback
  behavior before accepting syntax.
- [ ] Implement parsed element/attribute/property/URL allowlists, scoped selectors, and node, depth,
  byte, work, and recursion budgets; script stripping alone is not sufficient.
- [ ] Deliver static single-row presentation components before bounded repeaters.
- [ ] Add scoped styling and carefully allowlisted actions only after the static component boundary
  passes adversarial tests.
- [ ] Prohibit recursive visual/template expansion; consider non-recursive composition helpers only
  after deterministic budgets and fallbacks exist.
- [ ] Add accessibility rules and deterministic browser, export, email, terminal, and plain-text
  behavior.

**Exit gate:** Adversarial sanitizer, CSS isolation, URL, resource-exhaustion, recursion, action,
accessibility, and cross-surface tests pass with fail-closed behavior.

### Phase 11 — Visual Detail Popovers

- [ ] Accept a design for formatted detail and bounded semantic micro-charts with click, focus, hover,
  touch, keyboard, terminal, export, email, and assistive-technology behavior.
- [ ] Deliver formatted text detail first, then click/focus popovers, then one bounded micro-chart.
- [ ] Add viewport flip/shift positioning with deterministic fixtures.
- [ ] Enforce dependency-cycle, nesting, data-disclosure, node, byte, and render-work budgets.
- [ ] Start with visual targets; keep arbitrary container targets and recursively embedded dashboards
  out of the initial implementation.
- [ ] Measure interaction latency on a named report fixture instead of encoding an unverified
  sub-millisecond claim.

**Exit gate:** Keyboard, touch, accessibility, cycle, payload, positioning, disclosure, and measured
performance tests pass for the deliberately constrained feature set.

### Phase 12 — Reporting Samples, Conversion Guidance, and Closure

- [ ] Publish production-grade composite examples demonstrating layers, annotations, conditions,
  facets, interactions, accessibility, and cross-surface fallbacks with transformations visible in
  SQL.
- [ ] Publish a Vega-Lite-to-ETL-SQL concept guide covering layer, facet/repeat, scale resolution,
  conditional encoding, selections, parameters, transforms, lookup, windows, and themes.
- [ ] Update the Report-SQL guide, syntax index, focused reference pages, snippets, sample inventory,
  and cookbook for every syntax form that actually shipped.
- [ ] Complete serialization/version, cross-backend, visual-golden, accessibility, bundle, cold-start,
  memory, export-time, and output-size evidence defined by the GoG ADR.
- [ ] Reconcile the capability matrix with source and tests, remove stale migration language, update
  `ROADMAP.md` to retain only unfinished increments, and record shipped outcomes in `CHANGELOG.md`.

**Exit gate:** Documentation and samples are parser-tested and copy-pasteable, measurements are
reproducible, the capability matrix matches source, and no completed reporting work remains described
as future work.

## Platform and SaaS Parallel Lane

This lane may proceed in parallel with the reporting phases when capacity permits. Within the lane,
complete the phases in order because portability, reliability, and production claims depend on the
storage and authority foundations.

### Platform Phase 1 — Object-Native Artifact Storage Contract

- [ ] Write the provider-neutral object-storage ADR and failure model on top of the existing artifact
  storage abstraction.
- [ ] Define immutable/content-addressed objects, staging keys, conditional writes using ETags or
  version IDs, authoritative commit records, database-backed fencing, reconciliation, and abandoned
  staging garbage collection.
- [ ] Explicitly reject POSIX-style atomic-rename emulation through unreliable copy/delete sequences.
- [ ] Implement one object-store provider and certify concurrency, stale fences, partial writes, lost
  responses, retries, outages, reconciliation, and garbage collection.
- [ ] Add a second provider only after the first contract suite is provider-neutral; complete all
  dependency license and inventory work for provider SDKs.
- [ ] Integrate object storage with shared artifact consumers and large-content portability fixtures.

**Exit gate:** Two providers pass the same hostile contract suite and shared mutation cannot publish
partial, stale, or unfenced artifact state.

### Platform Phase 2 — Tenant Portability Correctness and Scale

- [ ] Define and implement a declared cross-system export consistency point.
- [ ] Fence cutover and prevent duplicate schedules or retained execution authority.
- [ ] Complete the eligible artifact/content inventory and reconcile stable IDs, counts, hashes,
  ownership, ACLs, and explicit exclusions.
- [ ] Add resumable, chunked large-content export/import using the object-native storage contract.
- [ ] Package a standalone validator that remains usable with customer-held keys after source access
  is gone.
- [ ] Add incremental deltas only after full-export consistency and completeness are certified.
- [ ] Optimize cross-provider performance only after correctness and resumability evidence passes.
- [ ] Certify hostile Shared-source isolation independently from the already-shipped Managed Dedicated
  export/exit evidence.

**Exit gate:** Concurrent export and cutover produce a declared, reconcilable consistency point;
interrupted large transfers resume; hostile packages fail before activation; and Shared isolation
evidence proves no cross-tenant content can enter a bundle.

### Platform Phase 3 — Workload Identity and M2M Hardening

- [ ] Write the federation/token-exchange threat model covering tenant, issuer, subject, audience,
  resource, owner, approval, lifetime, replay, and audit binding.
- [ ] Implement one short-lived CI workload-identity exchange and retain long-lived API credentials
  only as a compatibility baseline.
- [ ] Add GitHub, GitLab, and Azure DevOps OIDC federation through the same policy contract.
- [ ] Add certificate or `private_key_jwt` authentication where justified.
- [ ] Enable secretless scheduled workloads without allowing service identity to exceed its owner,
  tenant, resource, or approved operation.
- [ ] Add approval policies for sensitive publication/execution and credential-use anomaly evidence.
- [ ] Certify rotation, revocation, replay rejection, audience/resource restriction, and attributed
  audit behavior.

**Exit gate:** Supported automated workloads use short-lived audience-bound credentials, and hostile
cross-tenant, cross-resource, replay, approval-bypass, rotation, and revocation tests fail closed.

### Platform Phase 4 — Verified Viewer Context Threat Model and First Connector

- [ ] Write an ADR that separates asserted application context, OAuth delegated/on-behalf-of
  authentication, and Kerberos constrained delegation and states the assurance of each.
- [ ] Define a cryptographically authenticated Portal-to-Gateway envelope bound to tenant, resource,
  operation, viewer, executing credential, expiry, and replay protection.
- [ ] Define resource-specific claim allowlists, reserved keys, parameterized installation,
  transaction-local lifetime, fail-closed behavior, and connection-pool cleanup.
- [ ] Implement one asserted-context connector without claiming that the database authenticated the
  viewer.
- [ ] Prohibit deriving PostgreSQL role changes directly from OIDC roles or groups.
- [ ] Audit both the verified viewer context and the executing service credential.
- [ ] Add forgery, replay, cross-tenant/resource, reserved-key, injection, transaction-lifetime, and
  pool-reuse tests before adding more connectors.

**Exit gate:** The first connector passes the complete hostile-context and pool-cleanup suite, and the
documented security claim matches the identity actually authenticated by the database.

### Platform Phase 5 — Provider-Neutral Fault Certification

- [ ] Define reusable scenarios for process/worker loss, lease expiry and fencing races, database
  disconnect, partial artifact operations, storage outage, network partition, duplicate delivery,
  clock skew, and disk exhaustion before selecting a hosting-specific chaos tool.
- [ ] Implement deterministic local fault hooks and evidence capture.
- [ ] Add Docker/cloud adapters without changing scenario semantics.
- [ ] Prove no split-brain mutation, stale authority reuse, silent loss, or duplicate committed result.
- [ ] Permit named-checkpoint resume claims only for workloads that establish explicit resumable
  checkpoint semantics; verify safe failure and deliberate retry for all other workloads.
- [ ] Integrate the fault matrix into deployment-profile certification.

**Exit gate:** Repeated fault runs produce durable evidence for every supported provider/profile and
make no recovery claim stronger than the workload's checkpoint contract.

### Platform Phase 6 — Production Canaries

- [ ] Define hosted SLOs and the regions/failure domains each canary must exercise.
- [ ] Provision synthetic tenants, identities, resources, quotas, and costs that cannot reach customer
  systems or consume customer capacity.
- [ ] Implement an external health journey, then report and job journeys, then Gateway, export, and
  notification journeys.
- [ ] Distinguish ETL-SQL failures from synthetic dependency failures in evidence and alert routing.
- [ ] Automate canary credential rotation and compromise response.
- [ ] Use fault-injection drills to prove the expected SLO alerts fire without cross-tenant effects.

**Exit gate:** Production-like drills detect correctness and latency failures across required failure
domains without accessing customer data, exhausting customer quota, or producing ambiguous alerts.

### Platform Phase 7 — Measured Lean Worker Profile (Optimization Last)

- [ ] Measure startup working set, cold-start latency, published size, loaded assemblies, engine-only
  dependency closure, sandbox lifetime, and cost sensitivity before selecting an implementation.
- [ ] Decide whether the evidence justifies a dedicated engine-only entry project and explicit
  connector/profile manifest; record the decision.
- [ ] If justified, build the dedicated worker boundary before attempting trimming.
- [ ] Run an opt-in trimming experiment and preserve reflection, DI, connector discovery,
  cancellation, governance, and deployment-profile behavior.
- [ ] Publish a lean worker artifact only if reproducible measurements show material benefit and the
  full certification matrix passes.

**Exit gate:** A measured decision either closes the initiative without implementation or produces a
certified worker artifact with demonstrated cost/footprint improvement and no functional regression.

## v0.19.0 Release Evidence Gates

- [ ] Run the full local pre-release gate required by
  [`docs/releases/release-checklist.md`](docs/releases/release-checklist.md), including the selected
  SLT, Docker integration, scale, packaging, and platform lanes.
- [ ] Pass the Enterprise Release Evidence Checklist, `test-lane.ps1`, `Test-PreRelease.ps1`,
  `Test-EnterpriseHardeningCertification.ps1`, `admin restore --validate`, `ha-soak validate`, and
  `SecurityBoundaryDocTests` as applicable to the shipped v0.19.0 claims.
- [ ] Build the deployment-profile claim matrix from evidence and do not promote unfinished Shared
  SaaS or hosted-production roadmap outcomes into release claims.
- [ ] Verify third-party notices/inventory, secret scanning, SBOM, checksums, installers, release
  notes, upgrade guidance, and changelog entries for the final shipped scope.
- [ ] Reconcile `TODO.md` and `ROADMAP.md` immediately before release: remove verified completed work,
  retain unfinished increments with accurate status, and ensure release notes describe only evidence-
  backed outcomes.
>>>>>>> d504d881 (fix(portal): resolve release gate follow-ups for doc reconciliation, modal a11y, and sandbox fallback)

