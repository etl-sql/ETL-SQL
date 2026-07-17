# SHOW TABLES
Displays tables available on a connection or in the engine session.

## Syntax
```sql
SHOW TABLES [AT <conn>] [INTO #table];
```

## Parameters
- **AT conn** — Optional. Specifies the connection to list tables from. When omitted, lists engine-side `#temp` tables.
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
A result set with table name, schema, and type for each table on the target connection.

## Example
```sql
-- List tables on a connection
SHOW TABLES AT SalesDB;

-- Capture and filter for a naming pattern
SHOW TABLES AT SalesDB INTO #tbl_list;
SELECT table_name FROM #tbl_list WHERE table_name LIKE 'Order%';

-- List current engine temp tables
SHOW TABLES;
```

## Notes
- The columns returned depend on the connector type. SQL connectors typically include schema and table type information.
- File connectors may list available files as tables.

## References
- [SHOW Commands](README.md)
