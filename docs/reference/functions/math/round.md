# ROUND

Rounds a number to the specified number of decimal places.

## Syntax

```sql
ROUND(number, decimals)
```

## Parameters

- **number** - Numeric value to round.
- **decimals** - Number of decimal places to keep. Negative values round to the left of the decimal point.

## Returns

Returns the rounded numeric value.

## Null Behavior

Returns `NULL` when any required argument is `NULL`.

## Examples

```sql
SELECT ROUND(3.14159, 2) AS rounded_value;
```

```sql
SELECT ROUND(amount, 2) AS rounded_amount
FROM #prices;
```

## References

- [Standard Library](../standard-library.md)
- [FLOOR](floor.md)
- [CEILING](ceiling.md)
- [TRUNCATE](../general/truncate.md)
