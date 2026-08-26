# ETL-SQL Development TODO List

Use this list as the execution ledger for all unfinished product and release work. All remaining
product work is active for the current planning horizon. Work top to bottom unless a dependency or
release-blocking defect changes the order. Once an item is verified, record its notable outcome in
`CHANGELOG.md` and check it completed.

The only unfinished `ROADMAP.md` initiatives are represented here:

- **Lossless Visual Report Builder Editing** — section 1.
- **Constrained HTML Visuals** — sections 1 and 4.

---

## 1. Report Builder and Authoring

Authoritative references:
[`report-builder.md`](docs/guides/tooling/report-builder.md),
[`GrammarOfGraphicsSpecIR.md`](docs/architecture/decisions/grammar-of-graphics-spec-ir.md), and
[`constrained-html-visuals.md`](docs/architecture/decisions/constrained-html-visuals.md).

- [x] Finish lossless Report Builder editing certification. Surgical patching is already shared by
  the embedded and LSP-hosted paths, and regression tests cover multi-page scripts, chained CTEs,
  statement trivia, repeated CRLF/LF edits, and invalid intermediate scripts. Add deterministic
  mutation fuzz/property tests proving that every out-of-scope byte remains unchanged, plus a browser
  story and test proving that transient syntax errors retain the last valid canvas state.
- [x] Add a dedicated Report Builder `CHART` editor. Register `CUSTOM`, render a live preview, expose
  its supported layers, encodings, scales, coordinates, conditions, and refinement clauses without
  inventing a second grammar, and keep unedited or unsupported clause text byte-preserved. Cover
  embedded and LSP-hosted round trips, invalid intermediate edits, keyboard/accessibility behavior,
  and mutations of scripts containing advanced `CHART` syntax.
- [ ] Add Report Builder preview and lossless editing for the complete constrained `HTML` visual
  clause: optional `SOURCE`, `MODE`, `TEMPLATE`, scoped `STYLE`, `FALLBACK`, and declarative
  `ACTIONS`. Preserve unsupported or temporarily invalid text, use the same sanitizer and budgets as
  the runtime preview, and cover embedded/LSP parity. Design the component editor as a constrained
  structured authoring surface; it must not introduce arbitrary DOM scripting or raw executable
  HTML.

## 2. Browser and Offline Preview Foundations

- [x] Make the VS Code webview UI sandbox work from a clean checkout. The story fetches the ignored
  `src/etl-sql-vscode/ui/dist/index.html`, while `tools/ui-sandbox/serve.ps1` neither builds that
  bundle nor provides a self-contained fixture. Add a deterministic build/setup step or remove the
  generated-bundle dependency, then cover the results, preview, preview-sink, designer, and
  visual-flow fixtures with a clean-tree browser test.
- [x] Ship and exercise a real offline `.etlsnap` viewer/bootstrap before claiming offline bookmark
  or detail-popover replay. The shared runtime implements and tests the `window.__ETLSNAP__` branch,
  but no product host sets that flag today. Wire package loading to the runtime, prove bookmark and
  tooltip/detail replay without network access, and reconcile the offline claims in the Report-SQL,
  bookmark, tooltip, reporting-architecture, and report-CLI documentation.

## 3. Native Graphics Completion

These items are active in the current planning horizon. Native charts continue to use bounded **compact**,
**standard**, and **wide** layout tiers. `ChartSpec.InteractionSpec` remains the canonical semantic
interaction model, `PlotPlan` carries resolved semantics, and normal browser payloads receive only a
compact `InteractionManifest`.

- [ ] Implement bounded responsive layout tiers for native charts. Define compact/standard/wide
  bounds as explicit backend inputs. Observe the container after initial layout and request a
  debounced server re-resolve only when its tier changes; cache by report/visual/tier and preserve
  interaction state. Add tier-boundary, cache, interaction-refresh, PDF, and node-local HA-session
  tests. Do not send continuous resize traffic or restore `PlotPlan` to the browser.
- [ ] Implement deterministic smart label collision avoidance and viewBox bounds heuristics for
  `TEXT` mark layers and dense category axes. Cover clustered scatter points, multi-series line
  labels, and crowded band axes with priority-based occlusion pruning, angle staggering, or leader
  lines, plus stable golden and accessibility evidence.
- [ ] Expose statistical and financial channels in `CUSTOM`. Add `LOW`, `Q1`, `MEDIAN`, `Q3`,
  `HIGH`, `OPEN`, and `CLOSE` across parser, immutable AST, formatter, lowering, validation, plan,
  renderer, Analysis/LSP, documentation, samples, Report Builder, and golden coverage. Keep named
  `BOXPLOT` and `CANDLESTICK` as the concise presets while enabling layered combinations such as
  candlestick plus volume and box plot plus mean tick.
