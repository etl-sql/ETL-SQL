# ETL-SQL Development Roadmap

## Up Next
- [ ] **Code check** — findings below, implement separately per priority.

  #### 🔴 High Priority

  - [ ] **Duplicate `UnwrapJsonValue` / `JsonElementToValue`** — identical logic copied into all four external engines (`ExternalJoinEngine`, `ExternalSortEngine`, `ExternalAggregateEngine`, `ExternalWindowEngine`) and `SpillStore`. Any fix or precision change must be made in five places. Extract to a shared `SpillSerializationHelper` static class in `ETL-SQL.Engine/Spill/`.

  - [ ] **Integer overflow in `ExternalAggregateEngine` partition hashing** — `Math.Abs(rawHash)` at the partition index calculation: `Math.Abs(int.MinValue)` returns `int.MinValue` (negative), so the subsequent `% PartitionCount` produces a negative index, causing `IndexOutOfRangeException` or writing to wrong partition. Fix: use `(rawHash & 0x7FFFFFFF) % PartitionCount` (same pattern already used correctly in `ExternalJoinEngine`).

  - [x] ~~**`PartitionCount = 0` not validated in `ExternalJoinEngine`** — if `appsettings.json (app settings)` sets `ExternalHashPartitions = 0`, line 76 causes `DivideByZeroException` at runtime with no diagnostic. Add a guard in constructor or at call site.~~

  - [ ] **`ExternalJoinEngine` loads full partitions into memory** — `await leftReader.AsEnumerableAsync().ToListAsync()` at lines 46-47 materializes the entire partition. This defeats the purpose of partitioning for large data. For very large partitions, this could OOM. Should stream-process in the hash-build phase.

  - [ ] **Path safe-zone check is case-insensitive on all platforms** — `SecurityService.CheckRunawayProtection` uses `StartsWith(..., OrdinalIgnoreCase)` for path matching. On Linux (case-sensitive filesystem), this lets `/etc/` match `/ETC/` and vice-versa. Should be `OrdinalIgnoreCase` on Windows and `Ordinal` on Unix.

  - [ ] **`_operationCount` in `Evaluator` is not thread-safe** — the field is a plain `int` incremented without `Interlocked`. Under concurrent `EvaluateStatement` calls (e.g. from `PARALLEL` blocks), count can be corrupted. Use `Interlocked.Increment`.

  #### 🟡 Medium Priority

  - [ ] **Bare `catch { }` in `SpillStore.Cleanup()`** — silently swallows all cleanup exceptions (line 54). At minimum, log a warning so orphaned temp directories can be investigated.

  - [ ] **`__SORT_KEYS` column could collide with user data** — `ExternalSortEngine` attaches sort keys to each row via a synthetic column named `__SORT_KEYS`. If the query has a column with that name, it is silently overwritten. Use a name that cannot appear in user data (e.g. `\0__SORT_KEYS`) or pass sort keys out-of-band.

  - [ ] **`SpillSecurityRule` recursion is unbounded** — the rule walks nested `BlockStatement` trees without a depth limit. A deeply nested script could cause a stack overflow in the linter. Add a depth counter and bail out after a reasonable limit (e.g. 50).

  - [ ] **`ExternalWindowEngine.WindowSignature` hash/equals inconsistency** — the record uses a custom `Equals` but `GetHashCode` hashes expression strings. Two `WindowSignature` instances that are `Equals` must produce the same hash code — verify this holds for all fields, particularly `PartitionBy` and `OrderBy` expression lists.

  - [ ] **`ExternalSortEngine` chunk counter not reset between calls** — `_chunkCounter` is an instance field. If the same `ExternalSortEngine` is reused across queries (unlikely but possible), chunk names accumulate across calls. Reset at the start of `SortExternal`.

  - [ ] **`StatementParser.Report.cs ParseCreateVisual` is 150+ lines** — the method parses source, mappings, options, axis blocks, colors, series, actions, overlays, and formatting. Any new visual feature requires editing this monolith. Break into private `ParseVisualSource`, `ParseVisualMappings`, `ParseVisualOptions`, etc.

  - [ ] **`ExternalWindowEngine` uses `FirstOrDefault()` scan per window function** — line 81 scans the existing signature list linearly on every row. For scripts with many window functions, this is O(k·n). Use a `Dictionary<WindowSignature, WindowGroup>`.

  #### 🟢 Low Priority / Simplification

  - [ ] **`Encoding.UTF8.GetByteCount(json)` called per row in `SpillStore`** — line 111 measures byte count purely for telemetry (`TotalSpilledBytes`). This is O(n) per row. Use `Encoding.UTF8.GetByteCount(json)` only if already computed, or accumulate the stream position delta instead.

  - [ ] **`SecurityService` `ApprovedSafeZones.Any()` is O(n) per call** — called on every file operation check. If the safe zones list grows, this is inefficient. Use `HashSet<string>` (case-normalized at insert time) for O(1) lookup.

  - [ ] **`IsIdentifier` in `Parser.cs` is a 35-line method with many special cases** — every new contextual keyword needs a manual range check update. Consider building a `HashSet<TokenType>` of allowed-as-identifier tokens at startup from attributes or a registry, rather than hardcoded ranges and special cases.

  - [ ] **`color:` prefix in report option keys** — `StatementParser.Report.cs` line 329 concatenates `"color:" + colorKey`. If a user somehow passes `colorKey = "color:primary"`, the resulting key is `"color:color:primary"`. Add a guard or use a different separator.

  #### 🧪 Testing Gaps

  - [ ] **No round-trip test for SpillStore encryption + compression** — write rows, read back, assert values match. Test each combination: encrypt+compress, encrypt only, compress only, neither.

  - [ ] **No test for `ExternalJoinEngine` with `PartitionCount = 1`** — degenerate case; entire dataset in one partition.

  - [ ] **No test for NULL values in join keys / sort keys** — `CompoundKey` handles NULLs, but the external engines' `UnwrapJsonValue` path strips NULLs differently than the in-memory path. Verify parity.

  - [ ] **No test for LEFT JOIN correctness via `ExternalJoinEngine`** — the join-type check (`Contains("LEFT")`) is a string scan. Verify it works for `LEFT JOIN`, `LEFT OUTER JOIN`, and doesn't falsely trigger for other types.

  - [ ] **No test for `ExternalSortEngine` with DESC / mixed-direction ORDER BY** — only ascending is verified in existing tests.

  - [ ] **No test for `SpillSecurityRule` warning on `SET SPILL_ENCRYPTION OFF` inside nested block** — rule walks blocks recursively; confirm it fires at any depth.

  - [ ] **No test for `ExternalAggregateEngine` partition index overflow (int.MinValue hash)** — verify the fix doesn't regress and the edge case is covered.

  #### 🔴 High Priority — Full Codebase Review

  - [ ] **SQL injection in `QueryCompiler.cs`** — variable values are interpolated directly into SQL strings via string concatenation when compiling expressions for pushdown to remote sources (MSSQL, Postgres, Oracle). If a variable contains SQL special characters, arbitrary SQL can be injected into the remote query. Must use parameterized queries or a proper escaping layer.

  - [ ] **Ambiguous column resolution in `ExpressionEvaluator.ResolveIdentifierFallback`** — when a qualified name like `s.date` is not found, the evaluator falls back to returning any unqualified `date` column from any table in scope. With multiple tables having the same column name, the wrong value is silently returned. This is a correctness bug — silent wrong data is worse than an error.

  - [ ] **`CryptoUtils.Encrypt()` does not validate null password** — if a null password is passed, `Rfc2898DeriveBytes.Pbkdf2()` throws an unhandled exception that leaks a stack trace to the user. Validate and throw a meaningful error before the crypto call.

  - [ ] **Pushdown lineage regex in `Ast.cs` is incomplete** — `GetSourceTables()` uses a regex `(?i)\bFROM|JOIN\s+([\[\]\w\.-]+)` that misses subqueries in FROM, CTEs, and aliases. Lineage data is silently wrong for any non-trivial pushdown query. Affects audit and data governance.

  - [ ] **`FileOperationStatementHandler` — path traversal not fully verified** — `ValidatePath()` and `ValidateWriteAccess()` are called, but it is not confirmed that `..` sequences and symlinks are resolved and blocked before the path check. If `ResolvePath` does not canonicalize first, a relative traversal escapes the sandbox.

  - [ ] **`CreateConnectionStatementHandler` — timing/messaging leak on decryption retry** — distinct error messages and retry order (master password first, script password second) allow an attacker to infer which credential tier succeeded or failed. Use a single generic failure message regardless of which credential path was attempted.

  #### 🟡 Medium Priority — Full Codebase Review

  - [ ] **`SubqueryCache` in `Evaluator` is unbounded and never evicted** — the cache is a plain `Dictionary<Statement, object?>` with no size limit, LRU eviction, or TTL. Long-running TUI or Orchestrator sessions accumulate entries indefinitely. Add a capacity cap with LRU eviction.

  - [ ] **`SessionStateManager.SaveSession()` has no size limit** — global variables and connection state are serialized to disk without any size validation. A session with large result sets or binary blobs can exhaust disk. Add a size check or strip result data before serializing.

  - [ ] **`TransactionManager` snapshots leak if transaction is never committed or rolled back** — if a script throws after `BEGIN TRANSACTION` but before `COMMIT`/`ROLLBACK`, the snapshot stack is never cleaned up. Memory grows over long TUI sessions with multiple aborted transactions.

  - [ ] **`ParallelStatementHandler` result merge order is non-deterministic** — `Task.WhenAll()` merges results in completion order, not submission order. Scripts that depend on result order from PARALLEL blocks will fail intermittently. Either document this explicitly or sort by task index after `WhenAll`.

  - [ ] **`AggregateEngine` CUBE expansion is O(2^n)** — `ExpandGroupingSets()` for a CUBE on n columns generates 2^n grouping sets. A GROUP BY CUBE with 20 columns produces over 1 million grouping sets. No guard or warning exists. Add a hard cap (e.g. warn above 16 sets, refuse above 1024).

  - [ ] **`Lexer` keyword dictionary rebuilt per instance** — `InitializeKeywords()` is called in each constructor invocation. The dictionary is immutable after construction and should be a `static readonly` field shared across all instances.

  - [ ] **`TypeConverter` type casts throw unhandled exceptions to users** — `Convert.ToInt32(v)` and similar conversions in type cast lambdas throw `InvalidCastException` or `OverflowException` with no user-facing message. Wrap each in a try-catch that re-throws as `ExecutionException` with the column name and attempted value.

  - [ ] **`JsonExtractor` loads entire document into memory** — `JsonDocument.Parse(stream)` buffers the full file. For multi-GB JSON sources this causes OOM. Should use `JsonSerializer.DeserializeAsyncEnumerable` or a streaming reader.

  - [ ] **`SelectStatementHandler` — result cap at 50k rows is silent** — rows beyond 50k are streamed but not buffered in `LastResult`. The log message fires once. A user running `SELECT * FROM large_table` will see 50k rows in the result panel without any UI-level indicator that rows were cut. Emit a visible warning in the result header.

  #### 🟢 Low Priority — Full Codebase Review

  - [ ] **`IExecutionContext` is a god interface** — the interface is 200+ lines combining 11 sub-interfaces (`IVariableContext`, `IQueryContext`, `ILineageContext`, etc.). Handlers should receive only the sub-interface they need (already the design intent, but `Evaluator` downcasts are common). Enforce the pattern — no handler should accept `IExecutionContext` directly if a narrower interface suffices.

  - [ ] **`CryptoUtils` crypto parameters are hardcoded with no versioning** — `Iterations = 600000`, `KeySize = 256`, `SaltSize = 16` are compile-time constants. If parameters are ever upgraded, old encrypted data cannot be transparently re-encrypted. Store the parameter version as a prefix byte in the encrypted output (standard envelope pattern).

  - [ ] **`CredentialLeakRule` sensitive keyword list is not configurable** — the static array of sensitive keywords (`password`, `secret`, `key`, etc.) cannot be extended without a code change. Move to configuration so teams can add domain-specific tokens (`api_secret`, `oauth_token`, `bearer`).

  - [ ] **`TempFileHelper.SafeDelete()` swallows all exceptions silently** — if a temp file cannot be deleted due to a permission error (not just a transient lock), the failure is invisible. Over time, `/tmp` fills with orphaned files. Log at `Warning` level at minimum.

  - [ ] **`EmailStatementHandler` does not validate email address format** — malformed addresses are passed directly to the SMTP provider, which rejects them late with a provider-specific error. Validate with a simple regex or `MailAddress` constructor before sending.

  - [ ] **`SessionStateManager` session data is not compressed** — session state is written to disk uncompressed. In large sessions (hundreds of MB of variable data), compression would significantly reduce I/O and storage. GZip compression before encryption is already used in SpillStore — apply the same pattern here.

  - [ ] **`Program.cs` — scheduler failure causes hard startup abort** — if `scheduler.Start()` throws, the entire application exits. The scheduler is not a mandatory component for single-script headless execution. Catch the exception, log it, and continue with the scheduler disabled.

  - [ ] **`ConnectionStringBuilder` provider name is not validated upfront** — a misspelled provider name produces a generic late error. Validate against the known provider list at parse/build time and emit a `did you mean?` suggestion.

  - [ ] **`Ast.cs` — boilerplate `ToSql()` on every statement record** — dozens of records implement `ToSql() => AstSerializer.Format(this)` identically. Extract to a base class or default interface method to eliminate the repetition.

  #### 🧪 Testing Gaps — Full Codebase Review

  - [ ] **`CryptoUtils` RSA / SSH key pair round-trip** — no visible tests for SSH key pair generation, encryption with passphrase, decryption, or handling of corrupted key data.

  - [ ] **`SecurityService` path traversal and host validation** — no tests for `..` sequences, symlink resolution, IPv6 addresses, or international domain names in the host allowlist.

  - [ ] **`ParallelStatementHandler` race conditions** — no tests for concurrent variable writes, partial branch failures, or merged result ordering.

  - [ ] **`TransactionManager` nested transactions and rollback correctness** — no tests for nested `BEGIN TRANSACTION` blocks, rollback on exception, or mixed in-memory / remote source transactions.

  - [ ] **`CredentialLeakRule` adversarial input** — no tests for obfuscated credentials (unicode escapes, string concatenation across lines, encoded values).

  - [ ] **`AggregateEngine` CUBE/ROLLUP with large column counts** — no test that verifies the engine handles or rejects GROUP BY CUBE with 10+ columns.

