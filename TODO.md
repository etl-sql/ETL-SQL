# ETL-SQL Development TODO List

Use this list as the execution ledger for open active-release and roadmap work. Once work is
verified, record its notable outcome in `CHANGELOG.md` and remove it from this file and, when
applicable, `ROADMAP.md`. Git and the changelog retain completion history. If evidence invalidates a
completion claim, add a new open entry with a concrete correction path.

---

## v0.19.0 Release Execution Ledger

Target Release: **v0.19.0**
Authoritative Policy: [`docs/releases/release-checklist.md`](docs/releases/release-checklist.md) and
[`docs/architecture/decisions/Enterprise_Release_Evidence_Checklist.md`](docs/architecture/decisions/Enterprise_Release_Evidence_Checklist.md)

Only unfinished work remains below. Keep the Platform/SaaS phases in their listed order. When a
roadmap outcome is fully delivered and verified, update or retire its `ROADMAP.md` entry and record
the shipped result in `CHANGELOG.md`.

## Release Gate Follow-ups

Open release follow-ups confirmed by the completion audit.

- [ ] Make the VS Code webview UI sandbox work from a clean checkout. The story fetches the ignored
  `src/etl-sql-vscode/ui/dist/index.html`, while `tools/ui-sandbox/serve.ps1` neither builds that
  bundle nor provides a self-contained fixture. Add a deterministic build/setup step or remove the
  generated-bundle dependency, then cover the results, preview, preview-sink, designer, and
  visual-flow fixtures with a clean-tree browser test.
- [ ] Ship and exercise a real offline `.etlsnap` viewer/bootstrap before claiming offline bookmark
  or detail-popover replay. The shared runtime implements and tests the `window.__ETLSNAP__` branch,
  but no product host sets that flag today. Wire package loading to the runtime, prove bookmark and
  tooltip/detail replay without network access, and reconcile the offline claims in the Report-SQL,
  bookmark, tooltip, reporting-architecture, and report-CLI documentation.
- [ ] Reconcile the completed object-native artifact-storage work in `ROADMAP.md`. The implementation,
  ADR, S3/Azure hostile provider suite, portability integration, and changelog entry are present, but
  the roadmap still says the provider contract is undecided and lists the completed delivery slices
  as future work.

### Found during the post-Phase 13 declarative-graphics review

Recorded from a read of the shipped contracts, lowerers, renderers, browser runtime, lint rule, and
designer against `GrammarOfGraphicsSpecIR.md`. None of these are Phase 13 regressions — each is
either a boundary the program left open or a divergence between shipped behaviour and what the ADR
and capability matrix claim. Every item names the file and symbol so it can be confirmed before any
work starts.

#### Accepted direction and execution order

The design questions from this review are resolved. Implement in this order so a later item does not
restore payload or coupling removed by an earlier one:

1. **v0.19 correctness and authoring:** finish `CUSTOM` cross-filtering, correct the misleading
   recipes, build the shared golden harness, and assert the learning-path translations.
2. **v0.19 delivery and footprint:** publish a browser-specific manifest DTO, add compact resolved
   interaction metadata, remove unused semantic payload from normal delivery, and gate raw plus gzip
   size.
3. **v0.19 contract completion:** make `InteractionSpec` canonical, give focused layout modules the
   shared theme/palette/interaction/sizing inputs they need, remove chart-type decisions from the
   generic browser renderer, and bring Portal operational charts under their own asset gates.
4. **Post-v0.19 bounded features:** responsive layout tiers and expanded statistical, financial, and
   geographic `CUSTOM` composition. These are accepted product directions, but are not v0.19 exit
   gates and must not be smuggled into the correctness fixes above.

The following decisions are authoritative for the items below:

- Native charts use bounded **compact**, **standard**, and **wide** layout tiers. The browser requests
  a server re-resolve/re-render only when it crosses a tier, with debounce and cache reuse; it does
  not send continuous resize traffic.
- `ChartSpec.InteractionSpec` is the canonical semantic interaction model. `PlotPlan` carries resolved
  semantics, while the normal browser payload receives only a compact `InteractionManifest`.
- `CUSTOM` will support statistical/financial channels and, in a later separately designed feature,
  geographic composition. Named visuals remain the concise authoring path.
