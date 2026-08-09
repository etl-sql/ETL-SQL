# ERROR_STATE

Returns the state value for the current error inside a `CATCH` block.

## Syntax

```sql
ERROR_STATE()
```

## Parameters

None.

## Returns

Returns an `INT` state value, or `NULL` when no caught error is active.

## Null Behavior

Returns `NULL` outside an active `CATCH` error context.

## Remarks

- Error state is mainly useful for diagnostics and audit records.
- Use [`ERROR_NUMBER`](error_number.md) and [`ERROR_MESSAGE`](error_message.md) for most handling logic.

## Examples

```sql
BEGIN TRY
  THROW 'Validation failed.';
END TRY
BEGIN CATCH
  PRINT 'Error state: ' + CAST(ERROR_STATE() AS VARCHAR(20));
END CATCH;
```

## References

- [TRY...CATCH](../../control-flow/try-catch.md)
- [ERROR_NUMBER](error_number.md)
- [ERROR_MESSAGE](error_message.md)
- [User Manual](../../../guides/onboarding/getting-started.md)
