# ETL-SQL Product Roadmap

This document describes future product outcomes and their sequencing. Detailed architecture belongs
in the linked decisions and architecture documents, executable release work belongs in `TODO.md`,
and shipped outcomes belong in `CHANGELOG.md` and the release notes under `docs/releases/`.

The stable deployment-profile topology is defined in
[`docs/architecture/DeploymentProfiles.md`](docs/architecture/deployment-profiles.md). The Enterprise
operating model and trust hierarchy are defined in
[`docs/architecture/roadmaps/Enterprise_Platform_Strategy.md`](docs/architecture/roadmaps/enterprise-platform-strategy.md).

## Roadmap Authoring Contract

Before adding or revising an entry:

1. **Verify current reality.** Search source, tests, `TODO.md`, `CHANGELOG.md`, release notes, and
   authoritative architecture documents. Do not describe an implemented capability as future work.
2. **Separate outcome from mechanism.** State the user or operator outcome first. Treat libraries,
   providers, protocols, performance figures, and implementation shapes as candidates unless an ADR
   has accepted them.
3. **Declare maturity and horizon.** Every entry must use one status and one horizon from the
   vocabularies below.
4. **Keep one coherent initiative per entry.** Split work when parts have different dependencies,
   security boundaries, delivery horizons, or independent user value.
5. **Name boundaries and dependencies.** State what the initiative intentionally excludes and which
   contracts or earlier initiatives it requires.
6. **Deliver vertical slices.** Stages must produce independently testable product capability, not
   merely layers of internal plumbing or an exhaustive all-at-once migration.
7. **Make claims testable.** Exact latency, size, parity, coverage, or visual-count claims require a
   defined fixture and verification method. Otherwise describe them as targets to measure.
8. **Preserve sources of truth.** Detailed design belongs in an ADR or architecture document;
   active executable tasks belong in `TODO.md`; deferred requirements stay with their roadmap entry
   until picked up; shipped outcomes belong in `CHANGELOG.md` and release notes.
9. **Retire completed work.** When the promised outcome ships and is verified, remove the roadmap
   entry or rewrite it to describe only the genuinely remaining increment.
10. **Respect product boundaries.** Roadmap work must preserve script-first authoring, transparent
    transformation, portability, lineage, Zero-Trust execution, deployment profiles, and the
    third-party dependency policy.

### Status vocabulary

- **Exploring** — The problem is plausible, but product or architecture decisions remain open.
- **Accepted** — The outcome and governing architecture are accepted, but work is not scheduled.
- **Planned** — Delivery is selected for an upcoming milestone and decomposed in `TODO.md`.
- **In Progress** — Implementation is active; the entry states only remaining scope and risks.
- **Incremental** — A useful baseline has shipped; the entry describes the next independently
  valuable expansion.

### Horizon vocabulary

- **Foundation** — Required before dependent product work can safely proceed.
- **Next** — A leading candidate for the next planning horizon.
- **Later** — Valuable but not currently a dependency or launch gate.
- **Launch Gate** — Required before a named deployment profile or hosted service can make its
  production claim.

### Required entry shape

Each entry states its status, horizon, authoritative design, problem and intended outcome, why the
horizon is appropriate, boundaries, dependencies, vertical delivery slices, and acceptance evidence.
Keep entries concise and link to detailed designs rather than copying them here.

### Sprint planning for backlog tables

Select task IDs, not an entire family. Move only selected tasks into
`TODO.md`, keeping their dependencies and finish lines. **S** targets 1–2 focused developer-days;
**M** targets 3–5; **D** is a 1–2 day decision or measurement task whose output is a bounded proposal
and an implementation estimate. These are initial sizing targets, not measured delivery promises.
Re-estimate against the fixture and current source before committing a sprint. Split anything that
still exceeds five engineering days. Environment provisioning, soak duration, external access and
approval waits must be recorded separately from engineering effort. No task closes merely because
its test exists; the stated behavior must pass. Any defect found outside its scope becomes an
explicit task with its own estimate.

Tables below are open backlog tasks. Dependencies name prerequisite IDs; an em dash means only the
entry's existing foundations are required. Completed neighboring capabilities remain in the
implemented baseline and are not work to redo.

A task is ready for a sprint only when its fixture, prerequisite IDs, required environment/access
and revised estimate are recorded. Decision tasks may finish by retaining an unsupported outcome;
that does not schedule their proposed implementation.


---

## Foundation and Next Horizon

### Release Engineering — Routine, GitHub-Built Releases

**Status:** Planned  
**Horizon:** Foundation (blocks v0.22.0)  
**Authoritative design:** [Release reliability plan](docs/architecture/roadmaps/release-reliability-plan.md)

**Problem and intended outcome:** v0.22.0 took seven candidates on 2026-11-04. Five restarts came
from release-only tooling that had been tested on invented data and first ran for real on release
day; one came from the MSI Portal uninstall losing its hosted runner. The earlier goal (release in
four hours with the same evidence) optimized the length of a good run while restarts dominated.
The outcome now: releases are routine, built and published only by GitHub Actions, and kept
healthy by a nightly release rehearsal rather than by another sprint. v0.22.0 is the first public
release (VS Code Marketplace via OIDC Trusted Publishing), so the owner's machine must not be a
source of truth for anything a user installs.

**Why foundation:** the owner delayed v0.22.0 until this is done (2026-11-04) so the team can return
to feature work without a release sprint every month.

**Boundaries:**
- Default to common practice; anything heavier needs a stated reason tied to a real user or claim.
- Remove before adding: a new gate must replace or simplify an existing one.
- No waivers: a failing gate is fixed, re-baselined with a stated reason, or made report-only.
- Shared SaaS and ETL-SQL Studio ship as alpha; evidence that only backs them can be deferred.
- Not in scope: the full script and test review (a v0.23.0 priority), VS Code SecretStorage migration for AI API keys (v0.23.0), and Open VSX publishing
  (owner: v0.23.0 or v0.24.0, after the Marketplace path proves out).

**Dependencies:** the existing `nightly-release.yml` and its scheduler, the candidate supervisor,
`release.yml` and its `publish-marketplace` job, and the MSI and DEB certification workflows.

**Vertical delivery slices** (plan section 4; W6–W8 move to the backlog if the two-week timebox
runs out):
1. W0: failure census since v0.21.0, to confirm the order below with data.
2. W1: extend the nightly run into a full release rehearsal (native packages, every post-tag
   collector, operator validator, dry-run release up to the publish call).
3. W2: reuse a lane's result when its inputs are unchanged (design note first).
4. W3: MSI Portal uninstall runner loss fixed at the cause; installers certified nightly.
5. W4: release tooling tested on recorded real outputs.
6. W4b/W4c: remove the workstation from the release path; tag and publish from GitHub only, with
   the Marketplace job hardened (pinned `vsce`, safe reruns).
7. W5: flaky tests fixed at the cause, including the pre-push fixtures that inherit `GIT_DIR`.
8. Below the cut line: W6 performance certification redesign (deterministic hard gates, same-host
   A/B timing), W7 fast pre-push hook and local gate hygiene, W8 evidence scaled to current claims.

**Acceptance evidence** (two weeks from 2026-11-07, with a re-plan check at the end of week 1):
the nightly rehearsal is green five nights running with no manual help; the v0.22.0 candidate
passes every certification on its first attempt; no gate is passed by waiver; a script or docs fix
reruns only the lanes whose inputs changed; and a release reaches the Marketplace with one approval
and no command on the owner's machine.

**Lessons kept from v0.21.0:** defects surfaced only at certification (shift installer and Portal
lanes to nightly); hosted runners lost mid-step need watchdogs that upload evidence first; local
memory pressure made the workstation a bottleneck; release metadata edits must be one command.

### Windows Installer — The MSI Copies Files and Registers Services Only

**Status:** Accepted  
**Horizon:** Next (v0.23.0)  
**Authoritative design:** none yet; [Release reliability plan](docs/architecture/roadmaps/release-reliability-plan.md)
section 10 holds the runner-loss evidence that prompted it.

**Problem and intended outcome:** The WiX 3.14 MSI does more than install. Five JScript custom
actions run as LocalSystem inside the install (`WriteJwt`, `ReadSettings`, `RestoreSettings`,
`PromptCleanData`, `CleanData`) to generate secrets and carry settings forward, and
`util:CloseApplication` force-kills processes with `TerminateProcess`. Microsoft discourages script
custom actions as fragile and antivirus-sensitive, and a failure inside them surfaces only in the
msiexec log. Rollback is not a product feature either: the certification uninstalls, installs the
previous release and copies databases and configuration back by hand. The outcome: the MSI copies
files and registers, starts and stops the two services; the services create and migrate their own
configuration on first start; and rollback is one documented command.

**Why next:** Recommended 2026-11-07 while diagnosing the MSI Portal runner loss (TODO §1, W3). That
diagnosis has not shown the installer to be the cause (24 of 24 install, idle, uninstall runs
passed), so this is robustness work, not the W3 fix, and it waits for v0.23.0 unless the diagnosis
points at it.

**Boundaries:**
- WiX 3.14 stays (free; WiX 7 needs the OSMF fee). Open to MSIX or script-based installer.
- Install paths, profiles (`DEPLOY_PROFILE`), service names and ports are unchanged.
- Existing installs must upgrade in place with their settings and secrets kept.

**Dependencies:** the MSI upgrade certification (`msi-upgrade.yml`); `ETL-SQL admin backup`.

**Slices:**
1. The Orchestrator and Portal generate missing secrets and migrate settings on first start; the
   `WriteJwt`, `ReadSettings` and `RestoreSettings` custom actions are removed.
2. Remove `util:CloseApplication`/`TerminateProcess`; `ServiceControl Stop="both" Wait="yes"` stops
   the services.
3. `ETL-SQL admin restore`, the counterpart of `admin backup`; the certification's rollback uses it
   instead of copying files.
4. Uninstall data cleanup (`PromptCleanData`/`CleanData`) without a script custom action, or as an
   explicit `admin` command the uninstall guidance points to.
5. Run the MSI certification inside a Hyper-V-isolated Windows Server Core container that the
   runner only drives, so nothing on the runner host shares the installed files and a failure
   cannot take the job and its logs with it. Proven on `windows-2022` (branch
   `diag/nested-virt-probe`, run 42418358058, 7 of 7 attempts): the Portal-profile MSI installs,
   both services run, `/health` answers and an immediate uninstall succeeds in about 90 s. On
   `windows-latest` (Server 2025) the Hyper-V role is installed but no Docker engine runs.

**Acceptance evidence:** The MSI has no script custom actions and no `CloseApplication`; a fresh
install and an upgrade from v0.22.0 keep their secrets and settings; the Portal-profile certification
(fresh, upgrade, rollback via `admin restore`) passes on its first attempt on five nightly runs.

### SQL Language — `WITH` in Every Query Expression

**Status:** Accepted  
**Horizon:** Next (v0.23.0)  
**Authoritative design:** none yet; [`docs/grammar.ebnf`](docs/grammar.ebnf) is the grammar contract.

**Problem and intended outcome:** ETL-SQL accepts `WITH` only at the start of a statement, as
T-SQL does. Standard SQL allows it at the start of any query expression, and PostgreSQL, SQLite,
MySQL 8, Oracle and DuckDB accept it. Each of these fails today with a syntax error (probed
2026-11-13): a CTE body (`WITH a AS (WITH b AS (...) SELECT ...)`), a derived table, a scalar
subquery, `IN (...)`, `EXISTS (...)`, and a parenthesized set-operation branch. The outcome: a
query with its own `WITH` works in every one of those positions, and its CTEs are visible only
inside that query, shadowing an outer CTE or `#temp` table of the same name.

**Why next:** Owner decision 2026-11-13: completeness over following an incomplete dialect.
Statement-scoped CTEs (SQ18 in v0.22.0, `CteScope`) already restore names on exit, so nested scopes
can reuse that mechanism.

**Boundaries:**
- Statement-level `WITH`, including `WITH` on `INSERT`, `UPDATE`, `DELETE` and `MERGE`, is unchanged.
- No new CTE features (`MATERIALIZED`, `SEARCH`/`CYCLE`); `WITH RECURSIVE` in a nested position
  follows the same rules as at statement level.
- A correlated subquery with its own `WITH` may re-run its CTEs per outer row; measure before
  caching.

**Dependencies:** SQ18 (statement-scoped CTEs, done in v0.22.0).

**Slices:**
1. Parser and `docs/grammar.ebnf`: `query_expression = [ with_clause ] query_body` in every
   subquery position; `EbnfConformanceTests` and `AstSerializer` round-trip cover it.
2. Engine: a nested query registers its CTEs in its own `CteScope`; SLT cases for each position,
   shadowing an outer CTE and a `#temp` table, and a correlated subquery.
3. Tooling: the linter, lineage, `ScriptDataModelService` and LSP completion see nested CTEs.

**Acceptance evidence:** the six probe queries return the same results as PostgreSQL; an inner CTE
named like an outer one shadows it only inside its subquery; `SltTests`, `EbnfConformanceTests`
and `ParserFuzzTests` pass.

### Data Preparation — `TRANSFORM` Reliability, Schema/Lineage Integration, and Analyst Operations

**Status:** Incremental  
**Horizon:** Next (v0.23.0)  
**Scheduling:** Backlog for prioritization after v0.22.0 ships.  
**Authoritative design:** [TRANSFORM Statement Reference](docs/reference/statements/dml/transform.md),
[Data Prep Helpers](docs/reference/statements/data-prep.md),
[Standard Library Functions](docs/reference/functions/README.md), and
[Engine Architecture](docs/architecture/engine.md).

**Implemented baseline:** `TransformStatementHandler` implements nine `TRANSFORM <target> [FROM <source>] USING <algorithm> (...)`
operations (`FILL_DATES`, `INTERPOLATE`, `DEDUPLICATE`, `PIVOT`, `TOP_N_OTHERS`, `PERIOD_COMPARISON`,
`SHARE_OF_TOTAL`, `NORMALIZE`, `ROLLING_AGGREGATE`), alongside `GENERATE CALENDAR` and
`COMPARE DATASETS`.

**Problem and intended outcome:** The v0.22.0 `TRANSFORM` review (2026-11-10) found silent data
corruption on `NULL` and duplicate-date inputs, missing Zero-Trust / schema / lineage integration,
and missing high-value analyst operations:
1. **Silent `NULL` corruption and date/grain bugs in existing algorithms (`TransformStatementHandler.cs`):**
   - `NORMALIZE` (`MIN_MAX` and `Z_SCORE`), `PERIOD_COMPARISON`, `SHARE_OF_TOTAL`, and `TOP_N_OTHERS`
     call `Convert.ToDecimal(GetValue(row[valueCol]))` / `Convert.ToDouble(...)` without checking for
     `null`, turning SQL `NULL` into `0m` / `0.0`. In `NORMALIZE`, a single `NULL` shifts a positive
     group's `min` to `0` and corrupts `mean`/`stddev` for every row in the group; in
     `PERIOD_COMPARISON`, a `NULL` current value outputs `Diff = -prior` and `Pct = -100%` instead of
     `NULL`.
   - `INTERPOLATE` and `ROLLING_AGGREGATE` sort via `GetNumericOrderValue`, which returns `0m` for
     `NULL` and throws on ISO date strings (`'2026-01-01'`) because it does not call `TryGetDate`.
   - When multiple rows in a group share a calendar date, `FILL_DATES` silently overwrites `byDate[date.Date]`
     (dropping earlier rows), while `PERIOD_COMPARISON` crashes with an unhandled `ArgumentException`
     in `ToDictionary` and misses month-end prior dates (`2026-02-28` vs `2026-01-31`).
   - `TOP_N_OTHERS` emits unaggregated detail rows for the top $N$ categories alongside a single
     aggregated `'Others'` row, mixing two row grains in one output table.
   - `SHARE_OF_TOTAL` outputs a `0..1` fraction while `PERIOD_COMPARISON` outputs a `0..100`
     percentage, contradicting `docs/reference/transform/share_of_total.md`.
