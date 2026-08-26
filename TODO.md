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

- [x] Add `TRANSFORM` recipe snippets and authoring scaffolding. Expose built-in algorithms
  (`ROLLING_AGGREGATE`, `PERIOD_COMPARISON`, `SHARE_OF_TOTAL`, `TOP_N_OTHERS`, `FILL_DATES`, `PIVOT`,
  `INTERPOLATE`, `NORMALIZE`, `DEDUPLICATE`) via LSP trigger snippets (e.g., `$transform-mom`,
  `$transform-rolling`) and Report Builder data-prep helpers to streamline common analytical metrics.
- [ ] Implement standard Design Tokens (CSS variables) for report runtime and `HTML` visuals. Inject
  `--etl-surface-card`, `--etl-text-primary`, `--etl-text-muted`, `--etl-border`, `--etl-accent`,
  `--etl-success`, `--etl-danger`, and `--etl-radius-*` from resolved page/theme styles into the root DOM,
  enabling `HTML` visual scoped styles to automatically adapt across light, dark, and custom themes.
- [ ] Add `PALETTE = (...)` sequence support to `CREATE STYLE`. Parse and propagate color sequences from
  `CREATE STYLE` to multi-series charts, visuals, and theme tokens when assigned at the visual, container,
  or page level (`STYLE = StyleName`).
- [x] Add visual formatting pickers in Report Builder. Provide interactive color swatch pickers,
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

## 4. Code-First Connection Wizard & Schema-Driven Authoring

Authoritative references:
[`connectors-standards.md`](docs/architecture/standards/connectors-standards.md),
[`connectors.md`](docs/architecture/connectors.md),
[`portal.md`](docs/architecture/portal.md), and
[`language-server.md`](docs/architecture/language-server.md).

- [x] **Phase 1 — C# Schema Contract & Metadata Service (Tier 0 & 3)**:
  Extend `IConnector` and `IConnectorRegistry` to expose structured `ConnectorOptionDescriptor` metadata
  (name, value type [string, number, boolean, secret-ref, file-path], mandatory flag, default value,
  category [basic, auth, network, advanced], description, allowed values, and `MutuallyExclusiveWith` group).
  Implement connection string / URI parser helper (`ConnectionStringParser`) in `Connectors.Common` to decompose
  raw connection strings into structured options and isolate credentials into prompted secret references.
  Expose `GET /api/connectors/schema` and `POST /api/connectors/test` in Portal/Workstation and corresponding LSP
  methods (`etlsql/connectorSchema`, `etlsql/testConnection`). Add xUnit unit test coverage across all connectors.
- [x] **Phase 2 — Core UI Component & Multi-Host Embedding (Tier 2, 4 & 5)**:
  Implement the canonical `ConnectionWizard` component in `src/ETL-SQL.ReportRuntime/Resources/Shared/` and sync to Portal,
  Workstation Editor, and VS Code. Support dual-mode operation: **Script Mode** (emits canonical `CREATE CONNECTION name TYPE (...)`
  or `CREATE CONNECTION name AS TYPE('SHARED:alias')`) and **Admin Mode** (submits `POST /api/admin/connections`). Implement
  dynamic form generation from schema JSON, zero-trust password masking with `SECRET:name` combobox and `ENC:...` passphrase
  prompt, 4-layer diagnostic test runner UI, and live SQL preview. Embed into Report Builder, Workstation Editor, and VS Code
  extension treeview/command palette with smart script insertion (hoisting above datasets or cursor placement). Defer in-place AST edit mode.
- [x] **Phase 3 — Portal Staged Files, Gateway Routing & Boundary Checks**:
  Integrate Portal tenant staging file picker and dropzone for file connectors (`FLATFILE`, `PARQUET`, `EXCEL`),
  ensuring emitted paths are workspace-relative (`PATH = 'uploads/file.csv'`). Add active Data Gateway cluster
  routing selector for on-premise sources in Portal Admin (`GATEWAY = 'cluster_alias'`). Enforce zero-trust path
  boundary checks preventing absolute system root or traverse references (`../`, `C:\`, `/etc`) in the wizard UI.
  Add AST name collision checking before script insertion.
- [x] **Phase 4 — Documentation, Help Sync & Browser Test Gates**:
  Publish feature guide `docs/guides/feature-guides/connection-wizard.md` covering wizard authoring, diagnostic
  testing, secret vault integration, and file path guidelines. Update `docs/syntax-index.md` and connector
  reference pages. Add automated Playwright / `SandboxStoryTests` browser test coverage verifying schema rendering,
  diagnostic probe success/failure states, code generation accuracy for SQL and file connectors, and light/dark theme
  parity.

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
