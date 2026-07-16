# SQLITE
Connects to local or in-memory SQLite databases using the lightweight Microsoft.Data.Sqlite driver. Supports local transactions, schema inspection, and data loading.

## Syntax

```sql
CREATE CONNECTION <name> AS SQLITE(
  DATABASE        = 'C:\data\mydb.db',
  TIMEOUT_SECONDS = 30
);

-- Or in-memory database:
CREATE CONNECTION <name> AS SQLITE(
  DATABASE        = ':memory:'
);
```

## Options

- **Alias: SQLITE3** — accepted connector token for compatibility
- **DATABASE = 'path'** — file path to SQLite database file or ':memory:' (defaults to ':memory:' if empty)
- **TIMEOUT_SECONDS = n** — command/query execution timeout in seconds (default 30)
- **TABLE = 'name'** — default table for unqualified SELECT/INSERT operations

SQLite files are not encrypted by this connector. Use filesystem or volume encryption for sensitive
data. The shipped native SQLite library is not SQLCipher, so `PASSWORD` is not accepted.

## Examples

```sql
-- Create an in-memory SQLite database connection
CREATE CONNECTION LocalCache AS SQLITE(
  DATABASE = ':memory:'
);

-- SQLite connector supports transactions and DDL/DML pushdown
BEGIN TRANSACTION;

-- Create table in SQLite
EXECUTE LocalCache BEGIN
  CREATE TABLE items (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL,
    price REAL
  );
END;

-- Stage data in SQLite
SELECT 1 AS id, 'Widget A' AS name, 19.99 AS price INTO #stage;
SELECT 2 AS id, 'Widget B' AS name, 49.99 AS price INTO #stage;

-- Bulk copy staged data into SQLite table
SELECT * FROM #stage INTO LocalCache.items;

COMMIT;

-- Query SQLite database
SELECT id, name, price 
  INTO #results 
  FROM LocalCache.items
  WHERE price > 20.0;
```

## References
- [Data Connectors](../../../guides/administration.md)
