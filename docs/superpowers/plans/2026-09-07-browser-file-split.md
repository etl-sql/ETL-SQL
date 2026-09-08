# Browser File Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split `designer/designer.js` (9,008 lines) and `report-runtime.js` (9,668 lines) into per-concern modules so every browser source fits in one reading, without changing any behavior.

**Architecture:** Both files become an entry module plus sibling part modules in the same directory. `designer.js` is already an ES module, so its split is a pure move. `report-runtime.js` is a classic-script IIFE that `OfflineSnapshotViewer.cs` inlines whole into single-file `.etlsnap` snapshots, so it becomes ES modules plus a checked-in, drift-gated `report-runtime.bundle.js` that `scripts/sync-assets.js` generates by concatenating the parts.

**Tech Stack:** Plain browser ES modules (no framework, no bundler dependency). Node 20+ for `scripts/*.mjs` and `scripts/sync-assets.js` (CommonJS). ESLint 9 flat config. TypeScript `checkJs` type gate. Vitest + jsdom for the VS Code extension suite. PowerShell 7 for `Test-PrePush.ps1`.

**Spec:** [`docs/superpowers/specs/2026-09-07-browser-file-split-design.md`](../specs/2026-09-07-browser-file-split-design.md)

## Implementation record — 2026-09-08

Tasks 11–19 and 21 are implemented. Checks and remaining gate results are recorded in
[the post-split evidence](../../releases/v0.20.0-browser-split-baseline.md).
The task lists below retain the original procedure.

Live-source corrections: there are 16 parts plus the entry, and several parts import the entry's
hoisted `renderManifest`. Native layout observers and drill-in-flight state also need accessors.
The Portal report iframe and sandbox fixtures require module loading too. The payload gate counts
all online modules and excludes the alternative offline bundle.

The VS Code extension-host webview test and file-based offline browser tests cover paths task 20
originally called manual-only. A Portal preview browser test covers dynamic injection.
Extraction preserved template literal contents; all 219 original declarations matched an AST
comparison after reversing accessors, with top-level effect ordering unchanged.

## Global Constraints

Every task's requirements implicitly include this section.

- **Behavior-preserving.** Moves only. No renames, no signature changes, no reformatting, no drive-by fixes. A reviewer must be able to confirm each hunk is a cut-and-paste.
- **Both gate baselines stay comment-only.** `browser-lint-baseline.txt` and `browser-typecheck-baseline.txt` carry zero live findings. Never run either gate's `--update` in this work. A finding means the move was wrong; fix the move.
- **Part module export style is constrained**, because the bundler transform is line-based and depends on it:
  - Named `export` on a declaration only: `export function`, `export async function`, `export const`, `export let`, `export class`.
  - No `export { ... }` lists, no `export default`, no `export ... from`, no dynamic `import()` of a sibling.
  - Every `import` statement is **one line**: `import { a, b } from './rt-x.js';`
  - These rules bind **part** modules. The two *entry* modules (`designer/designer.js`, `report-runtime.js`) may use `export { x } from './part.js'` re-exports; `report-runtime.js`'s own imports must still be single-line, because the bundler reads it too.
- **Line ranges in this plan are from the pre-split files and shift as earlier tasks remove code.** Always locate code by symbol name (`grep -n "function symbolName"`), using the range only as a guide.
- **Do not fix pre-existing failures.** Four of the consumer scripts touched in Task 21 are among the nine red `scripts/test-*.mjs` checks in TODO §3. Task 1 records their state; repair only what this work breaks.
- **Never use `--no-verify`.** The pre-commit hook runs `dotnet format --verify-no-changes` over the whole solution. If it fails on C# this work did not touch, stop and report it.

---

## File Structure

**Created — `src/ETL-SQL.ReportRuntime/Resources/Shared/designer/`**

| File | Responsibility |
| :--- | :--- |
| `designer-util.js` | `escapeHtml`, `_feedback` — used from every region of the old file |
| `dag.js` | DAG layout, render, lineage reach, compact/capsule variants |
| `rptsql-language.js` | CodeMirror rptsql language, keyword sets, highlight style |
| `editor-toolbar.js` | Toolbar icon set and button builder |
| `run-results.js` | Run trace normalization, results panel, CSV/XLSX export |
| `script-editor.js` | `createScriptEditor` |
| `script-workbench.js` | `createScriptEditorWorkbench` |
| `data-prep-recipes.js` | `DATA_PREP_RECIPES` |
| `html-preview.js` | HTML-visual preview sanitizer and budgets |
| `visual-format-inspector.js` | Formatting inspector HTML builders and numeric parsers |

**Created — `src/ETL-SQL.ReportRuntime/Resources/Shared/`**

`rt-util.js`, `rt-state.js`, `rt-theme.js`, `rt-transport.js`, `rt-data.js`, `rt-detail.js`, `rt-charts.js`, `rt-table.js`, `rt-matrix.js`, `rt-controls-date.js`, `rt-controls-input.js`, `rt-visual.js`, `rt-layout.js`, `rt-actions.js`, `rt-views.js`, `rt-chrome.js`, and the generated `report-runtime.bundle.js`. Responsibilities are the spec's bundle-order table.

**Modified**

| File | Change |
| :--- | :--- |
| `.gitattributes` | per-filename LF pins → path globs |
| `scripts/sync-assets.js` | bundle generation + drift check |
| `eslint.config.mjs` | `report-runtime.js` leaves `classicScripts`; bundle ignored |
| `tsconfig.json` | bundle excluded |
| `src/ETL-SQL.Reporting/ETL-SQL.Reporting.csproj` | embed the bundle |
| `src/etl-sql-vscode/src/reportPreviewPanel.ts` | `type="module"` + CSP `${webview.cspSource}` |
| `src/ETL-SQL.ReportPlayer/Program.cs` | 2 templates → `type="module"` |
| `src/ETL-SQL.Portal/wwwroot/designer-preview.html` | `rt.type = 'module'` |
| `src/ETL-SQL.WorkstationEditor/WorkstationEditorApp.cs` | `rt.type = 'module'` |
| `src/etl-sql-vscode/src/test/reportRuntime.test.ts` | read the bundle; add bundle self-check |
| `scripts/test-page-layout-options.mjs`, `test-designer-polish.mjs`, `test-portal-studio.mjs` | follow moved text |
| `TODO.md` | record the bundler decision; add the follow-up item |

---

## The Extraction Procedure

Tasks 3–10 and 13–18 are the same seven mechanical steps over different inputs. The procedure is defined once here, in full. Each task supplies **SOURCE**, **TARGET**, **SYMBOLS**, and **IMPORTS**, then runs these steps. This is not a cross-reference to another task — every value a task needs is stated in that task.

**Step A — Cut.** For each name in SYMBOLS, locate it in SOURCE with `grep -n`, and move its entire declaration (including the JSDoc or comment block immediately above it, up to the preceding blank line) into TARGET. Delete it from SOURCE. Do not reindent, rewrap, or reword anything.

**Step B — Export.** In TARGET, prefix each moved top-level declaration with `export `. If the symbol already read `export function foo`, it stays `export function foo` — do not double it.

**Step C — Header.** TARGET opens with the repository's standard file header, matching the style already in `designer/studio-state.js`:

```js
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * <TARGET filename> — split out of <SOURCE filename>, TODO.md §2.
 * <One sentence naming this module's single responsibility.>
 */
```

**Step D — Import into TARGET.** Add the IMPORTS lines given by the task, each on one line, immediately below the header.

**Step E — Import into SOURCE.** Add one single-line import to SOURCE naming every symbol SOURCE still uses:

```js
import { /* names still referenced in SOURCE */ } from './<TARGET>';
```

Find the list by running, for each name in SYMBOLS, `grep -c "\b<name>\b" <SOURCE>` **after** the cut. A count of 0 means SOURCE no longer needs it — leave it out. An unused import is a `no-unused-vars` finding and fails the lint gate.

**Step F — Re-export from the entry module.** If a moved symbol was `export`ed by SOURCE before the cut, and SOURCE is an entry module, add it to SOURCE's re-export block so external importers are unaffected:

```js
export { movedName } from './<TARGET>';
```

Check with `git show HEAD:<SOURCE path> | grep -n "export .*<name>"`. Skip for symbols that were module-private.

**Step G — Verify.** Run the gate set (defined next) and confirm green before committing.

## The Gate Set

Referenced by every task. Run from the repository root.

```bash
node scripts/sync-assets.js && node scripts/sync-assets.js --check
node scripts/lint-browser.mjs
node scripts/typecheck-browser.mjs
git diff --stat browser-lint-baseline.txt browser-typecheck-baseline.txt
```

Expected: the first three print no findings and exit 0; the fourth prints **nothing** — a changed baseline means a real finding was introduced or silently absorbed, which the Global Constraints forbid.

---

## Task 1: Record the starting state

Nothing here changes behavior. It exists so that a failure later in this work can be told apart from a failure that was already there — TODO §3 says four of these checks are already red, and without this record the split would inherit the blame.

**Files:**
- Create: `docs/releases/v0.20.0-browser-split-baseline.md`

- [ ] **Step 1: Capture the red/green state of every consumer check**

```bash
for s in test-designer-polish test-lineage-ui test-page-layout-options test-portal-studio test-result-grid-ui; do
  printf '%-28s ' "$s"
  node "scripts/$s.mjs" >/dev/null 2>&1 && echo PASS || echo FAIL
done
```

- [ ] **Step 2: Capture the gate state**

```bash
node scripts/sync-assets.js --check; echo "sync=$?"
node scripts/lint-browser.mjs;      echo "lint=$?"
node scripts/typecheck-browser.mjs; echo "type=$?"
```

- [ ] **Step 3: Capture the vitest state**

```bash
cd src/etl-sql-vscode && npx vitest run src/test/reportRuntime.test.ts 2>&1 | tail -20
```

- [ ] **Step 4: Record line counts, so the split's effect is measurable**

```bash
wc -l src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.js \
      src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js
```

- [ ] **Step 5: Write the results into the baseline document**

Write `docs/releases/v0.20.0-browser-split-baseline.md` containing: today's date, the commit SHA (`git rev-parse HEAD`), the five PASS/FAIL lines from Step 1, the three exit codes from Step 2, the vitest summary line from Step 3, and the two line counts from Step 4. Add one sentence per FAIL naming the assertion that failed, taken from the script's output. State explicitly that these failures pre-date this work.

- [ ] **Step 6: Commit**

```bash
git add docs/releases/v0.20.0-browser-split-baseline.md
git commit -m "docs(browser): record the pre-split state of the browser gates and checks"
```

---

## Task 2: Pin line endings by path glob

`.gitattributes` pins LF one bare filename per line. Adding ~27 part files that way is a trap: omit one and the drift gate passes for everyone except a fresh clone with `core.autocrlf=true`, where `sync-assets.js --check` fails with no clue why. Doing this first also avoids a whole-tree line-ending churn landing on top of the move.

**Files:**
- Modify: `.gitattributes:6-30`

**Interfaces:**
- Produces: LF pinning that covers any file added under the canonical directory or the five host copy roots, so later tasks add files without touching `.gitattributes`.

- [ ] **Step 1: Read the current block**

```bash
sed -n '1,35p' .gitattributes
```

- [ ] **Step 2: Replace the enumerated JS/CSS entries with globs**

Delete the bare-filename lines for `report-runtime.js`, `designer.js`, `designer.css`, `studio.js`, `connection-wizard.js`, `arrow.min.js`, `feedback.js`, `studio-authoring.js`, `studio-authoring-ui.js`, `studio-contracts.js`, `studio-query-workbench.js`, `studio-sql-mutations.js`, `studio-pipeline-canvas.js`, `studio-data.js`, `studio-git-diff.js`, `studio-host.js` and any remaining siblings in that block. Keep `*.min.js` and any binary rules. In their place:

```gitattributes
# Shared browser/runtime assets are copied verbatim from
# src/ETL-SQL.ReportRuntime/Resources/Shared/ to host copies (VS Code, Player, Portal, Workstation).
# Pin them to LF by path rather than by filename: these directories gain files routinely, and a
# file missed by a per-name pin fails `sync-assets.js --check` only on a fresh clone with
# core.autocrlf=true, where the cause is invisible.
src/ETL-SQL.ReportRuntime/Resources/Shared/**   text eol=lf
src/ETL-SQL.Portal/wwwroot/**/*.js              text eol=lf
src/ETL-SQL.Portal/wwwroot/**/*.css             text eol=lf
src/ETL-SQL.Portal/wwwroot/**/*.html            text eol=lf
src/ETL-SQL.ReportPlayer/wwwroot/**             text eol=lf
src/ETL-SQL.WorkstationEditor/wwwroot/**        text eol=lf
src/etl-sql-vscode/media/**                     text eol=lf
```

