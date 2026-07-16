# ASCII
Returns the ASCII / Unicode code point of the first character of a string.

**Category:** String

## Syntax
```sql
ASCII(string)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The input string; only the first character is evaluated |

## Returns
`INT` — Numeric code point of the first character. Returns `NULL` if the string is empty or `NULL`.

## Example
```sql
SELECT ASCII('A');       -- → 65
SELECT ASCII('a');       -- → 97
SELECT ASCII('Hello');   -- → 72  (only 'H' is evaluated)
```

## Remarks
- For Unicode strings, returns the Unicode code point (same as [`UNICODE`](UNICODE.md)).
- To get the character for a code point, use [`CHAR`](CHAR.md).

## See Also
- [Standard Library — §3.5 Character Encoding](../../../../../Docs/Reference/Standard_Library.md#35-character-encoding)
- Related: [`UNICODE`](UNICODE.md), [`CHAR`](CHAR.md)
