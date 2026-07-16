# CONTINUE
Skips the remainder of the current loop iteration and begins the next one immediately.

## Syntax
```sql
FOR @i = 1 TO 10 BEGIN
  IF @i = 5 BEGIN
    CONTINUE;  -- skip iteration 5
  END;
  PRINT @i;
END;
```

## Works inside
- `FOR` numeric loops
- `FOR ... IN` query loops
- `FOREACH` loops
- `WHILE` loops

## Example — skip rows matching a condition
```sql
FOR @row IN (SELECT * FROM #orders) BEGIN
  IF @row.Status = 'Cancelled' BEGIN
    CONTINUE;
  END;
  -- process only non-cancelled orders
  PRINT @row.OrderId;
END;
```

## Notes
- CONTINUE affects only the innermost loop.
- For a `FOR @i` loop, CONTINUE still applies the STEP increment before re-evaluating the condition.
- See: BREAK, FOR, FOREACH, WHILE

References:
- [Control Flow](README.md)
