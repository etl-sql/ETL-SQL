# Report Runtime Asset Standards

This document establishes the official development rules and synchronization protocols for shared frontend browser assets (JavaScript, CSS, themes, and UI modules) used across **ETL-SQL** visual hosts.

---

## 1. Single Source of Truth

To prevent code drift and duplication, all browser-based report player and catalog components have exactly one canonical home:

```text
src/ETL-SQL.ReportRuntime/Resources/Shared/
```

- **Rule**: Edit authored JavaScript, styles, and vendor assets here. Migrated modules are authored
  under `Resources/TypeScript/`; their corresponding JavaScript here is generated. Currently
  `rt-util.ts`, `designer/designer-util.ts`, `designer/editor-toolbar.ts`, and
  `designer/studio-git-diff.ts` are migrated.
  Follow the generated banner to its source.
- **Strictly Prohibited**: Never edit files directly inside the generated target directories of host applications. Any direct edits in host directories will be flagged as drift and overwritten by the asset synchronizer.

---

## 2. Generated Host Copies

The canonical shared assets are compiled and synchronized into these specific host directories:

- **Report Player**: `src/ETL-SQL.ReportPlayer/wwwroot/`
- **Workstation Editor**: `src/ETL-SQL.WorkstationEditor/wwwroot/`
- **Portal**: `src/ETL-SQL.Portal/wwwroot/js/` and `src/ETL-SQL.Portal/wwwroot/css/`
- **VS Code Extension**: `src/etl-sql-vscode/media/`

---

## 3. The Synchronization Workflow

After modifying files inside the canonical `Shared` directory, you must run the asset synchronizer to update the host applications:

For migrated modules, edit `Resources/TypeScript/` instead. Install the pinned compiler once with
`npm ci --prefix scripts/typecheck`. Sync compiles TypeScript before bundling and copying assets;
check mode fails on stale compilation without writing files. .NET builds consume checked-in assets.

1. **Synchronize Assets**:
   Run the sync script from the repository root:
   ```powershell
   node .\scripts\sync-assets.js
   ```
2. **Verify Sync State**:
   Run the sync script in verification mode to ensure no drift remains:
   ```powershell
   node .\scripts\sync-assets.js -Check
   ```
   *Note: CI/CD build pipelines execute the `-Check` step to block merges with unsynced assets.*

---

### Runtime module and offline bundle ownership

`report-runtime.js` and the sibling `rt-*.js` files are ES modules; `rt-util.js` is generated from
`Resources/TypeScript/rt-util.ts`, while the other parts remain authored JavaScript. Keep part imports
as single-line named imports with no aliases, and export declarations directly. The entry's
hoisted `renderManifest` participates in the rendering cycle; do not call across that cycle at
module initialization time.

`report-runtime.bundle.js` is generated, including the copy under `Resources/Shared`. Never edit
it by hand. Add each new part to `RUNTIME_PARTS` in `sync-assets.js`; an unlisted part fails sync.
The generator strips supported module syntax, checks that the result parses, and wraps it in a
classic-script IIFE for `OfflineSnapshotViewer`. Online hosts use `type="module"` on the entry.

### Planned TypeScript compilation and ownership

This is the implementation design for TODO §5, recorded on 2026-09-08. The shared `rt-util` pilot
and designer utility now use this pipeline. Portal conversion and broader migration remain pending. The source roots,
compiler, drift checks, sync integration, and sandbox startup compilation are implemented; the
remaining acceptance steps apply to each subsequent conversion.

Preserve browser URLs and the existing offline concatenator. Use the pinned TypeScript toolchain
under `scripts/typecheck`; do not add another bundler or a Node invocation to the .NET build.

| Surface | Authored TypeScript | Generated JavaScript |
| :--- | :--- | :--- |
| Shared runtime and designer | `src/ETL-SQL.ReportRuntime/Resources/TypeScript/` | Matching relative path under `Resources/Shared/` |
| Portal-owned pages and modules | `src/ETL-SQL.Portal/BrowserSources/` | Matching relative path under `wwwroot/js/` |

Keep unmigrated JavaScript where it is. A migrated module has exactly one authored implementation:
its `.ts` file. Its old `.js` path becomes checked-in generated output, with a banner identifying
the TypeScript source. Shared host copies and `report-runtime.bundle.js` remain checked-in outputs.
Portal's generated shared copies must never become Portal-owned TypeScript inputs.

Keep `.ts` sources outside served asset directories. Do not emit source maps or declarations in
the pilot: no `.map`, `.d.ts`, or `sourceMappingURL` is shipped. Source-level debugging can be added
later with an explicit development-only map delivery decision. Preserve copyright/license comments,
emit LF, and retain comments needed by consumer checks. No minification or formatting cleanup is
part of a conversion.

#### Mixed-source compilation

