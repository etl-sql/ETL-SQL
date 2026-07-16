# DATALENGTH

Returns the number of bytes used to represent an expression.

## Syntax

```sql
DATALENGTH(expression)
```

## Returns

Returns an `INT` byte count.

## Null Behavior

`DATALENGTH(NULL)` returns `NULL`.

## Remarks

- `DATALENGTH` measures bytes, not characters.
- Use [`LEN`](len.md), [`LENGTH`](length.md), or [`CHAR_LENGTH`](char_length.md) for character counts.
- Byte counts depend on the value type and encoding used by the engine representation.

## Examples

```sql
SELECT DATALENGTH('hello') AS byte_count;
```

```sql
SELECT file_name, DATALENGTH(payload) AS payload_bytes
FROM #raw_files;
```

## References

- [Standard Library](../standard-library.md)
- [LEN](len.md)
- [CHAR_LENGTH](char_length.md)
- [LENGTH](length.md)
