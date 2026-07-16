# CHAR
Returns the character corresponding to an ASCII or Unicode code point.

**Category:** String

## Syntax
```sql
CHAR(code)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `code` | `INT` | The numeric code point (0–1,114,111 for full Unicode range) |

## Returns
`STRING` — A single-character string for the given code point. Returns `NULL` if `code` is out of range.

## Example
```sql
SELECT CHAR(65);         -- → 'A'
SELECT CHAR(233);        -- → 'é'
SELECT CHAR(13);         -- → carriage return (CR)
SELECT CHAR(10);         -- → line feed (LF)

-- Strip carriage returns from imported data
UPDATE #raw SET notes = REPLACE(notes, CHAR(13), '');
```

## See Also
- [Standard Library — §3.5 Character Encoding](../../../../../Docs/Reference/Standard_Library.md#35-character-encoding)
- Related: [`ASCII`](ASCII.md), [`UNICODE`](UNICODE.md)