Use separate strict compiler programs for shared assets and Portal-owned modules. Each uses its
TypeScript and JavaScript directories as `rootDirs`, with relative `.js` import specifiers preserved
in emitted code. The compiler host must hide known generated JavaScript implementations during
source resolution so an existing output cannot shadow its TypeScript owner. Read ownership from
the discovered `.ts` paths, not hand-maintained parallel lists. Reject output collisions and any
attempt to emit outside the corresponding JavaScript root.

The installed TypeScript 6.0.3 resolver supports mixed siblings with `.js` specifiers.
`scripts/test-browser-compiler.mjs` exercises the compiler host with stale generated files present,
unmigrated JS imports, strict errors, idempotence, orphan rejection, and unchanged outputs on failure.

Use `strict`, `noEmitOnError`, ES2022, ESNext modules, Bundler resolution, DOM libraries,
`verbatimModuleSyntax`, `erasableSyntaxOnly`, and `allowJs`. Keep `checkJs` off in the strict migration program and run
the existing JavaScript gate separately over remaining authored JS. Do not relax that gate or add
baseline entries. Resolve shared TypeScript dependencies from Portal through the same ownership
mapping; do not create a second compiled copy of shared modules.

Emit into memory or a temporary directory and select only outputs belonging to authored TypeScript
inputs. Never overwrite an unmigrated JS file emitted incidentally by `allowJs`. Check all diagnostics
before writing any output. Add strict unused-local/parameter checks and keep existing ESLint checks
over the emitted JS, including unsupported globals and duplicate keys. Evaluate a TypeScript-aware
lint parser separately if source-only rules become necessary; any new dependency follows the
repository license policy.

#### Compilation, sync, and verification order

The shared compiler is `scripts/compile-browser.mjs`. Normal mode writes
generated JS; `--check` computes expected output without repairing the checkout and fails on missing,
stale, or orphaned outputs. Track generated ownership so removing or renaming a source cannot leave
a silently served old implementation. Fail with the exact path and regeneration command.

`sync-assets.js` compiles first, builds the existing offline bundle second, and copies shared
assets to hosts last. Its check mode must validate compilation before comparing bundle and host
outputs. Stop before downstream writes on compiler errors. The constrained concatenator continues
to consume JavaScript: named imports, direct exports, and the existing cycle rules still apply.
Reject TypeScript constructs whose emitted helpers or module syntax violate those constraints;
do not silently strip unsupported output or introduce a second bundle path.

The UI sandbox continues serving the same JavaScript paths. Use
`node scripts/compile-browser.mjs --watch` for source editing; sandbox startup compiles once.
Reload alone cannot compile `.ts`.
CI and pre-push must run compilation drift checks before type/lint and consumer checks. Published
.NET hosts consume checked-in generated assets and require no Node installation. Update `AGENTS.md`
and these current-workflow instructions in the implementation commit, not before the pipeline exists.

#### Pilot acceptance

Start with `rt-util`: it is a stateless runtime leaf and exercises both online modules and offline
delivery without moving a stateful closure. Preserve its exports, coercions, HTML escaping, and URL
handling. Use typed boundaries without narrowing away input cases supported by the JavaScript.

Verify current-source diagnostics, unchanged output on a second compile, stale/missing/orphan output
rejection, mixed imports with generated files present, and no writes after a failed compilation.
Then run existing utility behavior tests, bundle rejection tests, asset drift, browser lint/type,
consumer checks, payload budget, sandbox, Portal preview, real VS Code preview/CSP, and an offline
snapshot from disk with networking blocked. Record results before expanding to another module.

## 4. UI Sandbox Prototyping

Before committing a user interface or charting change, you should prototype and verify the layout inside the dev-only **UI Sandbox**:

- **Location**: `tools/ui-sandbox/`
- **Command**: Run `pwsh -File tools\ui-sandbox\serve.ps1` to launch the local sandbox development server.
- **Workflow**:
  - The UI Sandbox imports served JavaScript/CSS files directly. For migrated TypeScript, run the
    compiler watcher and reload after successful compilation.
  - Develop your dashboard features in the sandbox using isolated mock datasets (`mockApi.js`) and story scenarios (`tools/ui-sandbox/stories/`).
  - This avoids the overhead of launching docker containers, databases, or web servers just to verify visual components.

---

## 5. Payload Budgets

Browser payload is gated, not observed. Raw and gzip bytes are compared against a blessed measurement
in `docs/benchmarks/report-payload-budget.json` on every run of the default test lane.

Gated figures:

| Figure | What it covers |
| :--- | :--- |
| `report-runtime.js` | Sum of raw and separately compressed gzip bytes of the entry and all `rt-*.js` modules |
| `report-runtime.css` | Raw and gzip bytes of the shared report runtime stylesheet |
| `shared-runtime-total` | Raw and gzip bytes of viewer assets, vendor bundles included; excludes authoring assets, map data, and the alternative offline bundle |
| `page-weight:<fixture>` | End-to-end page weight of the heaviest representative report: shared assets plus that report's delivered browser manifest |