2. **Engine, schema, `WHAT_IF`, and tooling gaps:**
   - `TransformStatementHandler` never checks `context.IsWhatIf`, truncating and overwriting target
     tables even under `SET WHAT_IF ON`.
   - `DataParser.ParseTransform` and `docs/grammar.ebnf` allow omitting `FROM <sourceTable>` for
     in-place transforms (and `docs/guides/patterns/sample-guide.md` teaches that syntax), but
     `StageSourceRows` always throws if `SourceTable` is `null` instead of defaulting to `TargetTable`.
   - Every algorithm calls `output.SetColumns(columns)` with string names only, resetting all output
     column schema types to `"OBJECT"`.
   - `BY_GROUP` columns are not validated in 5 of 9 algorithms (`TOP_N_OTHERS`, `PERIOD_COMPARISON`,
     `SHARE_OF_TOTAL`, `NORMALIZE`, `ROLLING_AGGREGATE`), silently collapsing groups on a column typo;
     unrecognized option keys in `stmt.Options` are silently ignored across all 9 algorithms; and
     `BuildGroupKey` conflates `DBNull.Value` with `""`.
   - `LineageAnalyzer` has no `TransformStatement` handling, and `ScriptScopeService` registers
     `TRANSFORM` target tables with an empty column list `[]`.
3. **Analyst "easy button" extensions and missing operations:**
   - Existing algorithms are single-column (`INTERPOLATE`, `NORMALIZE`), 1-day-only (`FILL_DATES`),
     and lack duplicate quarantine routing (`DEDUPLICATE`).
   - High-frequency analyst operations — `UNPIVOT` (`MELT`), `CLEAN` / `STANDARDIZE` (both as a
     scalar `SELECT` function and a multi-column `TRANSFORM`), `OUTLIERS` (clip/flag/filter), `BIN` /
     `BUCKETIZE` (producing `Bin_Start`/`Bin_End` for GoG histograms), `IMPUTE`, `SPLIT` / `EXPLODE`,
     `SCD2`, and `MASK_PII` — are not yet available under `TRANSFORM`.

The outcome is null-safe, schema-preserving, `WHAT_IF`-compliant `TRANSFORM` execution with full
lineage/LSP visibility, plus shared scalar/table hygiene functions and analyst reshape/bucketing
algorithms.

**Why next:** Identified during the v0.22.0 `TRANSFORM` review (2026-11-10) and held for v0.23.0 to
keep the v0.22.0 release candidate frozen.

**Boundaries:** Keep `TRANSFORM` memory-bounded via `OperatorMemoryGrantMB` and `MemoryArbiter`.
Reuse the same underlying C# evaluation logic when an operation exists in both scalar `SELECT` and
table-level `TRANSFORM` forms.

**Dependencies:** `TransformStatementHandler`, `DataParser`, `LineageAnalyzer`, `ScriptScopeService`,
`FunctionRegistry`, and `docs/reference/transform/`.

