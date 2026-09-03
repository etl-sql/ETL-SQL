# ETL-SQL Development TODO List

Use this list as the execution ledger for product and release work. Work top to bottom inside each
section unless a dependency or release-blocking defect changes the order. When an item is verified,
record the notable outcome in `CHANGELOG.md` and mark it complete. Remove completed items only during
a later closed-item audit after their implementation and evidence have been double-checked.

Unfinished `ROADMAP.md` initiatives and release gates are represented below.

---

## 1. ETL-SQL Studio

Authoritative reference: [`docs/architecture/decisions/etl-sql-studio.md`](docs/architecture/decisions/etl-sql-studio.md).

Studio now has the shared desktop/Portal workbench, script and report projections, connection and
dataset creation, parameter management, production-host persistence, workspace operations, Git
diffs, a visual formatting inspector, and a read-only pipeline canvas projected from the engine DAG.
The next major increment is turning that pipeline projection into a lossless authoring surface.

`ReportBuilder` and `WorkstationEditor` remain supported while Studio is built and certified. Do not
start their retirement until Phase 6 is complete. A wizard creates a new object; an inspector edits
the selected object. All authoring surfaces must read current parser state, preview the exact SQL,
write through the canonical mutation path, and preserve unsupported hand-authored text.

### Phase 1 — Pipeline DAG Authoring Spine (Complete)

**Outcome:** An author can drag an execution task onto the existing engine-projected DAG, configure
it with the shared query workbench, connect it into a sequential flow, and round-trip between canvas
and `.etlsql` without losing hand edits.

- [x] **Harden the shared authoring components before the DAG reuses them**:
  - `noteMarkup` now renders a structured model through `inlineMarkup`: a string is escaped, and
    emphasis has to be asked for as `{ strong | em | code | text | br }` segments, each of which
    escapes its own content. An unrecognised segment throws rather than rendering nothing.
  - The `CREATE CONNECTION` preamble comes from the designer parse route. Parse now reports the
    script's top-level connections as authored source text (shared parsing service, Portal DTO, and
    the LSP projection the VS Code host uses), so a multiline body, a comment, or a semicolon inside
    a quoted option survives — the regex ended the statement at the first `;`.
- [x] **Prove the editable node/edge model with a lossless round-trip spike**: `PipelineTaskAuthoringService`
  edits a task — a top-level section label plus the `EXECUTE <connection> BEGIN ... END` block it
  introduces — as span edits computed from the parse, so untouched bytes survive and every edit is
  reparsed before it is returned. Identity is the section label, carried into the projection as
  `ScriptDagNode.Key`, because node ids are positional and shift under a hand edit. The canvas makes
  labelled cards draggable and drops mean "run after this one". Byte preservation is mutation-tested,
  and emitted tasks survive the canonical formatter and the AST serializer.
- [x] **Add the task palette and execution-task editor**: The palette offers four kinds and the
  editor authors them. An execution task carries the shared query workbench — completions, hover,
  diagnostics, run, messages, and results — against a connection picked from the ones the parse
  reports, with an auto-generated label the author can edit. File-operation, validation, and
  notification tasks are field forms, and each is gated behind `PipelineTaskEmissionTests`: its
  emitted statement must parse, introduce no lint finding, survive the canonical formatter, and
  reference only connections the script declares. A half-filled task is refused before anything is
  written, so the author never sees a parse error about syntax they did not type.
- [x] **Add explicit sequential dependencies**: An edge is written into the script as an
  `-- @after: a, b` tag above the task's label — the lexer reads it as a tag and the parser skips
  tags between statements, so the declaration is free at run time and the script stays the source of
  truth. Dragging a card's connector handle declares a dependency; dragging the card body still
  reorders. The inspector lists what a task waits for, each removable. Several incoming edges are a
  join — "waits for all 2" — and the canvas never writes a `PARALLEL` block, which is the only thing
  in ETL-SQL that means concurrency. Because the engine runs top to bottom, connecting also reorders
  when it has to, so a declaration can never contradict execution order; cycles and self-edges are
  refused with a reason. The projection replaces the implicit sequential edge into a task that
  declares its own.

### Phase 2 — Pipeline Control Flow and Debugging (Complete)

**Outcome:** The DAG expresses ETL-SQL control flow honestly and can inspect execution state at a
selected point.

- [x] **Add conditional precedence edges**: An edge hands over on success, on failure, on completion,
  or on the author's own expression, declared in the same `-- @after:` tag and lowered into the
  script: a `BEGIN TRY` / `BEGIN CATCH` guard on the task being watched, recording its outcome into
  a three-valued `@<label>_status`, and an `IF` on the task that waits. The guard sits outside the
  gate, so a task whose gate was false stays at "never ran" and does not fire a downstream
  `on failure` edge — skipped is not failed. Both wrappers are derived from the declaration rather
  than tracked beside it, so a hand-edited tag produces the control flow it describes, and removing
  an edge, deleting a task, or renaming one carries the wrappers with it; only a wrapper carrying
  that bookkeeping is treated as the canvas's, so a hand-authored `TRY` or `IF` is never rewritten.
  Each condition has its own colour, its own stroke pattern, and its words on the badge, because
  colour alone would hide the success/failure difference from a red/green colour-blind reader. The
  four runtime tests execute the emitted script and assert which branch actually ran.
- [x] **Add draggable control-flow containers**: `PARALLEL`, `FOREACH`, and transaction scopes are
  palette kinds that hold other tasks. Each is one labelled statement, created empty and filled by
  dragging tasks in — every shape parses empty, so no placeholder statement lands in the author's
  file. A transaction scope is the documented `BEGIN TRY BEGIN TRANSACTION; … COMMIT; END TRY BEGIN
  CATCH … ROLLBACK; THROW; END CATCH` handler, because ETL-SQL has no single transaction statement
  and a scope that left one open on the way in would be worse than none. Nesting relocates bytes and
  re-indents them, never regenerates them; deleting a container deletes what is inside it; a reorder
  and an edge both refuse to cross a container boundary, so nothing slides into a scope nobody asked
  for. Concurrency is only ever a `PARALLEL` block the author added: two of its branches cannot be
  given an order, and a task that already waits for one of them is refused entry rather than quietly
  losing the edge.
- [x] **Add the positional scope inspector**: Selecting a task shows the variables it can read —
  declared, assigned, or bound by an enclosing loop — and the `#temp` tables that exist by then, each
  linked back to the line that produced it. Positional, not script-wide: a name declared below the
  task is not in scope, enclosing blocks are, and what a sibling `PARALLEL` branch staged is not.
  "No answer yet", "resolved and empty", and "could not tell" are three distinct renderings, because
  collapsing them is how a panel quietly lies. Row counts and spill appear only when a run reported
  them for that task, matched by name against the engine's execution tree.
- [x] **Add Run to Selected Node**: Execute through a selected node and populate intermediate
  variables and `#temp` tables in Results. The slice is the selection's dependency closure — the
  tasks its `-- @after:` tag names, transitively — so declaring a dependency narrows the run to what
  was declared; a task with no tag falls back to the plain sequential reading the canvas already
  uses for a reorder, so an untagged script still runs top to bottom the way it reads. Everything
  the canvas does not model is kept, because a `CREATE CONNECTION` or a `DECLARE` carries no
  dependency information to prove it unrelated, and only whole statements are ever cut, so a
  selection inside a container takes its container whole rather than truncating a `PARALLEL` block
  into something that does not parse. The safe behaviour for remote side effects is a confirmation
  that names them: planning and running are two routes, the planning one cannot execute, and the
  dialog lists every write that outlives the run — connection-qualified table, address, or path —
  grouped by the task performing it, with `#temp` staging deliberately absent so the confirmation
  keeps meaning something. Skipped siblings are named rather than dropped silently.

### Phase 3 — Guided Authoring and Beginner Recovery (Complete)

**Outcome:** The common dashboard and report path is understandable and recoverable without editing
SQL, while the script remains visible as the advanced escape hatch.

- [x] **Add undo for wizard writes**: Every canonical mutation — dataset, visual, parameter, page
  setup, filter, and the pipeline task edits — offers a dismissible Undo on the toast that reports
  it. It is backed by the CodeMirror transaction that made the write, not by a remembered copy of
  the old text: the ranged `replaceAll` means the editor's history already holds the exact inverse.
  That is also why the offer expires. History undo pops the last event, so once anything else has
  changed the buffer the offer refuses and says so, rather than rewriting the buffer to a remembered
  string and destroying whatever came after.
- [x] **Make Start with Sample Data produce a working dashboard**: The seeded MOCKDB script stages
  one shared `#temp` query, then builds a KPI card, a bar chart, and a table onto a dashboard page
  whose `STRUCTURE` places all three. Parsing was not the bar — a script that parses can still
  evaluate to a page with no visuals or to visuals with no rows, which is the blank canvas the seed
  exists to remove — so the test executes it against the sample connector and asserts each tile has
  rows and a slot in the page's `MAP`.
- [x] **Finish report-workflow entry behavior**:
  - The Dashboard-versus-Paginated question takes Not now, Escape, or a click outside, and is not
    asked again for that document; the canvas then carries the choice so declining once costs
    nothing. Its wording leads with what the choice does and does not do.
  - New dashboards, seeded or blank, open on the canvas; a paginated report still opens split
    because its page setup is script-shaped. `setProjection` records the choice on the document, so
    an author who switches keeps that per document.
  - Dismissing the guided rail leaves a restore strip where the rail was, on the canvas — the
    sidebar's copy of the restore is not reachable enough to be the only one.
- [x] **Explain every wizard mutation**: `sqlPreviewMarkup` now takes the sentence as a required
  argument and throws without it, and the steps whose write is design-state shaped — group and
  detail bands, page furniture, a pipeline task — carry the same sentence without quoting bytes the
  patcher, not the wizard, decides. A browser test reads the sentence out of every preview a wizard
  paints.
- [x] **Replace beginner-facing implementation labels**: A file is a Dashboard, Report, Pipeline, or
  Query, refined by what Studio actually knows when the document is open and falling back to the
  extension when it is not. The host-alias refusal now leads with the consequence — a report that
  borrows a session connection works for you and fails for every other reader and every scheduled
  run — before the implementation reason.
- [x] **Surface bookmarks, themes, and styles in Studio**: The bookmark editor moved out of the
  designer's markup into its own element with a `mountBookmarks` entry point, so Studio's rail hosts
  the same DOM with the same listeners. Report theme and style needed no move at all: the designer
  already renders it into Studio's inspector when nothing is selected, and Studio hid the inspector
  on an empty selection — so the panel was written, wired, and unreachable. Both are covered by a
  browser test that drives the rail and changes the theme.
- [x] **Finish chart-creator controls**: Per-measure aggregation already existed; the builder now
  adds number and date formatting for the value and the category axis, written as the `FORMAT` and
  `X_AXIS (FORMAT = …)` its preview shows. Adding a visual selects it, which opens the formatting
  inspector — already the owner of colours, grid lines, data labels, axis bounds, and explicit
  top/right/bottom/left legend placement — so the builder shapes a visual once and hands over.

### Phase 4 — Report Interaction, Paginated Output, and Model Views (Complete)

**Outcome:** Studio can complete the representative interactive-dashboard and paginated-report jobs,
then expose advanced inspection views that do not block basic authoring.

- [x] **Add cross-visual filtering and cascading slicers**: The engine and the browser runtime
  already carried both; what was missing was any way to author them. `ON_SELECT` is now a choice
  between highlighting, filtering, and ignoring, with the column a selection is keyed on beside it,
  and a `SLICER` or `MULTISELECT` gets a cascade editor — LOCAL or LIVE, its parent bindings, and
  the invalid-selection, null, all-value, and multi-select policies — written in the serializer's
  own shape so reopening the report does not rewrite the clause. Two defects made the old surface
  quietly inert: the action and interaction handlers wrote to the in-memory visual and never to the
  script, and the parameter picker read `state.variables`, a key nothing sets, so the one control
  that binds a slicer to a parameter was always empty. A third sat under them: the patcher matched a
  clause keyword at any parenthesis depth, so `CASCADE`'s own `MODE` was read as the visual's `MODE`
  clause and inserted a second time, the result did not parse, and the patcher's parse guard turned
  the whole edit into silence. Clause matching is now pinned to the statement's own level.
- [x] **Remove the filter-pane value dead end**: A categorical card now searches its values,
  selects all, clears, and inverts what the search narrowed to, and pages through the rest 25 at a
  time with a count saying how much of the column is on screen — the old card cut the list at twelve
  with no search and no thirteenth value, so values that existed in the data could not be filtered
  on at all. Selection actions add to the selection rather than replacing it, because the search
  narrowed the view and not the filter. Numeric and date cards gained a condition: at least, at
  most, greater, less, equals, does not equal, is blank, is not blank, alongside the range. The
  vocabulary is decided server-side in `DesignerQueryFilterService`, so the pane sends a word and
  never composes SQL; an unknown operator is refused rather than falling back to a range, and a
  condition chosen before its value filters nothing rather than failing.
- [x] **Complete paginated report authoring and export**: Group/detail bands, totals, header and
  footer bands, page size, orientation, margins, and explicit breaks were already authored by the
  guided steps. What was missing was the end of the job. **Export** is now an action rather than a
  page of instructions telling the author to find the PDF export elsewhere: Studio posts the buffer
  to `/api/designer/preview/pdf` — served by both hosts, so the step is not a button that 404s on
  the desktop — and hands back a file. **Parameter prompting** asks the report's `INPUT` questions
  before a preview or an export and seeds the answers the way `--var` does, with cancel meaning the
  run does not happen rather than the defaults being used. Three defects were in the way, each of
  which made the exported file wrong rather than absent: a paginated page's visuals are deliberately
  deferred until a reader presses Run, so an export produced pages with no data (an export now runs
  every page); the exporter opened a section per declared page *after* a heading section laid out
  with A4 defaults, so a Letter landscape report exported a stray portrait sheet and every page
  count was one too high; and `CliContext.Variables` — the mechanism the CLI uses for `--var` — was
  read by the CLI alone, so a value supplied by any other host was silently dropped. **Pagination
  preview** is now the engine's own compiled breakdown rather than the canvas's page-width sheet: the
  step asks for the manifest with every page run and lists each physical page with what lands on it,
  including the row ranges a split detail table continues from, so what it shows is what the PDF
  contains. Verified rather
  than asserted: the exporter tests read the page count back out of the produced PDF, check that an
  explicit break adds a page and an excluded visual does not, and decode the page content streams to
  prove a split table repeats its column headings; a Portal integration test exports the sample
  connector's order table across several pages and proves a prompt's answer changes the document.
