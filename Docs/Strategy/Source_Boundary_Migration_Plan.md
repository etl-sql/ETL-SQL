# Source Boundary Migration Plan

ETL-SQL has become more consolidated, but source-tree cleanup should stay incremental. The current priority is to make ownership boundaries obvious enough that new work lands in the right place and host applications stay thin.

This plan is intentionally conservative: document the target boundaries first, keep behavior stable, then move one boundary at a time with focused tests.

## Target Ownership

### Core Language Contracts

`src/ETL-SQL.Core` owns language contracts shared by every host and runtime:

- AST records, parser/lexer contracts, language metadata, shared expression and report model contracts.
- Cross-cutting value objects and interfaces that do not execute scripts or depend on a specific host.

Core should not depend on host shells such as ReportPlayer, ReportPortal, VS Code, the TUI, or CLI apps.

### Engine Execution

`src/ETL-SQL.Engine` owns script execution:

- Evaluator orchestration, statement handlers, query execution, temp-table behavior, spill behavior, transactions, and runtime security checks.
- Engine-side report preparation that requires executing ETL-SQL or resolving execution context state.
- Zero-trust path resolution through `IExecutionContext.ResolvePath()`.

Engine code may expose execution services to hosts, but hosts should not reimplement engine semantics.

### Connectors

`src/ETL-SQL.Connectors` owns provider-specific access:

- SQL, file, API, SFTP/FTP, object storage, and other connector implementations.
- Provider exception wrapping, connector authentication handling, connection options, read/write adapters, and source-specific type mapping.

Connector code should not know about Report Portal, VS Code, the TUI, or host UI concepts.

### Analysis

The target home for static language intelligence is a dedicated `ETL-SQL.Analysis` area. It should absorb analysis code that is currently spread across Core, Engine, LanguageServer, and host projects:

- Lint rules, dialect checks, lineage analysis, explain plans, documentation/help verification, diagnostics, quick fixes, and analyzer test utilities.
- Pure analysis over AST, manifests, metadata, and source text.

Analysis should not execute user scripts, perform connector I/O, or host reports. If an analyzer needs execution-time facts, pass those facts in as explicit inputs or contracts rather than reaching into host services.

### Reporting Semantics

`src/ETL-SQL.ReportBuilder` currently owns most reporting semantics. The target shape is `ETL-SQL.Reporting` once the report manifest, style cascade, visual/page/container/dataset semantics, and chart behavior are stable enough to rename without churn.

This area should own:

- `ReportManifest` construction and validation.
- Visual, page, container, navigation, dataset, style, theme, action, and chart semantics.
- Server-resolved report state that every host must render consistently.

The rename should happen after compatibility shims are defined. A package/project rename should not be combined with unrelated behavior changes.

### Report Runtime Assets

The canonical browser runtime lives in:

```text
src/ETL-SQL.ReportRuntime/Resources/Shared/
```

Generated host copies live under ReportPlayer, ReportPortal, and the VS Code extension. Do not edit those copies directly; use `node .\scripts\sync-assets.js`.

`ETL-SQL.ReportRuntime` is the dedicated source area for canonical JavaScript, CSS, themes, browser dependencies, runtime fixtures, and runtime debugging utilities. The contract in `Docs/Report_Runtime_Contract.md` is the source of truth.

### Report Hosting Services

`src/ETL-SQL.ReportHosting` owns reusable report session services that need Engine execution plus Reporting manifest construction, but should not belong to a specific host shell:

- Report script evaluation sessions, parameter state, selective visual refresh, manifest caching, background dataset refresh timers, and multi-report manifest factories.
- Shared hosting behavior used by ReportPlayer and ReportPortal.

This boundary may depend on Engine and Reporting. ReportPlayer and ReportPortal may depend on ReportHosting, but ReportPortal should not depend on ReportPlayer.

### Host Shells

Hosts should provide shell behavior and delegate semantics downward:

- ReportPlayer: local/standalone hosting, HTTP routes, session plumbing, static asset hosting, and report embedding.
- ReportPortal: authentication, authorization, folders, publishing, snapshots, subscriptions, audit, portal navigation, and dataset registry APIs.
- VS Code extension: editor integration, webview shell, LSP client wiring, commands, packaging, and marketplace-facing structure. Preserve the ecosystem-friendly `src/etl-sql-vscode` folder/package naming.
- TUI, CLI, App, Orchestrator, and LanguageServer: user interaction, process hosting, protocol wiring, scheduling, diagnostics transport, and command surfaces.

