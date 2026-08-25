# ETL-SQL Development TODO List

Use this list as the execution ledger for open active-release and roadmap work. Once work is
verified, record its notable outcome in `CHANGELOG.md` and remove it from this file and, when
applicable, `ROADMAP.md`. Git and the changelog retain completion history. If evidence invalidates a
completion claim, add a new open entry with a concrete correction path.

---

## v0.19.0 Release Execution Ledger

Target Release: **v0.19.0**
Authoritative Policy: [`docs/releases/release-checklist.md`](docs/releases/release-checklist.md) and
[`docs/architecture/decisions/Enterprise_Release_Evidence_Checklist.md`](docs/architecture/decisions/enterprise-release-evidence-checklist.md)

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

- [x] Stop serializing `chartSpec`, `chartData`, and `plotPlan` to normal browser clients by
  introducing a deliberate browser-delivery DTO/projection rather than deleting the server contracts.
  **Done.** `BrowserDeliveryProjection` classifies every manifest property exactly once and owns both
  option sets: the browser options drop `chartSpec`, `chartData`, `plotPlan`, `microCharts[].plotPlan`,
  and the legacy `interactions` map, and the authorized diagnostic options keep them. The two are
  separate instances, so nothing reaches the diagnostic payload from the normal path. Every browser
  boundary goes through it — the Portal execution and designer endpoints (`this.BrowserManifest(...)`),
  `LoadLightweightLayoutJsonAsync`, the snapshot response's stored JSON (`ProjectStoredJson`), the
  ReportPlayer page embed, the LSP preview manifest, and the Workstation Editor preview — and the
  server object is left intact, so PDF, markdown, and terminal still resolve against the full
  contracts. `EveryVisualManifestProperty_IsDeliberatelyClassifiedForBrowserDelivery` fails if a new
  property is added without a decision. Re-measured end to end: the six representative fixtures fell
  from **170.1 KB combined manifest to 65.9 KB raw / 15.1 KB gzip** delivered, against 1.63 MB raw /
  416.5 KB gzip of shared assets; the baseline harness now prints both figures per fixture and the
  combined line so page weight is measured rather than inferred from shared assets alone.
- [x] Add a regression budget for `report-runtime.js`. **Done.** `docs/benchmarks/report-payload-budget.json`
  is a blessed measurement gating raw and gzip bytes for `report-runtime.js` (285,491 / 62,181),
  `report-runtime.css`, the shared runtime total, and end-to-end page weight, at a 3% + 2 KB tolerance.
  `ReportPayloadBudgetTests` runs it in the default lane; growth past tolerance fails with the measured
  delta. The only way past is `scripts\Test-ReportPayloadBudget.ps1 -UpdateBudget`, which rewrites the
  checked-in JSON so the new numbers land in the diff for review — no hard ceiling. Shrink never fails.
  A unit test pins the comparison itself (grows past tolerance -> fail, within tolerance -> pass,
  shrinks -> pass), so the gate cannot silently stop gating the way the Phase 8 observation did.
- [x] End-to-end page-weight measurement covering manifest plus shared assets. **Done.**
  `ReportingBaselineMeasurementHarness.MeasurePageWeights` reports, per representative fixture, the
  shared assets plus that report's delivered browser manifest in raw and gzip, and the generated
  `reporting-phase2-baselines.md` carries the table. Shared assets are counted once (they cache across
  reports in a session), the manifest per report. The heaviest fixture is what the budget gates, so a
  budget set on the lightest report cannot pass while the worst page regresses.
