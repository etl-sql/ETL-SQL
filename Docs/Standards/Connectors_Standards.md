# ETL-SQL Connectors Engineering Standards

**Version 1.0 — Established with the IConnector / IDataSource / IDatabaseSource architecture**

This document is the authoritative standard for all work that touches any data connector in the ETL-SQL ecosystem. It defines rules that are non-negotiable and must be met by any change, any new connector, and any future version of the data access layer.

When in doubt about whether a change is acceptable: if it would require you to violate any rule in this document, the design is wrong. Rethink the design.

---

## Part I — The Inviolable Rules

These rules exist because previous violations caused security incidents, data corruption, and silent performance regressions. They are not style preferences — they are load-bearing constraints.

### Rule 1: The Engine Executes SQL. Connectors Move Data.

Nothing in a connector implementation may evaluate SQL, interpret expressions, or apply filter logic. A connector's only responsibilities are:

1. Translate the raw connection string and options into a live provider session.
2. Stream data in and out in batches.
3. Report metadata (table names, columns, version).

SQL evaluation is the engine's responsibility. Connectors that filter, aggregate, or transform rows internally bypass the engine's lineage tracking and optimizer.

**Violation indicator:** Any `WHERE`, `GROUP BY`, `ORDER BY`, or expression evaluation logic inside an `IDataSource` implementation.

### Rule 2: No Connector May Block an Async Call

All I/O operations — connection establishment, query execution, batch reads, batch writes — must use the `Async` variants of all SDK methods. Blocking calls on async threads exhaust the thread pool and cause latency spikes that affect the entire engine.

```csharp
// CORRECT
await _connection.OpenAsync(cancellationToken);
await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

// INCORRECT — blocking I/O on async thread
_connection.Open();
using var reader = cmd.ExecuteReader();
```

**Violation indicator:** Any `.Result`, `.Wait()`, or `GetAwaiter().GetResult()` inside a connector. Any call to a non-async SDK method where an async version exists.

### Rule 3: Credentials Must Never Leave the Connector Boundary

Connection strings, passwords, API keys, tokens, and any other secret material must be used for connection establishment and immediately discarded. They must never appear in:

- `ILogger` messages at any level
- `ExecutionException` messages
- `OutputMessage.Text` in any category
- Any `Dictionary<string, string>` returned to the engine (options, metadata, etc.)

The `ConnectionString` property on `IDatabaseSource` exists for internal connection renewal only. It is not a channel for exposing credentials to the engine.

**Violation indicator:** Any interpolated string in a log call or exception message that includes a connection string parameter.

### Rule 4: All File I/O Must Go Through ResolvePath

`IExecutionContext.ResolvePath()` is the security guardrail for all file-based connectors. It validates the path, enforces the Zero-Trust blocklist, and applies `ALLOW_*` override permissions. Bypassing it is a security vulnerability — it allows scripts to read system files, write to unintended directories, or escape the working directory sandbox.

```csharp
// CORRECT — security-aware
string safePath = _context.ResolvePath(rawConnectionString);
using var stream = File.OpenRead(safePath);

// INCORRECT — bypasses all security validation
using var stream = File.OpenRead(rawConnectionString);  // SECURITY VULNERABILITY
```

**Violation indicator:** Any direct `File.Open`, `File.Create`, `File.Delete`, `Directory.GetFiles`, or `Directory.EnumerateFiles` call that does not use a path returned by `ResolvePath`.

### Rule 5: All Provider Exceptions Must Be Wrapped

Raw provider exceptions (`SqlException`, `OracleException`, `NpgsqlException`, `IOException`, etc.) must not propagate out of the connector boundary. They must be caught and re-thrown as `ExecutionException` with a sanitized, user-readable message.

```csharp
// CORRECT
catch (SqlException ex)
{
    throw new ExecutionException(
        $"MSSQL error ({ex.Number}): {StripCredentials(ex.Message)}");
}

// INCORRECT — raw exception with internal detail escapes
catch (SqlException ex)
{
    throw;   // Connection string may appear in ex.Message
}
```

