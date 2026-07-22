# SHOW SCHEMA

Inspects column definitions, data types, nullability constraints, and governance tags for a specified table or dataset.

## Syntax
```sql
SHOW SCHEMA FOR <table> [INTO #result];
```

## Parameters
- **FOR <table>** — Required. The target table, connector reference (`conn.TableName`), engine-side `#temp` table, or dataset (`&dataset`) to inspect.
- **INTO #result** — Optional. Captures the schema result set into a temp table for programmatic introspection.

## Returns
A result set with the following columns:
- `ColumnName` — The column name.
- `DataType` — Data type of the column (`VARCHAR`, `INT`, `DECIMAL`, etc.).
- `IsNullable` — `TRUE` if the column accepts NULL values; `FALSE` otherwise.
- `Tags` — JSON-serialized lineage metadata and governance tags attached to the column (e.g. `{"pii":"true","classification":"confidential"}`).

## Example
```sql
-- Create a tagged temp table
CREATE TABLE #customers (
    customer_id INT /* @d: Customer ID; @pii: false */,
    email VARCHAR(255) /* @pii: true; @classification: Confidential */
);

-- Inspect column schema and tags
SHOW SCHEMA FOR #customers;

-- Capture schema into a temp table for programmatic inspection
SHOW SCHEMA FOR #customers INTO #schema_info;
SELECT ColumnName, DataType, Tags FROM #schema_info WHERE Tags LIKE '%pii%';
```

## Notes
- `SHOW SCHEMA FOR` is an alias for `SHOW COLUMNS FOR` with enriched schema data (`DataType`, `IsNullable`, and `Tags`).
- `DESCRIBE <table>` functions as a shorthand alias for `SHOW SCHEMA FOR <table>`.

## References
- [SHOW Commands](README.md)
- [Lineage & Governance Reference](../statements/session-control/lineage.md)
