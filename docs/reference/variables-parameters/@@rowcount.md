# @@ROWCOUNT
Number of rows affected by the last DML statement (INSERT, UPDATE, DELETE, MERGE) or returned by the last SELECT.

Set by: SELECT, INSERT, UPDATE, DELETE, MERGE, BULK INSERT.
Scope:  updated after each row-producing statement — read it immediately after.

```sql
-- Guard on empty result
SELECT * INTO #matches FROM dbo.Orders WHERE status = 'pending';
IF @@ROWCOUNT = 0 BEGIN
  PRINT 'No pending orders found.';
  RETURN;
END;

-- Log insertion count
INSERT INTO dbo.Archive SELECT * FROM #staging;
PRINT 'Archived: ' + @@ROWCOUNT + ' rows';

-- Report MERGE results
MERGE INTO dbo.Target USING #source ON target.id = source.id
  WHEN MATCHED THEN UPDATE SET target.value = source.value
  WHEN NOT MATCHED THEN INSERT (id, value) VALUES (source.id, source.value);
PRINT 'Rows merged: ' + @@ROWCOUNT;
```

References:
- [Standard Library](../../guides/getting-started.md)
