# LEN
Returns the number of characters in a string, or the number of items in a LIST.

**Category:** String

## Syntax
```sql
LEN(string)
LENGTH(string)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` or `LIST` | The value to measure |

## Returns
`INT` — Character count for strings; item count for LISTs. Trailing spaces are not counted (matches SQL Server behavior).

## Remarks
- `LEN` and `LENGTH` are interchangeable aliases.
- For byte-level length, use [`DATALENGTH`](datalength.md) instead.
- Returns `NULL` if the input is `NULL`.

## Example
```sql
SELECT LEN('hello');           -- → 5
SELECT LEN('hello   ');        -- → 5  (trailing spaces excluded)
SELECT LENGTH('café');         -- → 4  (character count, not byte count)

DECLARE @ids LIST = (1, 2, 3);
SELECT LEN(@ids);              -- → 3
```

## See Also
- [Standard Library — §3.6 Translation & Escaping](../../../guides/getting-started.md#36-translation--escaping)
- Related: [`DATALENGTH`](datalength.md), [`CHAR_LENGTH`](char_length.md)
