# INSERT

INSERT adds new rows to a target table from a SELECT result or a literal VALUES list.

## Syntax

```sql
INSERT INTO <target> [(col1, col2, ...)]
  SELECT ...;

INSERT INTO <target> [(col1, col2, ...)]
  VALUES (<val1>, <val2>, ...);
```

INSERT ... SELECT is the preferred form for bulk inserts from another table or query.
If column names are omitted the values are matched positionally.

## Examples

```sql
-- Insert from a query
INSERT INTO dbo.Archive (id, name, archived_at)
  SELECT id, name, GETDATE() FROM #staging;

-- Insert literal values
INSERT INTO #lookup (code, label) VALUES
  ('A', 'Active'),
  ('I', 'Inactive'),
  ('P', 'Pending');

-- Insert into a temp table for downstream use
INSERT INTO #summary (region, total)
  SELECT region, SUM(amount) FROM #sales GROUP BY region;
```

## Notes

To insert into a remote connection table, the connection must be specified via AT or via a qualified table name.
To replace all rows, use TRUNCATE followed by INSERT, or use MERGE for upsert semantics.

## References

- [Statements](../README.md)
- [SELECT](select.md)
