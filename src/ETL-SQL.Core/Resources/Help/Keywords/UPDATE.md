UPDATE modifies column values in existing rows. Use WHERE to limit which rows are affected — omitting it updates every row.

Syntax:
  UPDATE <target>
    SET col1 = val1, col2 = val2, ...
    [FROM <source>]
    [WHERE <condition>];

The FROM clause allows joining to another table to derive the new values.

```sql
-- Simple update
UPDATE #orders SET status = 'complete' WHERE processed = 1;

-- Update from a joined source
UPDATE tgt
  SET tgt.region = src.region
  FROM #orders AS tgt
  JOIN #region_map AS src ON tgt.zip = src.zip;

-- Update with expression
UPDATE #items
  SET price      = price * 1.1,
      updated_at = GETDATE()
  WHERE category = 'premium';

-- Update on a remote connection
UPDATE SalesDB.dbo.Orders
  SET shipped_at = GETDATE()
  WHERE status = 'pending' AND order_date < @cutoff;
```

@@ROWCOUNT reflects the number of rows affected after UPDATE.