- [ ] **Step 3: Verify the pins actually apply — read git, not the file**

```bash
git ls-files --eol src/ETL-SQL.ReportRuntime/Resources/Shared | grep -v 'w/lf' || echo "ALL LF"
```

Expected: `ALL LF`. Any line printed is a file whose working-tree eol is not LF; investigate before continuing.

- [ ] **Step 4: Confirm no file content changed**

```bash
git diff --stat -- . ':!.gitattributes'
```

Expected: empty. If files appear, `git add --renormalize .` was run or a checkout rewrote them — stop and report, do not commit a whole-tree churn inside this task.

- [ ] **Step 5: Confirm the drift gate still passes**

```bash
node scripts/sync-assets.js --check
```

Expected: exit 0, "Check Complete."

- [ ] **Step 6: Commit**

```bash
git add .gitattributes
git commit -m "build(browser): pin shared asset line endings by path, not by filename"
```

---

## Task 3: Extract `designer-util.js`

`escapeHtml` (30 references) and `_feedback` (31) are used from every region of `designer.js`. They must move first, because every later designer task imports them.

**Files:**
- Create: `src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer-util.js`
- Modify: `src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js`

**Interfaces:**
- Produces: `export function escapeHtml(value)` → `string`; `export const _feedback` (the `globalThis.ETLSQLFeedback` handle, possibly `undefined`). Tasks 4–10 import both from `'./designer-util.js'`.

Run the **Extraction Procedure** with:

- **SOURCE:** `designer/designer.js`
- **TARGET:** `designer/designer-util.js`
- **SYMBOLS:** `escapeHtml` (~line 871), `_feedback` (~line 28)
- **IMPORTS:** none

- [ ] **Step 1: Run Extraction Procedure steps A–E**

TARGET's responsibility sentence for Step C: `Small helpers used from every part of the designer.`

Note for Step B: `_feedback` is a `const` initialised from `globalThis.ETLSQLFeedback`. Move the initialiser verbatim; do not convert it to a function or add a fallback.

- [ ] **Step 2: Skip Step F**

Neither symbol is exported by `designer.js` today. Confirm before skipping:

```bash
git show HEAD:src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js | grep -nE "^export (function escapeHtml|const _feedback)"
```

Expected: no output.

- [ ] **Step 3: Verify no reference was left behind**

```bash
grep -n "^\(export \)\?function escapeHtml\|^const _feedback" src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js
```

Expected: no output — the declarations are gone from SOURCE.

- [ ] **Step 4: Run the Gate Set**

Expected: all green, baselines unchanged. A `no-undef` for `escapeHtml` or `_feedback` means Step E's import list is incomplete.

- [ ] **Step 5: Commit**

```bash
git add src/ETL-SQL.ReportRuntime/Resources/Shared/designer/ src/ETL-SQL.Portal/wwwroot src/ETL-SQL.ReportPlayer/wwwroot src/ETL-SQL.WorkstationEditor/wwwroot src/etl-sql-vscode/media
git commit -m "refactor(designer): extract escapeHtml and the feedback handle to designer-util.js"
```

---

## Task 4: Extract `dag.js`

**Files:**
- Create: `src/ETL-SQL.ReportRuntime/Resources/Shared/designer/dag.js`
- Modify: `src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js`

**Interfaces:**
- Consumes: `escapeHtml`, `_feedback` from `'./designer-util.js'` (Task 3).
- Produces: `export function renderDag(container, { nodes, edges }, options = {})`; `export function _edgeStyle(label)`; `export function updateDagLines(container)`; `export function flattenDagColumns(nodes, columns = [])`; `export function renderCompactDag(nodes)`; `export function renderDagCapsule(node, col, row)`. `renderDag` and `_edgeStyle` are re-exported by `designer.js`.

Run the **Extraction Procedure** with:

- **SOURCE:** `designer/designer.js`
- **TARGET:** `designer/dag.js`
- **SYMBOLS:** `_TYPE_COLOR` (~30), `_nodeColor` (~50), `_computeLayout` (~58), `_lineageReach` (~124), `_edgeStyle` (~168), `renderDag` (~177), `flattenDagColumns` (~1873), `renderCompactDag` (~1887), `renderDagCapsule` (~1912), `updateDagLines` (~1936)
- **IMPORTS:** `import { escapeHtml, _feedback } from './designer-util.js';` — include only the names TARGET actually references; check with `grep -c` after the cut and drop any with count 0.

- [ ] **Step 1: Run Extraction Procedure steps A–E**

TARGET's responsibility sentence for Step C: `Lineage DAG layout and rendering, including the compact and capsule variants.`

- [ ] **Step 2: Run Extraction Procedure step F**

`designer.js` exports `_edgeStyle` and `renderDag` today. Add to its re-export block:

```js
export { _edgeStyle, renderDag } from './dag.js';
```

Confirm the list is complete:

```bash
git show HEAD:src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js | grep -nE "^export .*(_edgeStyle|renderDag|updateDagLines|flattenDagColumns|renderCompactDag|renderDagCapsule)"
```

Add a re-export for every name this prints.

- [ ] **Step 3: Verify the module importers still resolve**

`scripts/test-lineage-ui.mjs` and `scripts/test-result-grid-ui.mjs` import `designer.js` as a module and exercise the DAG. Run both:

```bash
node scripts/test-lineage-ui.mjs; echo "lineage=$?"
node scripts/test-result-grid-ui.mjs; echo "result-grid=$?"
```

Expected: the same exit codes Task 1 recorded. A change from PASS to FAIL is this task's regression; a FAIL that Task 1 already recorded is not.

- [ ] **Step 4: Run the Gate Set**

- [ ] **Step 5: Commit**

```bash
git add src/ETL-SQL.ReportRuntime/Resources/Shared/designer/ src/ETL-SQL.Portal/wwwroot src/ETL-SQL.ReportPlayer/wwwroot src/ETL-SQL.WorkstationEditor/wwwroot src/etl-sql-vscode/media
git commit -m "refactor(designer): extract the lineage DAG renderer to dag.js"
```

---

## Task 5: Extract `rptsql-language.js`

**Files:**
- Create: `src/ETL-SQL.ReportRuntime/Resources/Shared/designer/rptsql-language.js`
- Modify: `src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `export function _loadCm()` → `Promise`; `export function _getRptsqlLang(cm)`; `export function _getRptsqlHighlightStyle(cm)`; `export const _KW`, `_FUNC`, `_TYPE` (each a `Set<string>`). Task 7 (`script-editor.js`) and Task 8 (`script-workbench.js`) import `_loadCm`, `_getRptsqlLang`, `_getRptsqlHighlightStyle`.

Run the **Extraction Procedure** with:

- **SOURCE:** `designer/designer.js`
- **TARGET:** `designer/rptsql-language.js`
- **SYMBOLS:** `_KW` (~825), `_FUNC` (~841), `_TYPE` (~855), `_cmPromise` (~865), `_loadCm` (~866), `_rptsqlLang` (~880), `_getRptsqlLang` (~881), `_rptsqlHighlight` (~941), `_getRptsqlHighlightStyle` (~942)
- **IMPORTS:** none

`_cmPromise`, `_rptsqlLang` and `_rptsqlHighlight` are module-level `let` memoization caches. They move with their accessors and stay module-private — do **not** export them. Their single-load semantics are preserved because each is read and written only by the accessor moving alongside it.

- [ ] **Step 1: Run Extraction Procedure steps A–E**

TARGET's responsibility sentence for Step C: `The rptsql CodeMirror language: keyword sets, lazy bundle load, and highlight style.`

- [ ] **Step 2: Run Extraction Procedure step F**

```bash
git show HEAD:src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js | grep -nE "^export .*(_KW|_FUNC|_TYPE|_loadCm|_getRptsql)"
```

Add a re-export for every name this prints. If it prints nothing, skip Step F.

- [ ] **Step 3: Verify the memoization caches did not get exported**

```bash
grep -n "^export \(let\|const\) \(_cmPromise\|_rptsqlLang\|_rptsqlHighlight\)" src/ETL-SQL.ReportRuntime/Resources/Shared/designer/rptsql-language.js
```

Expected: no output.

- [ ] **Step 4: Run the Gate Set**

- [ ] **Step 5: Commit**

```bash
git add src/ETL-SQL.ReportRuntime/Resources/Shared/designer/ src/ETL-SQL.Portal/wwwroot src/ETL-SQL.ReportPlayer/wwwroot src/ETL-SQL.WorkstationEditor/wwwroot src/etl-sql-vscode/media
git commit -m "refactor(designer): extract the rptsql CodeMirror language to rptsql-language.js"
```

---

## Task 6: Extract `editor-toolbar.js`

Small and self-contained; `toolbarButton` has 25 call sites, so doing it before the two large workbench extractions keeps those diffs clean.

**Files:**
- Create: `src/ETL-SQL.ReportRuntime/Resources/Shared/designer/editor-toolbar.js`
- Modify: `src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `export function toolbarIcon(name)` → `string` (SVG markup); `export function toolbarButton({ attr, icon, title, label, primary, key })` → `string`; `export const _TOOLBAR_ICONS` (object keyed by icon name).

Run the **Extraction Procedure** with:

- **SOURCE:** `designer/designer.js`
- **TARGET:** `designer/editor-toolbar.js`
- **SYMBOLS:** `_TOOLBAR_ICONS` (~2493), `toolbarIcon` (~2516), `toolbarButton` (~2532)
- **IMPORTS:** none expected; if the cut code references `escapeHtml`, add `import { escapeHtml } from './designer-util.js';`

- [ ] **Step 1: Run Extraction Procedure steps A–E**