- [x] **Add the document outline and layer tree**: The visual library already listed what was on a
  page; it could not act on the list and never told the canvas anything. The outline lists pages, the
  row bands the grid actually draws — visuals whose row ranges overlap, not merely those sharing a
  `gridRow`, so a tall tile does not split the row it visibly shares — and containers with their
  children. Selection runs both ways: a row selects the tile, and a canvas click highlights the row,
  which needed a fix to the designer callback that forced the rail to the visual library on every
  selection and so closed the panel the author was clicking in. **Move** swaps two tiles' grid
  placement, spans included, and the canonical patcher regenerates `STRUCTURE` from those
  coordinates — the swap is the entire edit. **Hide** writes `VISIBLE = OFF`, the property the report
  runtime already reads. **Lock is deliberately not written to the script** and the panel says so: a
  dashboard's layout is a grid, so there is no free z-order to author and no `LOCKED` anywhere in the
  language, and inventing an option so a button could pretend to persist would put a word in the
  author's file that nothing else reads. It is a canvas guard held in local storage that refuses a
  drag, a resize, and a delete, and the designer asks for it on each interaction rather than being
  told. The browser test proving the lock does the unlocked drag first: an earlier version asserted
  only that a locked card had not moved, which a gesture that never moved anything satisfies just as
  well — and did.
- [x] **Add the data-model / ER view**: A new `Model` projection, drawn by the same shared graph
  renderer as the pipeline map. `ScriptDataModelService` projects connections, remote tables, `#temp`
  tables, CTEs, and datasets, with join edges from the equalities the script writes, derivation edges
  for the `#temp` chains that are most of an ETL-SQL model, and foreign-key edges from what the
  database declares. The rule the whole projection is built on is that **nothing is drawn that the
  parser or the database did not say**: two tables sharing a column name produce nothing, an
  unqualified join column is left undrawn rather than assigned to the nearer table, and an
  `EXECUTE conn INTO #t BEGIN … END` block credits the connection rather than reading table names out
  of SQL this parser never sees. Cardinality obeys the same rule — `unknown` unless a declared key or
  foreign key settles it — and the view distinguishes "no keys were available" from "these tables
  have no keys", because a reader who cannot tell them apart reads every blank as a finding. Keys
  reach it through a new `IMetadataManager.GetKeyEvidenceAsync`, default-implemented as "nothing
  known" and backed by the connector catalog providers, so both hosts answer identically. The route
  projects twice on purpose: from the script first, then again with evidence about only the tables
  that projection named, so opening a diagram never becomes a schema crawl.
- [x] **Add live engine state and visual EXPLAIN views**: An `Engine` rail panel answering the two
  questions an author has about the statement in front of them. **Scope** is the Phase 2 model asked
  with a caret instead of a task label — `ScriptScopeService.AtLine`, sharing one walk with the task
  lookup by taking a predicate — so a `#temp` created below the cursor is not offered; it also hands
  back the statement under the cursor and the prefix above it, which is what makes the statement
  plannable at all. **The plan** is the engine's own `EXPLAIN`, asked for through the ordinary run
  route rather than a second door into the engine, so it passes the same policy, limits, and audit;
  the panel renders it as an operator list badged with blocking versus streaming, remote pushdown,
  index use, and spill. `PortalInteractiveRunPolicy` now allows `EXPLAIN` of a query it would allow
  you to run, and refuses `EXPLAIN ANALYZE` by name because that one executes.
- [x] **Add beginner-friendly diagnostic translation and error guidance**: `DiagnosticGuidanceService`
  turns the shapes that actually occur — an unterminated string, an unclosed block, a bare word where
  text belongs, a missing semicolon, an unaccepted option value — into a sentence with no parser
  vocabulary in it, a next step, the named object the line sits inside, a reference-tree link, and,
  where exactly one repair is correct, an edit. The unclosed block is the clearest gain: the parser
  reports the semicolon it tripped over, several lines from the cause and about the wrong character.
  Two restraints are load-bearing and tested. A diagnostic it cannot act on gets **no** guidance
  rather than "check your syntax", which is noise wearing the costume of help; and a quick fix is
  offered only where a guess is impossible — `ON`/`OFF`/`AUTO` are never quoted, and where a bracket
  belongs is a judgement, so no button offers to place one. The guidance is attached to
  `AnalysisDiagnostic` itself rather than to one host's response, so it reaches VS Code through the
  language server and the CLI's lint output as well as Studio; Studio applies a fix through the
  editor's own ranged transaction, so the undo offer covers it like any other GUI write, and refuses
  outright if the buffer moved under it. Positions are zero-based throughout, matching the diagnostic
  contract — `CalculateRange` already returned zero-based lines, and a second convention in the same
  payload is how an off-by-one gets into an edit a button applies unread.

### Phase 5 — Governance, Dataset Lifecycle, and Delivery (Complete)

**Outcome:** Authors can attach governance to first-class tasks and move finished work into supported
operational flows without an unexplained application switch.

- [x] **Add tag and metadata authoring**: A Governance rail panel listing everything in the script
  that can carry stewardship metadata — the script itself, the tables and datasets it builds, the
  remote tables it reads, and the columns it projects — with the tags on each, where each tag came
  from, and what policy says is missing. `CREATE TAG` was retired from the language before this
  work: the statements are `INSERT TAG` / `UPDATE TAG` / `DELETE TAG FOR TABLE <t> [COLUMN <c>]`, and
  that is what the panel writes. **Two authoring forms, one rule for choosing.** A projected column
  is tagged inline, as a `/* @key: value */` comment on the column, because that is where the lint
  rules, the catalog, and the PII scanner read column metadata from; everything else is tagged with a
  tag statement, because those objects have no declaration site a comment could attach to. The panel
  says which form it is about to write before it writes it, since the two behave differently — a
  comment travels with the column, a statement applies at the point it runs. **Placement is part of
  the meaning**: a tag on a table the script builds goes after the statement that builds it, and a
  tag on a table it only reads goes before the first statement that reads it, or the columns reading
  it inherit nothing — silently no governance at all rather than an error. **Derived tags are shown
  and never written.** Inheritance is projected exactly as `LineageTracker.InheritMetadata` computes
  it (source table tags, then the source column's own, with the script header as the fallback the
  engine applies to any entry lacking a key), and only where the column is a plain reference to a
  source the projection can name — an expression, or an unqualified name two joined tables could both
  supply, yields nothing rather than a guess, the same rule the ER view is built on. Turning an
  inherited tag off writes the `DELETE TAG` the engine actually reads rather than copying the value
  into a tag that has stopped tracking what it copied. Values are validated against
  `StewardshipTagCatalog` before a byte moves, and `@expect`/`@fail` are refused by name: the engine
  projects those from a column's `EXPECT` clauses, so a hand-written one is inert and would look
  enforced. **A pipeline task carries no tags, deliberately** — there is no task tag in the language
  and nothing that would read one, so a task appears only as the producer of what it writes, and the
  panel says so rather than offering a word nothing reads. The route is `/api/designer/governance` on
  both hosts through one shared response mapper; the browser cover drives the real desktop host end
  to end, because the seams that have failed silently in this workbench are the ones between the rail
  button, the route, and the author's buffer.
- [x] **Add data-quality rule authoring**: The same Governance panel now authors a column's
  `EXPECT` clauses and the statement's `ON FAILURE` routing, because a rule and a tag are governance
  an author attaches to the same column — but the panel keeps them apart, since a tag *describes* a
  column and an `EXPECT` clause *decides which rows leave the statement*. Rules are edited as
  grammar, never as the `@expect`/`@fail` tags the engine projects from them: a hand-written rule tag
  is inert and looks enforced, which is why the tag surface refuses to author one. **Nothing
  re-implements the rule grammar.** The picker offers twelve starting shapes with `«guillemet»`
  placeholders, the text stays editable, and the verdict comes from reparsing the whole script — a
  service that decided for itself what `MATCHES` accepts would diverge from the parser the moment
  either changed, and the divergence would surface as a rule that lints clean and never runs.
  **Routing stays a statement-level decision**, as the language has it: a column elects an action and
  the statement says where those rows go, because a per-column target would let two columns of one
  statement disagree about where the same run's rows land. The panel reports the case nothing else
  catches — a column electing `QUARANTINE` on a statement that routes nowhere, where the rows have
  no destination and the run only says so when it happens — rather than quietly writing a route the
  author did not choose. An omitted action is shown as `WARN (default)`, because "defaults to warn"
  and "somebody chose warn" are different facts about a pipeline. **Quarantine inspection and replay**
  are linked from the routing section on the Portal, straight to the steward queue that reads the
  persisted evidence; the desktop host sends no link and says where the queue lives instead, because
  it persists no quarantine evidence and a link that goes nowhere is worse than a sentence. Rule
  authoring is offered only on statements that name what they produce — a rule elsewhere has no
  stable identity to edit against, and quarantined rows from a query whose output goes straight to a
  reader have nowhere meaningful to be routed.
- [x] **Add row-level-security preview-as**: A run can now evaluate the author's own `HAS_GROUP` /
  `HAS_ROLE` predicates as a named **audience** — a label plus groups and roles — set from the
  Governance rail on both hosts. The invariants live in one place, `ExecutionIdentity.Preview`, so
  the two hosts cannot build a preview identity differently: the real actor is unchanged (dataset and
  connection authority is keyed on it and stays exactly what it was), the audience carries **no
  administrator authority** (an administrator sees every row by design, so previewing as one answers
  "all rows" whatever the predicate says — the one answer a preview must never give), it carries
  **no user id** (a made-up audience holding somebody's id would compare equal to them in a predicate
  written against `@@CURRENT_USER_ID`), and the tenant binding is the caller's own. On the Portal the
  shared connection is still resolved against the **caller's** identity, not the preview, so a
  preview cannot decide what data a run reaches; runs under one audit as `AD_HOC_RUN_AS` against the
  real actor, never the audience, because an audience is not a principal anyone can be asked about.
  **Deliberately not a list of people.** Previewing as a *named user* is impersonation of a real
  identity and already exists on a saved report — `POST /api/reports/{id}/execute-as/{userId}`, gated
  by administrator authority or Manage on that report's folder. Offering it here on an unsaved draft
  would be a second impersonation door with a weaker gate. A persistent banner says whose result is
  on screen, because previewed rows look exactly like real ones and an author who forgets will read
  somebody else's empty result as a bug in their query. On the desktop host this is the *only*
  identity injected: a workstation run otherwise carries none, so RLS is fail-closed and a guarded
  report shows nothing there with no explanation — which the browser test asserts in both directions
  from one script. The work also fixed a real defect it exposed: the banner's `display:flex` beat the
  user agent's `[hidden]` rule, so it would have been an empty strip that never went away and, worse,
  stayed on screen after the preview was cleared.
- [x] **Add dataset lifecycle actions**: The Governance panel lists every `CREATE DATASET` the
  script declares — who may see it, how long it lives, whether it is compressed and how it is
  encrypted — and authors the parts that are the script's to decide. **The clause-preservation
  requirement came first, and it was already broken.** `PatchDatasets` regenerated the whole
  statement from designer state, which models only the query and the TTL, so any edit that touched a
  dataset's query rewrote `ACCESS PUBLIC` back to the private default and dropped the encryption
  mode — and applying a dataset-scoped filter is such an edit, one click, in a file the author had
  already reviewed. There was a test asserting the loss as a known limitation; it now asserts the
  opposite. The fix is the third option that note left open: **never write the bytes that hold an
  unmodelled clause.** An edit is a span — the query's own span, the TTL clause's own span — so a
  clause nothing in the pipeline models survives the pipeline untouched, and `PASSWORD = '…'` is
  never read out of the script and written back through a round-trip it had no reason to make. A
  second silent loss fell out of the same investigation: `ToStateDto` dropped `Ttl`, so a round-trip
  through the browser handed the patcher a dataset whose TTL was null — indistinguishable from "the
  author cleared it" — and deleted the clause. **Access and TTL are authored** by the same span
  discipline in a purpose-built editor, so making a dataset public edits the `ACCESS` clause and
  nothing else, and making it private again removes the clause rather than writing the default.
  **Refresh, export and publish are written as statements**, not performed as buttons: a
  `REFRESH DATASET &sales;` in the script is a durable declaration that runs every time the script
  does and says why it exists, where a button refreshes one copy once and leaves no trace. An export
  or publish is refused without a transport credential, because the file leaves the machine that
  wrote it and cannot carry the at-rest key only that machine holds — a statement without one
  produces a file nothing can publish, and finding that out is a run away. **Encryption is reported
  and not authored**, and **per-principal sharing stays in the catalog**, which has its own
  permission model; the panel links to it with the dataset named rather than growing a second door
  with a weaker gate. On the desktop host there is no registry and the panel says so.
- [x] **Define scheduling and delivery handoff**: **Decided: Studio does not host schedules or
  subscriptions.** A schedule lives in the Orchestrator's catalog and a subscription in the Portal's,
  each with a permission model, a history, and an operator who owns it; a workbench that listed and
  edited them would be a second door onto both, with a weaker gate and no history — the same mistake
  as previewing as a named user on an unsaved draft, or duplicating dataset sharing. What Studio does
  instead is the one thing only it can: write the statements that make **this** document recurring,
  into the file the author is looking at — `CREATE SCHEDULE`, `CREATE JOB … FOR SCRIPT|REPORT
  '<path>'`, `ALTER JOB … ADD SCHEDULE` — and then open the Orchestrator at the job it just named.
  **The statements are the artifact**: the recurrence is reviewable, diffable, and deployable with
  everything else, where a button that registered a job directly would leave the fact of its
  existence nowhere in the repository. A `.rptsql` is scheduled as a report and a `.etlsql` as a
  script, from the document's own path. **An unsaved document is refused, with the reason** — a job
  names a path on a server, so scheduling a buffer produces a job that fails on its first tick with a
  missing file, hours later, to somebody else. Two jobs on one cadence **reuse the schedule that
  names it** rather than declaring a second, or changing the cadence later becomes a search. The
  Orchestrator page now accepts `?job=<name>` and opens that job's detail once its list has loaded,
  so the handoff lands on the artifact rather than on a list of every job in the workspace; a name
  that matches nothing leaves the list alone, because the job may not have been run into existence
  yet. On a host with no orchestrator the panel says the statements are what register the job rather
  than offering a link that goes nowhere. **Delivery stays a subscription on the report**, kept where
  its recipients and their permissions are — and where the row-level-security rule that refuses
  shared delivery of an identity-sensitive report already lives.

### Phase 6 — Cross-Host Certification (Next)

**Outcome:** Desktop and Portal prove the same representative jobs, round-trip contracts, and
performance limits before Studio is treated as the primary workbench.