- `TREEMAP`, `SUNBURST`, `SANKEY`, `NETWORK`, `MAP`, and `MATRIX` remain approved focused native
  layout modules. They are not required to lower through `PlotPlan`, but they must consume shared
  theme, palette, accessibility, interaction, sizing, and test inputs rather than inventing parallel
  presentation semantics.
- Portal operational graphics in `native-charts.js` remain a separate internal-UI implementation.
  They belong in asset ownership, dependency, accessibility, and footprint gates, not in the
  Report-SQL capability matrix or `PlotPlan` contract.
- Report formatting precedence is deterministic and never depends implicitly on the viewer's browser:
  `SET REPORT TIME_ZONE` -> `Scheduler:DefaultTimeZone` -> `UTC`;
  `SET REPORT LOCALE` -> `Reporting:DefaultLocale` -> invariant culture; and
  visual `OPTIONS (NULL_LABEL = ...)` -> `SET REPORT NULL_LABEL` ->
  `Reporting:DefaultNullLabel` -> `-`.

#### Payload and footprint

The retirement's headline numbers are real (`docs/benchmarks/reporting-phase8-results.md`: shared
assets 2.65 MB to 1.56 MB raw, 761 KB to 398 KB gzip; ClearScript's 131 MB multi-RID estimate to
zero). Per-report payload moved the other way, and nothing measures that.

- [ ] Stop serializing `chartSpec`, `chartData`, and `plotPlan` to normal browser clients by
  introducing a deliberate browser-delivery DTO/projection rather than deleting the server contracts.
  `VisualManifest`
  emits all three alongside `rows` and `nativeSvg` (`ReportManifest.cs`, the `chartSpec`/`chartData`/
  `plotPlan`/`nativeSvg` properties), and `VisualBuilder` populates them on every graphical visual.
  The browser reads exactly two: `renderNativeSvg` consumes `visual.nativeSvg` and `visual.rows`
  (`report-runtime.js`), and no runtime path reads `plotPlan` or `chartSpec` at all — the only
  mention is a comment. Nothing strips them before the manifest is served. That is five
  representations of one chart on the wire where two are used, and it is why the six representative
  fixtures record a 155.0 KB combined manifest against an 18.2 KB combined SVG. The trade as shipped
  exchanged once-cached library bytes for uncached per-report bytes on every report load. Keep the
  contracts available to server renderers, tests, and an explicit authorized diagnostic output; do
  not make a general production query flag expose them accidentally. Emit the compact resolved
  `InteractionManifest` required by browser actions/cross-filtering, then re-measure end-to-end page
  weight — manifest plus assets — not shared assets alone.
- [ ] Add a regression budget for `report-runtime.js`. It was 228,448 bytes immediately before the
  ECharts retirement (`d9fc135d~1`), 217,299 bytes immediately after, and is 280,472 bytes at
  `6ccb98d4` — now larger than the file the retirement shrank. `Measure-ReportingBaselines.ps1
  -CheckOnly` records footprint, but the Phase 8 numbers are a point-in-time observation rather than
  a gate, so this growth passed unremarked. Gate both raw and gzip bytes the way the engine performance
  budgets are gated, with an explicit reviewed baseline-update path rather than a magic hard ceiling.
- [ ] Record honestly what still dominates the browser payload. Of the remaining 1.56 MB shared
  assets, roughly 1.05 MB is `tabulator.min.js` (443 KB) and `arrow.min.js` (166 KB) plus their CSS —
  neither touched by this program. Any future footprint claim should name them rather than implying
  the chart runtime is the remaining cost.

#### Shipped behaviour that diverges from the ADR or the capability matrix

- [ ] **Post-v0.19:** implement bounded responsive layout tiers for native charts. The only
  `ResizeObserver` in the runtime repositions
  tooltips (`report-runtime.js`). `PlotPlanSvgRenderer` emits a fixed `width`/`height` with a
  matching `viewBox`, and `.native-chart-wrapper svg` is stretched to `width:100%; height:100%`
  (`report-runtime.css`). Under the default `preserveAspectRatio` the whole scene scales uniformly
  and letterboxes, so type and stroke widths scale with the container: labels are unreadable in a
  narrow card and oversized in a wide one, and the plan is never re-resolved for the actual viewport.
  ADR section 4 reserves viewport-dependent layout for "explicit backend inputs or bounded backend
  decisions"; no such input exists today. Define compact/standard/wide bounds as explicit backend
  inputs. After initial layout, observe the container and request a debounced server re-resolve only
  when its tier changes; cache results by report/visual/tier and preserve current interaction state.
  Do not send continuous resize traffic, restore `PlotPlan` to the browser, or claim that
  `vector-effect` alone fixes label/tick layout. Add tier-boundary, cache, interaction-refresh, PDF,
  and node-local HA-session tests.