The inner exception must NOT be chained. Inner exceptions may contain server paths, connection parameters, or stack traces that violate Rule 3.

**Violation indicator:** Any unhandled provider-specific exception type visible in the Messages tab. Any connection string appearing in error text.

### Rule 6: Sensitive Option Values Must Be Masked in All Metadata Output

`GetSupportedOptions()` declares option keys — including sensitive keys like `PASSWORD`, `API_KEY`, `TOKEN`. When values for these keys are returned in any display context (`SHOW CONNECTION`, `GetOptionValues()`, IDE hover), the value must be replaced with `***`.

The masking rule applies to any option key that contains: `PASS`, `KEY`, `TOKEN`, `SECRET`, `PWD`, `CREDENTIAL`, `AUTH`.

```csharp
// CORRECT — key declared for linter/IDE; value masked when displayed
public Dictionary<string, string[]> GetSupportedOptions() => new()
{
    ["PASSWORD"] = Array.Empty<string>(),  // Free string; value masked at display time
};

// GetOptionValues() — only return safe display values
public Dictionary<string, string[]> GetOptionValues() => new()
{
    ["ENCRYPT"] = new[] { "ON", "OFF" },
    // PASSWORD intentionally omitted — no safe display values
};
```

**Violation indicator:** Any credential value visible in SHOW CONNECTION output or IDE autocomplete suggestions.

### Rule 7: `IDataSource` Must Support O(1) Memory Processing

`ReadBatches()` must yield data in discrete batches. It must never accumulate the entire result set into memory before yielding. The default batch size of 10,000 rows must be respected as the ceiling, not a target — smaller batches are acceptable; larger batches are not.

```csharp
// CORRECT — streaming
public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10_000)
{
    var batch = new DataTable();
    while (await reader.ReadAsync())
    {
        batch.Rows.Add(ReadRow(reader));
        if (batch.Rows.Count >= batchSize)
        {
            yield return batch;
            batch = new DataTable();
        }
    }
    if (batch.Rows.Count > 0) yield return batch;
}

// INCORRECT — full materialization defeats the entire memory model
public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10_000)
{
    var allRows = await LoadAllRowsAsync();   // MEMORY EXHAUSTION RISK
    yield return new DataTable(allRows);
}
```

**Violation indicator:** Any `ToList()`, `ToArray()`, or `await foreach` followed by collection accumulation inside `ReadBatches()`. Memory usage scaling linearly with source row count during `INSERT INTO` or `MERGE` operations.

### Rule 8: SQL-Capable Connectors Must Implement IDatabaseSource

Any connector that targets a SQL-capable engine (relational databases, columnar warehouses, OLAP stores) must implement `IDatabaseSource` and set `SupportsSqlPushdown = true`. Not doing so forces the engine into row-by-row iteration mode, causing 10–100× performance degradation for large datasets.

**Violation indicator:** A relational database connector that does not implement `IDatabaseSource`. Any SQL connector that reads all rows into the engine when a pushdown path is available.

### Rule 9: Dialect Exclusions Must Be Declared

Every connector must implement `GetExcludedKeywords()` to declare which ETL-SQL baseline keywords are invalid in its dialect. This prevents the linter from allowing scripts that would fail at runtime on that connector.

```csharp
// CORRECT — Postgres connector
public HashSet<string> GetExcludedKeywords() =>
    new(StringComparer.OrdinalIgnoreCase) { "TOP", "DATALENGTH", "ISNULL", "GETDATE" };

// INCORRECT — silent runtime failures on Postgres
public HashSet<string> GetExcludedKeywords() => new();  // Returns empty — linter won't catch TOP
```

**Violation indicator:** A relational connector that returns an empty `GetExcludedKeywords()` when its target dialect is known to reject baseline keywords.

### Rule 10: `DisposeAsync` Must Release All Resources

`IDataSource` extends `IAsyncDisposable`. `DisposeAsync()` must close the provider connection, release connection pool slots, close file handles, and cancel any in-progress reads or writes. It must not throw.