Tolerance is 3% plus a 2,048-byte floor, applied per figure. Shrink never fails.

### Checking and re-blessing

```powershell
# Check the working tree against the blessed budget
pwsh -File scripts\Test-ReportPayloadBudget.ps1

# Re-bless from the working tree (reviewed baseline update)
pwsh -File scripts\Test-ReportPayloadBudget.ps1 -UpdateBudget
```

`-UpdateBudget` is the only way past the gate, and it is deliberately visible: it rewrites the
checked-in JSON so the new numbers land in the diff and get reviewed like any other change. Do not
run it to turn a red build green — say in the same commit why the payload grew.

The budget is a blessed measurement, not a hand-picked ceiling. It exists because `report-runtime.js`
grew from 217,299 bytes just after the ECharts retirement back past the size that retirement had
shrunk it to, and nothing failed.

### What page weight means

Page weight is the shared assets a browser downloads plus the report's own delivered manifest.
Shared assets are counted once because they cache across reports in a session; the manifest is per
report. Quoting shared-asset totals alone understates a page; quoting the manifest alone understates
it far more. The measured figures live in
[`reporting-phase2-baselines.md`](../../benchmarks/reporting-phase2-baselines.md), regenerated by
`scripts\Measure-ReportingBaselines.ps1`.

---

## 6. What Dominates the Browser Payload

Measured from `Resources/Shared/` at v0.19.0. Two totals matter, and they are not the same number:

| Scope | Raw | Gzip |
| :--- | ---: | ---: |
| Everything under `Resources/Shared/` | 1,713,640 B | 426,504 B |
| What a **report page** actually loads (designer assets excluded) | 979,829 B | 226,725 B |

The report page total breaks down as:

| Asset | Raw | Gzip |
| :--- | ---: | ---: |
| `tabulator.min.js` | 443,224 B | 101,482 B |
| `report-runtime.js` | 285,491 B | 62,181 B |
| `arrow.min.js` | 166,184 B | 45,897 B |
| `report-runtime.css` | 44,893 B | 9,782 B |
| `tabulator.min.css` | 28,481 B | 3,983 B |
| `feedback.js` | 11,556 B | 3,400 B |

**Tabulator and Arrow plus their CSS are 637,889 B raw / 151,362 B gzip — 65% of raw and 67% of gzip
of what a report page downloads.** The chart runtime (`report-runtime.js` + `report-runtime.css`) is
34% of raw and 32% of gzip. Neither was touched by the ECharts retirement or by the v0.19.0 delivery
work, and neither is in scope for either.

Two things follow, and any future footprint claim has to respect both:

- **Name the grid and the columnar reader.** A claim that the browser payload is dominated by the
  chart runtime is wrong by a factor of two. The table grid and the Arrow reader are the cost.
- **Do not quote the `Resources/Shared/` total as page weight.** It includes the designer bundle
  (`designer/codemirror/codemirror-bundle.min.js`, `designer/designer.js`, `designer/designer.css` —
  733,811 B raw combined), which a report viewer never loads. The shared folder total over-counts a
  report page by roughly 43%.

---

## 7. Portal Operational UI Assets

`src/ETL-SQL.Portal/wwwroot/js/native-charts.js` is **not** a shared report runtime asset and is not
governed by sections 1–3. It is a Portal-owned internal-UI adapter driving the orchestrator page's
Gantt, sparkline, and dependency-graph views. Keeping it separate is deliberate: see
[Portal UI](../portal-ui.md#native-chartsjs-ownership-boundary).

The boundary, restated so nobody has to infer it:

- It is **not** in `Resources/Shared/`, **not** synchronized by `scripts/sync-assets.js`, and **not**
  in the Report-SQL capability matrix.
- It is **not** a `PlotPlan` consumer and must not become one. Report visuals resolve through
  `PlotPlan` or through the focused layout modules; operational graphics do neither.
- It ships to Portal operators, not to report viewers, so it never appears in report page weight.

It is still governed — just by its own gates, asserted in `PortalOperationalChartAssetTests`:

| Gate | Rule |
| :--- | :--- |
| Ownership | Carries a banner naming its owner, its boundary, and that it is not a shared runtime asset |
| Dependency / license | Zero third-party dependencies: no import, require, or remote URL. Nothing to attribute, and nothing that can appear in a license audit unnoticed |
| Accessibility | Every rendered chart is a single `role="img"` element with an `aria-label` |
| Behaviour | Keeps the ECharts-compatible shim surface (`init`, `setOption`, `resize`, `dispose`, `on`, `dispatchAction`, `getInstanceByDom`) that `orchestrator.html` calls, and escapes every interpolated value |
| Footprint | Raw and gzip bytes capped. A Portal internal adapter that grows into a charting library has stopped being an adapter |

---

## References

- [Presentation Standards](presentation-standards.md)
- [Portal UI](../portal-ui.md)
- [Report SQL Strategy](../roadmaps/report-sql-strategy.md)