- [ ] Make the six approved focused layout modules consume shared presentation inputs.
  `SpecializedNativeSvgRenderer` renders TREEMAP, SUNBURST,
  SANKEY, NETWORK, MAP, and MATRIX directly from `VisualManifest.Rows` with a hardcoded 600x350
  canvas and its own private `Palette` array — no `PlotPlan`, no shared series colours, no theme
  tokens. Keeping focused algorithms is intentional and already stated by the ADR and capability
  matrix; do not force these types through `PlotPlan`. Instead pass shared theme/palette,
  accessibility, compact interaction metadata, and explicit sizing inputs into the focused modules,
  and add side-by-side conformance tests proving a focused visual matches a `PlotPlan` visual's active
  theme and series colours.
- [ ] Bring the deliberate Portal-only `native-charts.js` path under operational-UI asset governance.
  `src/ETL-SQL.Portal/wwwroot/js/native-charts.js` is a second
  charting implementation shaped as an ECharts-compatible shim (`setOption`, `dispatchAction`,
  `on(...)`, a no-op `resize()`), driving the orchestrator page's Gantt, sparkline, and dependency
  graph (`orchestrator.html`). It is not in `Resources/Shared/`, not in `scripts/sync-assets.js`, and
  not in the reporting capability matrix. That separation is intentional and documented in
  `docs/architecture/PortalUI.md`: do not turn it into a `PlotPlan` consumer or place it in the
  Report-SQL matrix. Explicitly record its ownership boundary and add dependency/license,
  accessibility, behavioural, and raw/gzip footprint gates for the operational UI asset.
- [ ] Make `InteractionSpec` the canonical chart interaction contract and retire the duplicate legacy
  semantic path after a compatibility migration. ADR section 3 lists tooltip, selection,
  action, and interaction semantics as part of the `ChartSpec` contract. `NamedVisualChartLowerer`
  populates only `InteractionBinding`s and always an empty `Selections` array; `AdvancedChartLowerer`
  never populates interactions at all; `PlotPlan` does not carry them; no renderer reads them.
  `SelectionSpec` and `SelectionMode` appear only in `ChartSpec.cs` and one test fixture.
  Interactions still flow entirely through the pre-GoG `visual.actions`/`visual.interactions`
  manifest fields. Lower both named and `CUSTOM` authoring into `InteractionSpec`, resolve it into
  `PlotPlan`, and project only a compact `InteractionManifest` to browser clients. Keep the legacy
  fields only for a tested migration window, then remove them and their serialized weight.
- [ ] Fix cross-filter column selection for layered visuals by emitting and consuming the resolved
  interaction key in the compact browser `InteractionManifest`. `renderNativeSvg` derives the filter
  column as `options['mapping:x']` falling through a chain of other `mapping:*` keys to
  `visual.columns[0]` (`report-runtime.js`). `CUSTOM` visuals have no `MAPPINGS` clause and therefore
  no `mapping:*` options, so every layered chart silently lands on `visual.columns[0]` and
  cross-filters on whatever column happens to be first. Derive the key server-side from resolved
  encodings; do not serialize or inspect the whole `PlotPlan` in the browser. Add an end-to-end test
  for a `CUSTOM` chart whose first source column is not its X binding. The current worktree reaches
  the correct `Region = North` request, but `src/etl-sql-vscode`'s `test:unit` lane is red because the
  new assertion expects the legacy parameter-map body instead of the current `params` request array;
  finish the contract migration and make that lane green before closing this item.
- [ ] Remove the generic browser renderer's dependence on `visual.visualType` by emitting semantic
  highlight metadata. `applyNativeHighlight` branches on the
  visual type being `BAR`/`HBAR`/`HORIZONTALBAR` to clone marks and compute partial-height selection
  overlays in the browser (`report-runtime.js`). ADR section 5.2 states renderers do not inspect
  layer names or global flags; inspecting the visual's type name to decide geometry is the same
  failure. Resolve proportional-highlight behaviour server-side and expose it through mark attributes
  or the compact interaction manifest so the browser can render it without knowing a chart name.

