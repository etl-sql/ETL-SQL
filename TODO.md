# ETL-SQL Development Roadmap
## Up Next
- [ ] **Subscription Parameters** — Full strategy: [`Docs/Strategy/SubscriptionParameters_Strategy.md`](Strategy/SubscriptionParameters_Strategy.md). RELDATE/LIST types, `SET WEEK_START_DAY`, `CREATE/ALTER SUBSCRIPTION PARAMETERS(...)`, portal INPUT parameter UX. ~6.5 dev-days across 6 phases. Implementation tasks below.
    - **Phase 1 — Engine: New Types** *(most isolated, start here)*
        - [ ] `ETL-SQL.Core/Ast.cs`: Add `RelDateType`, `ListType` to type system; `SetWeekStartDayStatement` record; `INPUT` modifier on `DeclareStatement`.
        ListType, INPUT modifier already exist.
        - [ ] `ETL-SQL.Core/TokenType.cs` + `Lexer.cs`: Add `RELDATE`, `LIST`, `INPUT`, `WEEK_START_DAY` tokens/keywords.
        - [ ] `ETL-SQL.Core/Parser`: Parse `DECLARE @var RELDATE = <expr> [INPUT]`, `DECLARE @var LIST(type) [= default] [INPUT]`, `SET WEEK_START_DAY = '<day>'`.
        - [ ] `ETL-SQL.Engine/RelDateResolver.cs` *(new)*: Stateless resolver — anchor parse, period-shift arithmetic, N/NU inline units, fixed-date passthrough, `ExecutionException` on bad input. See spec in strategy doc.
        - [ ] `ETL-SQL.Engine/SetWeekStartDayHandler.cs` *(new)*: Validate day name, store on `IExecutionContext`.
        - [ ] `ETL-SQL.Engine/Evaluator.cs`: Surface `WeekStartDay` from `appsettings.json → Engine.StartOfWeek` (default Monday). Wire handler.
        - [ ] `appsettings.json`: Add `Engine.StartOfWeek` string setting.
        - [ ] `ExpressionEvaluator.cs`: Resolve `RELDATE` variable reads via `RelDateResolver` at runtime.
        - [ ] Tests: `RelDateResolverTests.cs` (exhaustive), `SetWeekStartDayTests.cs`, `WeekStartArithmeticTests.cs`.
    - **Phase 2 — Subscription SQL Syntax**
        - [ ] `ETL-SQL.Core`: Add `Name?` + `Parameters: IReadOnlyList<SubscriptionParameter>` to `CreatePortalSubscriptionStatement`; new `SubscriptionParameter(Name, Value)` record; `AlterPortalSubscriptionStatement` record.
        - [ ] `ETL-SQL.Core/Parser`: Parse optional `<name>` on `CREATE SUBSCRIPTION`; parse `PARAMETERS(...)` clause; parse `ALTER SUBSCRIPTION` statement.
        - [ ] `ETL-SQL.Engine/CreatePortalSubscriptionHandler.cs`: Persist `Name` + `ParametersJson`.
        - [ ] `ETL-SQL.Engine/AlterPortalSubscriptionHandler.cs` *(new)*: Update schedule/format/active/params; replace full param set when clause present; leave unchanged when absent; clear when clause is empty list.
    - **Phase 3 — Portal Data Layer**
        - [ ] `Subscription.cs` entity: Add `Name` (nullable `TEXT`) + `ParametersJson` (nullable `TEXT`).
        - [ ] New EF Core migration: `AddSubscriptionNameAndParameters`.
    - **Phase 4 — Portal API**
        - [ ] `CreateSubscriptionRequest` / `UpdateSubscriptionRequest` models: Add `Name?`, `Parameters?`.
        - [ ] Subscription response DTOs: Add `Name?`, `Parameters?`, `ParameterSummary` (server-built compact string).
        - [ ] New endpoint: `GET /api/reports/{*path}/parameters` — parse script AST, return INPUT parameter metadata (name, type, default, required). No script execution.
        - [ ] `POST /api/subscriptions`: Persist name + parameters JSON.
        - [ ] `PUT /api/subscriptions/{id}`: Accept parameter replacement.
        - [ ] `GET /api/subscriptions` (admin + mine): Include name, parameters, summary.
        - [ ] Orchestrator job runner: Pass stored parameter values to script; resolve `RELDATE` expressions fresh at fire time.
    - **Phase 5 — Portal UI**
        - [ ] Subscribe modal: call `/api/reports/{path}/parameters` before render; append per-type INPUT controls (RELDATE=quick-pick+custom, LIST=chip input, etc.). Serialize to `{ "@name": "value" }` on save.
        - [ ] My Subscriptions list: show parameter summary; **Edit Parameters** modal (pre-populated, saves via PATCH).
        - [ ] Admin Subscriptions view: parameter summary column + Edit Parameters action.
    - **Phase 6 — Documentation** *(update as phases land)*
        - [ ] `Docs/Report_SQL_Guide.md`: Add `RELDATE`/`LIST`/`INPUT` to parameter type table; `INPUT` modifier section.
        - [ ] `Docs/ReportPortal_User_Guide.md`: Sections 6 + 7 with parameter controls and Edit Parameters UX.
        - [ ] `Docs/ReportPortal_Administrators_Guide.md`: Section 8 with `CREATE SUBSCRIPTION` / `ALTER SUBSCRIPTION` full syntax.
        - [ ] `Docs/User_Manual.md`: `SET WEEK_START_DAY` in SET reference; `Engine.StartOfWeek` in config reference.
        - [ ] `Docs/Reference/Grammar.md`: New productions for all added statements and types.
