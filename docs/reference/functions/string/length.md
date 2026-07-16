# LENGTH

Returns the length of a string or collection expression.

## Syntax

```sql
LENGTH(expression)
```

## Parameters

- **expression** - String, list, or collection expression to measure.

## Returns

Returns an `INT`.

## Null Behavior

`LENGTH(NULL)` returns `NULL`.

## Remarks

- For strings, `LENGTH` returns the character count.
- For list values, `LENGTH` returns the number of items.
- Use [`DATALENGTH`](datalength.md) when byte size matters.

## Examples

```sql
SELECT LENGTH('hello') AS string_length;
```

```sql
SELECT product_id
FROM #products
WHERE LENGTH(tags) > 0;
```

## References

- [Standard Library](../standard-library.md)
- [LEN](len.md)
- [CHAR_LENGTH](char_length.md)
- [DATALENGTH](datalength.md)
