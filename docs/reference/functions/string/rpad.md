# RPAD
Right-pads a string with another string until it reaches the specified target length.

**Category:** String

## Syntax
```sql
RPAD(str, length [, pad_str])
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `str` | `VARCHAR` / `STRING` | The original string to be padded |
| `length` | `INT` | The target length of the output string |
| `pad_str` | `VARCHAR` / `STRING` | (Optional) The character sequence to pad with. Defaults to a single space. |

## Returns
`STRING` — The padded string. If `str` is already longer than `length`, it is truncated to `length` characters. Returns `NULL` if `str` or `length` is `NULL`.

## Example
```sql
SELECT RPAD('hello', 8, 'xy'); -- → 'helloxyx'
SELECT RPAD('hello', 3);       -- → 'hel' (truncation)
```

## See Also
- [Standard Library — §1.1 Core String](../../../guides/getting-started.md#11-core-string)
- Related: [`LPAD`](lpad.md), [`REPEAT`](repeat.md)
