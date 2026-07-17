# SET SPILL_ENCRYPTION
Controls whether data buffers spilled to local disk during heavy queries are encrypted at rest.

## Syntax
```text
SET SPILL_ENCRYPTION = ON|OFF;
```

## Parameters
- **ON** — Encrypt spilled buffers at rest (default).
- **OFF** — Spill buffers are written unencrypted. Faster I/O but less secure.

## Example
```sql
-- Disable spill encryption for a benchmarking session (non-sensitive data)
SET SPILL_ENCRYPTION = OFF;

SELECT * FROM SalesDB.dbo.LargeTable INTO #data;

SET SPILL_ENCRYPTION = ON;
```

## Notes
- Corresponding `appsettings.json` key: `Security:SpillEncryptionEnabled`.
- See also: `SET SPILL_COMPRESSION`, `SET SPILL_FORMAT`.
- Default: ON.

## References
- [SET Commands](README.md)
