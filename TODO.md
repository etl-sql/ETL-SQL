# ETL-SQL Development Roadmap
## Bugs
- [x] **VS Code when launch doesn't launch**  Click the launch button starts up the serve but doesn't open chrome automatically like it used to

---

## Code Review — Fresh Eyes Audit (2026-05-13)

Review across 8 dimensions: Security, Single Responsibility, Documentation, Lint/User Warnings, Logging, Testing, Performance, Modern Standards.
Findings are independent of the roadmap phases below. Fix before v1 launch.

---

### SECURITY

- [x] **`CreateConnectionStatementHandler.cs:166–189` — Remove `Console.Error.WriteLine` debug traces**
  Five `Console.Error.WriteLine("[TRACE] CREATE CONNECTION: ...")` calls are left in the preview-generation path. These bypass the structured logging system and can leak schema column names and row counts to stderr in production. Replace with `_logger.Debug(...)`.

- [x] **`ExecutePushdownStatementHandler.cs:67–68` — SQL prefix stripping via `Replace()` is fragile**
  When a connection is named `"A"`, `sqlToExecute.Replace(" A.", " ", ...)` would corrupt any table reference that happens to contain ` A.` in unrelated context (e.g., `TABLE_A.column`). Use a regex with word-boundary anchors or strip only at token boundaries, not substring positions.

- [x] **`AuthController.cs:92,120` — `int.Parse(User.FindFirstValue(...)!)` throws on malformed claims**
  Called in two controller actions. If the `NameIdentifier` claim is absent or non-numeric (JWT tampering, misconfigured IdP), this throws an unhandled `FormatException` resulting in a 500. Replace with `int.TryParse` and return 401 on failure.

- [x] **`Program.cs:72–76` — JWT zero-byte placeholder starts the app in an insecure state**
  When `JwtSettings:Secret` is missing, the app initialises with 32 zero bytes so the DI container can start. The `JwtSecretValidationService` shuts it down later, but there is a window where the app can accept and validate tokens signed with the all-zero key. Move the nil-check to `WebApplicationBuilder` startup instead of a hosted-service.

- [x] **`SecurityService.cs:24` — `GetMachineKey()` entropy is weak in containerised environments**
  `MachineName + UserName` is the sole entropy source. Container orchestrators (Docker, Kubernetes) commonly assign predictable hostnames (e.g., `pod-abc123`) and run everything as the same user. This weakens the "machine-unique" property. Document the limitation and recommend overriding with a configured secret in production deployments.

- [x] **`SecurityService.cs` multiple bare `catch {}` blocks**
  Several exception paths in `ResolvePathSymlinks`, `CheckTestEnvironment`, and `IsSensitivePath` swallow exceptions entirely. While intentional in some cases, none log even at DEBUG level, making it impossible to diagnose security check failures in field deployments.

---

### SINGLE RESPONSIBILITY

- [x] **`CreateConnectionStatementHandler.cs` — Execute() has 10+ responsibilities**
  One method handles: expression evaluation, decryption, path resolution, connector lookup, existing-connection teardown, connection string building, column discovery, preview data generation, and result materialisation. Extract preview generation into a `ConnectionPreviewBuilder` helper and the teardown/replace logic into a separate method.

- [x] **`CreateDatasetStatementHandler.cs` — Mixes execution, persistence, and orchestration**
  Execute() evaluates the query, writes Parquet, updates the dataset registry, and creates a portal refresh job. The registry/job steps belong in a post-execution hook or coordinator, not in the handler.

- [x] **`ExpressionEvaluator.cs` — `ResolveIdentifierFallback` mixes resolution strategy with ambiguity detection**
  The fallback method classifies columns into strong/weak match buckets, detects cross-qualifier ambiguity, and throws execution exceptions — all in one 60-line method. Extract a `ColumnMatcher` class with a clear `MatchResult` return type.

- [x] **`Linter.cs:50–116` — `DiscoverScriptMetadata` switches over 10+ statement types inline**
  Metadata discovery logic for each statement type should delegate to statement-specific visitor methods, not accumulate in one giant switch. Use the visitor pattern or `IMetadataDiscoverer` per statement type.

---

### DOCUMENTATION

