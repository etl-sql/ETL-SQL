# GO
Batch separator. Divides a script into batches that run in order. If a batch fails, the script stops and later batches do not run.

## Syntax
```sql
GO
GO <count>    -- repeat the preceding batch <count> times
```

## Behavior
- Batches run in order.
- A runtime error that is not caught by `TRY…CATCH` inside the batch is logged as
  `Batch <n> failed`, stops the script, and fails the run (non-zero exit code), like
  `sqlcmd -b`. Before v0.22.0 the error was logged and the next batch ran.
- To keep going after an error, handle it with `TRY…CATCH` inside the batch.
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

-- Batch 2: Report; runs only if batch 1 succeeded
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
- [Control Flow](README.md)
