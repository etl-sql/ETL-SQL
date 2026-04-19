# ETL-SQL Development Roadmap

## Up Next
- [x] **Code check** — findings below, implement separately per priority.

  #### 🔴 High Priority

  - [x] ~~**Duplicate `UnwrapJsonValue` / `JsonElementToValue`** — extracted to `SpillSerializationHelper` in `ETL-SQL.Engine/Spill/`; all four engines updated.~~

  - [x] ~~**Integer overflow in `ExternalAggregateEngine` partition hashing** — fixed all three call sites to `(hash & 0x7FFFFFFF) % PartitionCount`.~~

  - [x] ~~**`PartitionCount = 0` not validated in `ExternalJoinEngine`** — if `appsettings.json (app settings)` sets `ExternalHashPartitions = 0`, line 76 causes `DivideByZeroException` at runtime with no diagnostic. Add a guard in constructor or at call site.~~

  - [x] ~~**`ExternalJoinEngine` loads full partitions into memory** — refactored to stream right partition into hash table, then stream left partition for probe; `ToListAsync()` eliminated.~~

  - [x] ~~**Path safe-zone check is case-insensitive on all platforms** — added `PathComparison` static field using `RuntimeInformation.IsOSPlatform(OSPlatform.Linux)` and applied to all path `StartsWith` checks in `SecurityService`.~~

  - [x] ~~**`_operationCount` in `Evaluator` is not thread-safe** — changed to `Interlocked.Increment`.~~

  #### 🟡 Medium Priority

  - [x] ~~**Bare `catch { }` in `SpillStore.Cleanup()`** — Checked and verified it already logs a warning with path and message context.~~

  - [x] ~~**`__SORT_KEYS` column could collide with user data** — `ExternalSortEngine` attaches sort keys to each row via a synthetic column named `_SYS_SORT_KEYS_`. If the query has a column with that name, it is correctly handled without collision.~~

  - [x] **`SpillSecurityRule` recursion is unbounded** — added depth counter (limit 50) and fixed missing namespace import to finalize the security guardrail.

  - [x] ~~**`ExternalWindowEngine.WindowSignature` hash/equals inconsistency** — Fixed by using a record with cached HashCode and SQL-based equality for performance and reliability.~~

  - [x] ~~**`ExternalSortEngine` chunk counter not reset between calls** — Verified `_chunkCounter` is now a local variable in `SortStreamAsync`, ensuring reset per operation.~~

  - [x] **`StatementParser.Report.cs ParseCreateVisual` is 150+ lines** — Decomposed into modular sub-handlers (`ParseVisualSource`, `ParseVisualMappings`, etc.) to improve maintainability.

  - [x] ~~**`ExternalWindowEngine` uses `FirstOrDefault()` scan per window function** — line 81 scans the existing signature list linearly on every row. Fixed by caching signatures which reduces the re-evaluation overhead.~~

  #### 🟢 Low Priority / Simplification

  - [x] **`Encoding.UTF8.GetByteCount(json)` called per row in `SpillStore`** — Optimized by wrapping in a `TelemetryEnabled` guard; accumulates length only when metrics are required.
  - [x] **`SecurityService` `ApprovedSafeZones.Any()` is O(n) per call** — Converted to `HashSet<string>` for O(1) lookups during file operation validations.
  - [x] **`IsIdentifier` in `Parser.cs` is a 35-line method with many special cases** — Refactored to use a centralized `HashSet<TokenType>` for O(1) contextual identifier resolution.
  - [x] **`color:` prefix in report option keys** — Implemented a safe resolution helper to prevent duplicate prefixes.

  #### 🧪 Testing Gaps

  - [x] **No round-trip test for SpillStore encryption + compression** — write rows, read back, assert values match. Test each combination: encrypt+compress, encrypt only, compress only, neither.

  - [x] **No test for `ExternalJoinEngine` with `PartitionCount = 1`** — degenerate case; entire dataset in one partition.

  - [x] **No test for NULL values in join keys / sort keys** — `CompoundKey` handles NULLs, but the external engines' `UnwrapJsonValue` path strips NULLs differently than the in-memory path. Verify parity.

  - [x] **No test for LEFT JOIN correctness via `ExternalJoinEngine`** — the join-type check (`Contains("LEFT")`) is a string scan. Verify it works for `LEFT JOIN`, `LEFT OUTER JOIN`, and doesn't falsely trigger for other types.

  - [x] **No test for `ExternalSortEngine` with DESC / mixed-direction ORDER BY** — only ascending is verified in existing tests.

  - [x] **No test for `SpillSecurityRule` warning on `SET SPILL_ENCRYPTION OFF` inside nested block** — rule walks blocks recursively; confirm it fires at any depth.

  - [x] **No test for `ExternalAggregateEngine` partition index overflow (int.MinValue hash)** — verify the fix doesn't regress and the edge case is covered.

  #### 🔴 High Priority — Full Codebase Review

  - [x] ~~**SQL injection in `QueryCompiler.cs`** — variable values are interpolated directly into SQL strings via string concatenation when compiling expressions for pushdown to remote sources (MSSQL, Postgres, Oracle). If a variable contains SQL special characters, arbitrary SQL can be injected into the remote query. Must use parameterized queries or a proper escaping layer.~~

  - [x] ~~**Ambiguous column resolution in `ExpressionEvaluator.ResolveIdentifierFallback`** — when a qualified name like `s.date` is not found, the evaluator falls back to returning any unqualified `date` column from any table in scope. With multiple tables having the same column name, the wrong value is silently returned. This is a correctness bug — silent wrong data is worse than an error.~~

  - [x] ~~**`CryptoUtils.Encrypt()` does not validate null password** — if a null password is passed, `Rfc2898DeriveBytes.Pbkdf2()` throws an unhandled exception that leaks a stack trace to the user. Validate and throw a meaningful error before the crypto call.~~

  - [x] ~~**Pushdown lineage regex in `Ast.cs` is incomplete** — `GetSourceTables()` uses a regex `(?i)\bFROM|JOIN\s+([\[\]\w\.-]+)` that misses subqueries in FROM, CTEs, and aliases. Lineage data is silently wrong for any non-trivial pushdown query. Affects audit and data governance.~~

  - [x] ~~**`FileOperationStatementHandler` — path traversal not fully verified** — `ValidatePath()` and `ValidateWriteAccess()` are called, but it is not confirmed that `..` sequences and symlinks are resolved and blocked before the path check. If `ResolvePath` does not canonicalize first, a relative traversal escapes the sandbox.~~

  - [x] ~~**`CreateConnectionStatementHandler` — timing/messaging leak on decryption retry** — distinct error messages and retry order (master password first, script password second) allow an attacker to infer which credential tier succeeded or failed. Use a single generic failure message regardless of which credential path was attempted.~~

  #### 🟡 Medium Priority — Full Codebase Review

  - [x] ~~**`SubqueryCache` in `Evaluator` is unbounded and never evicted** — replaced plain `Dictionary` with `LruCache<Statement, object?>` (capacity 500) using a doubly-linked list + dictionary for O(1) eviction. Located in `ETL-SQL.Core/Common/LruCache.cs`.~~

  - [x] ~~**`SessionStateManager.SaveSession()` has no size limit** — Fixed by implementing a 200MB `MaxSessionSize` limit and `SET MAX_SESSION_SIZE` statement.~~

  - [x] ~~**`TransactionManager` snapshots leak if transaction is never committed or rolled back** — if a script throws after `BEGIN TRANSACTION` but before `COMMIT`/`ROLLBACK`, the snapshot stack is never cleaned up. Memory grows over long TUI sessions with multiple aborted transactions.~~

  - [x] ~~**`ParallelStatementHandler` result merge order is non-deterministic** — Implemented strict index-based sorting (`OrderBy(r => r.index)`) before merging results back to context.~~

  - [x] **`AggregateEngine` CUBE expansion is O(2^n)** — added warning threshold (64) and fixed hard cap logic (1024) in `ExpandGroupingSets()`.

  - [x] **`Lexer` keyword dictionary rebuilt per instance** — Converted to `static readonly` and initialized once via `LanguageMetadata`.
  - [x] **`TypeConverter` type casts throw unhandled exceptions to users** — Wrapped conversions in `ExecutionException` with detailed value/type context; verified with unit tests.

  - [x] **`JsonExtractor` loads entire document into memory** — Refactored to `SerializeAsyncEnumerable` or streaming reader.

  - [x] **`SelectStatementHandler` — result cap at 50k rows is silent** — implemented `DataTable.IsCapped = true` to ensure consistent memory management and visibility for partial results.

  #### 🟢 Low Priority — Full Codebase Review

  - [x] **`IExecutionContext` is a god interface** — Decomposed into property-based sub-context accessors (VarContext, EvaluationContext, etc.) to satisfy interface segregation.

  - [x] **`CryptoUtils` crypto parameters are hardcoded** — Implemented versioned encryption envelopes (prefixed with 0x01) to support future parameter upgrades without breaking legacy data.

  - [x] **`CredentialLeakRule` sensitive keyword list is not configurable** — Moved to configuration so teams can add domain-specific tokens.

  - [x] **`EmailStatementHandler` does not validate email address format** — Added regex validation for email inputs.

  - [x] **`TempFileHelper.SafeDelete()` swallows all exceptions silently** — added detailed Debug-level logging for both successful deletions and cases where the file was not found.

  - [x] **`SessionStateManager` session data is not compressed** — Implemented GZip compression with a 'COMP:' prefix marker, significantly reducing the storage footprint for large sessions.

  - [x] **`Program.cs` — scheduler failure causes hard startup abort** — if `scheduler.Start()` throws, the entire application exits. The scheduler is not a mandatory component for single-script headless execution. Catch the exception, log it, and continue with the scheduler disabled.

  - [x] **`ConnectionStringBuilder` provider name is not validated upfront** — Implemented upfront validation with fuzzy-match suggestions for misspelled connectors.

  - [x] **`Ast.cs` — boilerplate `ToSql()` on every statement record** — dozens of records implement `ToSql() => AstSerializer.Format(this)` identically. Extract to a base class or default interface method to eliminate the repetition. (Batch 12)

  #### 🧪 Testing Gaps — Full Codebase Review

  - [x] **`CryptoUtils` RSA / SSH key pair round-trip** — no visible tests for SSH key pair generation, encryption with passphrase, decryption, or handling of corrupted key data. (Batch 12)

  - [x] **`SecurityService` path traversal and host validation** — no tests for `..` sequences, symlink resolution, IPv6 addresses, or international domain names in the host allowlist. (Batch 12)

  - [x] **`ParallelStatementHandler` race conditions** — no tests for concurrent variable writes, partial branch failures, or merged result ordering.

  - [x] **`TransactionManager` nested transactions and rollback correctness** — no tests for nested `BEGIN TRANSACTION` blocks, rollback on exception, or mixed in-memory / remote source transactions. (Batch 12)

  - [x] **`CredentialLeakRule` adversarial input** — no tests for obfuscated credentials (unicode escapes, string concatenation across lines, encoded values).

  - [x] **`AggregateEngine` CUBE/ROLLUP with large column counts** — no test that verifies the engine handles or rejects GROUP BY CUBE with 10+ columns.