**Violation indicator:** Connection pool exhaustion after DROP CONNECTION. File handles remaining open after a script completes.

---

## Part II — Testing Standards

### Rule T1: Every New Connector Must Have a Smoke Test

A smoke test must verify the minimum viable lifecycle:
1. `CreateDataSource()` returns without throwing.
2. `ReadBatches()` yields at least one batch for a known non-empty source.
3. `WriteBatches()` completes without throwing for a batch of known rows.
4. `DisposeAsync()` completes without throwing.

For SQL connectors, use Testcontainers to provision a real database instance. For file connectors, use `Path.GetTempPath()` as the working directory.

### Rule T2: Security-Relevant Paths Must Have Negative Tests

Every connector that accepts file paths must have a test asserting that passing a system path (e.g., `C:\Windows\System32`) causes an `ExecutionException` from `ResolvePath`, not a raw `UnauthorizedAccessException` from the OS.

### Rule T3: Credential Masking Must Be Verified by Test

For every connector that declares a sensitive option key, there must be a test that:
1. Creates the connector with a known password or API key.
2. Calls `GetSupportedOptions()` or the equivalent metadata path.
3. Asserts the credential value does NOT appear in any returned string.

### Rule T4: Exception Wrapping Must Be Verified by Test

For every provider SDK exception type that the connector can receive, there must be a test that:
1. Simulates the provider exception (e.g., wrong credentials, network unreachable).
2. Asserts that an `ExecutionException` is thrown (not the raw provider type).
3. Asserts the `ExecutionException.Message` does not contain the connection string.

### Rule T5: The Regression Gate Must Pass Before Any Merge

The full test suite must pass — including integration tests that exercise the connector in a real or containerized environment — before a connector change is considered complete. A connector that compiles but breaks integration tests is not shippable.

---

## Part III — Versioning Standards

### Rule V1: `GetSupportedOptions()` Is Additive

New option keys may be added to `GetSupportedOptions()` and `GetOptionValues()` at any time. Existing keys may not be removed or renamed without a deprecation period, because scripts written against the old key names will fail the linter.

### Rule V2: `GetExcludedKeywords()` Is Additive

New keywords may be added to the exclusion list. Existing exclusions may not be removed — they were added because the target dialect rejected them. Removing an exclusion would cause previously-linted scripts to become invalid at runtime.

### Rule V3: `Aliases` Are Additive

New aliases may be added to `IConnector.Aliases`. Existing aliases may not be removed — scripts referencing the old alias will fail at parse time if it disappears.

---

## Part IV — Data Warehouse Connector Checklist

A connector targeting an analytical data warehouse (Redshift, BigQuery, Snowflake, Databricks, Synapse, Trino, Dremio, etc.) must satisfy all rules in Parts I–III **plus** the following additional requirements.

### DW-1: Override `IsDataWarehouse` and `CommandTimeoutSeconds`

```csharp
public bool IsDataWarehouse => true;
public int CommandTimeoutSeconds => 1800;   // 30 minutes
```

Rationale: warehouse queries routinely scan billions of rows and take minutes. Inheriting the 30-second OLTP default causes false timeouts on first use.

### DW-2: Apply `TIMEOUT_SECONDS` from Options

The connector's `IDataSource` implementation must read `TIMEOUT_SECONDS` from `_options` in its constructor and apply it to every database command:

```csharp
_commandTimeout = options != null
    && options.TryGetValue("TIMEOUT_SECONDS", out var ts)
    && int.TryParse(ts, out var t) ? t : 1800;
```

For ADO.NET-based connectors, set `cmd.CommandTimeout = _commandTimeout;` on every `DbCommand` before execution.

### DW-3: Document `TIMEOUT_SECONDS` in `GetSupportedOptions()`

```csharp
{ "TIMEOUT_SECONDS", Array.Empty<string>() }
```

Users must be able to discover that this option exists via `SHOW CONNECTION HELP <name>`.

