# Source Boundary Migration Plan

ETL-SQL has become more consolidated, but source-tree cleanup should stay incremental. The current priority is to make ownership boundaries obvious enough that new work lands in the right place and host applications stay thin.

This plan is intentionally conservative: document the target boundaries first, keep behavior stable, then move one boundary at a time with focused tests.

## Target Ownership

### Core Language Contracts

`src/ETL-SQL.Core` owns language contracts shared by every host and runtime:

- AST records, parser/lexer contracts, language metadata, shared expression and report model contracts.
- Cross-cutting value objects and interfaces that do not execute scripts or depend on a specific host.
- Temporary location for canonical shared report runtime assets until a dedicated ReportRuntime source area exists.

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

The rename should happen after compatibility shims are planned. A package/project rename should not be combined with unrelated behavior changes.

### Report Runtime Assets

The canonical browser runtime currently lives in:

```text
src/ETL-SQL.Core/Resources/Shared/
```

Generated host copies live under ReportPlayer, ReportPortal, and the VS Code extension. Do not edit those copies directly; use `scripts/sync-assets.ps1`.

The target shape is a dedicated `ETL-SQL.ReportRuntime` source area for canonical JavaScript, CSS, themes, browser dependencies, runtime fixtures, and runtime debugging utilities. Until that exists, the contract in `Docs/Report_Runtime_Contract.md` is the source of truth.

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

1. Add the new runtime source area with the same canonical files.
2. Update `scripts/sync-assets.ps1` and `Docs/Report_Runtime_Contract.md`.
3. Run the runtime sync check.
4. Verify ReportPlayer, ReportPortal, and VS Code still consume generated copies.

### Phase 4: Reshape `ReportBuilder` into `Reporting`

Do this after report manifests and style/runtime behavior settle.

Recommended steps:

1. Introduce the new project or namespace boundary.
2. Move manifest, style, visual, page, container, dataset, chart, and action semantics.
3. Leave compatibility references or forwarding types while hosts migrate.
4. Update each host separately with smoke coverage.
5. Rename packages/projects only after references are clean.

### Phase 5: Thin Host Cleanup

After Analysis, ReportRuntime, and Reporting boundaries exist, remove any remaining duplicated semantics from hosts. Keep host changes focused on shell behavior: auth, routing, protocol, process lifetime, UX, and persistence.

## Move Checklist

Before moving files or projects:

- Name the ownership boundary being moved.
- Prefer pure moves before behavior changes.
- Move one boundary at a time.
- Preserve public CLI names, package names, endpoint contracts, and VS Code package identity unless a release plan says otherwise.
- Add or update smoke coverage for the affected host or service.
- Update `AGENTS.md`, architecture/strategy docs, and any sync scripts if canonical paths change.
- Run `scripts/sync-assets.ps1 -Check` whenever report runtime assets or their canonical path are touched.

## Dependency Direction

Target dependency flow should be downward and boring:

```text
Hosts
  -> Reporting / Analysis / Engine services
      -> Engine / Connectors / Core contracts
          -> Core
```

Avoid host-to-host semantic dependencies. If two hosts need the same behavior, move it into Reporting, Analysis, Engine, Connectors, Core, or ReportRuntime instead of copying it.
