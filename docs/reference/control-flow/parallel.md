PARALLEL runs enclosed statements concurrently on a thread pool. Useful for independent I/O-bound operations such as loading from multiple sources or running independent queries.

Syntax:
  PARALLEL BEGIN
    <statement1>;
    <statement2>;
    ...
  END;

All statements inside the block start at the same time. PARALLEL waits for all of them to complete before continuing. If any statement throws an error, the block re-throws after the remaining statements finish.

```sql
-- Load three sources at the same time
PARALLEL BEGIN
  SELECT * INTO #orders FROM SalesDB.dbo.Orders;
  SELECT * INTO #employees FROM HRDB.dbo.Employees;
  SELECT * INTO #customers FROM CRMDB.dbo.Customers;
END;

-- Two independent aggregations concurrently
PARALLEL BEGIN
  SELECT region, SUM(amount) AS total INTO #by_region FROM #orders GROUP BY region;
  SELECT month, SUM(amount) AS total INTO #by_month FROM #orders GROUP BY month;
END;
```

Statements inside PARALLEL share the same variable scope but write to separate #temp tables. Avoid reading the same #temp table from two branches — the result is non-deterministic.

References:
- [Control Flow](README.md)
