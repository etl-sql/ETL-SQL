# DROP
Removes a table, connection, index, or named set from the current session.

## Variants

**DROP TABLE** — remove a #temp table and release its memory/disk resources.
**DROP CONNECTION** — deregister a named connection alias.
**DROP INDEX** — remove an index from a #temp table.
**DROP SETS** — delete a named set defined with CREATE SETS.

## Syntax
```sql
DROP TABLE #staging;

DROP CONNECTION MyDB;

DROP INDEX idx_id ON #staging;

DROP SETS !Regions;
```

## Notes
- Dropping a #temp table that does not exist raises a runtime error; guard with `IF` checks if needed.
- Connections are session-scoped by default; they are automatically released when the script ends.
- Dropping an index does not drop the table.
- See: CREATE, CLEAR SESSION