Hosts must not fork report semantics, style resolution, runtime JavaScript behavior, path containment rules, or ETL-SQL execution rules.

## Migration Order

### Phase 0: Boundary Documentation

Complete this plan, keep `AGENTS.md` aligned, and reference `Docs/Report_Runtime_Contract.md` for report runtime rules.

### Phase 1: Stabilize Before Moving

Finish the active runtime, security, reporting consistency, golden workflow, smoke lane, and permission work before broad source moves. Source cleanup is safer when those regression checks are already available.

### Phase 2: Create `ETL-SQL.Analysis`

Move static language intelligence first because it has clear boundaries and high reuse across VS Code, CLI, TUI, docs, and CI.

Current progress:

- `src/ETL-SQL.Analysis` exists and owns linting contracts, `Linter`, `LinterFactory`, metadata overlays, and lint rules under `Linting/`.
- `src/ETL-SQL.Analysis/Lineage` owns static lineage analysis and graph rendering; Core still owns `LineageEntry`, `ILineageTracker`, and `LineageTracker` runtime state.
- `src/ETL-SQL.Analysis/Explain` owns explain-plan building heuristics; Engine still owns `EXPLAIN ANALYZE` execution, console rendering, telemetry wiring, and `INTO` output.
- `src/ETL-SQL.Analysis/Documentation` owns documentation/help verification utilities; Core still owns embedded help resources and the help registry contract.
- `src/ETL-SQL.Analysis/Diagnostics` owns neutral diagnostic shaping for parser diagnostics, lint results, and analysis exceptions; host projects still own protocol-specific diagnostic conversion.
- App, Engine, Orchestrator, TUI, LanguageServer, and lint/analysis tests reference `ETL-SQL.Analysis` for static analysis services instead of reaching into Core.

Recommended first moves:

- Lint rules and lint orchestration that do not execute scripts. *(Done: initial linting move complete.)*
- Dialect checks and diagnostic models. *(Done: dialect checks moved with linting; neutral diagnostic shaping moved; parser diagnostics remain Core language contracts.)*
- Lineage analyzer and graph rendering that operate on AST/source facts. *(Done: `LineageAnalyzer` and `LineageGraphRenderer` moved; runtime tracker state remains in Core.)*
- Explain-plan and documentation/help verification utilities. *(Done: explain-plan builder and help documentation verifier moved.)*

Phase 2 is now functionally complete for the first-pass boundary. Keep parser and AST contracts in Core. Keep runtime evaluation in Engine. Add compatibility shims where needed so hosts can migrate one at a time.

### Phase 3: Create `ETL-SQL.ReportRuntime`

Move canonical browser assets out of Core only after the sync check is stable.

Recommended steps:

1. Add the new runtime source area with the same canonical files. *(Done: canonical assets moved to `src/ETL-SQL.ReportRuntime/Resources/Shared` and represented by `ETL-SQL.ReportRuntime.csproj`.)*
2. Update `scripts/sync-assets.ps1` and `Docs/Report_Runtime_Contract.md`. *(Done: sync/check tooling now reads from ReportRuntime and covers nested map assets; use the Node wrapper for cross-platform runs.)*
3. Run the runtime sync check. *(Done: `node .\scripts\sync-assets.js -Check` passes from the ReportRuntime source.)*
4. Verify ReportPlayer, ReportPortal, and VS Code still consume generated copies. *(Done: ReportPlayer and ReportPortal build from generated host copies; VS Code compile runs the canonical sync script.)*

Phase 3 is complete. Keep canonical browser runtime changes in `ETL-SQL.ReportRuntime`, run the sync script after edits, and treat host runtime files as generated outputs.

### Phase 4: Reshape `ReportBuilder` into `Reporting`

Do this after report manifests and style/runtime behavior settle.

Current progress:

- `src/ETL-SQL.Reporting` exists as the reporting semantics boundary.
- Serializable report manifest contracts now live in `ETL-SQL.Reporting` under the `ETL_SQL.Reporting` namespace.
- Manifest and visual builders now live in `ETL-SQL.Reporting`; they still operate over `IExecutionContext` and do not move script execution ownership out of Engine.
- Style, page, and dataset builder semantics now live in `ETL-SQL.Reporting`; this project references Core for report AST/context contracts.
- Theme-to-ECharts JSON translation now lives in `ETL-SQL.Reporting`; the Engine `CREATE THEME` handler remains the execution entry point and forwards to the reporting helper.
- Shared report snapshot persistence now lives in `ETL-SQL.Reporting`.
- Markdown, SVG, PDF, and terminal rendering now live in `ETL-SQL.Reporting`.
- Shared ECharts chart rendering semantics now live in `ETL-SQL.Reporting` with namespace compatibility retained.
- `ETL-SQL.ReportBuilder` references `ETL-SQL.Reporting` and continues to own the engine-facing `EXPORT REPORT` statement handler as the compatibility assembly.