- [x] Record honestly what still dominates the browser payload. **Done**, and the re-measurement
  corrected the framing twice over. Report Runtime Asset Standards section 6 now carries the measured
  table. A report page downloads 979,829 B raw / 226,725 B gzip; `tabulator.min.js` (443,224 B),
  `arrow.min.js` (166,184 B), and `tabulator.min.css` (28,481 B) are 637,889 B raw / 151,362 B gzip of
  that — **65% of raw and 67% of gzip**, against 34% / 32% for the chart runtime. Neither vendor bundle
  was touched by the retirement or by this program. The second correction: the `Resources/Shared/`
  total (1,713,640 B) is not page weight. It includes 733,811 B of designer bundle
  (`codemirror-bundle.min.js`, `designer.js`, `designer.css`) that a report viewer never loads, so
  quoting it over-counts a report page by ~43%. The baseline harness now prints the dominant assets
  from measurement, so the note cannot drift from the bytes.

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
- [ ] **Post-v0.19:** implement smart label collision avoidance and viewBox bounds heuristics for `TEXT` mark
  layers and dense category axes. Clustered scatter points, multi-series line labels, and crowded discrete
  band axes currently render overlapping text nodes. Implement deterministic server-side bounding box
  estimation with priority-based label occlusion pruning, angle staggering, or leader line callouts,
  ensuring executive-ready rendering without requiring manual SQL label filtering.
- [x] Make the six approved focused layout modules consume shared presentation inputs. **Done.**
  `FocusedLayoutInputs.From(visual)` resolves, once per visual: theme tokens (`ChartStyleTokens.Theme`,
  the same source `ChartSpec.Theme` uses), series colours, an accessible name plus a `<desc>` built
  from the visual's `SemanticFallback`, the compact resolved `InteractionManifest` (stamped as
  `data-interaction-key` / `data-interaction-highlight`), and an explicit authored canvas from
  `OPTIONS (WIDTH = n, HEIGHT = n)`, clamped to 120-4000 px and defaulting to 600x350. `ChartPalette`
  in `ETL-SQL.Reporting.Contracts` is now the single series-colour rule — `PlotPlanResolver.ResolveColor`
  and every focused module resolve through it, so `COLOR:<series>` cannot reach one path and miss the
  other. The private `Palette` array and the hardcoded canvas are gone; so are the hardcoded `white`,
  `#94a3b8`, `#e2e8f0`, and `#f8fafc` literals, which now come from theme tokens and flip with a dark
  theme. The geometry stayed focused: nothing was forced through `PlotPlan`, and sizing is an explicit
  backend input, not a viewport reading — a relative `WIDTH = '100%'` falls back to the default rather
  than inventing a viewport. `FocusedLayoutPresentationConformanceTests` proves it side by side:
  `tests/fixtures/reporting/conformance/focused_layout_shared_presentation.rptsql` puts a BAR and a
  TREEMAP over the same categories on one page, and the test fails if any category's treemap tile
  fill differs from that series' `PlotPlan` palette colour. Six more cases assert every focused type
  resolves surface, description, and canvas from the shared inputs.
- [x] Bring the deliberate Portal-only `native-charts.js` path under operational-UI asset governance.
  **Done, and the separation was preserved rather than dissolved.** The asset carries an ownership
  banner naming its owner, its boundary, and the gates that hold it. `PortalOperationalChartAssetTests`
  asserts all five: **ownership** (banner present and accurate), **dependency/license** (no import,
  require, remote URL, or fallback to a real ECharts global), **accessibility** (`role="img"` with an
  `aria-label` taken from the caller's chart title — it previously labelled every chart "Native
  chart"), **behaviour** (the shim surface `orchestrator.html` calls is intact, and every
  template interpolation reaching `innerHTML` is either numeric or escaped), and **footprint**
  (8 KB raw / 3 KB gzip cap; it is 3,836 B raw today). Two boundary tests back them: the asset stays
  out of `Resources/Shared/`, out of `scripts/sync-assets.js`, and out of the capability matrix, and
  `orchestrator.html` is still its only consumer. The boundary is written down in
  `docs/architecture/portal-ui.md` and Report Runtime Asset Standards section 7.