#### Grammar and authoring

- [ ] **Post-v0.19:** expose statistical and financial channels in `CUSTOM`.
  `ReportParser.ParseAdvancedChannel` and `AdvancedChartChannel` expose eighteen channels;
  `FieldChannel` additionally carries `Low`, `Q1`, `Median`, `Q3`, `High`, `Open`, and `Close`, which
  `NamedVisualChartLowerer` uses for `BOXPLOT` and `CANDLESTICK`. A hand-composed box plot or
  candlestick — a candlestick with a volume layer, a box plot with an overlaid mean `TICK` — is
  therefore not expressible; those charts exist only as sealed presets. Add the channels with parser,
  AST, lowering, validation, plan, renderer, documentation, sample, and golden coverage. Keep named
  `BOXPLOT` and `CANDLESTICK` as the recommended concise presets.
- [ ] **Post-v0.19:** design and implement geographic composition as a separate bounded feature.
  `AdvancedChartCoordinateKind` is `Cartesian`,
  `TransposedCartesian`, `Polar`; `CoordinateKind` in `ChartSpec` also has `Geographic`. Layered map
  composition is therefore unavailable to `CUSTOM`, which is why
  `docs/cookbooks/report/custom-choropleth-point-map.md` presents two independent named `MAP` visuals
  rather than one layered surface. Define projection, geometry/map-source authority, region/point/
  label/route encodings, interaction semantics, zero-trust map-path handling, terminal fallback, and
  export behaviour before adding the enum member; an enum-only change is not an implementation.
#### Published examples

Sharing a chart is meant to be "here is the script" — no gallery and no marketplace feature, which is
the deliberate simplification against tools like Power BI. That makes the published examples the
whole distribution mechanism, so their accuracy carries the weight a feature would.

- [ ] Rename and correct the two cookbook pages published as declarative-graphics recipes that contain
  no `CHART (` block: `docs/cookbooks/report/custom-choropleth-point-map.md` and
  `docs/cookbooks/report/custom-alluvial-flow-composition.md`. Both are ordinary named visuals, both
  carry the `custom-` prefix, and both shipped in `1888d892` alongside the two that do use the
  grammar. Drop the `custom-` prefix and every claim that they demonstrate layered `CUSTOM` grammar;
  describe them honestly as coordinated named visuals. Geographic composition is accepted but
  deferred above, so do not fake a single layered surface before that contract exists. Add genuinely
  grammar-backed recipes later with the corresponding statistical/financial and geographic features.
- [ ] **Post-v0.19:** treat a Report Builder `CHART` editor as a future goal, not a current correctness
  gap. `CUSTOM` is absent from the
  designer's visual-type registry (`designer.js`, `VCATEGORIES`), and
  `DesignerScriptPatcher.PatchElementStatement` skips the `CHART` clause outright — "Advanced
  authoring is deliberately opaque until the designer has a dedicated CHART editor." For a
  script-first language that is the right default: authors compose the grammar in SQL and verify
  through report preview, so the preview loop above is what needs to be good. Recorded here only so
  the omission reads as a decision. If it is ever revisited, listing the type and rendering a live
  preview — with the clause still byte-preserved on mutation — is the increment that pays first.

#### Golden output coverage for `CUSTOM` charts

Standard visuals are pinned by SVG hash in `NativeSvgGeometryGoldenTests.RepresentativeGoldens` —
eight fixtures under `tests/fixtures/reporting/conformance/`. `CUSTOM` has no equivalent.
`AdvancedChartProductionTests` is 22 tests of contract properties (`Assert.Equal(.4m, ...YEnd)`,
`Assert.Equal(BindingSourceKind.Datum, ...)`) and pins no output at all, so a renderer change that
shifts every advanced-chart mark, or drops a layer, passes the whole file green. The single `CUSTOM`
fixture in the corpus, `custom_ordinal_secondary_points.rptsql`, is consumed by
`StandardCatalogCartesianMigrationTests`, not by a golden. Build the lane once, for both catalogs.