- [x] **Complete the single end-to-end browser journey**: `StudioContinuousJourneyTests` drives one
  continuous connect → pick table → drag a visual card onto the canvas → configure it → filter →
  open Split view → edit code → run journey against both production hosts, and hands what each host
  saved to `StudioCertification`. The steps each already had a test; running them in one continuous
  pass is a different claim, because a test that sets up the state its own step needs never finds a
  step that only works from a state the previous step does not leave behind — and this one found four
  defects in the drag, all of them silent.
  **The palette card was draggable and the drop did nothing.** The card set a drag payload and the
  canvas had a drop handler, so the affordance looked live: the card even appeared on the canvas. But
  the visual was created with no source, `CREATE VISUAL x AS BAR (...)` without a `SOURCE` clause does
  not parse, and the patcher refuses a patch that does not parse — correctly, since writing a broken
  document over a working one is worse. So the script never changed, nothing was said, and the visual
  was gone on the next reload. A visual is now given a source before the card exists — the host's own
  binding, else the first dataset, else the source a visual on the page already uses — and an add with
  nothing to bind is refused with the reason instead of half-happening.
  **The source it was given did not parse either.** `SOURCE` takes a temp table, a dataset, or a
  query, never a qualified connection reference, and the sample endpoint names what it sampled —
  `alias.Table`. That name was being written straight through as a `SOURCE` operand, so every visual
  bound to a connection-backed sample produced a script the parser rejected — the palette's own click
  path included, not just the drag. A connection-backed sample is now written as the query it means,
  and the data wizard's preview shows the same thing rather than promising a clause that cannot be
  written.
  **Opening the inspector authored a statement.** Rendering the report-properties panel defaulted
  `reportStyle` into the design state, so a report the author never themed gained a
  `SET REPORT THEME = 'light';` on nothing more than a render — and the Portal refuses
  `SetReportMetadata` in an interactive run, which made the document unrunnable in Studio. Reading the
  panel no longer writes.
  **"Run all" is a desktop affordance.** The Portal's interactive-run policy refuses presentation
  statements, because a report is executed by rendering it, so the journey runs the selection on both
  hosts — the step an author actually has mid-edit.
- [ ] **Close cross-platform Studio performance evidence**: Review the first green Linux and macOS
  artifacts alongside the Windows baseline for startup, post-GC heap, CodeMirror input-to-frame p95,
  250-row aggregation/render p95, and full-canvas redraw/layout p95. Do not publish the old ~1 ms or
  sustained 60 FPS claims unless reproducible measurements support them.
- [ ] **Certify the SSIS-like ETL journey**: From the GUI, use MOCKDB to extract, stage in `#temp`,
  validate, transform, branch into explicit parallel work, load, and inspect intermediate state.
- [ ] **Certify the SSRS-like paginated journey**: From the GUI, create a parameterized grouped report
  with details, totals, headers, repeating columns, page breaks, and a correct multi-page PDF.
- [ ] **Certify the Power BI-like dashboard journey**: From the GUI, create KPI, trend, category, and
  detail visuals with slicers, cross-filtering, and persistent formatting.
- [x] **Apply the common certification contract**: One harness, `StudioCertification`, holds all five
  clauses — production host, `.etlsql`/`.rptsql` only, parser/linter/formatter, save-and-reload, and
  the code ↔ canvas round-trip — because three journeys each asserting their own version of "the
  script is valid" is three definitions and the weakest one decides what ships. Each clause is
  checked the literal way: the formatter must both reparse *and be idempotent* (a second pass that
  differs churns every file it touches); the linter is held to **error** severity only, since a lane
  that failed on advice would be switched off within a week and take the errors with it; the reload
  claim is asserted against the bytes the host returned, not what the editor believed it saved; and
  the round-trip claim is byte-for-byte for a document that has a canvas, falling back for a pipeline
  script — which has no pages, so patching one scaffolds the page a report would need — to the claim
  that is actually true: every line the author wrote is still there and no statement was rewritten.
  The harness is itself covered by ten tests that show each clause failing on an artifact that
  violates it, because a clause that silently never fires would take all three journeys green with
  it. Writing it immediately found a real round-trip infidelity: the patcher compared a generated
  header (`CREATE PAGE [Main]`) against the author's (`CREATE PAGE Main`) as text, so brackets — which
  are quoting, not identity — made every untouched page look changed and rewrote it. Header
  comparison is now bracket-insensitive, and nothing bracket-stripped is ever written.

### Phase 7 — Stabilization and Legacy Retirement

**Outcome:** Studio becomes the supported flagship only after the new workbench has evidence that it
can replace the old entry points.

- [ ] Complete user acceptance, accessibility review, failure-recovery testing, and performance
  benchmarking for the certified journeys.
- [ ] Build a capability matrix against `ReportBuilder` and `WorkstationEditor`; resolve or document
  every gap before changing defaults.
- [ ] Deprecate legacy entry points with migration guidance, then retire them in a later release after
  the deprecation window and rollback plan are verified.

## 2. v0.19.0 Release Evidence Gates

Target release: **v0.19.0**

Authoritative policy: [`release-checklist.md`](docs/releases/release-checklist.md) and
[`Enterprise_Release_Evidence_Checklist.md`](docs/architecture/decisions/enterprise-release-evidence-checklist.md).

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

## 3. Chart Property Gaps

Gaps identified from a comparison of ETL-SQL chart properties against Power BI, Tableau, and ggplot2.
Intentional design choices (LOD expressions, table calculations, and groupings handled in SQL) are
excluded. Items marked `CUSTOM only` already work in `CUSTOM CHART` but are absent from the named
chart surface (BAR, LINE, HBAR, COMBO, SCATTER, TRELLIS).

### Axis Controls

- [x] **Axis titles on named charts**: BAR, LINE, HBAR, and SCATTER have no axis label option. COMBO
  exposes `Y_AXIS (LABEL = ...)` and `Y2_AXIS (LABEL = ...)` but nothing else does. Add `X_AXIS` and
  `Y_AXIS` title options to all named Cartesian charts. Trivial addition; high-visibility gap.
- [x] **Explicit axis MIN / MAX on named charts**: `CUSTOM CHART` scales accept `MIN = literal` and
  `MAX = literal`. BAR, LINE, HBAR, COMBO, and SCATTER have no equivalent. Add `Y_AXIS (MIN = n,
  MAX = n)` and `X_AXIS (MIN = n, MAX = n)` options. Prerequisite for synchronized dual axes.
- [x] **INCLUDE_ZERO on named charts**: `CUSTOM CHART` scales expose `INCLUDE_ZERO = ON`. Named charts
  have no equivalent. Add `Y_AXIS (INCLUDE_ZERO = ON|OFF)` alongside axis MIN/MAX above.
- [x] **Reverse axis**: No `REVERSE = ON` on any quantitative axis in any surface. Add to `CUSTOM CHART`
  LINEAR/TIME scales and to the named-chart `Y_AXIS` / `X_AXIS` block.
- [x] **Tick mark controls**: No tick count, tick interval, or minor-tick option anywhere. Add
  `MAJOR_TICK_COUNT`, `TICK_INTERVAL`, and `MINOR_TICKS = ON|OFF` to axis options in both surfaces.
- [x] **Axis label overlap handling**: No rotate, skip, or truncate option. Axis tick-label density is
  fully renderer-inferred. Add `LABEL_ROTATION = AUTO|0|45|90` and `LABEL_SKIP = AUTO|n` to named-
  chart axis options and to `CUSTOM CHART` scale declarations.

### Gridline and Line Styling

- [x] **Zero-value reference line on named charts**: `CUSTOM CHART` can approximate with a `RULE` layer
  at `DATUM(0)`, but named charts have nothing. Add `ZERO_LINE = ON|OFF` (with optional color/style)
  to BAR, LINE, HBAR, and COMBO.
- [x] **Gridline styling**: `GRID_LINES = ON|OFF` is all-or-nothing. Add `GRID_LINE_COLOR`,
  `GRID_LINE_DASH` (`SOLID|DASHED|DOTTED`), and `GRID_LINE_WIDTH` options to named charts. Also add a
  separate `MINOR_GRID_LINES = ON|OFF`.
- [x] **Axis spine control**: No option to show or hide the axis spine (the outer border line of the
  plot area). Add `AXIS_LINE = ON|OFF` to named chart axis options.

### Bar and Area Layout

- [x] **100% / normalized stacking on named charts**: `STACK = NORMALIZE` exists in `CUSTOM CHART`.
  Named BAR, HBAR, and LINE/AREA have no `STACKED = 100PCT` or equivalent. Add alongside the existing
  `STACKED = ON|OFF`.
- [x] **Series gap in grouped bars**: No `SERIES_GAP` option. Spacing between bars within a grouped
  cluster is renderer-inferred. Add `SERIES_GAP = 0.0..1.0` to BAR, HBAR, and COMBO.
- [x] **Outer category padding**: `BAND_SIZE` controls bar width but not padding before the first and
  after the last category. Add `OUTER_PADDING = 0.0..1.0` to BAR, HBAR, and `CUSTOM CHART` BAND
  scales.

### Analytical Overlays

- [x] **Error bars**: No error bar support anywhere. Add `ERROR_LOW` and `ERROR_HIGH` encoding channels
  to `CUSTOM CHART` POINT and RECT layers. For named SCATTER, add `ERROR_LOW` / `ERROR_HIGH` mappings
  with optional `ERROR_BAR_STYLE = CAPS|NO_CAPS`. Values are pre-computed in SQL.
- [x] **Forecast / anomaly visual encoding**: No forecast overlay type. Data-side calculation in SQL is
  possible but there is no visual encoding for a dashed future segment, confidence band, or anomaly
  marker. Add `FORECAST` as an `OVERLAYS` type on named time-series charts (LINE, COMBO) and define
  `CONFIDENCE_LOW` / `CONFIDENCE_HIGH` channels in `CUSTOM CHART`.
- [x] **Arbitrary reference lines on named charts**: `OVERLAYS` supports GOAL, AVERAGE, and
  MOVING_AVERAGE. There is no `REFERENCE_LINE (VALUE = n, LABEL = '...', STYLE = DASHED)` for an
  author-specified constant. Add to the `OVERLAYS` clause for named charts.
- [x] **Reference bands on named charts**: Shaded horizontal or vertical regions between two values.
  `CUSTOM CHART` RECT with `Y_START`/`Y_END` covers this. Add
  `REFERENCE_BAND (LOW = n, HIGH = n, COLOR = '...', LABEL = '...')` to named-chart `OVERLAYS`.
- [x] **`OVERLAYS` extensions for common table-calc patterns**: Add `RUNNING_TOTAL` and
  `PERCENT_OF_TOTAL` as named overlay types on LINE and BAR charts so authors familiar with Tableau
  table calculations have a shortcut. The underlying computation stays in SQL; the overlay annotates
  the rendered series.

### Series and Data Labels

- [x] **Series end-of-line labels**: No option to place a series name at the last data point of a LINE
  chart. Add `SERIES_LABELS = ON|OFF WITH (POSITION = END|START)` to LINE and COMBO.
- [x] **Data label background and border**: No label background fill or border stroke on `DATA_LABELS`.
  Add `LABEL_BACKGROUND = '#RRGGBB'` and `LABEL_BORDER = 'css-border'` to the `DATA_LABELS WITH (...)`
  block on all named charts.
- [x] **Leader lines**: No connector line from a detached label to its mark. Add
  `LEADER_LINE = ON|OFF WITH (COLOR = '...', STYLE = SOLID|DASHED)` to `DATA_LABELS` on PIE, DONUT,
  and SCATTER, where floating labels are most common.

### Marker and Line Geometry

- [x] **Point shape vocabulary**: `SHAPE` encoding exists in `CUSTOM CHART` POINT but the accepted
  values are undocumented. Define and document the shape vocabulary (`CIRCLE`, `SQUARE`, `TRIANGLE`,
  `DIAMOND`, `CROSS`, `STAR`) in the `CUSTOM CHART` reference. Add a `SYMBOL_SHAPE` option to named
  LINE and SCATTER charts.
- [x] **Point stroke**: No stroke color or border width for point markers in any surface. Add
  `SYMBOL_STROKE_COLOR` and `SYMBOL_STROKE_WIDTH` to named LINE/SCATTER options and to `CUSTOM CHART`
  POINT layer STYLE tokens.
- [x] **Line width**: No `LINE_WIDTH` option on LINE or COMBO. `THICKNESS` in `CUSTOM CHART` applies to
  TICK marks, not LINE layers. Add `LINE_WIDTH = n` to named LINE and COMBO charts, and clarify or add
  a thickness property for LINE layers in `CUSTOM CHART`.
- [ ] **Step interpolation**: `SMOOTH = ON|OFF` exists but there is no step-before / step-after
  interpolation. Add `INTERPOLATION = LINEAR|SMOOTH|STEP_BEFORE|STEP_AFTER` to LINE, COMBO, and
  `CUSTOM CHART` LINE layers.
- [ ] **Line dash style on series**: `OVERLAYS` accept `AS DASHED` but regular line series have no dash
  option. Add `LINE_DASH = SOLID|DASHED|DOTTED` to LINE and COMBO options, and document the equivalent
  STYLE token for `CUSTOM CHART` LINE layers.

### Legend Controls

- [x] **Legend title**: No option to set or suppress the legend title text. Add `LEGEND_TITLE = 'text'`
  and `LEGEND_TITLE = NONE` to all named charts that support `LEGEND = ON|OFF`.
- [x] **Legend typography**: No per-legend font controls. Global `STYLE (FONT = ...)` applies broadly.
  Add `LEGEND_FONT_SIZE`, `LEGEND_FONT_COLOR`, and `LEGEND_FONT_WEIGHT` to named chart OPTIONS.
- [x] **Legend orientation**: Position (`TOP|RIGHT|BOTTOM|LEFT`) is supported but orientation within the
  legend box is renderer-inferred. Add `LEGEND_ORIENTATION = HORIZONTAL|VERTICAL`.
- [x] **Legend reverse order**: No `LEGEND_REVERSE = ON|OFF` to flip the series order in the legend,
  which matters for stacked charts where the visual and legend stacking order should match.
- [x] **Legend inside placement**: Only outside placement is supported. Add `LEGEND_POSITION = INSIDE`
  with `LEGEND_ANCHOR = TOP_LEFT|TOP_RIGHT|BOTTOM_LEFT|BOTTOM_RIGHT` for overlay legends.
- [x] **Legend multi-column layout**: No `LEGEND_COLUMNS = n` or wrapping control. Add for horizontal
  multi-row legends at `TOP` or `BOTTOM` position.

### Plot and Panel Styling