- [x] Make `InteractionSpec` the canonical chart interaction contract and retire the duplicate legacy
  semantic path after a compatibility migration. **Done.** `ChartInteractionResolver` is the single
  lowering and resolution path: both `NamedVisualChartLowerer` and `AdvancedChartLowerer` call
  `Lower(...)`, which produces real `SelectionSpec`s and typed `InteractionBinding`s from the authored
  `ACTIONS`/`INTERACTIONS` clauses. `PlotPlanResolver` resolves that against the chart's encodings and
  data columns into `PlotPlan.Interaction` (`ResolvedInteraction`), and `VisualBuilder` projects the
  compact `InteractionManifest` onto `visual.interaction`. Visuals with no chart contract — TABLE,
  SLICER, the focused layout modules — resolve through `ResolveTabular` so there is still one contract
  per visual. `visual.actions` stays: it carries executable payload the browser genuinely consumes.
  `visual.interactions` is gone from browser delivery; the runtime's `legacyInteraction()` shim reads
  it only when `visual.interaction` is absent, which covers snapshots and cached artifacts built
  before v0.19.0. Both in-repo consumers (`report-runtime.js`, `ReportTab.tsx`) are migrated and the
  shim is pinned by tests on each side.
- [x] Fix cross-filter column selection for layered visuals by emitting and consuming the resolved
  interaction key in the compact browser `InteractionManifest`. **Done.** The key is resolved
  server-side from the chart's encodings and delivered as `visual.interaction.key`; the browser's
  `mapping:*` chain and its `visual.columns[0]` fallback are both gone, and a key that names no column
  in the visual raises no filter at all rather than a wrong one. New fixture
  `tests/fixtures/reporting/conformance/custom_crossfilter_offset_key.rptsql` is a `CUSTOM` chart whose
  X binding is the second source column; it is asserted on the server
  (`ChartInteractionContractTests`, `BrowserDeliveryCompatBreakTests`) and end to end in the browser
  (`reportRuntime.test.ts` drives a click through JSDOM and asserts the posted request carries
  `@Region = North`, not the column-zero revenue amount). The `src/etl-sql-vscode` `test:unit` lane is
  green at 37 tests.
- [x] Remove the generic browser renderer's dependence on `visual.visualType` by emitting semantic
  highlight metadata. **Done.** `ResolvedMarkLayer` now carries `ExtentAxis`/`ExtentAnchor` — the axis
  a mark's value grows along and the edge it grows from — resolved in `PlotPlanResolver` from the
  coordinate kind and mark kind, and deliberately `None` for ranged rects (an author-supplied interval
  owns both endpoints, so its height is a span, not a value) and for focused layouts. `PlotPlanSvgRenderer`
  publishes it on the mark as `data-extent-axis`/`data-extent-anchor`, and the highlight treatment
  itself (`CATEGORICAL` vs `PROPORTIONAL`) is resolved server-side onto the interaction manifest.
  `applyNativeHighlight` reads both and computes the overlay generically; when no mark declares an
  extent it falls through to the categorical treatment rather than leaving a selection invisible. A
  test asserts the function body contains no `visualType` and no chart-type literals. The only
  surviving reference is inside `legacyInteraction()`, the pre-v0.19 manifest shim. Representative SVG
  goldens were re-blessed for the five fixtures whose plain rect marks gained the attributes.

#### Grammar and authoring

- [ ] **Found while building the golden lane:** an unrecognised `MAPPINGS` role is accepted silently and
  produces a wrong chart. `CREATE VISUAL x AS BAR (MAPPINGS (CATEGORY = Region, VALUE = Revenue))`
  parses and evaluates with no diagnostic, but no visual documents a `CATEGORY` role — the canonical
  named-`BAR` spelling is `X`/`Y`. The result is a plan with no X scale at all: the category axis is
  missing, and the semantic fallback a screen reader and the terminal read labels the rows "Row 1",
  "Row 2", "Row 3" instead of the actual categories. This is a wrong answer, not a crash, so it is at
  least P0 by the triage rule. Reproduce by changing the `MAPPINGS` in
  `tests/fixtures/reporting/conformance/rosetta_bar_named_and_custom.rptsql` back to `CATEGORY`/`VALUE`
  and re-blessing. Decide whether unknown roles should be a lint warning or a hard validation error,
  then cover every named visual type — this was found on `BAR` because that is what the Rosetta pair
  used, and nothing suggests `BAR` is special.

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

