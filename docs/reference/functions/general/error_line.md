# ERROR_LINE

Returns the script line number associated with the current error inside a `CATCH` block.

## Syntax

```sql
ERROR_LINE()
```

## Parameters

None.

## Returns

Returns an `INT` line number, or `NULL` when no caught error is active.

## Null Behavior

Returns `NULL` outside an active `CATCH` error context.

## Remarks

- Line numbers are diagnostic hints and may vary when scripts are generated or nested.
- Use with [`ERROR_MESSAGE`](error_message.md) to make failure logs actionable.

## Examples

```sql
BEGIN TRY
  SELECT CAST('bad' AS INT);
END TRY
BEGIN CATCH
  PRINT 'Failed near line ' + CAST(ERROR_LINE() AS VARCHAR(20)) + ': ' + ERROR_MESSAGE();
END CATCH;
```

## References

- [TRY...CATCH](../../control-flow/try-catch.md)
- [ERROR_MESSAGE](error_message.md)