Recommended steps:

1. Introduce the new project or namespace boundary. *(Done: project boundary created; namespace compatibility retained for now.)*
2. Move manifest, style, visual, page, container, dataset, chart, and action semantics. *(Done for the first-pass boundary: manifest/visual builders, manifest contracts, style/page/dataset builders, theme translation, Markdown/SVG/PDF/terminal rendering, snapshot persistence, and shared ECharts chart semantics moved.)*
3. Leave compatibility references or forwarding types while hosts migrate. *(Done: `ETL-SQL.ReportBuilder` remains for the engine-facing export handler, and `CreateThemeStatementHandler.BuildEChartsTheme` forwards to the reporting helper.)*
4. Update each host separately with smoke coverage. *(Done for current hosts: ReportPlayer, ReportPortal, CLI, Engine, ReportBuilder, and focused reporting/snapshot tests pass.)*
5. Rename packages/projects only after references are clean.

Phase 4 is functionally complete for the first-pass boundary. Keep `ETL-SQL.ReportBuilder` as the compatibility assembly for the engine-facing `EXPORT REPORT` handler.

Phase 4b progress:

- Shared reporting source now uses the `ETL_SQL.Reporting` namespace.
- Repo callers have migrated from shared `ETL_SQL.ReportBuilder` types to `ETL_SQL.Reporting` types.
- `ETL_SQL.ReportBuilder.ExportReportStatementHandler` remains as the compatibility entry point for handler discovery.

### Phase 5: Thin Host Cleanup

After Analysis, ReportRuntime, and Reporting boundaries exist, remove any remaining duplicated semantics from hosts. Keep host changes focused on shell behavior: auth, routing, protocol, process lifetime, UX, and persistence.

Current progress:

- Report interaction refresh/dependency semantics moved from ReportPlayer into `ETL-SQL.Reporting` as `ReportInteractionRefresher`; ReportPlayer now delegates parameter-driven visual refresh behavior to Reporting.
- Report CSV rendering moved from ReportBuilder/ReportPortal host code into `ETL-SQL.Reporting` as `CsvRenderer`; export hosts now share the same table selection and CSV escaping behavior.
- Reusable report session hosting moved from ReportPlayer into `ETL-SQL.ReportHosting`; ReportPlayer and ReportPortal now consume the same `DashboardService`/`DashboardServiceFactory` without a host-to-host project reference.

Phase 5 is functionally complete for the first-pass boundary. Hosts now delegate shared report session, rendering, snapshot, runtime, and interaction semantics to lower layers.

### Phase 6: Documentation and Handoff Alignment

After source moves land, align architecture, strategy, and agent-facing docs so follow-up work does not reintroduce old ownership assumptions.

Current progress:

- Reporting and Portal architecture docs now describe `ETL-SQL.Reporting`, `ETL-SQL.ReportRuntime`, and `ETL-SQL.ReportHosting` as the shared boundaries.
- Portal strategy notes now point reusable report sessions at `ReportHosting`, report exporters/snapshot helpers at `Reporting`, and browser assets at `ReportRuntime`.

## Move Checklist

Before moving files or projects:

- Name the ownership boundary being moved.
- Prefer pure moves before behavior changes.
- Move one boundary at a time.
- Preserve public CLI names, package names, endpoint contracts, and VS Code package identity unless a release plan says otherwise.
- Add or update smoke coverage for the affected host or service.
- Update `AGENTS.md`, architecture/strategy docs, and any sync scripts if canonical paths change.
- Run `node .\scripts\sync-assets.js -Check` whenever report runtime assets or their canonical path are touched.

## Dependency Direction

Target dependency flow should be downward and boring:

```text
Hosts
  -> Reporting / Analysis / Engine services
      -> Engine / Connectors / Core contracts
          -> Core
```

Avoid host-to-host semantic dependencies. If two hosts need the same behavior, move it into Reporting, Analysis, Engine, Connectors, Core, or ReportRuntime instead of copying it.
