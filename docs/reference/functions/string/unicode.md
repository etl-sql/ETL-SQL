# UNICODE
Returns the Unicode code point of the first character of a string.

**Category:** String

## Syntax
```sql
UNICODE(string)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The input string; only the first character is evaluated |

## Returns
`INT` — Unicode code point of the first character. Returns `NULL` if the string is empty or `NULL`.

## Remarks
- For ASCII-range characters, `UNICODE` and `ASCII` return identical values.
- For characters outside the BMP (emoji, etc.), returns the full code point value.
- To reverse the operation, use [`CHAR`](char.md).

## Example
```sql
SELECT UNICODE('A');    -- → 65
SELECT UNICODE('é');    -- → 233
SELECT UNICODE('€');    -- → 8364
```

## See Also
- [Standard Library — §3.5 Character Encoding](../../../guides/getting-started.md#35-character-encoding)
- Related: [`ASCII`](ascii.md), [`CHAR`](char.md)