- [x] **`ExecutePushdownStatementHandler.cs:57–68` — No explanation of why prefix stripping exists**
  The comment says *what* it does but not *why* users write fully-qualified names in pushdown blocks or what connectors require stripping. Document the invariant: which connectors pass the prefix through vs. which reject it.

- [x] **`SecurityService.cs:121–137` — Extension and directory block-lists have no rationale per entry**
  `.pfx` is blocked but `.cer` is not; `bin` and `obj` are in `RestrictedDirectories` but `packages` is not. Add a comment per group explaining the security rationale, or link to a policy document.

- [x] **`SecurityService.cs:405–441` — `CheckRunawayProtection` flag semantics are opaque**
  `allowLargeCount` and `allowDeepRecursion` are separate parameters, but it is unclear when each is appropriate, whether they stack, and how they interact with safe zones. Add XML doc comments with a usage example.

- [x] **`CreateDatasetStatementHandler.cs:74–96` — No comment on failure ordering in multi-step persistence**
  The handler writes Parquet, then updates the registry, then creates a job. If registry update succeeds but job creation fails, the dataset is registered but never refreshed. Document the expected error-handling posture (rollback vs. partial success).

- [x] **`ExpressionEvaluator.cs:105` — "BUG FIX" comment gives no context**
  States *what* was fixed but not *what broke*, *which version*, or *how to verify* it is still correct. Replace with a link to the original issue or a minimal reproducer description.

- [x] **`ConnectionStringBuilder.cs` — No public type documentation**
  Public entry point for connector configuration has no XML docs. Callers cannot tell which keys are required, what formats are accepted, or what validation occurs before string construction.

---

### LINT / USER-FACING WARNINGS

- [x] **`AlterConnectionStatementHandler.cs` — Silent `${VAR}` substitution failure**
  `Interpolate()` leaves unresolved `${VAR}` placeholders in the connection string as-is without warning. A user who misspells a variable name gets a broken connection with no diagnostic. Emit a `LintWarning` or runtime `ExecutionWarning` for each unresolved placeholder.

- [x] **`BulkInsertStatementHandler.cs:107–112` — Bad BATCHSIZE/MAXERRORS values throw instead of lint**
  If a user writes `BATCHSIZE = 'yes'`, an uncaught `FormatException` escapes. The parser already has the option value; validate it at parse or lint time and produce a `TypeMismatch` lint error.

- [x] **`ExecutePushdownStatementHandler.cs:27–33` — Empty pushdown body not linted**
  An empty `EXECUTE PUSHDOWN { }` block is logged at debug but generates no warning to the user. Add a lint rule for empty pushdown bodies.

- [x] **`FlatFileDataSource` — Delimiter/row-delimiter conflict not detected**
  No validation that `DELIMITER` and `ROW_DELIMITER` are distinct. When they match, the file is unparseable but the error only surfaces at read time as a confusing row-count mismatch. Add a lint rule in the connector options validator.

---

### LOGGING

- [x] **`CreateConnectionStatementHandler.cs:114–121` — Connection string build failure loses connector context**
  The exception from `BuildConnectionString()` is re-thrown without logging the connector type, connection name, or options that were in use. Log at ERROR before rethrowing.

- [x] **`BulkInsertStatementHandler.cs:162–195` — Row-by-row fallback logs only a summary**
  On batch failure, only `"Batch write failed: {ex.Message}"` is logged. Include: batch index, row range, first-row preview (sanitised), and target table name to aid production triage.

- [x] **`ExecutePushdownStatementHandler.cs:55–63` — Prefix stripping is silent**
  The connection prefix replacement transforms the SQL sent to the provider without any log entry. If the transformation corrupts a query, nothing in the logs shows the original vs. rewritten SQL. Log at DEBUG with before/after.

- [x] **`Orchestrator/Execution/ProcessJobExecutor.cs:124–125` — stderr always logged at Warning**
  Some CLIs write progress info to stderr; logging all stderr at Warning pollutes the warning stream. Log at Info unless the process also exited non-zero, then escalate to Error.

- [x] **`SecurityService.cs:731–753` — `IsWithinSafeZone` is entirely silent on rejection**
  When a path is denied, the caller may not log it (e.g., `CheckRunawayProtection` only calls `IsWithinSafeZone` for a boolean). Add a DEBUG-level log line each time a path is checked and rejected for security auditability.

