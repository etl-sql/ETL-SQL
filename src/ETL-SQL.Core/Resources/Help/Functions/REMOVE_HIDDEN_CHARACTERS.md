# REMOVE_HIDDEN_CHARACTERS
Cleans invisible and whitespace-class characters out of a string. A specialized form of `REPLACE`.

**Category:** String

## Syntax
```sql
REMOVE_HIDDEN_CHARACTERS(string [, char1, char2, ...])
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The source string to clean |
| `char1, char2, ...` | `STRING` | *(optional)* One or more literal strings to target. When supplied, **only** these are replaced (with a space) and the default set is ignored |

## Returns
`STRING` — The cleaned string. Returns `NULL` if `string` is `NULL`.

## Remarks
- **Default behavior** (no extra arguments):
  - **Whitespace-class characters are replaced with a single standard space** — horizontal tab `CHAR(9)`, line feed `CHAR(10)`, vertical tab `CHAR(11)`, form feed `CHAR(12)`, carriage return `CHAR(13)`, next line `CHAR(133)`, no-break space `CHAR(160)` (`&nbsp;`), and the full Unicode space family (en/em/thin/hair spaces, narrow & medium-mathematical space, line/paragraph separators, ogham space, ideographic space).
  - **Zero-width / invisible formatting characters are removed entirely** — soft hyphen `CHAR(173)`, zero-width space `CHAR(8203)`, zero-width non-joiner `CHAR(8204)`, zero-width joiner `CHAR(8205)`, word joiner `CHAR(8288)`, byte-order mark `CHAR(65279)`.
- **Targeted mode**: pass specific characters (e.g. `CHAR(13)`, `CHAR(10)`) to replace only those with a space and leave everything else untouched.
- Replacement is 1:1 — adjacent hidden characters become adjacent spaces; the function does not collapse runs. Wrap with `TRIM` and/or `REGEXP_REPLACE(..., ' +', ' ')` if you need that.
- For typographic/"smart" Unicode and HTML entities (curly quotes, em dashes, `&nbsp;` text), use [`REMOVE_HTML_CHARACTERS`](REMOVE_HTML_CHARACTERS.md) instead.

## Example
```sql
-- Flatten a multi-line CSV-pasted value: tabs and CRLF become single spaces
SELECT REMOVE_HIDDEN_CHARACTERS(notes) AS clean_notes FROM #imported;

-- Strip a zero-width space that breaks an equality test
SELECT * FROM #t WHERE REMOVE_HIDDEN_CHARACTERS(sku) = 'ABC-123';

-- Targeted: only remove carriage returns and line feeds, keep tabs intact
SELECT REMOVE_HIDDEN_CHARACTERS(payload, CHAR(13), CHAR(10)) AS one_line FROM #raw;
```

## See Also
- [Standard Library — §3.6 Translation & Escaping](../../../../../Docs/Reference/Standard_Library.md#36-translation--escaping)
- Related: [`REMOVE_HTML_CHARACTERS`](REMOVE_HTML_CHARACTERS.md), [`REPLACE`](REPLACE.md), [`TRANSLATE`](TRANSLATE.md), [`TRIM`](../../../../../Docs/Reference/Standard_Library.md#36-translation--escaping)
