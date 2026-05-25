MERGE performs an upsert — matching rows are updated; unmatched rows are inserted. Optionally, rows present in the target but absent from the source can be deleted.

Syntax:
  MERGE INTO <target>
  USING <source> ON <join_condition>
  WHEN MATCHED THEN
    UPDATE SET col = val, ...
  WHEN NOT MATCHED THEN
    INSERT (col, ...) VALUES (val, ...)
  [WHEN NOT MATCHED BY SOURCE THEN DELETE];

The source can be a #temp table, a subquery, or a connection table.

```sql
SELECT id, name, amount FROM dbo.Incoming INTO #new_data;

MERGE INTO dbo.Orders AS tgt
USING #new_data AS src ON tgt.id = src.id
WHEN MATCHED THEN
  UPDATE SET tgt.name = src.name, tgt.amount = src.amount
WHEN NOT MATCHED THEN
  INSERT (id, name, amount) VALUES (src.id, src.name, src.amount);

PRINT 'Merged rows: ' + @@ROWCOUNT;
```

WHEN NOT MATCHED BY SOURCE THEN DELETE removes target rows with no matching source row — use with caution on production tables.
Wrap MERGE in a transaction to make the operation atomic.

References:
- [Grammar](../../../../../Docs/Reference/Grammar.md)
