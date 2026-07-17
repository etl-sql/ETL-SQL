# SET SPILL_FORMAT
Sets the serialization format used for data spills to disk.

## Syntax
```sql
SET SPILL_FORMAT = 'AUTO'|'JSON'|'PARQUET';
```

## Parameters
- **AUTO** — Engine chooses the best format based on data shape.
- **JSON** — Use JSON serialization for spills.
- **PARQUET** — Use Parquet serialization for spills.

## Example
```sql
-- Force Parquet format for spills
SET SPILL_FORMAT = 'PARQUET';

SELECT * FROM SalesDB.dbo.LargeTable INTO #data;
```

## Notes
- Corresponding `appsettings.json` key: `Security:SpillFormat`.
- See also: `SET SPILL_ENCRYPTION`, `SET SPILL_COMPRESSION`.
- Default: `Arrow`.

## References
- [SET Commands](README.md)
