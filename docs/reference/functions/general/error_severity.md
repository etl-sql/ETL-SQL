# ERROR_SEVERITY

Returns the severity value for the current error inside a `CATCH` block.

## Syntax

```sql
ERROR_SEVERITY()
```

## Parameters

None.

## Returns

Returns an `INT` severity value, or `NULL` when no caught error is active.

## Null Behavior

Returns `NULL` outside an active `CATCH` error context.

## Remarks

- Severity is useful for logging and alert routing.
- Use explicit script logic to decide whether to continue, retry, quarantine, or rethrow.

## Examples

```sql
BEGIN TRY
  THROW 'Load failed.';
END TRY
BEGIN CATCH
  INSERT INTO #errors(severity, message)
  VALUES (ERROR_SEVERITY(), ERROR_MESSAGE());
  THROW;
END CATCH;
```

## References

- [TRY...CATCH](../../control-flow/try-catch.md)
- [ERROR_MESSAGE](error_message.md)
- [ERROR_NUMBER](error_number.md)
