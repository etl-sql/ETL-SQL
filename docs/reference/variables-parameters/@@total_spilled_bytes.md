# @@TOTAL_SPILLED_BYTES
Cumulative bytes written to disk for all spill operations (sorts, joins, aggregations) in the current session.

```sql
SELECT *
  INTO #result
  FROM dbo.HugeTable
  ORDER BY category, created_at;

PRINT 'Total spilled: ' + @@TOTAL_SPILLED_BYTES + ' bytes';

IF @@TOTAL_SPILLED_BYTES > 1073741824 BEGIN  -- 1 GB
  PRINT 'Warning: excessive disk spill. Consider adding filters or raising thresholds.';
END;
```

High spill volumes slow execution significantly. To reduce them:
- Pre-filter the result set before sorting or joining.
- Raise spill thresholds in appsettings.json (Engine.SortSpillThreshold, Engine.JoinSpillThreshold).
- Add intermediate INTO #temp steps to reduce sort/join input size.

References:
- [Standard Library](../../guides/getting-started.md)
