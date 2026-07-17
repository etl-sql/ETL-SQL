# GREATEST

Returns the largest value from a list of arguments.

## Syntax

```sql
GREATEST(value1, value2, ...)
```

## Parameters

- **value1** - First comparable value.
- **value2** - Second comparable value.
- **...** - Additional comparable values.

## Returns

Returns the maximum value among all arguments.

## Null Behavior

Returns `NULL` when any argument is `NULL`.

## Examples

```sql
SELECT GREATEST(3, 1, 4, 1, 5) AS largest_value;
```

```sql
SELECT GREATEST(cost, minimum_charge) AS billed
FROM #jobs;
```

## References

- [Functions](../README.md)
- [LEAST](least.md)
- [MAX](../aggregate/max.md)
