# Report Runtime Asset Standards

This document establishes the official development rules and synchronization protocols for shared frontend browser assets (JavaScript, CSS, themes, and UI modules) used across **ETL-SQL** visual hosts.

---

## 1. Single Source of Truth

To prevent code drift and duplication, all browser-based report player and catalog components have exactly one canonical home:

```text
src/ETL-SQL.ReportRuntime/Resources/Shared/
```

- **Rule**: Edit authored JavaScript, styles, and vendor assets here. Shared runtime and
  designer modules are authored under `Resources/TypeScript/`; their corresponding JavaScript here is
  generated. Follow the generated banner to its source.
- **Strictly Prohibited**: Never edit generated files directly in host directories. Preparation rejects
  locally changed output bytes; move the intended change into its canonical owner and regenerate.

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
check mode fails on stale compilation without writing files. Prepare assets before invoking a .NET
build or publish, including builds started from an IDE. `Directory.Build.targets` verifies the
prepared SHA-256 manifest without invoking Node. Missing assets, changed sources, and newly added
sources fail with the preparation command before the product can ship an incomplete asset set.

The supported preparation toolchain is Node.js 22.x, npm 10.x, and the exact TypeScript version in
`scripts/typecheck/package.json` and its lockfile (currently 6.0.3). Restore enforces the Node/npm
engines. Sync, standalone compilation, and the watcher verify Node and the restored compiler's
version, lock metadata, and loaded implementation before writing assets. A missing, mismatched, or
broken compiler fails with `npm ci --prefix scripts/typecheck`; no global compiler or download fallback
is used during preparation. Restore needs the package registry or a populated npm cache; generation
uses only restored dependencies and retained assets.

Freshness covers the retained browser inputs, Portal markup/styles/images, generator and contract
code, compiler configuration/locks, and host asset-selection files. Keep generator helpers under
`scripts/lib` so added or removed helpers enter both Node and .NET verification. A changed input
invalidates preparation even when emitted JavaScript is unchanged. Read-only checks preserve the
prepared files and report the missing, added, or stale input. The preparation manifest describes
current source assets; assembled packages need their separate build and runtime acceptance.

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

The Portal, Orchestrator service, and sandbox-worker Dockerfiles prepare assets in a Node.js 22
build stage. That stage restores the pinned compiler, includes the complete browser source and
generator inputs, and runs preparation followed by read-only verification. The .NET stage receives
those inputs and outputs together so its freshness checks use the same prepared tree. Final images
contain published .NET output and browser assets; Node is required only during image construction.
The repository `.dockerignore` excludes local compiler restores, build outputs, and installer packages
from the context while retaining authored browser sources and canonical vendor assets.
Portal publish copies the prepared `wwwroot` tree. It must not overwrite those verified host copies
with raw canonical files from `Resources/Shared/`.

`report-runtime.js` and the sibling `rt-*.js` files are ES modules generated from their matching
`Resources/TypeScript/` sources. Keep part imports
as single-line named imports with no aliases, and export declarations directly. The entry's
hoisted `renderManifest` participates in the rendering cycle; do not call across that cycle at
module initialization time.

`report-runtime.bundle.js` is generated, including the copy under `Resources/Shared`. Never edit
it by hand. Add each new part to `RUNTIME_PARTS` in `sync-assets.js`; an unlisted part fails sync.
The generator strips supported module syntax, checks that the result parses, and wraps it in a
classic-script IIFE for `OfflineSnapshotViewer`. Online hosts use `type="module"` on the entry.

### TypeScript compilation and ownership

The pipeline compiles shared runtime/designer modules and migrated Portal-owned modules and pages.
Remaining authored Portal JavaScript stays under `wwwroot/js/`, covered by the browser type and lint
gates. `TODO.md` tracks the remaining conversions and the source-control cleanup acceptance checks.

Preserve browser URLs and the existing offline concatenator. Use the pinned TypeScript toolchain
under `scripts/typecheck`; do not add another bundler or a Node invocation to the .NET build.

| Surface | Authored TypeScript | Generated JavaScript |
| :--- | :--- | :--- |
| Shared runtime and designer | `src/ETL-SQL.ReportRuntime/Resources/TypeScript/` | Matching relative path under `Resources/Shared/` |
| Portal-owned pages and modules | `src/ETL-SQL.Portal/BrowserSources/` | Matching relative path under `wwwroot/js/` |

