# SQLITE

Connects to local or in-memory SQLite databases using the lightweight Microsoft.Data.Sqlite driver.
Supports local transactions, schema inspection, and data loading.

Aliases: `SQLITE3`

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `DATABASE` | File path to the SQLite database or `:memory:` | Yes (structured form) |
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
CREATE CONNECTION local_db AS SQLITE(DATABASE='${SQLITE_DB_PATH}', TIMEOUT_SECONDS=30);

-- Traditional connection string form (read-only mode)
CREATE CONNECTION legacy_db AS SQLITE('Data Source=C:\Data\legacy.db;Mode=ReadOnly;');
```

## Troubleshooting

- **Database Locked (SQLITE_BUSY)**: Multiple processes writing concurrently. Keep writes serialized or shorten transaction scopes; SQLite is single-writer.
- **File Not Found**: Verify path with `IExecutionContext.ResolvePath()`. Relative paths resolve against script root.
- **Permissions Error**: Ensure write access to directory for WAL journal files.

## References

- [Database Connectors](README.md)
- [Connectors](../README.md)
