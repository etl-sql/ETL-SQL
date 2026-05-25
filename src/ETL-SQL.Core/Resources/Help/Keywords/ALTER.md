# ALTER
Modifies an existing object.

## Syntax
```sql
-- Add a column
ALTER TABLE #staging ADD COLUMN region STRING;

-- Add a column with a default value
ALTER TABLE #staging ADD COLUMN loaded_at DATE = TODAY();

-- Drop a column
ALTER TABLE #staging DROP COLUMN region;

-- Replace a session-scoped query view definition
ALTER VIEW ActiveOrders AS
SELECT order_id, amount
FROM #orders
WHERE status = 'Active';
```

## Notes
- Only `ADD COLUMN` and `DROP COLUMN` are supported.
- Adding a column with no default fills existing rows with NULL.
- Dropping a column permanently removes it and all its data from the in-memory table.
- Column names are case-insensitive.
- ALTER TABLE applies only to #temp tables, not external connection tables.
- `ALTER VIEW` requires the view to exist. Use `CREATE OR ALTER VIEW` for idempotent scripts.
- See: CREATE TABLE, CREATE VIEW, SELECT INTO

References:
- [Grammar](../../../../../Docs/Reference/Grammar.md)
