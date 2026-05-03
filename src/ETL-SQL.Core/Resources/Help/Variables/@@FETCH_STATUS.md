# @@FETCH_STATUS
Status of the most recent FOREACH or cursor fetch operation.

Values:
  0   — fetch succeeded; the current item is valid
  -1  — end of the collection reached; no more items

```sql
FOREACH @row IN (SELECT id, name FROM #batch) BEGIN
  IF @@FETCH_STATUS <> 0 BEGIN
    BREAK;
  END;
  PRINT 'Processing: ' + @row.name;
  EXECUTE dbo.Process @row.id;
END;
```

@@FETCH_STATUS is automatically managed by FOREACH — you rarely need to check it manually. It is available for advanced cursor-like control patterns where you inspect the value explicitly before processing each row.
