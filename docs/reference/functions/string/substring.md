# SUBSTRING

Returns a portion of a string starting at a given position.

## Syntax

```sql
SUBSTRING(string, start, length)
SUBSTR(string, start, length)
```

## Parameters

- **string** - Source string.
- **start** - 1-based starting position. Negative values count from the end.
- **length** - Number of characters to return.

## Returns

Returns the extracted substring. Returns an empty string if `start` is beyond the end of `string`.

## Null Behavior

Returns `NULL` when any required argument is `NULL`.

## Remarks

- Positions are **1-indexed** (first character = 1), matching SQL Server convention.
- `SUBSTR` is a direct alias for `SUBSTRING`.
- If `start + length` exceeds the string length, characters up to the end are returned without error.
- **Dialect Translation**: In pushdown queries, the engine transpiles `SUBSTRING` to `SUBSTR` for **Oracle** targets.

## Examples

```sql
SELECT SUBSTRING('Hello World', 7, 5) AS selected_text;
```

```sql
SELECT SUBSTR(product_code, 1, 3) AS prefix
FROM #products;
```

## References

- [Functions](../README.md)
- [LEFT](left.md)
- [RIGHT](right.md)
- [CHARINDEX](charindex.md)
