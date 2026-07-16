# REVERSE

Returns a string with characters in reverse order.

## Syntax

```sql
REVERSE(string)
```

## Parameters

- **string** - String to reverse.

## Returns

Returns the input string with character order reversed.

## Null Behavior

Returns `NULL` when `string` is `NULL`.

## Examples

```sql
SELECT REVERSE('hello') AS reversed_text;
```

```sql
SELECT word
FROM #words
WHERE word = REVERSE(word);
```

## Remarks

- Operates on Unicode code points; surrogate pairs are kept intact.

## References

- [Standard Library](../standard-library.md)
- [SUBSTRING](substring.md)
- [LENGTH](length.md)
