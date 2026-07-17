# SHOW VERSION
Displays the engine version and build metadata.

## Syntax
```sql
SHOW VERSION [INTO #table];
```

## Parameters
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
Engine version string including major, minor, patch, and build metadata.

## Example
```sql
-- Display the engine version
SHOW VERSION;

-- Capture for scripted version checks
SHOW VERSION INTO #ver;
SELECT Version FROM #ver;
```

## Notes
- Useful for verifying engine versions in CI/CD pipelines or diagnostic scripts.

## References
- [SHOW Commands](README.md)