- [x] **Security Manifest**: Strategy document complete. See [`Docs/Strategy/ScriptSecurity_Strategy.md`](Strategy/ScriptSecurity_Strategy.md). Full PKI signing not recommended — disproportionate key management overhead. **Hash pinning** instead: store SHA-256 of script at schedule/publish time, compare at run time, warn or block on mismatch. ~2 dev-days across 3 phases.
    - **Phase 1 — Orchestrator hash pinning**
        - [ ] `OrchestratorJob` entity: Add `ScriptHash` (TEXT) + `HashPolicy` (`Warn`/`Block`, default `Warn`).
        - [ ] New EF Core migration: `AddJobScriptHash`.
        - [ ] `JobScheduler`: Compute and store hash at schedule time.
        - [ ] `JobRunner`: Recompute at run time; compare; apply policy; log result.
        - [ ] `ExecutionHistory` entity: Add `ScriptHashAtRunTime` (TEXT) + `HashMatched` (bool).
        - [ ] `appsettings.json`: Add `Engine.ScriptHashPolicy` global default.
        - [ ] `SET SCRIPT_HASH_POLICY` statement: Parse + apply per-script override.
        - [ ] Tests: match → runs; mismatch+Warn → runs with log; mismatch+Block → `ExecutionException`.
    - **Phase 2 — Report Portal hash pinning**
        - [ ] `Report` entity: Add `PublishedScriptHash` (TEXT).
        - [ ] Publish flow: Compute and store hash.
        - [ ] Snapshot builder: Compare hash; log `ScriptHashAtRunTime` + `HashMatched` on snapshot record.
        - [ ] Admin → Reports view: Show "script changed since published" (distinct from generic stale indicator).
        - [ ] Audit log: Include `ScriptHash` on `EXECUTE_REPORT` events.
    - **Phase 3 — Documentation**
        - [ ] `Docs/Administrators_Guide.md`: Add `Engine.ScriptHashPolicy` to config reference.
        - [ ] `Docs/ReportPortal_Administrators_Guide.md`: Hash tracking in publishing + execution sections.
        - [ ] `Docs/Architecture/Orchestrator.md`: Hash fields on job and execution history entities.
