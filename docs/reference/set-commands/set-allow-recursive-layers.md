# SET ALLOW_RECURSIVE_LAYERS
Overrides the directory recursion depth limit for the current session.

## Syntax
```sql
SET ALLOW_RECURSIVE_LAYERS = <n>;
```

## Parameters
- **n** — Maximum recursion depth. Default: 5.

## Example
```sql
-- Allow deeper recursion for a deeply nested directory scan
SET ALLOW_RECURSIVE_LAYERS = 10;

SELECT * INTO #all_files FROM dir_conn.[*];
```

## Notes
- Produces an audit entry.
- Corresponding `appsettings.json` key: `Security:MaxRecursiveNestingDepth`.
- Default: 5.

## References
- [SET Commands](README.md)
