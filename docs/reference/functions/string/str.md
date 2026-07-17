# STR

Formats a numeric value as a right-padded string of a specified length.

## Syntax

```sql
STR(number)
STR(number, length)
STR(number, length, decimals)
```

## Parameters

- **number** - Numeric value to format.
- **length** - Optional total output length including decimal point and sign. Defaults to `10`.
- **decimals** - Optional decimal places to include. Defaults to `0`.

## Returns

Returns a right-aligned numeric string padded with leading spaces. If the result exceeds `length`, returns asterisks (`*`).

## Null Behavior

Returns `NULL` when `number` is `NULL`.

## Examples

```sql
SELECT STR(1234.567, 8, 2) AS formatted_number;
```

```sql
SELECT STR(amount, 12, 2) AS formatted_amount
FROM #ledger;
```

## References

- [Functions](../README.md)
- [FORMAT](../string/format.md)
- [TO_STR](to_str.md)