- [ ] Design and implement bounded geographic composition in `CUSTOM`. Define projection,
  geometry/map-source authority, region/point/label/route encodings, interaction semantics,
  zero-trust map-path handling, terminal fallback, and export behavior before extending the
  coordinate enum. Add parser-to-renderer, cross-surface, security, and layered-map examples.

Formatting precedence remains deterministic and cannot depend implicitly on the viewer's browser:
`SET REPORT TIME_ZONE` -> `Scheduler:DefaultTimeZone` -> `UTC`; `SET REPORT LOCALE` ->
`Reporting:DefaultLocale` -> invariant culture; visual `OPTIONS (NULL_LABEL = ...)` ->
`SET REPORT NULL_LABEL` -> `Reporting:DefaultNullLabel` -> `-`.

## 4. Reporting — Constrained HTML Visuals

Authoritative direction:
[`ROADMAP.md`](ROADMAP.md#reporting--presentation--constrained-html-visuals) and
[`constrained-html-visuals.md`](docs/architecture/decisions/constrained-html-visuals.md). Parser,
immutable AST, formatter, escaped evaluator, conditional form, sanitizer, scoped CSS, initial
manifest projection, and focused unit tests exist. The visual is not yet a shipped surface.

- [ ] Complete the `CREATE VISUAL <name> AS HTML (...)` language authoring surface. Add Analysis
  diagnostics, LSP completion/hover/rename, syntax-index registration, focused help, snippet,
  parser-tested documentation examples, and production samples for the implemented `SOURCE`,
  `MODE = SINGLE | REPEATER`, `TEMPLATE`, `STYLE (CSS = ...)`, `FALLBACK`, and `ACTIONS` clauses.
- [ ] Complete source-free, parameter-driven, single-row, and repeated-row components with explicit
  row, node, byte, output, and aggregate render-work budgets. Parameter refresh must publish one
  consistent manifest without observable partial template state; the current builder only caps
  repeater rows.
- [ ] Add bounded `VISUAL(...)` embedding for declared report visuals. Resolve references statically,
  reuse existing visual manifests and declarative actions, and reject missing targets, cycles,
  recursive/nested expansion, secret disclosure, and aggregate query/render budget overruns.
- [ ] Render sanitized HTML visuals in the shared browser runtime, print, and PDF paths without
  executing author code. Reuse Report-SQL parameter, navigation, bookmark, drill, and cross-filter
  actions instead of DOM scripting, and preserve theme and formatting precedence across hosts.
- [ ] Require or deterministically resolve a concise semantic summary for email, Markdown, terminal,
  plain text, screen readers, and unsupported surfaces. Static output must preserve analytical
  meaning and must not imply unavailable interaction.
- [ ] Finish hostile security and cross-surface certification. Existing unit tests cover core markup,
  URL, CSS, escaping, conditional, optional-source parser, and repeater behavior. Add disclosure and
  malformed-parser cases, embedded-visual cycle/budget tests, action and refresh parity,
  accessibility, snapshot and browser/print/PDF/email/Markdown/terminal conformance, deterministic
  output, payload/render budgets, and proof that script-authored JavaScript cannot execute on any
  host.

**Exit gate:** Authors can build source-free or data-bound bespoke presentation components with
sanitized HTML, isolated CSS, typed escaped bindings, declarative actions, and bounded embedded
visuals; transformations remain SQL; browser and static surfaces consume one deterministic contract;
unsupported surfaces expose an accessible semantic fallback; and hostile input fails closed without
executing author code.

## v0.19.0 Release Evidence Gates

Target Release: **v0.19.0**
Authoritative policy: [`release-checklist.md`](docs/releases/release-checklist.md) and
[`Enterprise_Release_Evidence_Checklist.md`](docs/architecture/decisions/enterprise-release-evidence-checklist.md)

- [ ] Run the full local pre-release gate required by the release checklist, including the selected
  SLT, Docker integration, scale, packaging, and platform lanes.
- [ ] Pass the Enterprise Release Evidence Checklist, `test-lane.ps1`, `Test-PreRelease.ps1`,
  `Test-EnterpriseHardeningCertification.ps1`, `admin restore --validate`, `ha-soak validate`, and
  `SecurityBoundaryDocTests` as applicable to the shipped v0.19.0 claims.
- [ ] Build the deployment-profile claim matrix from evidence and do not promote unfinished Shared
  SaaS or hosted-production outcomes into release claims.
- [ ] Verify third-party notices/inventory, secret scanning, SBOM, checksums, installers, release
  notes, upgrade guidance, and changelog entries for the final shipped scope.
- [ ] Reconcile `TODO.md` and `ROADMAP.md` immediately before release: remove verified completed work,
  retain unfinished increments with accurate status, and ensure release notes describe only
  evidence-backed outcomes.
