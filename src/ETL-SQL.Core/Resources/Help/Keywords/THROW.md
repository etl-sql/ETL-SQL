# THROW / RAISEERROR
Raises a runtime error, terminating execution or transferring control to the nearest CATCH block.

## THROW Syntax
```sql
THROW 'Something went wrong.';

THROW 50001, 'Invalid region code: ' + @region, 1;
```

## RAISEERROR Syntax (compatibility alias)
```sql
RAISEERROR('Record not found for ID ' + @id);
```

## Inside a TRY/CATCH block
```sql
TRY BEGIN
  RUN SCRIPT 'load_data.etlsql';
END;
CATCH BEGIN
  PRINT 'Load failed: ' + ERROR_MESSAGE();
  THROW;  -- re-raise the original error
END;
```

## Notes
- `THROW` with no arguments inside a `CATCH` block re-raises the caught error (preserving original message and stack).
- `THROW <number>, <message>, <severity>` mirrors T-SQL THROW; the number and severity are recorded in execution logs.
- `RAISEERROR` is a single-argument alias for simple message-only throws.
- Uncaught errors abort the script and mark the execution session as FAILED in Orchestrator history.
- See: TRY, ASSERT, TRANSACTION