- [x] Rename and correct the two cookbook pages published as declarative-graphics recipes that contain
  no `CHART (` block. **Done.** They are now `docs/cookbooks/report/choropleth-point-map.md` and
  `docs/cookbooks/report/alluvial-flow-composition.md`, linked under the new names from the cookbook
  index. Both now describe what they are: coordinated named visuals over shared staged data. The
  choropleth page dropped its "single map surface" claim and says plainly that Report-SQL has no
  geographic composition in the `CHART` grammar, so a choropleth and its point overlay cannot share a
  projected canvas — two `MAP` visuals on one page and one source is the pairing an author gets. The
  alluvial page dropped "Grammar of Graphics ribbon composition"; `SANKEY` owns its own layout. No
  layered surface was faked ahead of the deferred geographic contract. `custom-bullet-target-
  performance.md` and `custom-marginal-scatter-plot.md` keep their prefix — they genuinely contain
  `CHART (`. Grammar-backed recipes for the rest arrive with the statistical/financial and geographic
  features.
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

**Closed.** One lane now covers both catalogs: `ReportingGoldenTests` discovers every fixture under
`tests/fixtures/reporting/conformance/` and pins the resolved `PlotPlan`, the native SVG, the
`SemanticFallback`, and the terminal render as checked-in artifacts, hashed in `index.json`. Blessing
is `scripts/Test-ReportingGoldens.ps1 -UpdateGolden`.

The state this replaced: standard visuals were pinned by SVG hash in
`NativeSvgGeometryGoldenTests.RepresentativeGoldens` — ten hand-listed fixtures folded into one
assertion — and `CUSTOM` had no equivalent. `AdvancedChartProductionTests` is 22 tests of contract
properties (`Assert.Equal(.4m, ...YEnd)`, `Assert.Equal(BindingSourceKind.Datum, ...)`) and pins no
output at all, so a renderer change that shifted every advanced-chart mark, or dropped a layer, passed
the whole file green.

- [x] Pin two hashes per fixture, not one: the resolved `PlotPlan` and the rendered SVG, compared
  independently. **Done.** `ReportingGoldenTests` compares them as separate artifacts, so the failure
  message distinguishes the two cases rather than reporting one merged movement.
  Original rationale: A single SVG hash reports that something moved but cannot separate a broken chart
  from a nudged label. With both, a plan hash that holds while the SVG hash moves is a pure rendering
  change to review, and a plan hash that moves is a semantic regression to stop on. The plan hash is
  the durable gate: `PlotPlan.Validate()` already enforces deterministic series, legend, and layer
  ordering, and `ChartContractSerializer` already carries stable-serialization coverage from
  `GrammarOfGraphicsContractTests`.
- [x] Commit the artifacts, not only their hashes. **Done.** Every fixture has a directory under
  `tests/fixtures/reporting/goldens/` holding `<visual>.plan.json`, `<visual>.svg`,
  `<visual>.fallback.json`, and `terminal.txt`; `index.json` keeps the hashes as the fast comparison.
  The lane also fails when a hash matches but the checked-in artifact does not, so the two cannot
  drift apart. 35 charts across 32 fixtures come to ~1 MB of text, most of it serialized plans.
  Original rationale: The current goldens store bare SHA-256 strings in a
  C# dictionary, so when one moves the diff reads `AE3BF4... -> 7C21B9...` and a reviewer can only
  trust it or reproduce it by hand — a hash change is not reviewable. Check in the SVG and the
  serialized plan beside each fixture and keep the hash as the fast comparison. The entire
  representative SVG set is 18.2 KB, so the cost is negligible and blessing becomes a diff a human can
  open in a browser.
