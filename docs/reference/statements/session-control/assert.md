# ASSERT
Validates a condition at runtime and halts execution with an error if it is false. Used for data quality checks and script contracts.

## Syntax
```sql
ASSERT <condition>;

ASSERT <condition> 'Custom failure message';
```

## Examples
```sql
-- Halt if no rows were loaded
ASSERT (SELECT COUNT(*) FROM #orders) > 0
  'No orders found for the reporting period.';

-- Validate a variable
ASSERT @batchSize BETWEEN 1 AND 10000
  'Batch size must be between 1 and 10000.';

-- Check referential integrity
ASSERT (
  SELECT COUNT(*) FROM #orders o
  LEFT JOIN #customers c ON o.CustomerId = c.Id
  WHERE c.Id IS NULL
) = 0 'Orphaned orders detected; missing customer records.';
```

## Notes
- If the condition evaluates to FALSE or NULL, execution stops and the message (or a default assertion error) is raised.
- ASSERT is removed at runtime in release mode if the `Engine.DisableAsserts` setting is `true` in `appsettings.json`.
- Use ASSERT for invariants and data contracts; use `THROW` for business-logic errors.
- See: THROW, TRY, LINT

References:
- [Grammar](../../../guides/getting-started.md)
