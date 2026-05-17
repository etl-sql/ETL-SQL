# NEWID
Generates a new globally unique identifier (UUID v7, time-ordered).

**Category:** System

## Syntax
```sql
NEWID()
NEWSEQUENTIALID()
```

## Returns
`UNIQUEIDENTIFIER` — A new UUID. `NEWSEQUENTIALID()` generates a time-ordered UUID v7 (same behavior as `NEWID()` in ETL-SQL).

## Remarks
- Use as a surrogate key or correlation ID for tracing pipeline runs.
- UUID v7 values are monotonically increasing, making them index-friendly.

## Example
```sql
SELECT NEWID() AS run_id;
INSERT INTO #audit (run_id, ts) VALUES (NEWID(), GETDATE());

DECLARE @batch_id UNIQUEIDENTIFIER = NEWID();
SELECT @batch_id AS batch;
```

## See Also
- [Standard Library — §8. System & Identity Functions](../../../../../Docs/Reference/Standard_Library.md#8-system--identity-functions)