- [x] Discover fixtures from the directory and drive them with `[Theory]`/`MemberData`. **Done.**
  `ReportingGoldenHarness.DiscoverFixtures` enumerates `tests/fixtures/reporting/conformance` in
  ordinal order, and each fixture is its own theory case naming the chart that moved. Discovery
  yields a sentinel rather than an empty set, so an empty directory fails instead of reporting a
  green lane with zero cases, and `GoldenIndex_HasNoEntriesWithoutAFixture` fails on a stale entry.
  Superseded detail:
  `RepresentativeNativeSvg_GeometryMatchesApprovedGoldens` currently folds all eight fixtures into one
  `Assert.True` with a joined string, which reports "something moved" instead of naming the chart.
- [x] Migrate the existing standard-catalog fixtures onto the same harness. **Done.** The hard-coded
  `RepresentativeGoldens` dictionary in `NativeSvgGeometryGoldenTests` is gone; every fixture in the
  conformance directory now runs on the one harness, which took the pinned set from ten hand-listed
  entries to all 32. `GoldenLane_CoversBothTheNamedAndCustomCatalogs` fails if either catalog stops
  being represented. Micro-chart geometry stayed behind — it is built from a factory, not a fixture.
  Original rationale: Items two and three are improvements the standard goldens want on
  their own merits, and one harness keeps the two catalogs from drifting into different definitions of
  "pinned".
- [x] Choose the `CUSTOM` corpus to cover what the grammar reaches and named visuals cannot. **Done.**
  Five fixtures were added: `custom_inherit_encodings_datum_value` (chart-scope inheritance,
  `INHERIT_ENCODINGS = OFF`, `DATUM`, `VALUE`), `custom_conditions_normalized_stack` (`CONDITIONS`,
  `STACK = NORMALIZE`), `custom_jitter_nudge_placement` (`JITTER` with a seed, `NUDGE`, `X_OFFSET`),
  `custom_facet_wrap_aspect_ratio` (`FACET WRAP`, `ASPECT_RATIO`, `RESOLVE`), and
  `custom_gradient_color_tick` (quantitative `GRADIENT` colour range, `TICK`). Terminal and
  `SemanticFallback` are pinned for every fixture in the lane, both catalogs, not only these.
  Original rationale: encoding inheritance and `INHERIT_ENCODINGS = OFF`, `DATUM` and
  `VALUE` bindings, `CONDITIONS`, `STACK = NORMALIZE`, jitter and nudge placement, `FACET WRAP`,
  `ASPECT_RATIO`, quantitative color ranges, and `TICK`. Jitter especially — it is SHA-256 over
  semantic layer placement, stable key, channel, and seed, so it is deterministic by construction and
  therefore exactly the kind of property that degrades silently. Pin the terminal render and the
  `SemanticFallback` from the same fixtures too; both consume the same plan and neither has `CUSTOM`
  coverage today.
- [x] Record the determinism precondition the SVG lane depends on, and keep it enforced. **Done.**
  `NativeSvgDeterminismPreconditionTests` asserts it four ways: the same plan rendered under `de-DE`
  is byte-identical to the invariant render (the decimal separator is the sharpest locale probe);
  repeated renders are equal and carry no clock-derived text or GUID; every `font-family` is the
  generic `sans-serif`; and the renderer source contains no clock, GUID, ambient-culture, or
  text-measurement API. The last one fails at the moment the option is taken rather than when CI
  hashes start disagreeing with a developer machine, and its message says what to do then — pin
  metrics or make the SVG lane advisory, with the plan hash staying the durable gate.
  Original rationale: Native SVG is
  currently hash-stable across platforms because `PlotPlanSvgRenderer` emits no timestamp, GUID, or
  `CurrentCulture` formatting and uses a generic `sans-serif` family with no text measurement. ADR
  section 8.2 explicitly permits text measurement "where needed"; the day that option is taken, SVG
  hashes become font- and platform-dependent and the lane goes flaky between developer machines and
  CI. Design for it now — the plan hash stays the durable gate, and if text measurement lands the SVG
  lane needs pinned metrics or must become advisory. Add an assertion that the rendered SVG contains
  no locale- or clock-derived text so the precondition cannot regress unnoticed.
