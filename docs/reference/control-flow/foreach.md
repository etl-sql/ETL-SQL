FOREACH iterates over a LIST variable, a #temp table's rows, or a JSON array.

Syntax:
  FOREACH @item IN <collection> BEGIN
    ...
  END;

Where collection is:
- **@list_var** — a LIST-typed variable; @item binds to each element
- **#temp_table** — each row of the table; @item.column accesses fields
- **(SELECT ...)** — inline query; each result row binds to @item

```sql
-- Iterate a LIST variable
DECLARE @regions LIST = 'North,South,East,West';
FOREACH @r IN @regions BEGIN
  PRINT 'Region: ' + @r;
END;

-- Iterate a temp table
SELECT name, amount FROM #orders INTO #order_list;
FOREACH @o IN #order_list BEGIN
  PRINT @o.name + ': ' + @o.amount;
END;

-- Inline query
FOREACH @row IN (SELECT id FROM dbo.Pending ORDER BY id) BEGIN
  EXECUTE dbo.Process @row.id;
END;
```

@@FETCH_STATUS is 0 while iterating and -1 when the collection is exhausted.
BREAK exits the loop early. CONTINUE skips to the next item.

References:
- [Grammar](../../guides/getting-started.md)
