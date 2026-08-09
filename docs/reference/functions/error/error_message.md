# ERROR_MESSAGE

Returns the message for the current error inside a `CATCH` block.

## Syntax

```sql
ERROR_MESSAGE()
```

## Parameters

None.

## Returns

Returns the current error message as `STRING`, or `NULL` when no caught error is active.

## Null Behavior

Returns `NULL` outside an active `CATCH` error context.

## Remarks

- Use `ERROR_MESSAGE()` inside `BEGIN CATCH ... END CATCH`.
- Do not print raw provider exception details or secret-bearing values. ETL-SQL connector boundaries should sanitize provider errors before they reach script code.

## Examples

```sql
BEGIN TRY
  SELECT CAST('bad' AS INT);
END TRY
BEGIN CATCH
  PRINT 'Load failed: ' + ERROR_MESSAGE();
  THROW;
END CATCH;
```

## References

- [TRY...CATCH](../../control-flow/try-catch.md)
- [ERROR_NUMBER](error_number.md)
- [ERROR_LINE](error_line.md)
- [ERROR_STATE](error_state.md)
- [User Manual](../../../guides/onboarding/getting-started.md)