### DW-4: `ITransactionalDataSource` Is Optional — Be Explicit

If the warehouse does not support traditional RDBMS transactions (e.g., BigQuery), do **not** implement `ITransactionalDataSource`. The engine gracefully falls back to auto-commit mode.

If the warehouse supports transactions (e.g., Snowflake), implement `ITransactionalDataSource` and hold a dedicated connection for the transaction lifetime.

### DW-5: Schema Introspection via `INFORMATION_SCHEMA`

All warehouse connectors must implement `GetTablesAsync()`, `GetViewsAsync()`, and `GetColumnsAsync(tableName)` via `INFORMATION_SCHEMA` queries. This feeds the LSP schema cache and TUI autocomplete.

### DW-6: `SupportsSqlPushdown = true`

Warehouse connectors must set `SupportsSqlPushdown = true`. They are the primary use case for full SQL pushdown — ETL-SQL generates the SQL and the warehouse executes it.

### DW-7: Backtick or Double-Quote Identifiers — Never Unquoted

All identifier quoting must handle multi-part names (`project.dataset.table` or `schema.table`):
- Snowflake / most warehouses: double-quote each segment (`"schema"."table"`)
- BigQuery: backtick each segment (`` `project`.`dataset`.`table` ``)

### DW-8: Credential File Paths Must Go Through `ResolvePath`

If the connector accepts a file path for credentials (e.g., `CREDENTIAL_FILE`, `PRIVATE_KEY_FILE`), it must resolve the path via `context.ResolvePath(rawPath)` — the same as any other file-I/O connector (Rule 4 of Part I applies here too).

### DW-9: ADC / Workload Identity Support

Cloud warehouse connectors must support credential-less auth via the platform's ambient identity mechanism:
- GCP (BigQuery): Application Default Credentials — omit `CREDENTIAL_FILE`
- Snowflake: Private-key JWT — omit `PASSWORD`, provide `PRIVATE_KEY_FILE`
- Azure (Synapse via ODBC): Managed Identity — use `Authentication=ActiveDirectoryMsi` in the DSN

### DW-10: Streaming vs. Bulk Write Trade-offs Must Be Documented

The connector's `GetHelp()` string and the reference documentation must explain the write mechanism (streaming inserts, bulk load, DML) and its implications for throughput, cost, and atomicity.

### Rule V4: Breaking Changes Require a Transition Period

Any change that removes an option key, renames a connector token, or changes the semantics of an existing interface method requires:

1. A deprecation notice in this document.
2. Dual support (accept both old and new forms simultaneously).
3. Consumer migration (all scripts and tests updated) before the old form is removed.

---

## Part IV — Security Standards

### Rule S1: The Zero-Trust Principle Applies to All Connectors

No connector has the authority to bypass the engine's security service. All path validation, permission checking, and credential decryption flows through `SecurityService`. A connector that implements its own security logic is non-compliant — it creates a gap in the unified security model.

### Rule S2: `ENC:` Decryption Is the Engine's Responsibility

When a connection string arrives at `CreateDataSource()`, any `ENC:` prefix has already been decrypted by `CreateConnectionStatementHandler`. Connectors must not check for or process `ENC:` prefixes themselves. If a connector receives an `ENC:`-prefixed string, it is a bug in the handler — the connector should treat it as a literal connection string, which will cause a readable error.

### Rule S3: Connectors Must Support `SET WHAT_IF` Dry-Run Mode

When the evaluator's `IsWhatIf` flag is true, connectors must not perform any write operations. `WriteBatches()` must be a no-op. `TruncateAsync()` must be a no-op. `ExecuteRawSql()` must not execute DDL or DML statements. Read operations are permitted.

```csharp
public Task WriteBatches(IAsyncEnumerable<DataTable> batches)
{
    if (_context.IsWhatIf) return Task.CompletedTask;  // Dry-run: skip all writes
    // ... normal write logic ...
}
```

**Violation indicator:** Any write or destructive operation executing when `IsWhatIf = true`.