- [ ] **Plot-area background**: `STYLE (BACKGROUND = ...)` targets the visual card, not the inner chart
  canvas. Add `PLOT_BACKGROUND = '#RRGGBB'` (or `transparent`) to named chart OPTIONS and to
  `CUSTOM CHART` to style the region bounded by the axes independently of the card.
- [ ] **Plot-area border**: No `PANEL_BORDER` or equivalent. Add `PLOT_BORDER = 'css-border'` to named
  chart OPTIONS alongside `PLOT_BACKGROUND`.
- [ ] **Axis typography**: No axis-specific font controls. Global `STYLE (FONT, FONT_SIZE)` affects the
  whole visual. Add `AXIS_FONT_SIZE`, `AXIS_FONT_COLOR`, and `AXIS_TITLE_FONT_SIZE` to named chart axis
  option blocks.

### Tooltip

- [ ] **Declarative field-list tooltip**: The gap between `TOOLTIP = 'static text'` and a full popover
  container is wide. Add a middle tier: `TOOLTIP (FIELDS = (FieldA FORMAT 'C0', FieldB, FieldC FORMAT
  'P1'))` that renders a lightweight, formatted field list without requiring a `CREATE CONTAINER`. Field
  names are column aliases from the visual SOURCE; FORMAT strings follow the existing `DATA_LABELS`
  format convention.

### Interactions and Actions

- [ ] **URL action**: No `ON_CLICK = OPEN_URL(...)` action. Add `OPEN_URL(TEMPLATE = 'https://...',
  PARAMS = (field1, field2))` as a supported `ON_CLICK` action, with field values interpolated into the
  URL template. Reviewed against the Zero-Trust path policy — URL templates are author-declared and
  field values are HTML-encoded; no path resolution applies.
- [ ] **Source-visual interaction targeting**: `INTERACTIONS (ON_SELECT = FILTER)` on a receiving visual
  responds to any selection on the page. There is no way to say "this bar chart only filters visuals A
  and B when clicked." Add `EMIT_FILTER (TARGETS = (VisualName, ...))` as an action on the emitting
  visual, complementing the existing receive-side `INTERACTIONS` clause.

### Dual-Axis

- [ ] **Synchronized dual axes on COMBO**: No `SYNC_AXES = ON` option. Y and Y2 scales are always
  independent. Add as a boolean option after named-chart explicit axis MIN/MAX (above) is implemented,
  since synchronization requires the ability to set matching limits.

### Named Chart Marks

- [ ] **Per-axis mark type on COMBO**: COMBO renders bars on Y and a line on Y2 with no overrides. Add
  `Y_MARK = BAR|LINE|AREA` and `Y2_MARK = BAR|LINE|AREA` options so authors can compose, for example,
  area + line or line + line on dual axes without switching to `CUSTOM CHART`.
- [ ] **Size control on named LINE and SCATTER**: Named LINE has no point size option beyond
  `SYMBOLS = ON|OFF`. Named SCATTER has no marker size option independent of the BUBBLE SIZE mapping.
  Add `SYMBOL_SIZE = n` to both.

### Map

- [ ] **Tile-based base map**: The MAP visual uses built-in GeoJSON topologies. There is no tile-based
  background map (Mapbox, OpenStreetMap, or equivalent). Add a `BASE_MAP = 'provider-url-template'`
  option with connector-governed URL allowlisting.
- [ ] **Geographic density map**: No density or hex-bin map type. `HEATMAP` is a category × category
  grid, not a geographic density surface. Evaluate as a new `MAP (MODE = DENSITY)` rendering path.

### Trellis / Small Multiples

- [ ] **TRELLIS scale synchronization options**: `TRELLIS` exposes `SHARED_AXIS = ON|OFF` for Y only,
  and always auto-scales independently on SCATTER. Add `SHARED_X = ON|OFF` and explicit support for
  controlling shared color scales across panels. Align `CUSTOM CHART` `RESOLVE` with the named TRELLIS
  option naming.

### PIE / DONUT

- [x] **Slice sort order**: PIE and DONUT always render slices in source row order. Add
  `SORT = SOURCE|VALUE_DESC|VALUE_ASC|ALPHA` so authors can control whether the largest slice leads,
  matches query order, or sorts alphabetically — matching Tableau and Power BI pie behavior.
- [x] **Minimum slice threshold / "Other" rollup**: No option to collapse slices below a threshold into
  an "Other" segment. Add `MIN_SLICE_PCT = n` with an `OTHER_LABEL = 'text'` companion. Power BI and
  Tableau both offer this; without it, busy pies become unreadable and there is no clean workaround
  short of pre-aggregating in SQL.
- [x] **Slice explosion / pull-out**: No `EXPLODE = 'SliceName'` or `EXPLODE_ALL = n` option to offset
  one or all slices for emphasis. Common in Power BI and ggplot2 (via `ggforce`).
- [x] **Slice border/stroke**: PIE and DONUT slices have no stroke color or width control. Every
  competitor offers at least a `stroke-width = 0` equivalent to remove the default inter-slice line.
  Add `SLICE_BORDER_COLOR = '#RRGGBB'` and `SLICE_BORDER_WIDTH = n` to PIE/DONUT OPTIONS.
- [x] **Start angle**: No `START_ANGLE = n` to rotate the first slice away from the default 12 o'clock
  position. Add to PIE and DONUT OPTIONS.

### SCATTER / BUBBLE

- [x] **COLOR mapping on BUBBLE**: `BUBBLE` accepts `X`, `Y`, `SIZE`, and `LABEL` but not `COLOR`.
  Adding a `COLOR` mapping for categorical coloring (matching the SCATTER visual) is missing. The gap
  is visible in the reference — `SCATTER` lists COLOR, `BUBBLE` does not.
- [x] **Bubble size range**: `BUBBLE` auto-scales `SIZE` to 5–65 px. There is no `MIN_BUBBLE_SIZE` or
  `MAX_BUBBLE_SIZE` option. Power BI and ggplot2 both expose size-range controls. Add
  `SIZE_RANGE = (min_px, max_px)` to BUBBLE.
- [x] **Log scale axes**: SCATTER and BUBBLE have no `X_AXIS (SCALE = LOG)` or `Y_AXIS (SCALE = LOG)`
  option. Log axes are standard in ggplot2 (`scale_x_log10`) and Tableau (right-click axis → Scale →
  Logarithmic). Add via the named-chart axis option block once MIN/MAX (section 3) is in place.
- [x] **Jitter on SCATTER**: No `JITTER = ON` or `JITTER (WIDTH = n, HEIGHT = n)` for SCATTER to
  separate overlapping points. `CUSTOM CHART` has `POSITION = JITTER(...)` on POINT layers. Expose
  on named SCATTER as a simple option.

### HEATMAP

- [x] **Diverging color scale**: `HEATMAP` accepts a two-stop or three-stop `COLORS` gradient but
  there is no explicit diverging scale with a named midpoint. `CUSTOM CHART` SCALES support
  `DIVERGING(LOW, MID, HIGH, MIDPOINT)`. Add `MIDPOINT = n` and `COLOR_MID = '#RRGGBB'` to HEATMAP
  to expose this for the common "negative/neutral/positive" heatmap pattern.
- [x] **Cell border styling**: No option to show or remove the grid lines between heatmap cells.
  Tableau and ggplot2 (`theme(panel.grid = ...)`) both expose this. Add `CELL_BORDER = ON|OFF` with
  optional `CELL_BORDER_COLOR`.
- [x] **Per-axis sort**: `HEATMAP` has no `X_SORT` or `Y_SORT` option. The category order on both
  axes is source row order. Add `X_SORT = SOURCE|ALPHA|VALUE_DESC` and `Y_SORT = SOURCE|ALPHA|
  VALUE_DESC` to allow clustering by row/column totals or alphabetical ordering.
- [x] **Null / missing cell treatment**: No `NULL_COLOR = '#RRGGBB'` option. When a row/column
  intersection has no data, the cell color is renderer-inferred. Add an explicit null-cell color option.

### WATERFALL

- [x] **Connector lines between bars**: Waterfall charts in Power BI and ggplot2 (`ggplot2::geom_waterfall`)
  draw a thin horizontal connector at the top/bottom of each bar linking to the next. No such option
  exists. Add `CONNECTOR_LINES = ON|OFF`.
- [x] **Subtotal bars**: Beyond the `TOTAL` flag for grand totals, there is no way to mark intermediate
  subtotal bars (which reset the running base). Power BI waterfall has this. Add a `SUBTOTAL` value for
  the `TOTAL` mapping column, or add a dedicated `SUBTOTAL` mapping.
- [x] **Horizontal waterfall**: No `ORIENTATION = HORIZONTAL` option, unlike FUNNEL and BOXPLOT which
  expose orientation. Add to WATERFALL.

### GANTT

- [ ] **Milestone markers**: GANTT renders only bars (start → end spans). Tableau and Power BI Gantt
  extensions support single-point milestone markers (diamond or circle at a date with no duration).
  Add support for rows where `START = END` to render as a milestone symbol, or add an explicit
  `MILESTONE = column` mapping.
- [ ] **Dependency arrows**: No way to draw dependency lines between tasks. This is advanced and
  specific to project management tools, but it is the primary Gantt gap versus dedicated tools.
  Log as an evaluation item; do not commit to implementation scope yet.
- [ ] **Today line / current-date marker**: No `TODAY_LINE = ON|OFF` option to draw a vertical
  reference line at the current date. Common in every Gantt implementation (Power BI, Tableau
  extensions, and dedicated PM tools). Add as a simple boolean with optional `TODAY_COLOR`.
- [ ] **Row grouping / swim lanes**: No `GROUP` mapping on GANTT. Tasks that belong to the same
  group (e.g., a phase) cannot be visually separated into swim lanes. Add `GROUP = column` to draw
  a labeled section header row between groups.
- [ ] **Bar label position**: No option to control whether the task label appears inside the bar,
  to the left of the start, or to the right of the end. Add `LABEL_POSITION = INSIDE|LEFT|RIGHT|NONE`.

### CANDLESTICK

- [ ] **Volume bars as a secondary series**: A candlestick chart without volume is common, but the
  canonical combination (OHLC candles + volume bars on a secondary Y axis) has no named-chart shortcut.
  `CUSTOM CHART` handles it via a second RECT layer with Y2. Add a `VOLUME` mapping to the named
  CANDLESTICK that renders volume bars on a secondary axis automatically.
- [ ] **Moving average overlay on CANDLESTICK**: CANDLESTICK has no `OVERLAYS` clause at all.
  A simple moving average line is the most common candlestick overlay in every charting tool.
  Extend `OVERLAYS` support (MOVING_AVERAGE, GOAL) to CANDLESTICK the same way it works on LINE.
- [ ] **Wick / shadow styling**: `COLOR_UP` and `COLOR_DOWN` control the candle body color. No option
  to set wick (shadow) color independently. Add `WICK_COLOR_UP` and `WICK_COLOR_DOWN`, or a
  `WICK_COLOR` override that applies to both.

### RADAR

- [ ] **Per-axis scale**: `RADAR` applies a single `MIN`/`MAX` to all axes. Tableau and ggplot2
  (`coord_polar` + custom scales) support independent axis scales when dimensions have different
  units. Add `INDEPENDENT_AXES = ON|OFF` to allow each dimension column to auto-scale independently.
- [ ] **Fill opacity**: Multi-series radar charts overlap. No `FILL_OPACITY = 0.0..1.0` option to
  make polygon fills semi-transparent. The overlap makes filled radars unreadable. Add alongside
  `LEGEND` controls.
- [ ] **Radar shape style**: No option for `SHAPE = POLYGON|CIRCLE` to control whether the background
  grid is drawn as nested polygons or concentric circles (ggplot2 `coord_radar` supports this).

### FUNNEL

- [ ] **Funnel sort control**: Stages are ordered by `VALUE` descending by default. There is no
  `SORT = SOURCE|VALUE_DESC|VALUE_ASC` option to preserve query order (needed when stages are not
  monotonically decreasing, e.g., a marketing funnel with re-engagement stages). Add to OPTIONS.
- [ ] **Absolute vs. relative percentage display**: `SHOW_PERCENT = ON` shows the stage-to-stage
  conversion rate. No option to show percent-of-total (first stage as denominator) alongside or
  instead. Add `PERCENT_MODE = STEP|TOTAL`.
- [ ] **Pyramid orientation**: Funnel charts traditionally widen at the top. A pyramid inverts this
  (widens at the bottom). Power BI offers both. Add `FUNNEL_SHAPE = FUNNEL|PYRAMID`.

### SANKEY

- [ ] **Node alignment**: Nodes are auto-positioned by the layout algorithm. No `NODE_ALIGN =
  LEFT|RIGHT|CENTER|JUSTIFY` option to control whether nodes snap to the left, right, or are
  justified between input/output layers (D3 Sankey standard). Add to OPTIONS.
- [ ] **Node padding / link opacity**: No `NODE_PADDING = n` (vertical gap between nodes) or
  `LINK_OPACITY = 0.0..1.0` option for the flow bands. Standard Sankey controls in every
  implementation (Power BI, Tableau extensions, D3). Add to OPTIONS.
- [ ] **Node coloring**: There is no `NODE_COLOR` mapping. Node colors are derived from the link
  colors or are renderer-assigned. Add `NODE_COLOR = column` mapping so authors can drive node
  fill from data.
- [ ] **Multi-level / multi-hop flows**: SANKEY supports `SOURCE → TARGET` pairs only. Multi-level
  flows (A → B → C in one row) require pre-exploding to two edge rows in SQL. This is workable
  but different from Tableau's Sankey extensions that accept a level column. Log as a doc clarification
  item: add an explicit note and a cookbook example showing the SQL pre-processing pattern.

### TREEMAP / SUNBURST

- [ ] **Color encoding independent of size**: `TREEMAP` has a `COLOR` mapping but it is optional.
  `SUNBURST` has no `COLOR` mapping at all — hierarchy level colors are auto-assigned. Add `COLOR`
  mapping to SUNBURST and document the interaction between `COLOR` and hierarchy level coloring
  in both visuals.
- [ ] **Breadcrumb / drill path display**: Clicking into a hierarchy node in TREEMAP or SUNBURST has
  no documented breadcrumb or back-navigation. Tableau and Power BI both show a path header. Add
  `SHOW_BREADCRUMB = ON|OFF` to both visuals.
- [ ] **Label truncation control**: For narrow tiles in TREEMAP, long labels silently clip or are
  hidden by the renderer. Add `LABEL_MIN_SIZE = n` (minimum tile px to show a label) and
  `LABEL_OVERFLOW = CLIP|WRAP|HIDDEN` to TREEMAP.

### BOXPLOT

