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

`PARALLEL` is not a loop. A `BREAK` inside a `PARALLEL` block that is not also inside one of the
loops above fails that branch at run time — only `FOR`, `FOREACH`, and `WHILE` catch it. To leave a
loop from inside a `PARALLEL` nested in it, the `BREAK` still belongs to the loop, not to the block.

## Notes
- BREAK exits only one level of nesting. For nested loops, use a flag variable or restructure the logic.
- BREAK inside a `TRY` block does not suppress any pending `CATCH` — the loop exits cleanly.
- See: CONTINUE, FOR, FOREACH, WHILE

References:
- [Control Flow](README.md)
