# STUFF

Deletes a specified number of characters and inserts a replacement string at a given position.

## Syntax

```sql
STUFF(string, start, length, replacement)
```

## Parameters

- **string** - Source string to modify.
- **start** - 1-based position where deletion or insertion begins.
- **length** - Number of characters to delete from `start`.
- **replacement** - String to insert at `start` after deletion.

## Returns

Returns the modified string.

## Null Behavior

Returns `NULL` when any required argument is `NULL`.

## Remarks

- To insert without deleting, pass `0` as `length`.
- To delete without inserting, pass `''` as `replacement`.

## Examples

```sql
SELECT STUFF('Hello World', 6, 0, ' Beautiful') AS expanded_text;
```

```sql
SELECT STUFF(phone, 4, 0, '-') AS formatted_phone
FROM #contacts;
```

## References

- [Standard Library](../standard-library.md)
- [REPLACE](replace.md)
- [OVERLAY](overlay.md)
- [SUBSTRING](substring.md)
