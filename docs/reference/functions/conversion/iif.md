# IIF

Returns one of two values based on a boolean condition. Inline conditional expression.

## Syntax

```sql
IIF(condition, true_value, false_value)
```

## Parameters

- **condition** - Boolean condition to evaluate.
- **true_value** - Value returned when `condition` is true.
- **false_value** - Value returned when `condition` is false or unknown.

## Returns

Returns the selected branch value.

## Null Behavior

A `NULL` or unknown condition selects `false_value`.

## Remarks

- `IIF` is compiled to `CASE WHEN condition THEN true_value ELSE false_value END`.
- Evaluation short-circuits: the untaken branch is not evaluated.
- Pushes down to any connector as universal `CASE`, not as a T-SQL-only function.

## Examples

```sql
SELECT IIF(score >= 90, 'Pass', 'Fail') AS result
FROM #tests;
```

```sql
SELECT order_id, IIF(qty > 0, price * qty, 0) AS extended_amount
FROM #orders;
```

```sql
SELECT IIF(region IS NULL, 'Unknown', region) AS region
FROM #data;
```

## References

- [Standard Library](../standard-library.md)
- [COALESCE](../null-handler/coalesce.md)
- [NULLIF](../null-handler/nullif.md)
- [DECODE](../conversion/decode.md)
