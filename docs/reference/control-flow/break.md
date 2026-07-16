# BREAK
Exits the innermost active loop immediately, transferring control to the statement after the loop's `END`.

## Syntax
```sql
FOR @i = 1 TO 100 BEGIN
  IF @i = 50 BEGIN
    BREAK;
  END;
  PRINT @i;
END;
```

## Works inside
- `FOR` numeric loops
- `FOR ... IN` query loops
- `FOREACH` loops
- `WHILE` loops
- `PARALLEL` blocks (exits the parallel worker, not the whole block)

## Notes
- BREAK exits only one level of nesting. For nested loops, use a flag variable or restructure the logic.
- BREAK inside a `TRY` block does not suppress any pending `CATCH` — the loop exits cleanly.
- See: CONTINUE, FOR, FOREACH, WHILE

References:
- [Control Flow](README.md)