- [x] **Data Lake Connection brainstorm**: Strategy document complete. See [`Docs/Strategy/DataLake_Connectors_Strategy.md`](Strategy/DataLake_Connectors_Strategy.md). Revised scope: existing ODBC connector already covers Redshift, Databricks, Synapse, Trino, Dremio. Existing Parquet + Avro connectors already cover the file formats. Only Snowflake and BigQuery need new native connectors (complex auth not expressible in an ODBC string). DuckDB added as low-priority ergonomics improvement. ~6.5 dev-days across 4 phases.
    - **Phase 1 — Snowflake native connector** *(most requested platform)*
        - [ ] `ETL-SQL.Core/TokenType.cs`: Add `SNOWFLAKE` keyword.
        - [ ] `ETL-SQL.Connectors/SnowflakeConnector.cs` (new): `Snowflake.Data.Client`. Auth: username+password and private-key JWT (`PRIVATE_KEY_FILE` option). Fields: `HOST`, `WAREHOUSE`, `DATABASE`, `SCHEMA`, `USERNAME`.
        - [ ] `ISchemaProvider` via `INFORMATION_SCHEMA`.
        - [ ] `DependencyInjectionSetup.cs`: Register connector.
        - [ ] Unit tests with Snowflake mock transport; `Category=Integration` tests with 30-day trial. CI secret: `SNOWFLAKE_CONNECTION_STRING`.
        - [ ] `Docs/Reference/Data_Connectors.md`: Snowflake section.
    - **Phase 2 — BigQuery native connector** *(unique SQL dialect + GCP auth)*
        - [ ] `ETL-SQL.Core/TokenType.cs`: Add `BIGQUERY` keyword.
        - [ ] `ETL-SQL.Connectors/BigQueryConnector.cs` (new): `Google.Cloud.BigQuery.V2`. Auth: `CREDENTIAL_FILE` (service account JSON) or ADC (omit file for workload identity).
        - [ ] Pushdown dialect: backtick `QuoteIdentifier`; `project.dataset.table` three-part name resolution via `ISqlCompilerContext`.
        - [ ] `ISchemaProvider` via `INFORMATION_SCHEMA`.
        - [ ] Unit tests against BigQuery emulator Docker image; `Category=Integration` tests using `bigquery-public-data` (no fixture setup). CI secret: `GCP_SA_KEY_JSON`.
        - [ ] `Docs/Reference/Data_Connectors.md`: BigQuery section.
    - **Phase 3 — Connector interface enhancements + ODBC docs**
        - [ ] `IConnector` / connector metadata: Add `CommandTimeoutSeconds` (default 30 for OLTP, 1800 for warehouse connectors) and `ReadOnly` flag (default `true` for warehouse connectors).
        - [ ] `CREATE CONNECTION OPTIONS(TIMEOUT_SECONDS = n)`: Parse and apply per-connection override.
        - [ ] `appsettings.json`: Add `Connectors.DataWarehouse.DefaultCommandTimeoutSeconds`.
        - [ ] LSP schema cache TTL: configurable per connection type; default 5 min for warehouse connections.
        - [ ] `Docs/Reference/Data_Connectors.md`: **Data Warehouse via ODBC** section with connection string examples for Redshift, Databricks, Synapse, Trino, Dremio. Note which platforms need native connectors vs. ODBC.
        - [ ] `Docs/Architecture/Connectors.md`: Document `CommandTimeoutSeconds` and `ReadOnly` fields.
        - [ ] `Docs/Standards/Connectors_Standards.md`: Data warehouse connector checklist.