- [x] **Remove @@dataSet** This does nothing that a temp table doesn't already do, confusing to have it.  Remove from any documentation.  Check all *.md documents for the use of @@dataset and replace it with #temp tables.

- [x] **CREATE STYLE**  I think we missed the word CREATE when making a STYLE so we need to add that to the syntax it should be CREATE STYLE <name> (<options>).  This is wrong in the Report_SQL_Guide.md

- [ ] **ALTER CHART, PAGE, CONTAINER, STYLE, NAVIGATION, DATASET**  Just like ALTER works in a query you can ALTER the items above.  Using ALTER only changes what is changed in the ALTER statement.

- [ ] **CREATE OR ALTER CHART, PAGE, CONTAINER, STYLE, NAVIGATION, DATASET**  Just like CREATE OR ALTER in a query but in this case it CREATES if it doesn't exists or ALTERS it if it does but in this case ALTER recreates it and does not use any existing options or settings.

- [x] **Need STYLE samples** We need some CREATE STYLE samples in the Report_SQL_Guide.md and the Report_Cookbook.md.

- [ ] **CREATE BUTTON** We need buttons in reports.  CREATE BUTTON <name> AS <button type> (<options>).  This will have an ACTION option.  Will also need the ALTER, CREATE OR ALTER, and DROP commands for this.  Possible button types: BACK, REFRESH, HELP, ...

- [ ] **Need a TOOLTIP option in all report objects**  TOOLTIP = '<string>' or TOOLTIP (<container object with charts> or <markdown>)

- [ ] **Need DROP CHART, PAGE, CONTAINER, STYLE, NAVIGATION, DATASET** This removes the object.  With our ACTION, likely from a button this could remove these objects. 

- [ ] **Create our own style templates**  Need a way to create our own style templates that can be reused.  These will have to save as a file that can be imported.  Thinking we'll need a custom template folder.  When checking for templates the code will look at the Echart ones and the ones in the custom template folder.  CREATE TEMPLATE <name> ( <options>);  Need a way to ALTER, CREATE OR ALTER, and DROP to remove.