# LPAD

Left-pads a string with another string until it reaches the specified target length.

## Syntax

```sql
LPAD(string, length)
LPAD(string, length, pad_string)
```

## Parameters

- **string** - Original string to pad.
- **length** - Target length of the output string.
- **pad_string** - Optional character sequence to pad with. Defaults to a single space.

## Returns

Returns the padded string. If `string` is already longer than `length`, it is truncated to `length` characters.

## Null Behavior

Returns `NULL` when `string` or `length` is `NULL`.

## Examples

```sql
SELECT LPAD('hello', 8, 'xy') AS padded_value;
```

```sql
SELECT LPAD(account_id, 10, '0') AS padded_account_id
FROM #accounts;
```

## References

- [Standard Library](../standard-library.md)
- [RPAD](rpad.md)
- [REPEAT](repeat.md)
