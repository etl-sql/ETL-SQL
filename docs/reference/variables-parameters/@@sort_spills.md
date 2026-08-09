# @@SORT_SPILLS
Count of external sort runs that have spilled to disk in the current session. Spills occur when an ORDER BY or window function sort exceeds the in-memory sort threshold.

```sql
SELECT * INTO #sorted FROM dbo.BigTable ORDER BY created_at DESC;

IF @@SORT_SPILLS > 0 BEGIN
  PRINT 'Sort spilled ' + @@SORT_SPILLS + ' time(s) — consider raising SORT_SPILL_THRESHOLD.';
END;
```

Spills increase latency significantly. To reduce them:
- Pre-filter the result set before sorting.
- Raise the threshold via SET SORT_SPILL_THRESHOLD = n or in appsettings.json (Engine.SortSpillThreshold).
- Add an INTO #temp before a large ORDER BY to reduce the sort input size.

References:
- [Standard Library](../../guides/onboarding/getting-started.md)
