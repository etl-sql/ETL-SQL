# LEAST

Returns the smallest value from a list of arguments.

## Syntax

```sql
LEAST(value1, value2, ...)
```

## Parameters

- **value1** - First comparable value.
- **value2** - Second comparable value.
- **...** - Additional comparable values.

## Returns

Returns the minimum value among all arguments.

## Null Behavior

Returns `NULL` when any argument is `NULL`.

## Examples

```sql
SELECT LEAST(3, 1, 4, 1, 5) AS smallest_value;
```

```sql
SELECT LEAST(sale_price, list_price) AS effective_price
FROM #items;
```

## References

- [Standard Library](../standard-library.md)
- [GREATEST](greatest.md)
- [MIN](../aggregate/min.md)
