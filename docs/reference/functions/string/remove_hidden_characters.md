# REMOVE_HIDDEN_CHARACTERS

Cleans invisible and whitespace-class characters out of a string. A specialized form of `REPLACE`.

## Syntax

```sql
REMOVE_HIDDEN_CHARACTERS(string)
REMOVE_HIDDEN_CHARACTERS(string, char1, char2, ...)
```

## Parameters

- **string** - Source string to clean.
- **char1, char2, ...** - Optional literal strings to target. When supplied, only these values are replaced with a space and the default set is ignored.

## Returns

Returns the cleaned string.

## Null Behavior

Returns `NULL` when `string` is `NULL`.

## Remarks

- Default mode replaces whitespace-class characters with standard spaces, including tabs, line breaks, carriage returns, no-break spaces, and other Unicode spacing characters.
- Default mode removes zero-width and invisible formatting characters, including soft hyphen, zero-width space, zero-width joiner, word joiner, and byte-order mark.
- Targeted mode replaces only the supplied characters, such as `CHAR(13)` or `CHAR(10)`, with spaces.
- Replacement is one-for-one. Adjacent hidden characters become adjacent spaces; the function does not collapse runs. Wrap with `TRIM` and/or `REGEXP_REPLACE(..., ' +', ' ')` if you need that.
- For typographic/"smart" Unicode and HTML entities (curly quotes, em dashes, `&nbsp;` text), use [`REMOVE_HTML_CHARACTERS`](remove_html_characters.md) instead.

## Examples

```sql
SELECT REMOVE_HIDDEN_CHARACTERS(notes) AS clean_notes
FROM #imported;
```

```sql
SELECT *
FROM #products
WHERE REMOVE_HIDDEN_CHARACTERS(sku) = 'ABC-123';
```

```sql
SELECT REMOVE_HIDDEN_CHARACTERS(payload, CHAR(13), CHAR(10)) AS one_line
FROM #raw;
```

## References

- [Functions](../README.md)
- [REMOVE_HTML_CHARACTERS](remove_html_characters.md)
- [REPLACE](../string/replace.md)
- [TRANSLATE](../string/translate.md)
- [TRIM](../string/trim.md)