- [x] **Fresh Eyes Deep Code Architecture & Refactor Audit**
    - [x] **De-bloat `Evaluator.cs`**: Extract concerns (Reporting, Metrics, Variable Scoping) to specialized services; current class is a "God Object" (60KB). (Completed: migrated to composition-based sub-contexts: ITelemetryContext, IVariableContext, IReportContext).
    - [x] **Refactor `SelectStatementHandler.cs` (SRP Violation)**: Move CTE registration, Lineage tracking, and Pushdown logic to dedicated engines/helpers.
    - [x] **Harden `CreateConnectionStatementHandler`**: Replace hardcoded `fileConnectors` list with interface-based capability detection for `ResolvePath` enforcement.
    - [x] **Centralize Security Guardrails**: Move manual recursion and `IncrementOperationCount` logic in `DirectoryOperationStatementHandler` to a centralized file system security policy.
    - [x] **Simplify `ExpressionEvaluator`**: Move ANSI string/date functions (`SUBSTRING`, `OVERLAY`, etc.) to `FunctionRegistry` and investigated performance of `ResolveIdentifierFallback` on wide rows (fixed shadowing bug & optimized name retrieval).
- [x] **Type System Bugs** — Specialty type behaviors are broken or incomplete (discovered during Grammar.md audit).
    - [x] **JSON validation at assignment** (`ETL-SQL.Core/Data/TypeConverter.cs`): The `JSON` converter just calls `v.ToString()`. It must call `JsonDocument.Parse()` and re-throw as `ExecutionException` on failure so a malformed JSON string errors at the `DECLARE` line, not buried in a `JSON_VALUE` call later.
    - [x] **XML validation at assignment** (`ETL-SQL.Core/Data/TypeConverter.cs`): Same issue — the `XML` converter just calls `v.ToString()`. Must call `XDocument.Parse()` and re-throw as `ExecutionException` on failure.
    - [x] **ENCRYPTED doesn't protect at runtime** (`ETL-SQL.Core/Parser/Components/SystemParser.cs`): `ENCRYPTED` is the canonical type for `ENC:...` passwords and connection string credentials, but the parser does NOT set `IsSensitive = true` for it — only `SENSITIVE` and `SECRET` get that flag. Consequence: `SHOW VARIABLES` displays raw `ENC:...` strings for ENCRYPTED variables, and the auto-decrypt path in `ExpressionEvaluator` (which checks `meta.IsSensitive`) silently skips them, passing the raw cipher text to connectors. Fix: add `"ENCRYPTED"` to the `isSensitive` check in `ParseDeclare()` alongside `SENSITIVE` and `SECRET`. Verify connector auth actually works end-to-end after the fix.
    - [x] **SECRET is a no-op alias for SENSITIVE** (`ETL-SQL.Core/Parser/Components/SystemParser.cs`, `ETL-SQL.Engine`): Both types set `IsSensitive = true` and nothing else — the documented "purged from memory on session end" behavior for `SECRET` does not exist. Need to implement session-end variable purge for `SECRET` (clear the variable from all scopes when the evaluator tears down) Grammar.md must be updated to show the differences.
    - [x] **Grammar.md section 1.2 cleanup**: After the bugs above are fixed, update the `MARKDOWN` description to make clear it carries no validation (all strings are valid markdown) and is a rendering hint only. Update `ENCRYPTED` to accurately reflect its now-fixed runtime masking and auto-decrypt behavior.
