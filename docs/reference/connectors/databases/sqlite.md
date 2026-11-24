# SQLITE

Connects to local or in-memory SQLite databases using the lightweight Microsoft.Data.Sqlite driver.
Supports local transactions, schema inspection, and data loading.

Aliases: `SQLITE3`

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `DATABASE` | File path to the SQLite database or `:memory:` | Yes (structured form) |
| `JOURNAL_MODE` | SQLite journal mode applied on every connection open (`DELETE`, `TRUNCATE`, `PERSIST`, `WAL`, `MEMORY`, `OFF`) | No |
| `BUSY_TIMEOUT_MS` | Non-negative lock wait timeout in milliseconds (`PRAGMA busy_timeout`) applied on every connection open | No |
| `TIMEOUT_SECONDS` | Command/query execution timeout in seconds (default: `30`) | No |
| `TABLE` | Default table context for unqualified operations | No |

> [!NOTE]
> Use either structured options (`DATABASE`, `TIMEOUT_SECONDS`, `TABLE`) or a positional
> connection string (`'Data Source=...;Mode=ReadOnly;'`), not both.

> [!IMPORTANT]
> SQLite files are not encrypted by this connector. Protect sensitive databases with filesystem and
> volume encryption. `PASSWORD` is intentionally unsupported because the shipped native SQLite library
> is not SQLCipher.

## Authentication

SQLite is an embedded, file-based database with no username/password authentication:
- **File System Permissions**: Access is governed by local file system permissions of the executing process. Avoid embedding sensitive paths inline by using `'${SQLITE_DB_PATH}'` (with `Security:AllowedEnvVars`), `'SECRET:...'`, or `'ENC:...'`.
- **Read-Only Mode**: Pass `Mode=ReadOnly;` in the positional connection string form to prevent any disk modifications.

## Examples

```sql
-- Standard unencrypted in-memory database
CREATE CONNECTION local_mem AS SQLITE(DATABASE=':memory:');

-- Standard unencrypted file-based database using an allow-listed environment variable (Security:AllowedEnvVars)
CREATE CONNECTION local_db AS SQLITE(DATABASE='${SQLITE_DB_PATH}', JOURNAL_MODE='WAL', BUSY_TIMEOUT_MS=5000, TIMEOUT_SECONDS=30);

-- Traditional connection string form (read-only mode)
CREATE CONNECTION legacy_db AS SQLITE('Data Source=C:\Data\legacy.db;Mode=ReadOnly;');
```

## Write Path & Transactions

- **Bulk / write path** — Cross-source `INSERT INTO` and `COPY INTO` run a single prepared parameterized `INSERT INTO ... VALUES ($p0, ...)` per row inside one transaction (either the active engine transaction or a local `SqliteTransaction` created for the write call). Same-connection `INSERT INTO ... SELECT` pushes down as native SQLite SQL. `COPY INTO ... APPEND=FALSE` runs `DELETE FROM` (SQLite has no `TRUNCATE TABLE`) inside the same transaction before inserting.
- **`MERGE` behavior** — Engine-emulated. `MERGE` against a `SQLITE` target evaluates in bounded engine memory and rewrites the target via `DELETE FROM` + parameterized `INSERT` inside a transaction. For larger tables, load `#delta` into a staging table and run SQLite's native `INSERT INTO ... ON CONFLICT (...) DO UPDATE SET ...` inside `EXECUTE local_db BEGIN ... END`.
- **Transactions** — Full `ITransactionalDataSource` support (`BEGIN TRANSACTION` / `COMMIT` / `ROLLBACK`). SQLite is **single-writer**: only one writer transaction can hold the database lock at a time, so keep write transactions short to avoid `SQLITE_BUSY`.
- **`WHAT_IF` behavior** — Under `SET WHAT_IF ON`, `WriteBatches` and `TruncateAsync` return without modifying the database file. Pushed-down DML and `EXECUTE` blocks log the compiled SQL without executing it.
- **Idempotent-load pattern** — Wrap staging + native `INSERT ... ON CONFLICT DO UPDATE` (or `DELETE` + reload of a partition key) inside `BEGIN TRANSACTION ... COMMIT`.

## Troubleshooting

- **Database Locked (SQLITE_BUSY)**: Multiple processes or connections writing concurrently. Set `BUSY_TIMEOUT_MS` (e.g., `5000`) and `JOURNAL_MODE='WAL'` on `CREATE CONNECTION` so writers wait instead of failing immediately, and keep write transactions short.
- **File Not Found**: Verify path with `IExecutionContext.ResolvePath()`. Relative paths resolve against script root.
- **Permissions Error**: Ensure write access to directory for WAL journal files.

## References

- [Database Connectors](README.md)
- [Connectors](../README.md)