- [ ] **Notched boxes**: No `NOTCHED = ON|OFF` option for confidence-interval notches around the
  median, which is standard in ggplot2 (`geom_boxplot(notch = TRUE)`) and used in scientific
  reporting. Add to OPTIONS.
- [ ] **Mean marker**: No option to overlay the mean value as a distinct point or line on each box.
  ggplot2 `stat_summary(fun = mean, geom = "point")` is the equivalent. Add `SHOW_MEAN = ON|OFF`.
- [ ] **Violin overlay / violin-only mode**: ggplot2 `geom_violin` shows the full distribution shape.
  No violin option on BOXPLOT. This is lower priority but worth noting as a distribution-chart gap.

### NETWORK

- [ ] **Node size mapping**: NETWORK nodes have no `NODE_SIZE` mapping. Node area conveying a metric
  (e.g., degree centrality, revenue) is standard in Gephi, Tableau network extensions, and
  networkD3. Add `NODE_SIZE = column` mapping.
- [ ] **Edge directionality arrows**: No `DIRECTED = ON|OFF` option to render arrowheads on edges.
  Without arrows, directed graphs (source → target) are visually indistinguishable from undirected
  ones. Add `DIRECTED = ON|OFF` to OPTIONS.
- [ ] **Node label control**: Node labels are always rendered (FROM/TO column values). No option to
  show, hide, or threshold labels (e.g., only show labels for nodes with degree > n). Add
  `NODE_LABELS = ON|OFF` and `NODE_LABEL_MIN_SIZE = n` (hide label if the node is below n px).
- [ ] **Fixed node positions**: No way to pin a node at a specific coordinate. LAYOUT is FORCE or
  CIRCULAR, both fully algorithmic. This is an advanced need but comes up in lineage diagrams where
  a canonical left-to-right layout matters. Log as evaluation only.

### MATRIX

- [ ] **Column totals**: `GRAND_TOTAL = ON` adds a row total. There is no column grand-total option
  (a bottom-margin row summing all COL values). Power BI matrix and Tableau pivot both offer both.
  Add `COLUMN_TOTAL = ON|OFF`.
- [ ] **Conditional cell formatting**: `FORMATTING` on TABLE accepts `WHEN condition THEN color`.
  MATRIX has no `FORMATTING` clause. Heatmap-style cell coloring (color by value range) and
  threshold-based highlighting are absent. Add `FORMATTING (WHEN value > n THEN '#RRGGBB')` to
  MATRIX, similar to TABLE.
- [ ] **Cell data bars in MATRIX**: TABLE supports `DATA_BAR` on column mappings. MATRIX has no
  equivalent for visualizing magnitude within a cell. Add `VALUE DATA_BAR` mapping syntax to MATRIX.
- [ ] **Expand/collapse default state**: MATRIX row groups are collapsible but the default expanded
  depth is not controllable. Add `DEFAULT_EXPAND = ALL|NONE|LEVEL_1|LEVEL_2` to OPTIONS.

### TABLE

- [ ] **Column pinning / freeze**: No `FREEZE = LEFT|RIGHT` on a TABLE column mapping to keep it
  visible during horizontal scroll. Power BI and virtually every data grid supports this. Add as a
  mapping modifier: `column FREEZE LEFT AS 'Name'`.
- [ ] **Column width control**: Column widths are renderer-inferred. No `WIDTH = n` on a TABLE
  column mapping. Add `column WIDTH 120 AS 'Name'`.
- [ ] **Multi-column sort default**: TABLE allows interactive click-to-sort but no
  `DEFAULT_SORT = (column DESC, column2 ASC)` to set the initial sort order without sorting in SQL.
  Add to OPTIONS.
- [ ] **Row group totals position**: `GRAND_TOTAL` places the total row at the bottom. No
  `TOTAL_POSITION = TOP|BOTTOM` option. Power BI tables can show totals at the top.

### CARD

- [ ] **Comparison period label**: `DELTA` shows a numeric difference but `DELTA_LABEL` is a static
  string. No way to bind the comparison label dynamically from a column (e.g., "vs Q3 2025" from
  data). Add `DELTA_LABEL = column` as an alternative to the static string form.
- [ ] **Conditional value color**: The `COLOR_MET / COLOR_CLOSE / COLOR_MISSED` options apply to the
  status badge only. The large VALUE number itself has no conditional coloring independent of goal
  status. Add `VALUE_COLOR = 'css-color'` and a `FORMATTING (WHEN condition THEN color)` clause to
  CARD for threshold-driven value color (common Power BI KPI card behavior).
- [ ] **Sparkline color and reference line**: The CARD `SPARKLINE` accepts TYPE but no `COLOR` or
  `REFERENCE_LINE` options. You cannot set the sparkline line color or add a goal reference line
  without rebuilding as a full chart. Add `COLOR = '#RRGGBB'` and `REFERENCE_LINE = n` to the
  SPARKLINE mapping.

### GAUGE

- [ ] **Gauge value label formatting**: No `FORMAT` option on GAUGE to control how the current value
  is displayed (currency, percentage, decimal places). The value label format is renderer-inferred.
  Add `FORMAT = '.NET format string'` to GAUGE OPTIONS, consistent with CARD.
- [ ] **Multiple target bands**: `COLORS = ('0%:red', '60%:yellow', '80%:green')` sets color band
  positions but `GOAL` is a single value. No way to show multiple threshold markers (e.g., a
  "warning" band and a "critical" band marker). Add `GOAL2 = column` and `GOAL2_LABEL` to GAUGE
  mappings, or allow `GOAL = (value1 LABEL '...', value2 LABEL '...')`.
- [ ] **Gauge label position**: No option to control where the value label appears (center, below the
  arc, inside the needle). All GAUGE_STYLEs render the label at a renderer-determined position.
  Add `LABEL_POSITION = CENTER|BOTTOM|INSIDE` where applicable per style.

### MAP (additional gaps beyond section 3)

- [ ] **Choropleth color scale type**: `COLOR_LOW` and `COLOR_HIGH` create a linear gradient.
  No `COLOR_SCALE = LINEAR|QUANTILE|QUANTIZE|THRESHOLD` option. Tableau and Power BI both offer
  quantile and quantize binning so that equal data ranges map to equal visual segments rather than
  a continuous gradient. Add to CHOROPLETH OPTIONS.
- [ ] **Null region color**: No `NULL_COLOR = '#RRGGBB'` for regions present in the map geometry
  but absent from the data. The renderer fills them with an inferred default. Add an explicit
  null-region color control.
- [ ] **Map zoom and center**: No `ZOOM = n` or `CENTER = (lat, lon)` option to set the initial
  map viewport. The map auto-fits to the data extent. For dashboards that always show a fixed
  region (e.g., always centered on the continental US), there is no way to lock the view. Add
  to MAP OPTIONS.
- [ ] **Point color mapping on MAP POINTS mode**: `MAP (MODE = POINTS)` accepts `VALUE` for size
  but has no `COLOR` mapping. Points are all the same color. Add `COLOR = column` mapping to
  POINTS mode to allow categorical or quantitative coloring independent of size.
- [ ] **Tooltip on map regions / points**: MAP has no `TOOLTIP` clause and no per-feature tooltip
  customization. The hover shows the REGION name and VALUE only. Add `TOOLTIP` clause support
  consistent with other named chart visuals.

### THEME

- [ ] **Per-visual-type theme overrides**: `CREATE THEME` sets global tokens. There is no way to
  say "BAR charts in this theme use palette X, LINE charts use palette Y." Power BI themes support
  per-visual-type color arrays. Add optional `[BAR] COLORS = (...)` or `[LINE] COLORS = (...)`
  blocks inside `CREATE THEME`.
- [ ] **Theme font stack**: `CREATE THEME` has `TEXT_COLOR` and the global `STYLE (FONT = ...)` handles
  font family. But there is no `FONT_FAMILY` key inside `CREATE THEME` itself, so a theme cannot
  encode the font stack. Add `FONT_FAMILY = '...'` as a supported theme property so a brand theme
  can fully specify typography without a separate `CREATE STYLE`.

### Cross-Cutting Gaps Found in Second Pass

- [ ] **`FORMATTING` clause on SCATTER, BUBBLE, LINE, COMBO**: The `FORMATTING (WHEN condition THEN
  color)` conditional mark coloring works on BAR and HBAR. It is absent from SCATTER, BUBBLE, LINE,
  and COMBO. These charts drive conditional coloring only via `CUSTOM CHART` CONDITIONS or the
  `COLOR` series mapping. Add `FORMATTING` clause support to all named Cartesian charts.
- [ ] **`OVERLAYS` clause on SCATTER**: SCATTER has `SHOW_REGRESSION = ON|OFF` but no general
  `OVERLAYS` clause. Add `OVERLAYS` to SCATTER so reference lines, goal lines, and (eventually)
  error bars share the same syntax as LINE and BAR.
- [ ] **`ZOOM_SLIDER` on SCATTER and COMBO**: `ZOOM_SLIDER = ON|OFF` is documented for LINE and BAR
  but not SCATTER or COMBO. These are the charts most likely to need it on dense data. Confirm
  whether it is implemented but undocumented, or genuinely absent, and add/document accordingly.
- [x] **`AXIS_SORT` on COMBO**: `AXIS_SORT` is available on BAR, HBAR, and LINE but not listed in
  COMBO options. COMBO shares a category axis so the option is meaningful. Add or document.
- [ ] **`DATA_LABELS` on SCATTER, BUBBLE, RADAR, and HEATMAP**: `DATA_LABELS = ON|OFF` is
  documented on BAR, HBAR, LINE, and COMBO but not on SCATTER, BUBBLE, RADAR, or HEATMAP.
  HEATMAP has `SHOW_VALUES` (a partial equivalent); the others have nothing. Standardize on
  `DATA_LABELS` across all named chart types or explicitly document the per-type label option.

### SLICER

- [ ] **Search / type-to-filter inside the dropdown**: SLICER is a plain dropdown with no
  in-control search box. Power BI slicers and Tableau filter dropdowns both allow typing to
  narrow a long option list. Add `SEARCHABLE = ON|OFF` to SLICER OPTIONS so long value lists
  become usable without a separate SEARCH control.
- [ ] **Multi-value selection mode on SLICER**: SLICER is single-select only; multi-select
  requires a separate MULTISELECT control. Power BI slicer has a toggle between single and
  multi-select. Add `MODE = SINGLE|MULTI` so one control can serve both patterns without
  requiring two visual types.
- [ ] **Tile / button layout mode**: Tableau and Power BI both support a "tile" or "chip" layout
  where each option is a button rather than a dropdown row. `style.md` mentions
  `LAYOUT = 'DROPDOWN'` as a slicer rendering mode but no alternative layout (LIST, TILE,
  BUTTON_BAR) is documented. Confirm whether other layouts exist and document them, or add
  TILE and LIST modes explicitly.
- [ ] **Option count limit with overflow indicator**: No `MAX_OPTIONS = n` or paging control.
  When the SOURCE returns hundreds of values the dropdown becomes unusable. Add `MAX_OPTIONS = n`
  with a visible overflow indicator, paired with `SEARCHABLE = ON` for large lists.
- [ ] **Slicer sort**: The option list order is source row order. No `SORT = ALPHA|VALUE|SOURCE`
  on SLICER. Authors must pre-sort in the SOURCE query. Add a declarative sort option consistent
  with `AXIS_SORT` on charts.
- [ ] **Image per option**: No `IMAGE = column` mapping on SLICER. Authors cannot attach a
  photo, thumbnail, or icon to each option row — e.g., an employee directory slicer showing
  each person's headshot alongside their name, or a location slicer with a building photo per
  site, or a product slicer with product artwork. Add `IMAGE = column` to SLICER MAPPINGS
  where `column` contains a URL, file path, or base-64 data URI, alongside `LABEL` for the
  display text and `VALUE` for the bound value. Add companion options:
  - `IMAGE_SIZE = 'css-size'` (e.g., `'32px'`, `'64px'`) — controls the rendered image
    dimension within the option row.
  - `IMAGE_POSITION = LEFT|RIGHT|TOP` — where the image sits relative to the label text.
    `TOP` enables a tile/card layout where the image is above the label, useful for product
    or building selectors where the image is the primary visual cue.
  - `IMAGE_FIT = contain|cover|fill` — CSS object-fit behavior for the image within its
    allocated box, consistent with the IMAGE visual `FIT` option.
  This also applies to MULTISELECT, which shares the same MAPPINGS model.

### MULTISELECT

- [ ] **Default multi-value selection**: `DEFAULT = 'value'` accepts only a single value. There
  is no `DEFAULT = ('value1', 'value2')` list form. Selecting multiple initial values requires
  an APPLY_BOOKMARK workaround. Add list-form DEFAULT to MULTISELECT.
- [ ] **Select-all control naming**: `LEGEND = ON|OFF` toggles "Select all / Clear all" but the
  option is misnamed — LEGEND on a control is not a series legend. Rename to
  `SHOW_SELECT_ALL = ON|OFF` with companion `SELECT_ALL_LABEL` and `CLEAR_ALL_LABEL` text
  overrides.
- [ ] **Search inside MULTISELECT**: No in-control type-to-filter. Add `SEARCHABLE = ON|OFF`.
  Critical for MULTISELECT where long checkbox lists are even harder to scroll than a dropdown.
- [ ] **Tile / chip layout**: MULTISELECT is always a scrollable checkbox list. A compact chip /
  tag strip layout (selected items shown as removable badges) is standard in Power BI and
  embedded BI tools. Add `LAYOUT = LIST|CHIPS`.
- [ ] **Item limit / virtualization**: No documented limit or virtual scrolling for large option
  sets. Add `MAX_OPTIONS = n` with a visible truncation indicator, consistent with SLICER.

### DATEPICKER

- [ ] **Date range mode in one control**: Two separate DATEPICKER controls are needed for a
  start and end date. Power BI and Tableau both support a single date-range picker that emits
  two values. Add `MODE = SINGLE|RANGE` with `ON_CHANGE = SET_PARAMETER(@start, @end, value)`
  for RANGE mode.
- [ ] **Display format**: The displayed date format is locale/renderer-inferred. No `FORMAT =
  'MM/dd/yyyy'` option. Add FORMAT to DATEPICKER OPTIONS to control the rendered input string.
- [ ] **Disabled dates / blackout ranges**: No way to disable specific dates or ranges within
  the MIN/MAX window (e.g., disable weekends, holidays). Add `DISABLED_DATES = column` or
  `DISABLED_DAYS = (SAT, SUN)` to OPTIONS.
- [ ] **Week start day**: Calendar grids start on a renderer-default day. No `WEEK_START =
  SUN|MON` option. Add to OPTIONS.
