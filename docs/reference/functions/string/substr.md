# SUBSTR

Returns a portion of a string. `SUBSTR` is an alias for [`SUBSTRING`](substring.md).

## Syntax

```sql
SUBSTR(string, start, length)
```

## Parameters

- **string** - Source string.
- **start** - 1-based starting position. Negative values count from the end.
- **length** - Number of characters to return.

## Returns

Returns a `STRING`.

## Null Behavior

Returns `NULL` when `string`, `start`, or `length` is `NULL`.

## Remarks

- Positions are 1-based.
- If `start + length` extends beyond the end of the string, ETL-SQL returns characters through the end.
- Use [`LEFT`](left.md) or [`RIGHT`](right.md) for edge-based extraction.

## Examples

```sql
SELECT SUBSTR('hello', 1, 4) AS prefix;
```

```sql
SELECT SUBSTR(product_code, 1, 3) AS product_family
FROM #products;
```

## References

- [Functions](../README.md)
- [SUBSTRING](substring.md)
- [LEFT](left.md)
- [RIGHT](right.md)
