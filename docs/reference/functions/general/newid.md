# NEWID

Generates a new globally unique identifier (UUID v7, time-ordered).

## Syntax

```sql
NEWID()
NEWSEQUENTIALID()
```

## Returns

Returns a new `UNIQUEIDENTIFIER`. `NEWSEQUENTIALID()` generates a time-ordered UUID v7, the same behavior as `NEWID()` in ETL-SQL.

## Null Behavior

`NEWID` does not return `NULL`.

## Remarks

- Use as a surrogate key or correlation ID for tracing pipeline runs.
- UUID v7 values are monotonically increasing, making them index-friendly.

## Examples

```sql
SELECT NEWID() AS run_id;
```

```sql
INSERT INTO #audit (run_id, ts) VALUES (NEWID(), GETDATE());
```

```sql
DECLARE @batch_id UNIQUEIDENTIFIER = NEWID();
SELECT @batch_id AS batch;
```

## References

- [Standard Library](../standard-library.md)
- [NEWSEQUENTIALID](newsequentialid.md)