- [x] **Subquery Cache Optimization** — Implement a sophisticated subquery cache that supports correlated subqueries. Current naive caching is disabled or incorrect for correlated queries.
    - [x] **Phase 1 — Foundation: Keyed Caching & Correlation Analysis**
        - [x] `ETL-SQL.Core/Data`: Create `SubqueryCacheKey` record: `(Statement Query, object[] CapturedValues)`.
        - [x] `ETL-SQL.Engine/Services`: Implement `SubqueryAnalyzer` to identify "Outer References" in a subquery AST.
        - [x] `Evaluator.cs`: Update `SubqueryCache` to `LruCache<SubqueryCacheKey, object?>`.
    - [x] **Phase 2 — ExpressionEvaluator Integration**
        - [x] `ExpressionEvaluator.cs`: Update `EvaluateSubquery` to harvest captured values from `OuterRowStack`.
        - [x] `ExpressionEvaluator.cs`: Implement keyed lookup/set using `SubqueryCacheKey`.
        - [x] Optimization: Detect and globally cache "Static" (non-correlated) subqueries.
        - [x] Telemetry: Track `@@SUBQUERY_CACHE_HITS/MISSES`.
    - [x] **Phase 3 — Complex Subquery Support**
        - [x] Support for correlated subqueries containing `GROUP BY`, `WINDOW` functions, and `ORDER BY`.
        - [x] Context Isolation: Ensure clean execution of inner engines (Aggregate/Window) while preserving outer scope visibility.
        - [x] Nested Subquery validation: Ensure cache keys work correctly at arbitrary recursion depths.
    - [x] **Phase 4 — Scalability & Memory Management**
        - [x] `Evaluator.SpillAsync()`: Implement selective clearing/spilling of cache based on cost/latency metrics.
        - [x] Config: Add `Engine.SubqueryCacheSize` to `appsettings.json` (default 5000).
    - [x] **Phase 5 — Automated Testing & Performance Validation**
        - [x] `SubqueryCacheTests.cs`: Comprehensive suite covering Scalar, Correlated, Nested, Nulls, and Complex logic (Window/Agg).
        - [x] Big Data Stress Test: Verify cache hit efficiency and memory stability on 1M+ row batches with recurring keys.
    - [x] **Phase 6 — Final Stabilization**
        - [x] Run full test suite (100% pass).
        - [x] Run all `/samples/` scripts (100% pass).
        - [x] Documentation: Update `Docs/Architecture/Engine.md` with keyed subquery model details.
        - [x] Add @@SUBQUERY_CACHE_HITS/MISSES to `Docs/Reference/Grammar.md`.
- [ ] **Report portal https** Add a way for the report portal to use HTTPS.
- [x] **Sample Suite Stabilization** — Resolved functional regressions in the Kitchen Sink sample suite (fn_date_math_sink, fn_string_sink, fn_sys_logic_sink).
    - [x] Implemented missing standard functions: EXP, LOG, LOG10, RAND, CONCAT_WS, SPLIT_PART, SPACE, and a full suite of REGEXP functions.
    - [x] Resolved PRINT multi-argument rendering.
    - [x] Validated full suite (93 scripts) with 100% success rate (excluding environmental skips).