- [x] Provide blessing under the existing convention. **Done.** `scripts/Test-ReportingGoldens.ps1`
  takes `-UpdateGolden` (plus `-Fixture` for iterating on one chart), setting
  `ETLSQL_REPORTING_GOLDEN_UPDATE` the way `Test-ReportPayloadBudget.ps1 -UpdateBudget` does. It
  rewrites the artifacts, then prints the `git status` of the golden directory so the refreshed SVG,
  plan, fallback, and terminal files are what lands in the commit and gets reviewed.


#### A learning path from named visuals into `CUSTOM`

`docs/guides/reporting/` carries `vega-lite-to-etl-sql.md` and `ggplot2-to-etl-sql.md` — two on-ramps
for authors arriving from another grammar — and nothing for an author arriving from ETL-SQL's own
named visuals, which is the common direction. `docs/reference/visuals-reporting/visuals/chart.md` is
114 lines with a single heading, a syntax dump rather than a tutorial, and both `CUSTOM` samples
(`samples/10_Kitchen_Sinks/39_CUSTOM_LAYERS.rptsql`,
`samples/08_Reporting/declarative_geometry_refinements.rptsql`) open at full complexity. A reader has
a reference and two finished showpieces with no path between them. Sequence this with the golden
coverage item above; it depends on the same harness.

- [x] Assert the translations rather than asserting them in prose. **Done.**
  `NamedVisualToCustomRosettaTests` pairs each named visual with its `CUSTOM` spelling in one fixture
  over one staged source, so the comparison cannot drift apart through the data. Compared: resolved
  layers, scales, palette, series, data, accessible summary, semantic fallback, theme tokens, and
  formatting. `BAR` and `LINE` pairs are equal across all of it. Excluded, and each exclusion pinned
  by its own assertion so it cannot widen: identity (spec/scale/layer ids name the authoring form),
  the per-visual-type null policy (`BAR` skips a null row, `LINE` gaps it; `CUSTOM` resolves the
  grammar default), and the style tokens transcribing `MAPPINGS`/`OPTIONS` (`mapping:x`, `mapping:y`,
  `AXIS_SORT`). Interaction turned out **not** to be divergent — resolved key and value key agree —
  so it is asserted equal rather than excused. Theme tokens and `FormattingSpec` are inside the scope
  as recorded; the only formatting difference is that named lowering lists its mapped columns as
  entries carrying no format, and a test fails the moment one of those starts carrying one.
  Original rationale: `NamedVisualChartLowerer` already
  lowers `BAR` into a `ChartSpec`, so each named/`CUSTOM` pair can ship as two fixtures whose resolved
  plans are compared by the golden harness. The teaching claim then becomes a test that cannot rot
  when the lowerer changes. Scope the comparison to resolved layers, scales, palette, and data:
  whole-plan equality will not hold today because named lowering sets per-type null policy and
  interaction bindings that `CUSTOM` has no `MAPPINGS` clause to produce. Theme tokens and the
  resolved `FormattingSpec` are no longer part of that divergence — both lowerers build them through
  `ChartStyleTokens` — so treat any difference there as a regression, not as test noise.

## Documentation — Focused Topic Ownership

Post-v0.19 documentation-maintenance backlog; this is not a v0.19 release gate. Review snapshot from
2026-08-25: `docs/` contains 1,046 Markdown files, including 718 under `docs/reference/`. The focused
reference structure is established and `node scripts/audit-syntax-index.js --strict` currently finds
all 676 non-index reference pages linked with no broken syntax-index targets. The remaining work is
mostly canonical ownership, consistent topic-page shape, and removal of umbrella-page duplication.

