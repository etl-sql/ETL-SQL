# ETL-SQL Development Roadmap

## Up Next

- [ ] **Code review round 2** — findings below, implement separately per priority.

  #### 🔴 High Priority — Security

  - [x] **SQL injection via unparameterized table names in connectors** — `SqlServerDataSource.cs:78`, `OracleDataSource.cs:71,234`, `PostgresDataSource.cs:77,231,277` all use `$"SELECT * FROM {_tableName}"` and similar patterns. Table names are user-supplied identifiers and must be quoted/validated, not interpolated directly. Use a safe quoting helper (bracket/double-quote escaping per dialect) since table names can't be parameterized via ADO.NET.

  - [x] **Hardcoded fallback password `"DefaultETLPass123!"`** — `FileOperationStatementHandler.cs:143,151` and `DirectoryOperationStatementHandler.cs:136,145` fall back to this literal when no password is provided and `MasterPassword` is null. Any encrypted file is trivially decryptable by anyone with source access. Remove the hardcoded fallback; throw a clear error instead when encryption is requested with no credential.

  - [x] **SMTP default port 25 (plaintext)** — `SmtpDataSource.cs:135` defaults to port 25 when no PORT option is set. Credentials and message body travel in plaintext. Default to 587 (STARTTLS) instead.

  #### 🔴 High Priority — Bugs

  - [x] **File handle leak in SMTP attachments** — `SmtpDataSource.cs:117` calls `File.OpenRead(path)` and passes the stream directly to `MimeContent`. The stream is never disposed if MimeKit doesn't take ownership, leaking file handles when sending multiple emails with attachments. Copy to a `MemoryStream` first or ensure MimeKit closes the underlying stream.

  - [x] **Negative OFFSET silently swallowed** — `SelectExecutionEngine.cs:280-281` checks `if (offset > 0)` before applying `.Skip()`. A negative OFFSET is silently treated as zero, returning all rows instead of an error. Should validate `offset >= 0` and throw `ExecutionException` otherwise.

  - [x] **Negative/zero threshold values not validated** — `SetThresholdStatementHandler.cs` validates `ExternalHashPartitions > 0` but not `BatchSize`, `ForeachPageSize`, `ExternalSortChunkSize`, or `MaxMessages`. Setting these to zero or negative causes hangs, silent data loss, or divide-by-zero downstream. Add a `> 0` guard matching the existing partition check pattern.

  #### 🔴 High Priority — Performance

  - [x] **O(n²) UNION DISTINCT deduplication in recursive CTEs** — `SelectStatementHandler.cs:440` uses `.Any(existing => context.IsSoftEqual(existing, r))` (full linear scan) per new row in the recursive accumulator. At 10k+ rows this becomes the dominant cost. Replace with a hash-based dedup set using `CompoundKey` as in the join engine.

  - [x] **Nested-loop Semi/Anti joins with no hash optimization** — `JoinEngine.cs:243-270` uses `foreach (left) { foreach (right) }` for SEMI/ANTI join types. No hash table is built on the probe side. These become O(n·m) at 100k rows. Build a `HashSet<CompoundKey>` on the right side once, then probe it per left row.

  - [x] **Unnecessary `.ToList()` on `Columns` dictionary per row in JoinEngine** — `JoinEngine.cs:35,209` calls `.ToList()` on `r.Columns` just to iterate, allocating a new `List` per row processed. Change to `foreach (var kv in r.Columns)` directly.

  #### 🟡 Medium Priority — Performance

  - [ ] **Uncached reflection per row in member access evaluation** — `ExpressionEvaluator.cs:436-440` calls `GetType().GetProperty()` and `GetType().GetField()` on every row evaluation when a `MemberAccessExpression` is hit. Cache the `PropertyInfo`/`FieldInfo` keyed by `(type, memberName)` in a static `ConcurrentDictionary`.

  - [ ] **O(n²) identifier ambiguity check per column reference** — `ExpressionEvaluator.cs:90` runs `.Any(other => ...)` inside a `foreach` over `context.Columns.Keys` to detect ambiguous names. For wide rows this is O(columns²) per identifier resolution. Pre-build a `HashSet<string>` of suffixes (column name without qualifier) once per row and check it in O(1).

  - [ ] **`ToSql()` called multiple times per expression in hot loops** — `AggregateEngine.cs:47,234` and `ExpressionEvaluator.cs:289` call `expr.ToSql()` 2–3× for the same expression in tight loops. Cache the result in a local variable at the start of the expression visit.

  - [ ] **TUI results panel `Skip().ToList()` on every render** — `ResultsPanel.cs:38,43` does `res.Rows.Skip(_renderer.ResultScrollRow).Take(...)..ToList()` on each redraw. With 50k+ rows this materializes a large intermediate allocation per keypress. Use indexed access with a page window instead.

  #### 🟡 Medium Priority — Maintainability / SRP

  - [ ] **`Evaluator` stores Report-SQL object registries** — `Evaluator.cs:206-220` has `VisualDefinitions`, `PageDefinitions`, `ContainerDefinitions`, etc. as direct properties. The execution engine layer owning UI/report definitions creates a layering violation and makes `Evaluator` a god class (1,000+ lines, 3+ interfaces). Extract to `IReportRegistry` and inject it as a dependency rather than baking it into the evaluator.

  - [ ] **`ManifestBuilder.BuildAsync` is a 230-line method** — Handles visuals, pages, containers, navigations, buttons, and datasets all inline. Split into `BuildVisuals()`, `BuildPages()`, etc. private methods so each report object type can be tested and modified independently.

  - [ ] **`DashboardService` duplicates parameter refresh logic** — `SetParametersAsync` (lines 65-109) and `SetParameterAsync` (lines 115-154) duplicate the visual dependency scan and re-query loop. Extract a `RefreshAffectedVisuals(IEnumerable<string> changedParams)` helper called by both.

  - [ ] **Magic numbers in `Evaluator.cs` without named constants** — `500` (cache size), `10000` (batch size, recursive depth), `100000` (join/sort thresholds), `1000` (max messages), `200 * 1024 * 1024` (session size) are all hardcoded inline. Define them as `const` in a `EngineDefaults` static class so they're tunable in one place and self-documenting.

  - [ ] **Inconsistent `DBNull` check pattern** — `ExpressionEvaluator.cs` uses 4 different patterns to check for null/DBNull (`val == null`, `val == DBNull.Value`, combined `&&`, and inverted `||`). Extract `static bool IsDbNull(object? val) => val is null or DBNull` and replace all 9+ call sites.

  #### 🟡 Medium Priority — Documentation

  - [ ] **`Architecture/Reporting.md` is missing new statement types** — `CREATE STYLE`, `CREATE TEMPLATE`, `CREATE BUTTON`, `ALTER <type>`, `DROP <type>`, and `CREATE OR ALTER` are all implemented but absent from the architecture overview and parser dispatch table. Update the doc to match the current statement set.

  - [ ] **TEXT visual documentation says "VALUE option" but parser uses `DEFAULT` clause** — `Report_SQL_Guide.md:147` tells users to write `OPTIONS (VALUE = '...')` for TEXT visuals, but the parser stores text content in the `DefaultValue` field via the `DEFAULT` clause. Update the guide with the correct syntax and a working example.

  #### 🟢 Low Priority / Simplification

  - [ ] **`BeginTransactionStatementHandler.cs` has duplicate `using System.Threading.Tasks;`** — Remove one.

  - [ ] **HTTP custom headers use `TryAddWithoutValidation`** — `RestDataSource.cs:154-161` bypasses .NET's header validation. Values containing CRLF could cause header injection. Either validate header values or use the validating `Add()` overload.

  - [ ] **`StatementParser` is a 7,000-line partial class across 7 files** — Consider whether the partial split across `StatementParser.Data.cs`, `StatementParser.Report.cs`, `StatementParser.Flow.cs`, etc. should become actual separate classes composited by a thin `StatementParser` dispatcher. The current approach technically works but makes cross-file navigation painful and disguises the true complexity.

  - [ ] **`quote` identifier logic in `Evaluator.GetSqlTableName` is an untestable inline lambda** — `Evaluator.cs:696-706` defines dialect-specific quoting as a local `Func<string,string>`. Extract to `private static string QuoteMssqlIdentifier(string s)` / `QuoteStandardIdentifier(string s)` so they can be unit-tested and reused from `QueryCompiler`.

  #### 🧪 Linting Gaps

  - [ ] **No linter rule: `MULTISELECT` / `SLICER` without `SOURCE`** — The parser allows it and silently produces a broken visual. Add `VisualSourceRequiredRule` that flags these as errors (not warnings).

  - [ ] **No linter rule: required `MAPPINGS` per visual type** — `BAR` without X+Y, `PIE` without LABEL+VALUE, `CARD` without VALUE are all accepted by the parser but produce empty/broken charts at runtime. Add `VisualMappingCompletenessRule` to catch these at lint time.

  - [ ] **No linter rule: deprecated connector syntax** — The parser throws immediately on `FILE(...)` (should be `FLATFILE`), but a linter rule would give a friendlier message during development with a suggestion to use the current syntax.
