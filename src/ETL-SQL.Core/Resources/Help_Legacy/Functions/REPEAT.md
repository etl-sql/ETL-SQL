# REPEAT
Repeats a string a specified number of times.

**Category:** String

## Syntax
```sql
REPEAT(str, count)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `str` | `VARCHAR` / `STRING` | The string to repeat |
| `count` | `INT` | The number of times to repeat the string |

## Returns
`STRING` — The repeated string. If `count` is 0 or negative, returns an empty string. Returns `NULL` if `str` or `count` is `NULL`.

## Example
```sql
SELECT REPEAT('abc', 3);    -- → 'abcabcabc'
```

## See Also
- [Standard Library — §1.1 Core String](../../../../../Docs/Reference/Standard_Library.md#11-core-string)
- Related: [`REPLICATE`](REPLICATE.md), [`LPAD`](LPAD.md), [`RPAD`](RPAD.md)