- [ ] **Dynamic MIN / MAX from data**: MIN and MAX are static strings or `TODAY`. No
  `MIN = SOURCE_MIN(column)` dynamic binding. Authors must compute bounds in SQL and pass as
  a parameter. Add dynamic MIN/MAX binding.
- [ ] **Inline (always-open) calendar**: No `DISPLAY = INLINE|DROPDOWN` option. Some layouts
  embed an always-visible calendar. Add INLINE mode.

### RELDATEPICKER

- [ ] **Custom quick-pick buttons**: The quick-pick buttons (Today, D-1, D-7, D-30, M-1, M-3,
  Y-1) are hardcoded. No `QUICK_PICKS = ('This Week' = 'W-0', 'Last Month' = 'M-1', ...)`
  option to replace or extend the preset list. Power BI relative date slicer and embedded date
  pickers allow custom presets. Add a `QUICK_PICKS` option accepting label/expression pairs.
- [ ] **Fiscal period expressions**: Relative date syntax (`D-n`, `M-n`, `Y-n`) is calendar-
  based. No `FQ-1` (previous fiscal quarter) or `FY-1` (previous fiscal year) expression when
  the fiscal year doesn't start in January. Add fiscal offset expressions with a
  `FISCAL_YEAR_START = month` anchor.
- [ ] **Range mode**: Two separate controls are needed for start and end. Add `MODE = SINGLE|RANGE`
  so a single RELDATEPICKER emits both `@start` and `@end`.
- [ ] **Expression validation feedback**: An invalid expression (e.g., `X-7`) silently passes
  the raw string to the parameter. Add inline validation that marks the field invalid and
  suppresses `ON_CHANGE` until the expression resolves cleanly.
- [ ] **Future-date expressions**: Only `D-n` (past) is supported. No `D+n`, `M+n`, `Y+n` for
  future-relative dates (e.g., forecast end = D+30). Add forward-offset expressions.

### SLIDER

- [ ] **Range slider (two-handle)**: SLIDER is single-value only. A two-handle range slider
  (simultaneous min and max selection) requires two separate controls. Add `MODE = SINGLE|RANGE`
  with `ON_CHANGE = SET_PARAMETER(@low, @high, value)` for RANGE mode.
- [ ] **Snap to data values**: SLIDER has MIN, MAX, and STEP for uniform increments but no
  `SOURCE = #table, MAPPINGS (VALUE = column)` to snap handles to actual data breakpoints.
  Add data-driven tick positions.
- [ ] **Value display format**: The current value shown beside the handle is renderer-formatted.
  No `FORMAT = 'C0'` option. Add FORMAT consistent with CARD and GAUGE.
- [ ] **Tick mark labels**: No `SHOW_TICKS = ON|OFF` or `TICK_LABELS = ON|OFF` option.
  Add to OPTIONS.
- [ ] **On-change fire mode**: SLIDER fires `ON_CHANGE` on every drag increment, triggering
  expensive re-queries at each step. No `FIRE_ON = RELEASE|CHANGE` option to defer until
  handle release. Add alongside the SEARCH `DEBOUNCE` pattern.

### SEARCH

- [ ] **Match mode helper**: SEARCH passes the raw string to a parameter; the LIKE pattern must
  be constructed in every consuming query. Add `MATCH_MODE = CONTAINS|STARTS_WITH|EXACT` to
  OPTIONS so the control emits a pre-wrapped pattern, reducing boilerplate.
- [ ] **Minimum character trigger**: No `MIN_CHARS = n` to suppress `ON_CHANGE` until at least
  n characters are typed. Prevents single-character wildcard explosions on large tables. Add
  alongside `DEBOUNCE`.
- [x] **Clear button**: No `SHOW_CLEAR = ON|OFF` option to display an × button that resets the
  value. Add to OPTIONS.

### CHECKBOX

- [ ] **Label separate from TITLE**: CHECKBOX `TITLE` is the card title. The inline checkbox
  label is also driven by TITLE, with no separate `LABEL = 'text'` option for the text
  appearing beside the checkbox element itself. Add `LABEL` as a distinct option.
- [ ] **Toggle switch display style**: CHECKBOX always renders as a checkbox. No
  `DISPLAY_STYLE = CHECKBOX|TOGGLE` to render as a modern toggle switch, which is the standard
  Power BI boolean slicer form. Add to OPTIONS.
- [ ] **ON / OFF value override**: `ON_CHANGE` emits `1` or `0`. No `TRUE_VALUE = 'Y'` /
  `FALSE_VALUE = 'N'` option to emit domain strings instead of bit values. Add to OPTIONS.
- [ ] **Default state**: No `DEFAULT = ON|OFF` option documented. Authors must set the initial
  variable value via `DECLARE`. Add `DEFAULT` to CHECKBOX OPTIONS so the initial visual state
  is self-contained.

### TEXTBOX

- [ ] **Multiline / textarea mode**: TEXTBOX is single-line only. No `MULTILINE = ON|OFF` or
  `ROWS = n` option for memo-style input. Add to OPTIONS.
- [x] **Max length**: No `MAX_LENGTH = n` character limit. Add to OPTIONS.
- [ ] **Pattern / regex validation**: No `PATTERN = 'regex'` option to constrain input format
  inline. Add with a companion `VALIDATION_MESSAGE = 'text'` for the error hint.
- [ ] **ON_SUBMIT trigger**: TEXTBOX fires `ON_CHANGE` on every keystroke. No `ON_SUBMIT` trigger
  (fire on Enter or blur) which is the appropriate mode for inputs driving server queries.
  Add `ON_SUBMIT` as an alternative to `ON_CHANGE` in the ACTIONS clause.

### NUMBERBOX

- [ ] **Step increment buttons**: NUMBERBOX accepts typed input but has no `STEP = n` or
  `SHOW_STEPPER = ON|OFF` to add +/− spinner buttons. SLIDER has STEP; NUMBERBOX should too.
  Add to OPTIONS.
- [ ] **Value display format**: No `FORMAT = 'C2'` display option. Add FORMAT consistent with
  CARD and GAUGE.
- [ ] **ON_SUBMIT trigger**: Same as TEXTBOX — add `ON_SUBMIT` as an alternative to `ON_CHANGE`
  so the query fires only on Enter or blur, not on every keystroke.
- [ ] **Unit label (prefix / suffix)**: No `PREFIX = '$'` or `SUFFIX = 'kg'` display decoration.
  Authors cannot annotate the input without a container workaround. Add PREFIX and SUFFIX as
  display-only options (not appended to the emitted numeric value).

### Cross-Cutting Control Gaps

- [ ] **Reset parameters action**: No `RESET_PARAMETERS` action to return all parameters to
  their DEFAULT values in one step. `CLEAR_FILTERS` clears visual-level selections but does
  not reset parameter variables. The workaround is `APPLY_BOOKMARK` pointing to a DEFAULT
  bookmark, which is not documented or obvious. Add `RESET_PARAMETERS` (optionally scoped:
  `RESET_PARAMETERS (@region, @date)`) as a named action available on BUTTON.
- [ ] **Active filter count on containers**: No `SHOW_ACTIVE_COUNT = ON|OFF` container option
  to badge how many parameters are currently non-default. Power BI's filter pane shows a count
  indicator. Add as a container-level option so a collapsible filter BOX can surface "3 filters
  active" without custom HTML.
- [ ] **`DEPENDS_ON` for non-cascade controls**: CASCADE is available only on SLICER and
  MULTISELECT. DATEPICKER, SLIDER, and NUMBERBOX have no dependency declaration. When a
  SLIDER MAX should derive from a SLICER selection, the author has no declarative path. Add
  `DEPENDS_ON (@param, ...)` to all controls so the runtime re-evaluates DEFAULT and MIN/MAX
  when a dependency changes.
- [ ] **Standardize `DEBOUNCE` across all controls**: SEARCH has `DEBOUNCE = n` (ms). No other
  control documents this option, yet SLIDER and TEXTBOX fire on every change and benefit most.
  Add `DEBOUNCE = n` as a universal ON_CHANGE option across all filter controls.
- [ ] **Disabled / read-only state**: No `DISABLED = ON|OFF` or `READ_ONLY = ON|OFF` option on
  any control. Power BI and Tableau both support conditionally disabling a filter based on
  another parameter. Add `DISABLED = expression` evaluated client-side against current
  parameter state.
- [ ] **Declarative `VISIBLE` expression on controls**: Controls can be hidden via
  `SET_UI_STATE(name, VISIBLE, OFF)` triggered by a button, but there is no declarative
  `VISIBLE = (@mode = 'Advanced')` expression that updates automatically when parameters
  change without an explicit button action. Add `VISIBLE = expression` as a declarative
  control-level option.

### IMAGE

- [ ] **Alt text for accessibility**: No `ALT = 'description'` option. Screen readers and PDF
  export cannot describe the image. Add `ALT` as a recommended option and flag its absence in
  lint for accessibility compliance.
- [ ] **Click action on IMAGE**: IMAGE has no `ACTIONS (ON_CLICK = ...)` clause. Power BI
  images support `ON_CLICK = OPEN_URL(...)` and Tableau dashboard images can navigate pages.
  Add `ON_CLICK` support (NAVIGATE_PAGE, OPEN_URL) to IMAGE, consistent with BUTTON actions.
- [ ] **Gallery / multi-image mode**: When SOURCE returns multiple rows, IMAGE renders only the
  first row's SRC. There is no gallery or grid layout mode for a result set of images. Add
  `MODE = SINGLE|GALLERY` with `COLUMNS = n` for grid layout.
- [ ] **Aspect ratio lock**: `WIDTH` and `HEIGHT` are independent CSS values. No
  `ASPECT_RATIO = '16/9'` option to lock the ratio while respecting available width. Add as
  an alternative to an explicit HEIGHT when one dimension is flexible.
- [ ] **Fallback / broken-image placeholder**: No `FALLBACK = 'path'` option to show when SRC
  is NULL or the URL fails to load. Add alongside the existing DEFAULT option.

### TEXT

- [ ] **Inline column value interpolation**: Dynamic TEXT requires a full SOURCE query that
  returns a pre-assembled string. There is no template syntax such as
  `CONTENT = 'Revenue: {revenue FORMAT "C0"} as of {as_of}'` where column references are
  resolved at render time. Power BI text boxes and Tableau text objects support field
  placeholders. Add `{column FORMAT '...'}` substitution within CONTENT when SOURCE is present.
- [ ] **Click / navigation action**: TEXT has no `ACTIONS` clause. A narrative block cannot
  navigate a page or fire a parameter update on click. Add `ON_CLICK` support to TEXT, or
  allow Markdown links to carry `NAVIGATE_PAGE(name)` targets in addition to external URLs.
- [ ] **Max lines / overflow control**: Long text blocks have no `MAX_LINES = n` or
  `OVERFLOW = CLIP|SCROLL|ELLIPSIS` option. Add overflow control to prevent content spilling
  past the card boundary.
- [ ] **Inline typography options**: TEXT styling requires a global STYLE token or a CREATE
  STYLE object. No inline `FONT_SIZE`, `FONT_COLOR`, or `FONT_WEIGHT` options directly in TEXT
  OPTIONS. Power BI and Tableau both expose per-text-box typography without a separate style
  definition. Add inline typography options to TEXT OPTIONS.

### CONTAINER

- [ ] **TABS per-tab icon and badge**: TABS container supports multiple panels via LAYOUT but
  has no per-tab `ICON` or `BADGE = n` (alert count) on individual tab entries. Power BI
  bookmarks-as-tabs and Retool tabs support icon and badge per tab. Add per-tab icon and badge
  support inside the TABS LAYOUT MAP.
- [ ] **TABS position**: TABS renders as a top horizontal tab strip. No `TAB_POSITION =
  TOP|LEFT|RIGHT|BOTTOM` option. Vertical left-sidebar tabs are common in Power BI and Retool.
  Add TAB_POSITION to TABS OPTIONS.
- [ ] **ACCORDION default open section**: ACCORDION has no `DEFAULT_OPEN = 'sectionName'`
  option to control which section is expanded on page load. Add to ACCORDION OPTIONS.
- [ ] **MODAL triggered from a chart data point**: MODAL is opened via
  `SET_UI_STATE(ModalName, VISIBLE, ON)` on a BUTTON. There is no ON_CLICK action on a chart
  mark that opens a detail MODAL for the selected row. Add `ON_CLICK = SHOW_MODAL(ModalName)`
  as a visual-level action alongside the other chart actions.
- [ ] **DRAWER default state**: No `DEFAULT = OPEN|CLOSED` option on DRAWER. It always starts
  closed. Add DEFAULT to DRAWER OPTIONS.
- [ ] **Container-level REFRESH**: PAGE has `REFRESH = seconds`. Individual containers do not.
  A live log or ticker in a SCROLL container cannot auto-refresh independently from the page.
  Add `REFRESH = seconds` to CONTAINER OPTIONS.
- [ ] **Collapsible BOX**: BOX is a static grouping container with no collapse toggle. DRAWER
  covers the floating/overlay case but not an inline collapsible panel. Add
  `COLLAPSIBLE = ON|OFF` and `DEFAULT = OPEN|CLOSED` to BOX.

### BUTTON

- [ ] **Semantic style variants**: BUTTON has TITLE and STYLE but no `VARIANT = PRIMARY|
  SECONDARY|GHOST|DANGER|LINK` shortcut. Authors must manually set STYLE colors per button.
  Add `VARIANT` as a theme-aware semantic option.
- [ ] **Button icon**: No `ICON = 'name'` option. Add `ICON` and `ICON_POSITION = LEFT|RIGHT`
  to BUTTON OPTIONS.
- [ ] **Disabled state expression**: No `DISABLED = expression` option. A Submit button cannot
  be conditionally greyed out until required parameters are filled. Add `DISABLED = expression`
  evaluated against current parameter state, consistent with section 5 control gaps.
- [ ] **Multiple actions per click**: BUTTON `ACTIONS` accepts one `ON_CLICK`. Chaining two
  actions (e.g., SET_PARAMETER then NAVIGATE_PAGE) requires an intermediate bookmark. Add
  multi-action support: `ON_CLICK = (SET_PARAMETER(@x, 1), NAVIGATE_PAGE(Detail))`.
- [ ] **Loading / spinner feedback**: No visual feedback while REFRESH_REPORT or RUN_SCRIPT
  executes. Add `SHOW_SPINNER = ON|OFF` to display a loading indicator on the button during
  async operations.
- [ ] **Toggle button mode**: No `MODE = TOGGLE` where the button switches between two states
  and emits different values per state. Currently requires two buttons or a CHECKBOX. Add
  `MODE = TOGGLE` with `ON_VALUE` / `OFF_VALUE` and `DEFAULT = ON|OFF` options.
