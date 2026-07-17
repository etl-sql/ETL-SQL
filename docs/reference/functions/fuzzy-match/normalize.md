# NORMALIZE

Preprocesses a string to eliminate surface variation before similarity scoring.

## Syntax

```sql
NORMALIZE(string)
NORMALIZE(string, mode)
```

## Parameters

- **string** - String to normalize.
- **mode** - Optional domain-specific normalization profile. See [accepted values](#accepted-values-for-mode).

## Returns

Returns the normalized string.

## Null Behavior

Returns `NULL` when `string` is `NULL`.

## Accepted Values for `mode`

- **omitted** - Lowercase, trim, collapse whitespace, normalize Unicode, and strip control characters.
- **`'COMPANY'`** - Remove legal suffixes, expand `&` to `and`, and strip articles.
- **`'PERSON'`** - Remove titles and generational suffixes.
- **`'ADDRESS'`** - Expand directional and street-type abbreviations and remove unit designators.
- **`'PHONE'`** - Strip non-digit characters and remove a leading `1` from 11-digit values.
- **`'EMAIL'`** - Lowercase and trim only.

## Examples

```sql
SELECT NORMALIZE(company_name, 'COMPANY') AS normalized_company
FROM #leads;
```

```sql
SELECT SIMILARITY(
  NORMALIZE(a.company_name, 'COMPANY'),
  NORMALIZE(b.company_name, 'COMPANY')
) AS score
FROM #source AS a
CROSS JOIN #reference AS b
WHERE SIMILARITY(
  NORMALIZE(a.company_name, 'COMPANY'),
  NORMALIZE(b.company_name, 'COMPANY')
) > 0.80;
```

## References

- [Functions](../README.md)
- [SIMILARITY](similarity.md)
- [LEVENSHTEIN](levenshtein.md)