- [ ] Pin two hashes per fixture, not one: the resolved `PlotPlan` and the rendered SVG, compared
  independently. A single SVG hash reports that something moved but cannot separate a broken chart
  from a nudged label. With both, a plan hash that holds while the SVG hash moves is a pure rendering
  change to review, and a plan hash that moves is a semantic regression to stop on. The plan hash is
  the durable gate: `PlotPlan.Validate()` already enforces deterministic series, legend, and layer
  ordering, and `ChartContractSerializer` already carries stable-serialization coverage from
  `GrammarOfGraphicsContractTests`.
- [ ] Commit the artifacts, not only their hashes. The current goldens store bare SHA-256 strings in a
  C# dictionary, so when one moves the diff reads `AE3BF4... -> 7C21B9...` and a reviewer can only
  trust it or reproduce it by hand — a hash change is not reviewable. Check in the SVG and the
  serialized plan beside each fixture and keep the hash as the fast comparison. The entire
  representative SVG set is 18.2 KB, so the cost is negligible and blessing becomes a diff a human can
  open in a browser.
- [ ] Discover fixtures from the directory and drive them with `[Theory]`/`MemberData`, so adding a
  chart means adding files rather than editing C#, and so each fixture reports as its own test result.
  `RepresentativeNativeSvg_GeometryMatchesApprovedGoldens` currently folds all eight fixtures into one
  `Assert.True` with a joined string, which reports "something moved" instead of naming the chart.
- [ ] Migrate the eight existing standard-catalog fixtures onto the same harness rather than standing
  up a parallel `CUSTOM`-only lane. Items two and three are improvements the standard goldens want on
  their own merits, and one harness keeps the two catalogs from drifting into different definitions of
  "pinned".
- [ ] Choose the `CUSTOM` corpus to cover what the grammar reaches and named visuals cannot, since
  that is the uncovered surface: encoding inheritance and `INHERIT_ENCODINGS = OFF`, `DATUM` and
  `VALUE` bindings, `CONDITIONS`, `STACK = NORMALIZE`, jitter and nudge placement, `FACET WRAP`,
  `ASPECT_RATIO`, quantitative color ranges, and `TICK`. Jitter especially — it is SHA-256 over
  semantic layer placement, stable key, channel, and seed, so it is deterministic by construction and
  therefore exactly the kind of property that degrades silently. Pin the terminal render and the
  `SemanticFallback` from the same fixtures too; both consume the same plan and neither has `CUSTOM`
  coverage today.
- [ ] Record the determinism precondition the SVG lane depends on, and keep it enforced. Native SVG is
  currently hash-stable across platforms because `PlotPlanSvgRenderer` emits no timestamp, GUID, or
  `CurrentCulture` formatting and uses a generic `sans-serif` family with no text measurement. ADR
  section 8.2 explicitly permits text measurement "where needed"; the day that option is taken, SVG
  hashes become font- and platform-dependent and the lane goes flaky between developer machines and
  CI. Design for it now — the plan hash stays the durable gate, and if text measurement lands the SVG
  lane needs pinned metrics or must become advisory. Add an assertion that the rendered SVG contains
  no locale- or clock-derived text so the precondition cannot regress unnoticed.
- [ ] Provide blessing under the existing convention — an `-UpdateGolden` switch matching
  `Test-SpillAllocProfile.ps1 -UpdateBudget` — and make the refreshed artifacts, not just the hashes,
  the thing that lands in the commit.


#### A learning path from named visuals into `CUSTOM`

`docs/guides/reporting/` carries `vega-lite-to-etl-sql.md` and `ggplot2-to-etl-sql.md` — two on-ramps
for authors arriving from another grammar — and nothing for an author arriving from ETL-SQL's own
named visuals, which is the common direction. `docs/reference/visuals-reporting/visuals/chart.md` is
114 lines with a single heading, a syntax dump rather than a tutorial, and both `CUSTOM` samples
(`samples/10_Kitchen_Sinks/39_CUSTOM_LAYERS.rptsql`,
`samples/08_Reporting/declarative_geometry_refinements.rptsql`) open at full complexity. A reader has
a reference and two finished showpieces with no path between them. Sequence this with the golden
coverage item above; it depends on the same harness.