---

### TESTING

- [x] **`BulkInsertStatementHandler` — Column mapping edge cases untested**
  The positional column mapper assumes source column count ≥ mapping list length. Missing tests for: source has fewer columns than the mapping, `NULL` values in mapped positions, and non-contiguous column indices.

- [x] **`SecurityService.ValidatePath` — UNC paths and mixed separators not tested**
  Current tests cover Linux temp paths and Windows drive paths. Missing: `\\server\share` UNC paths, paths with mixed `/` and `\`, and paths where casing matters (Linux) vs. does not (Windows). The static `PathComparison` field diverges per OS — test both.

- [x] **`ExpressionEvaluator.ResolveIdentifierFallback` — Ambiguity detection edge cases untested**
  No visible test coverage for: multiple weak matches with different qualifiers, partial match where row contains both `ID` and `#A.ID`, nested three-part qualifiers (`Schema.Table.Column`), and the `belongsToAnother` branch (line 129).

- [x] **`ExecutePushdownStatementHandler` — Prefix stripping correctness untested**
  No test for: connection name that is a prefix of a table name (connection `"A"` with table `"ARCHIVE"`), schema-qualified prefix in a multi-join query, multiple prefix occurrences in one SQL string.

- [x] **`AuthController` — Auth edge cases untested**
  No visible tests for: login with `IsActive = false`, login with correct password on a locked account, or rapid repeat login attempts that should trigger lockout. These paths exist in the controller but have no corresponding integration tests.

---

### PERFORMANCE

- [x] **`ExpressionEvaluator.ResolveIdentifierFallback` — O(N²) column lookup**
  The method scans all column names in an outer loop (`foreach var k in allNames`), then inside the loop calls `qualifiedSuffixes.Any(other => ...)` — a second O(N) scan. For wide result sets (100+ columns from multi-table joins), this is O(N²) per identifier resolution. Build a lookup index keyed by `baseName` at `Row` construction time.

- [x] **`CreateConnectionStatementHandler.cs:178` — `ReadBatches(10).Take(1)` batch size should be minimal**
  `ReadBatches(batchSize: 10)` then `Take(1)` is fine now (10 rows), but the comment and parameter name are inconsistent — the intent is "preview rows", not "batch count". Rename the parameter and add a comment so a future refactor does not inadvertently pass a large batch size here.

- [x] **`BulkInsertStatementHandler` — Row-by-row error fallback is O(N) write operations**
  When a batch write fails, the fallback tries each row individually using `WriteBatches()` per row. For an insert of 100 000 rows with a high error rate, this degrades to 100 000 individual write calls. Consider a binary-search bisect strategy (write half-batch, isolate failing half) or accumulate good rows and write in a single pass after error rows are identified.

- [x] **`CreateDatasetStatementHandler` — TTL `ParseDuration` called on every execution**
  `IsFreshEnough()` calls `ParseDuration(stmt.Ttl)` every time a dataset is evaluated. The parsed duration value should be cached in `DatasetMetadata` at registration time.

---

### MODERN STANDARDS

- [x] **`AuthController.cs:92,120` — Duplicate `int.Parse(User.FindFirstValue(...)!)` pattern**
  This two-liner appears in two actions. Extract a private `GetCurrentUserId()` helper that returns `int?` using `TryParse`, and handle the null case uniformly (return 401).

- [x] **`AlterConnectionStatementHandler.cs:63` — Hardcoded file-connector name array**
  `new[] { "FLATFILE", "CSV", "JSON", "XML", "EXCEL", ... }` must be manually updated whenever a new file connector is added. Extract to a `FileConnectorNames` constant set in the connector registry so the handler does not need to know connector identities.

- [x] **`AzureBlobConnector.cs` — `Task<IEnumerable<string>>` should be `IAsyncEnumerable<string>`**
  Listing blobs over a network is inherently streaming. Returning `Task<IEnumerable<string>>` buffers all results in memory before returning. Changing to `IAsyncEnumerable<string>` allows callers to process blobs incrementally.

- [x] **`ExpressionEvaluator.cs` — `qualifiedSuffixes.Where(...).ToHashSet()` allocated inside hot path**
  Line 110–111 allocates a new `HashSet` on every call to `ResolveIdentifierFallback`. Move the qualified-suffix index to the `Row` class (computed once on row construction) rather than per identifier resolution.