### Rule S4: Security Override Invocations Must Be Logged

When a connector invokes a `ALLOW_*` permission override (e.g., `ALLOW_FILE_TYPE_ACCESS`), it must emit a `MessageCategory.Security` message via the `IOutputSink`. Users must always be informed when a security override was required for their script to execute.

---

## Part V — Platform Consistency Standards

### Rule C1: Connector Behavior Must Be Source-Agnostic

A connector must behave identically whether it is used from the Terminal IDE, the VS Code extension, the `etl-sql` CLI, or the `etl-sql-report` build tool. Connectors must not branch on the calling context. Platform-specific behavior belongs in the presentation layer, not the connector.

### Rule C2: Connector Metadata Must Support Both IDE and CLI Contexts

`GetHelp()`, `GetSupportedOptions()`, `GetOptionValues()`, `GetTablesAsync()`, and `GetColumnsAsync()` must work without an active database connection where possible. The IDE calls these methods for autocomplete before the user has typed a complete connection string.

### Rule C3: Error Messages Must Be Platform-Neutral

`ExecutionException` messages from connectors are plain text with no ANSI codes, no Markdown, and no HTML. Each presentation platform formats errors for its own rendering target.

---

## Compliance Checklist (New Connector)

Use this checklist when reviewing any PR that adds or significantly modifies a connector:

**Interfaces & Registration**
- [ ] Implements both `IConnector` (factory) and `IDataSource` (session)?
- [ ] Implements `IDatabaseSource` if the target is a SQL-capable engine?
- [ ] Implements `ITransactionalDataSource` if the target supports ACID transactions?
- [ ] Registered as a singleton `IConnector` in `DependencyInjectionSetup`?

**Metadata**
- [ ] `GetSupportedOptions()` lists all `WITH` clause keys including sensitive ones?
- [ ] `GetOptionValues()` provides safe display values (no sensitive value defaults)?
- [ ] `GetExcludedKeywords()` lists all baseline keywords the target dialect rejects?
- [ ] `GetHelp()` documents authentication patterns and required vs. optional options?
- [ ] `BuildConnectionString()` implemented for connectors that use standard DSNs?

**Security**
- [ ] All sensitive option values masked as `***` in all metadata and display outputs?
- [ ] All file path I/O goes through `IExecutionContext.ResolvePath()`?
- [ ] No connection strings, passwords, or keys appear in any log or exception message?
- [ ] `ENC:` prefix NOT handled by the connector (engine decrypts before calling `CreateDataSource`)?
- [ ] `WriteBatches()` and `TruncateAsync()` are no-ops when `IsWhatIf = true`?

**Performance**
- [ ] `ReadBatches()` yields in discrete batches ≤ 10,000 rows — never accumulates all rows?
- [ ] SQL-capable target: `SupportsSqlPushdown = true` and `ExecuteRawSql()` implemented?
- [ ] `WriteBatches()` uses the provider's bulk-insert mechanism (not row-by-row INSERT)?

**Resource Management**
- [ ] All SDK calls use async overloads with `CancellationToken` support?
- [ ] No `.Result`, `.Wait()`, or `GetAwaiter().GetResult()` in any code path?
- [ ] `DisposeAsync()` closes connections, file handles, and stream readers — does not throw?

**Error Handling**
- [ ] All provider exceptions caught and re-thrown as `ExecutionException`?
- [ ] Inner exception NOT chained to `ExecutionException` (prevents credential leakage)?
- [ ] Empty source / empty write handled gracefully (no exception for zero rows)?

**Testing**
- [ ] Smoke test: create → read → write → dispose lifecycle passes?
- [ ] Negative test: system path rejected by `ResolvePath` with `ExecutionException`?
- [ ] Credential masking test: password/key value absent from all metadata output?
- [ ] Exception wrapping test: provider exception produces `ExecutionException`, not raw type?

---

*Refer to [Connectors.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Architecture/Connectors.md) for technical implementation details and [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) for language specifications.*
