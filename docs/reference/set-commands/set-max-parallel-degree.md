# SET MAX_PARALLEL_DEGREE
Sets the thread limit inside `PARALLEL BEGIN...END` blocks.

## Syntax
```sql
SET MAX_PARALLEL_DEGREE = <n>;
```

## Parameters
- **n** — Maximum concurrent threads. Default: CPU count.

## Example
```sql
-- Limit parallelism to avoid saturating shared resources
SET MAX_PARALLEL_DEGREE = 4;

PARALLEL BEGIN
    SELECT * FROM src1.dbo.Table1 INTO #t1;
    SELECT * FROM src2.dbo.Table2 INTO #t2;
    SELECT * FROM src3.dbo.Table3 INTO #t3;
    SELECT * FROM src4.dbo.Table4 INTO #t4;
END;
```

## Notes
- Corresponding `appsettings.json` key: `Security:MaxParallelDegree`.
- Default: CPU count (logical processors).

## References
- [SET Commands](README.md)
