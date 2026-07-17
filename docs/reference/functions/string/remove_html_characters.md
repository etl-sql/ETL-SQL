# REMOVE_HTML_CHARACTERS

Decodes HTML entities and normalizes typographic ("smart") Unicode to plain ASCII, fixing invisible mismatches that break string comparisons.

## Syntax

```sql
REMOVE_HTML_CHARACTERS(string)
```

## Parameters

- **string** - Source string to normalize.

## Returns

Returns the normalized string.

## Null Behavior

Returns `NULL` when `string` is `NULL`.

## Remarks

Processing happens in two passes:

1. HTML entity decoding: named and numeric entities such as `&nbsp;`, `&mdash;`, `&hellip;`, `&amp;`, `&#8217;`, and `&#x2014;` are decoded.
2. Typographic Unicode normalization: curly quotes, dashes, ellipses, bullets, no-break spaces, and invisible formatting characters are folded to plain ASCII or removed.

- The single most common offender is the right curly apostrophe (`CHAR(8217)`) used in possessives. It looks like `'` but is not equal to it, silently breaking joins and `WHERE` matches. This function maps it to a straight `'`.
- Characters that are already plain ASCII after decoding, such as `&amp;` to `&`, `&quot;` to `"`, and `&lt;` to `<`, are left as-is.
- For control characters such as tabs and newlines, use [`REMOVE_HIDDEN_CHARACTERS`](remove_hidden_characters.md).

## Examples

```sql
SELECT *
FROM #documents
WHERE REMOVE_HTML_CHARACTERS(title) = 'Q1 Sales - Final';
```

```sql
SELECT REMOVE_HTML_CHARACTERS('AT&amp;T it&#8217;s &mdash; done&hellip;') AS clean_text;
```

```sql
SELECT DISTINCT REMOVE_HTML_CHARACTERS(company_name) AS company
FROM #leads;
```

## References

- [Functions](../README.md)
- [REMOVE_HIDDEN_CHARACTERS](remove_hidden_characters.md)
- [REPLACE](../string/replace.md)
- [TRANSLATE](../string/translate.md)