- [ ] Assert the translations rather than asserting them in prose. `NamedVisualChartLowerer` already
  lowers `BAR` into a `ChartSpec`, so each named/`CUSTOM` pair can ship as two fixtures whose resolved
  plans are compared by the golden harness. The teaching claim then becomes a test that cannot rot
  when the lowerer changes. Scope the comparison to resolved layers, scales, palette, and data:
  whole-plan equality will not hold today because named lowering sets per-type null policy and
  interaction bindings that `CUSTOM` has no `MAPPINGS` clause to produce. Theme tokens and the
  resolved `FormattingSpec` are no longer part of that divergence — both lowerers build them through
  `ChartStyleTokens` — so treat any difference there as a regression, not as test noise.

## Reporting — Constrained HTML Visuals

Authoritative direction:
[`ROADMAP.md`](ROADMAP.md#reporting--presentation--constrained-html-visuals). The exact grammar and
security contract must be accepted in an ADR before parser implementation.

- [ ] Write the HTML-visual threat model and ADR. Define the exact template grammar, typed
  substitutions and conditional form, sanitizer allowlists, URL policy, CSS isolation, interaction
  projection, embedded-visual boundaries, budgets, and portable fallback contract.
- [ ] Add `CREATE VISUAL <name> AS HTML (...)` through lexer/parser, immutable AST, canonical
  formatter, Analysis lint, LSP completion/hover/rename, syntax index, focused help, snippet, and
  parser-tested examples. Preserve the normal visual clause shape with optional `SOURCE`,
  `MODE = SINGLE | REPEATER`, `TEMPLATE`, `STYLE (CSS = ...)`, and `ACTIONS (...)`.
- [ ] Implement the dependency-free typed template evaluator. Support escaped field and parameter
  substitution plus the accepted small conditional form; keep aggregation, lookup, filtering,
  calculation, and all other transformations in visible SQL. Do not add arbitrary expressions or a
  raw-HTML substitution escape hatch.
- [ ] Implement the HTML and CSS security boundary before rendering. Reject JavaScript, scripts,
  inline event handlers, unsafe URL schemes, iframes, objects, embeds, global document mutation,
  external form submission, and unapproved elements, attributes, URLs, or inline SVG. Scope CSS to
  the visual while exposing only approved report theme variables.
- [ ] Support source-free, parameter-driven, single-row, and repeated-row components with explicit
  row, node, byte, output, and render-work budgets. Parameter refresh must publish one consistent
  manifest without observable partial template state.
- [ ] Add bounded `VISUAL(...)` embedding for declared report visuals. Resolve references statically,
  reuse existing visual manifests and declarative actions, and reject missing targets, cycles,
  recursive/nested expansion, secret disclosure, and aggregate query/render budget overruns.
- [ ] Render sanitized HTML visuals in the shared browser runtime, print, and PDF paths without
  executing author code. Reuse Report-SQL parameter, navigation, bookmark, drill, and cross-filter
  actions instead of DOM scripting, and preserve theme and formatting precedence across hosts.
- [ ] Require or deterministically resolve a concise semantic summary for email, Markdown, terminal,
  plain text, screen readers, and unsupported surfaces. Static output must preserve analytical meaning
  and must not imply unavailable interaction.
- [ ] Keep the first delivery script-first. Add preview and lossless Report Builder preservation for
  the complete HTML clause, but defer a WYSIWYG HTML editor until it is separately designed and
  approved.
- [ ] Add hostile-markup/URL/CSS/disclosure tests, escaping and typed-binding tests, optional-source
  and repeater fixtures, embedded-visual cycle/budget tests, action and refresh parity, accessibility,
  snapshot and cross-surface conformance, deterministic output, payload/render budgets, production
  samples, capability inventory, and proof that script-authored JavaScript cannot execute on any host.

**Exit gate:** Authors can build source-free or data-bound bespoke presentation components with
sanitized HTML, isolated CSS, typed escaped bindings, declarative actions, and bounded embedded
visuals; transformations remain SQL; browser and static surfaces consume one deterministic contract;
unsupported surfaces expose an accessible semantic fallback; and hostile input fails closed without
executing author code.

## Platform and SaaS Parallel Lane

This lane may proceed in parallel with the reporting phases when capacity permits. Within the lane,
complete the phases in order because portability, reliability, and production claims depend on the
storage and authority foundations.

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
