# ETL-SQL Development TODO List

Use this list as the execution ledger for all unfinished product and release work. All remaining
product work is active for the current planning horizon. Work top to bottom unless a dependency or
release-blocking defect changes the order. Once an item is verified, record its notable outcome in
`CHANGELOG.md` and check it completed.

Unfinished `ROADMAP.md` initiatives and release gates are represented below.

---

## 1. Native Graphics Completion

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

## 2. Reporting Authoring & Design System Enhancements

- [ ] Add `TRANSFORM` recipe snippets and authoring scaffolding. Expose built-in algorithms
  (`ROLLING_AGGREGATE`, `PERIOD_COMPARISON`, `SHARE_OF_TOTAL`, `TOP_N_OTHERS`, `FILL_DATES`, `PIVOT`,
  `INTERPOLATE`, `NORMALIZE`, `DEDUPLICATE`) via LSP trigger snippets (e.g., `$transform-mom`,
  `$transform-rolling`) and Report Builder data-prep helpers to streamline common analytical metrics.
- [ ] Add first-class `BULLET` visual type. Register `BULLET` in parser, AST, serializer, Report Builder
  palette, and SVG renderer with semantic mappings (`ACTUAL`, `TARGET`, `MIN`, `MAX`, `RANGES`) so authors
  can create bullet / target-vs-actual visuals without hand-crafting multi-layer `CUSTOM` GoG definitions.
- [ ] Implement standard Design Tokens (CSS variables) for report runtime and `HTML` visuals. Inject
  `--etl-surface-card`, `--etl-text-primary`, `--etl-text-muted`, `--etl-border`, `--etl-accent`,
  `--etl-success`, `--etl-danger`, and `--etl-radius-*` from resolved page/theme styles into the root DOM,
  enabling `HTML` visual scoped styles to automatically adapt across light, dark, and custom themes.
- [ ] Add `PALETTE = (...)` sequence support to `CREATE STYLE`. Parse and propagate color sequences from
  `CREATE STYLE` to multi-series charts, visuals, and theme tokens when assigned at the visual, container,
  or page level (`STYLE = StyleName`).
- [ ] Add visual formatting pickers in Report Builder. Provide interactive color swatch pickers,
  border-radius sliders, and typography controls in the Properties panel alongside direct text editing.
- [ ] Complete `{{SPARKLINE(...)}}` and `{{PROGRESS_BAR(...)}}` template macro helpers in `HTML` visuals.
  Evaluate inline SVG sparkline and progress indicators directly within template substitution strings.

## 3. Hybrid Connectivity & Gateway Enhancements

Authoritative references:
[`secure-outbound-gateway.md`](docs/administration/platform/secure-outbound-gateway.md),
[`saas-tenant-isolation.md`](docs/architecture/saas-tenant-isolation.md#11-secure-outbound-data-gateway), and
[`verified-viewer-context.md`](docs/architecture/decisions/verified-viewer-context.md).

- [ ] Add unified Gateway resource discovery and binding in Portal Admin UI. In `Admin → Connections`
  and `Admin → Data Gateways`, provide an interactive picker populated from live active Gateway clusters
  and their published approved resources (`IGatewaySession.PublishedResources`). Eliminate manual string
  entry for Gateway ID and Resource ID when configuring `SHARED:alias` bindings, and display online
  status, connector type, and allowed operation classes in the connection editor.
- [ ] Extend Verified Viewer Context propagation to additional RDBMS connectors. Add session-context
  parameterization and deterministic pool cleanup for SQL Server (`SESSION_CONTEXT`), MySQL (user session
  variables), and Oracle (`SYS_CONTEXT`). Maintain parity with the PostgreSQL implementation: HMAC-signed
  envelopes, tenant/resource binding validation, resource-level opt-in, parameterization, and fail-closed
  cleanup before connection reuse.
- [ ] Implement Ambiguous Write outcome alerting and Portal operations triage. When network disconnection
  or process termination causes an in-flight mutating operation to enter an ambiguous state in the outcome
  ledger, surface a high-priority alert on the Portal operations dashboard. Provide a dedicated triage
  view displaying operation ID, tenant, gateway, resource, correlation ID, and execution timestamp with
  administrative reconciliation actions (acknowledge, mark resolved, or discard) while maintaining the
  fail-closed ambiguous-write safety invariant.

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
