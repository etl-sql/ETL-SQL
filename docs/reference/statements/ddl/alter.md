# ALTER
<!-- AlterCatalogObjectStatement -->
Modifies an existing object.

## Syntax
```sql
-- Add a column
ALTER TABLE #staging ADD region STRING;

-- Add a column with a default value
ALTER TABLE #staging ADD loaded_at DATE DEFAULT TODAY();

-- Drop a column
ALTER TABLE #staging DROP region;

-- Replace a session-scoped query view definition
ALTER VIEW ActiveOrders AS
SELECT order_id, amount
FROM #orders
WHERE status = 'Active';
```

## Notes
- Supported `ALTER` forms vary by object kind. See the [Lifecycle Capability Matrix](../lifecycle-matrix.md).
- Only `ADD` and `DROP` column actions are supported.
- Adding a column with no default fills existing rows with NULL.
- Dropping a column permanently removes it and all its data from the in-memory table.
- Column names are case-insensitive.
- ALTER TABLE applies only to #temp tables, not external connection tables.
- `ALTER VIEW` requires the view to exist. Use `CREATE OR ALTER VIEW` for idempotent scripts.
- See: CREATE TABLE, CREATE VIEW, SELECT INTO

References:
- [Statements](../README.md)
