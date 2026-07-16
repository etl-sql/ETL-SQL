# REMOVE_HTML_CHARACTERS
Decodes HTML entities and normalizes typographic ("smart") Unicode to plain ASCII, fixing invisible mismatches that break string comparisons.

**Category:** String

## Syntax
```sql
REMOVE_HTML_CHARACTERS(string)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The source string to normalize |

## Returns
`STRING` — The normalized string. Returns `NULL` if `string` is `NULL`.

## Remarks
Processing happens in two passes:

1. **HTML entity decoding** — named and numeric entities are decoded to their characters, e.g. `&nbsp;`, `&mdash;`, `&hellip;`, `&amp;`, `&#8217;`, `&#x2014;`.
2. **Typographic Unicode normalization** — the decoded text (and any Unicode already present) is folded to plain ASCII:

| Source | Becomes |
| :--- | :--- |
| Curly/angle double quotes (left, right, low-9, guillemets) | `"` |
| Curly/angle single quotes, apostrophes, acute accent | `'` |
| En dash, em dash, horizontal bar, minus sign | `-` |
| Horizontal ellipsis | `...` |
| Bullet, middle dot | `*` |
| No-break space (`&nbsp;` / `CHAR(160)`) | space |
| Zero-width space/joiner, word joiner, BOM, soft hyphen | *(removed)* |

- The single most common offender is the right curly apostrophe (`CHAR(8217)`) used in possessives — it looks like `'` but is not equal to it, silently breaking joins and `WHERE` matches. This function maps it to a straight `'`.
- Characters that are already plain ASCII after decoding (e.g. `&amp;` -> `&`, `&quot;` -> `"`, `&lt;` -> `<`) are left as-is.
- For control characters such as tabs and newlines, use [`REMOVE_HIDDEN_CHARACTERS`](remove_hidden_characters.md).

## Example
```sql
-- Normalize curly quotes/dashes copied from a word processor before comparing
SELECT * FROM #t WHERE REMOVE_HTML_CHARACTERS(title) = 'Q1 Sales - Final';

-- Decode HTML entities scraped from a web source
SELECT REMOVE_HTML_CHARACTERS('AT&amp;T it&#8217;s &mdash; done&hellip;');
-- -> 'AT&T it's - done...'

-- Clean a column for a reliable de-duplication key
SELECT DISTINCT REMOVE_HTML_CHARACTERS(company_name) AS company FROM #leads;
```

## See Also
- [Standard Library — §3.6 Translation & Escaping](../../../guides/getting-started.md#36-translation--escaping)
- Related: [`REMOVE_HIDDEN_CHARACTERS`](remove_hidden_characters.md), [`REPLACE`](../string/replace.md), [`TRANSLATE`](../string/translate.md)
