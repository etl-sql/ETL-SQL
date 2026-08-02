# SET SPILL_COMPRESSION
<!-- SetSpillOptionStatement -->
Controls whether data buffers spilled to disk are compressed.

## Syntax
```text
SET SPILL_COMPRESSION ON|OFF;
```

## Parameters
- **ON** — Compress spilled buffers to save disk space (default).
- **OFF** — Spill buffers without compression. Reduces CPU overhead but uses more disk.

## Example
```sql
-- Disable compression for CPU-bound workloads
SET SPILL_COMPRESSION OFF;

SELECT * INTO #data FROM SalesDB.dbo.VeryLargeTable;

SET SPILL_COMPRESSION ON;
```

## Notes
- Corresponding `appsettings.json` key: `Security:SpillCompressionEnabled`.
- See also: `SET SPILL_ENCRYPTION`, `SET SPILL_FORMAT`.
- Default: ON.

## References
- [SET Commands](README.md)