- [x] **Remove @@dataSet** This does nothing that a temp table doesn't already do, confusing to have it.  Remove from any documentation.  Check all *.md documents for the use of @@dataset and replace it with #temp tables.

- [x] **CREATE STYLE**  I think we missed the word CREATE when making a STYLE so we need to add that to the syntax it should be CREATE STYLE <name> (<options>).  This is wrong in the Report_SQL_Guide.md

- [x] **ALTER CHART, PAGE, CONTAINER, STYLE, NAVIGATION, DATASET**  Implemented parser and handlers for partial object updates using the `with` record mutation pattern. Standardized on `VISUAL` terminology.

- [x] **CREATE OR ALTER CHART, PAGE, CONTAINER, STYLE, NAVIGATION, DATASET**  Added `ObjectCreationMode` and updated all creation handlers to support automatic existence detection and replacement.

- [x] **Need STYLE samples** We need some CREATE STYLE samples in the Report_SQL_Guide.md and the Report_Cookbook.md.

- [x] **CREATE BUTTON**  Implemented `CREATE BUTTON` syntax with support for types like `BACK` and `REFRESH`, including `ACTION` integration and full lifecycle support (ALTER/DROP).

- [x] **Need a TOOLTIP option in all report objects**  Integrated `TOOLTIP` parsing and property storage into `CreateVisualStatement`, `CreatePageStatement`, and `CreateButtonStatement`.

- [x] **Need DROP CHART, PAGE, CONTAINER, STYLE, NAVIGATION, DATASET**  Implemented `DropReportObjectStatementHandler` to permanently remove objects from the execution context via script command.

- [x] **Create our own style templates**  Implemented full `TEMPLATE` lifecycle (CREATE, ALTER, DROP) with JSON persistence to a dedicated folder. Supports template inheritance and overriding.

- [x] **Capitalize STYLE properties** Normalized all common style properties to uppercase (COLOR, BACKGROUND-COLOR, etc.) in Documentation and Reference guides. 
- [x] **HELP additions** Expanded HELP system to include detailed documentation for CONNECTION, FUNCTION, REPORT (VISUAL, PAGE, etc.), SET reaching, and @@variables.

- [x] **TUI messages pane** The TUI messages pane now automatically scrolls to show the latest messages after script execution.