Keep unmigrated JavaScript where it is. A migrated module has exactly one authored implementation:
its `.ts` file. Its old `.js` path becomes generated output, with a banner identifying the TypeScript
source. These outputs remain tracked until the clean-checkout and packaged-host checks in `TODO.md`
§1.1 pass. Do not untrack them individually ahead of the verified preparation workflow.
Portal's generated shared copies must never become Portal-owned TypeScript inputs.

Preparation emits into memory, validates the offline bundle and host import closures, then preflights
the complete output set before writing files. `artifacts/browser-assets.json` version 2 stores each
output's generator/source and SHA-256 hash. Existing outputs are adopted only when byte-identical to
fresh generation, or replaced/deleted when their previous validated hash still matches. An output
with local edits stops preparation; preserve the intended change in its canonical owner first.
Version-1 inventories support exact-byte bootstrap, not deletion. The earlier TypeScript format without
the generated implementation pragma is reproduced from the current owner for that comparison; source
or body differences still fail. Unverified obsolete inventory entries require review before bootstrap.
Unowned files remain retained inputs,
even when their contents include a generated-looking banner. Ownership paths must follow the compiler,
bundle, and host-routing rules; traversal, duplicate/case-only owners, and symlink/junction escapes fail.

Keep `.ts` sources outside served asset directories. Do not emit source maps or declarations in
generated browser output: no `.map`, `.d.ts`, or `sourceMappingURL` is shipped. Source-level debugging can be added
later with an explicit development-only map delivery decision. Preserve copyright/license comments,
emit LF, and retain comments needed by consumer checks. No minification or formatting cleanup is
part of a conversion.

#### Stateful browser controllers

The entry points under `TypeScript/designer/` compose controllers through typed context interfaces.
Their public exports and corresponding JavaScript URLs remain stable.

| Entry point | Responsibility owners |
| :--- | :--- |
| `designer.ts` | `designer-history`, `designer-persistence`, `designer-canvas-render`, `designer-inspector`, `designer-inspector-format`, `designer-visual-actions`, `designer-script-sync`, `designer-pointer` |
| `script-workbench.ts` | `workbench-sidebar`, `workbench-execution`, `workbench-overlays` |
| `studio.ts` | Navigation, document tabs/sessions, editor hosting, run sessions, visual/pipeline views, syntax bridge, workspace explorer, data/filter/engine panels, file commands, and governance controllers in `studio-*.ts` |
| `studio-authoring.ts` | Dialog, data, chart, report, preview, and pipeline controllers in `studio-authoring-*.ts` |

The matching `*-context.ts` files own shared TypeScript contracts and DOM helpers. Type-only
re-exports preserve existing imports. Controllers keep private state, such as undo history, lease
renewal timers, and parse sequence counters. The composition layer passes live getters and setters
for shared bindings so sibling callbacks see current documents, selections, and editor instances.
Controller construction must not read deferred bindings before shell initialization. Keep DOM
listeners and disposal paired in the composition layer when their lifetime spans controllers.

`test-browser-controllers.mjs` covers isolated history, stale parse invalidation, explicit run
selection, and cancellation. Consumer assertions name the module owning each behavior. Authoring
architecture checks discover all `studio-authoring*.js` modules, and the route guard checks every
Studio TypeScript module except the generated route owner.

#### Generated Studio routes and palette contracts

`designer/studio-routes.generated.ts` is generated from named endpoints in the registered Portal
and WorkstationEditor hosts. It is re-exported by `studio-contracts.ts`; browser callers must use
those tables. `Studio.Shared.*` endpoint names supply common routes, `Studio.Catalog.*` supplies
Portal routes, and `Studio.Workspace.*` supplies desktop routes. Generation verifies that the
desktop host serves every common path with the same HTTP methods. No source-text route parsing
or separate hand-maintained URL inventory is involved.

After changing endpoint names, paths, methods, or browser DTOs, regenerate from the repository root:

```powershell
node scripts\generate-browser-contracts.mjs
node scripts\sync-assets.js
node scripts\sync-assets.js -Check
```

The coordinator generates the API-schema validator in memory, then builds the C# generators into an
isolated `artifacts/browser-contracts-*/dotnet/` directory. Completed runs retain staged contracts and
TRX diagnostics, then remove the disposable build directory. Tests write declarations and routes only
into staging. Both registered hosts must initialize and their route checks must pass; the staged
TypeScript contracts must compile strictly before the three tracked files are replaced.
The internal `RequiresBrowserAssets=false` and `BootstrapBrowserContracts=true` flags resolve the
bootstrap dependency on new contracts. This isolated build omits generated JavaScript embedded by
Reporting; every project rejects publishing with the bootstrap flag.
Those isolated build outputs cannot satisfy an ordinary build. Run preparation and its check next;
ordinary builds and gates keep the asset verifier enabled. The source contracts remain tracked.
Without update mode, the generator tests reject stale declarations and routes. The API-only command's
`--check` stays read-only; invoking it for generation delegates to the complete coordinator.