**Planning:** Use the [backlog sizing and readiness rules](#sprint-planning-for-backlog-tables).
Select individual IDs into `TODO.md` when scheduling.

#### Correctness, `NULL` safety, and engine/tooling integration

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| TR01 | **Fix `NULL` handling and date/grain bugs across the 9 existing `TRANSFORM` algorithms.** Preserve `NULL` inputs as `NULL` outputs (and exclude `NULL` from group `min`/`max`/`mean`/`stddev`/`sum`) in `NORMALIZE`, `PERIOD_COMPARISON`, `SHARE_OF_TOTAL`, and `TOP_N_OTHERS`; use `TryGetDate` and reject/skip `NULL` `ORDER_COL` values in `INTERPOLATE` and `ROLLING_AGGREGATE`; guard `WINDOW_SIZE >= 1`; handle duplicate dates and month-end lookups in `FILL_DATES` and `PERIOD_COMPARISON` without silent data loss or unhandled `ArgumentException`; align `TOP_N_OTHERS` output grain; and reconcile `SHARE_OF_TOTAL` scale with its reference doc. | — | M |
| TR02 | **Enforce `SET WHAT_IF ON`, option/column validation, in-place `FROM` defaults, and `DBNull` group keys.** Respect `context.IsWhatIf` before truncating/writing target tables; validate `BY_GROUP` columns and reject unknown option keys across all 9 algorithms; support in-place `TRANSFORM #t USING ...` by defaulting `SourceTable ?? TargetTable` (and fix stale syntax in `sample-guide.md`); and distinguish `DBNull.Value`/`null` from `""` in `BuildGroupKey` with stable tie ordering in `DEDUPLICATE`. | — | S |
| TR03 | **Preserve output `TableSchema` types and wire `TRANSFORM` into `LineageAnalyzer` and `ScriptScopeService`.** Copy source `ColumnMetadata` types (`DECIMAL`, `INT`, `DATE`, `VARCHAR`) and typed computed columns into `output.Schema`; record table/column lineage edges for `TransformStatement` in `LineageAnalyzer`; and populate target column schemas in `ScriptScopeService` for LSP autocomplete. | TR02 | M |
| TR04 | **Extend existing `TRANSFORM` algorithms for multi-column and flexible-interval workflows.** Support `INTERVAL = 'HOUR' \| 'DAY' \| 'WEEK' \| 'MONTH' \| 'QUARTER' \| 'YEAR'`, explicit `START_DATE`/`END_DATE`, and `GAPS_FILL = 'FORWARD'` in `FILL_DATES`; accept comma-separated `VALUE_COLS` in `INTERPOLATE` and `NORMALIZE`; add `WEEK` and `QUARTER` to `PERIOD_COMPARISON`; and add `REJECT_INTO = #table` / `KEEP = 'UNIQUE_ONLY'` to `DEDUPLICATE`. | TR01, TR02 | M |

#### New analyst `TRANSFORM` algorithms and shared `SELECT` functions

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| TR05 | **Shared scalar `CLEAN_TEXT` / `STANDARDIZE` functions in `SELECT` and multi-column `TRANSFORM ... USING CLEAN`.** Register scalar standard-library functions `CLEAN_TEXT(value [, rules])` and `STANDARDIZE(value, format)` (usable inline in `SELECT`, `WHERE`, `JOIN`, and `GROUP BY` for `TRIM`, `COLLAPSE_WHITESPACE`, `STRIP_CONTROL`, `NULL_IF_EMPTY`, case folding, `PHONE`, `EMAIL`, `POSTAL_CODE`), and wire `TRANSFORM #target [FROM #source] USING CLEAN (COLS = '*' \| 'col1,col2', RULES = '...')` to apply the same engine across multiple or all string columns in one statement. | TR02, TR03 | M |
| TR06 | **Add `TRANSFORM ... USING UNPIVOT` (`MELT`).** Reshape wide columns (`ID_COLS`, `VALUE_COLS`, `NAME_COL`, `VALUE_COL`, `INCLUDE_NULLS = OFF \| ON`) into normalized key/value rows with preserved schema types and SLT coverage. | TR02, TR03 | S |
| TR07 | **Add `TRANSFORM ... USING BIN` (`BUCKETIZE`) and `USING OUTLIERS`.** Implement `BIN` (`VALUE_COL`, `BINS`, `METHOD = 'EQUAL_WIDTH' \| 'QUANTILE' \| 'THRESHOLDS'`, `BY_GROUP`) emitting `Bin_Start`, `Bin_End`, and `Bin_Label` columns directly consumable by GoG ranged-rect histograms; implement `OUTLIERS` (`METHOD = 'IQR' \| 'Z_SCORE' \| 'PERCENTILE'`, `ACTION = 'CLIP' \| 'FLAG' \| 'FILTER' \| 'NULL'`, `BY_GROUP`). | TR01, TR03 | M |
| TR08 | **Add `TRANSFORM ... USING IMPUTE` and `USING SPLIT` (`EXPLODE`).** Implement multi-column missing-value replacement (`IMPUTE` with `METHOD = 'MEAN' \| 'MEDIAN' \| 'MODE' \| 'CONSTANT'`, `BY_GROUP`) and delimited/JSON array splitting (`SPLIT` with `MODE = 'ROWS' \| 'COLUMNS'`). | TR01, TR03 | M |
| TR09 | **Add `TRANSFORM ... USING MASK_PII` and `USING SCD2`.** Provide declarative column masking (`REDACT_EMAIL`, `MASK_LAST_4`, `HASH_SHA256`, `PERTURB_DATE`) and Type-2 Slowly Changing Dimension merging (`KEY_COLS`, `TRACK_COLS`, `EFFECTIVE_DATE` managing `EffectiveFrom`, `EffectiveTo`, `IsCurrent`). | TR02, TR03 | M |

**Acceptance evidence:** All `TRANSFORM` algorithms preserve `NULL` semantics, column data types,
`SET WHAT_IF ON` non-mutation, and lineage/LSP scope schemas; `CLEAN_TEXT`/`STANDARDIZE` work both
in `SELECT` expressions and table-wide `TRANSFORM ... USING CLEAN`; and positive + `statement error`
SLT tests in `tests/slt_data/transform.test` pass.

### Standard Library — File and Path Function Completeness

**Status:** Exploring  
**Horizon:** Later  
**Authoritative design:** none yet; [File and path functions](docs/reference/functions/file-path/README.md)
is the reference contract.

**Problem and intended outcome:** The file and path functions cover existence, size, last write time,
hash, listing, and three path parts. Scripts that move or archive files still need workarounds for
common needs: the creation time, the name without its extension, and a changed extension. The
`DIRECTORY` connector already exposes `CreationTime` and `IsReadOnly` as columns, but no scalar
function returns them. Remote listings (`REMOTE_FILE_LIST`) return only `Name`, `FullPath`, `Size`,
`LastModified`, and `IsDirectory`, and the only remote scalar is `REMOTE_FILE_EXISTS`. The outcome:
a script author finds the obvious file function without writing string surgery.

**Why later:** Every gap has a working alternative (`SUBSTRING`/`REPLACE` on `PATH_FILENAME`, or a
`SELECT` over `FILE_LIST`). None blocks a profile or launch claim.

**Boundaries:**
- Local functions go through `AuthorizeLocalPath`; remote functions go through the connection's
  `IRemoteFileSystem`. No new path-policy rules.
- Pure path functions (`PATH_*`) do no I/O and do not resolve or authorize the path.
- Names follow the existing `FILE_*`, `PATH_*`, `DIRECTORY_*`, `REMOTE_FILE_*` families. No aliases.
- Remote metadata a protocol does not provide (for example SFTP creation time) returns `NULL`,
  not an error.
- **Every function in this entry ships with documentation.** Each gets its own reference page built
  from the [function reference template](docs/templates/function-reference-template.md) and the
  [help and snippet standards](docs/architecture/standards/help-and-snippet-standards.md): full
  signature, parameters, return type, `NULL` behavior, a copy-pasteable example, and a
  `References` section. The page, its hub link, the `RegisterWithHelp` text, and the
  `LanguageMetadata` entry land in the same change as the function. No function merges without
  them.

**Dependencies:** none for the local functions. Remote size and modified time reuse
`IRemoteFileSystem.ListFilesAsync`; a remote creation time needs a new metadata field and is
out of this entry unless a connector exposes it.

**Backlog (select IDs, not the family):**

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| FN01 | **Done in v0.22.0.** **`PATH_FILENAME_NO_EXT(path)`.** Returns the file name without its extension (`orders.csv` → `orders`). Signature, return type, and example in the reference; `LanguageMetadata` entry. | — | S |
| FN02 | **Done in v0.22.0.** **`FILE_CREATED(path)`.** Returns the creation timestamp as a `DATETIME`, the counterpart of `FILE_MODIFIED`. Same authorization and error behavior as `FILE_MODIFIED`. | — | S |
| FN03 | **Done in v0.22.0.** **`PATH_CHANGE_EXTENSION(path, ext)`.** Replaces or removes the extension; accepts `'.bak'` and `'bak'`. | — | S |
| FN04 | **`PATH_ROOT`, `PATH_IS_ABSOLUTE`, `PATH_NORMALIZE`.** Pure string functions. `PATH_NORMALIZE` must match `CAST(... AS PATH)`; decide whether it is needed at all. | FN01 | S |
| FN05 | **`FILE_READONLY(path)` and `FILE_ACCESSED(path)`.** Read-only flag and last access time. Confirm the `DIRECTORY` connector columns and the functions agree. | FN02 | S |
| FN06 | **`DIRECTORY_SIZE` and `DIRECTORY_FILE_COUNT`.** Optional `recursive` flag like `FILE_LIST`. Honors the file-operation and recursion limits in the Zero-Trust guardrails. | — | S |
| FN07 | **Done in v0.22.0.** **`FILE_LINE_COUNT(path [, header_rows])`.** Wanted for flat-file validation (row-count checks before load). `header_rows` is an integer, default `0`, which counts every line. A value of `n` leaves out the first `n` lines, so a one-row header, a multi-row header, or a banner above the data all work. The count never goes below `0` (a file with fewer than `n` lines returns `0`); a negative or non-integer `header_rows` is an error; `NULL` returns `NULL`. Streams the file once, never loads it whole, and honors cancellation. Settle in the first hour: how a trailing newline and a final line without one count, and which line endings (`\n`, `\r\n`, `\r`) are recognized. Same authorization and missing-file behavior as `FILE_SIZE`. | — | S |
| FN08 | **Done in v0.22.0.** **`REMOTE_FILE_SIZE` and `REMOTE_FILE_MODIFIED`.** Scalar lookups over `ListFilesAsync` for FTP, SFTP, and cloud connections. | — | S |
| FN09 | **Done in v0.22.0.** **`REMOTE_DIRECTORY_EXISTS`.** `IRemoteFileSystem.DirectoryExistsAsync` exists, but `REMOTE_FILE_EXISTS` is the only registered remote check and its help text claims to cover directories. Decide whether to split or fix the text. | — | S |
| FN10 | **Done in v0.22.0.** **Remove the duplicate `FILE_EXISTS` / `DIRECTORY_EXISTS` registration.** Both are registered in `FileFunctions.cs` and `StandardFunctions.System.cs`; keep one, and confirm which behavior wins today. | — | S |

**Acceptance evidence:** Each function has a reference page that follows the function reference
template (signature, parameters, return type, `NULL` behavior, a copy-pasteable example,
references) and is linked from the file-path hub; a registry test covers every name; `NULL` and missing-file behavior is
tested and matches its neighbors; and the docs-versus-registry check passes.

### File Operations — `ENCRYPT FILE` / `DECRYPT FILE` Match Their Documentation

**Status:** Accepted  
**Horizon:** Next  
**Authoritative design:** none yet; [ENCRYPT / DECRYPT FILE](docs/reference/file-operations/encrypt-file.md)
is the reference page, and [`ExtensionParser.cs`](src/ETL-SQL.Core/Parser/Components/ExtensionParser.cs)
and [`FileOperationStatementHandler.cs`](src/ETL-SQL.Engine/Handlers/FileOperationStatementHandler.cs)
are what runs today.

**Problem and intended outcome:** Checked against source on 2026-11-13. The reference page documents
`ENCRYPT FILE ... WITH (DELETE_SOURCE, ALGORITHM, KEYFILE, PASSWORD)`. The parser accepts
`ENCRYPT FILE src TO dest [PASSWORD x | PASSWORD(x)] [KEYFILE x] [PGP_KEY x]
[WITH (OVERWRITE, DATE_SUFFIX, SUFFIX_SEPARATOR)] [AT conn] [IF EXISTS]`. In `WITH`, any other key is
parsed and dropped without an error, so `DELETE_SOURCE = ON` does nothing and the plaintext stays on disk.
`KEYFILE` is an SSH key and `PGP_KEY` is the PGP key, the reverse of what the page says. `ALGORITHM` is
not read. With no `TO`, the statement does nothing and raises no error, including under `SET WHAT_IF ON`.
The outcome: the documented statement is the working statement, an option that is not honored is an
error, and the two-step encrypt or decrypt has the options users expect.

**Why next:** The silent no-ops leave plaintext in places a script author believes it was removed. The
docs fix alone is small and can ship first.

**Boundaries:**
- Encrypting on write through a connector (`ENCRYPT = ON`) is separate and already works for
  `FLATFILE` and `EXCEL`; this entry only documents it (FO07).
- Compress before encrypt stays the rule; `CompressAfterEncryptRule` is unchanged.
- No new cryptographic algorithms. Key handling stays in `CryptoUtils`.
- Any new option must pass `FileSystemPolicyAuthorizer`. Deleting a source goes through the same
  delete authorization and script-file protections as `DELETE FILE`.

**Dependencies:** none.

**Backlog (select IDs, not the family):**

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| FO01 | **Done in v0.22.0.** **Rewrite `encrypt-file.md` to the real syntax.** Use the clause form (`PASSWORD`, `KEYFILE` for SSH, `PGP_KEY` for PGP), remove `ALGORITHM` and the `WITH` keys the engine ignores until FO03/FO05 land, fix the vendor-drop example so it deletes the plaintext with `DELETE FILE`, and document `ENCRYPT DIRECTORY` / `DECRYPT DIRECTORY` (password only today). `OVERWRITE` defaults to `ON` in the handler; `compress-file.md` says `OFF`, so correct whichever page is wrong. Follow the [function and statement templates](docs/templates/statement-reference-template.md) and every example must run. | — | S |
| FO02 | **Done in v0.22.0.** **Unknown `WITH` option is an error.** `ParseFileOperationOptions` discards unknown keys. Reject them for every file operation with a message naming the accepted keys, then check the directory-operation parser for the same pattern. Parser, `EbnfConformanceTests`, and a `statement error` test. | — | S |
| FO03 | **Done in v0.22.0** (`COMPRESS` and `DECOMPRESS` take it too; a directory source or an output over the source is refused). **`DELETE_SOURCE = ON \| OFF` (default `OFF`) on `ENCRYPT FILE` and `DECRYPT FILE`.** The source is deleted only after the destination is written and closed, never when the operation fails, and `SET WHAT_IF ON` logs it without deleting. Decide in the same change whether `COMPRESS` and `DECOMPRESS` take it. | FO02 | S |
| FO04 | **Done in v0.22.0.** **Default destination when `TO` is omitted.** Never encrypt in place and never overwrite the source. `ENCRYPT FILE` writes next to the source with a method suffix appended: `.pgp` for `PGP_KEY`, `.enc` for `PASSWORD`; settle the suffix for an SSH `KEYFILE`. `DECRYPT FILE` with no `TO` removes a recognized suffix (`.pgp`, `.enc`) and raises an error for any other name rather than guessing. An existing destination follows `OVERWRITE`. `WHAT_IF` shows the derived path. | FO02 | S |
| FO05 | **`ALGORITHM` decision.** `CryptoUtils.EncryptFile` and `DecryptFile` take an optional `HashAlgorithmName` the statement never passes. Decide whether to expose it (and what it controls) or leave it out of the language. The method already follows from the clause given, so `ALGORITHM = 'AES256' \| 'PGP'` as documented is not needed. | — | D |
| FO06 | **Done in v0.22.0** (PGP also left a partial output file; fixed). **`DECRYPT FILE` failure messages.** Check what a wrong password, a wrong key, and a file that was never encrypted report today; each should name the cause without echoing key material. One test per case. | — | S |
| FO07 | **Document write-time `COMPRESS` and `ENCRYPT` on connectors.** Confirm which file connectors take both options (`FLATFILE`, `EXCEL`, `AVRO` list them; `JSON`, `XML`, and `PARQUET` call the encryption service), that compress runs before encrypt on write, and that read reverses it. Add the options and an "easy button" example to each connector page, with the mutually exclusive options called out. | — | S |
| FO08 | **`TO DIRECTORY` for `ENCRYPT`/`DECRYPT` and `COMPRESS`/`DECOMPRESS`.** Today `TO DIRECTORY` and `DATE_SUFFIX` throw for anything but copy, move and rename. Decide whether to allow them; if not, the reference states it. | FO04 | D |
| FO09 | **Done in v0.22.0** (owner: the master password is never a file passphrase). **Session password for files.** `ENCRYPT`/`DECRYPT FILE` and `DIRECTORY` fall back to `SecurityService.MasterPassword` when `PASSWORD` is absent, but `USE PASSWORD` sets `ScriptPassword` and the CLI's `--pass` sets the evaluator's `MasterPassword`, so on the CLI the fallback never has a value and the statement fails asking for `PASSWORD`. Decide whether the session password should apply; then either wire it through or remove the fallback and the "configured master password" wording from the error. Found writing FO01. | — | S |
| FO10 | **Done in v0.22.0** (owner: allow `.pub` and `.pem`). **Generated public keys are blocked by the file-type list.** `CREATE SSH_KEY_PAIR` writes `id_rsa.pub`, and `.pub` (and `.pem`) are not in `SecurityService.AllowedExtensions`, so `ENCRYPT FILE ... KEYFILE 'id_rsa.pub'` needs `SET ALLOW_FILE_TYPE_ACCESS = '.pub'`. Owner call on adding `.pub`/`.pem` to the default list (`.key`, `.asc` and `.gpg` are already in it). Found writing FO01. | — | S |
| FO11 | **Done in v0.22.0** (encrypt-and-sign and decrypt-and-verify; PgpCore also left plaintext behind on a failed check, now written via a temp file. Detached `SIGN FILE` / `VERIFY FILE SIGNATURE` not built.) **PGP sign and verify (owner 2026-11-13).** Many vendors require files that are encrypted and signed, and an inbound file is only trustworthy if its signature checks. `ENCRYPT FILE src TO dst PGP_KEY 'partner_public.asc' SIGNING_KEY 'our_private.asc' [SIGNING_PASSWORD x]` encrypts and signs in one pass. `DECRYPT FILE src TO dst PGP_KEY 'our_private.asc' [PASSWORD x] VERIFY_KEY 'partner_public.asc'` fails and writes no output when the signature is missing, made by another key, or does not match. Use PgpCore's `EncryptAndSignAsync` and `DecryptAndVerifyAsync` (already a dependency; no new package). `SIGNING_KEY`/`VERIFY_KEY` are PGP only and are syntax errors with `PASSWORD`-only or `KEYFILE` encryption. Optional in the same change if small: `SIGN FILE` / `VERIFY FILE SIGNATURE` for a detached `.sig` without encryption. Reference page, grammar, help text, `LanguageMetadata`, and tests for a good signature, a wrong signer, a tampered file and an unsigned file. Never echo key material in errors. | FO02 | M |

**Acceptance evidence:** Every example on the reference pages runs as written; an unsupported `WITH`
key fails with a clear error; `DELETE_SOURCE = ON` removes the source only after a successful write and
not under `WHAT_IF`; an omitted `TO` produces the documented path or error and never alters the
source; and the tests in `CompressAfterEncryptRuleTests` that omit `TO` still pass.

### File Operations — Managing Remote File Trees over SFTP, FTP, and Cloud Connections

**Status:** Accepted  
**Horizon:** Next (RF01 and RF02), Later (the rest)  
**Authoritative design:** none yet; [SFTP](docs/reference/connectors/services/sftp.md),
[FTP](docs/reference/connectors/services/ftp.md), and [TRANSFER](docs/reference/file-operations/transfer.md)
are the references. [`IRemoteFileSystem`](src/ETL-SQL.Core/Data/IRemoteFileSystem.cs) is the contract.

**Problem and intended outcome:** Checked against source on 2026-11-13. `SFTP`, `FTP`/`FTPS`, `S3`,
`AZURE_BLOB`, and `SHAREPOINT` implement `IRemoteFileSystem` (list, upload, download, delete, exists,
rename, create and delete directory). `SEND FILE`/`RECEIVE FILE` take `*` and `?` wildcards, and
`DELETE`, `MOVE`, `RENAME` work with `AT conn`. That covers sending and fetching a file. It does not
cover managing a remote tree: `COPY FILE ... AT conn` and every remote directory operation except
create and delete fail with "not supported on remote systems", `SYNC DIRECTORY` has no remote handling,
wildcard receive does not descend into subfolders, and listings carry only name, path, size, modified
time, and a directory flag. The FTP page tells users to set `USE_SSL=TRUE`; the connector accepts only
`EXPLICIT`, `ON`, or `IMPLICIT` and treats anything else as no encryption, so following the page sends
credentials in clear text. The outcome: a script can copy, mirror, and verify a remote tree with the
same statements it uses locally, and the connector pages describe the options the code reads.

**Why this horizon:** RF01 is a documentation defect with a security effect and RF02 is a
small connector change; both are Next. The feature work has working alternatives (download and
re-upload, `FOREACH` over a listing) and is Later.

**Boundaries:**
- No new protocol or library. FluentFTP and SSH.NET provide what is needed; a new third-party
  dependency follows the third-party dependency policy.
- Every remote operation counts toward the file-operation limit and honors `SET WHAT_IF ON`, as the
  local operations do.
- No remote command execution (SSH shell or `exec`). It is a different security boundary and needs
  its own decision.
- Metadata a server does not report returns `NULL`, not an error.
- Each statement and function change ships with its reference page, following the
  [documentation standard and templates](docs/templates/statement-reference-template.md).

**Dependencies:** FN08/FN09 (remote file functions) share the listing metadata work in RF05.

**Backlog (select IDs, not the family):**

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| RF01 | **Done in v0.22.0.** **Fix the FTP page and reject bad `USE_SSL` values.** Document the options the connector reads: `USE_SSL = OFF \| EXPLICIT \| IMPLICIT` (`ON` is accepted as `EXPLICIT`) and `PASSIVE = ON \| OFF` (default `ON`), not `USE_PASSIVE` or `USE_SSL=TRUE`. Remove the "use SFTP instead" line under FTPS. A value other than the accepted ones is an error, not plaintext. List `TIMEOUT_SECONDS` once RF02 lands. Both authentication patterns stay documented. | — | S |
| RF02 | **Done in v0.22.0.** **FTP `TIMEOUT_SECONDS` and TLS certificate handling.** Read `TIMEOUT_SECONDS` (default `30`) like the other connectors. Decide how an invalid FTPS certificate behaves today and expose the choice (strict by default; a pin or an explicit opt-out, mirroring `HOST_KEY_FINGERPRINT` / `ALLOW_UNPINNED_HOST_KEY`). | RF01 | S |
| RF03 | **Verify SFTP and FTP listings.** SSH.NET `ListDirectory` normally returns `.` and `..`; the SFTP listing does not filter them. Add a test over a recorded or containerized server, drop the dot entries from `REMOTE_FILE_LIST`, `SELECT` over the connection, and `IsDirectory` handling, and confirm FTP matches. | — | S |
| RF04 | **Remote `COPY FILE`.** Same-connection copy (`COPY FILE a TO b AT conn`) by download to a bounded temp stream and upload, or a server-side copy where the protocol has one (S3, Azure Blob, SharePoint). Cross-connection copy follows the same path. Overwrite follows `OVERWRITE`; default `ON` as locally. | — | M |
| RF05 | **Richer remote metadata.** Extend `FileMetaData` with creation time (where reported), permissions, owner, and a symlink flag; `NULL` where the protocol cannot supply it. `REMOTE_FILE_LIST` and the connection `SELECT` expose the new columns and the lineage and LSP column lists follow. | — | M |
| RF06 | **Recursive listing and receive.** `REMOTE_FILE_LIST` and `RECEIVE FILE` take a recursive option bounded by `SET ALLOW_RECURSIVE_LAYERS` (five levels by default). Define what a non-empty directory delete does on every connector (SSH.NET and FluentFTP differ today) and make it identical, with an explicit option to delete recursively. | RF03 | M |
| RF07 | **Remote directory rename, move, and copy.** `RENAME DIRECTORY`, `MOVE DIRECTORY`, and `COPY DIRECTORY` with `AT conn`. Rename and move use the server rename; copy is built on RF04 and RF06. | RF04, RF06 | M |
| RF08 | **Remote `SYNC DIRECTORY`.** Mirror local to remote and remote to local by size and modified time, with `WHAT_IF` showing the plan. Deletions on the destination need an explicit option and pass the same guard as `DELETE FILE`. | RF05, RF06 | M |
| RF09 | **Verify a transfer.** An option on `SEND FILE`/`RECEIVE FILE` (and the `VERIFY FILE INTEGRITY` counterpart) that compares size and, where the server offers it, a checksum after the transfer, and removes a partial local file on failure. Decide whether to support resume of large downloads here or leave it out. | RF05 | M |
| RF10 | **Done in v0.22.0** (SFTP had the same bug and is fixed too). **FTP rename without losing the destination.** `RenameFileAsync` deletes an existing destination before renaming. Rename to a temporary name first or fail without deleting. | — | S |
| RF11 | **Decision: remote command execution.** Whether a connection type that runs a single approved command over SSH belongs in the product, and under what allow-list and audit rules. Output is a proposal and an estimate only. | — | D |

**Acceptance evidence:** The FTP page matches what `FtpConnector` reads and a wrong `USE_SSL` value
fails; an SFTP and an FTP listing contain only real entries, proven against a test server; `COPY FILE`
and the directory operations work with `AT conn` on SFTP, FTP, and one cloud connection; a remote
`SYNC DIRECTORY` plan under `WHAT_IF` matches what the real run does; and the transfer verification
option fails a truncated download.

### Database Connectors — Documentation Parity, Write-Path Clarity, and Credential Gaps

**Status:** Accepted  
**Horizon:** Next (DB01–DB03), Later (the rest)  
**Authoritative design:** none yet; [Database connectors](docs/reference/connectors/databases/README.md)
are the references and [Connector Standards](docs/architecture/standards/connectors-standards.md)
govern option naming. The connector classes under `src/ETL-SQL.Connectors.Databases/` own the accepted
option lists.

**Problem and intended outcome:** Checked against source on 2026-11-13 (a review of the ten database
connector pages against each connector's option allowlist). The connectors work, but the pages
disagree with the code, and a developer cannot tell from the docs how each target writes data. The
page text describes options the code does not accept (`ROLE`, `PRIVATE_KEY_PASSPHRASE`, `SSL_CA`,
`WALLET_LOCATION`, `SID`, `READ_ONLY`, `BUSY_TIMEOUT_MS`, `KEY_FILE`, `ENCRYPTION`) and omits options it
does (`ACCOUNT`, `PORT`, `PROTOCOL`, `ENCRYPT`, `TIMEOUT_SECONDS`, and several ODBC keys). Bulk-write
paths exist in the code (`SqlBulkCopy` for MSSQL, array binding for Oracle, `MySqlBulkCopy` for MySQL)
but only MySQL documents one. The outcome: every connector page matches what the connector reads, a
developer can pick a write strategy per target from one place, and the credential gaps below are
either closed or recorded as decisions.

**Why this horizon:** DB01–DB03 are documentation defects, and following them wastes a developer's
time or silently ignores an option. The new capabilities have working alternatives (`ODBC`, staging
through `#temp`) and are Later.

**Boundaries:**
- No new native warehouse connector. Redshift, Synapse, Databricks, Trino, and Dremio stay on `ODBC`
  per the [data-lake connectors strategy](docs/architecture/roadmaps/data-lake-connectors-strategy.md).
  DB11 only re-opens that decision with evidence.
- DuckDB is not shipped. The strategy document lists it as an unchecked, low-priority Phase 5 item;
  this entry does not change that.
- Option names stay `UPPERCASE_WITH_UNDERSCORES` with no aliases, per Connector Standards.
- `TEST CONNECTION` and `MERGE` already ship and are not roadmap work. Per-connector `MERGE` pushdown
  behavior is documented under DB04, not rebuilt.

**Dependencies:** none for DB01–DB05. DB07 and DB08 follow the third-party dependency policy for any
new package.

**Backlog (select IDs, not the family):**

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| DB01 | **Reconcile option lists with the code.** For each of the ten pages, make the Options table equal the connector's allowlist and fix the Authentication section. Known mismatches: Snowflake (`USER` vs `USERNAME`; add `ACCOUNT`, `PORT`, `PROTOCOL`; `ROLE` and `PRIVATE_KEY_PASSPHRASE` are documented but not read), BigQuery (`KEY_FILE` vs `CREDENTIAL_FILE`), Oracle (`SID`, `WALLET_LOCATION`), MySQL (`SSL_CA`, and the `VERIFY_*` spellings against the `SSL_MODE` values the code accepts), SQLite (`READ_ONLY`, `BUSY_TIMEOUT_MS`), Neo4j (`ENCRYPTION`; confirm whether encryption comes from the URI scheme), MSSQL (`ENCRYPT`, `PORT`), ODBC (`USER`, `PWD`, `TIMEOUT_SECONDS`, `TABLE`, `HTTPPATH`, `AUTHMECH`, `SSL`). A test compares each page's option table to the allowlist so they cannot drift again. | — | M |
| DB02 | **Document `TIMEOUT_SECONDS` everywhere.** Add it to Oracle, Postgres, Snowflake, and BigQuery with the defaults the code applies (30 s for OLTP, the data-warehouse default for Snowflake and BigQuery). State the defaults in each page's option table. | DB01 | S |
| DB03 | **Page-standard gaps.** Add the mutually exclusive call-outs the page standard requires (MSSQL and Oracle have them; the rest do not, including Snowflake `PASSWORD` vs `PRIVATE_KEY_FILE` and the Mongo URI vs `HOST`/`PORT` forms). Quote the `ENC:` value in the MongoDB example or confirm the lexer accepts it bare. Show one non-inline credential pattern (`ENC:`, `SECRET:`, or `${ENV}` with `Security:AllowedEnvVars`) on each page. | DB01 | S |
| DB04 | **Write-path reference per connector.** For each database connector, state the bulk path (`SqlBulkCopy`, Oracle array binding, `MySqlBulkCopy`, row batches for the rest), what `MERGE` pushes down versus what the engine emulates, transaction support, `WHAT_IF` behavior, and the idempotent-load pattern to use. Verify each statement against the data source class before writing it. | DB01 | M |
| DB05 | **Capability matrix.** One table on the databases README: transactions, pushdown, bulk load, schema introspection, `WHAT_IF`, default timeout, and auth modes, one row per connector. BigQuery has no transactions and SQLite is single-writer; Neo4j enlists transactions in its own way. Built from DB04 so it stays accurate. | DB04 | S |
| DB06 | **Dialect notes for Postgres, Oracle, and MSSQL.** Snowflake, MySQL, and BigQuery list their supported native SQL; these three do not. Cover row capping (`LIMIT`, `FETCH FIRST`, `TOP`), `DUAL`, and identifier quoting, checked against the linter's dialect metadata. | — | S |
| DB07 | **Postgres bulk load.** The Postgres data source has no binary `COPY` import, unlike MSSQL, Oracle, and MySQL. Add one behind the existing write path, with the same transaction and `WHAT_IF` behavior, and measure it against the current batch insert on a fixture before claiming a speedup. | DB04 | M |
| DB08 | **Decision: credential providers.** No source references Azure Key Vault or AWS Secrets Manager; secrets come from `ENC:`, `SECRET:`, and allow-listed environment variables. Decide whether a vault-backed `SECRET:` source belongs in the product and under which trust rules. Output is a proposal and an estimate only. | — | D |
| DB09 | **Decision: cloud identity authentication.** `SqlServerConnector` reads no Entra or managed-identity option, and the review did not check Postgres, MySQL, or Oracle for IAM or Entra token auth. BigQuery already uses Application Default Credentials. Verify the current state for each connector, then propose the options (for example Entra access tokens for MSSQL, IAM tokens for Postgres and MySQL) and an estimate. | — | D |
| DB10 | **Snowflake `ROLE` and key passphrase.** Either add `ROLE` and `PRIVATE_KEY_PASSPHRASE` to the connector (encrypted keys are common in production) or confirm they are out of scope and keep them off the page. An encrypted private key currently has no passphrase option. | DB01 | S |
| DB11 | **Decision: native warehouse connectors.** Whether Databricks or Redshift justify a native connector over `ODBC`, given that `ODBC` needs a vendor driver installed on the host. Gather evidence (install friction, feature loss through the bridge, type fidelity) and propose or reject. | — | D |
| DB12 | **Decision: connector resilience options.** The review found no connector-level retry, isolation-level, or session-setup option (`search_path`, time zone). Confirm that against source, then decide which belong on `CREATE CONNECTION` and which belong in script. Output is a proposal and an estimate only. | — | D |
| DB13 | **Document native-to-engine type mapping.** Per connector, how native types land in the engine (Oracle `NUMBER`, Postgres arrays and `jsonb`, Snowflake `VARIANT`, MSSQL `datetimeoffset`, BigQuery `STRUCT`), including that integer types store as `decimal`. Confirm whether this already exists elsewhere in the docs before writing. | DB04 | M |
| DB14 | **Incremental-load examples.** Confirm whether the ETL Cookbook already shows a watermark pull per source; if not, add runnable lifecycle recipes (MSSQL `rowversion`, Postgres `xmin` or a timestamp column, Oracle `SCN`) following the Extract → Stage → Validate → Merge → Cleanup → Notify shape. | DB04 | M |
| DB15 | **MongoDB and SQLite parity.** MongoDB has no pipeline or filter pass-through like Neo4j's `EXECUTE ... BEGIN ... END` and no upsert key like Neo4j's `KEY_COLUMNS`. SQLite has no journal-mode or pragma option. Decide per item whether to add or document the workaround. | DB04 | D |

**Acceptance evidence:** A test fails when any database page's option table differs from the matching
connector allowlist; each page lists `TIMEOUT_SECONDS` with its default and the mutually exclusive
options; the capability matrix and write-path reference match the data source classes, checked line
by line against the source; and each decision item (DB08, DB09, DB11, DB12, DB15) has a written
proposal and estimate.

## Later Reporting and Presentation Work

### Reporting & Presentation — Grammar-of-Graphics Semantic Extensions

**Status:** Incremental
**Horizon:** Later
**Scheduling:** Backlog. This sprint's GoG expansion is finished; no further combination is scheduled.
**Authoritative design:** [Native Grammar-of-Graphics Contract](docs/architecture/decisions/grammar-of-graphics-spec-ir.md),
[Native Advanced Chart Authoring](docs/architecture/decisions/native-advanced-chart-authoring.md),
[Connected mark conditions](docs/architecture/decisions/connected-mark-conditions.md), and
[Reporting semantic contracts](docs/architecture/reporting-semantic-contracts.md).

**Implemented baseline:** The renderer-neutral spine, bounded polar ARC radial stacking,
continuous quantitative transposed POINT/TEXT/LINE/AREA/RULE/RECT aspect forms, their supported
placement/interpolation and intervals, and source-owned Cartesian/transposed LINE/AREA/ribbon
conditions are implemented. Connected forms include GAP/CONNECT/ZERO, row decorations,
interpolation, categorical series and facets. Exact supported combinations remain owned by the
validator, capability matrix and focused references; implementation history remains in
`CHANGELOG.md` and the linked contracts.

**Problem and intended outcome:** Add a specific chart combination when a representative report
needs it. Authors should receive consistent geometry, ownership and accessible meaning across
backends, with explicit diagnostics for combinations outside the chosen scope.

**Why later:** No current catalog visual or renderer retirement depends on these extensions.
The former two-checkbox task had no bounded completion point. Each increment below can close
independently; finishing it creates no obligation to implement adjacent combinations.

**Boundaries:** Preserve raw values, source-owned conditions, domains and existing baseline,
ribbon and confidence semantics. Keep transformations in the script. Do not add renderer-specific
syntax, arbitrary paths, a second chart schema or new map providers/projections. Existing focused
native layouts remain valid. Confidence conditions and arbitrary AREA baselines are not implicitly
added to any task below.

**Dependencies:** Versioned ChartSpec/PlotPlan contracts, shared scale and axis ownership, native
SVG/static export, semantic terminal/accessibility fallback, lossless authoring, capability matrix
and the existing deterministic cross-backend tests.

**How a chunk is selected:** Move only one named increment into `TODO.md`. Name its mark, coordinate
orientation, scale kinds, placement unit, null policy and interpolation; include one representative
report and explicit exclusions. An implementation increment includes resolution, validation and
all applicable backends together. A decision increment finishes with an accepted contract and a
bounded implementation proposal, or an explicit decision to retain rejection. It does not enable syntax.
Facets, extra series, additional policies or orientations are separate follow-ups unless named in
that increment. Do not open a replacement umbrella checkbox for "all remaining combinations".

#### Connected placement — one mark and one unit per increment

**Status:** Accepted
**Horizon:** Later
**Outcome:** Displace a conditioned path and its row decorations together without changing raw
coordinates or source ownership. Start with one unstacked Cartesian layer, quantitative linear
primary X/Y, GAP and LINEAR interpolation. AREA uses either the explicit zero baseline or the
existing ribbon form. Retain the corresponding current decoration semantics.
**Dependencies:** Existing connected geometry and ordinary placement arithmetic; BAND and JITTER
also need fitted plot dimensions, and DATA needs a scale-mapped anchor contract.

- [ ] **LINE EM nudge.** One display displacement for the path and its decorations.
- [ ] **Zero-baseline AREA EM nudge.** Translate upper boundary, baseline and decorations together.
- [ ] **Ribbon AREA EM nudge.** Translate both authored bounds and decorations together.
- [ ] **LINE BAND nudge.** Use fitted plot dimensions; prove resize behavior.
- [ ] **Zero-baseline AREA BAND nudge.** Translate the complete strip using fitted dimensions.
- [ ] **Ribbon AREA BAND nudge.** Keep the displayed interval span intact through resize.
- [ ] **LINE DATA nudge.** Map each vertex's target through its original primary scales.
- [ ] **Zero-baseline AREA DATA nudge.** Define the scalar Y anchor and move its baseline with it.
- [ ] **Ribbon AREA DATA nudge.** Use authored Y_START as the anchor for both bounds.
- [ ] **LINE deterministic JITTER.** Keep stable-key displacement consistent across adjacent connections.
- [ ] **Zero-baseline AREA deterministic JITTER.** Share each cross-section displacement with its baseline.
- [ ] **Ribbon AREA deterministic JITTER.** Share each cross-section displacement between both bounds.

**Completion evidence:** Common implementation evidence below, plus complete-path/strip and decoration
alignment, raw-value/domain invariance, missing-coordinate behavior, resize and invalid targets.
JITTER additionally proves seed/key stability and that presentation changes do not change offsets.
Transposition, CONNECT/ZERO, non-linear interpolation, series and facets are excluded from these first
increments. Add a separately bounded follow-up for one of those combinations only when selected.

#### Stacking — separate connected and physical-aspect increments

**Status:** Accepted
**Horizon:** Later
**Outcome:** Preserve stacked boundaries and source-owned styles without changing stack totals.
**Dependencies:** Existing ordinary stack resolution; agree signed/normalized domain and baseline
rules before enabling a new form. Each first increment uses nonnegative values, linear primary axes,
LINEAR interpolation, GAP, IDENTITY and no facets or secondary axes.

- [ ] **Conditioned Cartesian AREA ZERO stack.** One scalar AREA definition partitioned by categorical
  series; resolve outgoing strips after stacking. No ribbons, placement or transposition.
- [ ] **Conditioned Cartesian AREA NORMALIZE stack.** Extend the previous form with explicit zero-total
  behavior and normalized bounds. No additional geometry forms.
- [ ] **Ordinary transposed fixed-aspect AREA ZERO stack.** One scalar AREA definition with categorical
  series and continuous quantitative X/Y; preserve physical units. No conditions or placement.
- [ ] **Ordinary transposed fixed-aspect AREA NORMALIZE stack.** Extend the preceding form and prove
  normalized scale/aspect behavior. No additional marks.

**Completion evidence:** Common evidence plus exact cumulative boundaries, shared cross-section edges,
series order, zero-total cases and unchanged totals under conditions. NORMALIZE depends on its ZERO
increment. Signed stacks and stacking on other marks require separately accepted tasks.

#### Secondary axes — one geometry form at a time

**Status:** Accepted
**Horizon:** Later
**Outcome:** A connected layer uses its declared axis without borrowing another layer's scale.
**Dependencies:** Explicit resolved axis ownership and current multi-axis scale/layout contracts.
Start with a Cartesian chart containing one primary ordinary layer and one secondary conditioned
layer, linear axes, IDENTITY, GAP and LINEAR interpolation; no facets or stacking.

- [ ] **Secondary-axis conditioned LINE.** Resolve connections, symbols and captions through that layer's Y scale.
- [ ] **Secondary-axis conditioned zero-baseline AREA.** Include zero in its owning Y domain and render its strips.
- [ ] **Secondary-axis conditioned ribbon AREA.** Keep both bounds on one declared secondary scale.
- [ ] **Decide physical aspect with secondary axes.** Define which scale owns the physical unit ratio,
  how the other axis is mapped, and which combinations remain invalid.
- [ ] **Transposed fixed-aspect POINT with a secondary axis.** After that decision, implement only its
  accepted minimal point form. No connected marks, stacking, placement or intervals.

**Completion evidence:** Common evidence plus deliberately different primary/secondary domains,
unused declarations, correct axis labels and resize. The physical-aspect decision is a prerequisite
for its POINT increment; other transposed marks remain separately selectable.

#### Temporal and categorical aspect — semantics before implementations

**Status:** Exploring
**Horizon:** Later
**Outcome:** Authors can predict the physical spacing of discrete or time coordinates.
**Dependencies:** Explicit time units or category spacing, existing time/band scales and fitted layout.

- [ ] **Decide temporal physical units.** Define elapsed-time units, supported date/time kinds and
  timezone behavior using one representative report; retain rejection for unspecified combinations.
- [ ] **Transposed aspect POINT with temporal X and quantitative Y.** After that decision, implement
  one accepted time kind with IDENTITY, primary axes and no conditions, intervals, series or facets.
- [ ] **Decide categorical physical spacing.** Define what an aspect ratio means for one ordered
  category axis, including empty/singleton categories. Declining support is a valid recorded outcome.
- [ ] **Transposed aspect POINT with categorical X and quantitative Y.** Only if the spacing contract
  is accepted; one layer with IDENTITY and no conditions, intervals, series or facets.
- [ ] **Transposed aspect TICK with categorical X and quantitative Y.** Depends on the categorical
  contract; one ordinary layer with IDENTITY. Other mark/encoding forms remain rejected.

**Completion evidence:** Decision tasks record exact units/spacing and exclusions. Implementation
chunks use common evidence plus independent pixel-spacing oracles, reversal, resize, empty inputs
and the chosen temporal or category edge cases. LINE/AREA and mixed-type compositions are separate work.

#### Polar extensions — separate aspect and connected-path decisions

**Status:** Exploring
**Horizon:** Later
**Outcome:** Extend the current ARC-only polar slice only where a report establishes a concrete need.
**Dependencies:** The shipped polar/radial contract; new path marks require accepted angular
connectivity and wrap semantics before any condition support.

- [ ] **Decide polar physical aspect.** Define angular/radial units and plot fitting for an unstacked
  ARC example; decide whether a physical aspect option is useful at all.
- [ ] **One polar ARC physical-aspect form.** After acceptance, implement that exact unstacked ARC
  example with IDENTITY, no conditions, facets or custom radius bounds.
- [ ] **Decide a polar connected LINE form.** Specify base LINE geometry, angular wrap, gaps and
  source ownership. Polar currently accepts ARC only, so this is a new-mark decision first.

**Completion evidence:** Accepted decisions identify one implementable form or retain rejection.
The ARC increment uses common evidence plus independent angular/radial geometry and resize.
Polar connected LINE implementation and AREA forms are not commitments of these decisions; add
one narrowly scoped task only after the underlying geometry is accepted.

#### Geographic extensions — projection meaning before geometry expansion

**Status:** Exploring
**Horizon:** Later
**Outcome:** Make one geographic distance/aspect or route-condition behavior explicit and portable.
**Dependencies:** Existing server-resolved maps/routes and the bounded equirectangular/Mercator
projection contract. No new projection, provider or remote map-loading behavior.

- [ ] **Decide geographic physical aspect.** Distinguish projected-coordinate units from ground
  distance and specify behavior near projection limits using one representative map.
- [ ] **One geographic POINT physical-aspect form.** After acceptance, implement one existing
  projection, one point layer with IDENTITY and no conditions or facets.
- [ ] **Source-owned COLOR/OPACITY on one geographic LINE route.** Accept the route connection
  contract first, including gaps and antimeridian crossings; then implement one existing projection.
  Exclude placement, decorations, interpolation, AREA and multiple routes from the first increment.

**Completion evidence:** Decisions state the projection, units and exclusions. Implementation uses
common evidence plus an independent projection oracle and exact route endpoint/ownership checks.
Geographic AREA remains outside the currently supported mark set and requires its own design decision.

#### Logarithmic connected scales — explicitly decide whether to extend scope

**Status:** Exploring
**Horizon:** Later
**Outcome:** Decide whether a real report needs conditioned connections on logarithmic scales.
**Dependencies:** Positive-domain rules, connected null semantics and interpolation coordinate space.

- [ ] **Decide one logarithmic connected LINE form.** Compare raw versus display-space geometry and
  establish positive-value/null requirements. If accepted, create a separate task for one Cartesian
  LINE with linear X, logarithmic Y, GAP, LINEAR and IDENTITY. Do not inherit scalar AREA ZERO,
  ribbons, transposition or smooth curves as part of that decision.

**Common implementation completion evidence:** Matching Core/ChartSpec validation and explicit
negative cases for every excluded combination; authoring/designer round trips, lineage and LSP rename;
guarded wire compatibility; deterministic plan/SVG fixtures with independent geometry assertions;
browser and static PDF behavior; terminal and accessible fallback preserving semantic values;
resize/reversal and missing-data cases; and unchanged payload, render-time and bundle budgets.
Update the capability matrix and focused reference for only the accepted form. Finish that increment
before selecting the next; preserve all existing fixtures for earlier supported forms.

### Reporting & Presentation — Analytical Overlays, Authoring Parity, and Golden Conformance

**Status:** Incremental  
**Horizon:** Next (v0.23.0)  
**Scheduling:** Backlog for prioritization after v0.22.0 ships.  
**Authoritative design:** [Reporting Semantic Contracts](docs/architecture/reporting-semantic-contracts.md),
[Native Grammar-of-Graphics Contract](docs/architecture/decisions/grammar-of-graphics-spec-ir.md),
[Native Advanced Chart Authoring](docs/architecture/decisions/native-advanced-chart-authoring.md), and
[Reporting Architecture](docs/architecture/reporting.md).

**Implemented baseline:** The reporting golden harness (`ReportingGoldenHarness` and
`ReportingGoldenTests`) pins `PlanHash`, `SvgHash`, `FallbackHash`, and `TerminalHash` across 34
`.rptsql` conformance scripts in [`tests/fixtures/reporting/goldens/index.json`](tests/fixtures/reporting/goldens/index.json).
`LINEAR` and `POLYNOMIAL` regressions, `GOAL`, `AVERAGE`, `MOVING_AVG`, `FORECAST`, `REFERENCE_LINE`,
`REFERENCE_BAND`, `RUNNING_TOTAL`, `PERCENT_OF_TOTAL`, and `ANNOTATIONS (POINT ...)` are parsed and
lowered into `ChartSpec` layers on named visuals.

**Problem and intended outcome:** Reviewing the Grammar of Graphics implementation and its SLT-style
golden suite surfaced three categories of gaps where authors either get silent/distorted output or
hit an immediate feature cliff when moving from named visuals (`BAR`, `LINE`, `COMBO`) to
`CUSTOM ... CHART`:
1. **Silent no-ops and distorted overlay/domain math:**
   - `EXPONENTIAL`, `LOGARITHMIC`, and `POWER` overlays parse and lower, then hit the default wildcard
     in `PlotPlanResolver.ResolveOverlay` and silently emit all-null datums with no SVG output and no
     error.
   - `PlotPlanResolver.ResolveOverlay` coerces `NULL` Y values to `0m` before computing `AVERAGE`,
     `MOVING_AVG`, `LINEAR`, and `POLYNOMIAL`, skewing means and regression slopes toward zero.
   - On multi-series charts, `ResolveOverlay` regresses across the entire Y column without grouping by
     series or rejecting ambiguous multi-series trendlines.
   - `PolynomialRegression` raises uncentered `decimal` X values to `row + column`, which can overflow
     `decimal.MaxValue` on large X values or higher polynomial degrees.
   - When a chart combines stacked layers (`STACK = ZERO` or `NORMALIZE`) with an unstacked `LINE` or
     `POINT` layer on the same primary Y axis, `PlotPlanResolver.ResolveScales` drops `FieldChannel.Y`
     from the unstacked layers and can clip them outside the resolved Y domain.
2. **Core scale, layer, and `CUSTOM ... CHART` authoring bugs and feature cliffs:**
   - **`ScaleKind.Time` (`TYPE = TEMPORAL`) renders as discrete categories:** `PlotPlanResolver.ResolveScales`
     sorts temporal values by string `ValueKey`, populates `Categories`, and emits one tick per
     distinct timestamp (ignoring `MAJOR_TICK_COUNT` and `TICK_INTERVAL`). `PlotPlanSvgRenderer`
     therefore spaces 1-day and 1-year gaps at equal category intervals, `ResolveLayerData` drops
     duplicate timestamps via `firstRowByCategory`, and `RenderPoints` skips points when `TIME_UNIT`
     or `TICK_FORMAT` changes the category display string.
   - **Horizontal grouped bars in `CUSTOM` overlap:** `PlotPlanSvgRenderer.RenderTransposedCartesian`
     gates grouped bar dodging on `LayerStyle(l, "series") is not null`, which `NamedVisualChartLowerer`
     sets but `PlotPlanResolver` does not set when splitting a `CUSTOM` `RECT` layer by `COLOR`
     (`layer.SeriesKey`), drawing all series bars at the same Y position. `X_OFFSET`/`Y_OFFSET`
     calculations in `PlotPlanResolver` also use `520px` instead of `CartesianPlotArea` width and
     double-shift when combined with `COLOR`.
   - **`COLOR = DATUM('...')` loses multi-layer series and legends:** `AdvancedChartLowerer` collapses
     `spec.Bindings` by channel (`GroupBy(b => b.Channel).Select(g => g.First())`) and
     `PlotPlanResolver.ResolveSeries` ignores `BindingSourceKind.Datum`, so constant-series layers
     render in the same default color without legend entries.
   - **Multi-grain datasets drop summary layers:** Layers cannot specify a per-layer `WHERE` filter,
     and `ResolveLayerData` picks the first row per category even when that layer's Y column is
     `NULL` on that row, turning `UNION ALL` detail+summary layers into gaps.
   - **Named-to-`CUSTOM` feature cliff:** `ReportParser` rejects `OVERLAYS` on `CUSTOM` visuals;
     `AdvancedChartSemanticValidator.ValidateConditions` rejects `CONDITIONS` on `LINE` and `AREA`
     layers unless `X` is `QUANTITATIVE` and `STYLE (INTERPOLATION = ...)` is explicitly set;
     `AdvancedChartLowerer` silently ignores outer `AXIS_X`/`AXIS_Y` scale options while `SCALES`
     lacks a `TITLE`/`LABEL` option; `VisualBuilder` silently ignores `SEGMENT_STYLE` on `CUSTOM`
     while `CONDITIONS` has no `LINE_DASH` channel; and `SCALES`/`ENCODE` lack discrete color maps,
     `SIZE` `RANGE = (min, max)`, and a data-driven `OPACITY` channel.
   - **Studio and runtime gaps on `CUSTOM`:** `visual-format-inspector.ts` omits `'CUSTOM'` from
     `FORMAT_INSPECTOR_CHARTS` and `FORMAT_INSPECTOR_CARTESIAN` (hiding legend, axis, label, and
     palette controls); `designer-interactions.ts` inspects `v.mappings` instead of `CUSTOM`
     encodings (breaking Studio cross-filter detection and hover-popover validation); and
     `VisualBuilder` `DRILL_IN` rewrites `Mappings` without updating `AdvancedChart` encodings.
3. **Golden harness and coverage gaps:**
   - `VisualBuilder.BuildResolvedAsync` catches all lowering/resolution/render exceptions into
     `vm.Error` while `ReportingGoldenHarness.ProduceAsync` does not assert `vm.Error is null`,
     allowing broken visuals to record `null` plan/SVG hashes during golden regeneration;
     `SvgChartRenderer.Render` retains legacy fallback paths when `PlotPlan` is `null`; and
     `NativeSvgGeometryGoldenTests` / `GrammarOfGraphicsContractTests` pin raw SHA-256 strings
     without checked-in artifact files.

The outcome is fail-closed overlay and scale semantics, continuous temporal axes, working grouped bars
and multi-layer legends in `CUSTOM ... CHART`, feature parity with named visuals and Studio, and
expanded golden conformance fixtures.

**Why next:** Identified during the v0.22.0 Grammar of Graphics review (2026-11-10). Held out of
v0.22.0 to avoid destabilizing the active release candidate, and prioritized ahead of the niche
combinatorial matrix in [Grammar-of-Graphics Semantic Extensions](#reporting--presentation--grammar-of-graphics-semantic-extensions).

**Boundaries:** Preserve the existing `ChartSpec`, `PlotPlan`, and four-artifact golden harness
contracts. Do not add in-engine histogram binning (keep `X_START`/`X_END` SQL-computed) or new
third-party math libraries.

**Dependencies:** `ReportParser`, `AdvancedChartSemanticValidator`, `AdvancedChartLowerer`,
`NamedVisualChartLowerer`, `PlotPlanResolver`, `PlotPlanSvgRenderer`, `VisualBuilder`,
`ReportingGoldenHarness`, and the Studio designer inspector modules.

**Planning:** Use the [backlog sizing and readiness rules](#sprint-planning-for-backlog-tables).
Select individual IDs into `TODO.md` when scheduling.

#### Fail-closed overlay semantics and multi-layer domain resolution

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| GG01 | **Decide or implement `EXPONENTIAL`, `LOGARITHMIC`, and `POWER` trendlines.** Either implement exact least-squares fitting in `PlotPlanResolver.ResolveOverlay` with domain guards (rejecting non-positive Y for `EXPONENTIAL`/`POWER` and non-positive X for `LOGARITHMIC`/`POWER`) or reject them at parse/lowering time until implemented. Remove the silent all-null fallback. | — | S |
| GG02 | **Exclude `NULL` Y pairs from statistical overlays.** Stop coercing `NULL` to `0m` in `PlotPlanResolver.ResolveOverlay` for `AVERAGE`, `MOVING_AVG`, `LINEAR`, and `POLYNOMIAL`. Skip null `(x, y)` pairs in regressions and averages, and define deterministic window behavior for `MOVING_AVG` over gaps. | — | S |
| GG03 | **Define multi-series trendline ownership.** Either partition overlay computation per series (or bind to an explicit target series) or fail validation when a statistical trendline is authored on a multi-series visual without a target series. | GG02 | S |
| GG04 | **Center/scale X in `PolynomialRegression` and guard singular/overflow cases.** Normalize X into `[-1, 1]` (or `[0, 1]`) before building the Vandermonde sums so large X domains (e.g., epoch dates or revenue) do not overflow `decimal.MaxValue`, and fail or emit a diagnostic when degree exceeds distinct X points. | GG02 | S |
| GG05 | **Preserve unstacked `FieldChannel.Y` in mixed stacked/unstacked Y domains.** Include `FieldChannel.Y` alongside `YStart`/`YEnd`/`ErrorLow`/`ErrorHigh`/`ConfidenceLow`/`ConfidenceHigh` in `PlotPlanResolver.ResolveScales` when stacked and unstacked layers share a primary or secondary Y axis; include explicit `AnnotationPoint` coordinates in scale domain resolution. | — | S |

#### Core scale, layer, and `CUSTOM ... CHART` authoring parity

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| GG11 | **Fix `ScaleKind.Time` (`TYPE = TEMPORAL`) to resolve and render as a continuous time axis.** Order temporal values chronologically rather than by string key, compute proportional time positions and nice ticks honoring `MIN`/`MAX`/`MAJOR_TICK_COUNT`/`TICK_INTERVAL` without populating discrete `Categories`, keep multiple rows sharing a timestamp in `ResolveLayerData`, and fix `RenderPoints` when `TIME_UNIT` or `TICK_FORMAT` is set. | — | M |
| GG12 | **Fix horizontal grouped bar dodging (`TRANSPOSED_CARTESIAN`) and `X_OFFSET`/`Y_OFFSET` geometry in `CUSTOM ... CHART`.** Gate grouped dodging in `PlotPlanSvgRenderer.RenderTransposedCartesian` on `layer.SeriesKey is not null` as well as `LayerStyle(l, "series")`, align `PlotPlanResolver` offset bounds with `CartesianPlotArea`, and prevent double-dodging or row deduplication when `X_OFFSET`/`Y_OFFSET` is bound. | — | S |
| GG13 | **Support `COLOR = DATUM('...')` multi-layer legends and non-null category row selection.** Preserve per-layer `COLOR` `DATUM` bindings in `AdvancedChartLowerer` and `PlotPlanResolver.ResolveSeries` so constant-series layers receive distinct palette colors and legend entries; in `PlotPlanResolver.ResolveLayerData`, select the first row per category where the layer's measure is non-null (or support a per-layer `WHERE` filter) so combined detail+summary tables do not drop summary layers. | — | M |
| GG14 | **Support analytical overlays and categorical/temporal `LINE`/`AREA` `CONDITIONS` in `CUSTOM ... CHART`.** Allow `OVERLAYS` (or a `CHART`-level equivalent) on `CUSTOM` visuals, relax `AdvancedChartSemanticValidator.ValidateConditions` so `LINE`/`AREA` layers on categorical or temporal X axes can use `CONDITIONS` without requiring quantitative X or an explicit `INTERPOLATION` token, and support `LINE_DASH` / `SEGMENT_STYLE`. | GG01, GG02 | M |
| GG15 | **Close scale and encoding gaps in `CHART (SCALES ... / ENCODE ...)`.** Support discrete domain-to-color mapping on `NOMINAL`/`ORDINAL` `COLOR` scales, `RANGE = (min, max)` on `SIZE` scales, a data-driven `OPACITY` channel in `ENCODE`, and axis `TITLE`/`LABEL` inside `SCALES(...)`; either lower or explicitly reject outer `AXIS_X`/`AXIS_Y` and named-visual-only `OPTIONS` on `CUSTOM` visuals. | — | M |
| GG16 | **Studio inspector, cross-filter, hover-popover, and `DRILL_IN` parity for `CUSTOM` visuals.** Include `'CUSTOM'` in `FORMAT_INSPECTOR_CHARTS` and `FORMAT_INSPECTOR_CARTESIAN` in `visual-format-inspector.ts`, derive `selectionKey` and `hoverContextColumn` from `CUSTOM` encodings in `designer-interactions.ts`, and update `VisualBuilder` `DRILL_IN` hierarchy rewriting to rebind `AdvancedChart` encodings. | — | S |

#### Harness fail-fast guarantees and golden SVG hash coverage

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| GG06 | **Fail closed in `ReportingGoldenHarness` and retire legacy `SvgChartRenderer` fallbacks.** Assert `visual.Error is null` in `ReportingGoldenHarness.ProduceAsync` so golden updates never record `null` artifacts for failed visuals; replace legacy `PlotPlan is null` fallback paths in `SvgChartRenderer.Render` with an explicit error for plot-plan-backed visual types; back `NativeSvgGeometryGoldenTests` and `GrammarOfGraphicsContractTests` with diffable artifact snapshots. | — | S |
| GG07 | **Add golden conformance fixtures for analytical overlays.** Add `.rptsql` fixtures and four-artifact goldens in `index.json` for `LINEAR`, `POLYNOMIAL`, `FORECAST` (with confidence band and anomaly points), `REFERENCE_LINE`, `REFERENCE_BAND`, `RUNNING_TOTAL`, `PERCENT_OF_TOTAL`, and `ANNOTATIONS (POINT ...)`. | GG01, GG02, GG04 | M |
| GG08 | **Add golden conformance fixtures for error bars, confidence bands, and connected interpolations.** Pin `.plan.json`, `.svg`, `.fallback.json`, and `terminal.txt` goldens for `ERROR_LOW`/`ERROR_HIGH`, `CONFIDENCE_LOW`/`CONFIDENCE_HIGH`, `INTERPOLATION = SMOOTH`/`STEP_BEFORE`/`STEP_AFTER`, and multi-color conditional line transitions. | — | M |
| GG09 | **Add golden conformance fixtures for `LOG`/`IDENTITY` scales, 2D facets, `INDEPENDENT` resolution, `NULL_POLICY`, and `CUSTOM` polar arcs.** Add `.rptsql` golden fixtures covering `TYPE = LOG`, `TYPE = IDENTITY`, `FACET (ROW = ..., COLUMN = ...)`, `RESOLVE (Y = INDEPENDENT)`, `NULL_POLICY = ZERO`/`CONNECT`/`SKIP`, and a `CUSTOM` `COORDINATE POLAR` `ARC` chart. | — | M |
| GG10 | **Declarative negative conformance corpus for reporting/GoG.** Add an SLT-style `statement error` script runner (or negative `.rptsql` fixture lane) that verifies expected parse, semantic-validator, and domain errors directly from `.rptsql` test scripts. | GG06 | M |

**Acceptance evidence:** Zero silent null-layer fallbacks in `PlotPlanResolver.ResolveOverlay`;
verified regression accuracy with `NULL` values and large X domains; unclipped mixed stacked/unstacked
Y domains; proportional continuous `ScaleKind.Time` spacing; dodged horizontal grouped bars and
multi-layer `DATUM` legends in `CUSTOM ... CHART`; Studio inspector and interaction parity on `CUSTOM`
visuals; `ReportingGoldenTests` passing with the expanded `index.json` fixtures; and `DocSanityTests`
passing cleanly.

### Presentation & Workspaces — Studio Authoring for Mobile Page Layouts

**Status:** Exploring
**Horizon:** Later
**Authoritative design:** [Page reference — `MOBILE_LAYOUT`](docs/reference/visuals-reporting/report/page.md)

The dialect already supports a separate small-screen layout per page:
`MOBILE_LAYOUT (STRUCTURE = ..., MAP (...), BREAKPOINT = <px>)` is parsed, compiled into the
manifest, and applied by the browser runtime below the breakpoint. Studio has no controls for it.
An author has to write the clause by hand and cannot see the result without resizing a browser.

**Why later:** Deferred on 2026-09-29 after the v0.20.0 dashboard audit. Analytics consumers work on
desktops, and deliberate mobile use of dashboards is rare. The script form covers the occasional
need, so a guided editor is not worth building ahead of demand.

**Boundaries:** Authoring only. No change to the `MOBILE_LAYOUT` grammar, the runtime breakpoint
behaviour, or native mobile applications. A hand-written clause must keep surviving designer edits
unchanged, as it does today.

**Dependencies:** The Studio page layout editor and the lossless designer patcher.

**Delivery slices:** (1) A mobile preview of the page at the declared breakpoint. (2) A guided editor
for the mobile grid that writes `STRUCTURE`/`MAP` from the desktop page's visuals. (3) Breakpoint
control and a warning for visuals the mobile layout leaves out.

**Acceptance evidence:** Designer round trips for pages with and without `MOBILE_LAYOUT`, and a
browser journey that authors a mobile layout and checks the rendered page at a narrow viewport.

### Presentation & Workspaces — ETL-SQL Studio (Report Studio, Script Editor, and Pipeline Studio)

**Status:** Incremental
**Horizon:** Next
**Scheduling:** Backlog for the next sprint; remaining Alpha work is outside the current sprint.
**Authoritative design:** [ETL-SQL Studio](docs/architecture/decisions/etl-sql-studio.md) and
[Portal ETL documents](docs/architecture/decisions/portal-etl-documents.md).

Studio shipped in v0.19.0 as an Alpha. It mounts in Portal and the self-installed WorkstationEditor
host, with the UI sandbox providing development coverage. Canvas ELSE authoring, task-field editing,
PARALLEL swimlanes, guided transforms, dashboard interactions, paginated bands and host-side recovery
drafts are implemented. v0.21.0 shipped Portal pipeline run, schedule, export/import and promotion
(ST04–ST11), private drafts (ST12–ST15), the beginner, reader, RLS and practice journeys (ST16–ST22,
ST21b), and the keyboard and zoom journeys (ST23–ST27). Those rows are retired. The remaining scope
is the backlog below; shipped details remain in `CHANGELOG.md` and the linked implementation contracts.

**Problem and intended outcome:** Studio helps users build ETL pipelines, reports and dashboards
while learning ETL-SQL. Visual helpers lead into confident script editing and remain available when
an author gets stuck. Support both production hosts without requiring VS Code or a TUI fallback,
including ordinary capability-scoped authors and readers. Learning must not require publishing or
administration rights.

**Why next:** Resume the remaining authoring, recovery and certification work next sprint. Completeness
is measured by successful report and ETL journeys across both hosts, with visual helpers and code
editing working together. Once Studio is feature complete, it replaces the Portal's older designer
(`designer.html`); the owner confirmed this direction on 2026-11-04, and the timing is a separate
future decision. Mobile authoring
retains its separate Later horizon.

**Boundaries:** Preserve standard `.rptsql` and `.etlsql` scripts as the source of truth, surgical
parser/patcher edits, per-document state, caller identity and row-level security, bounded previews,
and policy-governed host drafts. Keep native SQL an explicitly labelled advanced escape hatch.
Do not add a vendor SQL builder, proprietary project format or browser-side secret persistence.

**Dependencies:** Shared typed authoring contracts, designer parser/patcher, CodeMirror workbench,
connection/schema discovery, report runtime, Portal catalog capabilities and leases, recovery drafts,
and Orchestrator execution and scheduling.

**Planning:** Use the [backlog sizing and readiness rules](#sprint-planning-for-backlog-tables).
Select individual IDs when this initiative is scheduled; task sizes exclude external waits.

#### Unified authoring experience and completeness criteria

Studio combines ReportBuilder visual authoring and WorkstationEditor code editing into one complete
authoring experience across desktop and Portal. Users familiar with Power BI, Tableau, SSRS, and SSIS
should be able to build useful reports and pipelines while learning the generated SQL. SQL remains
the authoritative artifact; visual helpers preserve hand edits. Host differences follow local files,
permissions, and execution boundaries. These components are foundations of Studio; retiring their
entry points is a separate future decision, not the measure of success.

**Working inventory:** [Studio authoring completeness](docs/architecture/decisions/studio-authoring-completeness.md).

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| ST03 | **Unified beginner-experience acceptance — assessed, not yet accepted.** [Assessment, gaps and host limits](docs/architecture/decisions/studio-beginner-acceptance-assessment.md); Portal ETL, paginated and learner-observation evidence remain open. Assess completed report and ETL journeys, helper/code transitions without lost work, and the learner’s understanding of generated SQL. Record remaining gaps and host-specific limits. | ST01, ST02; required journey evidence | S |

#### Author, reader and beginner practice journeys

ST16/ST17 automated report sequences passed on 2026-10-11; the learner's own explanation is ST16b.
See [beginner report evidence](docs/architecture/decisions/studio-beginner-report-evidence.md).

ST16b is deferred, not scheduled: it waits on a person, not on engineering. Run it when someone new to
Studio is available — familiarity with SQL or another BI tool is fine. Until then no release claims a
learner-validated experience.

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| ST16b | **Observed learner walkthrough — deferred until a learner is available.** One learner new to Studio does the report sequence and the ETL exercise in each host while an observer records, per step, what they did unaided and how they explained the generated SQL and the repair. Needs a person; automation cannot close it. [Script and record sheet](docs/architecture/decisions/studio-learner-walkthrough.md), not yet run. | ST16, ST17, ST21, ST22 | S |

#### Keyboard and assistive technology

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| ST28 | **Portal screen-reader pass — deferred until needed (owner, 2026-10-17).** Needs a real screen reader; none is installed. [Groundwork and pass sheet](docs/architecture/decisions/studio-keyboard-assistive-evidence.md#screen-reader-pass-sheet-st28-st29). One named screen reader/browser combination completes the keyboard document journey with meaningful labels, errors and announcements; keep the observed transcript. | ST23 | M |
| ST29 | **Desktop screen-reader pass — deferred until needed (owner, 2026-10-17).** Record the same bounded journey in one supported desktop/browser combination. | ST24 | M |

#### Execution scope, cancellation and bounded helper previews

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| ST30 | **Portal execution-scope journey.** Exercise no selection, unresolved statement and mutating-prefix consent refused/accepted; actual statements and displayed scope agree under the permitted execution path. | ST04 when pipeline submission is used | M |
| ST31 | **Desktop execution-scope journey.** Prove the same cases against the desktop host, using real execution responses. | — | M |
| ST32 | **Portal cancellation and lost response.** Confirm server cancellation and one lost-response case; retain the run ID for support and distinguish unknown outcome from stopped work. | ST30 | M |
| ST33 | **Desktop cancellation and lost response.** Prove the same bounded failure cases and diagnostic ownership on the desktop host. | ST31 | M |
| ST34 | **Portal native-dialect helper preview.** One named connector/dialect fixture executes a bounded read-only native task and accepts an empty result without running arbitrary predecessors. | — | M |
| ST35 | **Desktop native-dialect helper preview.** Run that same fixture through the desktop preview path. | ST34 fixture contract | M |
| ST36 | **Portal staged-context helper preview.** One staged-data task and required variable preview correctly; unavailable predecessor context is explained and never silently executed. | — | M |
| ST37 | **Desktop staged-context helper preview.** Prove the same variable/staging and unavailable-context cases on the desktop host. | ST36 fixture contract | M |

#### Degraded editor and failed session recovery

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| ST38 | **Portal degraded-editor recovery.** Force a module-load failure, type and switch two tabs, then retry successfully; buffers survive and unsupported actions are disabled or explained. | — | M |
| ST39 | **Desktop degraded-editor recovery.** Prove the same failure/retry sequence in the desktop host. | — | M |
| ST40 | **Portal session startup timeout/retry.** Bound an unanswered session request and cover HTTP 500/disconnect; display an actionable error and recover on Retry without granting fallback capabilities. | — | M |
| ST41 | **Portal expired-session retry.** Cover access-token expiry and failed refresh during startup; re-authentication and retry restore the correct identity/capabilities and kept drafts. No new draft storage. | ST40 | M |

#### Delivery budgets, catalog scale and large documents

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| ST54 | **Measure and decide Studio transfer budgets.** Inventory the full production import closure, designer assets and CodeMirror bundle; record raw/gzip baselines, accepted ceilings and the gate's implementation estimate. No asset optimization or gate implementation. | — | D |
| ST42 | **Studio transfer-size gate.** Enforce the accepted raw/gzip ceilings for that import closure, with a negative oversized fixture. Preserve existing timing gates; new optimization work gets separate estimates. | ST54 | M |
| ST43 | **Portal cold-load evidence.** Measure first usable editor on a declared uncached Portal fixture, record the environment and set a defensible regression ceiling. No broad performance optimization. | ST42 | M |
| ST44 | **Desktop cold-load evidence.** Measure the matching desktop fixture with its own host ceiling. | ST42 | M |
| ST45 | **Paged Portal catalog discovery.** One bounded server-side search/paging path feeds Home while preserving folder/tenant visibility; verify a declared large-catalog fixture. No real-time synchronization. | — | M |
| ST46 | **Fresh catalog and stale-open recovery.** Refresh the paged list after external create/rename/move/delete; a stale deleted/moved selection fails clearly without disturbing dirty buffers. | ST45 | M |
| ST53 | **Measure and decide the large-document contract.** Pin one several-thousand-line Portal fixture and the existing server byte limit; measure editing/analysis, define client limit/degraded behavior and estimate implementation. No editor changes. | — | D |
| ST47 | **Portal large-document limits.** Implement the accepted client ceiling/degraded behavior on that fixture and verify the server-limit boundary without losing text. No general editor rewrite. | ST53 | M |
| ST48 | **Desktop large-document contract.** Measure the same bounded document in the desktop host and verify its limit/degraded behavior. | ST47 fixture contract | M |
| ST49 | **Portal analysis throttling recovery.** One controlled designer-rate-limit rejection preserves the buffer, explains stale diagnostics and resumes analysis after permitted retry; no arbitrary predecessor execution. | ST47 | M |

#### Release-versioned Studio guidance

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| ST50 | **Beginner report guide.** Document the certified report exercise, code/helper round trips and both host entry points, referring to release versions. | ST16, ST17 | S |
| ST51 | **Pipeline run/schedule guide.** Document the certified pipeline flow, grants, status/log/Stop and missing-Orchestrator behavior. | ST07, ST10 | S |
| ST52 | **Draft, publishing and recovery guide.** Explain kept versus unavailable drafts, private/public permissions, conflicts and retry/support diagnostics; include desktop policy limits. | ST14, ST15, ST40, ST41 | S |

#### Small pickup options for sprint planning

These are selectable outcomes, not committed sprints. The ranges sum the initial task sizes for one
developer and exclude external waits. Re-estimate and split again if the selected fixture exposes
more work; do not fill a sprint by assuming every M task takes three days.

| Outcome | Select first | Initial engineering range | Follow-on work |
| :--- | :--- | :--- | :--- |
| Set measurable delivery limits | ST53, ST54 | 2–4 days | Schedule ST47 and ST42 independently after the decisions. |

**Separate Later backlog:** [Mobile page-layout authoring](#presentation--workspaces--studio-authoring-for-mobile-page-layouts)
retains its own horizon. Its preview, grid editor and breakpoint/omitted-visual checks are three
separately selectable increments outside the remaining Alpha backlog.

**Shared completion evidence:** Each selected journey uses the named production host and intended
identity, real parser/patcher/execution responses, exact saved/reopened bytes and expected output.
Add focused regression evidence for defects found within that task. Preserve existing typed contracts,
asset sync, lint/type gates, isolation, draft policy and performance ceilings. Sandbox/source-pattern
checks alone do not certify a production-host journey. The ST03 assessment identifies which task IDs
must complete before the unified beginner experience can be accepted.

---

## Platform, SaaS, and Governance

### SaaS Isolation — Tenant-Scoped Orchestrator Bundle Store

**Status:** Accepted
**Horizon:** Next (v0.23.0)
**Authoritative design:** [SaaS Tenant Isolation Architecture](docs/architecture/saas-tenant-isolation.md)

**Problem and Intended Outcome:** The `bundles` and `bundle_files` tables key on
`(tenant_id, bundle_name, version)`, but no bundle store method binds or filters by tenant. Every
publish writes to `'default'` with `INSERT OR REPLACE`, and every read matches on bundle name
across tenants. On a shared Orchestrator one tenant can overwrite or read another tenant's bundle.
The outcome is that each tenant publishes, lists, reads and deletes only its own bundles, and two
tenants can use the same bundle name.

**Why next:** Found in the v0.22.0 security review (TODO.md §6). The fix spans Core, the Engine
bundle handlers, Orchestrator.Service and the LanguageServer, too wide for v0.22.0. Until it ships,
publishing takes Admin or OrchestratorManager and reading takes a role, but any tenant's caller
still sees every tenant's bundles, so Shared hosts must not claim bundle isolation.

**Boundaries:** Bundle contents and versioning stay as they are. Promotion packages keep their
own tenant binding.

**Dependencies:** `TenantContext` on every Orchestrator request path.

**Slices:**

1. A tenant-taking store contract (for example `ITenantBundleStore(TenantContext)`), with the
   SQLite and Postgres stores filtering every query by tenant.
2. Engine `PUBLISH`/`RUN SCRIPT 'orch://…'` handlers, the Service endpoints and the LanguageServer
   resolve bundles through the caller's tenant.
3. A migration that moves existing `'default'` rows to their owning tenant, or leaves them for an
   operator to reassign.

**Acceptance evidence:** A two-tenant test where tenant B can neither read nor overwrite tenant A's
bundle of the same name, on SQLite and Postgres.

### Security — Caller Identity for Process-Spawned Orchestrator Runs

**Status:** Accepted
**Horizon:** Next (v0.23.0)

**Problem and Intended Outcome:** With `Jobs:UseProcessSpawning`, the Orchestrator runs a job in a
child `etl-sql` process. The child gets no caller identity or tenant and has no
`IOrchestratorObjectAuthorizer`, so v0.22.0 refuses identity-bound runs in this mode (fail closed).
The outcome is that a spawned run executes as its caller, with the same identity, tenant, RLS and
per-object checks as an in-process run.

**Why next:** Found while verifying the v0.22.0 security review. Passing identity on the command
line or in plain environment variables would let any local user start the App as someone else, so
the fix needs a tamper-evident hand-off, too much for v0.22.0.

**Boundaries:** In-process runs are unchanged. Runs without an identity keep working in spawn mode.

**Dependencies:** The Orchestrator identity secret (already used to sign the job channel).

**Slices:**

1. A signed identity envelope (identity, tenant, scopes, job id, expiry; HMAC with the Orchestrator
   identity secret), written to a private temp file the child reads once and deletes.
2. The child App verifies the envelope, rejects expired or replayed ones, and binds the identity and
   tenant before the script runs.
3. An authorizer in the child that applies the same per-object checks, or calls back to the
   Orchestrator for them.

**Acceptance evidence:** A spawn-mode test where a run executes as its caller and is refused an
object the caller cannot use; and a test where a forged, altered or expired envelope is refused.

### Portal, Orchestrator & Studio — Unified Operations Console, Catalog-Job Cohesion, and SaaS Authoring Boundary

**Status:** Incremental  
**Horizon:** Next (v0.23.0)  
**Scheduling:** Backlog for prioritization after v0.22.0 ships.  
**Authoritative design:** [Portal Architecture](docs/architecture/portal.md),
[Orchestrator Architecture](docs/architecture/orchestrator.md),
[Portal UI Architecture](docs/architecture/portal-ui.md),
[Deployment Profiles](docs/architecture/deployment-profiles.md),
[ETL-SQL Studio](docs/architecture/decisions/etl-sql-studio.md), and
[Portal ETL Documents](docs/architecture/decisions/portal-etl-documents.md).

**Implemented baseline:** `ETL-SQL.Portal` proxies authenticated job, schedule, notification,
bundle, watermark, and per-object ACL management to `ETL-SQL.Orchestrator.Service` through
`OrchestratorController` and `OrchestratorProxyService` using `X-Orchestrator-Key` plus short-lived
HMAC-SHA256 `X-Orchestrator-Identity` caller assertions. Portal Studio (`/studio.html`) authors both
`Report` (`.rptsql`) and `Pipeline` (`.etlsql`) catalog documents and submits saved pipeline
revisions and schedules to `PIPELINE:{id}` run-slots via `StudioPipelineRunsController`.

**Problem and intended outcome:** Reviewing the end-to-end design across workstation editors (TUI,
VS Code, Desktop Studio), Portal Studio, and Orchestrator (2026-11-10) identified concrete bugs,
split-brain state between Portal and Orchestrator, and missing operator controls that make the
Portal Orchestrator experience feel disconnected from Reporting and Studio:
1. **Broken bundle URIs, mislabeled job types, and empty dependency graphs in `orchestrator.html`:**
   - `readBundleChoices` in `orchestrator-create-job.ts` emits `bundle://${name}/latest`, whereas
     `BundleUri.TryParse` (`BundleUri.cs`) and `PinBundleTargetAsync` (`JobApiEndpoints.cs`) require
     `orch://<bundle>[@<version>]/<entryPath>`. Selecting a published bundle in the Portal New Job
     modal fails `BundleUri.TryParse`, writes `RUN SCRIPT 'bundle://.../latest';` as a filesystem
     path, and fails at runtime.
   - `orchestrator-job-detail.ts` checks `value.jobType === 1 || (value.targetPath ?? '').startsWith('bundle://')`
     and labels the job `'Bundle'`, even though `JobTargetKind` `1` is `Report` and bundle URIs use
     `orch://`. `orchestrator-create-job.ts` also hardcodes `jobType: 'Script'`.
   - `OrchestratorController.GetJobDependencies` runs `scriptDag.Project(job.Script)` without
     following `RUN SCRIPT '<targetPath>';` on normalized jobs, rendering an empty Dependency Chain
     graph with zero table reads/writes or upstream/downstream jobs.
2. **Split-brain between Studio catalog pipelines (`PIPELINE:{id}`) and Orchestrator jobs:**
   - Pipelines authored in Portal Studio appear in `orchestrator.html` under raw `PIPELINE:{id}`
     names with no link to the catalog document, folder, or Studio.
   - Editing a `PIPELINE:{id}` script inside `orchestrator-job-detail.ts`'s inline CodeMirror editor
     overwrites `Jobs.Script` in the Orchestrator without updating the Portal catalog document that
     Studio owns, causing silent drift until the next Studio schedule save overwrites it.
   - Non-admin Studio authors who click **Run** on a new pipeline before an operator has run it once
     receive `403 job_not_provisioned`, and even after an admin provisions it the admin owns the
     slot until `EXECUTE` is manually granted on `PIPELINE:{id}`.
3. **SaaS / `CatalogOnly` authoring lockdown bypasses:**
   - When `Portal:Studio:Mode = CatalogOnly`, `ScriptsController` and `ReportsController` block raw
     server file uploads and path browsing, but `orchestrator.html`'s New Job modal and
     `OrchestratorController.CreateJob` still allow browsing server filesystem paths
     (`/api/orchestrator/scripts`) or saving arbitrary inline scripts—bypassing Studio edit leases,
     `CredentialLeakRule`, and `RequireApprovalToPublish`.
   - `ReportsController` script mutation routes used by `PortalDataSource` (`EXECUTE portal BEGIN
     PUBLISH REPORT ... END`) check role/folder permissions without enforcing
     `StudioCapabilities.ScriptIngress`.
4. **Split-brain storage when Portal and Orchestrator run on separate hosts:**
   - While `OrchestratorController` proxies CRUD over HTTP, `OperationsTriageService` (the Triage
     board), `OrchestratorPollerService` (linked dataset refreshes and subscription triggers), and
     `SubscriptionsController` (creating `sub_{id}` jobs) open a local SQLite database via
     `OrchestratorDbLocator.Resolve()` or `PortalTenantJobEvidenceStore`.
   - Portal Admin (`/admin.html`) manages `SHARED:` connections and `SECRET:` values in Portal's
     store, while Orchestrator exposes a separate `/api/connections` and `/api/secrets` store.
5. **Missing operator UI controls and `ORCHESTRATOR` connector parity:**
   - `orchestrator.html` has no Edit UI for existing job metadata (`PUT /api/orchestrator/jobs/{name}`
     is only used for inline script text), no Edit or Enable/Disable toggle for existing named
     Schedules or Notifications, and no Access (`READ`/`EXECUTE`/`OVERRIDE`/`MANAGE`) UI for
     Schedules or Notifications.
   - Clicking a run row in the Job Detail History tab does not open the per-statement execution
     waterfall (`GET /api/job-runs/{historyId}`), and the main job view lacks `Run with Parameters...`
     (`Variables` overrides), `Kill / Cancel Run`, and multi-node `Nodes` / sandbox-pool visibility.
   - `OrchestratorDataSource` (`EXECUTE orch BEGIN ... END`) throws `NotSupported` for `DROP JOB`,
     `KILL JOB`, `CREATE/ALTER/DROP NOTIFICATION`, `ALTER/DROP SCHEDULE`,
     `ALTER JOB ... ADD/REMOVE NOTIFICATION`, `SHOW JOB STATE`, and `ASSERT JOB`, while local
     `TRIGGER JOB <name>` without `AT <conn>` logs a message without queueing execution.

The outcome is a unified Portal + Studio + Orchestrator experience where catalog pipelines and
Orchestrator jobs share identity and governance, `CatalogOnly` SaaS lockdown has no backdoors,
Portal and Orchestrator operate cleanly across separate hosts over HTTP, and operators can manage
the full Orchestrator lifecycle from the Portal UI or `EXECUTE orch` scripts.

**Why next:** Identified during the v0.22.0 architecture and UX review (2026-11-10). Held out of
v0.22.0 to preserve the release freeze and scheduled for v0.23.0 alongside the Studio and SaaS
tracks.

**Boundaries:** Keep `ETL-SQL.Orchestrator` operable standalone via CLI and `.etlsql` scripts in
Solo/Team profiles. Do not execute mutating ETL pipelines inside the Portal web process.

**Dependencies:** `OrchestratorController`, `OrchestratorProxyService`, `StudioPipelineRunsController`,
`JobApiEndpoints`, `OrchestratorDataSource`, and the TypeScript modules under
`src/ETL-SQL.Portal/BrowserSources/orchestrator*.ts`.

**Planning:** Use the [backlog sizing and readiness rules](#sprint-planning-for-backlog-tables).
Select individual IDs into `TODO.md` when scheduling.

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| OR01 | **Fix Portal New Job bundle URIs (`orch://`) and target kind labeling.** Update `readBundleChoices` (`orchestrator-create-job.ts`) and the New Job modal to emit canonical `orch://<bundle>@<version>/<entryPath>` URIs (with version and entry-script selection) matching `BundleUri.TryParse`; support `JobType = REPORT` in New Job creation; and fix `orchestrator-job-detail.ts` so `jobType === 1` displays `'Report'` while `orch://` targets display `'Bundle'`. v0.22.0 removed the bundle target from the dialog until this lands. | — | S |
| OR02 | **Unify Studio catalog pipelines (`PIPELINE:{id}`) with Orchestrator jobs.** Enrich `PIPELINE:{id}` rows in the Portal Orchestrator UI with catalog document display names, folder badges, and an **Open in Studio** link; replace raw inline `Jobs.Script` editing on catalog-backed jobs in `orchestrator-job-detail.ts` with Studio navigation so catalog and Orchestrator scripts cannot diverge; and auto-provision (or provide a 1-click provision + author `EXECUTE` grant workflow for) `PIPELINE:{id}` slots so non-admin Studio authors do not hit a dead-end `403 job_not_provisioned`. | — | M |
| OR03 | **Enforce `CatalogOnly` / SaaS Portal-only authoring across Orchestrator and Report ingress.** When `Portal:Studio:Mode = CatalogOnly` (or external script ingress is disabled), disable raw server-filesystem script browsing (`/api/orchestrator/scripts`) and unreviewed inline script creation in `orchestrator.html` / `OrchestratorController.CreateJob` in favor of selecting a governed catalog `Pipeline` or signed `orch://` bundle; enforce `StudioCapabilities.ScriptIngress` on `ReportsController` script upload and mutation endpoints used by `PortalDataSource`. | OR02 | M |
| OR04 | **Retire direct SQLite file coupling (`OrchestratorDbLocator`) in Triage, Poller, and Subscriptions.** Route `OperationsTriageService`, `OrchestratorPollerService`, and `SubscriptionsController` through `OrchestratorProxyService` (or a shared remote store contract) instead of opening a local SQLite path via `OrchestratorDbLocator.Resolve()`, and synchronize or federate Portal's `SHARED:` connection catalog and `SECRET:` store with remote Orchestrator workers. | — | M |
| OR05 | **Fix Dependency Chain graph projection for normalized and bundle jobs.** Update `OrchestratorController.GetJobDependencies` so normalized jobs (`RUN SCRIPT '<targetPath>';`) resolve and parse their referenced script or `orch://` bundle entry and include explicit job dependencies when computing upstream/downstream table and job edges. | OR01 | S |
| OR06 | **Add Edit dialogs and per-object ACL management for Jobs, Schedules, and Notifications.** Add an Edit Job metadata dialog (`PUT /api/orchestrator/jobs/{name}` with `If-Match` for target, interval, retries, retry delay, timeout, hash policy, and sandbox profile), Edit and Enable/Disable actions on the Schedules and Notifications tabs (`PUT /api/orchestrator/schedules/{name}` and `PUT /api/orchestrator/notifications/{name}`), and mount the Access (`READ`/`EXECUTE`/`OVERRIDE`/`MANAGE` + owner) panel for Schedules and Notifications as well as Jobs. | — | M |
| OR07 | **Expose statement waterfalls, parameterized triggers, kill/cancel, and node/sandbox fleet health in `orchestrator.html`.** Make run rows in the Job Detail History tab clickable to open the per-statement execution waterfall and output (`GET /api/job-runs/{historyId}`); add `Run with Parameters...` (`Variables` overrides) and `Kill / Cancel Run` (`/kill` and `/cancel`) to the job table and detail drawer; and add a Nodes & Sandboxes view surfacing `NodeHeartbeatService` metrics and `RelationalSandboxAdmissionLedger` pool states. | — | M |
| OR08 | **Complete `ORCHESTRATOR` connector (`OrchestratorDataSource`) statement parity and local `TRIGGER JOB`.** Implement `DROP JOB`, `KILL JOB`, `CREATE/ALTER/DROP NOTIFICATION`, `ALTER/DROP SCHEDULE`, `ALTER JOB ... ADD/REMOVE NOTIFICATION`, `SHOW JOB STATE`, and `ASSERT JOB` in `OrchestratorDataSource.cs` against the existing `JobApiEndpoints` REST routes; make local `TRIGGER JOB <name>` in `EnableDisableTriggerJobHandlers.cs` enqueue an execution or fail closed when no scheduler is attached instead of logging a no-op message. | — | M |
| OR09 | **Complete the `/designer` to `/studio/` cutover in Portal.** Once the Studio beginner/authoring completeness gate (`ST03`) passes, redirect `/designer` and `/designer/new` in Portal to `/studio/` so Report and Pipeline authoring share one workbench, draft/approval pipeline, and capability boundary. | ST03 | S |

**Acceptance evidence:** Creating a bundle-backed job from `orchestrator.html` emits a valid
`orch://` URI that pins and executes cleanly; `PIPELINE:{id}` jobs display their catalog metadata,
link to `/studio/`, and disallow divergent inline script edits; `CatalogOnly` mode blocks raw
filesystem and unreviewed inline job/report script injection; Triage, Subscriptions, and linked
dataset refreshes work over HTTP when Portal and Orchestrator do not share a local SQLite file;
normalized jobs render their full table/job Dependency Chain; operators can edit Jobs, Schedules,
Notifications, and their ACLs, trigger with variable overrides, cancel active runs, and inspect
statement waterfalls for any run from `orchestrator.html`; `EXECUTE orch` supports the full job,
schedule, and notification statement set; and `DocSanityTests` passes cleanly.

### SaaS Operations — Shared Lifecycle, Metering, and Hosted Launch Evidence

**Status:** Incremental
**Horizon:** Launch Gate
**Scheduling:** Backlog; outside the current sprint. Required before the corresponding hosted-production claims.
**Authoritative design:** [SaaS Tenant Isolation Architecture](docs/architecture/saas-tenant-isolation.md), [Deployment Profile Certification](docs/administration/platform/deployment-profile-certification.md), and [Provider-Neutral Fault Certification](docs/architecture/decisions/provider-neutral-fault-certification.md)

Managed Dedicated and Shared SaaS profile lanes have passed their topology-specific isolation gates,
and more of the operational closure has landed than this entry previously claimed: the Gateway,
storage and scheduler metering producers all write to the tenant ledger with their connector class,
and queued admission is reconciled after scheduler-process loss by
`SandboxAdmissionReconciliationService`. What remains is sandbox measurement and failure/recovery
evidence, certified Shared lifecycle transitions, and physical runtime and hosting evidence bound to
production claims. Scheduled sandbox attempts already carry
CPU, process peak-memory and spill-I/O counters through the CLI completion envelope into the
Scheduler ledger event. That existing wiring is the baseline for SA01–SA05.

**Why launch gate:** Contract and deterministic-adapter evidence proves product invariants, but it
does not prove an untested hardened runtime, cloud service, HA topology, or production region. Those
claims must be bound to the provider and candidate commit that actually ran them.

**Boundaries:** Do not reopen the passing Shared hostile-isolation profile or infer a Shared
transition from Managed Dedicated evidence. Provider-specific fault activation stays behind the
provider-neutral contract. Metering remains observational and cannot become an execution-policy or
authorization input.

**Dependencies:** The Shared tenant lifecycle saga, ledger-backed sandbox admission, immutable
scheduler workload identity, object-native artifact storage, tenant metering ledger, hardened
sandbox provider, production canaries, HA soak tooling, and release claims index.

**Planning:** Use the [backlog sizing and readiness rules](#sprint-planning-for-backlog-tables).
Select individual IDs when this initiative is scheduled; task sizes exclude external waits.

**Scope for every hosted task:** Instantiate an ID for one named topology, runtime/provider version,
region and candidate configuration. Passing one instance does not certify other providers, regions
or topologies. SA12 records the selected instance and infrastructure prerequisites before physical
work is estimated. Missing prerequisites mean pending work. Soak and certification run durations are
calendar time in addition to the engineering size below. Repeat the relevant tasks as separate
instances when another topology is selected.

#### Sandbox metering producer

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| SA01 | **Review and pin sandbox measurement semantics.** Map the existing Scheduler event and completion envelope to attempt identity, terminal statuses, CPU/process-peak-memory/spill-I/O units and unavailable-counter behavior; identify gaps and estimate their fixes. No billing prices or authorization changes. | — | D |
| SA02 | **Completed-attempt sandbox metering evidence.** Exercise the existing wiring on one declared runtime for success, failure and cancellation. Verify tenant/history identity, approved counters and the agreed unavailable-counter behavior; correct only identified gaps. Do not add a duplicate producer/event. | SA01 | M |
| SA03 | **Meter interrupted attempts.** Record worker loss or termination with an explicit incomplete/ambiguous outcome; recovery never invents counters or attributes a stale attempt to a new run. | SA02 | M |
| SA30 | **Decide metering delivery failure/retry policy.** Specify observable ledger-outage behavior, retry ownership, retention and delivery identity; estimate implementation using the existing ledger. Preserve execution admission, authority and reported workload outcome. No retry implementation. | SA01 | D |
| SA04 | **Metering delivery failure/retry.** Implement the accepted policy for one ledger-outage/recovery fixture; verify observable failures and idempotent retries without altering execution admission, authority or reported workload outcome. | SA02, SA30 | M |
| SA05 | **Sandbox producer isolation/replay evidence.** Interleave two tenant attempts, duplicate deliveries and restart; verify correct partitioned counts and no double accounting or secret/script data. | SA02, SA03, SA04 | M |

#### Shared lifecycle transitions — one journey per task

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| SA06 | **Select supported Shared transitions.** Name one source/target/version fixture for each supported upgrade, promotion/import, restore and exit journey; record unsupported routes and the portable/host-owned boundary. | — | D |
| SA07 | **Shared in-place upgrade.** One accepted N-to-N+1 fixture preserves tenant state, rejects stale scheduler authority and proves its rollback point while another tenant remains isolated. | SA06 | M |
| SA08 | **Promotion into Shared.** One accepted source topology imports into a disabled Shared tenant, verifies ownership/bindings and rejects collisions or cross-tenant activation without partial changes. No additional source topologies. | SA06 | M |
| SA09 | **Shared portable import.** One approved bundle-to-tenant import round-trips declared state and rejects tampering, replay and another tenant's bindings; no new package format. | SA06 | M |
| SA10 | **Shared tenant backup/restore.** Restore one approved tenant fixture, validate usable state and prove another tenant is unaffected; include hostile target/tamper cases. No whole-platform disaster recovery claim. | SA06 | M |
| SA11 | **Shared customer exit.** One Shared-to-self-hosted Enterprise fixture verifies/decrypts its portable bundle without source-operator contact and states required target bindings before mutation. Preserve backward-promotion refusal. | SA06 | M |
| SA12 | **Select one hosted evidence target.** Pin topology, provider/runtime versions, region, image digest, database/artifact providers and access prerequisites; declare fault fixtures, soak duration and measurable claim limits. Produce per-task estimates. | — | D |
| SA13 | **Publish Shared transition evidence through the runner.** Register only supported completed transition fixtures; emit topology-specific claim rows and fail missing/skipped/dirty evidence. Uncovered transitions remain NotCertified. | SA07–SA11 for selected supported routes | M |

#### Physical hardened runtime

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| SA14 | **Provision one pinned hardened target.** Prepare the selected runtime and worker image, verify required controls and record reproducible setup. Standard container evidence cannot satisfy the hostile-tenant claim. | SA12 | M |
| SA15 | **Certify one physical hardened runtime.** Run the existing lifecycle/isolation fixtures on that target and retain version/digest-bound hostile negative evidence and clean teardown. No inference from deterministic adapters. | SA14 | M |

#### Physical fault activation — one scenario per task

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| SA16 | **Process/worker loss.** Activate one real loss point, run the catalog's required repetitions and retain safe outcome/checkpoint evidence. | SA12, SA15 | M |
| SA17 | **Lease expiry/fencing race.** Activate the selected target's real race and prove stale owners cannot commit alongside a new owner. | SA12, SA15 | M |
| SA18 | **Database disconnect.** Activate one physical provider disconnect and prove explicit failure/recovery with no silent loss. | SA12, SA15 | M |
| SA19 | **Partial artifact operation.** Interrupt one physical write and prove that partial state cannot masquerade as a committed artifact. | SA12, SA15 | M |
| SA20 | **Storage outage.** Make the selected storage provider unavailable and verify visible failure/recovery under the existing operation contract. | SA12, SA15 | M |
| SA21 | **Network partition.** Partition one declared authority path and prove single mutation authority and bounded recovery. | SA12, SA15 | M |
| SA22 | **Duplicate delivery.** Repeat one real workload delivery and prove at most one committed result for its operation identity. | SA12, SA15 | M |
| SA23 | **Clock skew.** Activate one declared physical clock-skew fixture and prove the lease/fencing invariants. Do not perturb unrelated shared hosts. | SA12, SA15 | M |
| SA24 | **Disk exhaustion.** Fill only the isolated test capacity declared in the fixture and prove failure, cleanup and safe retry eligibility. | SA12, SA15 | M |

Each fault task must implement or verify its physical provider action and emit the unchanged
provider-neutral evidence contract. Adapter-only tests do not close it. Use the runner's required
repetitions and checkpoint rules. If SA12 finds a missing provider-control capability, create and
estimate that prerequisite before assigning the affected M-sized task.

#### HA, soak, canary and final claims

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| SA25 | **One topology failover drill.** Lose one active node during the declared workload and prove lease fencing, output continuity or explicit safe failure, and measured recovery within the stated claim. | SA12, SA15; relevant completed fault tasks | M |
| SA26 | **One declared soak run.** Execute the pinned workload/duration, retain raw resource and correctness measurements, and evaluate the predefined pass criteria. Run duration is separate calendar time. | SA25 | M |
| SA27 | **One canary rollout/rollback drill.** Upgrade one permitted canary, verify drain and workload results, then exercise rollback/stop on a failed health check without lowering isolation. | SA12, SA15 | M |
| SA28 | **One clean-candidate evidence closure.** Re-run and validate every required selected-target lane against the exact clean candidate commit; hash/link complete bundles and reject stale, dirty, missing or skipped proof. Engineering estimate excludes execution time. | SA05, SA13 for claimed Shared routes, SA15–SA27 for the target claims | S |
| SA29 | **Reconcile one hosted claim set.** Align the claims index, operator guidance and release wording with that target's actual evidence; list unsupported topologies/transitions/regions explicitly. | SA28 | S |

#### Small pickup options for sprint planning

Select one track or a capacity-sized subset. Physical work is ready only after SA12 records access,
provider controls and revised estimates. These engineering ranges exclude provisioning waits and
run durations; tasks may span calendar sprints even when implementation is small.

| Outcome | Select first | Initial engineering range | Follow-on work |
| :--- | :--- | :--- | :--- |
| Account for one sandbox attempt | SA01, SA02 | 4–7 days | SA03 interruption, then SA30/SA04 delivery recovery and SA05 isolation/replay proof. |
| Establish one Shared transition | SA06, one of SA07–SA11 | 4–7 days | Register that supported route through SA13; other routes remain separate. |
| Establish one physical hardened target | SA12, SA14, SA15 | 7–12 days | Select individual fault scenarios SA16–SA24 after checking provider prerequisites. |

**Shared completion evidence:** Verified tenant partitioning and idempotent measurements;
positive and hostile-negative transition outcomes with continuity/rollback artifacts; physical
provider actions, exact versions and complete fault observations; validated HA/soak/canary reports;
and a claims index bound to the same clean candidate commit. Shared evidence must name Shared.
Managed Dedicated results cannot close Shared tasks, and a passed profile does not certify an
unexecuted lifecycle transition. Deterministic contract evidence remains useful development proof.