Do not apply a line-count rule mechanically. A topic page owns one user question or one language
surface and keeps its syntax, semantics, examples, guardrails, troubleshooting, and FAQ together.
Hub pages route readers. Guides own multi-topic workflows without becoming a second syntax reference.
Cookbooks own runnable end-to-end scenarios. Architecture pages own one subsystem model or one
cross-cutting decision; their unit of responsibility is larger than a command or function page.

- [x] Turn the current convention into an enforceable page-ownership contract. Update
  `docs/README.md`, the templates under `docs/templates/`, and
  `docs/architecture/standards/Help_and_Snippet_Standards.md` together so each documentation type
  names what it owns, what it only links to, and the required local sections. Include a migration
  rule for `docs/reference/**`: preserve runtime-help keywords, embedded-resource globs, and language
  metadata mappings whenever a reference page moves or is renamed.
- [x] Add a repository-wide documentation audit instead of relying only on syntax-index coverage.
  Check local Markdown targets and anchors, duplicate canonical-topic claims, title/filename policy,
  generated hub membership, and template conformance by reference type. Keep
  `audit-syntax-index.js --strict`, but add the broader audit to the docs/pre-push lane. Make generated
  README descriptions deterministic or support curated descriptions; the current generator truncates
  arbitrary opening prose and produces weak entries such as the clipped rows in
  `docs/architecture/README.md`.
- [x] Finish normalizing the focused help corpus against its type-specific templates, confirming each
  example against the parser/formatter and live implementation while touching it. **Done.**
  Normalized 245/245 function topic pages (including `coalesce.md` and `data-conversion.md`), 54/54 statement
  pages (with `## Syntax`, `## Examples`, `## References`), 31/31 connector pages (with `## Authentication`
  and `## Troubleshooting`), and 38/38 visual reference pages (standardizing on level-2
  Syntax/Mappings/Options/Examples/References). Verified 100% template conformance across all reference types
  via `audit-docs.js --strict` and `audit-syntax-index.js --strict`.

- [x] Replace the remaining broad user-facing manuals with small landing pages plus canonical topic
  pages. **Done.** Reduced `guides/feature-guides/report-sql.md` to a focused authoring path and 3-tier
  architecture overview routing to canonical `reference/visuals-reporting/**` and `guides/reporting/**`
  pages; reconciled `guides/feature-guides/data-quality.md` to an overview routing to modular
  `guides/data-quality/**` and `reference/statements/dml/data-quality-rules.md`; and streamlined
  `guides/feature-guides/testing.md` to an entry point routing to `guides/testing/**` and architecture test
  strategy docs.

- [x] Split the 515-line `reference/statements/session-control/lineage.md` by actual help topic. **Done.**
  Created `export-lineage.md` (covering OpenLineage JSONL and Markdown/Mermaid exports), `import-lineage.md`
  (covering OpenLineage imports and seed deletions), and `governance-tags.md` (covering inline comment tags,
  standard tag library, and inheritance). Retained `lineage.md` as the focused statement reference for the
  lineage query surface and cross-linked all related topics and catalog surfaces. Updated syntax index and hub.

- [x] Eliminate known competing owners before adding more pages. **Done.** Retired duplicate
  `guides/operations/one-person-quality-loop.md` in favor of canonical
  `guides/patterns/one-person-quality-loop.md`; retired duplicate
  `administration/portal/portal-config-reference.md` in favor of canonical
  `administration/platform/config/portal-configuration.md`; updated all inbound links across
  `docs/` in the same change and verified zero broken links and zero hub membership gaps via
  `audit-docs.js` and `audit-syntax-index.js --strict`.
- [x] Distribute standalone FAQ answers to the page that owns each topic. **Done.**
  Streamlined `guides/patterns/faq.md` and umbrella guides to serve as pure navigation routers to the
  canonical topic pages (`getting-started.md`, `eng/version.md`, `send-email.md`, `config.md`, `secrets.md`,
  and troubleshooting guides). Verified zero link or anchor drift via `audit-docs.js --strict`.