TARGET's responsibility sentence for Step C: `The editor toolbar's icon set and button markup builder.`

- [ ] **Step 2: Run Extraction Procedure step F**

```bash
git show HEAD:src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js | grep -nE "^export .*(toolbarIcon|toolbarButton|_TOOLBAR_ICONS)"
```

- [ ] **Step 3: Run the Gate Set**

- [ ] **Step 4: Commit**

```bash
git add src/ETL-SQL.ReportRuntime/Resources/Shared/designer/ src/ETL-SQL.Portal/wwwroot src/ETL-SQL.ReportPlayer/wwwroot src/ETL-SQL.WorkstationEditor/wwwroot src/etl-sql-vscode/media
git commit -m "refactor(designer): extract the editor toolbar builder to editor-toolbar.js"
```

---

## Task 7: Extract `run-results.js`

**Files:**
- Create: `src/ETL-SQL.ReportRuntime/Resources/Shared/designer/run-results.js`
- Modify: `src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js`

**Interfaces:**
- Consumes: `escapeHtml`, `_feedback` from `'./designer-util.js'`; `toolbarIcon`, `toolbarButton` from `'./editor-toolbar.js'`.
- Produces: `export function redactSecrets(text)`; `export function normalizeRunTrace(result, script)`; `export function createScriptResultsPanel(container, { onNavigate = null } = {})`; `export const MAX_RENDERED_ROWS = 5000`; `export function resultRenderWindow(filteredRows, totalRows, isFiltered, cap = MAX_RENDERED_ROWS)`; `export function filterRows(rows, columns, filter)`; `export function toCsv(columns, rows)`; `export function formatResultCell(value)`; `export function buildDataPreviewPayload(source, script, documentUri)`; `export function editLeaseRetryDelay(expiresAt, now = Date.now())`; `export function toXlsxXml(columns, rows)`. All except `toXlsxXml` are re-exported by `designer.js`.

Run the **Extraction Procedure** with:

- **SOURCE:** `designer/designer.js`
- **TARGET:** `designer/run-results.js`
- **SYMBOLS:** `redactSecrets` (~1779), `normalizeRunTrace` (~1788), `toXlsxXml` (~1848), `createScriptResultsPanel` (~1980), `MAX_RENDERED_ROWS` (~2422), `resultRenderWindow` (~2428), `filterRows` (~2450), `toCsv` (~2456), `formatResultCell` (~2467), `buildDataPreviewPayload` (~2473), `editLeaseRetryDelay` (~2485)
- **IMPORTS:** `import { escapeHtml, _feedback } from './designer-util.js';` and `import { toolbarIcon, toolbarButton } from './editor-toolbar.js';` — keep only names TARGET references.

- [ ] **Step 1: Run Extraction Procedure steps A–E**

TARGET's responsibility sentence for Step C: `Script run results: trace normalization, the results panel, and CSV/XLSX export.`

- [ ] **Step 2: Run Extraction Procedure step F**

```bash
git show HEAD:src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js | grep -nE "^export .*(redactSecrets|normalizeRunTrace|createScriptResultsPanel|MAX_RENDERED_ROWS|resultRenderWindow|filterRows|toCsv|formatResultCell|buildDataPreviewPayload|editLeaseRetryDelay)"
```

Add a re-export for every name this prints. These are the designer's most-imported utilities; a missing re-export breaks an external caller with no lint finding, because the caller lives outside the linted roots.

- [ ] **Step 3: Verify external importers still resolve**

```bash
node scripts/test-result-grid-ui.mjs; echo "result-grid=$?"
node scripts/test-designer-polish.mjs; echo "designer-polish=$?"
```

Expected: the exit codes Task 1 recorded. `test-designer-polish.mjs` greps `designer.js` as text and may now fail because the text moved — if so, that is Task 21's fix. Note it and continue; do not edit the script here.

- [ ] **Step 4: Run the Gate Set**

- [ ] **Step 5: Commit**

```bash
git add src/ETL-SQL.ReportRuntime/Resources/Shared/designer/ src/ETL-SQL.Portal/wwwroot src/ETL-SQL.ReportPlayer/wwwroot src/ETL-SQL.WorkstationEditor/wwwroot src/etl-sql-vscode/media
git commit -m "refactor(designer): extract run results and export helpers to run-results.js"
```

---

## Task 8: Extract `script-editor.js`

**Files:**
- Create: `src/ETL-SQL.ReportRuntime/Resources/Shared/designer/script-editor.js`
- Modify: `src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js`

**Interfaces:**
- Consumes: `escapeHtml`, `_feedback` from `'./designer-util.js'`; `_loadCm`, `_getRptsqlLang`, `_getRptsqlHighlightStyle` from `'./rptsql-language.js'`.
- Produces: `export async function createScriptEditor(container, opts = {})`. Re-exported by `designer.js`.

Run the **Extraction Procedure** with:

- **SOURCE:** `designer/designer.js`
- **TARGET:** `designer/script-editor.js`
- **SYMBOLS:** `createScriptEditor` (~1024–1778)
- **IMPORTS:** `import { escapeHtml, _feedback } from './designer-util.js';` and `import { _loadCm, _getRptsqlLang, _getRptsqlHighlightStyle } from './rptsql-language.js';` — keep only names TARGET references.

- [ ] **Step 1: Run Extraction Procedure steps A–E**

TARGET's responsibility sentence for Step C: `The CodeMirror-backed rptsql script editor.`

- [ ] **Step 2: Run Extraction Procedure step F**

`createScriptEditor` is exported today. Add:

```js
export { createScriptEditor } from './script-editor.js';
```

- [ ] **Step 3: Run the Gate Set**

- [ ] **Step 4: Commit**

```bash
git add src/ETL-SQL.ReportRuntime/Resources/Shared/designer/ src/ETL-SQL.Portal/wwwroot src/ETL-SQL.ReportPlayer/wwwroot src/ETL-SQL.WorkstationEditor/wwwroot src/etl-sql-vscode/media
git commit -m "refactor(designer): extract createScriptEditor to script-editor.js"
```

---

## Task 9: Extract `script-workbench.js` and `data-prep-recipes.js`

Two targets in one task: `DATA_PREP_RECIPES` is 75 lines of data consumed by the workbench, and splitting them across two commits would leave one commit with an import to a file that does not exist yet.

**Files:**
- Create: `src/ETL-SQL.ReportRuntime/Resources/Shared/designer/data-prep-recipes.js`
- Create: `src/ETL-SQL.ReportRuntime/Resources/Shared/designer/script-workbench.js`
- Modify: `src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js`

**Interfaces:**
- Consumes: `escapeHtml`, `_feedback` from `'./designer-util.js'`; `toolbarIcon`, `toolbarButton` from `'./editor-toolbar.js'`; `createScriptEditor` from `'./script-editor.js'`; `createScriptResultsPanel`, `normalizeRunTrace`, `redactSecrets`, `buildDataPreviewPayload`, `editLeaseRetryDelay` from `'./run-results.js'`; `renderDag`, `updateDagLines` from `'./dag.js'`.
- Produces: `export const DATA_PREP_RECIPES` (array); `export async function createScriptEditorWorkbench(container, opts = {})`. Both re-exported by `designer.js`.

- [ ] **Step 1: Extract `data-prep-recipes.js`**

Run the **Extraction Procedure** steps A–E with SOURCE `designer/designer.js`, TARGET `designer/data-prep-recipes.js`, SYMBOLS `DATA_PREP_RECIPES` (~3968–4042), IMPORTS none. Responsibility sentence: `The catalogue of data-preparation recipes offered by the script workbench.`

- [ ] **Step 2: Extract `script-workbench.js`**

Run the **Extraction Procedure** steps A–E with SOURCE `designer/designer.js`, TARGET `designer/script-workbench.js`, SYMBOLS `createScriptEditorWorkbench` (~2572–3967), and IMPORTS the full Consumes list above plus `import { DATA_PREP_RECIPES } from './data-prep-recipes.js';`. Responsibility sentence: `The script editor workbench: editor, results panel, run controls and data-prep recipes in one surface.`

Keep only names TARGET references. This is the largest single move in the designer half, so run the count check for every candidate:

```bash
for n in escapeHtml _feedback toolbarIcon toolbarButton createScriptEditor createScriptResultsPanel normalizeRunTrace redactSecrets buildDataPreviewPayload editLeaseRetryDelay renderDag updateDagLines DATA_PREP_RECIPES; do
  printf '%-28s %s\n' "$n" "$(grep -c "\b$n\b" src/ETL-SQL.ReportRuntime/Resources/Shared/designer/script-workbench.js)"
done
```

Import exactly the names with a non-zero count.

- [ ] **Step 3: Run Extraction Procedure step F for both**

```bash
git show HEAD:src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js | grep -nE "^export .*(createScriptEditorWorkbench|DATA_PREP_RECIPES)"
```

Both are exported today. Add:

```js
export { createScriptEditorWorkbench } from './script-workbench.js';
export { DATA_PREP_RECIPES } from './data-prep-recipes.js';
```

- [ ] **Step 4: Verify `EditorShell.cs`'s entry point still resolves**

`src/ETL-SQL.WorkstationEditor/EditorShell.cs:42` does `import { createScriptEditorWorkbench } from '/designer/designer.js?v=...'`. The re-export in Step 3 is what keeps that working. Confirm the symbol is reachable from the entry module:

```bash
grep -n "createScriptEditorWorkbench" src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js
```

Expected: exactly one line, the re-export.

- [ ] **Step 5: Run the Gate Set**

- [ ] **Step 6: Commit**

```bash
git add src/ETL-SQL.ReportRuntime/Resources/Shared/designer/ src/ETL-SQL.Portal/wwwroot src/ETL-SQL.ReportPlayer/wwwroot src/ETL-SQL.WorkstationEditor/wwwroot src/etl-sql-vscode/media
git commit -m "refactor(designer): extract the script workbench and data-prep recipes"
```

---

## Task 10: Extract the two pure helpers from inside `createDesigner`

`createDesigner` is a closure over ~15 mutable locals. Only code that closes over **none** of them moves. The `bind*` counterparts stay: they capture `renderCanvas`, `model` and the debounced sync, and moving them is the restructure the spec excludes.

**Files:**
- Create: `src/ETL-SQL.ReportRuntime/Resources/Shared/designer/html-preview.js`
- Create: `src/ETL-SQL.ReportRuntime/Resources/Shared/designer/visual-format-inspector.js`
- Modify: `src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js`

**Interfaces:**
- Consumes: `escapeHtml` from `'./designer-util.js'`.
- Produces: from `html-preview.js` — `export const HTML_PREVIEW_ELEMENT_ATTRIBUTES`, `export const HTML_PREVIEW_BUDGETS`, `export function _isSafeHtmlPreviewUrl(value)`, `export function _copyHtmlPreviewNode(source, ownerDocument, violations)`, `export function _validateHtmlPreviewCss(css)`. From `visual-format-inspector.js` — `export function toHexColor(val, fallback)`, `export function parseNumericRadius(val, fallback)`, `export function parseNumericOpacity(val, fallback)`, `export function visualFormatting(v)`, `export function splitRuleCondition(condition, fallbackField)`, `export function renderVisualFormatInspectorHtml(v, columns)`, `export function renderFormattingSectionHtml(v)`.

- [ ] **Step 1: Prove each candidate is closure-free before moving it**

For each candidate, check it references none of `createDesigner`'s mutable locals:

```bash
f=src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js
for n in _isSafeHtmlPreviewUrl _copyHtmlPreviewNode _validateHtmlPreviewCss toHexColor parseNumericRadius parseNumericOpacity visualFormatting splitRuleCondition renderVisualFormatInspectorHtml renderFormattingSectionHtml; do
  start=$(grep -n "function $n" $f | head -1 | cut -d: -f1)
  end=$(awk -v s="$start" 'NR>s && /^    }$/ {print NR; exit}' $f)
  hits=$(sed -n "${start},${end}p" $f | grep -cE '\b(model|pageIdx|selVisualId|selVisualIds|scriptEditor|reportName|reportVersion|sourceRevision|clipboardVisuals|isDirty|leaseState|leaseTimer|leaseRequestInFlight|leaseDisposed|activeSnapshotFilter|renderCanvas|renderAll|renderProps|syncScriptFromGridDebounced)\b')
  printf '%-34s closure-refs=%s\n' "$n" "$hits"
done
```

Expected: `closure-refs=0` for every name. **Any name with a non-zero count does not move in this task** — drop it from SYMBOLS and note it in the commit message. Do not add a parameter to make it move; that is a signature change, which the Global Constraints forbid.

- [ ] **Step 2: Extract `html-preview.js`**

Run the **Extraction Procedure** steps A–E with SOURCE `designer/designer.js`, TARGET `designer/html-preview.js`, SYMBOLS `HTML_PREVIEW_ELEMENT_ATTRIBUTES`, `HTML_PREVIEW_BUDGETS`, `_isSafeHtmlPreviewUrl`, `_copyHtmlPreviewNode`, `_validateHtmlPreviewCss` (all confirmed by Step 1), IMPORTS `import { escapeHtml } from './designer-util.js';` if referenced. Responsibility sentence: `Sanitizer and budgets for the designer's HTML-visual preview.`

These functions are nested inside `createDesigner`, so they are indented four spaces further than a top-level declaration. Dedent by four spaces when moving — this is the one formatting change the Global Constraints permit, because a nested function cannot be a top-level declaration without it. Change nothing else.

- [ ] **Step 3: Extract `visual-format-inspector.js`**

Run the **Extraction Procedure** steps A–E with SOURCE `designer/designer.js`, TARGET `designer/visual-format-inspector.js`, SYMBOLS `toHexColor`, `parseNumericRadius`, `parseNumericOpacity`, `visualFormatting`, `splitRuleCondition`, `renderVisualFormatInspectorHtml`, `renderFormattingSectionHtml` (minus any Step 1 rejected), IMPORTS `import { escapeHtml } from './designer-util.js';` if referenced. Responsibility sentence: `HTML builders and value parsers for the visual formatting inspector.` Dedent by four spaces as in Step 2.

- [ ] **Step 4: Skip Step F**

All of these are `createDesigner` internals and were never exported. Confirm:

```bash
git show HEAD:src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js | grep -nE "^export .*(renderVisualFormatInspectorHtml|renderFormattingSectionHtml|_isSafeHtmlPreviewUrl|toHexColor)"
```

Expected: no output.

- [ ] **Step 5: Measure the result**

```bash
wc -l src/ETL-SQL.ReportRuntime/Resources/Shared/designer/*.js
```

Expected: `designer.js` is roughly 3,200 lines, down from 9,008, and no part module exceeds ~1,500. Record the numbers in the commit message.

- [ ] **Step 6: Run the Gate Set**

- [ ] **Step 7: Commit**

```bash
git add src/ETL-SQL.ReportRuntime/Resources/Shared/designer/ src/ETL-SQL.Portal/wwwroot src/ETL-SQL.ReportPlayer/wwwroot src/ETL-SQL.WorkstationEditor/wwwroot src/etl-sql-vscode/media
git commit -m "refactor(designer): extract the closure-free preview and inspector helpers"
```