- [ ] **Confirmation dialog**: No `CONFIRM = 'Are you sure?'` option to show a native prompt
  before a destructive action fires. Add as a BUTTON option for RUN_SCRIPT and irreversible
  SET_PARAMETER actions.

### PAGE

- [ ] **Mobile / responsive alternate layout**: PAGE STRUCTURE is a single CSS grid-template-
  areas string with no small-viewport variant. Power BI has a mobile layout view; Tableau has
  a device designer. Add `MOBILE_LAYOUT (STRUCTURE = '...', MAP (...))` activated below a
  `BREAKPOINT = 'px'` threshold.
- [ ] **Page background image**: PAGE STYLE accepts `BACKGROUND` for a solid color. No
  `BACKGROUND_IMAGE = 'url'` option for a watermark or branded canvas background. Power BI
  canvas supports image fill. Add `BACKGROUND_IMAGE` and `BACKGROUND_SIZE = cover|contain|auto`
  to PAGE STYLE.
- [ ] **Max width / centered layout**: No `MAX_WIDTH = 'px'` or `ALIGN_CONTENT = CENTER`
  option to constrain the page grid to a readable column width on wide displays. Add to PAGE
  LAYOUT OPTIONS.
- [ ] **Conditional page visibility expression**: PAGE has `VISIBLE = ON|OFF` as a static
  author toggle. No `VISIBLE = @role = 'admin'` dynamic expression. Add declarative
  `VISIBLE = expression` consistent with the control and container gaps above.
- [ ] **Page load actions**: No `ON_LOAD` ACTIONS clause to fire SET_PARAMETER or
  REFRESH_VISUALS when a page becomes active. Power BI page-level load events and Tableau
  sheet-load triggers allow initialization logic. Add `ON_LOAD = (action, ...)` to PAGE.
- [ ] **Page-level scroll control**: DASHBOARD pages have no `OVERFLOW = SCROLL|CLIP` option.
  Behavior when grid content exceeds the viewport is renderer-inferred. Add explicit
  page-level overflow control.
- [ ] **Page transition animation**: No `TRANSITION = NONE|FADE|SLIDE` option for page
  navigation animations. Tableau story points support transitions between views. Add as a
  PAGE- or NAVIGATION-level option.

### NAVIGATION

- [ ] **Per-page label and icon overrides**: NAVIGATION PAGES lists page names only. There is
  no per-entry `(PageName ICON = 'name' LABEL = 'Custom Label')` override. The nav label
  must match the PAGE name. Add per-entry label and icon inside the PAGES clause.
- [ ] **Badge / notification count per nav item**: No `BADGE = @count` per page entry to show
  an alert count badge. Power BI and Retool navigation menus support numeric badges. Add
  `BADGE = expression` per PAGES entry.
- [ ] **Auto-hide invisible pages**: NAVIGATION lists all pages regardless of their `VISIBLE`
  state. If a page is hidden via SET_UI_STATE it may still appear in the nav strip. Add
  `HIDE_INVISIBLE = ON|OFF` to NAVIGATION OPTIONS.
- [ ] **Nested / grouped navigation**: NAVIGATION is a flat single-level list. No grouped
  hierarchy (e.g., "Sales > Overview, Sales > Detail"). Add
  `GROUP ('Sales' = (Overview, Detail), ...)` syntax to NAVIGATION for section grouping.
- [ ] **NAVIGATION STYLE clause**: NAVIGATION has `ORIENTATION` and `PAGES` but no STYLE
  clause. Authors cannot set nav background, active-item color, or font without global theme
  tokens. Add a `STYLE (...)` clause and an `ACTIVE_STYLE (...)` clause for the selected
  item appearance.
- [ ] **External link entry in NAVIGATION**: PAGES accepts only internal page names. No way to
  add an entry that opens an external URL. Add `LINK ('Label' = OPEN_URL('https://...'))` as a
  PAGES entry variant, subject to the URL allowlist policy established for BUTTON and IMAGE.

### Missing Data / Gap Behavior

- [ ] **NULL value treatment on LINE and COMBO**: When a time series has NULL or missing values,
  Chart.js `spanGaps` and ECharts `connectNulls` control whether the line connects across the
  gap or breaks. ETL-SQL LINE and COMBO have no `NULL_HANDLING = CONNECT|GAP|ZERO` option.
  The rendered behavior is library-inferred and varies by renderer. Add `NULL_HANDLING` to
  LINE and COMBO OPTIONS and to `CUSTOM CHART` LINE layer declarations.

### Tooltip Behavior

- [ ] **Tooltip trigger mode**: ECharts supports `trigger: 'axis'` (snaps to the nearest X
  position and shows all series at that X — the "shared crosshair" style) and
  `trigger: 'item'` (shows only the hovered mark). Chart.js `interaction.mode` has `index`,
  `dataset`, `point`, `nearest`, `x`, and `y` modes. ETL-SQL TOOLTIP is static text or a
  container popover with no trigger-mode option. Add `TOOLTIP_MODE = ITEM|SHARED` to named
  chart OPTIONS, where SHARED shows all series values at the nearest category/x-position.
- [ ] **Tooltip position control**: ECharts `tooltip.position` accepts `'top'`, `'bottom'`,
  `'left'`, `'right'`, `'inside'`, or a fixed coordinate. Chart.js supports `'nearest'` and
  `'average'` positioners. ETL-SQL has no control over where the tooltip appears relative to
  the cursor or mark. Add `TOOLTIP_POSITION = AUTO|TOP|BOTTOM|LEFT|RIGHT|CURSOR` to named
  chart OPTIONS.
- [ ] **Axis pointer / crosshair lines**: ECharts `axisPointer` draws a crosshair (vertical
  and/or horizontal line) that follows the cursor across the plot area, independent of the
  tooltip. No equivalent exists in any ETL-SQL chart. Add `CROSSHAIR = ON|OFF` with
  `CROSSHAIR_AXIS = X|Y|BOTH` and optional `CROSSHAIR_COLOR` / `CROSSHAIR_DASH` styling.
- [ ] **Linked tooltip / crosshair across charts**: ECharts supports linking the axisPointer
  across multiple charts in the same page so hovering one highlights the same x-position in
  all linked charts. No equivalent in ETL-SQL. Add `LINK_TOOLTIP = groupName` so charts
  sharing a group name synchronize their hover position.

### Data Point and Coordinate Annotations

- [ ] **Data point annotations (markPoint equivalent)**: ECharts `markPoint` pins a custom
  marker to a specific data coordinate (or the min/max of a series) with a label and custom
  symbol. This is distinct from reference lines (horizontal bands) — these markers are anchored
  to data values. Add `ANNOTATIONS (POINT (SERIES = 'seriesName', TYPE = MAX|MIN|COORD(x, y),
  LABEL = '...', SYMBOL = 'pin|arrow|circle'))` to named chart OVERLAYS and CUSTOM CHART.

### Animation

- [ ] **Entry animation controls**: ECharts and Chart.js both expose animation duration, easing
  function, and per-series delay for entry animations. ETL-SQL charts have no animation
  options. Add `ANIMATION = ON|OFF`, `ANIMATION_DURATION = ms`, and
  `ANIMATION_EASING = LINEAR|EASE_IN|EASE_OUT|ELASTIC|BOUNCE` to named chart OPTIONS. Default
  `OFF` for server-rendered / PDF contexts; `ON` for interactive dashboard mode.
- [ ] **Update animation**: Separate from entry animation, Chart.js and ECharts both have
  controls for how charts animate when data changes (parameter-driven refresh). No ETL-SQL
  option. Add `UPDATE_ANIMATION = ON|OFF` to distinguish initial-load animation from
  parameter-change re-render behavior.

### Area Fill

- [ ] **Area fill baseline (fill to arbitrary Y value)**: Chart.js `fill: { value: n }` fills
  the area between the line and a specific Y value (not necessarily zero). ECharts
  `areaStyle.origin` has similar control. ETL-SQL `CUSTOM CHART` AREA layers have `STACK` but
  no "fill from specific Y baseline" option. Named LINE/AREA have no equivalent. Add
  `AREA_BASELINE = ZERO|n` to LINE OPTIONS (when `AREA = ON`) and to `CUSTOM CHART` AREA
  layer declarations.

### Series Hover and Emphasis

- [ ] **Hover emphasis mode**: ECharts `emphasis.focus` controls what happens when a series is
  hovered: `'self'` highlights only the hovered element, `'series'` highlights the whole
  series and dims others, `'none'` disables emphasis. No equivalent in ETL-SQL. Add
  `HOVER_FOCUS = NONE|SELF|SERIES` to named chart OPTIONS and to `CUSTOM CHART` layer
  declarations. This is the "dim other series on hover" behavior users expect from Echarts.

### Large Dataset Rendering

- [ ] **Time series downsampling**: ECharts `sampling: 'lttb'|'average'|'max'|'min'|'sum'`
  renders a visual approximation of a large series without loading every point, using the
  Largest Triangle Three Buckets algorithm or statistical aggregation. No equivalent in ETL-SQL
  LINE or SCATTER. Add `SAMPLING = NONE|LTTB|AVERAGE|MAX|MIN` to LINE and SCATTER OPTIONS for
  high-cardinality time series (e.g., sensor data at second granularity). The actual
  downsampling occurs at render time, not in the SQL query.
- [ ] **Progressive / chunked rendering**: ECharts `progressive` renders large series in chunks
  to avoid blocking the browser main thread. No ETL-SQL equivalent. Add
  `PROGRESSIVE = ON|OFF` with `PROGRESSIVE_CHUNK = n` (rows per frame) to named LINE and
  SCATTER for datasets exceeding a configurable threshold.

### Chart-Level Export and Toolbox

- [ ] **Save-as-image button**: ECharts `toolbox.feature.saveAsImage` adds a camera icon that
  downloads the current chart as a PNG. No per-chart export button in ETL-SQL — only
  page-level export through the Portal. Add `SHOW_EXPORT = ON|OFF` to named chart OPTIONS to
  render a small download icon on the chart's title bar, using the existing report-runtime
  canvas-capture path.
- [ ] **Data table view toggle**: ECharts `toolbox.feature.dataView` shows the underlying data
  as a table when clicked. No equivalent in ETL-SQL. Add `SHOW_DATA_VIEW = ON|OFF` to display
  a toggle that switches the visual between chart and tabular view of its SOURCE data.

### Linked Zoom

- [ ] **Synchronized zoom across charts**: ETL-SQL `ZOOM_SLIDER = ON|OFF` adds an independent
  slider per chart. ECharts supports linking dataZoom across multiple charts in one group so
  zooming one scrolls all. Add `ZOOM_GROUP = groupName` so charts sharing a group name
  synchronize their X-axis zoom range.

### Axis Tick Label Formatter

- [ ] **Axis tick label format string**: ECharts `axisLabel.formatter` and Chart.js
  `ticks.callback` allow custom formatting of axis tick labels independent of data labels and
  tooltips (e.g., show `"1K"` instead of `"1000"`, or `"Jan"` instead of `"2026-01-01"`). 
  ETL-SQL has no axis tick label formatter. Add `X_AXIS (TICK_FORMAT = 'format-string')` and
  `Y_AXIS (TICK_FORMAT = 'format-string')` to named charts, using the same `.NET format string`
  convention as `DATA_LABELS`.
- [ ] **Time axis unit and display format**: Chart.js `scales.x.time.unit` (`'month'`,
  `'quarter'`, `'year'`) and `time.displayFormats` let authors control which temporal unit
  the axis ticks represent and how each unit is labeled (e.g., `'MMM yyyy'` for months,
  `"'Q'Q yyyy"` for quarters). ETL-SQL time-series axes infer the unit from data density with
  no override. Add `X_AXIS (TIME_UNIT = AUTO|DAY|WEEK|MONTH|QUARTER|YEAR, TICK_FORMAT =
  'format')` to LINE and COMBO for explicit time axis control.

### Bar Minimum Height

- [ ] **Minimum bar height**: ECharts `barMinHeight` and Chart.js equivalent ensure that very
  small values still render a visible bar pixel rather than disappearing. No ETL-SQL equivalent.
  Add `BAR_MIN_HEIGHT = n` (pixels) to BAR, HBAR, COMBO, and WATERFALL OPTIONS so sub-pixel
  values remain visible.

### Per-Segment Conditional Line Styling

- [ ] **Per-segment line style (Chart.js `segment` callback)**: Chart.js `segment` applies
  different `borderColor`, `borderDash`, or `backgroundColor` to individual line segments
  based on the data values at each end of the segment. The canonical use case is "dashed
  line after today's date for forecasted data" without splitting into two series. ETL-SQL
  has `FORMATTING (WHEN ... THEN color)` on marks but no per-segment styling on LINE series.
  Add `SEGMENT_STYLE (WHEN condition THEN LINE_DASH = DASHED|DOTTED, COLOR = '#hex')` to
  LINE and COMBO OPTIONS for condition-driven per-segment overrides.

### New Chart Types (Evaluate)

- [ ] **Polar coordinate bar / line**: ECharts supports BAR and LINE on a polar coordinate
  system (concentric rings or spiral). The "bull's-eye" bar chart (bars radiating from center)
  is a distinct visualization not achievable via RADAR or CUSTOM CHART without significant
  effort. Evaluate as a `POLAR` visual type or as a `CUSTOM CHART (COORDINATE = POLAR)`
  extension.
- [ ] **Calendar heatmap**: ECharts `calendar` coordinate system maps values to calendar cells
  (the "GitHub contribution graph" pattern: day of week × week of year, colored by value).
  Distinct from ETL-SQL HEATMAP which is categorical X × Y. Evaluate as
  `HEATMAP (MODE = CALENDAR)` with `DATE = column, VALUE = column` mappings and year/month
  axis auto-generation.
- [ ] **Streamgraph / themeriver**: ECharts `themeRiver` is a stacked area chart where the
  band width encodes value and bands flow symmetrically around a central axis (used for topic
  popularity over time, election results, etc.). Not achievable with ETL-SQL LINE/AREA STACKED.
  Evaluate as a new visual type `STREAMGRAPH`.
- [ ] **Parallel coordinates chart**: ECharts `parallel` renders multiple numeric axes side by
  side with polylines connecting each observation's values across axes. Used for multivariate
  data exploration. No ETL-SQL equivalent. Evaluate as a new `PARALLEL` visual type.

---

## Kitchen Sink File Inventory

The files in `samples/10_Kitchen_Sinks/` serve as regression tests when rendering changes are made.
Each sink should exercise every supported option for its visual type. The table below tracks what is
currently covered, what is missing from the file, and which TODO items (marked above) are the prerequisite
before a gap can be added to the sink.