- [x] Keep architecture overviews, ADRs, standards, and threat-model documents cohesive; do not split
  them solely because they are long. **Done.** Preserved architecture overviews as atomic subsystem maps
  (Engine, Reporting, Portal, Connectors, Orchestrator) while routing connector troubleshooting, operator
  procedures, syntax indexes, and API inventories to their canonical reference and administration pages.
  Verified ADRs and standards remain self-contained.

- [x] After each migration slice, regenerate affected hubs and the syntax index, run the strict index
  audit plus the new docs audit, build the embedded help corpus, and exercise representative CLI help
  and LSP hover topics. **Done.** Ran incremental migration slices with automated audit gating. Final
  `node scripts/audit-docs.js --strict` passed with 0 broken links, 0 filename policy violations, 0 hub gaps,
  and 0 template conformance gaps across all 1,049 markdown files, and `node scripts/audit-syntax-index.js --strict`
  passed with 0 broken links and 0 unlinked pages across 679 reference pages.


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

- [x] Write the federation/token-exchange threat model covering tenant, issuer, subject, audience,
  resource, owner, approval, lifetime, replay, and audit binding.
- [x] Implement one short-lived CI workload-identity exchange and retain long-lived API credentials
  only as a compatibility baseline.
- [x] Add GitHub, GitLab, and Azure DevOps OIDC federation through the same policy contract.
- [x] Add certificate or `private_key_jwt` authentication where justified.
- [x] Enable secretless scheduled workloads without allowing service identity to exceed its owner,
  tenant, resource, or approved operation.
- [x] Add approval policies for sensitive publication/execution and credential-use anomaly evidence.
- [x] Certify rotation, revocation, replay rejection, audience/resource restriction, and attributed
  audit behavior.

**Exit gate:** Supported automated workloads use short-lived audience-bound credentials, and hostile
cross-tenant, cross-resource, replay, approval-bypass, rotation, and revocation tests fail closed.

### Platform Phase 4 — Verified Viewer Context Threat Model and First Connector

- [x] Write an ADR that separates asserted application context, OAuth delegated/on-behalf-of
  authentication, and Kerberos constrained delegation and states the assurance of each.
- [x] Define a cryptographically authenticated Portal-to-Gateway envelope bound to tenant, resource,
  operation, viewer, executing credential, expiry, and replay protection.
- [x] Define resource-specific claim allowlists, reserved keys, parameterized installation,
  transaction-local lifetime, fail-closed behavior, and connection-pool cleanup.
- [x] Implement one asserted-context connector without claiming that the database authenticated the
  viewer.
- [x] Prohibit deriving PostgreSQL role changes directly from OIDC roles or groups.
- [x] Audit both the verified viewer context and the executing service credential.
- [x] Add forgery, replay, cross-tenant/resource, reserved-key, injection, transaction-lifetime, and
  pool-reuse tests before adding more connectors.

**Exit gate:** The first connector passes the complete hostile-context and pool-cleanup suite, and the
documented security claim matches the identity actually authenticated by the database.

### Platform Phase 5 — Provider-Neutral Fault Certification

- [x] Define reusable scenarios for process/worker loss, lease expiry and fencing races, database
  disconnect, partial artifact operations, storage outage, network partition, duplicate delivery,
  clock skew, and disk exhaustion before selecting a hosting-specific chaos tool.
- [x] Implement deterministic local fault hooks and evidence capture.
- [x] Add Docker/cloud adapters without changing scenario semantics.
- [x] Prove no split-brain mutation, stale authority reuse, silent loss, or duplicate committed result.
- [x] Permit named-checkpoint resume claims only for workloads that establish explicit resumable
  checkpoint semantics; verify safe failure and deliberate retry for all other workloads.
- [x] Integrate the fault matrix into deployment-profile certification.

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
