# RPAD

Right-pads a string with another string until it reaches the specified target length.

## Syntax

```sql
RPAD(string, length)
RPAD(string, length, pad_string)
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
SELECT RPAD('hello', 8, 'xy') AS padded_value;
```

```sql
SELECT RPAD(status_code, 8, '.') AS fixed_width_status
FROM #statuses;
```

## References

- [Functions](../README.md)
- [LPAD](lpad.md)
- [REPEAT](repeat.md)