- [x] **`SecurityService.cs:5` — `Regex` fields should be `[GeneratedRegex]` or verified static**
  `ConnRegex`, `PasswordOptionRegex`, and `EncRegex` are `static readonly` (good) but declared without `[GeneratedRegex]`. In .NET 7+ on hot paths, `[GeneratedRegex]` produces better compiled output. Low-priority but worth adopting for the security-critical scrubbing patterns.

---

## Up Next
- [ ] **Reporting and portal language/feature streamlining**  Work this before launch as one cohesive pass. Goal: make Report-SQL feel like normal ETL-SQL, make portal administration feel like SQL DDL/admin commands, and add the missing baseline BI portal behaviors while breaking syntax is still cheap.

### Phase 0 — Lock the mental model and canonical syntax
- [ ] Define the report object buckets and use them consistently everywhere:
    - `SOURCE` = data-producing query, table, or dataset reference.
    - `MAPPINGS` = visual data roles.
    - `LAYOUT` = page/container placement, structure, maps, gaps, responsive behavior.
    - `STYLE` = presentation/theme choices.
    - `OPTIONS` = renderer-specific settings only.
    - `ACTIONS` = outbound events emitted by visuals, controls, and buttons.
    - `INTERACTIONS` = cross-visual selection/filter/highlight behavior.
    - Portal commands = administrative DDL/operations such as users, folders, grants, publishing, subscriptions, and refresh jobs.
- [ ] Decide the remaining final grammar contract in `Docs/Reference/Grammar.md` before implementation. Since the product has not gone live, prefer one canonical syntax over compatibility aliases.
- [x] Page syntax decision: canonical syntax is `CREATE PAGE <name> AS (...)`; remove the old `CREATE PAGE <name> AS LAYOUT (...)` form from docs, help, samples, and tests before launch unless a deliberate compatibility decision is made.
- [ ] Update `Docs/Report_SQL_Guide.md`, editor help, samples, and tests after the remaining grammar direction is settled.

### Phase 1 — Report layout syntax
- [ ] Make `LAYOUT (...)` an explicit bucket for containers; pages use the page body itself for layout placement.
- [ ] Implement the canonical page syntax without repeating `PAGE` or forcing `AS LAYOUT`:
  ```sql
  CREATE PAGE overview AS (
    TITLE = 'Executive Overview',
    STRUCTURE = 'K K / A B / C C',
    MAP (
      'K' = KpiStrip,
      'A' = RevenueByRegion,
      'B' = MarginByProduct,
      'C' = OrderDetail
    ),
    GAP = '16px',
    STYLE (THEME = light)
  );
  ```
- [ ] Keep containers typed because container behavior matters:
  ```sql
  CREATE CONTAINER FilterDrawer AS DRAWER (
    TITLE = 'Filters',
    LAYOUT (
      STRUCTURE = 'A / B / C',
      MAP (
        'A' = RegionFilter,
        'B' = StatusFilter,
        'C' = ApplyWorkflow
      )
    ),
    OPTIONS (
      PINNABLE = ON,
      ICON = 'filter'
    )
  );
  ```
- [ ] Candidate container types: `BOX`, `SCROLL`, `DRAWER`, `SIDEBAR`, `TABS`, `ACCORDION`, `MODAL`, `POPOVER`. Avoid decorative/geometric container types unless there is a real reporting workflow need.
- [ ] Move layout-related settings such as `GAP`, responsive breakpoints, pinned panels, drawer placement, tabs, modals, and maximize behavior into `LAYOUT (...)` where possible.
- [ ] Update parser, AST, manifest builder, report runtime, VS Code preview, Report Portal renderer, docs, and samples together.

### Phase 2 — Actions, interactions, and buttons
- [ ] Replace `OPTIONS (CROSS_VISUAL_ACTION = HIGHLIGHT|FILTER|NONE)` with a dedicated interaction clause:
  ```sql
  INTERACTIONS (
    ON_SELECT = HIGHLIGHT,
    MATCHING = Region
  )
  ```
