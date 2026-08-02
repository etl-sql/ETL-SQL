# DELETE

DELETE removes rows from a target table. Without WHERE, all rows are removed; prefer TRUNCATE in that case, as it is faster.

## Syntax

```sql
DELETE FROM <target> [WHERE <condition>];
```

```sql
-- Remove specific rows
DELETE FROM #staging WHERE status = 'failed';

-- Remove old rows
DELETE FROM dbo.EventLog
  WHERE created_at < DATEADD(DAY, -90, GETDATE());

-- Conditional delete inside a loop
FOREACH @id IN (SELECT id FROM #orphans) BEGIN
  DELETE FROM dbo.Orders WHERE order_id = @id.id;
END;
```

DELETE on a connection table issues the DELETE on the remote database. DELETE on a #temp table removes rows from the in-memory working set. To remove all rows and reset identity, use TRUNCATE TABLE.

References:
- [Statements](../README.md)
