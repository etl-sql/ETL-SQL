# ERROR_NUMBER

Returns the numeric code for the current error inside a `CATCH` block.

## Syntax

```sql
ERROR_NUMBER()
```

## Parameters

None.

## Returns

Returns an `INT` error code, or `NULL` when no caught error is active.

## Null Behavior

Returns `NULL` outside an active `CATCH` error context.

## Remarks

- Use `ERROR_NUMBER()` for structured branching or audit records inside `CATCH`.
- Use [`ERROR_MESSAGE`](error_message.md) for human-readable diagnostics.

## Examples

```sql
BEGIN TRY
  THROW 'Invalid input.';
END TRY
BEGIN CATCH
  INSERT INTO #errors(error_number, error_message)
  VALUES (ERROR_NUMBER(), ERROR_MESSAGE());
END CATCH;
```

## References

- [TRY...CATCH](../../control-flow/try-catch.md)
- [ERROR_MESSAGE](error_message.md)
- [ERROR_STATE](error_state.md)