- [ ] Fix bidirectional cross-highlight behavior using `samples/kitchen_sinks/report_kitchen_sink.rptsql` as the reference. Current bug: clicking `BarByRegion` highlights `DrillRegionDetail`, but clicking `DrillRegionDetail` does not highlight `BarByRegion` after clearing the first selection.
- [ ] Decide and document valid triggers per object type:
    - Charts and tables: `ON_CLICK`.
    - Slicers/search/date/slider/textbox/numberbox/checkbox controls: `ON_CHANGE`.
    - Buttons: `ON_CLICK`.
    - Text/card/image visuals: no actions unless intentionally made clickable.
- [ ] Normalize button behavior so built-in buttons and custom buttons do not feel split-brained. Preferred direction: buttons are command emitters and `ACTIONS` defines behavior.
  ```sql
  CREATE BUTTON RefreshData AS BUTTON (
    TITLE = 'Refresh',
    ACTIONS (ON_CLICK = REFRESH_REPORT)
  );
  ```
- [ ] Add button/report actions for common workflow needs:
    - Show or hide `VISIBLE = OFF` visuals.
    - Refresh report or selected visuals.
    - Export CSV/Excel/PDF.
    - Navigate to page.
    - Open modal/drawer.
    - Clear filters.
- [ ] Add portal/viewer support for maximizing a single visual. Treat this as a layout/viewer capability, not a chart-specific option.

### Phase 3 — Navigation, datasets, publishing, and portal admin grammar
- [ ] Move `CREATE NAVIGATION ... WITH PAGES (...)` to one canonical body form:
  ```sql
  CREATE NAVIGATION MainNav AS TAB (
    ORIENTATION = HORIZONTAL,
    DEFAULT = Overview,
    PAGES (Overview, Details, Trends)
  );
  ```
- [ ] Review report datasets and portal datasets together. Keep `CREATE DATASET &name AS (...)` for report-owned reusable data, but make the naming story clear for `&dataset`, `#temp`, `USE DATASET`, `REFRESH DATASET`, and portal-registered datasets.
- [ ] Keep portal admin syntax as a separate command family:
    - Prefer `WITH (...)` for metadata/config on portal objects.
    - Prefer command verbs for operations: `PUBLISH REPORT`, `REFRESH REPORT`, `REBUILD SNAPSHOT`, `DROP SNAPSHOT`.
    - Decide whether paths are always string literals and names are always identifiers or strings; avoid mixing forms without a rule.
    - Keep secrets in expression positions so `ENC:` and future secret providers work consistently.
- [ ] Review subscription and refresh-job syntax for clarity. `CREATE REFRESH JOB FOR REPORT ... SCHEDULE ... AT ...` and `CREATE SUBSCRIPTION FOR REPORT ... DELIVER TO ...` are readable, but should be documented as portal commands rather than report-definition syntax.

### Phase 4 — Portal scriptability and baseline UX gaps
- [ ] Add Active Directory / LDAP / Windows-integrated identity support, or clearly define the first supported enterprise identity path.
- [ ] Treat every portal capability as script-first. If it can be done in the UI, it must have a SQL-like administrative syntax, and if the engine already has a primitive, prefer exposing that primitive coherently instead of inventing a second model.
- [ ] Polish and surface capabilities that already exist so they feel complete in the portal UI, docs, and scripting surface:
    - Group-based permissions and folder ACLs.
    - Publishing and republishing reports.
    - Subscriptions and subscription history.
    - Audit/activity log.
    - Dataset registry/refresh status.
    - Lineage/dependency data where available.
- [ ] Standardize report metadata. Owner/contact/tags can already come from script metadata comments such as `/* @owner: TeamName */`; define the canonical portal tags and decide how they flow into catalog fields.
- [ ] Standardize environment/deployment conventions. Dev/test/prod can already be handled with `CREATE SETS !DEV`, `CREATE SETS !TEST`, `CREATE SETS !PROD`, and `USE SETS !...`; define the portal/admin scripting pattern instead of adding a parallel deployment model too early.
- [ ] Fill catalog quality-of-life gaps expected in BI portals, with scriptable equivalents where useful:
    - Search reports/folders.
    - Favorites.
    - Recently viewed.
    - Tags/categories.
    - Last refreshed, last viewed, and failure status badges.
- [ ] Fill governance/admin gaps:
    - Effective permissions view for a user/report/folder.
    - Admin-facing usage metrics: views, unique viewers, refresh duration/failures, subscription delivery failures.
    - Content endorsement/certification or "trusted" marker.
