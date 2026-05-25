# GO
Batch separator. Divides a script into independent batches that each run in isolation — if one batch fails, subsequent batches still execute.

## Syntax
```sql
GO
GO <count>    -- repeat the preceding batch <count> times
```

## Behavior
- Each batch is executed as a self-contained unit.
- A runtime error in one batch is logged and skipped; the next batch runs normally.
- Without `GO`, an error stops the entire script (fail-fast).
- Variables declared in an earlier batch remain in scope for later batches (session-scoped).
- Temp tables (`#table`) created in an earlier batch are accessible in later batches.

## `GO <count>`
`GO 3` repeats the preceding batch three times. Useful for populating data or running a setup block multiple times.

```sql
-- Insert a test row 5 times
INSERT INTO #log (ts, msg) VALUES (GETDATE(), 'ping');
GO 5
```

## Examples
```sql
-- Batch 1: Create and populate a temp table
SELECT region, SUM(revenue) AS total
  INTO #summary
  FROM dbo.Sales
  GROUP BY region;
GO

-- Batch 2: Report; runs even if batch 1 fails on a real data source
SELECT * FROM #summary ORDER BY total DESC;
GO

-- Batch 3: Clean up
DROP TABLE IF EXISTS #summary;
```

## Notes
- `GO` is a client-side directive, not a SQL statement — it has no effect inside `BEGIN…END` blocks, procedures, or `TRY…CATCH`.
- A cancelled execution (`CancellationToken`) propagates immediately across all batches.
- See: TRY, RETURN, BREAK

References:
- [Grammar](../../../../../Docs/Reference/Grammar.md)
