# DECODE

Evaluates a value against a series of search terms, returning the corresponding result. Oracle-style CASE shorthand.

## Syntax

```sql
DECODE(value, search1, result1, search2, result2, ...)
DECODE(value, search1, result1, search2, result2, ..., default)
```

## Parameters

- **value** - Expression to compare.
- **searchN** - Comparison values, evaluated in order.
- **resultN** - Value returned when `value = searchN`.
- **default** - Optional value returned when no search value matches.

## Returns

Returns the first matching result, or `default` when supplied. If no match is found and `default` is omitted, returns `NULL`.

## Null Behavior

Returns `NULL` when no search value matches and `default` is omitted.

## Examples

```sql
SELECT DECODE(status, 'A', 'Active', 'I', 'Inactive', 'Unknown') AS status_label
FROM #customers;
```

```sql
SELECT DECODE(MONTH(order_date), 12, 'Q4', 11, 'Q4', 10, 'Q4', 'Other') AS quarter
FROM #orders;
```

## References

- [Standard Library](../standard-library.md)
- [IIF](../conversion/iif.md)
- [COALESCE](../conversion/coalesce.md)