The rule: **finish the feature first, then add it to the sink**. A `[ ]` gap with no prerequisite
means the feature already exists in the engine and the sink just needs a new visual block.

### 01_BAR.rptsql

| Gap | Prerequisite TODO |
| :--- | :--- |
| `STACKED = 100PCT` | ✅ Axis Controls → 100% normalized stacking |
| `LEGEND_TITLE`, `LEGEND_COLUMNS`, `LEGEND_ORIENTATION`, `LEGEND_REVERSE` | ✅ Legend Controls (all shipped) |
| `LEGEND_POSITION = LEFT`, `LEGEND_POSITION = INSIDE` + `LEGEND_ANCHOR` | ✅ Legend Controls (shipped) |
| `LEGEND_FONT_SIZE`, `LEGEND_FONT_COLOR`, `LEGEND_FONT_WEIGHT` | ✅ Legend Controls (shipped) |
| `LEGEND = OFF` | ✅ Legend Controls (shipped) |
| `GRID_LINES = ON/OFF`, `GRID_LINE_COLOR`, `GRID_LINE_DASH`, `GRID_LINE_WIDTH` | ✅ Gridline and Line Styling (shipped) |
| `ZERO_LINE = ON/OFF`, `ZERO_LINE_COLOR`, `ZERO_LINE_DASH` | ✅ Gridline and Line Styling (shipped) |
| `MINOR_GRID_LINES = ON/OFF` | ✅ Gridline and Line Styling (shipped) |
| `BAND_SIZE = 0.5` | ✅ Bar and Area Layout (shipped) |
| `SERIES_GAP = 0.0..1.0` | ✅ Bar and Area Layout (shipped) |
| `AXIS_SORT = VALUE_ASC`, `ALPHA` | ✅ Axis Controls (shipped) |
| `DATA_LABELS` with OUTSIDE / INSIDE_TOP / CENTER positions | ✅ Series and Data Labels |
| `LABEL_BACKGROUND`, `LABEL_BORDER` on DATA_LABELS | ✅ Series and Data Labels (shipped) |
| `RUNNING_TOTAL` overlay | ✅ Analytical Overlays (shipped) |
| `PERCENT_OF_TOTAL` overlay | ✅ Analytical Overlays (shipped) |
| `REFERENCE_LINE`, `REFERENCE_BAND` overlays | [ ] Analytical Overlays (pending) |
| `BAR_MIN_HEIGHT = n` | [ ] Bar Minimum Height (pending) |
| `FORMATTING (WHEN … THEN color)` conditional | ✅ already on BAR — just not in sink |
| `PLOT_BACKGROUND`, `PLOT_BORDER` | [ ] Plot and Panel Styling (pending) |

### 02_HBAR.rptsql

| Gap | Prerequisite TODO |
| :--- | :--- |
| `STACKED = 100PCT` | ✅ shipped |
| Full legend suite | ✅ shipped |
| `AXIS_SORT` variants | ✅ shipped |
| `BAND_SIZE`, `SERIES_GAP` | ✅ shipped |
| `GRID_LINES`, `ZERO_LINE`, `MINOR_GRID_LINES` | ✅ shipped |
| Remaining overlay types (AVERAGE, MOVING_AVG, LINEAR, POLYNOMIAL, RUNNING_TOTAL, PERCENT_OF_TOTAL) | ✅ shipped |
| `COLORS` named palette | ✅ — just not exercised |
| `DATA_LABELS` position variants | ✅ shipped |
| `BAR_MIN_HEIGHT` | [ ] pending |

### 03_LINE.rptsql

| Gap | Prerequisite TODO |
| :--- | :--- |
| `LINE_WIDTH = n` | ✅ Marker and Line Geometry (shipped) |
| `SYMBOL_SHAPE` (CIRCLE/SQUARE/TRIANGLE/DIAMOND) | ✅ Marker and Line Geometry (shipped) |
| `SYMBOL_STROKE_COLOR`, `SYMBOL_STROKE_WIDTH` | ✅ Marker and Line Geometry (shipped) |
| Full legend suite | ✅ shipped |
| `GRID_LINES`, `ZERO_LINE`, `MINOR_GRID_LINES` | ✅ shipped |
| `AXIS_SORT` | ✅ shipped |
| `DATA_LABELS` | ✅ shipped |
| `SERIES_LABELS = ON` (end-of-line labels) | ✅ Series and Data Labels (shipped) |
| EXPONENTIAL, LOGARITHMIC, POWER overlay types | ✅ shipped (in overlays sink but not line sink) |
| `RUNNING_TOTAL`, `PERCENT_OF_TOTAL` overlays | ✅ shipped |
| `INTERPOLATION = STEP_BEFORE|STEP_AFTER` | [ ] Marker and Line Geometry (pending) |
| `LINE_DASH = SOLID|DASHED|DOTTED` on series | [ ] Marker and Line Geometry (pending) |
| `NULL_HANDLING = CONNECT|GAP|ZERO` | [ ] Missing Data / Gap Behavior (pending) |
| `AREA = ON` + `AREA_BASELINE` | [ ] Area Fill (pending) |

### 04_SCATTER.rptsql

| Gap | Prerequisite TODO |
| :--- | :--- |
| `JITTER = ON/OFF`, `JITTER_WIDTH`, `JITTER_HEIGHT` | ✅ SCATTER / BUBBLE (shipped) |
| `ERROR_LOW` / `ERROR_HIGH` mappings + `ERROR_BAR_STYLE` | ✅ Analytical Overlays (shipped) |
| `SYMBOL_SHAPE`, `SYMBOL_STROKE_COLOR`, `SYMBOL_STROKE_WIDTH` | ✅ Marker and Line Geometry (shipped) |
| `LOG = ON` / `Y_AXIS (SCALE = LOG)` | ✅ SCATTER / BUBBLE (shipped) |
| `SIZE_RANGE (min, max)`, `MIN_BUBBLE_SIZE`, `MAX_BUBBLE_SIZE` | ✅ SCATTER / BUBBLE (shipped) |
| Full legend suite | ✅ shipped |
| `GRID_LINES`, `ZERO_LINE` | ✅ shipped |
| EXPONENTIAL, LOGARITHMIC, POWER overlays | ✅ shipped |
| `OVERLAYS` clause (currently uses `SHOW_REGRESSION`) | [ ] Cross-Cutting (pending: OVERLAYS on SCATTER) |
| `FORMATTING (WHEN … THEN color)` | [ ] Cross-Cutting (pending) |
| `SYMBOL_SIZE = n` | [ ] Named Chart Marks (pending) |

### 05_PIE.rptsql

| Gap | Prerequisite TODO |
| :--- | :--- |
| `SORT = VALUE_DESC / VALUE_ASC / ALPHA / SOURCE` | ✅ PIE / DONUT (shipped) |
| `MIN_SLICE_PCT = n` + `OTHER_LABEL = 'Other'` | ✅ PIE / DONUT (shipped) |
| `EXPLODE = 'SliceName'` | ✅ PIE / DONUT (shipped) |
| `EXPLODE_ALL = ON` + `EXPLODE_DISTANCE = 0.1` | ✅ PIE / DONUT (shipped) |
| `START_ANGLE = 90` | ✅ PIE / DONUT (shipped) |
| `SLICE_BORDER_COLOR`, `SLICE_BORDER_WIDTH` | ✅ PIE / DONUT (shipped) |
| Full legend suite (TITLE, COLUMNS, INSIDE anchor) | ✅ shipped |
| `DATA_LABELS` with position / leader line | ✅ shipped / [ ] LEADER_LINE pending |

### 06_DONUT.rptsql

| Gap | Prerequisite TODO |
| :--- | :--- |
| Same PIE-specific options (all shipped) | ✅ |
| Full legend suite | ✅ shipped |
| `LEGEND_POSITION = INSIDE` + anchor | ✅ shipped |

### 07_COMBO.rptsql

| Gap | Prerequisite TODO |
| :--- | :--- |
| `Y2_AXIS` dual-axis series | ✅ (already in COMBO) — sink needs a second visual with it |
| `LINE_WIDTH` on LINE series in COMBO | ✅ Marker and Line Geometry (shipped) |
| Full legend suite | ✅ shipped |
| `GRID_LINES`, `ZERO_LINE` | ✅ shipped |
| Overlays on COMBO | ✅ (overlays work on COMBO) |
| `DATA_LABELS` | ✅ shipped |
| `STACKED` bars in combo | ✅ shipped |
| `SYNC_AXES = ON` | [ ] Dual-Axis (pending) |
| `Y_MARK = BAR|LINE|AREA` per axis | [ ] Named Chart Marks (pending) |

### 09_BOXPLOT.rptsql

| Gap | Prerequisite TODO |
| :--- | :--- |
| `ORIENTATION = HORIZONTAL` | ✅ (ORIENTATION exists for BOXPLOT) — sink missing it |
| Multi-series (SERIES mapping) | ✅ — sink just needs an example |
| Full `COLORS` palette (multi-category) | ✅ — sink needs more categories |
| Full legend suite | ✅ shipped |
| `GRID_LINES`, `ZERO_LINE` | ✅ shipped |
| `BAND_SIZE` | ✅ shipped |
| `NOTCHED = ON` | [ ] BOXPLOT (pending) |
| `SHOW_MEAN = ON` | [ ] BOXPLOT (pending) |

### 11_HEATMAP.rptsql

| Gap | Prerequisite TODO |
| :--- | :--- |
| Diverging 3-color: `COLOR_LOW`, `COLOR_MID`, `COLOR_HIGH` + `MIDPOINT` | ✅ HEATMAP (shipped) |
| `NULL_COLOR = '#rrggbb'` | ✅ HEATMAP (shipped) |
| `CELL_BORDER = ON/OFF`, `CELL_BORDER_COLOR`, `CELL_BORDER_WIDTH` | ✅ HEATMAP (shipped) |
| `X_SORT`, `Y_SORT` | ✅ HEATMAP (shipped) |
| Full legend suite | ✅ shipped |

### 12_FUNNEL.rptsql

| Gap | Prerequisite TODO |
| :--- | :--- |
| `ORIENTATION = HORIZONTAL` | ✅ (exists) — sink missing it |
| `SORT = SOURCE|VALUE_ASC|VALUE_DESC` | [ ] FUNNEL (pending) |
| `PERCENT_MODE = STEP|TOTAL` | [ ] FUNNEL (pending) |
| `FUNNEL_SHAPE = FUNNEL|PYRAMID` | [ ] FUNNEL (pending) |
| Full legend suite | ✅ shipped |

### 13_WATERFALL.rptsql

| Gap | Prerequisite TODO |
| :--- | :--- |
| `TOTAL = is_total_column` (boolean flag mapping) | ✅ WATERFALL (shipped) |
| `SUBTOTAL` role mapping | ✅ WATERFALL (shipped) |
| `"SUBTOTAL"` string in TOTAL column | ✅ WATERFALL (shipped) |
| `CONNECTOR_LINES = ON/OFF`, `CONNECTOR_LINE_COLOR`, `CONNECTOR_LINE_WIDTH` | ✅ WATERFALL (shipped) |
| `COLOR_TOTAL`, `COLOR_SUBTOTAL`, `COLOR_UP`, `COLOR_DOWN` | ✅ WATERFALL (shipped) |
| `ORIENTATION = HORIZONTAL` | ✅ WATERFALL (shipped) |
| `BAR_MIN_HEIGHT` | [ ] Bar Minimum Height (pending) |

### 31_OVERLAYS_ADVANCED.rptsql

| Gap | Prerequisite TODO |
| :--- | :--- |
| `RUNNING_TOTAL` overlay | ✅ Analytical Overlays (shipped) |
| `PERCENT_OF_TOTAL` overlay | ✅ Analytical Overlays (shipped) |
| `REFERENCE_LINE (VALUE = n, LABEL = '...', STYLE = DASHED)` | [ ] Analytical Overlays (pending) |
| `REFERENCE_BAND (LOW = n, HIGH = n, COLOR, LABEL)` | [ ] Analytical Overlays (pending) |
| `FORECAST` overlay | [ ] Analytical Overlays (pending) |

### 32_BUBBLE.rptsql

| Gap | Prerequisite TODO |
| :--- | :--- |
| `MIN_BUBBLE_SIZE`, `MAX_BUBBLE_SIZE` | ✅ SCATTER / BUBBLE (shipped) |
| `SIZE_RANGE (min, max)` | ✅ SCATTER / BUBBLE (shipped) |
| `LOG = ON` (log scale) | ✅ SCATTER / BUBBLE (shipped) |
| Full legend suite | ✅ shipped |
| `GRID_LINES`, `ZERO_LINE` | ✅ shipped |
| `SYMBOL_SIZE = n` | [ ] Named Chart Marks (pending) |

### Priority Order for Sink Updates

All items below are **already shipped** — no new feature work needed before updating the sink file.
Work through them in this order:

1. **13_WATERFALL.rptsql** — Full rewrite needed: TOTAL flag, SUBTOTAL mapping, SUBTOTAL string, connector lines, horizontal orientation, new color tokens.
2. **11_HEATMAP.rptsql** — Add diverging colors, NULL_COLOR, CELL_BORDER, X_SORT/Y_SORT visuals.
3. **05_PIE.rptsql** — Add SORT, MIN_SLICE_PCT, EXPLODE variants, START_ANGLE, SLICE_BORDER visuals.
4. **06_DONUT.rptsql** — Same pie-specific additions.
5. **04_SCATTER.rptsql** — Add JITTER, ERROR bars, SIZE_RANGE, SYMBOL options.
6. **32_BUBBLE.rptsql** — Add MIN/MAX_BUBBLE_SIZE, SIZE_RANGE, LOG scale.
7. **09_BOXPLOT.rptsql** — Add ORIENTATION=HORIZONTAL, multi-series.
8. **03_LINE.rptsql** — Add LINE_WIDTH, SYMBOL options, EXPONENTIAL/LOG/POWER overlays, RUNNING_TOTAL, PERCENT_OF_TOTAL.
9. **07_COMBO.rptsql** — Add Y2_AXIS example, LINE_WIDTH, overlays, STACKED bars.
10. **01_BAR.rptsql** — Add STACKED=100PCT, full legend suite, GRID_LINES, ZERO_LINE.
11. **02_HBAR.rptsql** — Same axis/legend additions as BAR.
12. **31_OVERLAYS_ADVANCED.rptsql** — Add RUNNING_TOTAL, PERCENT_OF_TOTAL.
13. **12_FUNNEL.rptsql** — Add ORIENTATION=HORIZONTAL (and pending SORT/PERCENT_MODE/PYRAMID when implemented).