- [x] **Publish needs updating** — Added ReportPortal to publish scripts and updated documentation with correct naming conventions and basic setup instructions.
- [x] **EXEC and EXECUTE unification** — Unified the grammar for both keywords in `ExtensionParser.cs`. `EXEC` is now a formal shorthand for `EXECUTE`.
- [x] **Kitchen sink test for EXECUTE** — Implemented comprehensive validation for positional/indexed parameters, dynamic SQL strings, shorthand synonyms, and connection expressions.
```sql
 CREATE CONNECTION ds ON MOCKDB();
 DECLARE @id int = 1
         ,@name varchar(50) = 'John'
;
 EXECUTE ds INTO #temp WITH(@id, @name)
 BEGIN
    SELECT * FROM ds.Employees WHERE EmployeeID = ? AND Name = ?;
 END

 EXECUTE ds INTO #temp2 WITH(@id, @name)
 BEGIN
    SELECT * FROM ds.Employees WHERE EmployeeID = ?1 AND Name = ?2;
 END

 DECLARE @query varchar(2000);
 SET @query = 'SELECT * FROM ds.Employees WHERE EmployeeID = ' + @id + ' AND Name = ' + QUOTENAME(@name) + ';';
 EXECUTE ds INTO #temp3 (@query);

 -- Shorthand and Expression support:
 EXEC ds (@query);
 EXECUTE (@ds) INTO #temp4 (@query);
```
- [x] **Check code for any hardcoded values** Any hardcoded values should be in appsettings.json and not in the code.  They should also have a SET ... statement that allows the user to change the value at runtime for that script.
- [x] **Missing metrics** We have a lot of great metrics exposed by @@ variables.  Added @@SUBQUERY_CACHE_HITS/MISSES etc. to docs.
- [x] **Code check** We have done a lot of code changes.  Any findings record them as TODO.md items.
   - [x] **Check documentation** - Standardized names and added metrics.
   - [x] **Check tests** - Resolved BulkInsertErrorTests regression.
   - [x] **Check samples** - Completed (verified with 95 scripts).
    - [x] **Check for performance issues** - Fixed ArrowSpillWriter bottleneck.
    - [x] **Check for stability issues** - 1132 unit tests passing.
    - [x] **Check for any regressions** - 1132 tests passing. Full suite validated.
    - [ ] **Final Audit & Stability Refactor** — Comprehensive cleanup and stabilization phase. Focuses on SRP compliance, security linter guardrails, and refactoring "God Objects" to prevent maintenance fatigue and IDE instability.
        - **Phase 1 — StandardFunctions Refactor (SRP)**
            - [x] `ETL-SQL.Engine/Functions/`: Split `StandardFunctions.cs` into partial classes: `.String.cs`, `.Math.cs`, `.Date.cs`, `.Logic.cs`, `.System.cs`.
            - [x] Verify `IFunctionRegistry` registrations match baseline exactly.
            - [x] **Verification**: Run `dotnet test` (Core/Engine suite). Must be 100% pass.
            - [x] **Verification**: Run `/scripts/Test-AllSamples.ps1`. Must be 100% pass (excluding environmental skips).
        - **Phase 2 — EChartsRenderer Refactor (Strategy Pattern)**
            - [x] `ETL-SQL.ReportBuilder/Renderers/`: Create specialized renderers (`Cartesian`, `Circular`, `Hierarchical`, `Statistical`, `Specialized`, `Overlay`).
            - [x] `EChartsRenderer.cs`: Refactor to a dispatcher/factory pattern. Ensure zero change in output JSON structure.
            - [x] **Verification**: Run `dotnet test`. Must be 100% pass.
            - [x] **Verification**: Run `/scripts/Test-AllSamples.ps1`. Must be 100% pass (excluding pre-existing script errors).
        - [x] **Phase 3 — Linter & Security Guardrails**
            - [x] `ETL-SQL.Core/Linting/Rules/AbsolutePathRule.cs`: Implement warning for relative paths in I/O operations (FROM, INTO, EXECUTE, etc.).
            - [x] `ETL-SQL.Core/Linting/Rules/FileSystemSecurityRule.cs`: Implement warning for system directory access (`C:\Windows`, `/etc`) and direct root access.
            - [x] `ETL-SQL.Connectors/Postgres/PostgresSyntax.cs`: Add `GETDATE` and `SYSDATE` to `Exclusions`.
            - [x] **Verification**: Run `dotnet test`. Must be 100% pass.
            - [x] **Verification**: Run `/scripts/Test-AllSamples.ps1`. Must be 100% pass.
        - **Phase 4 — Core Data & Stability Audit**
            - [x] `ETL-SQL.Core/Data/DataSources.cs`: Extract `InMemoryTableIndex` from `InMemoryDataSource` to separate concern.
            - [x] Audit `InMemoryDataSource.UpdateRows` and `DeleteRows` for atomicity/null safety (potential crash prevention).
            - [x] Audit `SpillStore.cs` for file handle leaks or race conditions during rapid spill/read cycles.
            - [x] **Verification**: Run `dotnet test`. Must be 100% pass.
            - [x] **Verification**: Run `/scripts/Test-AllSamples.ps1`. Must be 100% pass.
        - [x] **Phase 5 — Help System & Final Polish**
            - [x] `ETL-SQL.Core/Metadata/LanguageHelpRegistry.cs`: Comprehensive audit. Ensure all new statements and functions have accurate help signatures.
            - [x] Standardize Visual option naming (consistent case-sensitivity handling in `VisualBuilder` vs `EChartsRenderer`).
            - [x] Update Grammar.md with any missing syntax including all help commands, charts, etc.
            - [x] **Final Verification**: Run full regression suite. 100% pass required.
