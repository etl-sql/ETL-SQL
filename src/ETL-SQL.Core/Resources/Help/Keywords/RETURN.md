# RETURN
Exits the current script or procedure immediately, optionally surfacing output variable values to the caller.

## Syntax
```sql
-- Exit unconditionally
RETURN;

-- Early exit inside a condition
IF @rowCount = 0 BEGIN
  PRINT 'No data to process.';
  RETURN;
END;
```

## Returning output parameters
Output variables are declared with `OUTPUT` and written back automatically when the script exits — no special RETURN syntax is needed. The caller receives them via `WITH (... @param = @out)`.
```sql
-- In subscript: get_count.etlsql
DECLARE @count INT OUTPUT;
SET @count = (SELECT COUNT(*) FROM #data);
RETURN;
```

## Notes
- `RETURN` in the top-level script ends the entire run.
- `RETURN` inside a `RUN SCRIPT` call returns control to the calling script.
- All OUTPUT variables are flushed to the caller at the point of RETURN, not only at natural end-of-file.
- RETURN does not roll back open transactions — use `ROLLBACK` before RETURN if needed.
- See: RUN SCRIPT, DECLARE, TRANSACTION