Failures before promotion preserve the tracked set. Failed promotion restores saved bytes. An
interruption leaves `artifacts/browser-contracts-transaction.json` and the preparation lock; browser
preparation, compilation, and MSBuild reject that pending transaction. After the recorded process
has stopped, run `node scripts\generate-browser-contracts.mjs --recover`. Recovery restores the prior
set, or verifies an already completed set. It preserves concurrent edits and leaves the transaction
for review if the saved or generated bytes no longer match. Do not discard the transaction record
or backups to bypass that rejection. If recovery itself was interrupted, confirm its process has
stopped before removing `artifacts/browser-contracts-recovery.lock` and retrying recovery.

Pre-push and CI run these checks. The existing strict browser compiler checks route-key usage.
`PipelinePaletteCoverage` compares the palette's inferred IDs with the generated `PipelineTaskKind`
union, rejecting both invalid IDs and missing kinds. The consumer gate includes negative compiler
fixtures for these failures and checks that Studio does not bypass its route tables with raw URLs.
Behavioral endpoint, authorization, and browser journey tests remain separate requirements.

#### Mixed-source compilation

Use separate strict compiler programs for shared assets and Portal-owned modules. Each uses its
TypeScript and JavaScript directories as `rootDirs`, with relative `.js` import specifiers preserved
in emitted code. The compiler host must hide known generated JavaScript implementations during
source resolution so an existing output cannot shadow its TypeScript owner. Read ownership from
the discovered `.ts` paths, not hand-maintained parallel lists. Reject output collisions and any
attempt to emit outside the corresponding JavaScript root.

The installed TypeScript 6.0.3 resolver supports mixed siblings with `.js` specifiers.
`scripts/test-browser-compiler.mjs` exercises the compiler host with stale generated files present,
unmigrated JS imports, strict errors, exact import casing, idempotence, orphan rejection in check
mode, cleanup in normal mode, and unchanged outputs on failure. Portal imports of shared host URLs
resolve to the original TypeScript owner before host copies exist.

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
`node scripts/compile-browser.mjs --watch` for source editing; it runs full preparation, including
host sync and manifest updates, after source changes. Sandbox startup prepares once.
Reload alone cannot compile `.ts`.
CI and pre-push prepare assets before drift, type/lint, and consumer checks. Published .NET hosts
include prepared assets and require no Node installation. `artifacts/browser-assets.props` is a
generated, ignored build manifest; it records source and asset hashes. The .NET verifier performs
read-only checks, so parallel project builds do not race writes to shared outputs. Preparation and
verification take an exclusive `.browser-assets.lock`; concurrent writers fail clearly, and .NET
builds refuse to consume assets during preparation. If a preparation process is interrupted, confirm
it has stopped before removing its leftover lock.

Preparation verifies that authored inputs stayed unchanged through compilation, synchronization,
and final manifest creation. An edit during that work invalidates the preparation; rerun sync.
Watch mode schedules another preparation for edits made while a previous run finishes and releases
its lock when interrupted. Temporary consumer-test modules named `*.__test-*.mjs` are excluded from
compilation discovery, sync inventories, MSBuild discovery, and packages.

`artifacts/browser-assets.json` records every output's kind and canonical owner. It includes
TypeScript emits, the offline bundle, copied CSS/maps, and vendor host copies. Canonical CSS/maps,
vendor libraries, `.d.ts` contracts, the generated Studio route `.ts`, and the schema-generated Portal
API validator remain tracked inputs. Their existing generators own changes; do not hand-edit them.
The inventory is generated from discovered source paths and host import closures, so new migrations
enter the output set automatically. No broad JavaScript ignore is needed to identify outputs.

`scripts/browser-asset-ownership.mjs` reconciles that inventory with the Git index. Run without
arguments it prints the removal plan: each output, its owner, and the count of inputs that must stay
tracked. `--check` is the gate pre-push, pre-release, and CI run after preparation; the pre-commit hook runs it
for commits that touch browser assets. It reads staged content, so an
unstaged file cannot stand in for a missing one. It fails when:

- an owner or another freshness input is untracked or ignored;
- a staged output differs from its prepared bytes, which means someone edited generated code;
- a tracked page, one of the five asset-consuming project files, or a script or style tag written
  from a host's C# code names a file that is neither a tracked input nor an inventoried output;
- an output sits on the wrong side of the index for the declared layout.

The layout is declared by a managed block in `.gitignore`. Without the block, every output must be
tracked. With it, no output may be tracked and each must be ignored by its own anchored rule; no
pattern is used, so a rule cannot hide an authored neighbour. `--ignore-rules` prints the block and
`--write-ignore` replaces it, leaving other rules untouched. After the cutover, adding a module
means running `--write-ignore`; the gate fails until the block matches the inventory. Do not add the
block before the delivery paths in the
[delivery matrix](../../releases/v0.21.0-browser-asset-delivery-matrix.md) are accepted.

CodeQL excludes generated JavaScript so findings land on the TypeScript that produced it.
`scripts/test-source-analysis-coverage.mjs` reads the exclusions from `.github/workflows/codeql.yml` and
fails when one hides a TypeScript owner or leaves a generated script analysed. Exclude output folders,
never a source project. `scripts/` stays excluded: it is build tooling and does not ship.

A current preparation does not make existing binaries current. An assembly keeps the runtime bundle
it embedded, and an output folder keeps the files it copied, until the project is built again. Each
asset-consuming project therefore writes the manifest hash it built against to
`artifacts/browser-assets-built/<Project>.<Configuration>.<Runtime>.sha256`. Two things read it:

- `VerifyBuiltBrowserAssets` in `Directory.Build.targets` runs before every `Publish` and every
  `dotnet run` or `dotnet watch` launch, and fails if the record is missing or older. It does not
  depend on `NoBuild`, which `dotnet run --no-build` never sets; after a real build the record is
  current and the check passes.
- `scripts/check-built-browser-assets.mjs` runs in the `-NoBuild` branch of the test and smoke lanes,
  because a test project is not itself an asset consumer.

Both say to build without `--no-build`. A direct `dotnet test --no-build` is not guarded; use the lane
scripts for acceptance runs.

`scripts/verify-source-only-preparation.mjs` proves the real checkout can rebuild its outputs. It
checks each output against the inventory, deletes all of them and both manifests, prepares again, and
requires the same paths and bytes. If preparation fails, it restores what it removed. CI runs it after
preparation and uploads the inventory and hashes. Run it locally before proposing the cutover.

`scripts/test-asset-generation.mjs` runs preparation in an isolated source-only tree. It covers
missing directories and outputs, repeat-run stability, rename/delete cleanup, import closure,
failed compilation, and the MSBuild verifier's missing/stale/new-source failures.

#### Recovering missing or stale assets

| Situation | What to run |
| :--- | :--- |
| A build says assets are missing, stale, or not prepared | `node scripts/sync-assets.js` |
| The compiler restore is missing or does not match | `npm ci --prefix scripts/typecheck`, then prepare |
| A build says binaries came from an older preparation | Build again without `--no-build` |
| Preparation says an output was locally edited | Move the change into its owner, delete the edited output, prepare |
| Preparation was interrupted and left `.browser-assets.lock` | Confirm no preparation is running, delete the lock, prepare |
| You want to prove everything regenerates | `node scripts/verify-source-only-preparation.mjs` |

`dotnet clean` removes build output and the build record under `artifacts/browser-assets-built/`. It
does not touch prepared assets, which live in source folders, so no preparation is needed afterwards.

Once generated outputs are ignored by Git, `git clean -X` deletes them together with the manifests and
the restored compiler. Preview first with `git clean -ndX`. Afterwards run the restore and preparation
commands above; nothing authored is ignored, so nothing authored is lost. To remove only generated
outputs, use the proof script instead of `git clean`: it deletes exactly the inventoried files and
puts them back.

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

`src/ETL-SQL.Portal/BrowserSources/native-charts.ts` owns the compiled
`wwwroot/js/native-charts.js` classic script. It is a Portal-owned internal-UI adapter driving the orchestrator page's
sparkline and dependency-graph views. Keeping it separate is deliberate: see
[Portal UI](../portal-ui.md#native-chartsjs-ownership-boundary).

The boundary, restated so nobody has to infer it:

- It is **not** in `Resources/Shared/` or the Report-SQL capability matrix. Asset preparation compiles
  its Portal output; it does not copy this adapter to report hosts.
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