- [ ] Fill lifecycle/publishing gaps:
    - Report version/history metadata.
    - Replace/republish flow with validation before publish.
    - Scripted promotion/deployment pattern built on `CREATE SETS` and portal `PUBLISH`/`ALTER REPORT` commands.
    - Dependency/lineage view showing report -> datasets -> source connections if the raw lineage is already available but not exposed as a portal experience.
- [ ] Fill sharing/consumption gaps:
    - Share link with permissions check.
    - Embed link/token story for internal apps.
    - Per-user saved parameter/filter views, similar to bookmarks.
    - Comments/annotations can wait unless collaboration becomes a target v1 feature.
- [ ] Add alerting after subscriptions are solid:
    - Threshold alerts on KPI/card/gauge visuals.
    - Alert ownership and visibility rules.
    - Alert delivery through the same notification/subscription infrastructure.

### Phase 5 — Documentation, samples, and release readiness
- [ ] Update the golden workflow and kitchen sink reports to the new canonical syntax.
- [ ] Add parser tests for every changed statement form.
- [ ] Add report runtime tests for interactions, buttons, layout containers, navigation, and maximize.
- [ ] Add portal integration tests for publish, permissions, subscriptions, refresh, export, audit, and catalog search.
- [ ] Update `AGENTS.md`, `Docs/Report_SQL_Guide.md`, `Docs/Reference/Grammar.md`, `Docs/Strategy/ReportPortal_Strategy.md`, editor help, and sample guide so all agents and users generate the same syntax.
- [ ] Remove old docs/examples for replaced syntax before launch unless a deliberate compatibility decision is made.

- [ ] **Phase 6 — Advanced Visualization Capability Gaps (BI Parity)**
    - [x] **GANTT Visual**: Port the existing Orchestrator Portal Gantt implementation (ECharts 'custom' series) into the reporting engine.
    - [ ] **Pivot/Matrix Visual**: Cross-tab representation with collapsible row/column headers (Industry Standard: Power BI Matrix).
    - [ ] **Sankey/Sunburst**: Relational/Flow visualizations using ECharts native types.
    - [ ] **Small Multiples (Trellis)**: Repeat a visual across a grid for each category value.
    - [ ] **Selection Primitives**: Brush/Lasso selection on Scatter/Scatter3D to drive parameter filters (Industry Standard: Tableau Brush).
    - [ ] **Network Graph**: Force-directed graphs for lineage and relationship exploration.

- [ ] **Fuzzy Matching — Full Feature Set** — See `Docs/Strategy/FuzzyMatching_Strategy.md` for the complete design. Five phases in recommended order:
    - **Phase 1 — `NORMALIZE()` function** *(go first — highest ROI, smallest scope)*: Domain-aware string preprocessing with presets for COMPANY, PERSON, ADDRESS, PHONE, EMAIL. Eliminates surface variation before any similarity algorithm runs.
    - **Phase 2 — String Similarity & Phonetic Functions**: `SIMILARITY(a, b, algorithm)` supporting JAROWINKLER, LEVENSHTEIN, TRIGRAM, JACCARD, TOKENSORT. Engine-level `SOUNDEX`, `METAPHONE`, `DMETAPHONE`. Foundation for all subsequent phases.
    - **Phase 3 — Blocking Utilities**: `NGRAMS(s, n)` and `NGRAM_TOKENS(s)` to support user-built inverted-index blocking patterns. Documented cookbook recipes for manual block → score → rank pipeline.
    - **Phase 4 — `FUZZY JOIN` syntax** *(biggest ergonomic win)*: `FROM #a FUZZY JOIN #b ON SIMILARITY(...) > 0.80 KEEP BEST 1`. Built-in trigram blocking index; `LEFT FUZZY JOIN` variant; `__score` injected into result. Semantics for threshold/cardinality/ties fully documented in strategy doc.
    - **Phase 5 — Embedding-Based Semantic Matching** *(defer until Phase 4 ships)*: `EMBED(col, endpoint, model)` via pluggable HTTP endpoint (Ollama/OpenAI-compatible). `VECTOR` column type. `SIMILARITY(a, b, 'COSINE')`. For the cases where string algorithms fail entirely (semantic variation).
