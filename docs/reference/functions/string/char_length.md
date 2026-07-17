# CHAR_LENGTH

Returns the number of characters in a string. `CHAR_LENGTH` is the SQL-standard alias for [`LEN`](len.md).

## Syntax

```sql
CHAR_LENGTH(string)
```

## Parameters

- **string** - String expression to measure.

## Returns

Returns an `INT` character count.

## Null Behavior

`CHAR_LENGTH(NULL)` returns `NULL`.

## Remarks

- Counts characters, not encoded bytes. Use [`DATALENGTH`](datalength.md) for byte length.
- For list values, use [`LENGTH`](length.md).

## Examples

```sql
SELECT CHAR_LENGTH('hello') AS len;
```

```sql
SELECT customer_id, email
FROM #customers
WHERE CHAR_LENGTH(email) > 254;
```

## References

- [Functions](../README.md)
- [LEN](len.md)
- [LENGTH](length.md)
- [DATALENGTH](datalength.md)
