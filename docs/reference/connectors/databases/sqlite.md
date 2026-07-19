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

> [!IMPORTANT]
> SQLite files are not encrypted by this connector. Protect sensitive databases with filesystem and
> volume encryption. `PASSWORD` is intentionally unsupported because the shipped native SQLite library
> is not SQLCipher.

## Examples

```sql
-- Standard unencrypted in-memory database
CREATE CONNECTION local_mem AS SQLITE(DATABASE=':memory:');

-- Standard unencrypted file-based database
CREATE CONNECTION local_db AS SQLITE(DATABASE='C:\Data\local.db', TIMEOUT_SECONDS=30);

-- Traditional connection string form
CREATE CONNECTION legacy_db AS SQLITE('Data Source=C:\Data\legacy.db;Mode=ReadOnly;');
```

## References

- [Database Connectors](README.md)
- [Connectors](../README.md)