---

## Task 11: Add bundle generation to `sync-assets.js`

Written **before** any runtime part exists, against a bundle of one file, so the generator has a red/green loop of its own rather than being debugged in the middle of a 9,668-line move.

**Files:**
- Modify: `scripts/sync-assets.js`
- Modify: `eslint.config.mjs`
- Modify: `tsconfig.json`
- Create: `src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.bundle.js` (generated)

**Interfaces:**
- Produces: `RUNTIME_PARTS` — the ordered array in `sync-assets.js` that Tasks 13–18 append to as each part is created. `buildRuntimeBundle()` → `Promise<string>`, called in both sync and check mode.

- [ ] **Step 1: Write the failing check — the generator does not exist yet**

Run:

```bash
node -e "const s=require('fs').readFileSync('scripts/sync-assets.js','utf8'); process.exit(s.includes('buildRuntimeBundle')?0:1)"; echo "exit=$?"
```

Expected: `exit=1`.

- [ ] **Step 2: Add the generator to `scripts/sync-assets.js`**

Insert after the `getExpectedContent` function, before `existsAsync`:

```js
// ── Offline-snapshot bundle ──────────────────────────────────────────────────
//
// OfflineSnapshotViewer.cs inlines the report runtime into a single <script> tag in a one-file
// .etlsnap. A single file has no siblings to import, so the ES-module parts are concatenated here
// into report-runtime.bundle.js, which is what ETL-SQL.Reporting embeds.
//
// The transform is line-based, which is why the parts obey a constrained export style (see
// docs/superpowers/specs/2026-09-07-browser-file-split-design.md). Anything it cannot handle is an
// error rather than a silent omission: a part that vanished from a snapshot would fail only for a
// user opening an .etlsnap, which is the furthest possible place from this script.
const RUNTIME_PARTS = [
    'report-runtime.js',
];
const RUNTIME_BUNDLE = 'report-runtime.bundle.js';

const INTRA_IMPORT = /^import\s*\{[^}]*\}\s*from\s*'\.\/rt-[a-z-]+\.js';\s*$/;
const ANY_IMPORT = /^\s*import\b/;
const EXPORT_DECL = /^export\s+(?=(?:async\s+)?function\b|const\b|let\b|class\b)/;
const ANY_EXPORT = /^\s*export\b/;

async function buildRuntimeBundle() {
    const names = await fs.readdir(sharedDir);
    const present = names.filter(n => /^rt-[a-z-]+\.js$/.test(n));
    const unlisted = present.filter(n => !RUNTIME_PARTS.includes(n));
    if (unlisted.length > 0) {
        throw new Error(
            `Runtime part(s) not listed in RUNTIME_PARTS, so they would be missing from every ` +
            `offline snapshot: ${unlisted.join(', ')}`);
    }

    const bodies = [];
    for (const name of RUNTIME_PARTS) {
        const src = await fs.readFile(path.join(sharedDir, name), 'utf8');
        const out = [];
        const lines = src.split('\n');
        for (let i = 0; i < lines.length; i++) {
            const line = lines[i];
            if (INTRA_IMPORT.test(line)) continue;
            if (ANY_IMPORT.test(line)) {
                throw new Error(`${name}:${i + 1} imports something the bundle cannot inline: ${line.trim()}`);
            }
            if (EXPORT_DECL.test(line)) {
                out.push(line.replace(EXPORT_DECL, ''));
                continue;
            }
            if (ANY_EXPORT.test(line)) {
                throw new Error(`${name}:${i + 1} uses an export form the bundle cannot strip: ${line.trim()}`);
            }
            out.push(line);
        }
        bodies.push(`// ─── ${name} ───\n${out.join('\n')}`);
    }

    const banner = `// @ts-nocheck — generated bundle; check the canonical parts.
/* GENERATED FILE - DO NOT EDIT.
 * Built from the rt-*.js parts by: node .\\\\scripts\\\\sync-assets.js
 * Consumed by src/ETL-SQL.Reporting for single-file .etlsnap snapshots.
 */\n\n`;
    return `${banner}(function () {\n    'use strict';\n\n${bodies.join('\n\n')}\n})();\n`;
}

async function syncOrCheckBundle() {
    const bundlePath = path.join(sharedDir, RUNTIME_BUNDLE);
    const expected = await buildRuntimeBundle();
    if (checkMode) {
        if (!(await existsAsync(bundlePath))) {
            drift.push(`Offline bundle missing ${RUNTIME_BUNDLE}`);
            return;
        }
        const actual = await fs.readFile(bundlePath, 'utf8');
        if (actual !== expected) {
            drift.push(`Offline bundle drifted: ${RUNTIME_BUNDLE}`);
        }
        return;
    }
    await fs.writeFile(bundlePath, expected, 'utf8');
    console.log(`  ${RUNTIME_BUNDLE} OK`);
}
```

- [ ] **Step 3: Call it from `run()`, before the sync walk**

In `run()`, immediately after the `sharedDir` existence check and before `const files = await walk(sharedDir);`, insert:

```js
    // Before the walk: the generated bundle is itself an asset the walk then copies to the hosts.
    await syncOrCheckBundle();
```

- [ ] **Step 4: Exclude the generated bundle from both gates**

In `eslint.config.mjs`, add to the `ignores` array beside the existing generated-copy entries:

```js
            // Generated by scripts/sync-assets.js from the rt-*.js parts. Linting it would report
            // every finding a second time, under a path nobody would fix it in.
            'src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.bundle.js',
```

In `tsconfig.json`, add to `exclude`:

```json
    "src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.bundle.js",
```

- [ ] **Step 5: Generate the bundle and confirm it is byte-identical to the source**

At this point `RUNTIME_PARTS` holds only `report-runtime.js`, which has no imports and no exports, so the transform is a no-op wrap.

```bash
node scripts/sync-assets.js
node -e "
const fs=require('fs');
const d='src/ETL-SQL.ReportRuntime/Resources/Shared/';
const b=fs.readFileSync(d+'report-runtime.bundle.js','utf8');
const s=fs.readFileSync(d+'report-runtime.js','utf8');
if(!b.includes(s.split('\n').slice(11).join('\n').slice(0,200))){console.error('FAIL: bundle does not contain the source body');process.exit(1)}
console.log('OK: source body present in bundle');
"
```

Expected: `OK: source body present in bundle`.

- [ ] **Step 6: Verify the drift check works in both directions**

```bash
node scripts/sync-assets.js --check; echo "clean=$?"
printf '\n// tamper\n' >> src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.bundle.js
node scripts/sync-assets.js --check; echo "tampered=$?"
node scripts/sync-assets.js
node scripts/sync-assets.js --check; echo "restored=$?"
```

Expected: `clean=0`, `tampered=1` with `Offline bundle drifted` in the output, `restored=0`. A gate that cannot fail is not a gate — this step is why it is checked rather than assumed.

- [ ] **Step 7: Verify the unlisted-part guard fires**

```bash
echo "export const _probe = 1;" > src/ETL-SQL.ReportRuntime/Resources/Shared/rt-probe.js
node scripts/sync-assets.js 2>&1 | grep -q "not listed in RUNTIME_PARTS" && echo "GUARD OK" || echo "GUARD FAILED"
rm src/ETL-SQL.ReportRuntime/Resources/Shared/rt-probe.js
node scripts/sync-assets.js
```

Expected: `GUARD OK`. This guard is the reason a later task cannot silently omit a part from snapshots.

- [ ] **Step 8: Point `ETL-SQL.Reporting` at the bundle**

In `src/ETL-SQL.Reporting/ETL-SQL.Reporting.csproj:32-34`, change the `Include` path from `report-runtime.js` to `report-runtime.bundle.js`, leaving `<LogicalName>runtime.report-runtime.js</LogicalName>` unchanged so `OfflineSnapshotViewer.cs` needs no edit:

```xml
    <EmbeddedResource Include="..\ETL-SQL.ReportRuntime\Resources\Shared\report-runtime.bundle.js">
      <LogicalName>runtime.report-runtime.js</LogicalName>
    </EmbeddedResource>
