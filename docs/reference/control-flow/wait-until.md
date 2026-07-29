# WAIT UNTIL

Polls a scalar condition until it becomes true, then continues script execution. Use this for readiness checks where the script should pause until data, state, or a dependency is available.

## Syntax
```sql
WAIT UNTIL condition;
```

## Forms
- **condition** - A scalar expression or subquery expression evaluated by the engine.
- **Poll interval** - Scalar conditions are checked every 200ms.

## Examples
```sql
-- Wait until a staged table has rows
WAIT UNTIL (SELECT COUNT(*) FROM #incoming) > 0;

-- Wait until a flag variable is set by earlier control flow
WAIT UNTIL @is_ready = TRUE;
```

## Notes
- `WAIT UNTIL condition` is the preferred spelling for condition polling.
- `WAITFOR (condition)` remains accepted for compatibility with the `WAITFOR` statement family.
- Use `WAITFOR FILE UNLOCKED` for file-arrival waits.
- Use a `WHILE` loop with `WAITFOR DELAY` inside when you need a custom poll interval or logic between checks.

References:
- [WAITFOR](waitfor.md)
- [WAITFOR FILE UNLOCKED](../file-operations/waitfor-file-unlocked.md)
- [Control Flow](README.md)
