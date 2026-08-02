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
    SELECT * INTO #t1 FROM src1.dbo.Table1;
    SELECT * INTO #t2 FROM src2.dbo.Table2;
    SELECT * INTO #t3 FROM src3.dbo.Table3;
    SELECT * INTO #t4 FROM src4.dbo.Table4;
END;
```

## Notes
- Corresponding `appsettings.json` key: `Security:MaxParallelDegree`.
- Default: CPU count (logical processors).

## References
- [SET Commands](README.md)
