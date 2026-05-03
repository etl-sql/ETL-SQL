# ALTER TABLE
Modifies the schema of an existing #temp table by adding or removing columns.

## Syntax
```sql
-- Add a column
ALTER TABLE #staging ADD COLUMN region STRING;

-- Add a column with a default value
ALTER TABLE #staging ADD COLUMN loaded_at DATE = TODAY();

-- Drop a column
ALTER TABLE #staging DROP COLUMN region;
```

## Notes
- Only `ADD COLUMN` and `DROP COLUMN` are supported.
- Adding a column with no default fills existing rows with NULL.
- Dropping a column permanently removes it and all its data from the in-memory table.
- Column names are case-insensitive.
- ALTER TABLE applies only to #temp tables, not external connection tables.
- See: CREATE TABLE, SELECT INTO