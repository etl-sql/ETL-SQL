# Changelog

All notable changes to ETL-SQL are documented here. This project follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) conventions.

## Versioning Policy

Version numbers follow [Semantic Versioning 2.0.0](https://semver.org/).
- **Pre-1.0.0 (`0.y.z`):** The engine runtime is in active development. Minor version increments (e.g., `v0.13.0` to `v0.14.0`) may introduce breaking changes or syntax deprecations, which are formally cataloged in [BREAKING_CHANGES.md](BREAKING_CHANGES.md). Patch version increments (e.g., `v0.14.1`) are strictly reserved for backwards-compatible bug fixes.
- **Production (`1.0.0` and beyond):** Upon reaching `1.0.0`, the public API, syntax grammar, and execution behaviors are considered stable. Breaking changes will only occur on major version increments (e.g., `v2.0.0`).

---

## [Unreleased]

- Studio has a document outline. The visual library already listed what was on a page but could not
  act on the list and never told the canvas anything; the outline lists every page, the row bands and
  containers those pages lay out, and the visuals inside them, and selection runs both ways — a row
  selects the tile, and a tile highlights the row. Move reorders by swapping two tiles' grid
  placement, which the canonical patcher turns back into a rearranged `STRUCTURE`, and hide writes
  `VISIBLE = OFF`, the property the report runtime already honours. Lock is deliberately not written
  into the script: the report language has no `LOCKED`, so it is a canvas guard held on the author's
  own machine — it refuses a drag, a resize, and a delete — and the panel says so rather than letting
  anyone assume a colleague inherits it.

- Studio has a data-model view. A new `Model` projection draws the connections a script opens, the
  tables it reads, the `#temp` tables and CTEs it builds, and the relationships between them, using
  the same shared graph renderer as the pipeline map. Nothing is drawn that the parser or the
  database did not say: a join appears because the author wrote it, a foreign key because the
  database declares it, and two tables that merely share a column name produce nothing at all.
  Cardinality follows the same rule — it reads "not stated" unless a declared key or foreign key
  settles it, and the view distinguishes "these tables declare no keys" from "no database was asked",
  because only one of those is a finding. Connectors that expose a catalog (SQL Server, PostgreSQL,
  MySQL) now surface their primary and foreign keys through `IMetadataManager`, so both hosts answer
  the same way.

- Studio has an engine panel: what a statement can see from where it sits, and what the engine would
  do with it. Scope is the pipeline scope model asked with a caret instead of a task label — a
  `#temp` created below the cursor is not offered, because offering it is wrong only at run time.
  The plan is the engine's own `EXPLAIN`, requested through the ordinary run route so it passes the
  same policy, limits, and audit as any other execution, and rendered as an operator list with the
  badges an author acts on: blocking versus streaming, remote pushdown, index use, and spill.
  `EXPLAIN` is now allowed in a Portal interactive run because it plans without executing;
  `EXPLAIN ANALYZE` is refused by name, because it does execute.

- Parser and lint diagnostics now carry plain-language guidance wherever ETL-SQL can act on them: a
  sentence with no parser vocabulary in it, a next step, the card or page the offending line sits in,
  a link into the reference tree, and — where exactly one repair is correct — a button that makes the
  edit. An unclosed bracket is the clearest gain: the parser reports the semicolon it tripped over,
  several lines from the cause and about the wrong character, and the guidance says which block was
  left open. A diagnostic the translation cannot act on gets no guidance rather than a generic
  "check your syntax", and a repair is offered only where a guess is impossible — a button that
  guessed would turn a visible error into an invisible wrong answer. The guidance rides on the
  diagnostic itself, so it reaches VS Code through the language server and the CLI's lint output, not
  only Studio.

- `SEARCH` controls can opt into an accessible clear button with `SHOW_CLEAR = ON`, and `TEXTBOX`
  controls can cap typed input with a positive-integer `MAX_LENGTH` option. `COMBO` category sorting
  is now covered by a focused lowering-and-resolution test alongside its existing reference docs.

- Named `LINE` and `COMBO` visuals and `CUSTOM CHART` `LINE` layers can set `LINE_WIDTH` from `0.1`
  through `10` pixels. The width follows each line series through ordinary, stacked, transposed,
  and geographic native SVG paths without changing marker, overlay, rule, or error-bar
  widths; `THICKNESS` remains specific to `TICK` marks.

- Named `LINE` and `SCATTER` markers and `CUSTOM CHART` `POINT` layers can set a portable outline
  with `SYMBOL_STROKE_COLOR` and `SYMBOL_STROKE_WIDTH`. Colors use `#RRGGBB`, widths are
  non-negative pixel values, and a color without an explicit width renders as a one-pixel stroke.

- Point markers now share one portable shape vocabulary across named `LINE` and `SCATTER` visuals
  and `CUSTOM CHART` `POINT` layers: circle, square, triangle, diamond, cross, and star. Authored
  constants are validated, data-driven shapes fall back to circles when a row contains an unknown
  value, and the native SVG renderer preserves the selected geometry for interactive and static
  output.

- Studio exports a paginated report to PDF. Step 8 used to name the export and tell the author to go
  and find it, which for a report living in an unsaved buffer meant nowhere; it now produces the file
  from the report as it stands, with the page setup, breaks, and repeating table headings the author
  configured. Both hosts serve the route, so the action is not a button that works in the Portal and
  fails on the desktop.

- Studio's pagination preview lists the pages the export will produce — each physical page and what
  lands on it, including the row ranges a split detail table continues from — read from the engine's
  own compilation instead of being implied by the page-width sheet the canvas draws.

- A report's `INPUT` parameters are asked before Studio previews or exports it, and the answers are
  applied the way `--var` applies them — `DECLARE` prefers a supplied value to its own initial one.
  Cancelling the prompt cancels the run instead of quietly using the defaults.

- Fixed three defects that made a paginated export wrong rather than missing. A paginated page's
  visuals are deferred until a reader presses Run, so an export rendered pages with no data on them;
  an export now runs every page, because the file is the finished document. The exporter laid its
  heading block out with A4 defaults and then started a new section for every declared page, so a
  Letter landscape report exported a stray portrait sheet first and every page count was one too
  high. And `CliContext.Variables`, the mechanism behind `--var`, was read by the CLI alone: a
  variable supplied by any other host was accepted and silently ignored.

- The Studio filter pane reaches every value in a column. A categorical card searches its values,
  selects all, clears, and inverts within what the search narrowed to, and pages through the rest
  with a count of how much of the column is showing. It used to stop at twelve values with no search
  and no way to reach the thirteenth, so the rest of a column could not be filtered on at all.

- Numeric and date filters can ask more than "between": at least, at most, greater than, less than,
  equals, does not equal, is blank, and is not blank. The vocabulary lives in the filter service, so
  the pane sends a word and never composes SQL — an operator the service does not know is refused
  rather than quietly treated as a range, and a condition chosen before its value simply filters
  nothing.

- An action Studio offers to undo replaces its own previous offer instead of stacking one toast per
  click.

- Cross-visual filtering and cascading slicers can be authored in Studio. Choosing what a visual
  does when another one is selected — highlight, filter, or ignore — and the column selections are
  matched on are controls now, not a free-text box whose placeholder was the documentation, and a
  `SLICER` or `MULTISELECT` has a cascade editor for its mode, parent bindings, and invalid, null,
  all-value, and multi-select policies. The clause is written exactly as the formatter writes it, so
  reopening a report does not rewrite what was just saved, and a cascade Studio cannot read is shown
  as read-only text and left alone rather than rewritten from a partial reading.

- Fixed three defects that made those surfaces inert. The action and interaction handlers wrote to
  the visual in memory and never to the script, so a setting survived until save and then vanished.
  The parameter picker read a state key nothing populates, leaving the only control that binds a
  slicer to a parameter permanently empty. And the script patcher matched a clause keyword at any
  parenthesis depth: a nested `MODE` — `CASCADE`'s own — was taken for the visual's `MODE` clause
  and written a second time at the top level, producing a script that did not parse, which the
  patcher's own parse guard then turned into an edit that silently did nothing. Clause matching is
  now pinned to the statement's own level, which also protects every other clause from a keyword
  inside an inline `SELECT`.

- The formatting inspector remembers which sections are open. Changing one setting rebuilds the
  panel, and a rebuilt section starts closed, so every edit used to collapse the section being
  worked in.

- Guided authoring in Studio now explains itself and can be undone. Every wizard, step, and canvas
  action that writes Report-SQL offers a one-click **Undo** afterwards, backed by the editor's own
  transaction rather than a remembered copy of the old text — so taking it is exactly Ctrl+Z, and
  once anything else has changed the buffer the offer refuses and says why instead of taking back
  the wrong edit. Alongside the exact SQL, each surface now shows one sentence saying what it will
  add or change and what it leaves alone; a preview without that sentence is now a programming
  error rather than a silent omission.

- **Start with sample data** now opens a working dashboard. The seeded MOCKDB script stages one
  shared `#temp` query and builds a KPI card, a bar chart, and a table onto a laid-out dashboard
  page, and the document opens on the canvas rather than beside a script the author has not written
  yet. A new test executes the seed against the sample connector and asserts all three tiles arrive
  with rows and a place on the page, because a starter that only parses can still open blank.

- Report entry is recoverable. The Dashboard-versus-Paginated question can be declined — with Not
  now, Escape, or a click outside — and is not asked again for that document; the canvas then
  carries a strip offering the choice whenever the author is ready. That strip is also where the
  guided steps come back after they are dismissed, so hiding the teaching no longer hides the way
  back to it. New dashboards open on the canvas, and each document keeps whichever projection its
  author chooses from then on.

- Studio's rail reaches the bookmark editor and the report theme and style panel. Both already
  existed in the designer, inside a sidebar Studio hides — the rail now mounts the same elements
  rather than growing a second implementation of either.

- The chart creator finishes the visual and hands over. Per-measure aggregation is joined by number
  and date formatting for the value and the category axis, written as the `FORMAT` and
  `X_AXIS (FORMAT = …)` the preview shows, and adding a visual now selects it so the formatting
  inspector — which already owns colours, grid lines, data labels, axis bounds, and legend
  placement — continues the job.

- Studio names things the way an author does: a file is a Dashboard, Report, Pipeline, or Query
  rather than `REPORTSQL` or `ETLSQL`, and the dataset wizard's refusal to borrow a host connection
  now leads with the consequence — a report that borrows one works for you and fails for everyone
  else who opens it — before the implementation reason.

- Added Run to Selected Node to the Studio pipeline canvas. Selecting a task and choosing **Run to
  here** executes the pipeline through it, so its variables and `#temp` tables land in Results. The
  run covers the selection's dependency closure rather than the whole file above it: the tasks its
  `-- @after:` tag names, transitively, with a task that declares nothing still waiting for the one
  above it. Connections, `DECLARE`s, and unlabelled staging are always kept, and a selection inside
  a `PARALLEL`, `FOREACH`, or transaction scope runs that scope whole. Before anything runs, a
  confirmation names every write that would outlive it — the connection-qualified table, the
  address, the path — grouped by the task performing it, along with any sibling tasks the run
  skipped. `#temp` staging is not listed, so the confirmation appears only when something real is
  at stake.

- Added a positional scope inspector to the Studio pipeline canvas. Selecting a task shows the
  variables and `#temp` tables it can actually read from where it sits — including an enclosing
  loop's item variable, and excluding anything declared below it — with each name linking back to
  the line that produced it. Row counts and spill appear once a run reports them for that task.

- Added control-flow containers to the Studio pipeline canvas. `PARALLEL`, `FOREACH`, and
  transaction scopes are palette kinds that hold other tasks: drop a task onto one to put it inside,
  and its statement is relocated rather than regenerated. Concurrency is written only as a
  `PARALLEL` block the author asked for, and the canvas refuses to give two of its branches an
  order rather than silently dropping the dependency.

- Added conditional precedence edges to the Studio pipeline canvas. An edge can hand over on
  success, on failure, on completion, or on an expression the author writes, and the choice is
  lowered into the script as a `BEGIN TRY` / `BEGIN CATCH` guard on the task being watched and an
  `IF` on the task that waits, so the pipeline runs what the canvas draws. Each condition has its
  own edge colour, stroke pattern, and label.

- Added a visual formatting inspector to Studio's selected-chart sidebar. Authors can edit title
  typography, subtitles, number formats, axes, legends, palettes, table data bars, and conditional
  table/KPI rules while the corresponding Report-SQL clauses stay synchronized with the canvas.

- Added a reproducible Studio performance-budget gate for full workbench startup, post-GC browser
  heap, CodeMirror keystroke latency, 250-row visual aggregation, and canvas redraw/layout. The
  checked-in Windows, Linux, and macOS ceilings run in a dedicated CI matrix and publish JSON
  evidence, replacing the earlier unverified startup, memory, keystroke, and sustained-frame claims.

- Split the canonical Studio browser implementation into responsibility-owned modules for host
  contracts, document state, data sampling, Report-SQL mutations, save security, and lease
  lifecycle. `studio.js` now composes those services with the workbench UI, and Portal-only dataset
  routes are checked separately from cross-host and desktop-only routes.

- Split Studio's Data and Filters activity-rail tools into independent sidebars that can remain open
  together. Data now prioritizes New connection and field discovery, while Filters provides a
  type-aware New filter dialog, active-rule controls, and drag-and-drop or keyboard field transfer.

- Added a side-by-side Git diff viewer to desktop Studio. The Source Control rail now compares the
  live editor buffer with `HEAD` or a selected local commit, including unsaved changes, aligned line
  numbers, and added/deleted line highlighting.

- Fixed narrow desktop Studio layouts collapsing dashboard cards below a usable width. The authored
  12-column canvas now scrolls internally while the outer Studio shell remains overflow-free.

- Added a full desktop Studio workspace Explorer. It now creates, renames, and deletes files and
  folders, moves files into folders or back to the workspace root by drag-and-drop, keeps open tabs
  aligned with moved paths, blocks deletion of dirty documents, and enforces the workspace boundary.

- Added inline file rename to desktop Studio tabs. Double-clicking a tab name now opens a keyboard-
  accessible editor that renames the underlying workspace file, preserves its extension when omitted,
  updates the open document and Explorer paths, and rejects read-only, unsafe, or colliding targets.

- Added authenticated production-host Studio browser journeys for Portal and desktop. The Portal
  journey creates and opens a catalog report, connects to a governed catalog source, samples and
  filters data, edits and runs a statement, saves, reloads, and closes its edit lease. The desktop
  journey covers the same authoring loop plus simultaneous project windows, host shutdown, and
  relaunch with persisted source. Studio now carries the active document URI through desktop
  connection/schema discovery and sends the selected governed connection with Portal run requests.

- Split Studio report authoring into distinct **Dashboard** and **Paginated Report** workflows. Both
  create ordinary `.rptsql` files and keep the shared parser, patcher, data, expression, formatting,
  preview, and code surfaces. Dashboard opens a responsive tile board with data, visual,
  cross-filter, layout, and formatting guidance. Paginated Report opens a physical-page surface with
  an eight-step path through parameters, group/detail bands, totals, headers/footers, page setup,
  breaks, preview, and export. Existing files select a workflow from explicit page modes; mixed or
  implicit reports ask without touching source bytes. Physical page settings and visual print-break
  clauses now round-trip through the shared authoring contract.

- **Breaking:** data-quality rules moved out of comment tags into first-class syntax. A column now
  declares rules with `EXPECT <rule> [ON FAILURE THROW | WARN | QUARANTINE]`, repeated as needed,
  replacing `/* @expect: '…'; @fail: '…'; */` and the numbered `@expect_N`/`@fail_N` pairing. A rule
  decides which rows leave a statement, so it must not be something a formatter or comment stripper
  can silently remove; comments keep carrying the tags that describe data (`@d`, `@owner`, `@pii`).
  Rules combine with `AND`/`OR` — the comma separates columns in a select list — and a `MATCHES`
  pattern is now a quoted string literal, so the outer quoting and doubled quotes the tag layer
  forced are gone. Writing a rule as a tag is a lint error naming the clause to use instead.

- **Breaking:** `ASSERT JOB` folds onto the same failure-action vocabulary. `ON CRITICAL_FAILURE
  THROW` and `WITH (FAIL_ON_WARN = TRUE)` are replaced by stacked `ON FAILURE WARN | NOTIFY
  <notification> | THROW` blocks: severity is an action, not a clause name, and not an option
  hidden in a `WITH()` bag that could fail a run without the severity clause saying so. `WARN` is
  the default on both surfaces, `NOTIFY` alone is non-fatal, and "any warned row fails the run" is
  now the predicate `WARN_PERCENT = 0` with `ON FAILURE THROW`.

- Fixed an `ASSERT JOB` predicate naming a column no sink in the script writes reporting green
  forever. The runtime skipped the unobserved metric and the assertion passed, so a typo such as
  `NULL_PERCENT(clean_users.Emial)` was a guard that could never fire; it is now a lint error at
  author time. Skip-with-warning remains for what is genuinely unknowable until runtime — a run
  that observed no rows, and historical cold start.

- Renamed the data-quality rule catalog's `rule_tag` column to `rule_clause`, and its values from
  `@expect`/`@expect_1` to `EXPECT`/`EXPECT #2`, so the catalog names a form that can still be
  written. This affects `eng.data_quality_rules`, `SHOW DATA QUALITY RULES`, and the Portal's
  data-quality views.

- Fixed `||` string concatenation across the full expression grammar. Concatenations now retain
  explicit aliases, work inside parentheses and larger expressions, preserve SQL-style `NULL`
  propagation in engine execution, and compile to the target dialect's concatenation operator.
  Report designer fuzz scripts again exercise the operator directly.

- Replaced Studio's regex-generated pipeline cards with the shared engine DAG projection. `.etlsql`
  canvases now show real sequential and branching edges, including `IF`, `PARALLEL`, loop,
  `TRY`/`CATCH`, and validation stages. The canonical graph opens as a fitted left-to-right execution
  map, node selection navigates to source, and invalid edits keep the last valid topology without
  rewriting the script.

- Added the desktop Studio host lifecycle. `etlsql studio <project>` now reconnects to a healthy
  per-project host, while `studio list`, `studio open`, `studio stop`, `--new-window`, and the
  advanced `--new-instance` option manage local instances explicitly. Authenticated session records
  retain normalized workspace, PID, assigned port, start time, and local authentication metadata;
  stale records are removed after health checks. Browser heartbeats, active-run tracking,
  configurable idle shutdown, a bounded **Exit Studio** flow, and revision-checked saves prevent
  orphan hosts and same-project overwrite conflicts.

- Finished Studio code-to-canvas data synchronization. A valid `CREATE DATASET` query edit now
  reparses the canvas and refreshes that document's `__ETLSNAP__` rows through the bounded,
  read-only preview contract on both Portal and desktop hosts. Parse failures retain the last valid
  canvas and sample. Per-document cancellation and revision checks prevent late parse or preview
  responses from overwriting a newer edit or another tab's data.

- Fixed the Studio script pane erasing what you typed. The canvas regenerates its script from the
  design state alone, and updating the canvas *from* the editor let that regeneration run and
  overwrite the buffer ~800ms later — so anything the design state does not model, most visibly a
  `CREATE CONNECTION`, vanished as it was typed. A canvas update caused *by* the script no longer
  writes back to it; genuine canvas edits still do. This also made the Connection Wizard look
  broken: its inserted statement disappeared moments after Insert.

- Fixed the Connection Wizard accepting an empty alias. The field was marked required but nothing
  enforced it, and the generator substitutes a literal `<alias>` placeholder when it is blank, so
  confirming the dialog wrote `CREATE CONNECTION <alias> AS …` — which does not parse. The alias is
  now prefilled with a free, valid name derived from the connector (avoiding names already used in
  the script), stays editable, and Insert is blocked with a specific reason when it is empty,
  malformed, or already taken. Studio now passes its existing connection names to the wizard, so
  collision detection works there at all.

- Added MOCKDB to the Connection Wizard's built-in connector list. It is the only connector that
  needs no external database and it backs Studio Home's "Start with sample data", but it was absent
  from the fallback list used when connector discovery fails — so the zero-dependency on-ramp
  disappeared exactly when the environment could least reach a real server.

- Gave ETL-SQL Studio the script workbench it was missing. Studio now mounts the shared results
  panel, so it has the Workstation Editor's **Results / Messages / Pipeline / Performance** tabs,
  result filter, CSV/Excel/JSON export, and column lineage bar. Results are a per-document trace
  replayed into one panel, so switching tabs restores each document's own run. Lint diagnostics are
  routed to the Messages tab — Studio suppressed the editor's own diagnostics panel without providing
  a replacement, so they had existed only as gutter squiggles — and clicking one jumps to its line.

- Canvas edits are applied as ranged editor transactions rather than replacing the whole document.
  The author keeps cursor and scroll position, the generated span is scrolled into view, and because
  the edit is an ordinary transaction, undo now covers it: Ctrl+Z genuinely reverses an "Add visual".
  Bound the shortcuts the toolbar had advertised all along (`Ctrl+N`, `Ctrl+S`, `Ctrl+Enter`,
  `Ctrl+Shift+Enter`) — Studio had no keyboard handler at all — and added an unsaved-work guard on
  browser close.

- The shared `$trigger` snippet library reaches the GUI editors. Its 83 templates were embedded in
  the engine and already served the TUI and VS Code, but neither Studio nor the Workstation Editor
  exposed them; both now offer them through a shared `SnippetCompletionSource`, carrying the readable
  `«placeholder»` form rather than LSP tab stops.

- Studio Home no longer dead-ends a first session. Because the visual palette stays disabled until a
  data sample exists, and a sample needs a connection a new author does not have, Home now leads with
  **Start with sample data** — a working dashboard on the built-in MOCKDB connector, needing no
  database. The three blank-document actions are distinguishable (they previously read as two
  identical `.etlsql` buttons), and the Portal's permission dead-ends now say what is missing and who
  can grant it. Starter scripts are parser-checked in CI.

- Removed ~250 lines of unreachable Studio code, including a hardcoded filter pane with fabricated
  `$32,000`/`$71,000` values that a routing change would have exposed. `CARD` — the KPI tile — now
  gets its intended compact grid size; the previous special case tested for a `KPI` type name that
  the grammar does not define.

- Repaired Studio's editor-assist layer, which was silently dead in Portal Studio. The shared
  `studio.js` requested `/api/analyze`, `/api/complete`, `/api/hover`, `/api/format`, and `/api/run` —
  names only the desktop Workstation Editor serves — so autocomplete and hover documentation returned
  nothing, the linter pinned a spurious "Not Found" diagnostic to line 1, Format silently changed
  nothing while reporting success, and a failed Run rendered as a green "In-Memory Run Completed" over
  stale design-time sample rows. Studio now resolves every server path through one `STUDIO_ROUTES`
  table on the canonical `/api/designer/*` dialect that both hosts serve. Format reads the `script`
  field both hosts actually return and reports success only when the document changed; a failed run
  renders as a failure and never presents sample rows as results.

- Closed the Studio desktop/Portal route gap. Added the governed desktop
  `POST /api/designer/data-sample` — schema-validated, bounded, secret-redacted, and self-registering
  the script's connections — without which the desktop visual canvas could never enable its palette.
  Added `/api/designer/hover` and `/api/designer/format` on both hosts, with hover served from one
  host-neutral `LanguageHoverService` over the embedded `docs/reference` help corpus. Desktop-only
  workspace routes are now gated behind an explicit host capability instead of 404ing silently.

- Added `StudioRouteContractTests`, which asserts that every route Studio calls exists on both the
  Portal and the desktop host and that no route bypasses the route table, plus behavioural cover for
  the hover and format endpoints. The ui-sandbox mock now fails closed on an unmatched route; its
  previous `{ok:true}` catch-all answered any URL successfully and is what kept this entire class of
  defect invisible. Shared-asset sync now also covers the Workstation Editor's published `wwwroot`.

- Routed Studio visual creation, duplication, deletion, property edits, mapping edits, and slicer
  promotion through the canonical parser and surgical patcher. The shared authoring contract now
  round-trips report parameters, and promoted slicers emit real parameter/action bindings without
  rewriting hand-authored queries, CTEs, comments, or formatting.

- Connected Portal Studio Home to the real permission-filtered report and folder catalog. Creating,
  opening, saving, and closing now carry catalog identity, optimistic version, source revision,
  deployment capabilities, source-control state, and renewable edit leases. Studio releases leases
  only on actual close or page teardown, blocks saves without an active lease, and keeps untouched
  documents clean when switching tabs. Snapshots, filters, selected sources, field metadata, preview
  caches, diagnostics/run ownership, and result panes are now document-scoped. Production browser
  coverage proves catalog creation/open, lease acquire/release, and cross-tab state restoration.

- Fixed the two Studio save integrity blockers. Portal Studio now opens real versioned catalog
  reports, saves with report identity, source revision, and optimistic concurrency, preserves dirty
  state after failures, and blocks close after a failed save. Studio secret handling no longer puts
  plaintext values in modal markup or emits Base64 disguised as encryption; it now uses the same
  PBKDF2 + AES-GCM `ENC:` envelope as the Connection Wizard and engine. Production-host browser
  journeys prove exact save/reload persistence, stale-writer conflicts, close protection, hidden
  plaintext, and engine-compatible decryption.

- Completed the connection-catalog bug backlog: Gateway-bound verification now probes live approved
  resources, the connection wizard can refresh and clear Gateway bindings and groups MOCKDB under
  Test Data, and TUI report controls update parameters and refresh dependent visuals.

- Refined ETL-SQL Studio report authoring around one left workspace. The embedded designer no longer
  duplicates the visual library or property dock; the activity sidebar now owns the report tree,
  searchable visual catalog, guided source/table picker, draggable typed fields, and an honest empty
  filter lane. Split view adds local Format, Run selected, and Run all controls, while the first save
  of an untitled script now asks for its file name and folder. Visual creation is gated on a reusable
  source sample, filters update that preview sample, chart fields can be clicked or dragged into the
  complete property editor, card titles are editable in place, and duplicate/remove actions use
  precisely centered SVG controls.

- Certified Verified Viewer Context for SQL Server Gateway resources. SQL Server preserves the
  configured service login, installs signed viewer values through parameterized `SESSION_CONTEXT`,
  prohibits claim-driven role selection, and clears or evicts pooled sessions after success,
  provider failure, cancellation, timeout, and broken connections.

- Added durable ambiguous Gateway write triage to Portal Operations. Mutating transport uncertainty
  creates one high-priority case per tenant and operation with acknowledgement, assignment, evidence,
  notes, four externally verified resolutions, and append-only event history. Script and scheduler
  execution now carry a non-retryable outcome so configured job retries cannot duplicate the write.
  Gateway restart promotes abandoned in-flight writes to ambiguous and republishes their non-secret
  metadata during the authenticated handshake for deduplicated Portal recovery.

- Added Phase 6 of **ETL-SQL Studio** establishing automated multi-resolution usability audits and Playwright
  browser verification. Added full geometry and bounding-box assertion suites testing viewports from 1024x768 to 4K UHD,
  guaranteeing zero layout shift, strict horizontal scroll containment (`scrollWidth <= clientWidth`), minimum 24px
  accessible button hitboxes, responsive auto-fitting visual card grids, and zero uncaught console/runtime errors.

- Added Phase 5 of **ETL-SQL Studio** delivering multi-surface packaging across Desktop CLI and SaaS Portal.
  Introduced the top-level `etlsql studio [script|dir]` CLI command launching the workstation host over local
  loopback with automatic browser opening. Deployed the authenticated SaaS Portal route (`/studio` and
  `studio.html`) binding the Zero-Trust connection catalog, Gateway routing, and OIDC identity contexts.

- Added Phase 4 of **ETL-SQL Studio** introducing surgical AST synchronization and split-view CodeMirror
  navigation. Clicking any visual card on the interactive canvas automatically navigates and highlights the
  corresponding `VISUAL <id>` statement in the CodeMirror editor. Updating visual options (titles, layouts,
  mappings) applies surgical AST span patching without modifying surrounding hand-crafted CTEs, custom procedural
  logic, comments, or formatting. Debounced code-to-canvas synchronization refreshes visual calculations and DAG
  nodes while preserving canvas stability during syntax editing.

- Added Phase 3 of **ETL-SQL Studio** introducing the type-aware Filter Pane and 1-Click "Promote to Slicer"
  workflow. Inspects `__ETLSNAP__` sample rows to populate categorical filter checkboxes (with distinct value counts),
  numeric range inputs, and relative date presets (`Last 7 Days`, `Last 30 Days`, `This Quarter`, `YTD`). Clicking
  "Promote to Slicer" automatically injects `@selected_*` parameter definitions and interactive `SLICER` visuals
  into the script and WYSIWYG canvas, allowing live interactive multi-pill filtering across all canvas cards in <1ms.

- Added Phase 2 of **ETL-SQL Studio** featuring live data `__ETLSNAP__` ingestion via `POST /api/designer/data-sample`
  and a 60 FPS in-memory client math engine. Evaluates real-time aggregations (KPI `reduce()` sums/averages,
  categorical Bar/Column SVG groups, Donut/Pie share-of-total arcs, and Line trend splines) in <1ms without
  remote database round-trips. Automatically detects `.etlsql` scripts and projects an interactive directed
  acyclic graph (DAG) visualizer tracing data movement from source connectors across memory staging tables to
  destination loads.

- Added Phase 1 of **ETL-SQL Studio**, the unified dual-projection visual and script workbench.
  Features a top document tab manager, left Activity Rail (Explorer, Data Catalog, Filter Pane, Git Status,
  and Settings), dual projection toggles (`[ 🎨 Canvas | 🌓 Split | ⌨️ Code ]`), CodeMirror 6 sub-line
  selection execution with zero full-line expansion drift, and interactive zero-trust plaintext password
  scanning with client passphrase encryption (`ENC:`) dialogs.

- Added approved Gateway resource discovery and binding in the canonical Connection Wizard,
  `Admin → Connections`, and `Admin → Data Gateways`. Active gateway sessions publish non-secret
  metadata (resource ID, connector type, allowed operation classes, approval state, online status,
  and last seen timestamp) for interactive resource selection, replacing manual Gateway and Resource ID
  entry. Server-side validation re-verifies tenant ownership, active gateway session, resource approval,
  allowed operations, and persisted connection use grants on save and execution. Reserved binding keys
  cannot bypass validation, no grant denies execution, discovery is admin-only, and rejected catalog saves
  remain visible in the wizard without exposing server error details or physical endpoints and credentials.

- Added `SPARKLINE(...)` and `PROGRESS_BAR(...)` helpers to constrained `HTML` visual templates.
  Helpers validate source fields and bounded options during analysis, compile through the shared
  server-side chart plan into native SVG, render in single and repeater components without browser
  geometry code, and append deterministic semantic text for terminal, Markdown, and other
  non-graphical output.

- Added the code-first Connection Wizard across Report Builder, Portal Admin, Workstation Editor,
  VS Code, and LSP. Connector metadata drives SQL/file forms, diagnostics, shared references,
  staged workspace files, Gateway routing, secret handling, and canonical script generation. Live
  zero-trust path feedback and real-browser coverage now gate success/failure diagnostics, SQL and
  file output, name collisions, shared bindings, and light/dark rendering.

- Added nine `TRANSFORM` recipe snippets and matching Report Builder data-preparation helpers for
  rolling aggregates, period comparisons, share of total, top-N grouping, date filling, pivots,
  interpolation, normalization, and deduplication. Report Builder formatting controls now include
  color swatches, radius controls, and typography pickers alongside direct values.

- Added the report Design Token contract and `CREATE STYLE PALETTE = (...)` sequences. Page,
  container, and visual styles now resolve into scoped `--etl-*` variables with deterministic
  inheritance, safe CSS serialization, stable series-color assignment, explicit-color precedence,
  contrast validation, and shared browser/export rendering behavior.

- Completed native graphics across bounded responsive layout, deterministic smart-label placement,
  statistical/financial `CUSTOM` channels, and geographic `CUSTOM` composition. Geographic charts
  declare projection and map authority, resolve file-backed GeoJSON through the execution-context
  path boundary, enforce geometry/render budgets, and share one resolved plan across SVG, PDF,
  terminal fallback, accessibility, interactions, responsive refresh, and golden coverage.

- Shipped constrained `HTML` report visuals across authoring and output surfaces. Analysis and LSP
  now validate and understand escaped typed bindings, scoped CSS, declarative actions, and bounded
  `VISUAL(...)` embeds. Source-free, single-row, and repeater components publish atomically under
  explicit row/node/byte/query/render budgets. Browser and print use the sanitized shared runtime;
  PDF, Markdown, terminal, email, plain text, snapshots, screen readers, and unsupported hosts retain
  a deterministic semantic fallback. Hostile markup, URLs, SVG, CSS, disclosure, cycles, JavaScript,
  refresh consistency, aggregate limits, and cross-surface behavior are covered by focused tests and
  real-browser tests.

- Added Report Builder preview and lossless editing for constrained `HTML` visuals. Registered `HTML`
  in the visual type registry with live sanitized preview on canvas cards, and added a dedicated Constrained
  HTML Component editor in the Properties panel supporting optional `SOURCE`, `MODE` (`SINGLE` / `REPEATER`),
  `TEMPLATE`, scoped `STYLE` (`CSS`), `FALLBACK`, and declarative `ACTIONS`. Extended `DesignerAnalysisService`,
  `DesignerScriptPatcher`, and `DesignerScriptGenerationService` to preserve author comments, nested trivia,
  and formatting byte-for-byte during round-trips across embedded and LSP-hosted paths.

- Added dedicated Report Builder `CHART` editor and lossless `CUSTOM` visual support. Registered `CUSTOM`
  in the visual type registry with live SVG Grammar-of-Graphics layered preview on canvas cards, and exposed
  coordinate systems, mark layers, scales, encodings, and conditions in the properties panel with bi-directional
  syntax synchronization. Updated `DesignerAnalysisService`, `DesignerScriptPatcher`, and
  `DesignerScriptGenerationService` to preserve author comments, nested trivia, and refinement clauses
  byte-for-byte during round-trips across embedded and LSP-hosted paths.

- Added lossless Report Builder editing certification tests and UI sandbox transient syntax error resilience.
  `ReportDesignerLosslessFuzzTests` exercises deterministic mutation fuzzing, verifying out-of-scope byte
  preservation across complex CTEs, SQL data-prep statements, variables, and comments, line ending stability
  (CRLF/LF), corrupted syntax injection safety, and patcher idempotency. The UI sandbox and browser test suite
  prove that transient syntax errors in split-screen script editing preserve existing canvas cards while displaying
  diagnostic warnings.

- Added `etl-sql-report offline <script>`, which turns an existing `.etlsnap` package into a single
  self-contained HTML file that opens with no server and no network. The shared runtime had carried an
  offline branch — bookmarks applying from the manifest's precomputed envelope, detail popovers reading
  captured rows — since author bookmarks shipped, but nothing set `window.__ETLSNAP__`, so none of it
  was reachable by a reader. The exporter inlines the runtime, its stylesheet, and the manifest, and
  declares a `default-src 'none'` Content-Security-Policy so the file cannot quietly depend on being
  online. Reads the package rather than re-evaluating the script, so the page shows the figures the
  package captured.

- Added one golden lane covering both visual catalogs. Fixtures are discovered from
  `tests/fixtures/reporting/conformance`, so adding a chart means adding a `.rptsql` file and blessing
  it rather than editing C#, and each fixture reports as its own test result instead of folding into a
  single assertion that says only that something moved. Every fixture pins four artifacts, checked in
  beside their hashes so a change is a diff a reviewer can open: the serialized `PlotPlan`, the rendered
  native SVG, the `SemanticFallback`, and the terminal render. The plan and the SVG are compared
  independently — a moved plan hash is a semantic regression, a moved SVG hash with the plan holding is
  a rendering change. Bless with `scripts\Test-ReportingGoldens.ps1 -UpdateGolden`.
- Added `CUSTOM` grammar coverage to the golden lane for the surface named visuals cannot reach:
  encoding inheritance and `INHERIT_ENCODINGS = OFF`, `DATUM` and `VALUE` bindings, `CONDITIONS`,
  `STACK = NORMALIZE`, jitter and nudge placement, `FACET WRAP`, `ASPECT_RATIO`, quantitative colour
  ranges, and `TICK`. Jitter is hashed over semantic placement and a seed, so it is deterministic by
  construction and exactly the kind of property that would otherwise degrade silently.
- Added asserted named-visual-to-`CUSTOM` translations. Each pair ships as one fixture holding both
  spellings over one staged source, and the resolved layers, scales, palette, series, data, accessible
  summary, semantic fallback, theme tokens, and formatting are compared. Identity, the per-visual-type
  null policy, and the style tokens transcribing `MAPPINGS`/`OPTIONS` are excluded, and each exclusion
  is itself pinned so it cannot quietly widen into hiding a real divergence.
- Added enforcement of the determinism precondition the SVG lane depends on. The native SVG is
  hash-stable across platforms only while the renderer stays free of clock, GUID, ambient-culture, and
  text-measurement input; that is now asserted at the source and by rendering the same plan under a
  locale with a different decimal separator and requiring byte equality.
- Added short-lived, audience-bound workload identity exchange for GitHub, GitLab, Azure DevOps, and
  `private_key_jwt` schedulers. Exact tenant/issuer/subject/resource/operation policy, durable replay
  rejection, non-self one-use approvals, owner-capped service tokens, and attributed anomaly audit
  make CI and scheduled workloads secretless while client-secret exchange remains compatible.
- Completed tenant-portability correctness and scale: v2 bundles now bind a cross-system consistency
  point, reconciled stable-ID/hash/owner/ACL/exclusion inventory, certified delta sequencing, and
  resumable content-addressed chunks over the object-native storage contract.
- Added durable tenant cutover authority and scheduler fencing, Shared-source hostile isolation
  certification, and offline self-contained tenant-bundle validators for Windows, Linux, and macOS
  that can verify customer-encrypted content after source access is gone.
- Completed the native Grammar-of-Graphics authoring surface with typed field/`DATUM`/`VALUE`
  bindings, inherited encodings, explicit stack/offset/interval placement, deterministic jitter and
  nudge, continuous color ranges, wrapped facets, fixed Cartesian aspect, and category-local `TICK`
  marks across parser, formatter, lint, LSP, lineage, Report Builder, serialization, native SVG,
  terminal, accessibility, and export paths.
- Added a parser-tested production composite report, Vega-Lite and ggplot2 conversion guidance, a
  declarative-geometry cookbook entry, and a reproducible Phase 13 closure evidence index.
- Added `SET REPORT TIME_ZONE`, `SET REPORT LOCALE`, and `SET REPORT NULL_LABEL` as real, documented
  report properties, with deterministic server-side precedence: `SET REPORT TIME_ZONE` then
  `Scheduler:DefaultTimeZone` then `UTC`; `SET REPORT LOCALE` then `Reporting:DefaultLocale` then the
  invariant culture; a visual's `OPTIONS (NULL_LABEL = '...')` then `SET REPORT NULL_LABEL` then
  `Reporting:DefaultNullLabel` then `-`. Zones resolve through the same resolver schedules, `AT TIME
  ZONE`, and relative dates use; locales validate with `CultureInfo.GetCultureInfo`. Nothing is
  inferred from the viewer's browser, so the browser, PDF, email, and terminal renderings of one report
  agree. The resolved values are carried on the manifest and survive report-context clone and clear,
  parallel visual builds, interaction refreshes, and snapshots.
- Added `Scheduler:DefaultTimeZone`, `Reporting:DefaultLocale`, and `Reporting:DefaultNullLabel` to
  shipped configuration and the administration reference. An invalid configured zone or locale now
  fails rather than silently degrading a whole deployment to the fallback.
- `ChartSpec.InteractionSpec` is now the canonical chart interaction contract. Named `ACTIONS`/
  `INTERACTIONS` clauses and `CUSTOM` charts lower through one shared path into typed selections and
  trigger bindings; `PlotPlan` carries the resolved semantics — selection key, measure column,
  selection mode, effect, and highlight treatment — and browser clients receive a compact
  `interaction` manifest projected from it. `PlotPlan` also records each mark layer's resolved value
  extent, which the native SVG publishes on the mark as `data-extent-axis`/`data-extent-anchor`.
- Added a deliberate browser-delivery projection. One class classifies every manifest property as
  delivered or server-only, and every path that hands a manifest to a browser — the Portal report and
  designer APIs, stored snapshots, the ReportPlayer page, the LSP preview, the Workstation Editor —
  goes through it. A test fails if a manifest property is added without being classified.
- Added a browser payload regression budget. Raw and gzip bytes for `report-runtime.js`,
  `report-runtime.css`, the shared runtime total, and end-to-end page weight are now gated against a
  blessed measurement in `docs/benchmarks/report-payload-budget.json` on every default test run, with
  a 3% plus 2 KB tolerance. `report-runtime.js` had grown from 217,299 bytes just after the external
  chart runtime was retired back past that size with nothing failing, because footprint was observed
  in a report rather than gated. Growth past tolerance now fails, and the only way past it is
  `scripts\Test-ReportPayloadBudget.ps1 -UpdateBudget`, which rewrites the checked-in budget so the
  new numbers land in the diff for review — not a hard-coded ceiling to argue with.
- Added end-to-end page-weight measurement to the reporting baseline harness: per fixture, the shared
  runtime assets plus that report's delivered browser manifest, raw and gzip. The report also names
  what actually dominates the payload, measured rather than asserted.
- Documented what the browser payload is actually made of. Of the 979,829 B raw / 226,725 B gzip a
  report page downloads, Tabulator and Arrow plus their CSS are 637,889 B / 151,362 B — 65% of raw and
  67% of gzip. The chart runtime is 34% and 32%. Neither vendor bundle was touched by the chart-runtime
  retirement or by this release's delivery work, and a footprint claim that does not name them is
  wrong by roughly a factor of two. The `Resources/Shared/` total is also not page weight: it includes
  733,811 B of designer bundle a report viewer never loads.
- The six focused native layout modules — `TREEMAP`, `SUNBURST`, `SANKEY`, `NETWORK`, `MAP`, `MATRIX` —
  now consume shared presentation inputs instead of inventing their own. `FocusedLayoutInputs`
  resolves the visual's theme tokens, series colours, accessible name and description, compact resolved
  interaction metadata, and an explicit authored canvas (`OPTIONS (WIDTH = n, HEIGHT = n)`, clamped,
  defaulting to 600×350); `ChartPalette` is now the single series-colour rule the plan-backed path and
  the focused path both resolve through. The geometry stays focused on purpose — these types are not
  lowered through `PlotPlan` — and sizing remains an explicit backend input, not a viewport reading.
  Side-by-side conformance tests render a focused visual and a plan-backed visual over the same data
  and fail if their series colours diverge.
- Brought the Portal-only `native-charts.js` operational chart adapter under its own asset governance:
  an ownership banner naming its boundary, plus ownership, dependency/license, accessibility,
  behavioural, and raw/gzip footprint gates. It stays out of `Resources/Shared/`, out of
  `sync-assets.js`, out of the Report-SQL capability matrix, and is not a `PlotPlan` consumer — that
  separation is the decision, and the gates hold it to the separation rather than dissolving it.

### Changed

- Renamed and corrected two cookbook pages published under a `custom-` prefix that contain no `CHART`
  block. `custom-choropleth-point-map.md` and `custom-alluvial-flow-composition.md` are now
  `choropleth-point-map.md` and `alluvial-flow-composition.md`, and both describe what they actually
  are: coordinated named visuals over shared staged data. The choropleth recipe no longer claims a
  single layered map surface at that point in its history; it remains the concise two-visual named
  `MAP` pattern, while `CUSTOM` now supports a shared projected region/route/point/label surface.
- Date and time values in charts are now rendered by the report formatter rather than reused from the
  engine's generic row strings, because only the formatter knows the report's zone and locale. A
  temporal string carrying no offset is anchored to UTC instead of picking up the server's local
  offset, so the same report no longer renders different instants on two hosts.
- Normal browser delivery no longer serializes `chartSpec`, `chartData`, `plotPlan`, or
  `microCharts[].plotPlan`. Every graphical visual was shipping five representations of one chart and
  the runtime read two, trading once-cached library bytes for uncached per-report bytes on every load.
  Across the six representative fixtures the combined manifest fell from 170.1 KB to 65.9 KB raw
  (15.1 KB gzip); the baseline harness now reports browser-delivered raw and gzip bytes beside the
  server figure, so end-to-end page weight is measured rather than inferred from shared assets alone.
  The full contracts stay available to server renderers, tests, and an explicitly authorized
  diagnostic projection that a general query flag cannot reach.
- The detail-surface payload budget is measured through the browser projection, so a popover is
  charged for what it actually downloads instead of for semantic contracts that never leave the
  server.
- Focused layout SVGs now carry the resolved interaction key (`data-interaction-key`) and highlight
  treatment, a themed surface and text colour, and a `<desc>` built from the visual's semantic
  fallback. They previously drew on a hardcoded 600×350 white canvas with a private colour array, so a
  `TREEMAP` and a `BAR` of the same categories on one page could disagree on colour.
- The Portal orchestrator page's chart adapter now takes its `aria-label` from the caller's chart
  title instead of labelling every chart "Native chart".
- The browser runtime no longer decides chart geometry from `visual.visualType`. Proportional
  selection highlighting reads the treatment from the resolved interaction manifest and the extent
  from the mark's own attributes, so a chart type name no longer implies a shape. The legacy
  `visual.interactions` map remains readable by a tested compatibility shim, which is the only
  surviving reference to a visual's type name and is reached only for manifests built before v0.19.0.

### Fixed

- Fixed the offline snapshot branch treating a viewer served over http as web mode, which started
  auto-refresh polling against an API that is not there, and fixed detail popovers refreshing through
  the parameter API on open — offline, every popover rendered "could not be loaded", which made the
  tooltip documentation's offline claim false. `window.__ETLSNAP__` now suppresses web mode regardless
  of protocol, and parameter reads resolve from the manifest already in memory.
- Fixed the UI sandbox's VS Code results fixture depending on `src/etl-sql-vscode/ui/dist/`, a
  gitignored build output. The story rendered the real bundle for whoever had built it and a stub for
  everyone else, and silently — the fallback looked like a working panel. The built-in fixture is now
  the deterministic path and covers the panel's status, message, progress-tree, results, and
  performance surfaces; the real bundle stays available behind `?vscodeDist=1`.
- Named visuals now reject unsupported `MAPPINGS` roles with a valid-role diagnostic instead of
  silently lowering a wrong chart. Catalog-wide tests cover every named chart type, including the
  original `BAR (CATEGORY, VALUE)` failure.
- Corrected the roadmap and capability inventory after auditing partial constrained-HTML work. The
  accepted ADR, parser, formatter, evaluator, sanitizer, and initial manifest projection exist, but
  browser/static rendering and the remaining authoring and certification surfaces are still open.
- Reconciled the object-native artifact-storage roadmap entry with its shipped provider-neutral
  contract, S3/Azure certification, shared snapshot consumers, and tenant-portability integration.
- Corrected the `CUSTOM` learning path to describe the shipped resolved cross-filter key, restored
  strict documentation-audit coverage for the constrained-HTML and lean-worker decisions, and fixed
  the production-canary reference to the live Portal readiness guide.
- `CUSTOM` charts now cross-filter on the column they are keyed on. A layered chart has no `MAPPINGS`
  clause, so the browser's `mapping:*` lookup always missed and fell through to `visual.columns[0]` —
  every `CUSTOM` chart filtered on whatever column its source query happened to list first. The
  selection key is now resolved server-side from the chart's encodings. When the resolved key names no
  column in the visual's data, a click raises no filter rather than a confidently wrong one.
- A non-stacked `CUSTOM` `RECT` layer now honours author-supplied `Y_START`/`Y_END` and
  `X_START`/`X_END`. The channels always parsed and lint stayed silent, but both rect paths read the
  interval endpoints only under `STACK` and otherwise forced the start endpoint to zero — so a ranged
  bar, a qualitative band, or an explicit-bin histogram lowered cleanly and then rendered from the
  baseline with the author's start silently discarded. Both endpoints now take part in scale-domain
  resolution, Cartesian and transposed geometry, native SVG, terminal, semantic fallback, and the
  PDF/email export paths that share them. Pairing is enforced: each interval requires both endpoints
  with matching quantitative or temporal types, and a ranged `RECT` rejects `Y`/`Y2` alongside
  `Y_START`/`Y_END` and `X`/`X2` alongside `X_START`/`X_END` rather than picking a renderer heuristic.
  Stacked `RECT` behaviour is unchanged — its endpoints are still resolver-computed — and is pinned by
  regression tests. The bullet card in `samples/10_Kitchen_Sinks/39_CUSTOM_LAYERS.rptsql` now expresses
  its qualitative bands as three ranged rectangles instead of four overlapping full-height bars ordered
  by `Z_INDEX`.
- An unrecognised `SET REPORT` key is now a syntax error naming the supported keys. `SET REPORT
  TIMEZONE = 'UTC'` used to parse through the arbitrary-identifier path and then be silently discarded
  by the handler, producing a report that looked configured and was not.
- `CUSTOM` charts now receive the resolved theme and style tokens named visuals receive. The advanced
  lowerer built a theme name with no tokens, so a `CREATE STYLE` theme reached `BAR` and stopped at
  `CUSTOM` — the sharp edge for exactly the authors following the themed-dashboard recipe. Both
  lowerers now build tokens and the resolved `FormattingSpec` through one shared path.

- `CUSTOM` chart errors now point at the mistake instead of the top of the statement. The authoring
  lint rule stamped every diagnostic on the `CREATE VISUAL` header, so on a multi-layer chart all ~20
  possible errors squiggled the statement's first line. The parser now records a source span on every
  chart node — coordinate, scales, encodings, binding sources, layers, styles, conditions, position
  adjustments, color ranges, facet, and resolution — and each diagnostic is anchored to the node that
  carries the mistake.
- Every duplicate layer name, scale name, and encoding channel is reported in a single pass. The rule
  previously reported only the first duplicate group, so fixing one duplicate and re-running was the
  only way to discover the next.
- A `CUSTOM` chart can no longer fail with no editor diagnostic at all. Semantic checks now live in one
  shared validator that both the lint rule and the report lowerer run, so the editor and report preview
  cannot drift. Lowering failures — including scale-inference conflicts, undeclared or secret-bearing
  parameters, and the contract-level backstop — are raised as typed diagnostics carrying the offending
  node's position and published on the visual manifest. A failing visual still degrades to a safe error
  state; it just no longer exists only as unpositioned text painted inside the rendered report.
- Replaced every mirrored-enum `Enum.Parse(value.ToString())` bridge in the advanced-chart path with
  explicit arm-per-member mappings. Adding or renaming a member on either side used to produce a runtime
  `ArgumentException` inside a rendered report rather than a build or test failure; parity tests now keep
  the AST and contract enum families aligned.
- A chart-type visual delivered without a server-rendered SVG payload no longer aborts the whole
  report page. The browser runtime called an undefined `renderChart`, so an older snapshot, a
  lightweight manifest, or an unrendered visual type raised a `ReferenceError` that took every other
  visual on the page with it. That card now degrades to an explicit, screen-reader-announced
  "chart payload missing" state, covered by a browser-lane render test.
- Documentation grammar validation now honours CommonMark-indented ` ```sql ` fences. The extractor
  required the closing fence at column zero, so an indented block ran past its own end and swallowed
  the prose after it — reported as a syntax error in that prose. Validated statements rose from 3457
  to 3495, and the gate now ratchets on that count.
- `ReportDesignerRoundTripFixtures.WithLineEnding` normalizes to LF before applying the requested
  ending, so the LF case of the designer round-trip tests actually runs on a `core.autocrlf=true`
  checkout instead of reporting a false red.

### Changed

- Reconciled the reporting capability matrix with the source-backed renderer contract. Every
  graphical catalog visual now uses the shared native `PlotPlan` path or an approved focused native
  SVG module; no graphical visual requires ECharts, ClearScript, or another external chart runtime.

## [0.18.0] — 2026-08-20

For complete release details, highlights, and migration notes, see [Release Notes v0.18.0](docs/releases/v0.18.0.md).

### Breaking Changes

- Flat secret and connection commands moved under `admin machine` (`admin machine secret ...` and `admin machine connection ...`).
- Retired `CREATE SMTP CONNECTION`; SMTP connections now use standard connector grammar `CREATE CONNECTION <alias> AS SMTP(...)`.
- `ALTER STYLE`, `ALTER NAVIGATION`, `ALTER THEME`, and report-scoped `ALTER DATASET` are rejected at parse time; use `CREATE OR REPLACE` instead.
- Report-object `DROP` statements now require canonical `DROP <kind> IF EXISTS <name>` syntax.

### Added

- **Secure Outbound Data Gateway**: Added an on-premises data gateway daemon connecting to the Portal over outbound TLS WebSockets (`/api/gateway/ws`) with ECDSA P-256 machine identity, active-active pooling, and write idempotency.
- **SaaS Hardened Tenant Isolation**: Delivered hostile multi-tenant isolation across storage, memory, and runtime namespaces with virtual-time Weighted Fair Queuing (WFQ) and Managed Dedicated fleet management.
- **Non-Bypassable Egress Fence**: Added socket-level egress filtering blocking cloud metadata (`169.254.0.0/16`), container bridges, and operator-configured CIDR ranges (`Security:DeniedEgressRanges`).
- **Studio Collaborative Leases & Row Preview**: 5-minute renewable collaborative edit locking in Studio, plus real-time row previews for sources and `#temp` tables with RLS and PII masking.
- **Transactional File Publication**: Added `TRANSACTIONAL = ON` across `FLATFILE`, `JSON`, `XML`, `EXCEL`, `PARQUET`, and SFTP `ATOMIC_UPLOAD = ON` using atomic staging and replacement renames.
- **Fast Pre-Push Validation**: Added `Test-PrePush.ps1` running formatting, runtime assets, syntax index, shell line endings, and contract smoke tests in under 60 seconds.
- **Identity Access Simulator**: Added `GET /api/admin/access-simulator/user/{id}` to simulate and explain effective permissions across roles, groups, folder ACLs, and RLS.

### Changed

- Split `ETL-SQL.Connectors` into domain-specific assemblies (`.Cloud`, `.Databases`, `.Files`, `.Messaging`, `.Remote`, `.Common`) to minimize runtime dependencies.
- Studio and Portal navigation standardized with accessible dialog focus trapping and unified headers.

### Removed

- Removed legacy `api/admin/smtp` endpoints in favor of unified `api/admin/connections`.

### Fixed

- Fixed Gateway registration race condition between `HelloAck` and routing table enrollment.
- Fixed sandbox checkpoint session encryption key binding to use tenant authority.
- Hardened `DROP THEME` and `DROP TEMPLATE` with file extension validation and operation tracking.
- Resolved load-sensitive timing races in Language Server metadata caching and live-object scale assessment suites.
- Fixed folder permission panel state resets so network errors never display stale ACL rows.

### Security

- Remote Orchestrator management calls now require caller assertion tokens verified with HMAC-SHA256 digests alongside `X-Orchestrator-Key`.
- Automated redaction of raw passwords, connection strings, tokens, and `SECRET:name` references across all diagnostic bundles and logs.

## [0.17.0] — 2026-07-26

### Added

- Added `ASSERT JOB <name> (<predicates>) [ON FAILURE ALERT <connection>] [ON CRITICAL_FAILURE THROW]`,
  asserting on the run's own metrics rather than a query result: `ROW_COUNT`, `NULL_PERCENT(<col>)`,
  qualified `NULL_PERCENT(<target>.<col>)`, `FRESHNESS(<col>)`, `QUARANTINE_PERCENT`, and
  `WARN_PERCENT`, each comparable against a literal/interval or against a historical baseline with
  `WITHIN <fraction> OF HISTORICAL`; supported historical metrics also accept
  `WITHIN <n> SIGMA OF HISTORICAL`. Metrics are collected in-stream during the run (never a post-run
  re-scan), so write-only sinks are supported. Historical baselines use the mean of recent completed
  runs and skip themselves below a configurable minimum (`Engine:DataQuality:MinHistoryRuns`,
  default 3; sigma default 10) so new jobs do not alert-storm. Per-column null metrics are persisted
  to job history for target-aware `NULL_PERCENT ... OF HISTORICAL`. Failures can post a counts-only
  summary through a webhook connection — sample data is never included — and optionally fail the run.
  Orchestrator-hosted alerts are transition-based: pass→fail alerts, repeated fail→fail runs are
  suppressed until `Engine:DataQuality:AlertRealertHours` elapses (default 24), and fail→pass sends
  a recovery notification.

- Added column-level data-quality rules: `@expect` / `@fail` tags declared inline on SELECT columns,
  routed by a trailing `ON FAILURE <ACTION> [TO <table>] [WITH (RETENTION = '…')]` clause. Rules
  cover `NOT NULL`, `UNIQUE` (plus `UNIQUE WITH (cols)` and `UNIQUE_FIRST/LAST BY <expr>`),
  `MATCHES <regex>`, `IN (<list>)`, `EXISTS IN table(col)`, `EXPR <predicate>`, and numeric
  comparisons; actions are `THROW`, `WARN` (aggregated diagnostics, optional row capture), and
  `QUARANTINE` (row diverted to a capture table with the `__dq_*` provenance columns). Failing rows
  are captured pre-projection so stewards see the cause, `@pii` values are masked in diagnostics and
  logs, and per-run quarantined/warned counts are persisted to job history and surfaced on the
  execution result. Rules are validated at lint time (malformed rules, non-sink QUARANTINE,
  orphaned clauses, missing section labels) and appear in editor completions.

- Added the first quarantine-remediation v2 foundation: orchestrator-hosted jobs now persist a
  replay manifest when rows are quarantined, recording the job, script path, section label, source
  table, quarantine target, replayability flag, non-replayable reason, and captured input schema
  fingerprint. Single-table labeled quarantines are marked replayable; join-source quarantines are
  captured normally but marked non-replayable until the v3 provenance design lands.

- Added data-quality quarantine disposition enforcement for `UPDATE`: `__dq_*` evidence columns are
  immutable except `__dq_status`, warn rows cannot be released, and quarantine statuses follow the
  v2 lifecycle (`quarantined` may become `released` or `discarded`; `released` may become
  `replayed` or `discarded`).

- Added `REPLAY QUARANTINE <table>` replay support for v2 single-table quarantines. The statement
  resolves the orchestrator replay manifest, rejects missing or non-replayable quarantine targets
  with clear errors, builds a source stream from rows marked `released` with `__dq_*` evidence
  columns stripped, and resumes the recorded section label with that stream substituted for the
  original source. After a successful replay, consumed rows move from `released` to `replayed`;
  replay is fenced by the orchestrator cluster-lock store so concurrent stewards cannot consume the
  same released row set twice.

- Added the first Portal data-quality quarantine queue surface. `/api/data-quality/quarantine`
  exposes orchestrator replay manifests with replayability filters, and the Governance sidebar now
  includes a Quarantine Queue view with target/search filters and copyable `REPLAY QUARANTINE`
  statements for steward workflows.

- Added Portal quarantine replay submission. The quarantine queue can now submit replayable manifests
  through the configured Orchestrator job channel, rejects blocked or tampered manifest targets, and
  reports the submitted replay job id back to the steward.

- Added Portal quarantine disposition submission. `/api/data-quality/quarantine/disposition`
  accepts explicit row ids plus optional source-column edits, builds a guarded `UPDATE` that leaves
  `__dq_*` evidence immutable, and submits it through the Orchestrator job channel for release or
  discard workflows.

- Added the Portal quarantine row editor. `/api/data-quality/quarantine/rows` previews capped
  quarantine rows for Portal-resolvable targets, the Quarantine Queue can open an inline row grid,
  and stewards can edit source columns then submit release or discard actions without touching
  immutable `__dq_*` evidence. Targets whose producing connection or session-local table is not
  available inside Portal are labeled view-only with the reason and copyable review SQL instead of
  opening a row editor that would fail or misleadingly return an empty temp table.

- Hardened the data-quality `UNIQUE` pre-pass for larger inputs: projected key records now spill
  into hash partitions and reduce partition-by-partition instead of keeping the full key map in
  memory. Duplicate lookup is keyed by rule occurrence, so identical `UNIQUE` rule text on different
  columns no longer collides.

- Added an opt-in data-source capability for connector-side data-quality retention pruning.
  `WITH (RETENTION = '...')` capture targets now use the connector capability when available, with
  SQLite-backed quarantine/warn tables deleting rows older than `__dq_ts` through a bounded
  connector-side `DELETE`.

- Added a write-only `WEBHOOK` connector (aliases `SLACK`, `TEAMS`) that POSTs each inserted row as a
  JSON payload — Slack/Teams message shaping via `FORMAT`, custom bodies via `BODY_TEMPLATE`, and
  opt-in retry policy. The endpoint URL is treated as a credential: `SECRET:` references resolve on
  `URL` for webhook connections, and the URL is masked to scheme + host in `SHOW CONNECTION`, logs,
  and error messages. Every request and redirect hop passes egress-policy validation; only 307/308
  redirects are followed so a delivery is never silently downgraded to a body-less GET.

- Added a design-time script DAG/Flow preview for `.etlsql` and `.rptsql` authoring surfaces, derived
  from parsed script text and wired into existing shared DAG rendering paths.
- Added report-designer ergonomics for keyboard deletion, save shortcuts, escape-to-clear,
  grid nudging, undo/redo, duplication, multi-select movement, container detachment, container
  collapse, tab/accordion child assignment, dynamic column mapping suggestions, and dataset-column
  drag-and-drop mapping.
- Added a business-consumer Portal home experience with favorites, recently viewed reports, featured
  reports, popularity sections, and permission-aware catalog discovery.
- Added fuzzy and synonym-aware Portal catalog search with match reasons across titles,
  descriptions, tags, folders, and report metadata.
- Added self-service report access requests, report-owner/admin approval and denial endpoints, and
  report-level ACLs so approvals can grant one report without broadening folder access.
- Added published-report metadata headers with owner/contact, freshness, last-refresh state, and
  interactive tag badges that navigate or post catalog-search intents.
- Added stale-report refresh requests: users with `Execute` can start a refresh, while read-only
  consumers create an audited owner request without bypassing permissions.
- Added one-click "My Default View" saving for current report parameter/slicer state, updating a
  single per-user default saved view.
- Added `DATE_SUFFIX` and `SUFFIX_SEPARATOR` file-operation options for common dated archive names
  on copy/move flows.
- Extended `SHOW SCHEMA`/`DESCRIBE` lookup so file-based connections can expose schema metadata to
  authors and agents.
- Added `SHOW PROTECTED DATA [AT <portal_or_orchestrator>] [LIMIT n] [INTO #temp]` to inventory protected lineage tagged as PII, PHI, PCI, sensitive, confidential, or restricted from local, Portal, or Orchestrator catalogs.
- Added `SHOW PROTECTED DATA SUGGESTIONS [AT <portal_or_orchestrator>] [LIMIT n] [INTO #temp]` for reviewable classifier findings from column names, source-column names, catalog metadata hints, and supported sampled values without automatically changing tags.
- Added `SHOW PORTAL AUDIT [ACTION '...'] [LIMIT n] [INTO #temp]` for script-first Portal audit review, including steward-impact lineage events.
- Added `samples/08_Reporting/protected_data_audit.rptsql` as a starter protected-data stewardship dashboard.
- Added Portal Lineage Audit mode for a steward-focused workflow that combines protected inventory, classifier suggestions, metadata queues, stale protected assets, inferred impact, steward-impact audit rows, and audit outbox health.
- Added tag-driven governance policy lint and Portal runtime gates for public dataset stewardship metadata, restricted/confidential public datasets, protected dataset exports, and `@quality=gold` promotion metadata.

**Stewardship catalog impact analysis**
- Added `/api/catalog/impact` for upstream, downstream, and bidirectional impact analysis by table,
  column, job, script, dataset, report, subscription, owner, and steward.
- Added Portal Lineage Impact mode and pre-publish report validation impact summaries so publishers
  can review affected reports, datasets, subscriptions, jobs, owners, and stewards before changes.
- Added auditable `STEWARD_LINEAGE_IMPACT` hooks for report execution and persisted ad hoc
  interaction lineage changes that affect steward-owned assets.
- Added [Data Stewardship and Impact Analysis](docs/guides/feature-guides/data-stewardship-impact.md) as the
  operator and publisher usage guide.

**Data prep helpers**
- Added `GENERATE CALENDAR FROM <start> TO <end> INTO #temp`, materializing a full date dimension
  (`DateKey`, ISO week, fiscal year/quarter, month/day names, and boundary flags such as
  `IsMonthEnd` / `IsQuarterStart`).
- Added `FILL_DATES(#source, DATE_COL = …, GAPS_FILL = …, BY_GROUP = …) INTO #temp` to fill missing
  daily rows per group, copying existing rows unchanged.
- Added `COMPARE DATASETS #source WITH #baseline KEY (…) [EXCLUDE (…)] INTO #diff`, writing only
  inserted/updated/deleted rows with `_change_type`, `_changed_columns`, and `<column>_old` /
  `<column>_new` pairs.
- Added 14 productivity functions: `SAME_PERIOD_LAST_YEAR`, `START_OF_MONTH`, `END_OF_MONTH`,
  `START_OF_QUARTER`, `END_OF_QUARTER`, `START_OF_WEEK`, `END_OF_WEEK`, `SAFE_DIVIDE`, `AGE_BUCKET`,
  `VALUE_BUCKET`, `CLEAN_STRING`, `MASK_EMAIL`, `MASK_PHONE`, and `MASK_SSN`. The `MASK_*` functions
  are presentation masking for reports and diagnostics, not a security control.

**Authoring & CLI**
- Added string variable interpolation inside string literals — `${@var}` and `${var}` — resolved
  across statement options, file paths, dynamic connection settings, and expressions. An undeclared
  name is left intact as literal text so shell and regex strings are not corrupted.
- Added `etl-sql edit`, which opens the browser-based script editor, and a unified script editor
  workbench shared by the Workstation and Portal hosts.
- Added `SHOW SCHEMA` as a statement, plus `--mock` mode and `--json` output options for
  scripting and agent use.
- Added `SET DATA_QUALITY_DRY_RUN` so a rule set's impact can be previewed without quarantining,
  warning, or failing a run.

**Workstation editor**
- Added a Git status surface with a header branch badge, a formatter settings panel persisted to
  `.etlsql-formatter.json`, and local run history.
- Added a memory ceiling and a destructive-statement guard for local runs, plus cancellable runs
  with visible elapsed time and a graceful exit path.
- Added column lineage and report preview, and compact colour-coded hover help.

**MOCKDB**
- Added built-in `Numbers`, `Dates`, `Times`, `Geography`, `Currencies`, and `Flags` dimension
  tables, with `Numbers` expanded to 1M rows and `Dates` covering a 200-year range (1900–2100), so
  demos and tests no longer need an external database.

**Portal & designer**
- Promoted Lineage to a top-level Governance workspace with its own sidebar, and added a docs
  endpoint so in-Portal documentation matches the Portal layout.
- Added governed multi-statement runs and workbench sidebar parity.
- Extended the visual designer with report-level theme persistence, custom colour-palette pickers, an
  interactive `@variable` parameter binder, Tidy Layout compacting, governance badges, live
  split-screen mode, snapping grid guides, hover drop-zone highlights, container box styling and
  group dragging, and `LAYOUT(COLSPAN, ROWSPAN, WIDTH, HEIGHT)` emission on `CREATE VISUAL`.
- Expanded ECharts option mapping so every visual type renders in snapshot mode.

**Engine & type safety**
- Added integer digit-precision and sign constraints on temp tables, and `INT(N,+)` / `INT(N,-)`
  sign enforcement for flat-file columns.

**Tooling**
- Added a VS Code Visual Flow (DAG) webview backed by the shared script DAG builder.
- Added Portal subject-module sub-choices to the Windows installer and Linux package configuration.

### Changed

**Connector assemblies**
- Split the monolithic `ETL-SQL.Connectors` assembly into per-domain projects — `.Cloud` (S3, Azure
  Blob, SharePoint), `.Messaging` (Kafka, SMTP), `.Remote` (FTP, SFTP, Directory, Active Directory)
  and `.Databases` (the ten database connectors, plus `DatabaseConnectionStringBuilder` and
  `ConnectorRetryPolicy`) — alongside the existing `.Common` and `.Files`. Hosts now reference only
  the connector groups they register, so a host no longer drags in every provider SDK transitively.
  Provider namespaces are unchanged, so scripts and connection syntax are unaffected.

**Report Designer lays out against the last compiled snapshot**
- The designer canvas now renders visuals using data from the report's most recent `.etlsnap`
  package instead of empty wireframe placeholders, so layout decisions are made against real shapes
  without touching a production database. Rows are capped at 500 per visual and the canvas badges a
  sampled snapshot.
- A report that has never run, or one whose output depends on the viewer's identity, has no shared
  snapshot and continues to show placeholders — identity-sensitive reports deliberately never
  persist one.

**Release gate**
- `Test-PreRelease.ps1` now fails when `THIRD-PARTY-INVENTORY.md` no longer matches the package
  graph, so the licence review and NOTICES cannot silently drift.
- Ten build and publish scripts were renamed from `under_scores` to `hyphens`
  (`publish-release.ps1`, `build-msi.ps1`, `build-linux-packages.sh` and so on). Anything invoking
  them by path needs updating; `scripts/README.md` lists all 90 scripts.

### Fixed

**Real column types for MOCKDB and SQLite**
- The schema and session explorers previously showed `ANY` for every MOCKDB and SQLite column. Both
  now report real declared types, including nullability and primary keys.
- **Note:** the schema cache is consulted before the connector, so an existing workstation keeps
  showing `ANY` until its cached entry ages out (14-day maximum) or `%LOCALAPPDATA%/ETL-SQL/SchemaCache`
  is cleared.

**Editor CLI rejects unknown options**
- `etl-sql-editor` previously ignored an unrecognised flag and then treated its value as the
  workspace path, so `--profile dev` silently opened a folder named `dev`. Unknown options now fail
  with usage. `--profile` was removed from the documented command shape; local connection profiles
  were deliberately not built.

**Result grid no longer renders unbounded result sets**
- The grid built one row of DOM for every row returned. Runs started from the Workstation editor and
  the Portal are capped, but the VS Code REPL streams whatever the CLI evaluated, so a large
  `SELECT` could hang the results panel. The grid now draws at most 5,000 rows and labels a
  truncated view "showing first N of M". Export is unaffected and still writes every row.

**`WAITFOR FILE UNLOCKED` no longer reports a false syntax error**
- The linter grammar modelled only `WAITFOR DELAY | TIME | (condition)`, so a valid
  `WAITFOR FILE UNLOCKED` statement was flagged as a syntax error in the editor and completion
  stopped offering next tokens. The parser always accepted it.

**`SHOW DATASETS` no longer reports a false parse failure**
- Fixed alongside the data-quality work; `QUARANTINE` was also unreserved as a keyword so existing
  scripts using it as an identifier keep parsing.

### Performance

- Removed 432 bytes of per-row validator allocation for passing synchronous data-quality rules by
  keeping their hot path out of async state machines and boxed interface enumeration. A 100,000-row
  allocation budget test allows no more than 4 KB total measurement noise; `EXPR` evaluation and
  real quarantine/warn writes remain asynchronous.
- Reduced Portal catalog-search allocation pressure by replacing the Levenshtein two-dimensional
  allocation with rolling buffers.
- Cached request-scoped Portal group lookups by user to avoid repeated `UserGroups` queries during
  catalog and permission checks.
- Compiled repeated variable-interpolation regular expressions and optimized soft equality byte-array
  comparison with span-based sequence comparison while preserving existing DateTime second-level
  semantics.

### Security

**SFTP host-key verification is now closed by default**
- The SFTP connector previously connected with only a logged warning when `HOST_KEY_FINGERPRINT` was
  unset, trusting whatever server answered. With no trust anchor the client cannot distinguish the
  real server from an interceptor, so an unpinned connection is now **rejected**.
- Added `ALLOW_UNPINNED_HOST_KEY` (default `false`) to opt out explicitly where an unverified
  connection is genuinely intended, making that an intentional choice rather than the default. A
  fingerprint that is set but does not match is still always rejected; the opt-out does not weaken it.
- **Breaking:** scripts using SFTP without `HOST_KEY_FINGERPRINT` now fail until they set either the
  pin (preferred — `ssh-keygen -lf <server_host_key>`) or `ALLOW_UNPINNED_HOST_KEY = 'TRUE'`.
  See [SFTP connector](docs/reference/connectors/services/sftp.md).

**Cached schema reads re-check egress policy**
- The Workstation editor's schema endpoint served table and column names straight from its cache
  without consulting the connector that enforces egress policy, so a host blocked after the cache
  warmed kept being completed in the editor. Policy is now re-checked on every request and a denied
  host returns `403`.
- Report access approval is now report-scoped by default through `ReportAcl` and audited atomically
  with the grant/denial mutation.

**Report authorship does not survive deprovisioning**
- Report authorship upgrades an existing grant to `Manage`; it is not standing permission on its own.
  An author with no remaining folder access and no report ACL loses access to reports they created,
  so removing a user from their groups or from the directory actually revokes that access.
- The same rule governs anonymous share and embed links: a link resolves only while its creator still
  has access, and otherwise reports as `PermissionLost` in the admin anonymous-access inventory
  instead of continuing to serve report data to unauthenticated visitors.

## [0.16.0] — 2026-07-19

### Added

**Central Security Events**
- Added a versioned, vendor-neutral security-event contract with correlated policy denials, lifecycle failures, override attempts, enrollment changes, and resource-limit violations across every host.
- Added a bounded durable local outbox, acknowledgement-based HTTPS delivery using enrolled machine identity, signed-policy severity filtering, bootstrap OS/file sinks, delivery diagnostics, and optional fail-closed health thresholds.
- Added fault-injection coverage for collector and acknowledgement failures, corrupt state, storage pressure, crash recovery, redaction, and enforcement independence from monitoring availability.
- Documented the collector protocol and example Splunk CIM, Elastic ECS, and Microsoft Sentinel ASIM field mappings.
- Added retained-evidence Windows and Linux enterprise certification lanes covering policy lifecycle, enforcement boundaries, standalone behavior, and security-event delivery.
- Certified enterprise policy bootstrap across Portal, Orchestrator, CLI, TUI, Report Player, Report Builder, Language Server, scheduled jobs, spawned runners, and parallel execution; corrected Report Player policy configuration ordering.
- Added retained malicious-input and policy-bypass drills; canonicalized connector aliases before policy enforcement and stripped log-forging characters from security events.
- Certified unenrolled standalone startup with no enterprise HTTP clients or remote event collector, unchanged local configuration, and unrestricted local workflows.

**Schema-resilient flat files**
- Added schema-resilient CSV and Excel ingestion modes: map columns by header, ignore extra source columns, and null missing columns so upstream schema drift no longer fails a load.

**Portal report editor**
- Added an in-designer report preview pane to the standalone script editor so authors can render a report without running a separate serve command.
- Separated **Save** from **Git commit** in the editor, each with its own action, so saving a draft no longer forces a commit.

**Engine**
- Added data-source cancellation hooks so long-running source reads observe cancellation and unwind promptly.

### Changed

- **Connector modularization.** Split the connector implementations into independently deployable projects — `ETL-SQL.Connectors.Common` (shared helpers) and `ETL-SQL.Connectors.Files` — and decoupled `ConnectionStringBuilder` from the database drivers so a host no longer loads every database, cloud, messaging, and native dependency to use one connector.
- **Thinner Portal controllers.** Extracted `ReportScriptInspectionService`, `ReportDependencyService`, and `ReportStructureService` out of `ReportsController`, moving report-parameter parsing, dependency resolution, and structure/AST work into application services.
- Renamed internal `ReportPortal` identities to `Portal` for a consistent namespace.
- **Documentation restructure.** Reorganized the docs tree around single-responsibility sections (`guides/`, `reference/`, `architecture/`, `administration/`, `releases/`) with thin guide hubs and a task index; embedded runtime-help filenames were preserved so in-app help keeps resolving.
- Enforced the documented source-tier layering with architecture boundary tests, so a new upward project reference or banned cross-layer package fails CI.

### Performance

- Bounded Portal storage sampling so usage reporting no longer scans unboundedly on large stores.
- Batched Portal user-role lookups to remove per-user round trips when listing users.

### Security

- Canonicalized connector aliases before policy enforcement and stripped log-forging characters from emitted security events.
- Corrected the Docker security-event outbox startup path so containerized hosts initialize delivery reliably.
- Resolved the security and release findings raised in the v0.16.0 sprint code review.

### Fixed

- Serialized enterprise policy initialization and ignored stale policy notifications so runtime configuration and security-event transport cannot regress during concurrent refreshes; disposed configuration roots now release their policy subscriptions.
- Restored true Release builds for `ETL-SQL.Analysis`, redacted fatal CLI/TUI startup exceptions, and passed report-launch arguments without string concatenation.
- Restored the repository format gate by correcting import ordering in enterprise security and fleet-policy files.
- Propagated cancellation through warehouse and data-source schema resolution so cancelled jobs stop promptly instead of completing schema work.
- Read Portal user lists using the paged API so large directories return complete, correct results.
- Included the split connector projects in the Docker restore so container builds resolve every connector assembly.

## [0.15.0] — 2026-07-12

### Added

**SQL Logic, Parser & Correctness Fuzzing (Phases 1-4 & Hardening)**
- Shipped pure in-memory `MOCKDB()` crash-testing fuzz harness, executing up to 1,000,000 queries in under 5 minutes without memory leaks or unhandled parser faults.
- Added **NoREC (No Relation Query Evaluation)** correctness checks, automatically comparing optimized count queries against unoptimized case-when sum queries on `MOCKDB()` to assert logical execution parity.
- Added **Token Corruption & Mutation Fuzzing** (5% probability) to ensure the parser recovers cleanly with structured `SyntaxException` warnings rather than unhandled index or reference crashes.
- Extended fuzzer query walks to support advanced relational syntax: windowing functions (`ROW_NUMBER()`, inline partition/order frames, and named `WINDOW` declarations), filtering clauses (`QUALIFY` and aggregate `FILTER(WHERE...)` clauses), and advanced grouping set combinations (`ROLLUP`, `CUBE`, `GROUPING SETS`, `ALL`).
- Added diagnostics, concurrency blocks (`PARALLEL BEGIN ... END`), transactional bounds (`COMMIT`/`ROLLBACK`), system options (`SHOW`/`SET`), and global variables (`@@NOW`, `@@TODAY`).
- Integrated recursive AST expression minimizer (`QueryMinimizer.cs`) to isolate and prune crashing queries to minimal reproduction cases.
- Configured fuzzer iterations using the `ETLSQL_FUZZ_ITERATIONS` environment variable, defaulting to 500 for check-ins.

**Column-to-Column Interactive Lineage Engine**
- Built an interactive, high-fidelity Vanilla JS column-to-column lineage graph engine featuring ReactFlow-style visuals, visual mapping ports, midpoint edge badges, and column path isolation.
- Added cursor-pinned zoom math, floating details sidebar, Ctrl-Click lineage filtering, node filters, PII toggles, inline formulas, and recursive BFS column lineage traces.

**Shared Connection Governance & Secret Hardening (Phase 7)**
- Added organization-designated sensitive connection metadata and per-connection use ACLs.
- Added connection catalog with `SHARED:alias` expansion.
- Shipped Portal secret store (admin API, provider, key-ring checks).
- Created native admin services (Slice E) and lifecycle CLIs (Slice A) for secrets.
- Hardened parsing to reject unquoted `SECRET:` or `ENC:` values and lint unresolvable references.

**High Availability (HA) & Soak Certification (Phase 6)**
- Added native HA large-job soak runner, HA fault-injection runner, and CLI commands.
- Integrated HA diagnostics bundle and metrics snapshots.
- Shipped sustained load workload templates, topology harness, and evidence validation gates for pre-release verification.

**Adaptive Execution & Resource Controller (Phase 2)**
- Integrated adaptive worker admission and concurrency caps for parallel loops.
- Wired adaptive batch and memory grant setpoints based on resource sampler.
- Gated spill writes with adaptive concurrency.

**Allocation Budgets & Spill Churn Reduction (Phase 1)**
- Met Gate F round-trip performance benchmarks: +74% throughput, -63% GC allocations at scale (10M / 50M rows and 1B scale certification).

## [0.14.0] — 2026-07-05

### Added

**Enterprise Policy Enforcement & Monitoring (Phase 3)**
- Added an administrator-only policy-authority API (`api/admin/policy-authority`) to validate, version, sign, publish (staged or active), activate a staged version, emergency-rollback, and retrieve organization policies per tenant/environment, backed by a durable append-only published-version history (dual-provider SQLite/PostgreSQL migrations).
- Added machine-authenticated policy distribution (`GET api/policy-authority/envelope`): enrolled machines retrieve their signed policy using enrollment headers plus an optional TLS client certificate; responses are bound to the registered tenant/environment, and unknown, revoked, or reassigned machine identities are refused and audited.
- Added a policy-authority availability health check and signing-key-rotation tracking; publication, activation, rollback, machine revocation, and distribution denials are recorded in the durable audit trail.
- Added staged rollout and emergency rollback with monotonic issuance, so clients that reject older issuance times always converge on the newer signed version.

**Billion-Row Columnar Execution Foundations**
- Designed and implemented a native, high-performance, append-only segmented `#temp` storage engine with `ColumnBatch` buffers to bypass row-at-a-time (`Row`/`DataTable`) overhead.
- Built a process-wide memory-grant arbiter (RAM governor) backing external sort, join, distinct, aggregates, and window query operations, dynamically controlling memory ceilings and triggering partition spilling.
- Optimized spilling to use large sequential spill extents (128 MB target) to reduce file metadata and reader/writer overhead.
- Integrated bounded double-buffered pipelining to overlap extent writing with chunk production.
- Optimized projection, UTF-8 selection slicing, and key-only/numeric aggregations directly on native buffers (columnar islands).
- Added adaptive hash partition sizing, window/join fan-out scaling, and sort run extraction without boxing.
- Integrated scale certification tiers: Smoke (1 GB), Standard (4 GB, 10M rows), Stress (8 GB, 5M rows), and Huge (16 GB, 50M rows).

**Row-Level Security (RLS) & Impersonation (Phase 1 & 2)**
- Added identity system variables (`@@CURRENT_USER`, `@@CURRENT_USER_ID`, `@@REAL_USER`, `@@IS_ADMIN`) and functions/predicates `HAS_GROUP('name')` / `HAS_ROLE('name')` with default-on admin bypass.
- Added table-valued `USER_GROUPS()` and `USER_ROLES()` to query active groups/roles in joins.
- Implemented secure preview-as/impersonation for folder editors and administrators, never-cached sensitive reports, and recipient-level execution identity resolution for subscription emails.

**File Connectors & Excel Write Support**
- Added write and append support for Excel (.xlsx) files via MiniExcel.
- Enforced stream-on-the-fly decryption and decompression for FlatFile, JSON, XML, and Excel connectors.
- Added support for `.etlds` extension for exported dataset files and `.etlsnap` for Apache Arrow snapshots.

**Host Metrics & Operational Alerting**
- Added persistent host metrics tracking disk/memory/CPU capacity, a new `SHOW HOST METRICS` statement, and daily rollups.
- Added automatic reconciliation of stale RUNNING jobs as `INTERRUPTED` on startup.
- Shipped Portal operational metrics digest email.

**SFTP Connector Hardening**
- Host-key verification using `HOST_KEY_FINGERPRINT` for MITM protection.
- Opt-in atomic upload (`ATOMIC_UPLOAD = true`) uploading to temporary files before renaming.

### Changed

- **Octocolee Product Naming:** Introduced Octocolee as the product name (ETL-SQL remains the engine name).
- **Default Columnar Temp Storage:** Configured columnar temp storage by default.
- **Release Infrastructure:** Added lightweight secret scan, SBOM generation, and pre-release gates.

### Fixed

- **Parser and Security Fixes:** Sanitized `QuoteIdentifier` routines to prevent SQL injection.
- **VS Code Extension & TUI Fixes:** Fixed VS Code extension vulnerabilities, terminal command builder escape bugs, and resolved window resize lag/input blocking on Unix in the TUI.

### Security

- **Execution Policy Enforcement Boundary:** Added execution policy snapshot context (`ExecutionPolicySnapshot`) and dynamic policy validation.
- **Shared Enforcement Snapshot:** An immutable policy snapshot is captured when execution begins and propagated unchanged through CLI, TUI, Report Player, Portal, Orchestrator, parallel branches, recursion, and scheduled jobs, making denials deterministic across in-process and spawned execution.
- **Governed Connector Egress:** Enforced enterprise connector-type, destination host, scheme, and port allowlists before DNS resolution and connection creation, including dynamic REST redirect/pagination/template targets. Local egress denials surface as a plain security error; organization-policy denials carry the governed key and correlation identity.
- **DNS-Rebinding & Proxy-Bypass Hardening:** The REST connector re-validates the DNS-resolved address at connect time and pins the socket to the validated set, and disables ambient proxy use — closing rebind-to-internal-IP and proxy-bypass paths. Obfuscated IP literals are normalized and loopback/link-local/private/CGNAT/ULA ranges are denied unless explicitly listed; URL-embedded credentials are rejected regardless of policy.
- **Filesystem Policy Boundary:** Restricted local paths in remote file transfers, directory synchronization, and recursive file/directory operations. `COPY FILE` and recursive directory copy stream through handle-validated opens (OS-resolved final-path re-check after open) to resist link-substitution races; delete/move/copy re-authorize immediately before the OS call.
- **Governed Resource Ceilings:** `MAX_PARALLEL_DEGREE`, `MAX_FILE_OPERATIONS`, `MAX_RECURSIVE_DEPTH`, `MAX_SMTP_EMAILS_PER_SCRIPT`, and `MAX_STRING_RESULT_SIZE` cannot be weakened by `SET`, configuration, environment variables, command-line options, restored sessions, or report parameters; the enterprise ceiling is bound from the immutable execution snapshot at execution start and re-checked at each operation boundary.
- **Allowed Extension Tightening:** Removed generic `.tmp` from whitelisted user file extensions to prevent insecure temp file usage.

## [0.13.0] — 2026-06-28

### Added

**Apache Arrow Snapshot Integration**
- Completed end-to-end Apache Arrow IPC snapshot support: the `SnapshotStore` now saves and loads secure `.etlsnap` zip packages by default in CLI and local execution contexts.
- Local and CLI snapshot packaging runs without explicit key configuration by falling back to host-bound at-rest encryption (see Security for the hardened behavior).
- The report runtime player now lazy-loads and decodes Arrow IPC streams on-demand with automatic fallback to JSON row endpoints for older clients.
- Downloaded and bundled the minified Apache Arrow JS library (`arrow.min.js`); synchronized front-end runtime assets across Portal, Player, and VS Code extension.
- Added test coverage verifying CLI/local `.etlsnap` roundtrip packaging.

**Portal Execution Metrics & Observability**
- Added persistence of per-execution resource metrics (CPU, memory, duration) to the Portal database so historical load can be trended over time (`AddPortalExecutionResourceMetrics` EF migration for both SQLite and PostgreSQL).
- Exposed a historical execution load metrics endpoint on `AdminController` for operators and monitoring systems.
- Added lazy-loading of Arrow snapshot rows in the Portal to avoid pulling large result payloads into memory until requested.

**`SHOW PORTAL USAGE METRICS` and `SHOW PORTAL OPERATIONAL METRICS` Statements**
- Added `SHOW PORTAL USAGE METRICS [INTO #t]` inside an `EXECUTE portal` block to return report view counts, unique viewers, refresh health, and subscription delivery failures for the requested period.
- Added `SHOW PORTAL OPERATIONAL METRICS [INTO #t]` to return live queue depth, execution concurrency caps, recent failure counts, storage size, schema migration status, and last-24-hour execution load/resource buckets — complementing the existing `GET /health` endpoint with a scriptable, queryable form.
- Wired both statements through the parser (`SystemParser`), AST (`ShowPortalUsageMetricsStatement`, `ShowPortalOperationalMetricsStatement`), and `PortalDataSource`; updated `PORTAL_SHOW.md` help file, `Grammar.md`, and `Syntax_Index.md`.

**`SHOW LOCKS` Statement**
- Added `SHOW LOCKS` to display currently held engine-level and orchestrator-level resource locks, aiding live diagnosis of stalled pipelines and contention scenarios.
- Documented `SHOW LOCKS` in `Grammar.md`, `Syntax_Index.md`, `User_Manual.md`, `PORTAL_SHOW.md` help file, and the `SHOW` keyword help document; wired a corresponding test in `SystemAndReportHandlerTests`.

**LSP Cross-File Declaration Resolution**
- Extended the Language Server's `DefinitionProvider` and `HoverProvider` to resolve `GO TO DEFINITION` and hover targets across all currently open files in the workspace, not just the active document.

### Changed

**Performance — Engine & Language Server**
- Indexed lineage in `LineageTracker` and cached parameter scans in `ParameterScanner` to avoid repeated linear walks during analysis and execution.
- Added parse-result caching to `RunScriptStatementHandler` so `RUN SCRIPT` targets that have not changed on disk are not re-parsed on every invocation.
- Cached LSP definition declarations in `DefinitionProvider` and `DocumentStateStore` to avoid redundant re-analysis on every keystroke.
- Hardened Portal metrics and scaled hot paths: added `AssetFingerprinter`, tuned spill-store and external sort/join engines, and improved scheduler throughput under load.

**Machine-Aware Orchestrator Throttling & Startup Sweep**
- `JobThrottle` now reads available logical processors and physical memory at startup to derive a machine-aware default concurrency ceiling, preventing over-subscription on small VMs.
- Added `ChildProcessTracker` to associate child processes spawned by the Orchestrator with their parent job, enabling clean resource reclamation on job cancellation.
- Added a startup temp-table sweep in `EngineRunner` to remove orphaned `#temp` working directories left by crashed sessions, preventing unbounded disk growth.

**Stabilization & Refactoring (Engine, Analysis, Portal, TUI, Tooling)**
- Completed a broad stabilization pass across the engine: audited and hardened all `ETL-SQL.Engine` statement handlers, `RelDateResolver`, `ResultFormatter`, `SessionStateManager`, `VariableScopeManager`, `CteManager`, `PushdownEngine`, `QueryCompiler`, `DataSourceManager`, `LineageManager`, and `SpillStore`.
- Hardened the `AliasScanner`, `SnippetLibrary`, and `SnapshotStore` in `ETL-SQL.Core` and `ETL-SQL.Reporting`; made the `sync-assets.js` asset-sync script idempotent and banner-aware.
- Tightened `AbsolutePathRule`, `CredentialLeakRule`, and `FileSystemSecurityRule` linting rules with additional corpus cases for path boundary and credential-leak scenarios; strengthened `SchemaValidationRule` in Analysis.
- Hardened `CryptoUtils`, `MachineBoundCrypto`, and `LruCache` in `ETL-SQL.Core.Common`; hardened `SqliteSessionMetadataStore` with retry semantics and tighter WAL mode configuration.
- Hardened engine cleanup and path handling across `RunScriptStatementHandler`, `ExecuteStatementHandler`, `BundleStatementHandlers`, `WaitForFileStatementHandler`, `CteManager`, `ProcedureExecutor`, and `SessionStateManager`.
- Hardened async export and backup paths in `BackupRestoreService`, `EngineRunner`, `BrowserReportPdfExporter`, `ExportController`, and the TUI `ConsoleEditor`.
- Added `AssetFingerprinter` to the Portal for cache-busting on static asset updates; added EF migration for PII column encryption on both SQLite and PostgreSQL providers.
- Stabilized `JobApiEndpoints` with improved cancellation propagation and error surfacing; tightened `NodeCapacityMonitor` assertions and added `SchedulerService` queue-wait-time argument fixes.

**TUI Frame Metadata Caching**
- `EditorRenderer` now caches rendered frame metadata between redraws, reducing CPU usage during idle periods and making the status bar and key-binding overlays allocation-free on unchanged frames.

**Documentation & Policy**
- Reconciled identity configuration reference in `Administrators_Guide.md` to match shipped OIDC behavior.
- Tightened contribution rules and compatibility policies in `CONTRIBUTING.md`.
- Documented future performance and scalability enhancements in `TODO.md`.

### Fixed

- **Support bundle redaction**: `SupportBundleBuilder` now redacts connection-string passwords, API keys, and JWT secrets from all diagnostic fields before archiving; added corresponding `OperatorToolingTests` coverage.
- **Portal database migration test failures**: Resolved a portal database upgrade migration ordering issue and fixed a metric timezone normalization bug that caused flaky test failures under certain locale configurations.
- **SFTP connector `ConnectionStringBuilder`**: Corrected option serialization for `SFTP` connector key-file auth paths.
- **TUI frame caching**: Fixed stale frame metadata being rendered after connection or tab changes in `EditorRenderer` and `StatusBar`.
- **Migration lint corpus**: Added a migration lint corpus (`test(compat)`) to catch invalid dialect usage introduced across schema migration scripts.
- **Scheduler test mock**: Fixed `SchedulerService` test mocks that passed an incorrect argument count for the queue-wait-time parameter after an API change.
- **GROUP BY ALL column expansion**: Resolved a bug in `SelectStatementHandler` where `GroupByAll` was expanded before output column expansion, resulting in engine crashes when star-modifiers (`* EXCLUDE (...)`) or qualified stars (`t.*`) were present in the query.
- **Positional reference star projection checks**: Hardened positional reference checks in `Parser.ResolvePositionalReference` to correctly identify and block qualified star and star-modifier projections from bypassing positional sorting/grouping syntax checks.

### Security

- **PII column encryption at rest**: Portal database columns storing user PII (email addresses, display names in audit records) are now encrypted at rest using a key derived from the configured Data Protection key ring, applied via a background maintenance service and corresponding EF Core migration for both SQLite and PostgreSQL.
- **Support bundle hardening**: Connection strings, JWT secrets, and API keys are now actively redacted from the support bundle rather than relying solely on config-key exclusion lists.
- **Crypto hardening**: Strengthened `MachineBoundCrypto` key derivation and `CryptoUtils` authenticated-encryption paths; added additional test coverage for encrypt/decrypt roundtrips and tamper-detection.
- **Service Account token exchange timing mitigation**: Hardened the service-credentials token endpoint against client-ID enumeration timing attacks by always executing password verification against a dummy hash when the Client ID is not found or is inactive.
- **Client certificate store handle leak cleanup**: Resolved an OS handle leak in `EnterprisePolicyRuntime` during OIDC/HTTPS policy certificate store searches by disposing non-matching certificate instances.
- **Egress sanitization & parameter utility ReDoS hardening**: Hardened regular expressions in `ConnectorExceptionWrapper` and `ParameterUtility` to use source-generated regex `[GeneratedRegex]` with a `1000ms` timeout to protect against catastrophic backtracking.
- **Snapshot at-rest encryption fallback hardening**: When `Portal:Dataset:AtRestKey` is unset, report snapshot (`.etlsnap`) packages now fall back to the same host-bound `ENCRYPT=MACHINE` protection used for dataset caches (DPAPI LocalMachine on Windows; authenticated AES-256-GCM keyed from the machine id elsewhere), instead of a source-public default key. Reading a key-managed snapshot now fails closed if the key is absent. `MachineBoundCrypto.Protect/Unprotect` are exposed for reuse, and a one-time warning is logged when the host-bound fallback is in effect.
- **Authenticated machine-bound generic encryption**: `CryptoUtils` machine-key protection on platforms without DPAPI is now encrypt-then-MAC (HKDF-SHA256 encryption/MAC sub-keys + HMAC-SHA256 verified in constant time) instead of unauthenticated AES-CBC; legacy CBC-only payloads remain readable.
- **`machine.key` permissions**: the generated machine key file is now created owner read/write only (`0600`, directory `0700`) on Unix, atomically, so it is never briefly world-readable.

## [0.12.0] — 2026-06-19

### Added

**Practical High Availability — Multi-Node Portal & Orchestrator**
- Made both the Portal (EF Core) and Orchestrator (hand-written) state stores **provider-selectable** between SQLite (default, unchanged) and PostgreSQL via configuration (`Portal:Database` / `Orchestrator:Database` Provider + ConnectionString), removing the previously hardcoded SQLite coupling. PostgreSQL is implemented end to end for both stores and verified against a real Postgres via Testcontainers: the Portal gained a dedicated migrations assembly for Postgres, and the Orchestrator store became a provider-neutral `RelationalJobHistoryStore` behind a dialect (portable SQL, with a Postgres `nocase` ICU collation backing `COLLATE NOCASE`).
- Added `etl-sql admin migrate-database --from sqlite --to postgres [--dry-run]` to copy existing single-node SQLite Portal/Orchestrator state into the configured PostgreSQL deployment: values are coerced to each target column's type, foreign-key ordering is bypassed for the load, identity sequences are resynced, and per-table row counts are verified — any mismatch fails closed (nothing is committed). `--dry-run` verifies counts and target-schema compatibility without writing.
- Added a unified `IArtifactStorage` interface with **Local** and **SMB/UNC** providers so reports, scripts, snapshots, and custom-map assets live on a shared root reachable by all nodes, with `SecurityService` guardrails enforced at the storage boundary.
- Added database-backed cluster coordination: **node heartbeats and a cluster registry** (liveness on the database clock, with expired rows pruned on the heartbeat loop), **monotonic fencing tokens** for state and shared-storage writes, and **database-backed leader election** that serializes migrations and singleton work. Stale writers are fenced and in-flight portal work is cancelled on node lease loss.
- Added per-node capacity gating with **job quarantine**, cross-node capacity claims, and snapshot write-failure recovery.
- Added a scalable **HAProxy** docker-compose with sticky (session-affinity) balancing, a configurable shared Data Protection key ring, and a lightweight `GET /healthz` load-balancer probe (richer diagnostics remain on `GET /health`). HA clusters require a shared artifact root, a shared key ring, identical JWT/orchestrator/dataset keys across nodes, and load-balancer session affinity for node-local interactive sessions.

**Job-Scoped State Persistence & Incremental Watermarking**
- Implemented `GET_JOB_STATE(key)` and `SET_JOB_STATE(key, value)` primitives for scheduled and ad-hoc incremental data loads.
- Buffered state updates during execution, committing them atomically to the orchestrator store (SQLite or PostgreSQL) only upon successful script completion.
- Added a developer CLI fallback that persists state in local `[script_name].etlstate` JSON files.

**JSON/Spec-Backed Schema Contract Checks**
- Extended the `EXPECT SCHEMA` syntax to validate schemas using a reviewed JSON specification contract file: `EXPECT SCHEMA target FROM 'path/to/spec.json' [ON DRIFT WARN];`.
- Added support for verifying column presence, type family matching, nullability constraints, string length limits, and decimal precision/scale settings loaded from the JSON `"schema"` array, respecting `context.ResolvePath()`.

**Certified OpenID Connect (OIDC) Authentication**
- Implemented federated login, logout, and token refresh in the Report Portal with support for external Identity Providers.
- Hardened user account binding by keying local profiles to the immutable OIDC `sub` (subject) claim to prevent takeover risks if usernames/emails are reassigned.
- Added dynamic group mapping to synchronize identity provider role/group claims to local Report Portal user groups at login.
- Added configuration diagnostics and redacted status checks to ensure OIDC provider availability can be monitored without exposing client secrets.
- Certified recovery scenarios (IdP outages, JWKS key rotation, claim modifications, and token revocation) with a robust integration test suite.

**VS Code Extension Enhancements**
- Cleaned up ESLint static analysis and type declarations across TypeScript sources.
- Stabilized the extension integration test suite by tuning Mocha bootstrap timeouts to accommodate headless environment activation delays.

### Changed

**Pushdown Aggregation & Staged Extracts**
- Enabled SQL pushdown for eligible `SELECT ... INTO #temp` queries containing `GROUP BY`, aggregates, `DISTINCT`, and compatible joins. Pushes aggregation down to the source database and streams only grouped/filtered results back.

**Cross-Connection Semi-Join Pushdown**
- Added an optimizer that rewrites joins between small local temp tables (1-1000 rows) and large remote SQL tables to push a parameterized key filter (`IN` clause) directly to the remote query, preventing full-table memory loading.
- Optimized compiling of the query key list using driver-parameterized values (`@p0`, `@p1`, etc.) to leverage caching and prevent injection, with plan visibility under `[SEMI-JOIN PUSHDOWN ON ...]`.

**Evaluator Performance Enhancements**
- Optimized hot-path identifier and column resolution by switching to allocation-free `Row.TryGetValue` instead of copying new row columns dictionaries, saving significant heap allocation during streaming query execution.
- Avoided redundant column lookups during variable and identifier evaluations using a unified `TryResolveIdentifier` check.

### Fixed

**Test Stability**
- Stabilized two timing-sensitive Docker integration-lane tests that failed intermittently only under full pre-release load: relaxed a `Retry-After` delay assertion to tolerate the ~15.6ms Windows timer quantum, and raised the orchestrator scheduled-job history poll timeout above the container's own job timeout so a job nearing its budget under load is not abandoned prematurely.

## [0.11.0] — 2026-06-14

### Added

**Secure Datasets**
- Reworked the DATASET subsystem for multi-user safety: globally unique dataset names with stable-Id storage paths, dataset→folder linkage where `PUBLIC` resolves to folder-read permission, and caller-identity threading that closes an ACL bypass.
- Added portal-managed at-rest encryption for the dataset cache (parquet encrypted at rest), failing closed on a missing or weak at-rest key, with at-rest key rotation and a verification deck.
- Added `EXPORT DATASET` (a portable transport-encrypted copy) and `PUBLISH DATASET` (import a portable file and re-encrypt at rest).
- Added serve-stale-with-warning behavior plus an editor/owner refresh gate, refresh triggers, and authorization/atomicity hardening.

**Script-First Portal Reconstruction**
- Added `EXPORT PORTAL CONFIGURATION` to export users, groups, memberships, folders, ACLs, report publications, dataset metadata/grants, SMTP aliases, subscriptions, and alerts as a versioned, idempotent `.etlsql` bootstrap script that emits logical names (never database IDs).
- Excluded all credentials, keys, and cached values from the export, emitting `${...}` secret placeholders with a generated requirements header.
- Made bootstrap import deterministic and rerun-safe (create-or-skip by logical name) with `SET WHAT_IF ON` dry-run validation that fails closed on missing secrets or references.
- Added a companion content manifest / recovery runbook, and an automated clean-server round-trip reconstruction proof.

**Multi-User Correctness & Recovery**
- Fixed the folder/asset ownership lifecycle (ownership now implies Manage) with explicit ownership transfer/reassignment before user deletion.
- Made audit recording part of the operation contract: security-sensitive mutations and their audit rows now commit atomically, with correlation IDs for background work and opt-in retention.
- Added a durable per-job execution lease (Orchestrator), a recoverable subscription lifecycle, and a durable subscription delivery ledger with at-most-once semantics and idempotency/failure tests.
- Added per-user execution fairness limits, scriptable SMTP connection management, refresh-token reuse detection/purge with cached-token validation, and bounded report-snapshot retention.

**Operator Tooling (CLI)**
- Added an `etl-sql admin` command group with `admin doctor` (a backward-compatible alias of `doctor`) and `admin support-bundle`, which produces a credential-redacted archive (config, health snapshot, recent logs, database metrics).
- Added `etl-sql init` to scaffold a starter configuration (with a generated JWT secret) and a first runnable `.etlsql` script for CLI-first onboarding.
- Added `etl-sql admin backup` (split-custody data + keys archives) and `etl-sql admin restore` with fail-closed `--validate` (matching backup-id pair, key-version coverage, per-file checksums, and version compatibility).
- Surfaced database schema migration status on the operational metrics endpoint, and wired the N→N+1 in-place upgrade-path drill into `Test-PreRelease.ps1` as a release gate.

**Verification & Observability**
- Added a hosted-service integration lane, genuine multi-process coordination tests, fault-injection/recovery tests, an automated backup/restore drill, and an admin operational metrics endpoint (queue depth, active executions, failure rates, dataset/snapshot disk usage).

**Language & Engine**
- Added inline tags in `CREATE TABLE` and `INT(N)` fixed-width digit precision.
- Added a memory-grant arbiter, tag value validation, and lineage cycle warnings.

### Changed

- **Licensing:** Relicensed ETL-SQL from PolyForm Noncommercial 1.0.0 to the Apache License 2.0 and aligned the installer, VS Code extension metadata, bundled browser assets, contribution policy, and public documentation.
- **Documentation validation:** Added connector-aware checks for `CREATE CONNECTION` examples so unsupported option names and published option values fail the documentation test suite instead of passing grammar-only validation. Connector metadata now exposes supported named `PATH`, `HOST`, and flat-file truncation options used by public examples.
- Formalized automatic SQLite schema migrations on Portal startup: the applied migration set is logged and a migration failure now fails fast rather than serving a half-migrated catalog.
- Realigned the `CREATE` `ENCRYPT` clause as transport-only and removed the cleartext-credential dataset-refresh sidecar.
- Adopted an optimistic-concurrency contract for concurrent administration, batched dataset-listing permission checks for performance, and refreshed branding, trademark, logo, and README positioning.

### Fixed

- Resolved FLATFILE connectors with EXCEL/JSON/XML/PARQUET/AVRO formats to their correct dialects in `PipelineGenerator`, and fixed a `FlatFileDataSource` compiler error.
- Fixed `SessionCache` race leaks and stale admin caller context, a refresh debounce race, and disabled accounts surviving LDAP login; removed the hardcoded first-run admin password.
- Corrected dataset at-rest encryption metadata to be truthful, required Manage to change dataset access level, and regenerated the dataset-refresh-permission migration via EF tooling.

### Security

- Backup secret artifacts (keys archive, key ring, re-injected config) are written with owner-only permissions, and backup manifest validation rejects path-traversal entries.
- Hardened portal sessions and anonymous delivery, added authentication rate limiting and a content security policy, and added runtime secret rotation.
- Closed authentication, SSRF, injection, key-handling (.p8), and audit release blockers; added Dependabot for the NuGet and npm ecosystems.

## [0.10.0] — 2026-06-08

### Added

**Experimental: Specification-Driven Development (Beta)**
- Added `gen-script` CLI command to compile standardized JSON specification contracts into ETL-SQL starter scripts. Generated templates include source layout review notes, confidence/source-evidence comments, casting expressions, inline lineage tags, `EXPECT SCHEMA` gates, validation issue summaries, optional quarantine tables, and outbound load scaffolding.
- Added `extract-spec` CLI command utilizing PDFsharp to automatically trim and extract data dictionary pages from large vendor PDF documents using heuristic keyword scoring.
- Added workflow guide `Docs/Reference/Spec_Driven_Development.md`, prompt instruction guide `Docs/data_spec_parser_instructions.md`, machine-readable contract `Docs/Reference/spec_pipeline.schema.json`, and Cookbook recipe 25 with a runnable customer-feed example.
- Added [PipelineGenerator](./src/ETL-SQL.App/App/PipelineGenerator.cs#L14) and [SpecExtractor](./src/ETL-SQL.App/App/SpecExtractor.cs#L12) test suites under `tests/ETL-SQL.Tests/App/` covering contract validation, generated-script parsing, review metadata, validation gates, and PDF trimming scoring.
- *Note on limits*: This is a developer productivity feature, not an automated production-pipeline generator. LLM spec parsing and vendor formats are variable; generated scripts are intended as reviewed starting points. Developers must verify the JSON, complete the extraction query, review evidence/low-confidence fields, and test against real vendor files.

**Terminal IDE (TUI) Modernization**
- Implemented collapsible sidebar file explorer tree and tabbed multi-file support in [ConsoleEditor.cs](./src/ETL-SQL.TUI/UI/ConsoleEditor.cs#L29).
- Added support for multi-cursor editing, F1 help dialog shortcuts, and drag-to-select text in the editor.
- Added in-editor text find/search with result highlighting and `F3`/`Shift+F3` navigation.
- Added live query diagnostics while editing and visual gutter diagnostic markers.
- Added non-blocking, cancellable script execution, allowing queries to run asynchronously in the background.
- Added a Schema Explorer in the sidebar showing database tables and views with lazy loading support.
- Added a Variables explorer tab in the bottom pane matching the VS Code Variable Explorer functionality.
- Added query result-cell navigation and inspection, along with cell-value inspection popups.
- Added automatic workspace persistence and recovery, preserving open files and tabs across TUI restarts.
- Added customizable JSON-based editor themes with a preset theme library and `F3` theme-cycling hotkey.
- Re-implemented robust console keyboard input via Win32 ReadConsoleInput, resolving terminal input lockups.
- Added per-tab caching for query results, execution messages, active execution tree, and performance metrics.
- Added a new `rollback-all-transactions` command to abort all active transactions.
- Added an Output tab to act as a durable, clickable home for served URLs and export paths.
- Added custom terminal rendering features including braille line charts, fractional-block bar charts, buttons, containers, and `RELDATEPICKER` controls.
- Added a TUI Command Palette (`Alt+P`) and support for exporting reports directly to Markdown or PDF.
- Added a `serve` utility (`Ctrl+Shift+R`) to run report previews directly in the browser via dynamic self-invocation, supporting serve-folder multi-report launching.
- Added Publish to Portal support (matching VS Code publish features) and connection reset commands.

**Connectors & Integrations**
- Added a native **Neo4j** graph database connector supporting key merging, validation, and metadata queries (see [Neo4jConnector.cs](./src/ETL-SQL.Connectors.Databases/Neo4j/Neo4jConnector.cs) and [Neo4jDataSource.cs](./src/ETL-SQL.Connectors.Databases/Neo4j/Neo4jDataSource.cs)).
- Added outbound writing support and completed production gaps for the REST API connector.
- Enhanced Azure Blob, SFTP, S3, and local Directory connectors to include fallback decryption and structured path parsing.

**Language, Lineage & Governance**
- Added `CREATE TAG` and `CREATE LINEAGE FROM ...` syntax to support programmatic importing of curated lineage assets and metadata tags.
- Added the `DIFFERENCE(s1, s2)` Soundex similarity scoring string function (see [FuzzyFunctions.cs](./src/ETL-SQL.Engine/Functions/FuzzyFunctions.cs)).
- Added a cross-platform CLI `etl-sql purge` command for cleaning up old data and session histories.
- Expanded SQL Logic Test (SLT) coverage for index creation, table truncation, table alteration, `LEFT SEMI`/`LEFT ANTI` joins, and `QUALIFY` statements.

**Verification & Orchestration Hardening**
- Added job scheduler chaos coverage and concurrency race verification tests (scheduler, subscription, and active-work).
- Added a subscription delivery diagnostics UI and preserved subscription failures in the history store.
- Added verification tests for Report Portal user permission models and user workflows.
- Added a new capacity planning guide (`docs/architecture/roadmaps/Capacity_Planning.md` or similar) and published service capacity baselines.
- Added capacity workload templates and row-volume capacity planning profiles.
- Added scaling tests for portal administration catalogs and enterprise identity lifecycle verification.

### Fixed
- **Query Parser:** Fixed parser bugs for `LEFT SEMI`/`LEFT ANTI` joins and tolerated trailing semicolons (`;`) for statements inside `BEGIN`/`TRY` blocks.
- **Cookbook Recipes:** Audited and fixed all 23 Cookbook recipes to ensure they compile and parse cleanly, fixing issues with `ENCRYPT`, `SEND EMAIL`, `EXEC`, `DECLARE`, and deprecated `WITH PARAMETERS` report options.
- **TUI Editor:** Implemented file overwrite warnings when a file changes on disk, fixed sidebar layout wipeout during redraw by clearing partial line width, and resolved keyboard input lockups on Windows.
- **TUI Autocomplete:** Fixed snippet triggers (`$mssql`) showing inside the autocomplete suggestions and prevented crashes when brackets appeared in prompt titles.
- **TUI Metadata:** Restored temp table querying inside [TuiMetadataManager](./src/ETL-SQL.TUI/UI/SuggestionProviders.cs#L106).
- **Report Preview:** Fixed report preview wrapping bugs, added rounding for Card/Table numbers, and added page navigation arrows via keyboard/mouse.
- **Test Integrity:** Resolved parallel test conflicts in Neo4j tests, and excluded Docker LDAP portal tests from non-Docker lanes.

### Changed
- **Dependencies:** Upgraded `SQLitePCLRaw` package reference to `3.0.3` to resolve pre-release auditing and scoped it exclusively to Core instead of globally.
- **Code Refactoring:** Refactored `ConsoleEditor` dependencies to use dependency injection instead of service-locating patterns.
- **Platform Infrastructure:** Hardened shell scripts and systemd unit files to use Unix LF line endings.
- **Packaging:** Brought the Linux `.deb` installer to parity with the Windows MSI (including uninstall prompts and service configuration) and published VSIX as a standalone asset.
- **Release Tooling:** Made the pre-release NuGet dependency audit reliable on the pinned .NET 10.0.300 SDK with central package management — solution-level `--deprecated`/`--vulnerable` checks fall back to per-project auditing and fail with an actionable message rather than silently skipping when no authoritative audit can run.

### Security
Hardening from the v0.10.0 release-readiness security review:
- **Orchestrator API authentication:** The ad-hoc job API (`POST /jobs`, `DELETE /jobs/{id}`, `GET /jobs/{id}`) now requires the `X-Orchestrator-Key` header like the scheduled-job and management routes; only `/health` and `/metrics` remain open. The service fails fast at startup when no API key is configured while bound to a non-loopback address, and the MSI/Linux installers generate and mirror matching `Orchestrator:ApiKey` / `Portal:Orchestrator:ApiKey` values.
- **Spec module injection:** Restricted spec dataset names to a documented safe-identifier format, normalized each generated module path to stay within the modules directory, and escaped generated ETL-SQL string literals — preventing path traversal and ETL-SQL injection in `gen-script` output.
- **REST egress / SSRF:** Disabled automatic HTTP redirects in the REST connector; redirects are now followed explicitly with a bounded count, every hop's host is re-validated against the egress allowlist, and credential headers are stripped on cross-host or HTTPS→HTTP redirects.
- **Path Validation:** Enforced zero-trust path validation for the Snowflake `PRIVATE_KEY_FILE` option while accepting the documented `.p8` PKCS#8 key extension.
- **Token Permissions:** Restricted portal token file permissions strictly to the owner.

---

## [0.9.0] — 2026-06-01

### Added

**Reporting: Export Fidelity**
- Server-side ECharts SSR export path: report chart visuals can render real ECharts output into SVG for PDF generation.
- PDF export now includes chart-rendering coverage through `EChartsSsrRenderer` and `PdfExporter` tests, including a PDF magic-header assertion and chart visual rendering path.
- Markdown/table export formatting tightened through the shared report cell formatter so exported tables preserve cleaner display values across report outputs.

**Language: Pipeline Checkpoint / State Resume**
- `LabelName:` syntax as `SectionLabelStatement` — top-level labels auto-serialize `#temp` table contents (Apache Arrow spill) and variable scope (JSON) as named checkpoints.
- `GOTO LabelName;` control-flow statement with full scoping guardrails: GOTO may jump OUT of nested loops, conditionals, and `TRY…CATCH` blocks; jumping INTO nested blocks is a compile-time error; cross-script jumps blocked.
- `--session <id>` and `--resume` CLI flags: `--session` names the state store; `--resume` restores the most recent checkpoint and skips already-completed labels. Passing `--resume` without `--session` or without a saved checkpoint is a fail-fast error.
- LSP: section labels exposed in document outline for folding and symbol navigation; `GOTO` autocomplete lists reachable label names.
- Grammar, User Manual, and Specialized_Operations.md updated with label/GOTO syntax, scoping rules, and `--resume` CLI reference.

**Connector: Native MySQL / MariaDB**
- `MySqlConnector` provider built on the `MySqlConnector` NuGet package — eliminates the ODBC bridge dependency, delivers native dialect parsing, and wraps all provider exceptions as sanitised `ExecutionException`s at the connector boundary.
- Procedure/routine metadata discovery via `MySqlCatalogProvider`.
- Dedicated `MySqlFixture` / `[Collection("MySQL")]` so non-MySQL database tests no longer pay MySQL container startup cost.
- Third-party inventory updated with MySqlConnector 2.3.7 and Testcontainers.MySql 4.11.0.

**Diagnostics: EXPLAIN / EXPLAIN ANALYZE**
- `EXPLAIN <statement>` produces a query-plan table (ID, Operation, Details, Cost, Mode, Est. Rows).
- `EXPLAIN ANALYZE <statement>` adds Actual Rows, Actual Time, and Spill (bytes) columns by executing the statement under instrumentation.
- Available as a `--explain` CLI flag for whole-script plan output.

**Observability: Spill & Memory Metrics**
- `--perf` summary table now includes a "Disk Spilled: X MB" row.
- `--verbose` JSON telemetry packet includes `spilledMb`.
- `SHOW PROFILE` tracks `SpilledBytes` per statement alongside elapsed time and row counts.
- `ExecutionTelemetryManager` exposes `TotalSpilledBytes`, `SubquerySpilledBytes`, and `SortSpillCount` for downstream reporting.
- `Docs/Reference/Performance.md` (new): all four external engine thresholds and activation conditions, `SET` threshold overrides, `appsettings.json` defaults, spill storage and encryption, observability reference, memory model, tuning guidance table, and scale certification tier definitions.

**Governance: Execution Audit Log for Ad-Hoc Runs**
- `Engine:AuditAdHocRuns` appsetting (default: `false`) gates audit logging for standalone `--run` executions.
- When enabled, `EngineRunner` calls `IJobHistoryStore.LogJobStartAsync` / `LogJobEndAsync` so script runs appear in the Orchestrator execution history alongside scheduled jobs.

**Release Infrastructure**
- `scripts/Test-PreRelease.ps1`: local pre-release validation runner with resumable phases (source-hash fingerprinting prevents reusing stale results after code changes). Phases: sync-assets drift, restore, build, smoke/fast test lanes, Node.js unit tests, sample smoke, Smoke-tier scale cert. Optional switches: `-IncludeDockerIntegration`, `-IncludeStandardScale`, `-BuildInstallers`, `-SkipNode`, `-SkipScale`, `-Resume`.
- `scripts/Compare-CertBaseline.ps1`: diffs a `cert-report.json` against a stored baseline — exact pass/fail, result-row count, checksum, and elapsed-time regression (±50% threshold). Exits 1 with a regression table on any failure.
- `docs/architecture/roadmaps/Release_Capability_Matrix.md`: release claim matrix tying public product claims to concrete evidence and preventing release notes from overstating tested behavior.
- `scripts/Get-TestLaneInventory.ps1`: static lane inventory report showing discovered xUnit tests by lane, category trait, project, and fast-lane exclusion reason.
- `perf` lane now runs engine hardening performance tests plus the dedicated perf project; `fast`, `portal`, and `full` lanes include the Node lineage UI smoke test.
- Scale certification baselines committed: `certification-results/baseline-smoke.json` (Smoke, 1×) and `certification-results/baseline-standard.json` (Standard, 10×, 13 scenarios, all passing).
- `.github/CODEOWNERS` and Dependabot configuration added.
- Four GitHub workflow templates under `.github/workflow-templates/` (local-validated-release, manual-docker-certification, manual-release-validation, manual-scale-certification) — staged for future activation; not yet wired to automatic triggers.
- `docs/architecture/roadmaps/Release_Workflows.md` documents the local-first release ownership model and workflow template activation guide.
- Windows release packaging scripts hardened for reliable local/CI builds: resolved WiX tool lookup, WiX 3.x Program Files discovery, explicit MSI failure handling, and local validated release workflow WiX installation.

**Documentation**
- `Docs/Architecture/Lineage.md` (new): what is tracked, `LineageEntry` data model, `SHOW LINEAGE` syntax variants, Mermaid and OpenLineage export, `SHOW LINEAGE HISTORY` cross-run catalog, metadata inheritance rules, and Orchestrator (`etlsql.db`) integration.
- `Docs/Reference/Performance.md` (new): see Observability above.
- `docs/architecture/roadmaps/Release_Workflows.md` (new): see Release Infrastructure above.
- Architecture documentation expanded for connector, engine, expression evaluation, language server, lineage, orchestrator, parser/lexer, portal UI, report portal, reporting, TUI editor, variable scoping, and VS Code extension boundaries.
- `docs/guides/testing.md`, `docs/architecture/roadmaps/Test_Strategy.md`, and `scripts/README.md` reorganized around the current lane model, pre-release phases, SLT usage, coverage expectations, and installer prerequisites.
- Connector standards and reference docs corrected for current connector option naming rules, supported connector inventory, and source-boundary guidance.

**Tests**
- `ResumeEdgeCaseTests.cs` — 5 integration tests covering: fail-fast on IsResuming without checkpoint; fresh-variable guarantee on `--session` without `--resume`; GOTO keyword-target parse diagnostic; SaveSession graceful return for non-Evaluator contexts; mid-script resume uses loaded checkpoint state.
- `ParserErrorQualityTests.cs` — 17 parameterized cases across 4 constructs (GOTO, CREATE CONNECTION, SEND EMAIL, RUN SCRIPT) asserting error messages name the construct and expected token.
- `ExampleOutputCorrectnessTests.cs` — 6 assertion-based tests verifying correct output (row counts, column values, specific cell values) for self-contained scripts in `01_Basics/` and `07_Real_World/`: function library, window deduplication, incremental MERGE, data masking, anti-join reconciliation, and PIVOT.
- `CrossHostConsistencyTests.cs` — verifies that the same `.rptsql` fixture produces identical manifest structure (title, visual count, visual names, row counts, column names) when executed via `DashboardService` directly and via the Portal API execute → snapshot path.
- `MySqlTests.cs` — Docker real-integration tests for the new native MySQL connector.
- ETL scenario golden tests expanded to 27 scenarios covering staged ETL, cleansing, JSON extraction, file round trip, lineage tags/source columns, `WHAT_IF`, loops, `TRY...CATCH`, transactions, DML audit, merge, hash-change detection, set ops, recursive CTE, pivot/unpivot, semi/anti joins, and modular scripts.
- SLT release evidence added for custom ETL-SQL semantics plus the explicit `slt` lane; the release branch SLT lane passed on 2026-06-01.
- Docker-backed integration lane audited and stabilized; the release branch integration lane passed on 2026-06-01 with 97 tests covering connector and platform service boundaries.
- Standard scale certification evidence recorded on 2026-06-01: 13 scenarios passed at 10× row scale.
- Windows package evidence recorded on 2026-06-01: `publish_release.ps1 -Platforms win-x64` produced ZIP/VSIX assets and `build_msi.ps1` produced `ETL-SQL-Enterprise-v0.9.0.msi`.
- UI sandbox and Node smoke coverage added for lineage DAG, designer, script editor, VS Code webviews, datasets admin, and lineage catalog browser-side surfaces.

### Fixed

- **Report export rendering**: PDF chart export now uses the ECharts SSR pipeline so chart visuals render as real chart images; table and filter visual formatting paths were tightened for PDF/Markdown output.
- **VS Code Extension cross-platform hardening**: Added automatic execute permissions setup (`chmod +x`) on Linux/macOS for bundled executables, resolved terminal commands using dynamic shell detection (fixing PowerShell-only `&` operator errors on zsh/bash/cmd), fixed notebook engine lookup in packaged environments, resolved broken welcome links using a GitHub repository fallback in production, added auto-cleanup of temporary scripts, and implemented child spawn error listeners to prevent crashes.
- **`--resume` silently ignored**: passing `--resume` without `--session` would run the full script from the beginning with no warning. Now fails fast with a descriptive error.
- **Stale session state on fresh runs**: `LoadSessionState` fired whenever a `--session` ID was supplied, restoring variables from prior runs even without `--resume`. Now only called when `--resume` is explicitly set.
- **GOTO keyword targets**: the GOTO validation guard used `&&` so keyword tokens (e.g. `SELECT`) passed validation and produced a `GotoStatement` with a keyword target — a silent parse error that deferred to a confusing runtime failure. Targets now restricted to `TokenType.IDENTIFIER`.
- **`SaveSession` ArgumentException on mocks**: `SessionStateManager.SaveSession` hard-cast `IExecutionContext` to `Evaluator` and threw `ArgumentException` for any stub, mock, or sub-evaluator. Now returns early gracefully for non-Evaluator contexts.
- **BigQuery null dereference**: `t.Reference.TableId` in `GetTablesAsync`/`GetViewsAsync` had no null guard; `t.Reference?.TableId` added with a skip on null entries.
- **MySQL double-dispose**: `RollbackAsync` disposed `_transactionalConnection` in its `finally` block then nulled the field; if that `DisposeAsync` threw, the null-assignment was skipped and `DisposeAsync` was called a second time. Connection is now captured locally and nulled before the call in both `CommitAsync` and `RollbackAsync`.
- **Parser error messages**: 12 messages across `DataParser.cs` (CREATE CONNECTION), `ExtensionParser.cs` (SEND EMAIL), and `SystemParser.cs` (RUN SCRIPT) updated to name both the construct and the expected token, matching the quality bar of the core engine.
- **Docker platform service tests**: Report Portal and Orchestrator service Docker tests now build images through a direct `docker build` helper and `.dockerignore` excludes local databases/logs/generated output from build context archives.
- **Windows MSI discovery**: `build_msi.ps1` now detects installed WiX 3.x toolsets under Program Files, including v3.14 installations, before compiling the MSI.

### Security

- **JWT secret hardening**: `JwtSecretValidationService` rejects default or weak JWT secrets at portal startup in production mode.
- **CI workflow hardening**: CODEOWNERS enforces review requirements; Dependabot tracks dependency updates; `sync-assets.js -Check` runs in CI to prevent stale shared report runtime assets from shipping.

---

## [0.8.0] — 2026-05-25

### Added

**Connector Testing & Certification**
- **Connector Certification Matrix**: Formal 4-class certification framework (`MetadataOnly`, `MockedIntegration`, `LocalRealIntegration`, `DockerRealIntegration`) across all 21 connectors. `Connector` and `CertificationClass` traits on every test class enable targeted release gate selection.
- **FTP Docker real-integration**: `delfer/alpine-ftp-server` Testcontainers fixture covering connection, upload/download round-trip, root listing, wrong-password provider-failure wrapping, and `PORT` option handling.
- **REST API real-integration**: Loopback HTTP server tests for PUT and DELETE requests with Basic, Bearer, and API key auth; PUT body verification.
- **Azure Blob (Azurite) integration**: Smoke, upload/list round-trip, download, bad account key, expired SAS token, and host-allowlist enforcement.
- **SMTP (Mailpit) integration**: Docker-backed send-and-verify, multi-row batch, connection-refused and host-allowlist failure paths.
- **BigQuery emulator integration**: `ghcr.io/goccy/bigquery-emulator` Testcontainers coverage for T1 smoke plus T2–T4 unit coverage (invalid credentials, credential masking, host allowlist).
- **Snowflake emulator integration**: Emulator-backed tests plus unit coverage for JWT connection properties, host suffix normalisation, and host-allowlist enforcement. Fixed a `StackOverflowException` in `SnowflakeDataSource.CreateCommand`.
- **Parquet/Avro corrupt-file coverage**: Real-file negative-path reads that verify corrupt provider errors are wrapped as sanitised `ExecutionException`s.
- **Exception wrapping (T4)**: Provider-exception wrapping verified for 11 connectors: ORACLE, ODBC, EXCEL, PARQUET, AVRO, FTP, AZURE_BLOB, API, SMTP, REPORTPORTAL, ORCHESTRATOR.

**`etl-sql doctor` Enhancements**
- `--profile quick|full` — quick profile stays fast; full profile runs report-manifest smoke, PDF export smoke, Graphviz/browser capability checks, and service probes (Report Portal `/health`, Orchestrator `/health`, SMTP, SFTP, Azure Blob).
- `--json` output mode for automation.
- `--strict` flag returns non-zero on warnings.
- Full runtime-path write checks, parser/engine/linter/security/encryption/file/report-asset/Node/portal-DB health probes.

**Scale Certification Harness**
- `scripts/Test-ScaleCertification.ps1` runs smoke/standard/stress tiers with `CERT_ROW_SCALE`-driven row counts.
- Certified scenarios: external sort, aggregate, join, temp-table spill, result cap, window spill, CUBE grouping-set spill, scalar subquery cache, and non-persistent spill cleanup after success and forced failure.
- Each scenario asserts correct row count, `TotalSpilledBytes > 0` for spill paths, tier-derived managed-memory bounds, and cleanup completion.
- `FullyMaterializingDml` warnings for uncapped `MERGE`/`UPDATE`/`DELETE` paths documented with explicit limits.
- 50k-row `CREATE DATASET` Parquet snapshot/reload certified with row count and checksum (`Cert_Smoke_ReportDatasetSnapshotReload_50kRows`).

**Persistent Lineage & Stewardship Catalog**
- `ILineageCatalogStore` interface with `SaveLineageAsync`, `GetHistoryForTableAsync`, `GetHistoryForTagAsync`; implemented in `SQLiteJobHistoryStore` (`LineageHistory` table, auto-migrated).
- New statements: `SHOW LINEAGE HISTORY FOR TABLE <name>` and `SHOW LINEAGE HISTORY FOR TAG <key> [= 'value']`, both supporting `LIMIT` and `INTO #t`.
- Portal Lineage catalog view: target/source/source-file/tag/job queries, column and date filters, tags list, jobs list, source-file links, report links, CSV export, and saved query presets.
- Lineage catalog persistence for portal in-process report executions, bundle publish events, and `CREATE DATASET`/`CREATE VISUAL` runtime events.
- Authenticated portal APIs for table, source, source-file, tag, and job lineage history with report context attached.

**Report Portal Hardening**
- Concurrent snapshot/history/report/list reads during refresh and duplicate-refresh debounce verified by integration test.
- `EXPORT_CSV` and `EXPORT_PDF` audit events added to `ExportController`.
- Read-only report access: snapshot/export allowed, execute/refresh denied, private dataset ACL filtering on dependency and dataset-list endpoints.
- Report history modal updated with dedicated table rendering and horizontal scroll fallback for long hashes.

**Snippet Library Phase 4**
- 13 new built-in snippets covering common connector, lineage, reporting, and scheduling patterns.
- User-defined snippets loaded from disk at startup.
- TUI tab-stop navigation inside snippet placeholders.
- F1 reference integration: snippets surface in `HELP SNIPPETS` and the snippet reference panel.

**Documentation**
- Doc sanity tests: SQL blocks in `Grammar.md`, `Syntax_Index.md`, and all bundled help files parse without syntax errors; help link resolution verified; stale roadmap language guardrail for reference docs.
- Connector Standards doc updated to reflect XML streaming refactor (Rule 7 compliance).
- Scale certification claims page added (`docs/architecture/standards/ScaleCertification.md`).
- SLT corpus coverage documented in `docs/architecture/standards/SLT_Coverage.md`.

### Fixed
- **Snowflake StackOverflow**: `SnowflakeDataSource.CreateCommand` was recursively calling itself; fixed to delegate to the underlying connection.
- **VS Code password prompt**: "requires an interactive console" error when an `ENC:`-protected connection was opened in VS Code; password masking now works via the VS Code input mechanism.
- **Test coverage gate**: Coverage had slipped below 70%; restored to 70.8%+ with T4 exception-wrapping test additions.
- **SLT DML gap**: Added `dml.test`, `insert.test`, and `merge.test` to the SLT corpus; `MergeStatementHandler` was missing from `SltRunner` and is now registered. All 40 SLT files pass.
- **Oracle negative-path coverage**: `gvenzl/oracle-free` Testcontainers fixture extended with missing-table and invalid-SQL failure paths.
- **Azure Blob expired SAS**: `AzureBlobIntegrationTests` now generates and tests an expired account SAS token.

### Changed
- **XML streaming refactor**: XML connector refactored from full-DOM accumulation to streaming `XmlReader`, eliminating full materialisation of large XML files (Rule 7).
- **ODBC/Excel async exceptions**: Accepted exceptions documented with inline comments in `OdbcConnector.cs` and `ExcelDataSource.cs`.
- **`SET SHOW_SECRETS`**: `SET SHOW_PASSWORDS` is now an alias for the preferred `SET SHOW_SECRETS` form.
- **`v0.7.0` baseline notes moved**: Migration Guide updated to reflect 0.8.0 as the current baseline.

---

## [0.7.0] — 2026-05-18

### Added

**Reporting & Interactive Dashboards**
- **Advanced Drill-Down**: Implemented `DRILL_IN` and `DRILL_DOWN` for hierarchical, in-place data exploration; added `DRILL_TO` for cross-report navigation with parameter state passing.
- **Paginated Reports**: Support for `PAGINATED = ON` reports featuring automatic header/footer repetition, multi-page data grid spans, and specialized snapshot formats.
- **ETL Notebooks (`.etlnb`)**: Native VS Code notebook support with cell-based execution, stateful REPL persistence, and cross-cell IntelliSense for connections and variables.
- **Cross-Visual Highlighting**: Power BI-style interactive filtering where clicking a chart segment highlights related data across all other visuals.
- **Ghost Rendering**: Enhanced interaction logic with "ghosting" (dimming) support for Line, Scatter, Pie, and Donut charts during highlighting.
- **New Visual Types**:
    - **MAP**: Integrated ECharts-based mapping with custom GeoJSON support (`MAP_FILE`).
    - **Specialized Charts**: Added `GAUGE`, `BOXPLOT`, `WATERFALL`, `BUBBLE`, `RADAR`, and `CANDLESTICK`.
    - **Input Visuals**: Added `TEXTBOX`, `NUMBERBOX`, and `CHECKBOX` for direct scalar parameter input.
    - **Interactive Slicers**: Support for `SLIDER` and `SEARCH` visual types with immediate dashboard re-rendering.
    - **Interactive Multi-Select**: New `MULTISELECT` visual type rendering as a checkbox list with automatic parameter synchronization.
- **Collapsible Containers**: Support for `COLLAPSABLE = ON`, `ICON`, and pinning logic for overlay drawers and sidebar panels.
- **Deferred Execution**: Added `RUN` button support with staged parameter batching (prevents report refresh on every slicer change).
- **Visibility Engine**: Standardized `VISIBLE = ON|OFF` syntax (replacing legacy `HIDDEN`); added support for dynamic visibility via `@variables`.
- **Enhanced Date Picking**: Native `RELDATEPICKER` (hybrid text + calendar) support.
- **Markdown Tables**: Full support for GFM-style tables in `TEXT` visuals via `marked.js` integration.

**Data, Lineage & Orchestration**
- **Shared Datasets**: Implemented a global dataset registry allowing reports to consume cached, shared data with automated background refreshes and access control.
- **OpenLineage Integration**: Support for exporting data lineage in OpenLineage-compliant JSON format.
- **Lineage 2.0 Engine**: 
    - **Standard Tag Library**: Defined 20 core lineage tags (`@pii`, `@sensitive`, etc.) with `@pii: true-wins` inheritance logic.
    - **Transformation Tracking**: Automated recording of transformation types (`Cast`, `Aggregation`, etc.) across the pipeline.
    - **Visualization**: Enhanced Mermaid-based lineage graphs with distinct shapes for Reports and Datasets.
- **Data Lake Connectors**: Native support for **Snowflake** and **BigQuery**.
- **Batch Separator**: Added `GO` keyword support for separating execution batches.
- **Improved Loops**: `FOR` loops now support implicit start values with `FOR @i TO 10`.
- **QUALIFY Clause**: Added T-SQL/Snowflake-style `QUALIFY` clause for filtering results based on window function values.
- **Window FILTER**: Support for the `FILTER (WHERE ...)` clause inside aggregate window functions.
- **@@FETCH_STATUS**: Added support for checking cursor/foreach fetch status.

**Security & Governance**
- **JWT Secret Generation**: New `GENERATE JWT_SECRET` command for securing report portal communications.
- **Proactive Guardrails**: Linter now warns on high-risk operations and blocks sensitive directory access more aggressively.
- **Decompression**: Added `DECOMPRESS FILE` and `DECOMPRESS DIRECTORY` statements to the specialized operations library.
- **PGP Engine Hardening**: Improved `PGP_KEY_PAIR` generation and validation logic.

**IDE, Tooling & UX**
- **Terminal IDE (TUI) 2.0**: Massive overhaul of the TUI with scrolling, smart copy, message panel optimization, and specialized visual rendering.
- **Unified IntelliSense**: 
    - New dot-aware suggestion engine with priority-based ranking and member-access discovery.
    - LSP support for `@`-prefix tag completions and documentation hovers.
    - Finalized purge of unstable semantic features for improved stability.
- **VS Code Preview**: Support for new chart types (Bubble, Radar, Candlestick, Map) and improved sidebar variable discovery.
- **Report SQL Audit**: Comprehensive rewrite of `Report_SQL_Guide.md` and inline help files to match current production state.
- **Deployment Packaging**: Integrated Windows MSI/ZIP, Linux `.deb`/ZIP, macOS DMG/ZIP, and platform-targeted VSIX generation into the release pipeline.

### Fixed
- **Multi-Select Regression**: Fixed a duplication bug where legacy dropdown logic was overwriting the new checkbox-list implementation.
- **Markdown Rendering**: Resolved issues where Markdown tables were displayed as raw text due to library interface mismatches.
- **IntelliSense Regressions**: Fixed missing connector option suggestions and asterisk expansion failures.
- **Portal State Bugs**: Resolved "white screen" and state synchronization issues in the report portal.
- **Slicer Logic**: Fixed null-reference errors in `renderSlicer` when actions were undefined.
- **Cross-Filesystem Paths**: Fixed portal publish flow failures when handling paths across different drives.
- **Gauge Rendering**: Resolved template string errors and implemented auto-formatting for decimal values.
- **Notebook Reliability**: Fixed "REPL process exited unexpectedly" and communication deadlocks by implementing atomic process lifecycle management and heartbeat checks.
- **Protocol Standardization**: Migrated REPL communication to strict PascalCase JSON with mandatory CRLF endings for Windows pipe stability.

### Changed
- **Sample Reorganization**: Expanded the curated `samples/` library and redirected generated sample outputs under `samples/output/` patterns for repository cleanliness.
- **Visibility Syntax**: Standardized report visibility on the unified `VISIBLE` property.
- **Directory Connections**: Statements like `COPY DIRECTORY` and `FILE_LIST` now natively accept `DIRECTORY` connection aliases as path arguments.

## [Unofficial 0.6.0] — 2026-05-11

### Added

- **Hierarchical Drill-Down and Drill-Through:** Implemented `DRILL_IN` and `DRILL_DOWN` (supporting multi-key drill parameters) for interactive, in-place dashboard exploration.
- **Power BI-style Cross-Visual Highlights:** Added cross-visual highlight filtering with dual-direction updates and dimming/ghosting effects for chart visuals (Line, Scatter, Pie, Donut).
- **Shared Dataset Management:** Built dataset explorer features including persistence, cross-report consumption, access control, LS dataset awareness, and portal-triggered refreshes with async execution.
- **Advanced Parameter & Execution Controls:** Added textbox, numberbox, checkbox scalar inputs, and deferred execution support (RUN button) with staged parameter batching.
- **New Visual Enhancements:** Added collapsible containers, standard `VISIBLE = ON|OFF` syntax (replacing legacy `HIDDEN`), and support for custom GeoJSON maps (`MAP_FILE`) with build-time validation.
- **Interactive Tooling:** Added `serve` command and dynamic `ReportPlayer` lifecycle management for live report previews in-browser.
- **OpenLineage Integration:** Added OpenLineage export support and database catalog metadata imports.

### Changed

- **Sample Reorganization:** Cleaned up and renamed all sample scripts, redirecting outputs to standard `samples/output/` patterns.

### Fixed

- **Portal Reactivity:** Stabilized slicer reactivity, multiselect visual components, and cross-filesystem path handling during portal publishing.

## [Unofficial 0.5.0] — 2026-05-04

### Added

- **Report Portal Subsystem (Phases 1–6):** Introduced the `ETL-SQL.Portal` web application. Features include JWT authentication, role-based access control (RBAC), folder structure organization, report publishing, execution/snapshot tracking, and web-based ECharts/Markdown rendering.
- **Automated Report Subscriptions:** Shipped report subscriptions allowing scheduled report exports via `EXPORT REPORT` sent as Link or Markdown emails, complete with SMTP connection management.
- **Portal Observability & Administration:** Added a `/health` endpoint with JSON diagnostics of database and orchestrator status, audit logs CSV exports, and administrative endpoints.
- **Portal Security Hardening:** Implemented JWT secret validation on startup via hosted service, a path traversal guard, and HSTS security configurations.
- **Apache Arrow Spill Format & Decryption:** Integrated Apache Arrow IPC spill format for high-speed serialized temp table caching, and implemented client-side credential auto-decryption.
- **Unified IntelliSense Engine:** Built a priority-based suggestion ranking, dot-notation autocomplete prefix filtering, dynamic option discovery, and member-access resolution.
- **Data Lake Connectors:** Native support for **Snowflake** and **BigQuery** databases.
- **Security & Encryption:** Added `GENERATE JWT_SECRET` for secure Report Portal communications.
- **Language Syntax Additions:** Implemented `QUALIFY` clause filtering, window function `FILTER (WHERE ...)` support, cursor status checks (`@@FETCH_STATUS`), and `FOR` loop syntax support for implicit start values.
- **TUI IDE Completion:** Overhauled TUI console with path completion, Smart Copy, screen stability, Compare Mode, SHOW commands, and a two-line status bar.
- **Installer & Packaging Release Pipelines:** Integrated MSI, Linux `.deb`, and macOS DMG installer packages with install bootstrap configurations.

### Changed

- **Security Auditing:** Standardized security overrides by migrating legacy comments to formal `SET ALLOW_... ON/OFF` statements.

### Fixed

- **TUI & Telemetry bugs:** Resolved rendering artifacts, status bar layout errors, and stabilized TUI telemetry.
- **LSP Cleanup:** Purged experimental unstable features (Quick Fixes, Smart Rename) for stability.

## [Unofficial 0.4.0] — 2026-04-20

### Added

- **Report-SQL Scripting and `CREATE VISUAL` Support (Phases 9A–9D):** Introduced native support for Report-SQL scripts (`.rptsql`) with `CREATE VISUAL`, `CREATE PAGE`, and `CREATE DATASET` statements. Added full grammar for visual types (BAR, LINE, PIE, SCATTER, TABLE, CARD, SLICER), axes, column mappings, and page slot layout definitions.
- **ReportBuilder Library and CLI Tooling:** Created `ETL-SQL.ReportBuilder` for Chart.js rendering, GFM markdown generation, and snapshot serialization. Shipped the report builder command-line utility with build, refresh, and serve commands.
- **VS Code Extension Preview Integration:** Added a WebviewPanel to the VS Code extension for live report previews, displaying rendered Chart.js charts, tables, cards, and interactive slicers.
- **ReportPlayer Web Dashboard:** Shipped a Kestrel-hosted local dashboard server (`ReportPlayer`) supporting live parameter injection, interactive updates, and auto-refresh endpoints.
- **Orchestration & Scale Hardening:** Implemented job retry logic with exponential backoff and session persistence in the Orchestrator, alongside `#temp` table spill-to-disk and result capping logic.
- **Hyper-scale Window Spilling:** Added deep-spilling mechanism for window query execution to partition results under high-volume workloads.
- **ANSI SQL Functions & Statistical Aggregates:** Implemented standard ANSI string functions (`SUBSTRING`, `POSITION`, `OVERLAY`, `TRIM`, `EXTRACT`), date arithmetic enhancements, and statistical aggregate calculations.
- **Script Assertions:** Added the `ASSERT` statement to natively validate data qualities and script outcomes.
- **JSON & XML Security Hardening:** Replaced bare catch blocks with explicit system exception filters and added security sandbox protections for remote file transfers.
- **LSP & UI Enhancements:** Modernized results panel, TUI performance dashboard, and stabilized telemetry pipelines.
- **PIVOT & UNPIVOT Validation:** Added linter validation for PIVOT columns, quarter-based `DATEPART` support, and query metadata derivations.

### Fixed

- **SMTP Attachment Leak:** Fixed a handle leak for SMTP attachments.
- **3VL Null Handling:** Implemented three-valued logic (3VL) null propagation and fixed substring start index boundary behaviors.

## [Unofficial 0.3.0] — 2026-04-06

### Added

- **VS Code Extension v0.1 Alpha:** Integrated LSP parser with formatting, lineage hover, and smart CLI execution.
- **Security & Encryption Utilities:** Added SSH key pairing (`GENERATE SSH_KEY_PAIR`), connection altering (`ALTER CONNECTION`), and file encryption/decryption (`ENCRYPT FILE`, `DECRYPT FILE`).
- **Serilog Logging Infrastructure:** Integrated Serilog for application-wide logging and consolidated logs to the `logs/` directory.
- **Join Optimization:** Implemented `CompoundKey` to optimize hash joins and handle mixed-type comparisons (string/numeric/date) across diverse sources.
- **Bulk Insert Lineage:** Added explicit column mapping support and column-level lineage tracking.
- **SQL Pushdown:** Enabled SQL pushdown execution and support for standalone `EXECUTE INTO #temp`.
- **Syntax Enhancements:** Supported `LIKE ESCAPE` and grouping sets (`ROLLUP` / `CUBE`).

### Changed

- **Syntax Standardization:** Migrated `ON FILE` to `ON FLATFILE` for file connections.

### Fixed

- **Thread Safety:** Eliminated deadlocks and silent exception swallowing under concurrent execution contexts.

## [Unofficial 0.2.0] — 2026-03-23

### Added

- **Core Query Dialect & Standard Library:** Support for `DISTINCT`, `TOP`, `LIMIT`, `MERGE`, `OFFSET`, `NTILE`, `STRING_AGG`, and transactional statements (`COMMIT`, `ROLLBACK`, `THROW`).
- **Database Connectors:** Added initial support for MSSQL, Postgres, and Oracle database engines.
- **File Connectors:** Read/write capabilities for XML and JSON files.
- **Temp Tables & Indexes:** Support for `#temp` tables with query plan indexes (`CREATE INDEX`) and query plan tracing via `EXPLAIN`.
- **Control Flow & Parallel Execution:** Parallel execution pipelines (`PARALLEL`), cross-script execution (`RUN SCRIPT`), and directory synchronization tasks.
- **Notifications & Transfer Connectors:** Added `SEND EMAIL` and file transfer connectors (SFTP/SSH, FTP, Azure Blob).
- **Linter & UI Foundations:** Added a command-line script editor, local test harness (`--test`), and baseline security linter.

## [Unofficial 0.1.0] — 2026-03-13

### Added

- **Proof of Concept Completed:** Successfully loaded flat files (CSV) and joined them into in-memory `#temp` tables.
- **Abstract Syntax Tree (AST) Parser:** Implemented the initial AST parser to parse SQL statements and evaluate expression trees.
- **Core SQL Execution Engine:** Developed the core engine to execute queries, process DML scripts, and return formatted results.
- **Terminal IDE (TUI) Foundations:** Added a basic console editor interface to write scripts and display execution output.
- **Git Repository Initialized:** Initialized the git repository and established the project structure.
- **Development Kickoff:** Work began on March 6, 2026, to design and prototype the initial engine proof of concept.