```

- [ ] **Step 9: Confirm the project still builds and the resource resolves**

```bash
dotnet build src/ETL-SQL.Reporting/ETL-SQL.Reporting.csproj -v q --nologo 2>&1 | tail -5
```

Expected: `Build succeeded`. A missing file surfaces here as MSB3030.

- [ ] **Step 10: Run the Gate Set**

- [ ] **Step 11: Commit**

```bash
git add scripts/sync-assets.js eslint.config.mjs tsconfig.json src/ETL-SQL.Reporting/ETL-SQL.Reporting.csproj src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.bundle.js src/ETL-SQL.Portal/wwwroot src/ETL-SQL.ReportPlayer/wwwroot src/ETL-SQL.WorkstationEditor/wwwroot src/etl-sql-vscode/media
git commit -m "build(browser): generate a drift-gated offline-snapshot bundle for the report runtime"
```

---

## Task 12: Add the bundle self-check to the vitest suite

Written before the runtime split so it is red for the right reason first. A bundle that concatenates cleanly but throws on load would otherwise reach a `.etlsnap` with no gate in the way.

**Files:**
- Modify: `src/etl-sql-vscode/src/test/reportRuntime.test.ts`

**Interfaces:**
- Consumes: `report-runtime.bundle.js` produced by Task 11.
- Produces: a regression test every later runtime task runs.

- [ ] **Step 1: Add the bundle path and the self-check test**

At the top of `src/etl-sql-vscode/src/test/reportRuntime.test.ts`, beside the existing `RUNTIME_PATH`:

```ts
const BUNDLE_PATH = resolve(
    __dirname,
    '../../../../src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.bundle.js'
);
const BUNDLE_SRC = readFileSync(BUNDLE_PATH, 'utf8');
```

Then add this suite at the end of the file:

```ts
// The offline snapshot inlines the bundle, not the parts. Nothing else proves the concatenation
// produced runnable code: sync-assets.js checks that the bytes match what it would generate, which
// is true of a bundle that throws on the first line.
describe('offline snapshot bundle', () => {
    it('carries no leftover module syntax', () => {
        const offenders = BUNDLE_SRC.split('\n')
            .map((line, i) => ({ line: line.trim(), n: i + 1 }))
            .filter(({ line }) => /^import\b/.test(line) || /^export\b/.test(line));
        expect(offenders).toEqual([]);
    });

    it('parses and exposes the test hook when loaded as a classic script', async () => {
        const win = makeDOM(w => {
            w.__ETLSNAP__ = true;
            w.__MANIFEST__ = EMPTY_MANIFEST;
        }, BUNDLE_SRC);
        expect(win.__reportRuntime__).toBeDefined();
        expect(typeof win.__reportRuntime__.renderCard).toBe('function');
    });
});
```

- [ ] **Step 2: Give `makeDOM` an optional source parameter**

`makeDOM` currently injects `RUNTIME_SRC`. Add a second parameter defaulting to it, so existing call sites are unchanged:

```ts
function makeDOM(setup: (w: any) => void, source: string = RUNTIME_SRC) {
```

and use `source` where it previously used `RUNTIME_SRC` in the script-injection line. Read the existing body before editing — do not rewrite the rest of the helper.

- [ ] **Step 3: Run the new tests and confirm they pass against the one-part bundle**

```bash
cd src/etl-sql-vscode && npx vitest run src/test/reportRuntime.test.ts -t "offline snapshot bundle"
```

Expected: 2 passed. The bundle currently wraps the unsplit runtime, so this is green from the start and stays green through Tasks 13–18 — it is a regression guard, not a red-to-green cycle.

- [ ] **Step 4: Prove the guard can fail**

```bash
cd src/etl-sql-vscode
printf '\nexport const _tamper = 1;\n' >> ../../src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.bundle.js
npx vitest run src/test/reportRuntime.test.ts -t "carries no leftover module syntax" 2>&1 | tail -5
cd ../.. && node scripts/sync-assets.js
```

Expected: the test FAILS while tampered, then the regeneration restores it. A guard never seen red is a guard nobody has checked.

- [ ] **Step 5: Run the full suite to confirm nothing else broke**

```bash
cd src/etl-sql-vscode && npx vitest run src/test/reportRuntime.test.ts 2>&1 | tail -10
```

Expected: the same pass count Task 1 recorded, plus 2.

- [ ] **Step 6: Commit**

```bash
git add src/etl-sql-vscode/src/test/reportRuntime.test.ts
git commit -m "test(browser): assert the offline bundle parses and exposes the runtime hook"
```

---

## Task 13: Extract `rt-util.js`, `rt-state.js` and `rt-theme.js`

The first runtime cut. These three are the leaves of the dependency graph — nothing in them imports another part — so they establish the pattern with no cycle to reason about.

**Files:**
- Create: `src/ETL-SQL.ReportRuntime/Resources/Shared/rt-util.js`, `rt-state.js`, `rt-theme.js`
- Modify: `src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.js`
- Modify: `scripts/sync-assets.js` (`RUNTIME_PARTS`)
- Modify: `eslint.config.mjs` (`classicScripts`)

**Interfaces:**
- Produces: from `rt-util.js` — `escHtml`, `safeUrl`, `interpolateUrlTemplate`, `cssClassToken`, `parseHexColor`, `interpolateColor`, `formatValue`, `abbreviateNumber`, `toCssLength`, `toPixels`, `isOn`, `isOff`, `getOption`, `getStyle`, `getParam`, `parseMultiParameter`, `noDataEl`, `errorEl`, `simpleMarkdown`, `renderInlineMarkdown`, `inputTypeForParameter`. From `rt-state.js` — `isOfflineHost`, `isWebMode`, `vscode`, `isInteractive`, `feedback`, `safeRequestAnimationFrame` (all `const`), plus `getLastManifest()`/`setLastManifest(m)`, `getBaselineManifest()`/`setBaselineManifest(m)`, `getLastActivePage()`/`setLastActivePage(p)`, `getRefreshTimers()`/`setRefreshTimers(t)`. From `rt-theme.js` — `DESIGN_TOKENS`, `isAllowedTokenName`, `isSafeCssValue`, `extractBorderColor`, `resolveDesignTokens`, `isDarkColor`, `isCustomThemeDark`, `clearDynamicTokens`, `applyDesignTokens`, `getDefaultTheme`, `updateBodyTheme`.

- [ ] **Step 1: Unwrap the IIFE**

`report-runtime.js` becomes a module. Delete the opening `(function () {` and `'use strict';` at lines 12–13 and the closing `})();` on the last line, then dedent the whole file by four spaces. Modules are strict by default, so `'use strict'` is redundant, and the bundler re-adds both wrapper and directive.

Verify the dedent did not corrupt template literals — those must keep their original interior whitespace:

```bash
node --check src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.js && echo "PARSES"
```

Expected: `PARSES`.

- [ ] **Step 2: Move `report-runtime.js` out of `classicScripts`**

In `eslint.config.mjs`, delete this line from the `classicScripts` array:

```js
    'src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.js',
```

The config's own comment already states the rule: "A file that grows an `import` has become a module and belongs out of this list."

- [ ] **Step 3: Extract `rt-util.js`**

Run the **Extraction Procedure** steps A–E with SOURCE `report-runtime.js`, TARGET `rt-util.js`, SYMBOLS the `rt-util.js` list in Interfaces above, IMPORTS none. Responsibility sentence: `Pure helpers with no DOM ownership: escaping, URL safety, formatting, option lookup.`

- [ ] **Step 4: Extract `rt-state.js`**

Run the **Extraction Procedure** steps A–E with SOURCE `report-runtime.js`, TARGET `rt-state.js`, SYMBOLS `isOfflineHost`, `isWebMode`, `vscode`, `isInteractive`, `feedback`, `safeRequestAnimationFrame`, `baselineManifest`, `_lastManifest`, `_lastActivePage`, `_refreshTimers`, IMPORTS none. Responsibility sentence: `Host-mode detection and the cross-cutting mutable state the render path shares.`

The four `let` bindings need accessors, because an imported binding is read-only at the importer. Replace each direct assignment across the codebase with its setter. For `_lastManifest`:

```js
let _lastManifest = null;
export function getLastManifest() { return _lastManifest; }
export function setLastManifest(m) { _lastManifest = m; }
```

Do the same for `baselineManifest`, `_lastActivePage` and `_refreshTimers`. This is the one place the "no signature changes" rule bends, and only because ES module semantics leave no alternative — note it in the commit message. Rewrite every read and write:

```bash
grep -n "\b_lastManifest\b\|\bbaselineManifest\b\|\b_lastActivePage\b\|\b_refreshTimers\b" src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.js
```

A bare read becomes `getLastManifest()`; `_lastManifest = x` becomes `setLastManifest(x)`. Compound forms such as `_refreshTimers.push(t)` read through the getter: `getRefreshTimers().push(t)`.

- [ ] **Step 5: Extract `rt-theme.js`**

Run the **Extraction Procedure** steps A–E with SOURCE `report-runtime.js`, TARGET `rt-theme.js`, SYMBOLS the `rt-theme.js` list in Interfaces above, IMPORTS `import { getOption, getStyle, isOn } from './rt-util.js';` — keep only names TARGET references, confirmed by `grep -c`. Responsibility sentence: `Design tokens: resolution, safety validation, and application to an element.`

- [ ] **Step 6: Register the parts, in dependency order**

In `scripts/sync-assets.js`, `RUNTIME_PARTS` becomes:

```js
const RUNTIME_PARTS = [
    'rt-util.js',
    'rt-state.js',
    'rt-theme.js',
    'report-runtime.js',
];
```

`report-runtime.js` is always last — it is the entry.

- [ ] **Step 7: Regenerate and verify the bundle still runs**

```bash
node scripts/sync-assets.js
cd src/etl-sql-vscode && npx vitest run src/test/reportRuntime.test.ts 2>&1 | tail -10; cd ../..
```

Expected: the pass count from Task 12 Step 5. A failure in `carries no leftover module syntax` means a part used a form the transform rejects; a failure in `parses and exposes the test hook` means a duplicate declaration across parts, or a part ordered after something it needs at evaluation time.

- [ ] **Step 8: Run the Gate Set**

- [ ] **Step 9: Commit**

```bash
git add src/ETL-SQL.ReportRuntime/Resources/Shared/ scripts/sync-assets.js eslint.config.mjs src/ETL-SQL.Portal/wwwroot src/ETL-SQL.ReportPlayer/wwwroot src/ETL-SQL.WorkstationEditor/wwwroot src/etl-sql-vscode/media
git commit -m "refactor(runtime): extract util, host state and theme; report-runtime.js is now a module

The four shared 'let' bindings gain getters and setters. An imported binding is
read-only at the importer, so a module split has no other way to keep shared
mutable state; this is the one signature change in the split."
```

---

## Task 14: Extract `rt-transport.js` and `rt-data.js`

**Files:**
- Create: `src/ETL-SQL.ReportRuntime/Resources/Shared/rt-transport.js`, `rt-data.js`
- Modify: `src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.js`, `scripts/sync-assets.js`

**Interfaces:**
- Consumes: `escHtml`, `errorEl` from `'./rt-util.js'`; `isWebMode`, `vscode`, `isOfflineHost`, `getLastManifest` from `'./rt-state.js'`.
- Produces: from `rt-transport.js` — `postDrillIn`, `postDrillUp`, `postParameters`, `_postParametersInternal`, `postRunScript`, `postRefreshVisuals`, `savedViewsRequest`. From `rt-data.js` — `ensureArrowLibrary`, `fetchJsonRows`, `loadVisualRows`, `hasDeferredRows`, `beginLazyRows`, `finishLazyRows`, `publishExportState`, `markExportNotReady`, `markExportReady`, `waitForImagesToSettle`.

- [ ] **Step 1: Extract `rt-transport.js`**

Run the **Extraction Procedure** steps A–E with SOURCE `report-runtime.js`, TARGET `rt-transport.js`, SYMBOLS the `rt-transport.js` list above (`postDrillIn` ~9382, `postDrillUp` ~9400, `postParameters` ~9415, `_postParametersInternal` ~9429, `postRunScript` ~9485, `postRefreshVisuals` ~9499, `savedViewsRequest` ~9015), IMPORTS from the Consumes list, filtered by `grep -c`. Responsibility sentence: `Every network call the runtime makes, and the VS Code postMessage equivalents.`

- [ ] **Step 2: Extract `rt-data.js`**

Run the **Extraction Procedure** steps A–E with SOURCE `report-runtime.js`, TARGET `rt-data.js`, SYMBOLS the `rt-data.js` list above (`publishExportState` ~630, `markExportNotReady` ~646, `waitForImagesToSettle` ~654, `markExportReady` ~670, `hasDeferredRows` ~686, `beginLazyRows` ~690, `finishLazyRows` ~695, `ensureArrowLibrary` ~705, `fetchJsonRows` ~734, `loadVisualRows` ~742), plus the module-private `let` bindings `_exportReadyGeneration`, `_exportReadyPromise`, `_exportReadyResolve`, `_pendingLazyRows`, `_arrowLibraryPromise`. IMPORTS from the Consumes list, filtered. Responsibility sentence: `Row loading, the Arrow library handle, and export-readiness signalling.`

Those five `let` bindings are read and written **only** by the functions moving with them, so they stay module-private and need no accessors. Verify before moving:

```bash
f=src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.js
for n in _exportReadyGeneration _exportReadyPromise _exportReadyResolve _pendingLazyRows _arrowLibraryPromise; do
  printf '%-26s %s\n' "$n" "$(grep -c "\b$n\b" $f)"
done
```

After the cut, re-run against `report-runtime.js`: every count must be 0. A non-zero count means something outside `rt-data.js` touches the binding and it needs accessors like Task 13's — add them rather than exporting the `let`.

- [ ] **Step 3: Register both parts**

`RUNTIME_PARTS` becomes `rt-util.js`, `rt-state.js`, `rt-theme.js`, `rt-transport.js`, `rt-data.js`, `report-runtime.js`.

- [ ] **Step 4: Regenerate and run the bundle self-check**

```bash
node scripts/sync-assets.js
cd src/etl-sql-vscode && npx vitest run src/test/reportRuntime.test.ts 2>&1 | tail -10; cd ../..
```

- [ ] **Step 5: Run the Gate Set**

- [ ] **Step 6: Commit**

```bash
git add src/ETL-SQL.ReportRuntime/Resources/Shared/ scripts/sync-assets.js src/ETL-SQL.Portal/wwwroot src/ETL-SQL.ReportPlayer/wwwroot src/ETL-SQL.WorkstationEditor/wwwroot src/etl-sql-vscode/media
git commit -m "refactor(runtime): extract network transport and row loading"
```

---

## Task 15: Extract `rt-detail.js` and `rt-charts.js`

**Files:**
- Create: `src/ETL-SQL.ReportRuntime/Resources/Shared/rt-detail.js`, `rt-charts.js`
- Modify: `src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.js`, `scripts/sync-assets.js`

**Interfaces:**
- Consumes: `escHtml`, `formatValue`, `noDataEl`, `errorEl`, `getOption`, `getStyle`, `isOn`, `abbreviateNumber` from `'./rt-util.js'`; `applyDesignTokens`, `resolveDesignTokens` from `'./rt-theme.js'`; `getLastManifest`, `safeRequestAnimationFrame`, `isOfflineHost` from `'./rt-state.js'`; `loadVisualRows`, `hasDeferredRows` from `'./rt-data.js'`; `postRefreshVisuals` from `'./rt-transport.js'`.
- Produces: from `rt-detail.js` — `DETAIL_FLIP_ORDER`, `computeDetailPlacement`, `detailSurfaceMode`, `announceDetail`, `closeOpenDetail`, `appendDetailStaticNote`, `destroyDetailSurfaces`, `attachDetailSurface`. From `rt-charts.js` — `renderNativeSvg`, `renderMissingChartPayload`, `resolveInteraction`, `legacyInteraction`, `crossFilterActive`, `nativeToggleOn`, `attachNativeChartToolbox`, `buildDataViewTable`, `saveChartImage`, `attachProgressiveReveal`, `attachNativeZoomSlider`, `broadcastZoomRange`, `nativeLayoutTier`, `observeNativeLayout`, `findVisualInManifest`, `updateNativeVisualInPlace`, `requestNativeLayout`, `applyNativeHighlight`.

- [ ] **Step 1: Extract `rt-detail.js`**

Run the **Extraction Procedure** steps A–E with SOURCE `report-runtime.js`, TARGET `rt-detail.js`, SYMBOLS the `rt-detail.js` list above (~2854–3394), plus the module-private `let` bindings `detailLiveRegion` (~2957), `detailLastAnnouncement` (~2958) and `openDetail` (~2978). IMPORTS from the Consumes list, filtered by `grep -c`. Responsibility sentence: `Detail popovers and tooltips: placement, focus, announcement and teardown.`

Confirm the three `let` bindings are touched only by the moving functions, using the same check as Task 14 Step 2. `openDetail` in particular is the module's single open-popover handle; if anything outside `rt-detail.js` still references it after the cut, add `getOpenDetail()`/`setOpenDetail()` rather than exporting the binding.

- [ ] **Step 2: Extract `rt-charts.js`**

Run the **Extraction Procedure** steps A–E with SOURCE `report-runtime.js`, TARGET `rt-charts.js`, SYMBOLS the `rt-charts.js` list above (~3395–4140), IMPORTS from the Consumes list plus `import { attachDetailSurface, destroyDetailSurfaces } from './rt-detail.js';` — filtered by `grep -c`. Responsibility sentence: `Native SVG chart rendering, its toolbox, zoom, layout tiers and highlighting.`

`renderNativeSvg` calls the global `nativeCharts`, which `eslint.config.mjs` declares as a readonly global. It stays a global reference — do not import it.

- [ ] **Step 3: Register both parts**

`RUNTIME_PARTS` becomes `rt-util.js`, `rt-state.js`, `rt-theme.js`, `rt-transport.js`, `rt-data.js`, `rt-detail.js`, `rt-charts.js`, `report-runtime.js`.

- [ ] **Step 4: Regenerate and run the bundle self-check**

```bash
node scripts/sync-assets.js
cd src/etl-sql-vscode && npx vitest run src/test/reportRuntime.test.ts 2>&1 | tail -10; cd ../..
```

The suite's existing native-chart tests exercise `renderNativeSvg` through `window.__reportRuntime__`. Expected: the pass count from Task 12 Step 5.

- [ ] **Step 5: Run the Gate Set**

- [ ] **Step 6: Commit**

```bash
git add src/ETL-SQL.ReportRuntime/Resources/Shared/ scripts/sync-assets.js src/ETL-SQL.Portal/wwwroot src/ETL-SQL.ReportPlayer/wwwroot src/ETL-SQL.WorkstationEditor/wwwroot src/etl-sql-vscode/media
git commit -m "refactor(runtime): extract detail surfaces and native chart rendering"
```

---

## Task 16: Extract `rt-table.js`, `rt-matrix.js`, `rt-controls-date.js` and `rt-controls-input.js`

Four targets, one task: they are the four largest leaf renderers, none imports another of the four, and each is a single-symbol-family cut with no shared state.

**Files:**
- Create: `src/ETL-SQL.ReportRuntime/Resources/Shared/rt-table.js`, `rt-matrix.js`, `rt-controls-date.js`, `rt-controls-input.js`
- Modify: `src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.js`, `scripts/sync-assets.js`

**Interfaces:**
- Consumes: `escHtml`, `formatValue`, `safeUrl`, `interpolateUrlTemplate`, `cssClassToken`, `noDataEl`, `getOption`, `getStyle`, `isOn`, `isOff`, `getParam`, `parseMultiParameter`, `inputTypeForParameter`, `parseHexColor`, `interpolateColor`, `abbreviateNumber` from `'./rt-util.js'`; `applyDesignTokens` from `'./rt-theme.js'`; `getLastManifest` from `'./rt-state.js'`; `attachDetailSurface` from `'./rt-detail.js'`; `findMicroChart` (Task's own `rt-matrix.js`) — see below.
- Produces: `renderTable`; `renderMatrix`, `findMicroChart`; `renderDatePicker`, `renderRelDatePicker`, `showRelDateHelpModal`; `renderSlicer`, `renderSlider`, `renderSearch`, `renderCheckbox`, `renderTextbox`, `renderNumberbox`, `renderButton`, `applyControlState`, `setParameterAccessibleName`.

- [ ] **Step 1: Extract `rt-table.js`**

Run the **Extraction Procedure** steps A–E with SOURCE `report-runtime.js`, TARGET `rt-table.js`, SYMBOLS `renderTable` (~4679–5209), IMPORTS from the Consumes list filtered by `grep -c`. Responsibility sentence: `The table visual, including its Tabulator integration.`

`renderTable` uses the global `Tabulator`, declared readonly in `eslint.config.mjs`. It stays a global — do not import it.

- [ ] **Step 2: Extract `rt-matrix.js`**

Run the **Extraction Procedure** steps A–E with SOURCE `report-runtime.js`, TARGET `rt-matrix.js`, SYMBOLS `renderMatrix` (~5210–5683) and `findMicroChart` (~9567), IMPORTS filtered. Responsibility sentence: `The matrix visual and its embedded micro-charts.`

`findMicroChart` lives 4,000 lines away from `renderMatrix` in the old file but is used only by it — confirm before moving:

```bash
grep -c "\bfindMicroChart\b" src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.js
```

Expected: 2 (its declaration and one call). A higher count means another module needs it; put it in `rt-util.js` instead and import it here.

- [ ] **Step 3: Extract `rt-controls-date.js`**

Run the **Extraction Procedure** steps A–E with SOURCE `report-runtime.js`, TARGET `rt-controls-date.js`, SYMBOLS `renderDatePicker` (~6678), `showRelDateHelpModal` (~6967), `renderRelDatePicker` (~7036), IMPORTS filtered. Responsibility sentence: `Absolute and relative date parameter controls.`

- [ ] **Step 4: Extract `rt-controls-input.js`**

Run the **Extraction Procedure** steps A–E with SOURCE `report-runtime.js`, TARGET `rt-controls-input.js`, SYMBOLS `setParameterAccessibleName` (~5953), `renderSlicer` (~5960), `renderSlider` (~7281), `renderSearch` (~7493), `renderCheckbox` (~7579), `renderTextbox` (~7661), `renderNumberbox` (~7783), `renderButton` (~8027), `applyControlState` (~4422), IMPORTS filtered. Responsibility sentence: `Slicer, slider, search, checkbox, textbox, numberbox and button parameter controls.`

`renderButton` and `applyControlState` reference `executeAction`, which does not move until Task 18. Leave the reference unresolved for now — the type gate will report `no-undef` at the end of this task, which is expected and is fixed in Task 18 Step 5. **Do not** add a baseline entry for it; if Task 18 does not clear it, the split is wrong.

- [ ] **Step 5: Register all four parts**

`RUNTIME_PARTS` gains `rt-table.js`, `rt-matrix.js`, `rt-controls-date.js`, `rt-controls-input.js`, inserted after `rt-charts.js` and before `report-runtime.js`.

- [ ] **Step 6: Regenerate and confirm the bundle still parses**

```bash
node scripts/sync-assets.js
cd src/etl-sql-vscode && npx vitest run src/test/reportRuntime.test.ts -t "carries no leftover module syntax" 2>&1 | tail -5; cd ../..
```

Expected: PASS. The `parses and exposes the test hook` test may fail on the unresolved `executeAction` — that is the known state from Step 4.

- [ ] **Step 7: Record the expected-red state**

Run `node scripts/lint-browser.mjs` and confirm the **only** findings are `no-undef` for `executeAction` in `rt-controls-input.js`. Any other finding is a mis-move; fix it now. Note the expected-red state in the commit message rather than leaving a reviewer to guess.

- [ ] **Step 8: Commit**

```bash
git add src/ETL-SQL.ReportRuntime/Resources/Shared/ scripts/sync-assets.js src/ETL-SQL.Portal/wwwroot src/ETL-SQL.ReportPlayer/wwwroot src/ETL-SQL.WorkstationEditor/wwwroot src/etl-sql-vscode/media
git commit -m "refactor(runtime): extract table, matrix and parameter control renderers

The lint gate is red at this commit with no-undef for executeAction in
rt-controls-input.js. It is resolved in the next commit, which extracts
rt-actions.js; the gate is green again by the end of the runtime split."
```

---

## Task 17: Extract `rt-visual.js` and `rt-layout.js`

These enter the cycle. Read the spec's "Cycles are expected here, and are safe" section before starting.

**Files:**
- Create: `src/ETL-SQL.ReportRuntime/Resources/Shared/rt-visual.js`, `rt-layout.js`
- Modify: `src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.js`, `scripts/sync-assets.js`

**Interfaces:**
- Consumes: everything from `rt-util.js`, `rt-theme.js`, `rt-state.js`, `rt-data.js`, `rt-charts.js`, `rt-detail.js`, `rt-table.js`, `rt-matrix.js`, `rt-controls-date.js`, `rt-controls-input.js` that the moved code references.
- Produces: from `rt-visual.js` — `renderVisual`, `renderCard`, `renderText`, `renderImage`, `renderHtmlVisual`, `htmlVisualContainerId`, `isSafeHtmlVisualUrl`, `copyHtmlVisualNode`, `HTML_VISUAL_ELEMENT_ATTRIBUTES`, `shouldShowVisualToolbar`, `addVisualToolbar`, `toggleVisualMaximize`, `closeMaximizedVisual`, `resizeChartsIn`. From `rt-layout.js` — `renderPage`, `renderResponsiveLayout`, `renderPhysicalPages`, `renderContainer`, `renderCollapsibleContainer`, `renderTabsContainer`, `renderAccordionContainer`, `renderLayout`, `applyPageOptions`, `isPageVisible`, `executePageOnLoad`, `getPageContainer`, `calculateContainerActiveCount`.

- [ ] **Step 1: Write the cycle-safety comment first**

Create `rt-visual.js` with its header and this comment, before moving any code. Writing it first is the point: it is the invariant the rest of the task must not violate.

```js
// This module and rt-layout.js / rt-actions.js / rt-chrome.js import each other. The cycle is
// deliberate — breaking it needs a dispatch registry, which is a behavior change and is out of
// scope for TODO.md §2 — and it is safe only because of two properties:
//
//   1. Every binding crossing the cycle is a hoisted `function` declaration. A `const foo = () => {}`
//      in its place is in the temporal dead zone when the partner module evaluates, and the page
//      breaks with "Cannot access 'foo' before initialization".
//   2. Nothing in the cycle runs at module-evaluation time. Every entry point is reached from
//      boot(), which runs on DOMContentLoaded.
//
// Preserve both. They are not style preferences.
```

- [ ] **Step 2: Extract `rt-visual.js`**

Run the **Extraction Procedure** steps A–E with SOURCE `report-runtime.js`, TARGET `rt-visual.js`, SYMBOLS the `rt-visual.js` list above (`shouldShowVisualToolbar` ~1625, `addVisualToolbar` ~1632, `toggleVisualMaximize` ~1652, `closeMaximizedVisual` ~1710, `resizeChartsIn` ~1615, `renderVisual` ~2330, `HTML_VISUAL_ELEMENT_ATTRIBUTES` ~2592, `htmlVisualContainerId` ~2606, `isSafeHtmlVisualUrl` ~2610, `copyHtmlVisualNode` ~2629, `renderHtmlVisual` ~2659, `renderCard` ~5698, `renderText` ~6463, `renderImage` ~7951), plus the module-private `let _maximizedVisualCard` (~621) and `_nativeLayoutObservers` (~622). IMPORTS as needed, filtered by `grep -c`. Keep Step 1's comment at the top.

Note: `renderInlineMarkdown` and `simpleMarkdown` were placed in `rt-util.js` by Task 13. If `renderText` is their only caller, leaving them in `rt-util.js` is still correct — do not move them back.

- [ ] **Step 3: Extract `rt-layout.js`**

Run the **Extraction Procedure** steps A–E with SOURCE `report-runtime.js`, TARGET `rt-layout.js`, SYMBOLS the `rt-layout.js` list above (`isPageVisible` ~1772, `executePageOnLoad` ~1781, `applyPageOptions` ~1817, `renderPage` ~1846, `renderResponsiveLayout` ~1874, `renderPhysicalPages` ~1901, `renderContainer` ~1964, `getPageContainer` ~2115, `renderCollapsibleContainer` ~2120, `renderLayout` ~2251, `calculateContainerActiveCount` ~4445, `renderTabsContainer` ~4537, `renderAccordionContainer` ~4625), IMPORTS including `import { renderVisual } from './rt-visual.js';`. Responsibility sentence: `Page, container and layout rendering, including tabs, accordions and physical pages.`

- [ ] **Step 4: Verify property 1 of the cycle holds**

Every symbol crossing between `rt-visual.js`, `rt-layout.js` and (from Task 18) `rt-actions.js` must be a hoisted `function` declaration:

```bash
cd src/ETL-SQL.ReportRuntime/Resources/Shared
for f in rt-visual.js rt-layout.js; do
  echo "--- $f"
  grep -nE "^export (const|let) [A-Za-z_]+ = (\(|async|function)" $f || echo "  none (good)"
done
cd ../../../..
```

Expected: `none (good)` for both. A hit is an arrow-function export that will break the cycle at runtime — convert it to a `function` declaration.

- [ ] **Step 5: Register both parts and regenerate**

`RUNTIME_PARTS` gains `rt-visual.js` then `rt-layout.js`, before `report-runtime.js`.

```bash
node scripts/sync-assets.js
cd src/etl-sql-vscode && npx vitest run src/test/reportRuntime.test.ts -t "carries no leftover module syntax"; cd ../..
```

- [ ] **Step 6: Commit**

The lint gate is still red for `executeAction` from Task 16. Confirm that is the only class of finding, then:

```bash
git add src/ETL-SQL.ReportRuntime/Resources/Shared/ scripts/sync-assets.js src/ETL-SQL.Portal/wwwroot src/ETL-SQL.ReportPlayer/wwwroot src/ETL-SQL.WorkstationEditor/wwwroot src/etl-sql-vscode/media
git commit -m "refactor(runtime): extract visual and layout rendering

Enters the deliberate import cycle between rt-visual, rt-layout, rt-actions and
rt-chrome. The two properties that make it safe are documented at the top of
rt-visual.js. The gate stays red for executeAction until the next commit."
```

---

## Task 18: Extract `rt-actions.js`, `rt-views.js` and `rt-chrome.js`

The last runtime cut. Ends with every gate green — the previous two commits' expected-red state must clear here.

**Files:**
- Create: `src/ETL-SQL.ReportRuntime/Resources/Shared/rt-actions.js`, `rt-views.js`, `rt-chrome.js`
- Modify: `src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.js`, `scripts/sync-assets.js`

**Interfaces:**
- Produces: the three symbol lists from the spec's bundle-order table rows 14, 15 and 16.
- After this task `report-runtime.js` holds only `boot`, `renderManifest`, `syncParameters`, `applyLaunchPrecedence`, `checkRequiredParameters`, the `DOMContentLoaded` and VS Code `message` wiring, and the `window.__reportRuntime__` assignment.

- [ ] **Step 1: Extract `rt-actions.js`**

Run the **Extraction Procedure** steps A–E with SOURCE `report-runtime.js`, TARGET `rt-actions.js`, SYMBOLS `getPageState` (~2749), `applyPageCrossFilter` (~2755), `reApplyCrossFilterStyling` (~2810), `exportCsv` (~4141), `exportExcel` (~4158), `exportExcelDownload` (~4184), `findVisualData` (~4206), `showDrillBackButton` (~4212), `hideDrillBackButton` (~4240), `showCtxMenu` (~4247), `hideCtxMenu` (~4308), `matchesCondition` (~4312), `compareValues` (~4351), `evaluateExpressionAgainstParameters` (~4365), `actionsFor` (~8336), `resolveActionValue` (~8340), `resolveActionParameters` (~8352), `navigateToPage` (~8369), `getActivePage` (~8411), `getActivePageName` (~8418), `isActivePagePaginated` (~8423), `executeAction` (~8428), plus the module-private `let _ctxMenu` (~4246) and `_drillInFlight` (~616). IMPORTS as needed, filtered by `grep -c`. Responsibility sentence: `Actions, navigation, cross-filtering, drill state, the context menu and CSV/Excel export.`

Add the same cycle-safety pointer at the top:

```js
// Part of the deliberate import cycle documented at the top of rt-visual.js. Both properties
// described there apply to this module too.
```

- [ ] **Step 2: Extract `rt-views.js`**

Run the **Extraction Procedure** steps A–E with SOURCE `report-runtime.js`, TARGET `rt-views.js`, SYMBOLS `parseStateHash` (~874), `isOfflineSnapshot` (~8865), `recordParametersOffline` (~8892), `applyParametersOffline` (~8902), `savedViewsBase` (~8925), `applyBookmark` (~8716), `commitResolvedState` (~8768), `applyPresentationState` (~8784), `applyResolvedState` (~8813), `applySavedView` (~8934), `applyUserDefaultSavedView` (~8962), `captureResolvedState` (~8981), `listSavedViews` (~9029), `saveCurrentAsView` (~9034), `saveDefaultView` (~9048), `updateSavedView` (~9056), `deleteSavedView` (~9064), `resetToReportDefault` (~9078), `styleMenuItem` (~9093), `menuHeading` (~9109), `buildViewsPicker` (~9131). IMPORTS filtered. Responsibility sentence: `Bookmarks, saved views, state capture and restore, including the offline-snapshot paths.`

- [ ] **Step 3: Extract `rt-chrome.js`**

Run the **Extraction Procedure** steps A–E with SOURCE `report-runtime.js`, TARGET `rt-chrome.js`, SYMBOLS `showRequiredParametersModal` (~931), `renderAutoPanel` (~1152), `renderHeader` (~1248), `renderNavBar` (~1381), `showModalDialog` (~4472), `hideModalDialog` (~4531), `renderFooter` (~8183), `renderPipelineConsole` (~8190), `updateStagedUI` (~9611). IMPORTS filtered. Responsibility sentence: `Report chrome: header, nav bar, footer, auto-panel, modals and the pipeline console.`

- [ ] **Step 4: Register all three and regenerate**

`RUNTIME_PARTS` gains `rt-actions.js`, `rt-views.js`, `rt-chrome.js`, before `report-runtime.js`. Then:

```bash
node scripts/sync-assets.js
```

- [ ] **Step 5: Confirm the expected-red state from Tasks 16 and 17 has cleared**

```bash
node scripts/lint-browser.mjs;      echo "lint=$?"
node scripts/typecheck-browser.mjs; echo "type=$?"
```

Expected: `lint=0` and `type=0`, with no findings at all. This is the step the previous two commits' red state was deferred to. If `executeAction` is still undefined in `rt-controls-input.js`, add `import { executeAction } from './rt-actions.js';` there.

- [ ] **Step 6: Verify the cycle-safety property across all four cycle members**

```bash
cd src/ETL-SQL.ReportRuntime/Resources/Shared
for f in rt-visual.js rt-layout.js rt-actions.js rt-chrome.js; do
  echo "--- $f"
  grep -nE "^export (const|let) [A-Za-z_]+ = (\(|async|function)" $f || echo "  none (good)"
done
cd ../../../..
```

Expected: `none (good)` for all four.

- [ ] **Step 7: Confirm the entry module holds only the boot path**

```bash
grep -nE "^(export )?(async )?function |^const |^let " src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.js
wc -l src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.js
```

Expected: only `boot`, `renderManifest`, `syncParameters`, `applyLaunchPrecedence`, `checkRequiredParameters`, and roughly 400 lines. Anything else left behind belongs in a part — move it and note which.

- [ ] **Step 8: Run the full bundle self-check**

```bash
cd src/etl-sql-vscode && npx vitest run src/test/reportRuntime.test.ts 2>&1 | tail -15; cd ../..
```

Expected: the pass count Task 12 Step 5 recorded. This is the first point at which the whole runtime runs from a 17-part concatenation, so a duplicate declaration or a bad part order surfaces here.

- [ ] **Step 9: Run the Gate Set**

- [ ] **Step 10: Commit**

```bash
git add src/ETL-SQL.ReportRuntime/Resources/Shared/ scripts/sync-assets.js src/ETL-SQL.Portal/wwwroot src/ETL-SQL.ReportPlayer/wwwroot src/ETL-SQL.WorkstationEditor/wwwroot src/etl-sql-vscode/media
git commit -m "refactor(runtime): extract actions, saved views and report chrome

Completes the report-runtime.js split. report-runtime.js is now the boot entry
only. All gates are green again."
```

---

## Task 19: Switch the runtime hosts to module loading

Nothing before this task changed how a browser loads the runtime — the hosts still request `report-runtime.js`, which is now a module served as a classic script and would fail on its first `import`. This task and Task 20 are what make the split actually work in a browser, so they must land together with Task 18 in one push.

**Files:**
- Modify: `src/etl-sql-vscode/src/reportPreviewPanel.ts:395,453`
- Modify: `src/ETL-SQL.ReportPlayer/Program.cs:642,676`
- Modify: `src/ETL-SQL.Portal/wwwroot/designer-preview.html:31`
- Modify: `src/ETL-SQL.WorkstationEditor/WorkstationEditorApp.cs:1103`

**Interfaces:**
- Consumes: the split runtime from Task 18.
- Produces: no new symbols. Every host that loads `report-runtime.js` loads it as a module.

- [ ] **Step 1: Fix the VS Code CSP — this is the load-bearing change**

In `src/etl-sql-vscode/src/reportPreviewPanel.ts:395`, the CSP reads `script-src 'nonce-${nonce}'`. A nonce does **not** propagate to modules fetched by an `import` statement, so the preview would break with a CSP violation and no other symptom. Change it to match `visualFlowPanel.ts:138`, the module webview already working in this extension:

```ts
script-src ${webview.cspSource} 'nonce-${nonce}';
```

- [ ] **Step 2: Make the VS Code runtime script a module**

At `reportPreviewPanel.ts:453`:

```ts
    <script nonce="${nonce}" type="module" src="${runtimeJsUri}"></script>
```

Leave line 452's `feedbackJsUri` script alone — `feedback.js` is still a classic script and is still in `eslint.config.mjs`'s `classicScripts`.

- [ ] **Step 3: Make both ReportPlayer templates modules**

At `src/ETL-SQL.ReportPlayer/Program.cs:642` and `:676`, change:

```csharp
        "<script src=\"/report-runtime.js\"></script>\n" +
```

to:

```csharp
        "<script type=\"module\" src=\"/report-runtime.js\"></script>\n" +
```

- [ ] **Step 4: Make the Portal preview injection a module**

At `src/ETL-SQL.Portal/wwwroot/designer-preview.html:31`, the script element is built dynamically. Add the type immediately before the `src` assignment:

```js
      rt.type = 'module';
      rt.src = '/js/report-runtime.js';
```

The surrounding comment says the runtime "boots on load and reads `window.__MANIFEST__`, so it must be present first". That still holds: the manifest is assigned before the element is appended, and module deferral makes the ordering safer, not less safe. Update the comment to say so.

- [ ] **Step 5: Make the WorkstationEditor injection a module**

At `src/ETL-SQL.WorkstationEditor/WorkstationEditorApp.cs:1103`, apply the same change:

```csharp
      rt.type = 'module';
      rt.src = '/runtime/report-runtime.js';
```

- [ ] **Step 6: Confirm no host still loads the runtime as a classic script**

```bash
grep -rn "report-runtime\.js" --include=*.cs --include=*.html --include=*.ts src/ | grep -v Installer | grep -v "bundle" | grep -viE "type=.?.?module|rt\.type|^\S+: *\*|//"
```

Review every line printed. Each must either be a module load, a comment, or the embedded-resource path in `ETL-SQL.Reporting`. Anything else is a host that will break at runtime.

- [ ] **Step 7: Build the affected projects**

```bash
dotnet build src/ETL-SQL.ReportPlayer/ETL-SQL.ReportPlayer.csproj -v q --nologo 2>&1 | tail -3
dotnet build src/ETL-SQL.WorkstationEditor/ETL-SQL.WorkstationEditor.csproj -v q --nologo 2>&1 | tail -3
dotnet build src/ETL-SQL.Portal/ETL-SQL.Portal.csproj -v q --nologo 2>&1 | tail -3
```

Expected: `Build succeeded` for all three.

- [ ] **Step 8: Compile the VS Code extension**

```bash
cd src/etl-sql-vscode && npx tsc --noEmit -p . 2>&1 | tail -10; cd ../..
```

Expected: no errors.

- [ ] **Step 9: Run the Gate Set**

- [ ] **Step 10: Commit**

```bash
git add src/etl-sql-vscode/src/reportPreviewPanel.ts src/ETL-SQL.ReportPlayer/Program.cs src/ETL-SQL.Portal/wwwroot/designer-preview.html src/ETL-SQL.WorkstationEditor/WorkstationEditorApp.cs
git commit -m "fix(browser): load the report runtime as a module in every host

The VS Code preview also needs webview.cspSource in script-src: a nonce does not
propagate to modules fetched by an import statement, so without it the preview
fails with a CSP violation and no other symptom. visualFlowPanel.ts already sets
it for the same reason."
```

---

## Task 20: Manual verification of the two paths no gate covers

The CSP change and the bundle are both invisible to every automated gate in this repository. TODO §3 already records what happens when a browser-side guarantee is asserted but never driven.

**Files:** none modified.

- [ ] **Step 1: Verify the VS Code preview webview**

Launch the extension host (`F5` in VS Code from `src/etl-sql-vscode`, or `code --extensionDevelopmentPath=src/etl-sql-vscode`), open a `.rptsql` report, and run the preview command. Open the webview developer tools (Command Palette → "Developer: Open Webview Developer Tools").

Expected: the report renders, and the console shows **no** `Refused to load the script` or `Content Security Policy` error. A blank preview with a CSP error means Step 1 of Task 19 did not take effect.

- [ ] **Step 2: Verify an offline snapshot**

Generate a `.etlsnap` and open it in a browser directly from disk:

```bash
grep -rn "etlsnap" --include=*.cs src/ETL-SQL.App | head -5
```

Use the CLI path this prints to export a snapshot of any sample report, then open the resulting file with `file://`.

Expected: the report renders, the console is clean, and interactive controls respond. A snapshot is a single file with no server, so a failure here means the bundle is broken in a way the JSDOM self-check did not reach.

- [ ] **Step 3: Verify the Portal designer preview**

Start the Portal, open the designer, and switch to preview. Expected: the preview iframe renders the report and the console is clean. This is the dynamic-injection path from Task 19 Step 4.

- [ ] **Step 4: Record the results**

Append a "Post-split verification" section to `docs/releases/v0.20.0-browser-split-baseline.md` recording each of the three checks, what was observed, and the date. If any failed, stop and fix before continuing — do not proceed to Task 21 with a red manual check.

- [ ] **Step 5: Commit**

```bash
git add docs/releases/v0.20.0-browser-split-baseline.md
git commit -m "docs(browser): record manual verification of the webview CSP and offline bundle"
```

---

## Task 21: Repair the consumer checks the split broke

Only the breakage this work caused. Four of these were already red per TODO §3, and Task 1 recorded which.

**Files:**
- Modify: `scripts/test-page-layout-options.mjs:14`
- Modify: `scripts/test-designer-polish.mjs:4`
- Modify: `scripts/test-portal-studio.mjs:12`

**Interfaces:**
- Consumes: the split files from Tasks 3–18 and the bundle from Task 11.

- [ ] **Step 1: Compare against Task 1's record**

```bash
for s in test-designer-polish test-lineage-ui test-page-layout-options test-portal-studio test-result-grid-ui; do
  printf '%-28s ' "$s"; node "scripts/$s.mjs" >/dev/null 2>&1 && echo PASS || echo FAIL
done
cat docs/releases/v0.20.0-browser-split-baseline.md
```

Only a check that went PASS → FAIL is this work's to fix. A check that was already FAIL stays FAIL and belongs to TODO §3.

- [ ] **Step 2: Point `test-page-layout-options.mjs` at the bundle**

It greps the whole runtime as text, so the bundle — which contains every part — is the right target:

```js
const runtimePath = 'src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.bundle.js';
```

Add above it:

```js
// The bundle, not report-runtime.js: this check greps the runtime as one body of text, and after
// TODO.md §2 that text is spread across the rt-*.js parts. The bundle is their concatenation and is
// drift-gated by scripts/sync-assets.js, so it cannot go stale.
```

- [ ] **Step 3: Point `test-designer-polish.mjs` at the parts it needs**

It reads `designer.js` as text at line 4. Determine which parts now hold the strings it asserts on:

```bash
node scripts/test-designer-polish.mjs 2>&1 | head -20
```

For each failing assertion, find the string's new home:

```bash
grep -ln "<the asserted string>" src/ETL-SQL.ReportRuntime/Resources/Shared/designer/*.js
```

Replace the single read with a concatenation of `designer.js` and every part the assertions need:

```js
const js = (await Promise.all([
    'designer.js', 'script-workbench.js', 'run-results.js', 'dag.js',
].map(n => readFile(`src/ETL-SQL.ReportRuntime/Resources/Shared/designer/${n}`, 'utf8')))).join('\n');
```

Include exactly the parts the assertions require, established by the greps above — not every part by default, which would make the check unable to tell where anything lives.

- [ ] **Step 4: Point `test-portal-studio.mjs` at the parts it needs**

Same procedure at line 12. Run it, find each failing assertion's new home with `grep -ln`, and extend the `read(...)` list in its `Promise.all` with those parts. Leave its other reads (`api.js`, `StudioController.cs`, `studio.js`, `designer.css`, `portal.css`) untouched.

- [ ] **Step 5: Confirm the module importers need no change**

`test-lineage-ui.mjs` and `test-result-grid-ui.mjs` import `designer.js` as a module and rely on Task 4's and Task 7's re-exports:

```bash
node scripts/test-lineage-ui.mjs; echo "lineage=$?"
node scripts/test-result-grid-ui.mjs; echo "result-grid=$?"
```

Expected: Task 1's exit codes. If either regressed, a re-export is missing — fix it in the entry module, not in the test.

- [ ] **Step 6: Confirm the final state matches or improves on Task 1's**

```bash
for s in test-designer-polish test-lineage-ui test-page-layout-options test-portal-studio test-result-grid-ui; do
  printf '%-28s ' "$s"; node "scripts/$s.mjs" >/dev/null 2>&1 && echo PASS || echo FAIL
done
```

Every check must be at least as green as Task 1 recorded. No check may have regressed.

- [ ] **Step 7: Run the Gate Set**

- [ ] **Step 8: Commit**

```bash
git add scripts/test-page-layout-options.mjs scripts/test-designer-polish.mjs scripts/test-portal-studio.mjs
git commit -m "test(browser): follow the split sources in the text-grepping checks"
```

---

## Task 22: Run the full pre-push gate

**Files:** none modified.

- [ ] **Step 1: Run the pre-push gate**

```bash
pwsh -File Test-PrePush.ps1
```

Per [[project_v0190_release]], this script has had no timeout and buffered its logs. If it hangs with no output for more than ten minutes, kill it and run its stages individually rather than waiting.

- [ ] **Step 2: Run the browser test lane**

Per [[project_browser_lane_chrome_leftovers]], a previously killed browser run leaves `chrome.exe` behind and the next run then fails every fixture test in about 1ms with a misleading "server has not been started". Clear them first:

```bash
pwsh -Command "Get-Process chrome -ErrorAction SilentlyContinue | Stop-Process -Force"
```

Then run the browser lane per `AGENTS.md`'s test-lane section.

- [ ] **Step 3: Triage any failure against the recorded baselines**

Compare every failure against `docs/releases/v0.20.0-browser-split-baseline.md` and `docs/releases/flaky-test-stability.md`. Per [[project_fulllane_aborts]], never quote a failure count without checking the output for "Test Run Aborted" — the default lane aborts mid-run under memory pressure, and an aborted run's count means nothing.

- [ ] **Step 4: Report, do not silently absorb**

If a failure is genuinely caused by this work, fix it and re-run from Step 1. If it is pre-existing, add it to the baseline document with the evidence that it pre-dates this work. Do not add anything to either gate baseline file.

---

## Task 23: Close out the TODO item

**Files:**
- Modify: `TODO.md:44-52`
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Record the bundler decision, which contradicts what §2 says today**

TODO §2's third bullet currently reads "no bundler is required for this slice, and introducing one here would change the delivery model." That is no longer what was built. Replace that bullet with:

```markdown
- [x] Split along the seams the defects already follow rather than by file size, and keep
  `scripts/sync-assets.js`, the `.gitattributes` LF pinning and the drift gate working — all three
  still work, and `.gitattributes` now pins by path glob rather than by filename so a new part
  cannot be missed. **This slice did introduce a bundle step, contrary to the original bullet.**
  `report-runtime.js` is inlined whole into single-file `.etlsnap` snapshots by
  `OfflineSnapshotViewer.cs`, and a single file has no siblings to `import`; the alternative was
  namespaced IIFEs routing every cross-part call through a `window` object, which is the indirection
  that hid the scope defects this work exists to prevent. `sync-assets.js` now generates a
  checked-in, drift-gated `report-runtime.bundle.js` — no new gate, no Node in the .NET build graph.
  See [the design](docs/superpowers/specs/2026-09-07-browser-file-split-design.md).
```

- [ ] **Step 2: Mark the two split bullets complete with their measured outcome**

```markdown
- [x] Split `designer.js` (9,069 lines). Now ~3,200 lines across ten modules.
- [x] Split `report-runtime.js` (9,426 lines). Now a ~400-line boot entry across seventeen modules.
```

Use the real numbers from Task 10 Step 5 and Task 18 Step 7, not these.

- [ ] **Step 3: Add the follow-up item the spec's out-of-scope section requires**

Add to TODO §2, unchecked:

```markdown
- [ ] Extract the stateful interiors of `createDesigner` (~3,200 lines),
  `createScriptEditorWorkbench` and `createStudioWorkbench`. Each is a closure over its own mutable
  state, so its interior cannot move without threading a context object through every reference —
  a restructure, not a move, and deliberately excluded from the split so a reviewer could tell the
  two apart. `studio.js` (315 KB) and `studio-authoring.js` (149 KB) are larger than `designer.js`
  was and were never in §2's scope; assess them here.
```

- [ ] **Step 4: Update the §2 count in the summary table**

TODO.md's summary table row for §2 reads `3`. Change it to `1` — one open bullet remains, the follow-up added in Step 3.

- [ ] **Step 5: Add the CHANGELOG entry**

Under the v0.20.0 heading, in the style of the surrounding entries:

```markdown
- **Browser sources split by concern.** `designer.js` (9,008 lines) and `report-runtime.js`
  (9,668 lines) are now ten and seventeen modules; no file exceeds ~1,500 lines. `report-runtime.js`
  became an ES module, so `scripts/sync-assets.js` generates a drift-gated
  `report-runtime.bundle.js` for the single-file offline snapshot viewer, and the VS Code preview
  webview's CSP now allows imported modules — a nonce alone does not cover them.
```

- [ ] **Step 6: Verify no stale reference to the old shape survives**

```bash
grep -rn "9,069\|9,426\|9068\|9425" TODO.md ROADMAP.md docs/ 2>/dev/null | grep -v superpowers
```

Review each hit; update any that describe the files as unsplit.

- [ ] **Step 7: Commit**

```bash
git add TODO.md CHANGELOG.md
git commit -m "docs(todo): close the browser file split and record the bundler decision"
```

---

## Self-Review

**Spec coverage.** Every spec section maps to a task: invariants → Global Constraints; `designer/` decomposition → Tasks 3–10; `report-runtime` decomposition → Tasks 13–18; the bundle → Task 11; host wiring and the CSP → Task 19; `.gitattributes` → Task 2; tests and gates → Tasks 12, 21, 22; the out-of-scope follow-up → Task 23 Step 3; sequencing → task order.

**Two deviations from the spec, both deliberate:**

1. **`designer-util.js` is new** — the spec listed eight designer modules; this plan has ten. `escapeHtml` (30 references) and `_feedback` (31) are used from every region, so they cannot live in `rptsql-language.js` where the spec's line ranges implied. Discovered by measurement after the spec was written.
2. **Task 13 adds getters and setters** for the four cross-cutting `let` bindings, which is a signature change the Global Constraints otherwise forbid. An imported binding is read-only at the importer, so a module split has no alternative. Called out in the task and in its commit message.

**Placeholder scan.** No "TBD", no "add appropriate error handling", no "similar to Task N". The Extraction Procedure is a parameterized procedure defined in full in this document, and every task states its own SOURCE, TARGET, SYMBOLS and IMPORTS.

**Type consistency.** `escapeHtml`/`_feedback` (designer) and `escHtml` (runtime) are genuinely different functions in different files and keep their existing names — not a typo. `RUNTIME_PARTS` and `buildRuntimeBundle()` are introduced in Task 11 and referenced by that name in Tasks 13–18. Accessor names introduced in Task 13 (`getLastManifest`/`setLastManifest`, `getBaselineManifest`/`setBaselineManifest`, `getLastActivePage`/`setLastActivePage`, `getRefreshTimers`/`setRefreshTimers`) are used under those names in Tasks 14–18.

**Known-red windows.** Tasks 16 and 17 commit with the lint gate red for `no-undef` on `executeAction`, cleared by Task 18 Step 5. Both commit messages say so. Tasks 18, 19 and 20 must land in the same push: after Task 18 the runtime is a module, and until Task 19 the hosts still load it as a classic script.
